using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// H2 release, part 3 (HY-0018 split): dams. A block a player places wakes the reach it may close, and beside one of
    /// Earth's rivers marks the columns a dam can back up (decision 0009) and starts H2DamPools (decision 0010). The rule
    /// itself: H2Release.cs.
    /// </summary>
    public static partial class H2Release
    {
        /// <summary>River columns a player's block has touched (rc.11, EA-0066, decision 0009): (dimension, x, z). Only there
        /// may water stand above Earth's generated river surface, and only where it is held. Kept for the session (bounded):
        /// a placed block marks its reach again after a restart; water raised before stays where it stands.</summary>
        static readonly HashSet<(int, int, int)> dammed = new();

        const int MaxDammed = 1 << 20;

        /// <summary>A player's block has touched this river column's water (a dam may hold water over its surface).</summary>
        public static bool Dammed(BlockPos pos) => dammed.Count > 0 && dammed.Contains((pos.dimension, pos.X, pos.Z));
        static bool markCapLogged;

        static readonly HashSet<(int x, int y, int z)> placeBody = new(), placeWoken = new();

        static readonly Queue<(int x, int y, int z)> placeQueue = new();

        static readonly List<BlockPos> placeWake = new();

        /// <summary>Sources a placed block's reach may have for <see cref="Placed"/> to wake its water.</summary>
        const int MaxReach = 4096;

        /// <summary>
        /// Dams (rc.9, HY-0009, decision 0008): a player placed a block at <paramref name="pos"/>. Wakes the flowing water over
        /// and beside the reaches the block may close (the sources beside it and beside the cells below it, a dam being built
        /// up from the bed), so H2 asks about them again: a reach a dam closes fills to the dam's top as a closed space fed
        /// through the fall above it (decision 0006, followed upstream), and one more layer that spills over the crest when a
        /// lake or a river feeds it (H2's fed channel: the crest is its outlet), so the water falls past the dam and H4 makes
        /// rapids there for a wheel. Banks lower than the crest are the outlet instead (the water spills there), a terrace
        /// all edge holds nothing. Removing the dam is a pond's opening: the raised water drains (<see cref="Opened"/>) and H2
        /// keeps what the remaining crest still holds. Beside one of Earth's natural rivers the block also marks the columns
        /// a dam there can back up (decision 0009) and starts the river filling the pool from its own flow
        /// (<see cref="H2DamPools"/>, decision 0010). Returns the cells woken.
        /// </summary>
        public static int Placed(IWorldAccessor world, BlockPos pos)
        {
            if (!Enabled || !H2Filling.Enabled || pos.Y < 1) return 0;
            var ba = world.BlockAccessor;
            // HY-0010: scratch reused (every block a player places comes here, nearly all of them away from water)
            var body = placeBody;
            var queue = placeQueue;
            var woken = placeWoken;
            var wake = placeWake;
            body.Clear(); queue.Clear(); woken.Clear(); wake.Clear();
            var n = placeN ??= new BlockPos(0);
            n.dimension = pos.dimension;
            for (int d = 0; d <= 4 && pos.Y - d >= 1; d++)
                for (int i = 0; i < 4; i++)
                {
                    n.Set(pos.X, pos.Y - d, pos.Z).Add(H[i]);
                    var f = Liquids.Fluid(ba, n);
                    if (Liquids.Handles(f) && Liquids.IsSource(f) && body.Add((n.X, n.Y, n.Z))) queue.Enqueue((n.X, n.Y, n.Z));
                }
            if (queue.Count == 0) return 0;
            var a = placeA ??= new BlockPos(0);
            a.dimension = pos.dimension;
            if (EarthRiverQuery.Active && pos.dimension == 0)
            {
                // rc.13 (HY-0011): the river fills the pool from its own flow
                int top = MarkDammed(ba, pos, a);
                if (top != int.MinValue) H2DamPools.Dammed(world, pos, top, markCols, markRiver, markUnloaded);
            }
            while (queue.Count > 0)
            {
                var (x, y, z) = queue.Dequeue();
                a.Set(x, y, z);
                var src = Liquids.Fluid(ba, a);
                foreach (var face in BlockFacing.ALLFACES)
                {
                    if (face == BlockFacing.DOWN) continue;
                    n.Set(a).Add(face);
                    if (body.Contains((n.X, n.Y, n.Z)) || !Liquids.Passes(ba, a, n, face)) continue;
                    var f = Liquids.Fluid(ba, n);
                    if (!Liquids.Same(f, src)) continue;
                    if (Liquids.IsSource(f))
                    {
                        if (body.Count >= MaxReach) continue;
                        body.Add((n.X, n.Y, n.Z));
                        queue.Enqueue((n.X, n.Y, n.Z));
                        continue;
                    }
                    // flowing water over or beside the reach: asked again by its liquid's next update
                    if (woken.Add((n.X, n.Y, n.Z))) wake.Add(n.Copy());
                }
            }
            if (wake.Count == 0) return 0;
            // rc.10: what H2 remembers about the reach no longer holds. A dam is built a block at a time: the first block wakes
            // the reach while the dam is still open, H2 refuses it and remembers the refusal for 2 s, and the last block's wake
            // got that answer, after which nothing asked again (in game, a 2-high dam placed by hand raised nothing).
            int x0 = pos.X, y0 = pos.Y, z0 = pos.Z, x1 = x0, y1 = y0, z1 = z0;
            foreach (var w in wake)
            {
                if (w.X < x0) x0 = w.X; if (w.X > x1) x1 = w.X;
                if (w.Y < y0) y0 = w.Y; if (w.Y > y1) y1 = w.Y;
                if (w.Z < z0) z0 = w.Z; if (w.Z > z1) z1 = w.Z;
            }
            H2Filling.ForgetHeldNear(pos.dimension, x0 - 1, y0 - 1, z0 - 1, x1 + 1, y1 + 1, z1 + 1);
            int count = wake.Count;
            // copied out first, so nothing a liquid update starts can change the list under the loop
            var cells = wake.ToArray();
            wake.Clear();
            foreach (var w in cells) Liquids.Fluid(ba, w).OnNeighbourBlockChange(world, w, w.Copy());
            HydrologyStats.DamsWoken++;
            return count;
        }

        /// <summary>River columns <see cref="MarkDammed"/> walks from one placed block.</summary>
        const int MaxMarkWalk = 16384;

        static readonly HashSet<(int x, int z)> markSeen = new();

        /// <summary>The river columns the last <see cref="MarkDammed"/> walked, and a river source beside the block.</summary>
        static readonly List<(int x, int z)> markCols = new();

        static readonly BlockPos markRiver = new(0);

        static readonly Queue<(int x, int z)> markQueue = new();

        /// <summary>
        /// rc.11 (EA-0066, decision 0009): a block placed beside a natural river's water marks the river columns a dam there
        /// can back up: the surface of the river water beside the block (its pool) is walked across, through the columns
        /// whose own water reaches that height (the pool, and the pools upstream, which stand higher). Pools downstream stand
        /// lower and stay unmarked: a river that ends in a closed hollow below the dam is not raised by it.
        /// </summary>
        /// <summary>The last <see cref="MarkDammed"/> walk stopped at unloaded land.</summary>
        static bool markUnloaded;

        /// <summary>
        /// A waiting dam asked again (<see cref="H2DamPools"/>): the river's columns are marked and the pool started, without
        /// the rest of <see cref="Placed"/> (its reach wake and the held memo's forgetting ran when the block was placed;
        /// Fable's re-review, low 2). A dam that still waits is back in the waiting list.
        /// </summary>
        internal static void RetryDam(IWorldAccessor world, BlockPos pos)
        {
            if (!Enabled || !H2Filling.Enabled || !EarthRiverQuery.Active || pos.dimension != 0) return;
            var a = placeA ??= new BlockPos(0);
            a.dimension = pos.dimension;
            int top = MarkDammed(world.BlockAccessor, pos, a);
            if (top == int.MinValue) return;
            H2DamPools.Retrying = true;
            try { H2DamPools.Dammed(world, pos, top, markCols, markRiver, markUnloaded); }
            finally { H2DamPools.Retrying = false; }
        }

        /// <summary>A partial river pool marks its river again from its dam block: the columns found are added to
        /// <paramref name="cols"/>. True while the walk still meets unloaded land.</summary>
        internal static bool Remark(IWorldAccessor world, BlockPos pos, HashSet<(int x, int z)> cols)
        {
            var a = placeA ??= new BlockPos(0);
            a.dimension = pos.dimension;
            int top = MarkDammed(world.BlockAccessor, pos, a);
            if (top == int.MinValue) return true;
            foreach (var c in markCols) cols.Add(c);
            return markUnloaded;
        }

        static int MarkDammed(IBlockAccessor ba, BlockPos pos, BlockPos a)
        {
            markCols.Clear();
            markUnloaded = false;
            if (dammed.Count >= MaxDammed)
            {
                // Fable's pass: said once, not silently
                if (!markCapLogged) { markCapLogged = true; Log?.Invoke($"Hydrology: {MaxDammed} river columns are marked for dams this session; no more are marked until the world restarts"); }
                return int.MinValue;
            }
            // the dam's pool: the top of the river water in the columns beside the block
            int top = int.MinValue;
            for (int i = 0; i < 4; i++)
            {
                a.Set(pos.X, pos.Y, pos.Z).Add(H[i]);
                for (int d = 0; d <= 4 && a.Y >= 1 && !(Liquids.IsSource(Liquids.Fluid(ba, a)) && EarthRiverQuery.RiverWater(a)); d++) a.Y--;
                if (!Liquids.IsSource(Liquids.Fluid(ba, a)) || !EarthRiverQuery.RiverWater(a)) continue;
                while (true)
                {
                    a.Y++;
                    var f = Liquids.Fluid(ba, a);
                    if (!Liquids.IsSource(f) || !EarthRiverQuery.RiverWater(a)) break;
                }
                if (a.Y - 1 > top) { top = a.Y - 1; markRiver.dimension = pos.dimension; markRiver.Set(a.X, a.Y - 1, a.Z); }
            }
            if (top == int.MinValue) return top;
            markSeen.Clear();
            markQueue.Clear();
            for (int i = 0; i < 4; i++)
            {
                a.Set(pos.X, top, pos.Z).Add(H[i]);
                if (Liquids.IsSource(Liquids.Fluid(ba, a)) && markSeen.Add((a.X, a.Z))) markQueue.Enqueue((a.X, a.Z));
            }
            // HY-0018: its own scratch (Placed's n was shared)
            var c = markC ??= new BlockPos(0);
            c.dimension = pos.dimension;
            while (markQueue.Count > 0)
            {
                var (x, z) = markQueue.Dequeue();
                a.Set(x, top, z);
                if (!EarthRiverQuery.RiverWater(a)) continue;
                dammed.Add((pos.dimension, x, z));
                markCols.Add((x, z));
                if (dammed.Count >= MaxDammed) return top;
                for (int i = 0; i < 4; i++)
                {
                    c.Set(a).Add(H[i]);
                    if (markSeen.Count >= MaxMarkWalk || markSeen.Contains((c.X, c.Z))) continue;
                    // Fable's re-review (medium): the river runs on into unloaded land (read as air there): the pool's
                    // extent is not known, so the dam waits instead of starting a pool over a part of it
                    if (!Liquids.Loaded(ba, c)) { markUnloaded = true; continue; }
                    if (!Liquids.Passes(ba, a, c, H[i])) continue;
                    if (!Liquids.IsSource(Liquids.Fluid(ba, c))) continue;
                    markSeen.Add((c.X, c.Z));
                    markQueue.Enqueue((c.X, c.Z));
                }
            }
            return top;
        }
    }
}
