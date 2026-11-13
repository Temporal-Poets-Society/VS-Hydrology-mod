using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hydrology.Rules
{
    /// <summary>
    /// C1 (client, 0.1.11): rapid-water sources throw less foam. Vanilla's flowing water blocks (every flow label but "still"
    /// and falling) spawn foam particles on the client within 37 blocks of the player, fading in over 7 s of ticking; rapid
    /// water's are white spray, about 24 alive per block (30 spawn ticks a second, a 0.4 chance, 6 particles, 0.33 s each).
    /// Earth's pool layout 9 labels every source of a rapids reach with the river's flow, so a 70-wide river is some 4 000
    /// emitters around the player: 100 000 particles and more against the async pool's 80 000 (40 000 in RAM-saving mode).
    /// The pool ran full and the foam came and went in waves while the player stood by the river, and was gone when they left
    /// (the play-test at the Congo, 2026-10-26: "it turns into rapids and the animation kind of pulses"). At the play-test's
    /// spot the scan held 4 405 rapid sources and 1 446 flowing rapid cells (ramps): about 140 000 particles wanted. A rapid
    /// source now spawns its foam on <see cref="SourceShare"/> of its ticks and flowing rapid water on
    /// <see cref="FlowingShare"/> (38 000 there, the steps foamier than the pools); falling water is unchanged. Only the look:
    /// no block changes, nothing sent. Applies on the client (Hydrology is required there since 0.6.1; a client turns it
    /// off on a server without Hydrology).
    /// </summary>
    public static class C1RapidFoam
    {
        /// <summary>The share of a rapid source's particle ticks that spawn foam (1: vanilla).</summary>
        public static double SourceShare = 0.2;
        /// <summary>The same for flowing rapid water (levels 1-6: ramps, rivulets).</summary>
        public static double FlowingShare = 0.5;
        /// <summary>0.6.2 (decision Earth 0046): the same for an ordinary source labelled with a flow. Earth's pool layout 11
        /// labels every calm river's top sources so, and each throws vanilla's small white speck on every tick: thousands of
        /// emitters again around a wide river, which read as bubbles on slow water (the author's playtest, 2026-11-03). None
        /// by default: the surface looks like a lake's, its texture still shows the current, and flowing water (ramps,
        /// edges, falls) keeps vanilla's specks. The client cannot tell Earth's rivers from other labelled sources, so it
        /// also applies to the few vanilla labels with a direction (a pond's sources at its outflow, a spring on a
        /// slope), in any world.</summary>
        public static double CalmSourceShare = 0;

        const string HarmonyId = "hydrology.client";
        static Harmony harmony;

        const string Feature = "C1 (less foam on rapid water)";
        static readonly GamePatches.Breaker Breaker = new(Feature);

        public static void Apply(ILogger log)
        {
            // rc.4 (audit): a previous world whose Dispose never ran left its patch and breaker: start afresh
            if (harmony != null) Remove();
            if (SourceShare >= 1 && FlowingShare >= 1 && CalmSourceShare >= 1) return;
            harmony = new Harmony(HarmonyId);
            Breaker.Reset(log);
            // declared on BlockWaterflowing with this exact signature: if the class stopped overriding it, a lookup through
            // the base class would patch every block's particle tick
            if (!GamePatches.Apply(harmony, log, Feature,
                    GamePatches.On(typeof(BlockWaterflowing), nameof(BlockWaterflowing.OnAsyncClientParticleTick),
                        new[] { typeof(IAsyncParticleManager), typeof(BlockPos), typeof(float), typeof(float) }, typeof(void), typeof(C1RapidFoam), prefix: nameof(Prefix))))
                return;
            log.Notification("Hydrology: C1 rapid water throws foam on {0:0%} of a source's particle ticks and {1:0%} of flowing water's; calm labelled sources throw specks on {2:0%}",
                SourceShare, FlowingShare, CalmSourceShare);
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
        }

        /// <summary>Runs on the client's particle thread: no state but a thread-safe random (and the feature's breaker).</summary>
        static bool Prefix(BlockWaterflowing __instance)
        {
            if (Breaker.Off) return true;
            try { return Spawns(__instance); }
            catch (Exception e) { Breaker.Fault(e); return true; }
        }

        static bool Spawns(BlockWaterflowing __instance) =>
            __instance.Code.Path.StartsWith("rapidwater-", StringComparison.Ordinal)
                ? Random.Shared.NextDouble() < (__instance.LiquidLevel == Liquids.SourceLevel ? SourceShare : FlowingShare)
                : __instance.LiquidLevel != Liquids.SourceLevel || !__instance.Code.Path.StartsWith("water-", StringComparison.Ordinal)
                    || Random.Shared.NextDouble() < CalmSourceShare;
    }
}
