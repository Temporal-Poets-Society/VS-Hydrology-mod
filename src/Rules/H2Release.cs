using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// H2 release: a pond a player opens loses the water above the opening over time, and that water collects at the lowest
    /// point its flow reaches (decisions 0007, 0008, 0009).
    ///
    /// Vanilla's sources are endless and its flowing water has no volume, so an opened pond keeps its level for ever and
    /// only a stream of flowing water runs out; decision 0006 stops that stream from flooding the land below. This moves
    /// the water itself, when the player's break happens:
    /// <list type="number">
    /// <item>The body: the sources of the liquid joined to a source beside the opened cell, on loaded land (a body reaching
    /// an unloaded chunk is not measured). A natural river's own water is never part of it: it is the body's floor and feed,
    /// and makes the body unlimited only where it meets a channel dug from the river at or above the opening. A body of
    /// <see cref="MaxBody"/> sources or more is unlimited too: nothing moves, and the H2 rules decide what its water
    /// fills.</item>
    /// <item>What drains: the body's sources at or above the opening's level.</item>
    /// <item>The rate: <see cref="DrainRate"/> sources a second for each opened cell and each block of water over the
    /// opening's floor (a weir). A body whose top layer would take longer than <see cref="UnlimitedSeconds"/> to let out
    /// is unlimited too: a lake practically holds, a pond visibly empties. While it drains, its remaining sources send
    /// vanilla's flowing water down the path, so the stream is seen; the drained cells are not filled again meanwhile.</item>
    /// <item>The path: from the opening the water runs along its level up to <see cref="MaxRun"/> blocks to the nearest
    /// edge with nothing under it, falls, and runs on (the search's layers). Water reaching the sea, a river or a
    /// large lake (<see cref="AbsorbBody"/>) joins it; that water does not rise. Water reaching a small pool fills on top
    /// of it.</item>
    /// <item>The pool: where it lands, the water stands, nearest the landing point first, unless its flowing water
    /// would reach an edge (within <see cref="MaxRun"/> of the pool), where it spills on down. A basin it fills to its
    /// walls it fills layer by layer upwards. Every filled cell is below the opening.</item>
    /// <item>Nothing moves when there is no lower land within <see cref="MaxRun"/> of the opening, or the search grows
    /// past <see cref="MaxSearch"/> or reaches an unloaded chunk before the water has left the opening's level: the pond
    /// keeps its water and lets out flowing water. Once the water has fallen from the opening's level, the end of the
    /// search or unloaded land is an open valley running on downhill: a pond's water runs off there, as into the sea. A
    /// closed basin on the way fills to its sill: the pond lets out the whole layers the basins hold and keeps the
    /// rest.</item>
    /// </list>
    /// The path is planned once, when the drain starts; each second the next sources (the top layer first, the far ones
    /// first) go and as many stand at the next cells of the plan. Another opening into a draining pond widens its outlet;
    /// closing every opening ends the drain once the layer it is draining has gone (its holes would fill again from the
    /// sources left). A drain pauses while its opening's chunk is unloaded and is saved with the world (its openings; it is
    /// planned again when the world is loaded). When it ends, water that stands at or above the opening again drains in a
    /// new round. Only a player's break starts it (the server's break event): buckets, world generation and other mods do
    /// not. The other parts: H2Drains.cs (drains, save), H2Dams.cs (placed blocks, river marks).
    ///
    /// History: 0.6.2-rc.8 moved the water at once (HY-0007; the author: "if pond is drained then it should end up in
    /// lowest point by flow"); rc.9 drained it over time at the opening's rate (HY-0009; "drainage of large lake would
    /// depend on drain rate - size of opening irl"); rc.11 added the open valleys and closed basins; rc.12 the river as a
    /// floor; HY-0013 (after rc.13) the loaded-land rule.
    /// </summary>
    public static partial class H2Release
    {
        public static bool Enabled = true;

        /// <summary>Sources a second let out per opened cell and per block of water over the opening's floor; 0 lets all of
        /// it out at once (rc.8).</summary>
        public static double DrainRate = 1.0;

        /// <summary>A body whose top layer would take longer than this many seconds to let out is unlimited (2,880: a game day
        /// at the default speed).</summary>
        public static int UnlimitedSeconds = 2880;

        /// <summary>A body with this many sources or more counts as unlimited without measuring its rate.</summary>
        public const int MaxBody = 65536;

        /// <summary>Water the released water lands in that takes it without rising: this many sources or more (as
        /// <c>MaxHeldLevel</c> bounds a held level), or a natural river.</summary>
        public const int AbsorbBody = 4096;

        /// <summary>Cells the path and basin search may look at, with the bodies it meets.</summary>
        public const int MaxSearch = 32768;

        /// <summary>How far water runs on a level to find an edge: from the opening on its own level, and from the edge of
        /// the pool it makes below (vanilla's flowing water runs 7 blocks from a source).</summary>
        public const int MaxRun = 8;

        /// <summary>Rounds one opening drains in (a new round when water stands above the opening again).</summary>
        const int MaxRounds = 8;

        /// <summary>One line per drain started and ended (the game's log; null offline); one per opening whose water stays
        /// (the verbose debug log: a trench dug from a pond opens one at every break).</summary>
        public static System.Action<string> Log, LogKept;

        static readonly BlockFacing[] H = BlockFacing.HORIZONTALS;

        /// <summary>What a search ended in.</summary>
        enum End { Fits, Absorbed, Kept }

        /// <summary>The last <see cref="Opened"/> found the body reaching unloaded land (its size unknown).</summary>
        static bool lastUnknown;

        /// <summary>A new world in the same process: no drain carries over.</summary>
        public static void ResetWorld() { jobs.Clear(); draining.Clear(); pending.Clear(); dammed.Clear(); largeBodies.Clear(); H2DamPools.ResetWorld(); }

        /// <summary>
        /// A player opened <paramref name="pos"/> (broke the block there). Returns the number of sources the drain it started
        /// will let out (0: nothing moves, or the opening widened a drain already running).
        /// </summary>
        public static int Opened(IWorldAccessor world, BlockPos pos) => Opened(world, pos, 0, false, null);

        /// <param name="again">A drain started again (a new round, a saved drain): water standing in the opening is the
        /// pond's own, not a cell inside the water.</param>
        /// <param name="known">The openings of the drain this one continues: they are part of the breach even when water
        /// stands in them again (a save from before rc.9's guard on openings).</param>
        static int Opened(IWorldAccessor world, BlockPos pos, int rounds, bool again, List<(int x, int y, int z)> known)
        {
            lastUnknown = false;
            if (!Enabled || !H2Filling.Enabled || pos.Y < 1) return 0;
            var ba = world.BlockAccessor;
            if (!Liquids.Open(ba, pos)) return 0;
            var here = Liquids.Fluid(ba, pos);
            if (here.Id != 0 && (!Liquids.Handles(here) || (Liquids.IsSource(here) && !again))) return 0;
            Block b = null;
            var n = openN ??= new BlockPos(0);
            var start = openStart ??= new BlockPos(0);
            n.dimension = start.dimension = pos.dimension;
            for (int i = 0; i < 4 && b == null; i++)
            {
                n.Set(pos).Add(H[i]);
                var f = Liquids.Fluid(ba, n);
                if (Liquids.Handles(f) && Liquids.IsSource(f) && Liquids.Passes(ba, pos, n, H[i])) { b = f; start.Set(n); }
            }
            if (b == null) return 0;

            // a pond already draining: the new opening widens its outlet
            var key = (start.X, start.Y, start.Z);
            var opening = (pos.X, pos.Y, pos.Z);
            foreach (var j in jobs)
            {
                if (j.dim != pos.dimension || !j.body.Contains(key)) continue;
                if (!j.openings.Contains(opening)) { j.openings.Add(opening); draining.Add((j.dim, pos.X, pos.Y, pos.Z)); }
                if (pos.Y < j.sill) j.sill = pos.Y;
                return 0;
            }

            // HY-0010: one search reused from call to call (a player's every break beside water runs it); a drain that
            // starts takes the search's pond and plan with it, and the search makes new ones
            var s = search ??= new Search();
            s.Reset(ba, pos, b);
            // Fable's pass (F3): a body found to be large (65,536 sources) a moment ago is not measured again for every block
            // broken on its shore (16 ms a break beside the sea on the offline adapter)
            if (InLargeBody(pos.dimension, start, world.ElapsedMilliseconds))
            {
                HydrologyStats.ReleaseKept++;
                return 0;
            }
            if (!s.Body(start, s.pond, MaxBody, pos.Y))
            {
                if (s.large) RememberLargeBody(pos.dimension, s.pond, world.ElapsedMilliseconds);
                // HY-0013: a body reaching unloaded land cannot be measured: the water stays (a saved drain waits)
                lastUnknown = s.unknown;
                HydrologyStats.ReleaseKept++;
                return 0;
            }
            int topY = int.MinValue;
            foreach (var c in s.pond) if (c.y >= pos.Y) { s.drained.Add(c); s.walls.Add(c); if (c.y > topY) topY = c.y; }
            int v = s.drained.Count;
            if (v == 0) return 0;
            int layer = 0;
            foreach (var c in s.drained) if (c.y == topY) layer++;
            // the breach: every open cell beside the water that drains (a 2-wide break opened one block at a time counts
            // both once both are open), its width the columns it spans
            var outs = openOuts;
            outs.Clear();
            outs.Add((pos.X, pos.Y, pos.Z));
            var a = openA ??= new BlockPos(0);
            a.dimension = pos.dimension;
            foreach (var c in s.drained)
                foreach (var face in H)
                {
                    n.Set(c.x, c.y, c.z).Add(face);
                    var m = (n.X, n.Y, n.Z);
                    // the breach is where the player opened it: leaks elsewhere on a long shore were there before
                    if (System.Math.Abs(m.X - pos.X) > MaxRun || System.Math.Abs(m.Z - pos.Z) > MaxRun) continue;
                    if (s.pond.Contains(m) || outs.Contains(m) || !Liquids.Open(ba, n) || !Liquids.Passes(ba, a.Set(c.x, c.y, c.z), n, face)) continue;
                    var f = Liquids.Fluid(ba, n);
                    if (Liquids.IsSource(f) && Liquids.Same(f, b)) continue;
                    outs.Add(m);
                }
            if (known != null)
                foreach (var o in known)
                    if (!outs.Contains(o) && Liquids.Open(ba, a.Set(o.x, o.y, o.z))) outs.Add(o);
            int width = Width(outs);
            if (DrainRate > 0 && layer / (DrainRate * width * (topY - pos.Y + 1)) > UnlimitedSeconds)
            {
                HydrologyStats.ReleaseKept++;
                LogKept?.Invoke($"Hydrology: a pond opened at {pos.X},{pos.Y},{pos.Z} keeps its water (its top layer of {layer} would take over {UnlimitedSeconds} s to let out through {width} cells)");
                return 0;
            }
            var end = s.Pour(v);
            // rc.11 (HY-0009): a closed basin on the way fills to its sill and no more: the pond lets out what the basins
            // below its opening take, in whole layers (a layer drained in part fills again from the sources left beside its
            // holes, vanilla's endless supply), and keeps the rest; H2 may complete the basin from the stream
            bool part = false;
            if (end == End.Kept && s.filled.Count > 0)
            {
                int whole = 0;
                for (int y = topY; y >= pos.Y; y--)
                {
                    int n1 = 0;
                    foreach (var c in s.drained) if (c.y == y) n1++;
                    if (whole + n1 > s.filled.Count) break;
                    whole += n1;
                }
                if (whole > 0)
                {
                    LogKept?.Invoke($"Hydrology: a pond opened at {pos.X},{pos.Y},{pos.Z} lets out only what the land below holds ({whole} of {v}, the basins take {s.filled.Count}; {s.why})");
                    v = whole;
                    end = End.Fits;
                    part = true;
                }
            }
            if (end == End.Kept)
            {
                HydrologyStats.ReleaseKept++;
                LogKept?.Invoke($"Hydrology: a pond opened at {pos.X},{pos.Y},{pos.Z} keeps its water ({s.why}; {v} sources above the opening" + (s.filled.Count > 0 ? $", {s.filled.Count} would fit below, less than its top layer" : "") + ")");
                return 0;
            }

            // the top layer first, and in it the cells farthest from the opening, so the water by the opening runs to the end
            var order = s.drained.OrderByDescending(c => c.y)
                .ThenByDescending(c => (c.x - pos.X) * (c.x - pos.X) + (c.z - pos.Z) * (c.z - pos.Z)).Take(v).ToList();
            var job = new Drain
            {
                dim = pos.dimension, sill = pos.Y, rounds = rounds, liquid = b,
                sourceId = Liquids.SourceOf(b).Id,
                body = s.pond, drained = order, filled = s.filled, why = s.why
            };
            s.Release();
            job.origin = opening;
            // the dam is open: a river pool filling behind it stops (rc.13)
            H2DamPools.Stop(job.dim, job.body);
            // the stream leaves through the openings: H2 leaves them flowing water while the drain runs
            foreach (var o in outs)
            {
                job.openings.Add(o);
                draining.Add((job.dim, o.x, o.y, o.z));
            }
            HydrologyStats.Released++;
            // (the search has handed its plan to the drain)
            var plan = job.filled;
            string where = "";
            if (plan.Count > 0)
            {
                var (fx, fy, fz) = plan[0];
                int low = fy;
                foreach (var c in plan) if (c.y < low) low = c.y;
                where = $" from {fx},{fy},{fz} (lowest {low})";
            }
            Log?.Invoke($"Hydrology: a pond opened at {pos.X},{pos.Y},{pos.Z} lets out {v} sources" +
                        (DrainRate > 0 ? $" (about {v / (DrainRate * width * (topY - pos.Y + 1)):0} s through {width} cells)" : "") +
                        $": {System.Math.Min(v, plan.Count)} collect lower down{where}" + (v > plan.Count ? $", {v - plan.Count} join {job.why}" : "") +
                        (part ? $" (the pond keeps the rest: {job.why})" : ""));
            if (DrainRate <= 0)
            {
                Step(world, job, int.MaxValue);
                Finish(world, job, false);
            }
            else jobs.Add(job);
            return v;
        }

        // Fable's pass (F3): the last two bodies found large, kept LargeBodyMs (a lake does not change in that time)
        const long LargeBodyMs = 30000;
        static readonly List<(int dim, HashSet<(int x, int y, int z)> cells, long until)> largeBodies = new();

        static bool InLargeBody(int dim, BlockPos start, long now)
        {
            for (int i = largeBodies.Count - 1; i >= 0; i--)
            {
                var (d, cells, until) = largeBodies[i];
                if (now > until || now < until - LargeBodyMs) { largeBodies.RemoveAt(i); continue; }
                if (d == dim && cells.Contains((start.X, start.Y, start.Z))) return true;
            }
            return false;
        }

        /// <summary>A player placed a block at or beside a remembered large body (it may wall a bay off from the sea): it is
        /// measured again at the next break (Fable's re-review, low 6; a break only joins water, so it keeps the memory).</summary>
        public static void ForgetLargeBodyNear(int dim, int x, int y, int z)
        {
            for (int i = largeBodies.Count - 1; i >= 0; i--)
            {
                var (d, cells, _) = largeBodies[i];
                if (d != dim) continue;
                if (cells.Contains((x, y, z)) || cells.Contains((x + 1, y, z)) || cells.Contains((x - 1, y, z)) || cells.Contains((x, y + 1, z))
                    || cells.Contains((x, y - 1, z)) || cells.Contains((x, y, z + 1)) || cells.Contains((x, y, z - 1)))
                    largeBodies.RemoveAt(i);
            }
        }

        static void RememberLargeBody(int dim, HashSet<(int x, int y, int z)> cells, long now)
        {
            if (largeBodies.Count >= 2) largeBodies.RemoveAt(0);
            largeBodies.Add((dim, new HashSet<(int x, int y, int z)>(cells), now + LargeBodyMs));
        }

        // HY-0010: scratch reused from call to call (server thread only; none of these calls runs inside another)
        static Search search;

        static BlockPos openN, openStart, openA, placeN, placeA, markC;

        static readonly List<(int x, int y, int z)> openOuts = new();

        static readonly HashSet<(int, int)> widthColumns = new();

        /// <summary>An outlet's width: the columns its open cells span (a breach 2 wide and 2 deep is 2 wide; the depth is
        /// the head).</summary>
        static int Width(List<(int x, int y, int z)> openings)
        {
            var columns = widthColumns;
            columns.Clear();
            foreach (var o in openings) columns.Add((o.x, o.z));
            return System.Math.Max(1, columns.Count);
        }

        sealed class Search
        {
            IBlockAccessor ba;
            Block b;
            int dim, sill;
            /// <summary>The opened pond's sources; those draining (also walls to the water let out); the basin's new sources
            /// in fill order (and as a set); small pools the water lands on (support). A drain that starts keeps
            /// <see cref="pond"/> and <see cref="filled"/> (<see cref="Release"/>).</summary>
            public HashSet<(int x, int y, int z)> pond = new();
            public readonly HashSet<(int x, int y, int z)> walls = new(), filledSet = new(), pools = new(), others = new();
            public List<(int x, int y, int z)> filled = new();
            public readonly List<(int x, int y, int z)> drained = new();
            /// <summary>Why the water stays, or what absorbed it.</summary>
            public string why = "";
            int looked;
            /// <summary>The water has left the opening's level over an edge: it runs downhill from here.</summary>
            bool fell;
            /// <summary>HY-0013: <see cref="Body"/> reached a cell in an unloaded chunk: the body's size is not known.</summary>
            public bool unknown;
            /// <summary>The body reached its cap (Fable's re-review: a flag, not a test of the log's words).</summary>
            public bool large;
            readonly Dictionary<(int, int, int), bool> chunks = new();
            bool ChunkLoaded(BlockPos c)
            {
                var key = (c.X >> 5, c.Y >> 5, c.Z >> 5);
                if (!chunks.TryGetValue(key, out bool loaded)) chunks[key] = loaded = Liquids.Loaded(ba, c);
                return loaded;
            }
            readonly BlockPos p = new(0), q = new(0), step = new(0), bodyA = new(0), bodyC = new(0);
            // HY-0010: the searches' own scratch, cleared on use
            readonly Queue<(int x, int y, int z)> bodyQueue = new();
            readonly HashSet<(int x, int y, int z)> riverFloor = new();
            readonly HashSet<(int x, int y, int z)> pool = new(), seen = new();
            readonly Queue<((int x, int y, int z) c, int d)> layerQueue = new();
            readonly List<(int x, int y, int z)> seeds = new(), region = new();

            /// <summary>Starts a new search from <paramref name="opening"/> for liquid <paramref name="b"/>.</summary>
            public void Reset(IBlockAccessor ba, BlockPos opening, Block b)
            {
                this.ba = ba;
                this.b = b;
                dim = opening.dimension;
                sill = opening.Y;
                p.dimension = q.dimension = step.dimension = bodyA.dimension = bodyC.dimension = dim;
                cur = (opening.X, opening.Y, opening.Z);
                pond.Clear(); walls.Clear(); filledSet.Clear(); pools.Clear(); others.Clear();
                filled.Clear(); drained.Clear();
                why = "";
                looked = 0;
                fell = false;
                unknown = false;
                chunks.Clear();
            }

            /// <summary>
            /// rc.11 (HY-0009): the path reaches the end of the search or unloaded land. Water that has already fallen from
            /// the opening's level is running down an open valley, which on Earth leads to a river or the sea (the author: most
            /// dry valleys are open, most closed ones are lakes already): a pond's water runs off there, as into the sea. Water
            /// still at the opening's level has found no lower land: it stays. A lake of <see cref="AbsorbBody"/> sources or
            /// more keeps its water too: it feeds the channel dug from it (decision 0008), which carries its water to the drop
            /// and fills the reach below, while a drain's stream is only vanilla's flowing water, dry a few blocks from the
            /// lake (the reach labs on the first rc.11 build).
            /// </summary>
            End Beyond(string what)
            {
                if (!fell) { why = what; return End.Kept; }
                if (pond.Count >= AbsorbBody) { why = $"{what}; a lake feeding its channel"; return End.Kept; }
                why = $"the flow down the open valley ({what})";
                return End.Absorbed;
            }

            /// <summary>A drain took <see cref="pond"/> and <see cref="filled"/>: the next search makes its own.</summary>
            public void Release()
            {
                pond = new();
                filled = new();
            }

            (int x, int y, int z) cur;

            /// <summary>
            /// Collects the body of <paramref name="from"/>'s liquid joined to it into <paramref name="into"/>; false when it is
            /// unlimited (<paramref name="cap"/> sources or more, or a natural river's own water), with <see cref="why"/> set.
            /// rc.11 (EA-0066, decision 0009): a river feeds water up to its own surface. River water is never part of the body;
            /// it makes the body unlimited where it meets a cell outside the river's columns (a channel dug from the river) at
            /// or above <paramref name="riverFrom"/> (the opening's level). Under or beside water raised over the river's own
            /// columns it is the body's floor and feed: water a dam raised drains when the dam opens, and the river stays.
            /// rc.12 (the in-game look at a dam below a step of the Rhine): a body that holds water raised over the river's own
            /// columns is a dam's water even where it also covers bank cells beside the river at or above the opening (the
            /// strip between a levee and the river fills to the upstream pool's level): only a body wholly outside the river's
            /// columns that meets river water at or above the opening is a channel dug from the river, fed for ever.
            /// </summary>
            public bool Body(BlockPos from, HashSet<(int x, int y, int z)> into, int cap, int riverFrom = int.MinValue)
            {
                var f0 = Liquids.Fluid(ba, from);
                var queue = bodyQueue;
                queue.Clear();
                into.Add((from.X, from.Y, from.Z));
                queue.Enqueue((from.X, from.Y, from.Z));
                bool river = EarthRiverQuery.Active && dim == 0;
                var floor = riverFloor;
                floor.Clear();
                var a = bodyA;
                var c = bodyC;
                if (river && EarthRiverQuery.RiverWater(from)) { why = "river water"; return false; }
                // the pond's body (a drain's opening level given): a channel fed by the river, or a dam's water over it
                bool pondBody = riverFrom != int.MinValue, fedChannel = false, overRiver = false;
                while (queue.Count > 0)
                {
                    var (x, y, z) = queue.Dequeue();
                    a.Set(x, y, z);
                    bool aRiver = river && pondBody && EarthRiverQuery.RiverColumn(a);
                    if (aRiver) overRiver = true;
                    var aSolid = Liquids.Solid(ba, a); // read once for the six faces (F13)
                    foreach (var face in BlockFacing.ALLFACES)
                    {
                        c.Set(a).Add(face);
                        if (c.Y < 0 || into.Contains((c.X, c.Y, c.Z))) continue;
                        // HY-0013: an unloaded neighbour reads as air: the body cannot be measured (it may be a lake)
                        if (!ChunkLoaded(c)) { unknown = true; why = "unloaded land"; return false; }
                        if (!Liquids.Passes(ba, aSolid, a, c, face)) continue;
                        var f = Liquids.Fluid(ba, c);
                        if (!Liquids.IsSource(f) || !Liquids.Same(f, f0)) continue;
                        if (river && (floor.Contains((c.X, c.Y, c.Z)) || EarthRiverQuery.RiverWater(c)))
                        {
                            if (c.Y >= riverFrom && !(pondBody ? aRiver : EarthRiverQuery.RiverColumn(a)))
                            {
                                if (!pondBody) { why = "river water"; return false; }
                                fedChannel = true;
                            }
                            // the river under raised water: its floor
                            floor.Add((c.X, c.Y, c.Z));
                            continue;
                        }
                        if (into.Count >= cap) { why = "a large body of water"; large = true; return false; }
                        into.Add((c.X, c.Y, c.Z));
                        queue.Enqueue((c.X, c.Y, c.Z));
                    }
                }
                looked += into.Count;
                if (fedChannel && !overRiver) { why = "river water"; return false; }
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
                seeds.Clear();
                seeds.Add(cur);
                while (true)
                {
                    region.Clear();
                    var r = Layer(seeds, region, v, out var spill, out bool closed);
                    if (r != End.Fits) return r;
                    if (spill.HasValue)
                    {
                        // the level leaks at its nearest edge: fall from there to whatever holds water
                        var (x, y, z) = spill.Value;
                        fell = true;
                        while (true)
                        {
                            if (y - 1 < 1) { why = "the world's floor"; return End.Kept; }
                            if (!Loaded(x, y - 1, z)) return Beyond("unloaded land");
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
                            if (++looked > MaxSearch) return Beyond("open land beyond the search");
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
                    if (!closed) return Beyond("open land beyond the search");
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
                // Fable's pass: another liquid keeps the water, however large it is (it was absorbed by a large body of it)
                if (!Liquids.Same(f, b)) { why = "another liquid"; return End.Kept; }
                pool.Clear();
                if (!Body(p, pool, AbsorbBody)) return unknown ? Beyond("unloaded land") : End.Absorbed;
                if (looked > MaxSearch) return Beyond("open land beyond the search");
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
                seen.Clear();
                var queue = layerQueue;
                queue.Clear();
                foreach (var c in seeds)
                    if (seen.Add(c)) queue.Enqueue((c, 0));
                while (queue.Count > 0)
                {
                    var (c, d) = queue.Dequeue();
                    if (d > limit) { closed = false; continue; }
                    if (!Loaded(c.x, c.y, c.z)) return Beyond("unloaded land");
                    if (!Supported(c.x, c.y, c.z))
                    {
                        // an edge, or water under it to land on
                        spill = c;
                        return End.Fits;
                    }
                    region.Add(c);
                    // the pool is complete: look on only as far as its flowing water runs
                    if (!atSill && region.Count == water) limit = d + MaxRun;
                    if (++looked > MaxSearch) return Beyond("open land beyond the search");
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
                            pool.Clear();
                            if (!Body(p, pool, AbsorbBody)) return unknown ? Beyond("unloaded land") : End.Absorbed;
                            others.UnionWith(pool);
                            if (looked > MaxSearch) return Beyond("open land beyond the search");
                            continue;
                        }
                        // over a pool it landed on the water runs on as a stream: only dry land counts towards the reach
                        // (a dam's top taken away: the raised water runs along the reach below to its outfall, rc.9)
                        queue.Enqueue((m, pools.Contains((c.x, c.y - 1, c.z)) ? d : d + 1));
                    }
                }
                return End.Fits;
            }
        }
    }
}
