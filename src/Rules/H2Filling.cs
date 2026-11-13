using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// H2: filling. A flowing cell resting on solid ground becomes a source of its liquid (fresh or salt) when two of its
    /// four neighbours are sources of that liquid, or one neighbour is and so is the diagonal cell beside it, with the
    /// remaining cell of that 2 × 2 square holding the same liquid (the diagonal case).
    ///
    /// The new source wakes itself and its neighbours, so the fill runs on: a 2-wide trench fills to its end whether it was
    /// opened last or dug outward. In a 1-wide channel the 2 × 2 square always contains a wall, so by this rule it carries
    /// flowing water only, 6 blocks, as in vanilla.
    ///
    /// The original ground rule fills only on solid ground (0024 also allowed "or on a source"): Earth's river steps have no lip, so the
    /// upper pool's lower layer touches the lower pool, and a cell over the lower pool then has the same neighbours as the
    /// top layer of a 2-deep trench. Filling on water flooded touched steps and falls in the Earth server test
    /// (2026-10-22), against H3 and against Earth's pool levels. Which water may fill over water needs Earth's river
    /// query (0024 section 4).
    ///
    /// The rest needs the vessels case on (<see cref="VesselsActive"/>, the world's Water flooding): the vessels case fills on
    /// supported water; 0.1.12 seeds lower layers from wide connected sheets outside natural river columns, where the query
    /// supplies that distinction for fresh water; 0.6.2 fills enclosed pockets (<see cref="Pockets"/>), in river columns
    /// too. A pocket can be a single cell: a 1-wide ditch dug outward from water fills cell by cell; a dead-end ditch dug
    /// first and opened last fills if it is at most 6 blocks long, and a longer one keeps flowing water (its dry far end,
    /// beyond the 6 blocks of flowing water, is ground and ends the pocket).
    /// Refill (<see cref="RefillOnSource"/>) needs no vessels. The order in <see cref="TryFill"/> only decides which counter
    /// a fill counts in: every branch makes the same source.
    ///
    /// Every fill but refill and the pocket (enclosed by its own test) also needs its level held (<see cref="Held"/>):
    /// water stands at a level only where walls and water of that level close it in.
    /// </summary>
    public static class H2Filling
    {
        public static bool Enabled = true;
        /// <summary>
        /// Communicating vessels (0.1.4, off by default; study 2026-10-23): a flowing cell resting on a source of its liquid
        /// fills when a source beside it also rests on a source of that liquid. The 2 x 2 vertical square under and beside the
        /// cell is then all source water, one body, so the layer above takes its level: a canal or port 2 or more deep, dug
        /// from deep water, fills layer by layer up to the water's surface. A 1-wide channel keeps flowing water (its bottom
        /// layer never fills, so nothing above it rests on a source).
        ///
        /// Earth 0.16.x's 1-block river steps have no sill: the upper pool's lower layer touches the lower pool, so the ramp
        /// cell below a step looks the same and the step floods. Use this only on worlds whose steps have a sill under the
        /// upper pool's edge, or none (vanilla worlds); the study's simulator shows steps with a sill keep their shape.
        /// </summary>
        public static bool Vessels = false;

        /// <summary>
        /// 0.1.7: the vessels case fills only from water at least as deep as the water under the cell, and only a cell with
        /// no dry ledge beside it. A player who broke the sill under an Earth step's edge (2026-10-24, Congo rapids) opened a
        /// one-block hole under the upper pool's edge; the ramp cell beside it then had a source on a source beside it, and
        /// the lower pool rose by one block from ramp cell to ramp cell, over its flush banks, down to the next step. A canal
        /// fills from deeper water and is held by its walls; the hole is shallower than the pool below the step, and a river's
        /// flush banks are dry ledges. Off: as 0.1.4 to 0.1.6.
        /// </summary>
        public static bool VesselsDepth = true, VesselsContained = true;

        // 0.1.8 (decision Earth 0027): no vessels fill in a column Earth's river query calls natural river water. Earth's
        // layout 8 draws its steps without sills, so the upper pool's lower layer touches the lower pool, and the ramp cell
        // below a step meets the vessels case; refusing it in river columns keeps the steps as generated. Always on where
        // the query answers (the config's QueryNoVessels=false is ignored there: the steps would flood). The one exception
        // since 0.6.2 is an enclosed pocket (TryFillPocket), which a step's ramp never is. Canals and ports dug off a river
        // are not river columns and fill as before. Without the query (no Earth, older layouts) nothing changes.

        /// <summary>The world-config attribute may turn the vessels case on (config WorldMayFillVessels; true by default).</summary>
        public static bool WorldMayFillVessels = true;

        /// <summary>
        /// Refill (0.1.5, study 2026-10-23): a flowing cell resting on a source of its liquid becomes a source when at least
        /// this many of its four sides are sources (0: off). Vanilla has the same rule with 3, but reaches it only when no
        /// downward path is near, so a lip cell knocked out beside a fall never came back.
        /// </summary>
        public static int RefillOnSource = 3;

        /// <summary>The world-config attribute Earth sets on worlds whose river steps have a sill (Earth 0.16.8, pool layout 5).</summary>
        public const string WorldVesselsKey = "hydrologyFillVessels";

        /// <summary>
        /// The world-config attribute Earth sets instead on worlds with its river query (layout 8, steps without sills;
        /// decision Earth 0027). It counts only where the query answers, so a Hydrology older than 0.1.8, which does not know
        /// it, keeps vessels off there (it would flood the sill-less steps).
        /// </summary>
        public const string WorldVesselsWithQueryKey = "hydrologyFillVesselsWithRiverQuery";
        /// <summary>User-facing per-world override. Missing means the complete legacy vessels policy applies.</summary>
        public const string FloodingKey = "hydrologyFlooding";
        static bool worldVessels;
        static long worldVesselsCheckedMs = long.MinValue;

        /// <summary>
        /// The vessels case is on when the config says so, or when the world says so (Earth sets the attribute while it
        /// builds its parameters, after Hydrology has started, so it is read lazily: once per second until it is found).
        /// </summary>
        public static bool VesselsActive(IWorldAccessor world)
        {
            // Check presence, not a default of true: old saves must retain their config and Earth safety gates.
            // An explicit false must also win over both the mod config and a cached legacy true.
            if (world.Config?.HasAttribute(FloodingKey) == true)
            {
                // rc.4 (audit): an Earth world that asks for vessels only with its river query (steps without sills) keeps
                // them off while the query is not bound (before the game runs, or a later Earth whose query no longer binds):
                // vessels in its river water would flood the steps
                bool on = world.Config.GetAsBool(FloodingKey, false);
                if (on && !EarthRiverQuery.Active && world.Config.GetAsBool(WorldVesselsWithQueryKey, false)) return false;
                return on;
            }
            if (Vessels || worldVessels) return true;
            if (!WorldMayFillVessels) return false;
            long now = world.ElapsedMilliseconds;
            // the first call always reads (the sentinel would overflow the subtraction)
            if (worldVesselsCheckedMs != long.MinValue && now - worldVesselsCheckedMs < 1000) return false;
            worldVesselsCheckedMs = now;
            worldVessels = (world.Config?.GetAsBool(WorldVesselsKey, false) ?? false)
                || (EarthRiverQuery.Active && (world.Config?.GetAsBool(WorldVesselsWithQueryKey, false) ?? false));
            return worldVessels;
        }

        /// <summary>Forget the world's answer (a new world in the same process, singleplayer).</summary>
        public static void ResetWorld()
        {
            worldVessels = false;
            worldVesselsCheckedMs = long.MinValue;
            ForgetHeld();
        }

        static readonly BlockFacing[] H = BlockFacing.HORIZONTALS;

        public static bool TryFill(IWorldAccessor world, BlockPos pos, Block b)
        {
            if (!Enabled || Liquids.IsSource(b) || !Liquids.Handles(b)) return false;
            var ba = world.BlockAccessor;
            if (!Liquids.GroundBelow(ba, pos))
            {
                if (TryRefill(world, ba, pos, b)) return true;
                if (!VesselsActive(world)) return false;
                // the vessels case tries the pocket on each of its refusals: only there is the cell known to rest on a source
                if (TryFillVessel(world, ba, pos, b) || TryFillUnevenBasin(world, ba, pos, b)) return true;
                WakePocketBelow(world, ba, pos, b);
                return false;
            }

            // bit i: side H[i] holds water of the cell's liquid (wet), or a source of it (src), and water passes between it and
            // the cell (0.6.2: water behind a liquid-tight face, such as a waterlogged stair's back, feeds nothing)
            int src = 0, wet = 0;
            int sources = 0;
            var n = Liquids.Side2(pos);
            for (int i = 0; i < 4; i++)
            {
                n.Set(pos).Add(H[i]);
                var nb = Liquids.Fluid(ba, n);
                if (!Liquids.Same(nb, b) || !Liquids.Passes(ba, pos, n, H[i])) continue;
                wet |= 1 << i;
                if (Liquids.IsSource(nb)) { src |= 1 << i; sources++; }
            }

            bool fill = sources >= 2;
            // HORIZONTALS run N, E, S, W: consecutive entries are at right angles and share a diagonal corner
            for (int i = 0; i < 4 && !fill; i++)
            {
                int j = (i + 1) % 4;
                bool srcI = (src >> i & 1) != 0, srcJ = (src >> j & 1) != 0, wetI = (wet >> i & 1) != 0, wetJ = (wet >> j & 1) != 0;
                if (!(srcI && wetJ) && !(srcJ && wetI)) continue;
                n.Set(pos).Add(H[i]).Add(H[j]);
                var db = Liquids.Fluid(ba, n);
                // the diagonal source joins the square through one of the two sides
                fill = Liquids.IsSource(db) && Liquids.Same(db, b) && DiagonalJoins(ba, pos, n, i, j);
            }
            if (!fill) return VesselsActive(world) && (TryFillUnevenBasin(world, ba, pos, b) || TryFillPocket(world, ba, pos, b));
            // not held: the basin and pocket cases fill the same cell at the same level, so they would be refused too
            if (!Held(ba, pos, b)) return false;

            Fill(world, pos, b);
            return true;
        }

        [System.ThreadStatic] static BlockPos sideI, sideJ;

        /// <summary>Water passes between the diagonal cell <paramref name="d"/> and at least one of the two sides it shares
        /// with <paramref name="pos"/> (on <see cref="H"/>[i] and [j]).</summary>
        static bool DiagonalJoins(IBlockAccessor ba, BlockPos pos, BlockPos d, int i, int j)
        {
            var si = sideI ??= new BlockPos(0);
            var sj = sideJ ??= new BlockPos(0);
            si.Set(pos); si.dimension = pos.dimension; si.Add(H[i]);
            sj.Set(pos); sj.dimension = pos.dimension; sj.Add(H[j]);
            return Liquids.Passes(ba, si, d, H[j]) || Liquids.Passes(ba, sj, d, H[i]);
        }

        static void Fill(IWorldAccessor world, BlockPos pos, Block b)
        {
            var source = b.LiquidCode == "saltwater" ? Liquids.SaltSource : Liquids.WaterSource;
            Liquids.SetAndWake(world, pos, source.Id);
            HydrologyStats.Filled++;
            H4Rapids.WakeEdgesOnNewSource(world, pos);
        }

        /// <summary>Refill: resting on a source of its liquid, with <see cref="RefillOnSource"/> or more source sides.</summary>
        static bool TryRefill(IWorldAccessor world, IBlockAccessor ba, BlockPos pos, Block b)
        {
            if (RefillOnSource <= 0) return false;
            var below = Liquids.Fluid(ba, Liquids.Below(pos));
            if (!Liquids.IsSource(below) || !Liquids.Same(below, b)) return false;
            int sources = 0;
            var n = Liquids.Side2(pos);
            for (int i = 0; i < 4; i++)
            {
                var f = Liquids.Fluid(ba, n.Set(pos).Add(H[i]));
                if (Liquids.IsSource(f) && Liquids.Same(f, b) && Liquids.Passes(ba, pos, n, H[i])) sources++;
            }
            if (sources < RefillOnSource) return false;
            Fill(world, pos, b);
            HydrologyStats.Refilled++;
            return true;
        }

        /// <summary>The vessels case: resting on a source, with a side source that rests on a source too (0.1.7: as deep as
        /// the water under the cell, and the cell has no dry ledge beside it).</summary>
        static bool TryFillVessel(IWorldAccessor world, IBlockAccessor ba, BlockPos pos, Block b)
        {
            var below = Liquids.Fluid(ba, Liquids.Below(pos));
            if (!Liquids.IsSource(below) || !Liquids.Same(below, b)) return false;
            if (EarthRiverQuery.Active && EarthRiverQuery.River(pos))
            {
                if (TryFillPocket(world, ba, pos, b)) return true;
                HydrologyStats.VesselsInRiverRefused++;
                return false;
            }
            if (VesselsContained && !Contained(ba, pos, b)) return TryFillPocket(world, ba, pos, b);
            int depth = -1;
            var n = Liquids.Side2(pos);
            for (int i = 0; i < 4; i++)
            {
                n.Set(pos).Add(H[i]);
                var side = Liquids.Fluid(ba, n);
                if (!Liquids.IsSource(side) || !Liquids.Same(side, b) || !Liquids.Passes(ba, pos, n, H[i])) continue;
                var sideBelow = Liquids.Fluid(ba, n.Down());
                if (!Liquids.IsSource(sideBelow) || !Liquids.Same(sideBelow, b)) continue;
                if (VesselsDepth)
                {
                    if (depth < 0) depth = SourceDepth(ba, pos.X, pos.Y - 1, pos.Z, pos.dimension, b);
                    if (SourceDepth(ba, n.X, n.Y, n.Z, pos.dimension, b) < depth) continue;
                }
                // a pocket is held by its own test, so a level that is not held is no pocket either
                if (!Held(ba, pos, b)) return false;
                Fill(world, pos, b);
                HydrologyStats.FilledVessels++;
                return true;
            }
            if (depth >= 0) HydrologyStats.VesselsRefused++;
            return TryFillPocket(world, ba, pos, b);
        }

        /// <summary>
        /// 0.6.2 (the author's playtests at the Nile, 2026-11-03), with the vessels case on: a flowing cell on a source or on
        /// its floor still fills when it belongs to an enclosed pocket (<see cref="Pocket"/>), in natural river water too.
        /// A basin dug beside the Nile filled from its floor up except where it overlapped the river's columns: there the
        /// river refusal left a pocket of flowing water 3 deep in the river's own pool, which kept the bank's sources rapid
        /// edges (a drop of 3 beside them) for good. A pit dug into the delta's river bed left a nook a block wide and 10 to
        /// 14 deep beside the river, outside its columns: the ground rule keeps a 1-wide channel flowing and the vessels
        /// case needs a 2 x 2 square, so it stayed rapid flowing water. A 1-wide ditch enclosed by water now fills too.
        /// </summary>
        public static bool Pockets = true;

        /// <summary>
        /// A pocket fills from its lowest layer up, and only that layer rests on sources or its floor. A flowing cell with a
        /// source of its liquid beside it, over flowing water of that liquid, wakes the cell below it (it fills nothing
        /// itself), so a block placed and broken at the surface of a pocket left by an older version reaches the layer that
        /// can fill. It also wakes the first cell beyond a waterfall's lip and the curtain under it (audit A2 took that for
        /// wasted work, but narrowing it to cells that can fill left rapid tongues on deep canal floors unsettled, 1 to 2
        /// cells short, in the headless labs: the wake also settles them, so it stays as in 0.6.2-rivers.6).
        /// </summary>
        static void WakePocketBelow(IWorldAccessor world, IBlockAccessor ba, BlockPos pos, Block b)
        {
            if (!Pockets) return;
            var below = wakeScratch ??= new BlockPos(0);
            below.Set(pos); below.dimension = pos.dimension; below.Down();
            var f = Liquids.Fluid(ba, below);
            if (!Liquids.Same(f, b) || Liquids.IsSource(f) || !Liquids.Passes(ba, pos, below, BlockFacing.DOWN)
                || !SourceBeside(ba, pos, b)) return;
            f.OnNeighbourBlockChange(world, below.Copy(), pos);
        }

        [System.ThreadStatic] static BlockPos wakeScratch;

        /// <summary><see cref="Pockets"/>: the cell becomes a source when it belongs to an enclosed pocket. Only a cell with a
        /// source of its liquid beside it looks (the edge of a body of water); the cells inside a pocket follow as its edge
        /// fills. The caller has made sure the cell itself rests on a source of its liquid or on the ground, as
        /// <see cref="Pocket"/> requires of every other cell: called elsewhere it would fill water H7 then drains.</summary>
        static bool TryFillPocket(IWorldAccessor world, IBlockAccessor ba, BlockPos pos, Block b)
        {
            if (!Pockets || !SourceBeside(ba, pos, b) || !Pocket(ba, pos, b)) return false;
            Fill(world, pos, b);
            HydrologyStats.FilledPockets++;
            return true;
        }

        static bool SourceBeside(IBlockAccessor ba, BlockPos pos, Block b)
        {
            var n = new BlockPos(pos.dimension);
            for (int i = 0; i < 4; i++)
            {
                var side = Liquids.Fluid(ba, n.Set(pos).Add(H[i]));
                if (Liquids.IsSource(side) && Liquids.Same(side, b) && Liquids.Passes(ba, pos, n, H[i])) return true;
            }
            return false;
        }

        /// <summary>Cells at one level a pocket may span (<see cref="Pocket"/>).</summary>
        const int MaxPocket = 128;
        [System.ThreadStatic] static HashSet<(int, int)> pocketSeen;
        [System.ThreadStatic] static Queue<(int x, int z)> pocketQueue;

        /// <summary>
        /// The cell is part of a pocket in a body of water: the open cells at its level that are not sources of its liquid,
        /// reached through each other, are at most <see cref="MaxPocket"/>, every one rests on a source of that liquid (or,
        /// holding water, on the ground), and all around them are sources of it or liquid-tight walls (a block that water
        /// passes into, such as a fence or a slab, is a leak, not a wall). A river step's ramp never is: at
        /// its level the lower pool runs on to the next drop, where a cell rests on flowing water or air, and out over its
        /// flush banks, dry ground. A dry cell on the ground is land, not part of a body of water, even where walls close it
        /// in (Hydrology's offline river test, whose last pool has no outflow, floods its steps otherwise).
        /// </summary>
        static bool Pocket(IBlockAccessor ba, BlockPos pos, Block b)
        {
            // the search's sets, reused (one update at a time on the server's main thread; Pocket never nests)
            var seen = pocketSeen ??= new HashSet<(int, int)>();
            var queue = pocketQueue ??= new Queue<(int x, int z)>();
            seen.Clear();
            queue.Clear();
            seen.Add((pos.X, pos.Z));
            queue.Enqueue((pos.X, pos.Z));
            var p = new BlockPos(pos.dimension);
            var n = new BlockPos(pos.dimension);
            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    p.Set(x, pos.Y, z);
                    n.Set(p).Add(H[i]);
                    // a liquid-tight face is a wall
                    if (seen.Contains((n.X, n.Z)) || !Liquids.Passes(ba, p, n, H[i])) continue;
                    var f = Liquids.Fluid(ba, n);
                    if (Liquids.Same(f, b) && Liquids.IsSource(f)) continue;
                    // a block water passes into that is no open cell (a fence, a slab, a waterlogged beam with no source in
                    // it): the water leaks through it, as vanilla spreads into it, so the pocket is not enclosed
                    if (!Liquids.Open(ba, n)) return false;
                    if (f.Id != 0 && !Liquids.Same(f, b) || seen.Count >= MaxPocket) return false;
                    var below = Liquids.Fluid(ba, Liquids.Below(n));
                    if (!(Liquids.IsSource(below) && Liquids.Same(below, b)) && !(f.Id != 0 && Liquids.GroundBelow(ba, n))) return false;
                    seen.Add((n.X, n.Z));
                    queue.Enqueue((n.X, n.Z));
                }
            }
            return true;
        }

        /// <summary>Open cells <see cref="Held"/> follows at the cell's own level (the surface a basin would take), and in all
        /// (its depths too); beyond either the water counts as not held.</summary>
        const int MaxHeldLevel = 4096, MaxHeldSpace = 16384;
        /// <summary>How long a search's answer stands, in milliseconds: a held answer for every cell the search covered (the
        /// next fills in the same basin need no search of their own), a refusal for the cell asked (it is not searched again
        /// at once, as when the vessels case and the uneven basin both ask about it in one update).</summary>
        const long HeldMemoMs = 20000, UnheldMemoMs = 2000;
        [System.ThreadStatic] static HashSet<(int, int, int)> heldSeen;
        [System.ThreadStatic] static Queue<(int x, int y, int z)> heldQueue;
        // (dimension, x, y, z) -> (until, world generation); the main server thread only, as every H2 call
        static readonly Dictionary<(int, int, int, int), (long until, int gen)> heldMemo = new(), unheldMemo = new();
        static int heldGeneration;

        /// <summary>
        /// 0.6.2-rc.3 (bugfix; a Mod DB report, 8 November 2026: a source that spilled into the sea made the sea climb to it
        /// and flood the map): water is made to stand at the cell's level only where something holds it there. The open
        /// space at or below that level reached from the cell through faces water passes (dry or wet cells, but no source of
        /// its liquid, which is water already held) must be closed by liquid-tight walls and sources, within
        /// <see cref="MaxHeldLevel"/> cells at the level and <see cref="MaxHeldSpace"/> in all: more space than that is an
        /// outlet (a sea, open land draining down a hillside). A source of another liquid is a wall, as vanilla spreads into
        /// none (a canal from a river to the salt sea fills from each end); flowing water of another liquid and a block water
        /// passes into (a fence, a slab, a water wheel) are part of the space, and what lies beyond them decides. So a
        /// trench, a canal with a pit in its bed, a port or a walled basin is held, and its water rises to the level of the
        /// water it is dug from; the sea, a lake with an outflow or open shores, a field whose edge drops away down a
        /// hillside, a floor open to a cave below are not. A pond in a hollow closed above its surface is held: a source
        /// over it fills the hollow to the source's level, as vanilla's endless sources would.
        ///
        /// rc.4: the level and the whole space have their own limits (a canal 3 x 3 x 500 dug out and then opened was
        /// refused at its surface, counted with its unfilled depths, and never asked again); answers are remembered
        /// (filling one basin searched it again for every cell: 41 million block reads for a 63 x 63 layer, Codex's
        /// adversarial review); another liquid's source was an outlet.
        ///
        /// Before, any source stood for a body of water at its level: a bucket emptied onto the sea or a lake raised all of
        /// it by a block (the vessels case, cell by cell from the bucket), a pool two wide falling over a cliff seeded each
        /// layer under its sheet (the uneven basin) so the sea climbed to the pool, and two sources placed on open flat
        /// land flooded all of it at their level (the ground rule). Each was reproduced in the headless lab (lab_spill),
        /// on 0.6.1 as on 0.6.2-rc.2.
        /// </summary>
        static bool Held(IBlockAccessor ba, BlockPos pos, Block b)
        {
            long now = System.Environment.TickCount64;
            int gen = heldGeneration;
            var key = (pos.dimension, pos.X, pos.Y, pos.Z);
            if (heldMemo.TryGetValue(key, out var held) && held.gen == gen && held.until > now) return true;
            if (unheldMemo.TryGetValue(key, out var unheld) && unheld.gen == gen && unheld.until > now)
            {
                HydrologyStats.Unheld++;
                return false;
            }
            var seen = heldSeen ??= new HashSet<(int, int, int)>();
            var queue = heldQueue ??= new Queue<(int x, int y, int z)>();
            seen.Clear();
            queue.Clear();
            seen.Add((pos.X, pos.Y, pos.Z));
            queue.Enqueue((pos.X, pos.Y, pos.Z));
            int atLevel = 1;
            var p = new BlockPos(pos.dimension);
            var n = new BlockPos(pos.dimension);
            while (queue.Count > 0)
            {
                var (x, y, z) = queue.Dequeue();
                foreach (var face in BlockFacing.ALLFACES)
                {
                    p.Set(x, y, z);
                    n.Set(p).Add(face);
                    // nothing above the level; the world's floor and a liquid-tight face are walls
                    if (n.Y > pos.Y || n.Y < 0 || seen.Contains((n.X, n.Y, n.Z)) || !Liquids.Passes(ba, p, n, face)) continue;
                    var f = Liquids.Fluid(ba, n);
                    // a source of the liquid is water already held; a source of another liquid is a wall
                    if (Liquids.IsSource(f)) continue;
                    if ((n.Y == pos.Y && ++atLevel > MaxHeldLevel) || seen.Count >= MaxHeldSpace)
                    {
                        if (unheldMemo.Count > 4096) unheldMemo.Clear();
                        unheldMemo[key] = (now + UnheldMemoMs, gen);
                        HydrologyStats.Unheld++;
                        return false;
                    }
                    seen.Add((n.X, n.Y, n.Z));
                    queue.Enqueue((n.X, n.Y, n.Z));
                }
            }
            // every cell the search covered is held at its own height too: what it reaches below that is part of this space
            if (heldMemo.Count + seen.Count > 65536) heldMemo.Clear();
            foreach (var (x, y, z) in seen) heldMemo[(pos.dimension, x, y, z)] = (now + HeldMemoMs, gen);
            return true;
        }

        /// <summary>A new world in the same process (singleplayer): no remembered answer carries over.</summary>
        static void ForgetHeld() { System.Threading.Interlocked.Increment(ref heldGeneration); }

        static bool IsSourceOf(Block f, Block b) => Liquids.IsSource(f) && Liquids.Same(f, b);

        const int MaxDepthScan = 16;

        /// <summary>
        /// 0.1.12, issue #20: a two-wide sheet falling past a bed step has no sources at its landing level, so neither
        /// ordinary H2 nor the vessels rule can start the lower layer. Seed it from a source square at or above it,
        /// through continuous water, then fill upwards on that support. Never seed water in the air. Salt water may
        /// equalise; fresh water needs Earth's query to exclude natural river columns (older worlds keep their guards).
        /// The existing depth/containment guards remain on the original vessels path.
        /// </summary>
        static bool TryFillUnevenBasin(IWorldAccessor world, IBlockAccessor ba, BlockPos pos, Block b)
        {
            if (b.LiquidCode != "saltwater" && (!EarthRiverQuery.Active || pos.dimension != 0)) return false;
            if (EarthRiverQuery.Active && EarthRiverQuery.River(pos)) return false;
            var below = Liquids.Fluid(ba, pos.DownCopy());
            if (!Liquids.GroundBelow(ba, pos) && !(Liquids.IsSource(below) && Liquids.Same(below, b))) return false;

            var p = pos.Copy();
            var side = pos.Copy();
            var across = pos.Copy();
            var diagonal = pos.Copy();
            var previous = pos.Copy();
            for (int d = 0; d < MaxDepthScan; d++, p.Y++)
            {
                if (!Liquids.Open(ba, p) || !Liquids.Same(Liquids.Fluid(ba, p), b)) break;
                if (d > 0 && !Liquids.Passes(ba, previous.Set(p).Down(), p, BlockFacing.UP)) break;
                // A full 2 x 2 wet square with the far side made of sources. Unlike a single falling stream, it
                // carries the width needed by H2; no source is invented from flowing water alone.
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    side.Set(p).Add(H[i]);
                    across.Set(p).Add(H[j]);
                    diagonal.Set(side).Add(H[j]);
                    var s = Liquids.Fluid(ba, side);
                    var a = Liquids.Fluid(ba, across);
                    var ds = Liquids.Fluid(ba, diagonal);
                    if (!Liquids.Same(s, b) || !Liquids.Same(a, b)
                        || !(Liquids.IsSource(s) || Liquids.IsSource(a))
                        || !Liquids.IsSource(ds) || !Liquids.Same(ds, b)) continue;
                    if (!Liquids.Passes(ba, p, side, H[i]) || !Liquids.Passes(ba, p, across, H[j])
                        || !Liquids.Passes(ba, side, diagonal, H[j]) || !Liquids.Passes(ba, across, diagonal, H[i])) continue;
                    // the sheet's own level does not hold the water it lands in (a fall into the open sea)
                    if (!Held(ba, pos, b)) return false;
                    Fill(world, pos, b);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Sources of the cell's liquid straight down from (x, y, z), at most <see cref="MaxDepthScan"/>.</summary>
        static int SourceDepth(IBlockAccessor ba, int x, int y, int z, int dim, Block b)
        {
            var p = new BlockPos(x, y, z, dim);
            int d = 0;
            for (; d < MaxDepthScan; d++, p.Y--)
            {
                var f = Liquids.Fluid(ba, p);
                if (!Liquids.IsSource(f) || !Liquids.Same(f, b)) break;
            }
            return d;
        }

        /// <summary>No side of the cell is a dry ledge: each side is a block, water of its liquid, or open over water of its
        /// liquid (a canal running on).</summary>
        static bool Contained(IBlockAccessor ba, BlockPos pos, Block b)
        {
            var n = new BlockPos(pos.dimension);
            for (int i = 0; i < 4; i++)
            {
                n.Set(pos).Add(H[i]);
                if (!Liquids.Open(ba, n) || Liquids.Same(Liquids.Fluid(ba, n), b)) continue;
                n.Y--;
                if (!Liquids.Same(Liquids.Fluid(ba, n), b)) return false;
            }
            return true;
        }
    }
}
