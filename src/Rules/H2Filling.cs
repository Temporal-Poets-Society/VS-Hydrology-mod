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
    /// flowing water only, 6 blocks, as in vanilla. The ground rule fills only on solid ground: Earth's river steps have no
    /// lip, so a cell over a lower pool has the same neighbours as the top layer of a 2-deep trench; which water may fill
    /// over water needs Earth's river query.
    ///
    /// The rest needs the vessels case on (<see cref="VesselsActive"/>, the world's Water flooding): the vessels case fills
    /// on supported water; the uneven basin seeds lower layers from wide connected sheets outside natural river columns (the
    /// query tells fresh river water apart); enclosed pockets fill (<see cref="Pockets"/>), in river columns too. A pocket
    /// can be a single cell: a 1-wide ditch dug outward from water fills cell by cell; a dead-end ditch dug first and opened
    /// last fills if it is at most 6 blocks long, and a longer one keeps flowing water (its dry far end, beyond the 6 blocks
    /// of flowing water, is ground and ends the pocket). Refill (<see cref="RefillOnSource"/>) needs no vessels. The order
    /// in <see cref="TryFill"/> only decides which counter a fill counts in: every branch makes the same source.
    ///
    /// Every fill but refill and the pocket (enclosed by its own test) also needs its level held and fed (H2Held.cs,
    /// decisions 0005, 0006, 0008): water stands at a level only where walls and water of that level close it in, and fills
    /// a space only from water that could fill it.
    ///
    /// History: Earth decision 0024 allowed filling "on a source" too; filling on water flooded touched steps and falls in
    /// the Earth server test of 22 October 2026, so the ground rule fills on solid ground only. The uneven basin came in
    /// 0.1.12 (decision 0001), the pockets in 0.6.2, held levels in rc.3 (0005), supply in rc.7 (0006), fed channels in rc.9
    /// (0008).
    /// </summary>
    public static partial class H2Filling
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

        /// <summary>
        /// Decision 0011 (option 3): a world without <see cref="FloodingKey"/> (an old save, or a new world a dedicated server
        /// created without listing it) gets the attribute written with the legacy policy's answer, so the setting shows in
        /// the world settings and can be changed there. Called once the river query is bound (the legacy answer may depend on
        /// it); the value is what the world already did, so nothing changes. A world that has the attribute is left alone, and
        /// so is one whose legacy answer needs the query while the query is not bound.
        /// Returns the value written, or null when the world already had one.
        /// </summary>
        public static bool? WriteMissingFlooding(IWorldAccessor world)
        {
            var config = world.Config;
            if (config == null || config.HasAttribute(FloodingKey)) return null;
            // Fable's re-review (low 7): a world that asks for vessels only with Earth's river query, at a start where the query
            // is not bound (it failed to bind this time), would get "off" written for good; it is written at a start where the
            // query is bound
            if (!EarthRiverQuery.Active && config.GetAsBool(WorldVesselsWithQueryKey, false)) return null;
            ForgetWorldVessels();
            bool legacy = VesselsActive(world);
            config.SetBool(FloodingKey, legacy);
            return legacy;
        }

        /// <summary>Earth's query was unbound at run time: the remembered world vessels answer is read again at the next call
        /// (now without the query). Only two field writes, safe inside a liquid update.</summary>
        public static void ForgetWorldVessels()
        {
            worldVessels = false;
            worldVesselsCheckedMs = long.MinValue;
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
            // a pond draining through an opening: the cells it has emptied stay empty while it runs (rc.9)
            if (H2Release.Draining(pos)) return false;
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
            var source = Liquids.SourceOf(b);
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

        // HY-0017 (finding 9): scratch per helper; none of these calls another that takes the same one
        [System.ThreadStatic] static BlockPos besideN, pocketP, pocketN, basinP, basinSide, basinAcross, basinDiagonal, basinPrevious, depthP, containedN;

        static BlockPos Scratch(ref BlockPos s, BlockPos pos) { s ??= new BlockPos(0); s.dimension = pos.dimension; s.Set(pos); return s; }

        static bool SourceBeside(IBlockAccessor ba, BlockPos pos, Block b)
        {
            var n = Scratch(ref besideN, pos);
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
            var p = Scratch(ref pocketP, pos);
            var n = Scratch(ref pocketN, pos);
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
            if (!Liquids.IsSalt(b) && (!EarthRiverQuery.Active || pos.dimension != 0)) return false;
            // rc.11 (EA-0066, decision 0009): over a river a player has dammed, only the river's own water, up to its generated
            // surface, is left alone; a level the dam holds above it fills like any held level (decision 0005; the river feeds
            // it). Elsewhere a river column is left alone as before: a natural river whose end is closed does not rise.
            if (EarthRiverQuery.Active && EarthRiverQuery.River(pos) && !(H2Release.Dammed(pos) && !EarthRiverQuery.RiverWater(pos))) return false;
            var below = Liquids.Fluid(ba, Liquids.Below(pos));
            if (!Liquids.GroundBelow(ba, pos) && !(Liquids.IsSource(below) && Liquids.Same(below, b))) return false;

            var p = Scratch(ref basinP, pos);
            var side = Scratch(ref basinSide, pos);
            var across = Scratch(ref basinAcross, pos);
            var diagonal = Scratch(ref basinDiagonal, pos);
            var previous = Scratch(ref basinPrevious, pos);
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
                    // the cell's own level must hold the water it lands in (a fall into the open sea does not), fed by the
                    // water above the sheet (HY-0008, rc.9: the supply follows the fall upstream; rc.7 measured the space up
                    // to the sheet's top, so a reach on a slope, whose banks stand below the water above, read as open)
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
            var p = depthP ??= new BlockPos(0);
            p.dimension = dim;
            p.Set(x, y, z);
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
            var n = Scratch(ref containedN, pos);
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
