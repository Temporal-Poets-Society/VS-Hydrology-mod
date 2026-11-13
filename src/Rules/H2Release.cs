using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// H2 release (0.6.2-rc.8, HY-0007, decision 0007): a pond a player opens loses the water above the opening, and that
    /// water collects at the lowest point its flow reaches. The author: "if pond is drained then it should end up in lowest
    /// point by flow".
    ///
    /// Vanilla's sources are endless and its flowing water has no volume, so an opened pond keeps its level for ever and
    /// only a stream of flowing water runs out; rc.7 (decision 0006) stopped that stream from flooding the land below. This
    /// moves the water itself, at once, when the player's break happens:
    /// <list type="number">
    /// <item>The body: the sources of the liquid joined to a source beside the opened cell, at its level. A body of
    /// <see cref="MaxBody"/> sources or more (a lake, the sea), or one with a natural river column (Earth's river query), is
    /// unlimited: nothing moves, and decision 0006's supply rule decides what its water fills.</item>
    /// <item>What drains: the body's sources at or above the opening's level.</item>
    /// <item>The path: from the opening the water runs along its level up to <see cref="MaxRun"/> blocks to the nearest
    /// edge with nothing under it, falls, and runs on (<see cref="Layer"/>). Water reaching the sea, a river or a large lake joins it;
    /// that water does not rise. Water reaching a small pool fills on top of it.</item>
    /// <item>The pool: where it lands, the water stands, nearest the landing point first, unless its flowing water
    /// would reach an edge (within <see cref="MaxRun"/> of the pool), where it spills on down. A basin it fills to its
    /// walls it fills layer by layer upwards. Every filled cell is below the opening.</item>
    /// <item>Nothing moves when the water does not fit below the opening (no lower land within <see cref="MaxRun"/> of
    /// the opening, a basin smaller than the water), when the search grows past <see cref="MaxSearch"/>, or reaches an
    /// unloaded chunk: the pond keeps its water and lets out flowing water, as rc.7.</item>
    /// </list>
    /// The move is immediate: water in transit has no volume in Vintage Story, so no stream is simulated. Volume is kept to
    /// the layer: the last, partly filled layer of a basin may be completed by H2 when decision 0006's supply rule allows.
    /// Only a player's break starts it (the server's break event): buckets, world generation and other mods do not.
    /// </summary>
    public static class H2Release
    {
        public static bool Enabled = true;
        /// <summary>A body with this many sources or more counts as unlimited (as <c>MaxHeldLevel</c> bounds a held level).</summary>
        public const int MaxBody = 4096;
        /// <summary>Cells the path and basin search may look at, with the bodies it meets.</summary>
        public const int MaxSearch = 32768;
        /// <summary>How far water runs on a level to find an edge: from the opening on its own level, and from the edge of
        /// the pool it makes below (vanilla's flowing water runs 7 blocks from a source).</summary>
        public const int MaxRun = 8;
        /// <summary>One line per release (the game's log; null offline); one per opening whose water stays (the verbose
        /// debug log: a trench dug from a pond opens one at every break).</summary>
        public static System.Action<string> Log, LogKept;

        static readonly BlockFacing[] H = BlockFacing.HORIZONTALS;

        /// <summary>What a search ended in.</summary>
        enum End { Fits, Absorbed, Kept }

        /// <summary>
        /// A player opened <paramref name="pos"/> (broke the block there). Returns the number of sources moved out of the
        /// pond (0: nothing moved).
        /// </summary>
        public static int Opened(IWorldAccessor world, BlockPos pos)
        {
            if (!Enabled || !H2Filling.Enabled || pos.Y < 1) return 0;
            var ba = world.BlockAccessor;
            if (!Liquids.Open(ba, pos)) return 0;
            var here = Liquids.Fluid(ba, pos);
            if (here.Id != 0 && (!Liquids.Handles(here) || Liquids.IsSource(here))) return 0;
            Block b = null;
            BlockPos start = null;
            var n = new BlockPos(pos.dimension);
            for (int i = 0; i < 4 && b == null; i++)
            {
                n.Set(pos).Add(H[i]);
                var f = Liquids.Fluid(ba, n);
                if (Liquids.Handles(f) && Liquids.IsSource(f) && Liquids.Passes(ba, pos, n, H[i])) { b = f; start = n.Copy(); }
            }
            if (b == null) return 0;

            var s = new Search(ba, pos, b);
            if (!s.Body(start, s.pond))
            {
                HydrologyStats.ReleaseKept++;
                return 0;
            }
            foreach (var c in s.pond) if (c.y >= pos.Y) { s.drained.Add(c); s.walls.Add(c); }
            int v = s.drained.Count;
            if (v == 0) return 0;
            var end = s.Pour(v);
            if (end == End.Kept)
            {
                HydrologyStats.ReleaseKept++;
                LogKept?.Invoke($"Hydrology: a pond opened at {pos.X},{pos.Y},{pos.Z} keeps its water ({s.why}; {v} sources above the opening)");
                return 0;
            }

            // apply: the pond's water above the opening goes, the basin's cells become sources
            int id = (b.LiquidCode == "saltwater" ? Liquids.SaltSource : Liquids.WaterSource).Id;
            var p = new BlockPos(pos.dimension);
            foreach (var (x, y, z) in s.drained) Liquids.SetAndWake(world, p.Set(x, y, z), 0);
            foreach (var (x, y, z) in s.filled) Liquids.SetAndWake(world, p.Set(x, y, z), id);
            int x0 = pos.X, y0 = pos.Y, z0 = pos.Z, x1 = pos.X, y1 = pos.Y, z1 = pos.Z;
            foreach (var list in new[] { s.drained, s.filled })
                foreach (var (x, y, z) in list)
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                    if (z < z0) z0 = z; if (z > z1) z1 = z;
                }
            H2Filling.ForgetHeldNear(pos.dimension, x0 - MaxRun, y0 - MaxRun, z0 - MaxRun, x1 + MaxRun, y1 + MaxRun, z1 + MaxRun);
            HydrologyStats.Released++;
            HydrologyStats.ReleasedCells += v;
            HydrologyStats.ReleaseAbsorbed += v - s.filled.Count;
            string where = "";
            if (s.filled.Count > 0)
            {
                var (fx, fy, fz) = s.filled[0];
                int low = fy;
                foreach (var c in s.filled) if (c.y < low) low = c.y;
                where = $" from {fx},{fy},{fz} (lowest {low})";
            }
            Log?.Invoke($"Hydrology: a pond opened at {pos.X},{pos.Y},{pos.Z} let out {v} sources: {s.filled.Count} collect lower down{where}" +
                        (v > s.filled.Count ? $", {v - s.filled.Count} join {s.why}" : ""));
            return v;
        }

        sealed class Search
        {
            readonly IBlockAccessor ba;
            readonly Block b;
            readonly int dim, sill;
            /// <summary>The opened pond's sources; those draining (also walls to the water let out); the basin's new sources
            /// in fill order (and as a set); small pools the water lands on (support).</summary>
            public readonly HashSet<(int x, int y, int z)> pond = new(), walls = new(), filledSet = new(), pools = new(), others = new();
            public readonly List<(int x, int y, int z)> drained = new(), filled = new();
            /// <summary>Why the water stays, or what absorbed it.</summary>
            public string why = "";
            int looked;
            readonly BlockPos p, q, step;

            public Search(IBlockAccessor ba, BlockPos opening, Block b)
            {
                this.ba = ba;
                this.b = b;
                dim = opening.dimension;
                sill = opening.Y;
                p = new BlockPos(dim);
                q = new BlockPos(dim);
                step = new BlockPos(dim);
                cur = (opening.X, opening.Y, opening.Z);
            }

            (int x, int y, int z) cur;

            /// <summary>
            /// Collects the body of <paramref name="from"/>'s liquid joined to it into <paramref name="into"/>; false when it is
            /// unlimited (<see cref="MaxBody"/> or more, or a natural river column), with <see cref="why"/> set.
            /// </summary>
            public bool Body(BlockPos from, HashSet<(int x, int y, int z)> into)
            {
                var f0 = Liquids.Fluid(ba, from);
                var queue = new Queue<(int x, int y, int z)>();
                into.Add((from.X, from.Y, from.Z));
                queue.Enqueue((from.X, from.Y, from.Z));
                bool river = EarthRiverQuery.Active && dim == 0;
                var columns = new HashSet<(int, int)>();
                var a = new BlockPos(dim);
                var c = new BlockPos(dim);
                while (queue.Count > 0)
                {
                    var (x, y, z) = queue.Dequeue();
                    a.Set(x, y, z);
                    if (river && columns.Add((x, z)) && EarthRiverQuery.River(a)) { why = "river water"; return false; }
                    foreach (var face in BlockFacing.ALLFACES)
                    {
                        c.Set(a).Add(face);
                        if (c.Y < 0 || into.Contains((c.X, c.Y, c.Z)) || !Liquids.Passes(ba, a, c, face)) continue;
                        var f = Liquids.Fluid(ba, c);
                        if (!Liquids.IsSource(f) || !Liquids.Same(f, f0)) continue;
                        if (into.Count >= MaxBody) { why = "a large body of water"; return false; }
                        into.Add((c.X, c.Y, c.Z));
                        queue.Enqueue((c.X, c.Y, c.Z));
                    }
                }
                looked += into.Count;
                return true;
            }

            bool Loaded(int x, int y, int z) => Liquids.Loaded(ba, p.Set(x, y, z));

            bool Wall(int x, int y, int z, int fx, int fy, int fz, BlockFacing face)
            {
                p.Set(fx, fy, fz);
                q.Set(x, y, z);
                return walls.Contains((x, y, z)) || filledSet.Contains((x, y, z)) || !Liquids.Open(ba, q) || !Liquids.Passes(ba, p, q, face);
            }

            /// <summary>Under (x, y, z) holds water: ground, a block water cannot pass down into, a cell the basin fills, a
            /// small pool landed on. Open space or a source under it is a drop.</summary>
            bool Supported(int x, int y, int z)
            {
                if (filledSet.Contains((x, y - 1, z)) || pools.Contains((x, y - 1, z))) return true;
                if (Liquids.GroundBelow(ba, p.Set(x, y, z))) return true;
                q.Set(x, y - 1, z);
                if (!Liquids.Open(ba, q) || !Liquids.Passes(ba, p, q, BlockFacing.DOWN)) return true;
                return false;
            }

            /// <summary>
            /// Lets <paramref name="v"/> sources run from the opening: level by level, falling at the nearest edge, filling
            /// closed levels below the opening.
            /// </summary>
            public End Pour(int v)
            {
                var seeds = new List<(int x, int y, int z)> { cur };
                while (true)
                {
                    var region = new List<(int x, int y, int z)>();
                    var r = Layer(seeds, region, v, out var spill, out bool closed);
                    if (r != End.Fits) return r;
                    if (spill.HasValue)
                    {
                        // the level leaks at its nearest edge: fall from there to whatever holds water
                        var (x, y, z) = spill.Value;
                        while (true)
                        {
                            if (y - 1 < 1) { why = "the world's floor"; return End.Kept; }
                            if (!Loaded(x, y - 1, z)) { why = "unloaded land"; return End.Kept; }
                            if (Liquids.GroundBelow(ba, p.Set(x, y, z)) || filledSet.Contains((x, y - 1, z)) || pools.Contains((x, y - 1, z))) break;
                            var f = Liquids.Fluid(ba, p.Set(x, y - 1, z));
                            if (Liquids.IsSource(f))
                            {
                                var e = Join(x, y - 1, z);
                                if (e != End.Fits) return e;
                                break;
                            }
                            if (!Liquids.Open(ba, p) || !Liquids.Passes(ba, q.Set(x, y, z), p, BlockFacing.DOWN)) break; // rests on it
                            y--;
                            if (++looked > MaxSearch) { why = "open land beyond the search"; return End.Kept; }
                        }
                        seeds.Clear();
                        seeds.Add((x, y, z));
                        continue;
                    }
                    // no edge within reach: the water stands here, below the opening, nearest the landing first
                    if (region[0].y >= sill) { why = $"no lower land within {MaxRun} blocks of the opening"; return End.Kept; }
                    int k = System.Math.Min(v, region.Count);
                    for (int i = 0; i < k; i++) { filled.Add(region[i]); filledSet.Add(region[i]); }
                    v -= k;
                    if (v == 0) return End.Fits;
                    if (!closed) { why = "open land beyond the search"; return End.Kept; }
                    seeds.Clear();
                    foreach (var (x, y, z) in region)
                        if (!Wall(x, y + 1, z, x, y, z, BlockFacing.UP)) seeds.Add((x, y + 1, z));
                    if (seeds.Count == 0) { why = "a closed basin too small for the water"; return End.Kept; }
                }
            }

            /// <summary>The water lands on a source at (x, y, z): the sea, a river or a large lake takes it; a small pool of
            /// the same liquid holds it up (Fits); the opened pond itself or another liquid's pool keeps it.</summary>
            End Join(int x, int y, int z)
            {
                if (pools.Contains((x, y, z))) return End.Fits;
                if (pond.Contains((x, y, z))) { why = "the pond itself"; return End.Kept; }
                var f = Liquids.Fluid(ba, p.Set(x, y, z));
                var pool = new HashSet<(int x, int y, int z)>();
                if (!Body(p.Copy(), pool)) return End.Absorbed;
                if (looked > MaxSearch) { why = "open land beyond the search"; return End.Kept; }
                if (!Liquids.Same(f, b)) { why = "another liquid"; return End.Kept; }
                foreach (var c in pool) if (pond.Contains(c)) { why = "the pond itself"; return End.Kept; }
                pools.UnionWith(pool);
                return End.Fits;
            }

            /// <summary>
            /// The level of <paramref name="seeds"/>: the open cells joined to them on that level that the water reaches,
            /// in search order from the seeds (nearest first). Water reaches a cell with nothing under it within
            /// <see cref="MaxRun"/> of the opening on the opening's own level, and below that within <see cref="MaxRun"/>
            /// of the pool its <paramref name="water"/> sources make (flowing water runs 7 blocks from a source): the first
            /// such cell is the <paramref name="spill"/>. Without one, <paramref name="closed"/> says whether the level was
            /// searched to its walls (a basin the water may fill and rise in). A source met on the level is a wall, or takes
            /// the water when it belongs to the sea, a river or a large lake.
            /// </summary>
            End Layer(List<(int x, int y, int z)> seeds, List<(int x, int y, int z)> region, int water,
                      out (int x, int y, int z)? spill, out bool closed)
            {
                spill = null;
                closed = true;
                bool atSill = seeds[0].y >= sill;
                int limit = atSill ? MaxRun : int.MaxValue;
                var seen = new HashSet<(int x, int y, int z)>();
                var queue = new Queue<((int x, int y, int z) c, int d)>();
                foreach (var c in seeds)
                    if (seen.Add(c)) queue.Enqueue((c, 0));
                while (queue.Count > 0)
                {
                    var (c, d) = queue.Dequeue();
                    if (d > limit) { closed = false; continue; }
                    if (!Loaded(c.x, c.y, c.z)) { why = "unloaded land"; return End.Kept; }
                    if (!Supported(c.x, c.y, c.z))
                    {
                        // an edge, or water under it to land on
                        spill = c;
                        return End.Fits;
                    }
                    region.Add(c);
                    // the pool is complete: look on only as far as its flowing water runs
                    if (!atSill && region.Count == water) limit = d + MaxRun;
                    if (++looked > MaxSearch) { why = "open land beyond the search"; return End.Kept; }
                    foreach (var face in H)
                    {
                        step.Set(c.x, c.y, c.z).Add(face);
                        var m = (x: step.X, y: step.Y, z: step.Z);
                        if (seen.Contains(m) || Wall(m.x, m.y, m.z, c.x, c.y, c.z, face)) continue;
                        seen.Add(m);
                        var f = Liquids.Fluid(ba, p.Set(m.x, m.y, m.z));
                        if (f.Id != 0 && !Liquids.Handles(f)) continue;
                        if (Liquids.IsSource(f))
                        {
                            // water standing at this level: the sea or a river takes the water; a pool's edge is a wall
                            if (pools.Contains(m) || pond.Contains(m) || others.Contains(m)) continue;
                            var pool = new HashSet<(int x, int y, int z)>();
                            if (!Body(p.Copy(), pool)) return End.Absorbed;
                            others.UnionWith(pool);
                            if (looked > MaxSearch) { why = "open land beyond the search"; return End.Kept; }
                            continue;
                        }
                        queue.Enqueue((m, d + 1));
                    }
                }
                return End.Fits;
            }
        }
    }
}
