using System;
using System.Collections.Generic;

namespace WaterSim
{
    /// <summary>
    /// The server's side of liquid updates, as vanilla 1.22.7 runs them:
    /// - one pending update per cell (RegisterCallbackUnique: a later registration replaces the earlier one, so its time
    ///   and its handler, the block behaviour that registered it, are the last registrant's);
    /// - due updates run once per server tick (33 ms), in the order of the callback dictionary;
    /// - a 100 ms pulse first runs queued neighbour updates (TriggerNeighbourBlockUpdate: the cell, its 6 neighbours and
    ///   the 4 cells beside the one above) and then wakes every cell a bulk commit changed (the fluid at that cell only);
    /// - writes through the bulk accessor are staged and committed after the update; direct writes (world.BlockAccessor)
    ///   apply at once and wake nothing.
    /// </summary>
    public sealed partial class Sim
    {
        public readonly World W;
        public readonly RuleSet R;
        public Random Rand;
        public long Now;
        public const int TickMs = 33, PulseMs = 100;
        long nextPulse = PulseMs;

        struct Callback { public long Due; public int Kind; }
        readonly Dictionary<int, Callback> pending = new();
        readonly Queue<int> updatedBlocks = new();
        readonly Queue<int> modifiedBlocks = new();
        readonly Dictionary<int, int> staged = new();
        readonly List<int> stagedOrder = new();
        bool readStaged;

        public long Updates, Writes, RuleWrites;
        /// <summary>Updates per cell since the last <see cref="ResetCounters"/> (loop detection).</summary>
        public readonly Dictionary<int, int> UpdatesAt = new();
        public readonly RuleStats Stats = new();
        /// <summary>Optional production H2 adapter for the offline integration tests; other rules keep the port.</summary>
        public Func<int, int, int, int, bool> FillOverride;

        public Sim(World w, RuleSet r, int seed)
        {
            W = w; R = r; Rand = new Random(seed);
        }

        public bool Idle => pending.Count == 0 && updatedBlocks.Count == 0 && modifiedBlocks.Count == 0 && featureDue < 0;
        public int Pending => pending.Count;

        public void ResetCounters() { Updates = 0; Writes = 0; RuleWrites = 0; UpdatesAt.Clear(); }

        // ---------------------------------------------------------------- reads and writes

        public bool Solid(int x, int y, int z) => W.IsSolid(x, y, z);
        public int Fl(int x, int y, int z) => W.Get(x, y, z);

        /// <summary>The bulk accessor's read: staged values once UpdateNeighbouringLiquids has turned that on.</summary>
        int FlBulk(int x, int y, int z)
        {
            if (readStaged && W.In(x, y, z) && staged.TryGetValue(W.Idx(x, y, z), out int v)) return v;
            return W.Get(x, y, z);
        }

        /// <summary>world.BlockAccessor.SetBlock on the fluid layer: at once, no wake.</summary>
        public void SetDirect(int x, int y, int z, int fluid)
        {
            if (!W.In(x, y, z) || W.IsSolid(x, y, z)) return;
            int i = W.Idx(x, y, z);
            if (W.Fluid[i] != fluid) Writes++;
            if (F.Level(W.Fluid[i]) != F.Level(fluid) || F.Kind(W.Fluid[i]) != F.Kind(fluid)) W.Sub[i] = 0;
            W.Fluid[i] = fluid;
        }

        /// <summary>world.BulkBlockAccessor.SetBlock on the fluid layer: staged until the update's commit.</summary>
        void SetBulk(int x, int y, int z, int fluid, int sub = 0)
        {
            if (!W.In(x, y, z)) return;
            int i = W.Idx(x, y, z);
            if (!staged.ContainsKey(i)) stagedOrder.Add(i);
            staged[i] = fluid;
            stagedSub[i] = (byte)sub;
        }

        readonly Dictionary<int, byte> stagedSub = new();

        void Commit()
        {
            readStaged = false;
            foreach (int i in stagedOrder)
            {
                int v = staged[i];
                if (W.Solid[i] != 0) continue;
                if (W.Fluid[i] != v) Writes++;
                W.Fluid[i] = v;
                W.Sub[i] = stagedSub.TryGetValue(i, out byte sb) ? sb : (byte)0;
                modifiedBlocks.Enqueue(i);
            }
            staged.Clear();
            stagedSub.Clear();
            stagedOrder.Clear();
        }

        // ---------------------------------------------------------------- scheduling

        /// <summary>RegisterCallbackUnique(handler of a block of <paramref name="kind"/>, pos, delay).</summary>
        void Register(int x, int y, int z, int kind, int delay)
        {
            if (!W.In(x, y, z)) return;
            pending[W.Idx(x, y, z)] = new Callback { Due = Now + delay, Kind = kind };
        }

        /// <summary>The fluid block at the cell gets OnNeighbourBlockChange: it schedules its own update.</summary>
        public void Wake(int x, int y, int z)
        {
            int c = Fl(x, y, z);
            if (c == 0) return;
            Register(x, y, z, F.Kind(c), F.Delay(F.Kind(c)));
        }

        /// <summary>IBlockAccessor.TriggerNeighbourBlockUpdate: queued for the next 100 ms pulse.</summary>
        public void QueueNeighbourUpdate(int x, int y, int z)
        {
            if (W.In(x, y, z)) updatedBlocks.Enqueue(W.Idx(x, y, z));
        }

        /// <summary>ServerMain.TriggerNeighbourBlocksUpdate.</summary>
        void TriggerNeighbours(int x, int y, int z)
        {
            Wake(x, y, z);
            for (int f = 0; f < 6; f++)
            {
                int nx = x + Dir.AllX[f], ny = y + Dir.AllY[f], nz = z + Dir.AllZ[f];
                if (!W.In(nx, ny, nz)) continue;
                Wake(nx, ny, nz);
                // the default-layer block is the fluid when there is no solid block: then the ring above is skipped
                bool fluidIsDefault = !Solid(nx, ny, nz) && Fl(nx, ny, nz) != 0;
                if (fluidIsDefault) continue;
                if (f == Dir.Up && !Solid(nx, ny, nz))
                    for (int h = 0; h < 4; h++) Wake(nx + Dir.HX[h], ny, nz + Dir.HZ[h]);
            }
        }

        void Pulse()
        {
            int n = 0;
            while (updatedBlocks.Count > 0 && n++ < 500)
            {
                W.Pos(updatedBlocks.Dequeue(), out int x, out int y, out int z);
                TriggerNeighbours(x, y, z);
            }
            while (modifiedBlocks.Count > 0)
            {
                W.Pos(modifiedBlocks.Dequeue(), out int x, out int y, out int z);
                Wake(x, y, z);
            }
        }

        readonly List<int> keys = new();

        /// <summary>Called at the end of every tick that had a 100 ms pulse (the playback records a frame there).</summary>
        public Action AfterPulse;

        void Tick()
        {
            Now += TickMs;
            bool pulsed = false;
            if (Now >= nextPulse)
            {
                Pulse();
                pulsed = true;
                while (nextPulse <= Now) nextPulse += PulseMs;
            }
            RunDue();
            RunFeaturesIfDue();
            if (pulsed) AfterPulse?.Invoke();
        }

        void RunDue()
        {
            if (pending.Count == 0) return;
            keys.Clear();
            keys.AddRange(pending.Keys);
            foreach (int k in keys)
            {
                if (!pending.TryGetValue(k, out var cb) || Now - cb.Due < 0) continue;
                pending.Remove(k);
                W.Pos(k, out int x, out int y, out int z);
                RunUpdate(x, y, z, cb.Kind);
            }
        }

        /// <summary>Runs ticks until nothing is pending, or <paramref name="maxMs"/> of game time passed. True if it settled.</summary>
        public bool Settle(long maxMs = 120_000, long maxUpdates = 400_000)
        {
            long end = Now + maxMs, u0 = Updates;
            while (!Idle)
            {
                if (Now >= end || Updates - u0 > maxUpdates) return false;
                if (updatedBlocks.Count == 0 && modifiedBlocks.Count == 0)
                {
                    // skip empty ticks: jump to the tick in which the next update is due
                    long due = featureDue >= 0 ? featureDue : long.MaxValue;
                    foreach (var cb in pending.Values) if (cb.Due < due) due = cb.Due;
                    if (due > Now + TickMs)
                    {
                        long ticks = (due - Now) / TickMs - 1;
                        if (ticks > 0) { Now += ticks * TickMs; while (nextPulse <= Now) nextPulse += PulseMs; }
                    }
                }
                Tick();
            }
            return true;
        }

        /// <summary>Runs for <paramref name="ms"/> of game time.</summary>
        public void Run(long ms)
        {
            long end = Now + ms;
            while (Now + TickMs <= end) Tick();
        }

        // ---------------------------------------------------------------- the player

        /// <summary>A player places a stone block: it displaces the fluid there, then the server triggers neighbour updates.</summary>
        public void PlaceStone(int x, int y, int z)
        {
            if (!W.In(x, y, z) || W.IsSolid(x, y, z)) return;
            W.SetSolid(x, y, z, true);
            TriggerNeighbours(x, y, z);
            ScheduleFeatures(500);
        }

        /// <summary>
        /// A player empties a bucket into an open cell: with H6 (noLiquidSourceTransport, Hydrology's default) the bucket spills
        /// water-still-3, else a source. The liquid's own update is registered and the server triggers neighbour updates.
        /// </summary>
        public void Pour(int x, int y, int z)
        {
            if (!W.In(x, y, z) || W.IsSolid(x, y, z) || W.Get(x, y, z) != 0) return;
            SetDirect(x, y, z, F.Make(F.Water, F.Still, R.BucketPlacesSource ? 7 : 3));
            Wake(x, y, z);
            TriggerNeighbours(x, y, z);
        }

        /// <summary>A player breaks a block.</summary>
        public void BreakBlock(int x, int y, int z)
        {
            if (!W.In(x, y, z) || !W.IsSolid(x, y, z)) return;
            W.SetSolid(x, y, z, false);
            TriggerNeighbours(x, y, z);
            ScheduleFeatures(500);
        }

        /// <summary>Wakes every fluid cell once, as if each had a neighbour change (the "settle everything" reference).</summary>
        public void WakeAll()
        {
            for (int i = 0; i < W.Fluid.Length; i++)
                if (W.Fluid[i] != 0) { W.Pos(i, out int x, out int y, out int z); Wake(x, y, z); }
        }

        // ---------------------------------------------------------------- one update

        /// <summary>The context of one OnDelayedWaterUpdateCheck: the handler's block kind and its multiplySpread.</summary>
        public struct U
        {
            public int Kind;
            public bool Multiply;
            public int Delay => F.Delay(Kind);
        }

        void RunUpdate(int x, int y, int z, int kind)
        {
            Updates++;
            int i = W.Idx(x, y, z);
            UpdatesAt[i] = UpdatesAt.TryGetValue(i, out int k) ? k + 1 : 1;
            var u = new U { Kind = kind, Multiply = F.MultiplySpread(kind) };
            int before = Fl(x, y, z);
            BeforeUpdate(x, y, z, ref u);
            DelayedUpdate(x, y, z, u);
            AfterUpdate(x, y, z, before, u.Multiply);
        }
    }

    public static class Dir
    {
        // BlockFacing.HORIZONTALS: N, E, S, W (north is -z)
        public static readonly int[] HX = { 0, 1, 0, -1 };
        public static readonly int[] HZ = { -1, 0, 1, 0 };
        // BlockFacing.ALLFACES: N, E, S, W, U, D
        public static readonly int[] AllX = { 0, 1, 0, -1, 0, 0 };
        public static readonly int[] AllY = { 0, 0, 0, 0, 1, -1 };
        public static readonly int[] AllZ = { -1, 0, 1, 0, 0, 0 };
        public const int Up = 4, Down = 5;
        public static int Opposite(int h) => (h + 2) & 3;
    }
}
