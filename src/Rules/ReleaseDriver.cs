using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// What H2's release (pond drains, dams, river pools) and H2's timed retries do on a player's break, a player's placement
    /// and the server's half-second tick, with the gate they run behind (Fable's pass, R2: this was policy in
    /// <c>HydrologySystem.StartServerSide</c>'s lambdas; here the offline suite drives the same code the game runs).
    /// HY-0012: drains, dams and pools run only while the water rules they rely on are applied and their breaker is on;
    /// when the rules go off, running drains and pools end once, logged. Errors go to a breaker, not the log at every call.
    /// </summary>
    public sealed class ReleaseDriver
    {
        /// <summary>What the driver needs of a breaker (<c>GamePatches.Breaker</c> in the game): off after too many errors.</summary>
        public interface IFaults { bool Off { get; } void Fault(Exception e); }

        readonly IFaults release, retries;
        readonly Func<bool> fillOn, waterRulesOn;
        readonly Action<string> warn, error;
        /// <summary>Run on the tick after Earth's river query was unbound at run time (the game re-reads the vessels answer
        /// and tells the clients).</summary>
        public Action<IWorldAccessor> Unbound;

        /// <param name="fillOn">H2's fill rule is on in the config.</param>
        /// <param name="waterRulesOn">The water rules' hooks are applied (H5) and their breaker is on.</param>
        public ReleaseDriver(IFaults release, IFaults retries, Func<bool> fillOn, Func<bool> waterRulesOn, Action<string> warn, Action<string> error)
        {
            this.release = release;
            this.retries = retries;
            this.fillOn = fillOn;
            this.waterRulesOn = waterRulesOn;
            this.warn = warn;
            this.error = error;
            H2Release.Fault = release.Fault;
        }

        /// <summary>Drains, dams and river pools may run now.</summary>
        public bool On => H2Release.Enabled && fillOn() && !release.Off && waterRulesOn();

        /// <summary>A player broke a block: a held answer for a space opened there no longer holds (HY-0014), and a pond
        /// opened there lets its water out (decision 0007).</summary>
        public void OnBreak(IWorldAccessor world, BlockPos pos)
        {
            if (pos == null) return;
            if (fillOn()) H2Filling.ForgetAround(pos.dimension, pos.X, pos.Y, pos.Z);
            // a break joins water and never splits a body: a remembered large body stays large (forgetting it here walked the
            // sea again at every shore break: the reviewer's re-review, high)
            if (!On) return;
            try { H2Release.Opened(world, pos); }
            catch (Exception e) { release.Fault(e); }
        }

        /// <summary>A player placed a block: a held answer there no longer holds, and the block may close a fed reach (a
        /// dam, decision 0008).</summary>
        public void OnPlace(IWorldAccessor world, BlockPos pos)
        {
            if (pos == null) return;
            if (fillOn()) H2Filling.ForgetAround(pos.dimension, pos.X, pos.Y, pos.Z);
            // only a placed block can wall a part of a remembered large body off (a bay closed, then opened again)
            H2Release.ForgetLargeBodyNear(pos.dimension, pos.X, pos.Y, pos.Z);
            if (!On) return;
            try { H2Release.Placed(world, pos); }
            catch (Exception e) { release.Fault(e); }
        }

        /// <summary>The server's tick (every 500 ms): drains and pools step, H2 retries what waited.</summary>
        public void OnTick(IWorldAccessor world, float dt)
        {
            // Earth's query was unbound at run time (its breaker): H2's vessels answer is read again without it (Fable
            // re-review)
            if (EarthRiverQuery.TakeUnbound())
            {
                H2Filling.ResetWorld();
                Unbound?.Invoke(world);
            }
            if (!waterRulesOn() || release.Off)
            {
                // the rules a drain keeps its volume with are gone: drains and filling pools end once, logged; the waiting
                // ones stay saved (Fable's pass, F7)
                if (H2Release.Running > 0 || H2DamPools.Running > 0)
                {
                    int drains = 0, pools = H2DamPools.Running;
                    try { drains = H2Release.EndAll(world); H2DamPools.EndPools(); }
                    catch (Exception e) { error?.Invoke($"Hydrology: ending the drains failed: {e}"); }
                    warn?.Invoke($"Hydrology: the water rules are off: {drains} pond drains and {pools} river pools stop where they stand");
                }
                return;
            }
            if (On)
            {
                try { H2Release.Tick(world, dt); H2DamPools.Tick(world, dt); }
                catch (Exception e) { release.Fault(e); }
            }
            if (!retries.Off)
                try { H2Filling.Retry(world); }
                catch (Exception e) { retries.Fault(e); }
        }
    }
}
