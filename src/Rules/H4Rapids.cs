using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// H4: a fall of 2 or more over a short distance makes rapids (0024 section 1b, the user, 2026-10-22: rapids up to
    /// 4 blocks are chutes of 1-block steps, waterfalls from 5).
    /// - A fresh-water source at an edge becomes <c>rapidwater-still-7</c> when the water surface falls 2 or more blocks
    ///   within <see cref="Reach"/> steps between columns, across this edge's own step. A single drop of 2 or more (any
    ///   height, up to <see cref="MaxDropScan"/>) is the simplest case. In a chute of 1-block steps up to 3 blocks apart
    ///   (Earth's <c>ChuteSpacing</c>, measured along the river, so up to about 5 column steps on a diagonal river) every
    ///   step's edge is rapid, the last one too: the window may start upstream of the edge. A lone 1-block step stays
    ///   ordinary (H3): Earth keeps at least 32 blocks of pool on each side of one.
    /// - The flowing water it feeds follows its kind: falling rapid water down the drop (vertical faces belong here) and a
    ///   rapid ramp on the lower water or floor. Vanilla's rapid spreading draws that water; this rule only keeps each
    ///   flowing cell the kind of the water above or beside it that feeds it, because vanilla never lets rapid water
    ///   replace ordinary flowing water.
    /// - When the fall is filled back to less than 2 within the reach, the edge becomes ordinary source water again, and
    ///   its water follows. An edge that changes wakes the source edges within the reach, so a chute settles as a whole.
    /// - A lone rapid source with no source beside it (a vanilla rivulet spring) is never reverted.
    /// Salt water has no rapid block in vanilla, so salt-water drops stay vanilla.
    /// </summary>
    public static class H4Rapids
    {
        public static bool Enabled = true;
        /// <summary>0.1.5: a rapid source with a source beside it spreads like ordinary water (else one stream).</summary>
        public static bool RapidSourceSheets = true;
        /// <summary>0.1.5: a cell that has just become a source wakes the edges within <see cref="Reach"/>.</summary>
        public static bool WakeOnNewSource = true;
        /// <summary>0.1.5: a rapid source with a same-liquid source above or below it is not lone (it may revert).</summary>
        public static bool LoneVertical = true;
        public static int MaxDropScan = 384;
        public const int RapidDrop = 2;
        /// <summary>
        /// Column steps (4-neighbour) over which the surface fall is measured: a chute's steps 3 blocks apart along a
        /// diagonal river are up to 5 column steps apart, plus 1 to reach the water below the next step.
        /// </summary>
        public const int Reach = 6;
        /// <summary>Levels above and below a changed edge whose sources are woken: a chute is at most 4 steps.</summary>
        const int WakeDepth = 4;

        static readonly BlockFacing[] H = BlockFacing.HORIZONTALS;

        // IsEdge's search state, reused (the server's main thread runs one update at a time; IsEdge never nests)
        [ThreadStatic] static HashSet<(int, int)> downSeen, upSeen;
        [ThreadStatic] static Queue<(int x, int z, int level, bool wet, int dist)> downQueue;
        [ThreadStatic] static Queue<(int x, int z, int level, int dist)> upQueue;
        [ThreadStatic] static int[] downLevels, upLevels;
        [ThreadStatic] static BlockPos edgeScratch, dropScratch;

        /// <summary>
        /// How far water entering the open cell <paramref name="n"/> sideways falls before it lands: on lower water (its
        /// surface source) or on the floor. 0 when <paramref name="n"/> stands on the ground. <paramref name="onWater"/>: it
        /// lands on a source.
        /// </summary>
        public static int DropAt(IBlockAccessor ba, BlockPos n, out bool onWater)
        {
            onWater = false;
            var p = dropScratch ??= new BlockPos(0);
            p.Set(n);
            p.dimension = n.dimension;
            for (int d = 1; d <= MaxDropScan; d++)
            {
                p.Y--;
                if (p.Y < 1) return d - 1;
                if (!Liquids.Open(ba, p)) return d - 1;
                var f = Liquids.Fluid(ba, p);
                if (f.IsLiquid() && Liquids.IsSource(f)) { onWater = true; return d; }
            }
            return MaxDropScan;
        }

        /// <summary>
        /// Every chunk column <see cref="IsEdge"/> may read (<see cref="Reach"/> blocks around <paramref name="pos"/>) is
        /// loaded. The server's block accessor reads a column that is not loaded as air from top to bottom, which the window
        /// takes for a bottomless drop: a source updated near the loaded area's border (a pending update, or a wake reaching
        /// it) turned rapid and stayed so after the column loaded. One chunk lookup per column crossed, so usually one.
        /// </summary>
        static bool WindowLoaded(IBlockAccessor ba, BlockPos pos)
        {
            const int Size = GlobalConstants.ChunkSize;
            var p = loadScratch ??= new BlockPos(0);
            p.dimension = pos.dimension;
            int x0 = (pos.X - Reach) / Size, x1 = (pos.X + Reach) / Size, z0 = (pos.Z - Reach) / Size, z1 = (pos.Z + Reach) / Size;
            if (pos.X - Reach < 0 || pos.Z - Reach < 0) return false; // the world's edge: no answer either
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    p.Set(cx * Size, pos.Y, cz * Size);
                    if (ba.GetChunkAtBlockPos(p) == null) return false;
                }
            return true;
        }

        [ThreadStatic] static BlockPos loadScratch;

        /// <summary>
        /// The source at <paramref name="pos"/> is an edge (a side opens onto water or floor that is not a source) and the
        /// surface falls <see cref="RapidDrop"/> or more within <see cref="Reach"/> column steps, counting up to
        /// <c>Reach - 1</c> upstream and at least one downstream. Two bounded searches over columns: downstream, the level
        /// where water from the source lands at each distance; upstream, the highest water surface. The downstream path
        /// carries on through water and across dry ground at the same level (water spreads there anyway), but ends where
        /// water has fallen onto dry ground: a lone step's pool does not reach an unrelated drop beyond its bank.
        /// </summary>
        public static bool IsEdge(IBlockAccessor ba, BlockPos pos)
        {
            int y0 = pos.Y;
            var n = edgeScratch ??= new BlockPos(0);
            n.dimension = pos.dimension;

            // downstream: the lowest landing level at each path distance 1..Reach
            var down = downLevels ??= new int[Reach + 1];
            for (int d = 0; d <= Reach; d++) down[d] = int.MaxValue;
            var seen = downSeen ??= new HashSet<(int, int)>();
            var queue = downQueue ??= new Queue<(int x, int z, int level, bool wet, int dist)>();
            seen.Clear();
            queue.Clear();
            seen.Add((pos.X, pos.Z));
            int nearest = int.MaxValue; // the lowest landing level one step away: down[1] once the search has run
            for (int i = 0; i < 4; i++)
            {
                n.Set(pos).Add(H[i]);
                if (!Liquids.Open(ba, n) || !Liquids.Passes(ba, pos, n, H[i])) continue;
                var f = Liquids.Fluid(ba, n);
                if (f.IsLiquid() && Liquids.IsSource(f)) continue;
                seen.Add((n.X, n.Z));
                int drop = DropAt(ba, n, out bool onWater);
                queue.Enqueue((n.X, n.Z, y0 - drop, onWater || drop == 0, 1));
                nearest = Math.Min(nearest, y0 - drop);
            }
            if (queue.Count == 0) return false;
            // a drop right beside the edge decides it: the rest of the search only adds farther distances
            if (y0 - nearest >= RapidDrop) return true;
            while (queue.Count > 0)
            {
                var (x, z, level, wet, dist) = queue.Dequeue();
                down[dist] = Math.Min(down[dist], level);
                if (dist == Reach || !wet) continue;
                for (int i = 0; i < 4; i++)
                {
                    int bx = x + H[i].Normali.X, bz = z + H[i].Normali.Z;
                    if (seen.Contains((bx, bz))) continue;
                    n.Set(bx, level, bz);
                    if (!Liquids.Open(ba, n)) continue;
                    seen.Add((bx, bz));
                    var f = Liquids.Fluid(ba, n);
                    if (f.IsLiquid() && Liquids.IsSource(f)) queue.Enqueue((bx, bz, level, true, dist + 1));
                    else
                    {
                        int drop = DropAt(ba, n, out bool landsOnWater);
                        queue.Enqueue((bx, bz, level - drop, landsOnWater || drop == 0, dist + 1));
                    }
                }
            }
            for (int d = 2; d <= Reach; d++) down[d] = Math.Min(down[d], down[d - 1]);
            if (y0 - down[1] >= RapidDrop) return true;

            // upstream: the highest surface at each path distance 0..Reach-1, through water at or above each surface
            var up = upLevels ??= new int[Reach];
            for (int d = 0; d < Reach; d++) up[d] = y0;
            var b = Liquids.Fluid(ba, pos);
            var seenUp = upSeen ??= new HashSet<(int, int)>();
            var queueUp = upQueue ??= new Queue<(int x, int z, int level, int dist)>();
            seenUp.Clear();
            queueUp.Clear();
            seenUp.Add((pos.X, pos.Z));
            queueUp.Enqueue((pos.X, pos.Z, y0, 0));
            while (queueUp.Count > 0)
            {
                var (x, z, level, dist) = queueUp.Dequeue();
                up[dist] = Math.Max(up[dist], level);
                if (dist == Reach - 1) continue;
                for (int i = 0; i < 4; i++)
                {
                    int bx = x + H[i].Normali.X, bz = z + H[i].Normali.Z;
                    if (seenUp.Contains((bx, bz))) continue;
                    int surface = Surface(ba, n, bx, level, bz, b);
                    if (surface < level) continue;
                    seenUp.Add((bx, bz));
                    queueUp.Enqueue((bx, bz, surface, dist + 1));
                }
            }
            for (int d = 1; d < Reach; d++) up[d] = Math.Max(up[d], up[d - 1]);

            for (int i = 0; i < Reach; i++)
                for (int j = 1; i + j <= Reach; j++)
                    if (up[i] - down[j] >= RapidDrop) return true;
            return false;
        }

        /// <summary>
        /// The top of the water of liquid <paramref name="b"/> in column (x, z) that reaches down to <paramref name="level"/>
        /// or starts at most <see cref="RapidDrop"/> above it (a higher pool on its own floor); int.MinValue if none.
        /// </summary>
        static int Surface(IBlockAccessor ba, BlockPos n, int x, int level, int z, Block b)
        {
            for (int y = level; y <= level + RapidDrop; y++)
            {
                if (!Liquids.Same(Liquids.Fluid(ba, n.Set(x, y, z)), b)) continue;
                // bounded like every other column read (rc.4, audit: a tall column of water was climbed whole)
                for (int top = y + MaxDropScan; y < top && Liquids.Same(Liquids.Fluid(ba, n.Set(x, y + 1, z)), b); ) y++;
                return y;
            }
            return int.MinValue;
        }

        /// <summary>
        /// 0.1.8 (decision Earth 0027): in a column Earth's river query marks as rapids, surface sources and flowing water are
        /// rapid and stay rapid (never reverted), whether or not a step is near. Query versions 1 and 2 mark a whole rapids
        /// reach (by its average slope), so a whole steep stretch shows white water; version 3 (Earth's pool layout 11,
        /// decision Earth 0046) marks only a rapids reach's drops (the ramps below its steps and the steps' edges), so the
        /// pools between them stay calm. Elsewhere H4 works by its window as before.
        /// </summary>
        public static bool QueryRapids = true;

        /// <summary>A rapids column by the query; for a source only up to the river's surface (EarthRiverQuery.SpanAbove).</summary>
        static bool RapidsByQuery(BlockPos pos, bool source = false) => Enabled && QueryRapids && EarthRiverQuery.Active && EarthRiverQuery.Rapids(pos, source);

        /// <summary>
        /// 0.1.10 (Earth pool layout 9, query version 2): in a rapids reach by the query, a surface source resting on a source
        /// or the ground carries the river's flow (<c>RiverFlowAt</c>). Earth draws a rapids reach's sources as
        /// <c>rapidwater-&lt;flow&gt;-7</c>, so the whole reach looks like and pushes as running water; vanilla relabels a
        /// source from the levels around it whenever water beside it updates: "still" in a pool (all equal), toward a hole
        /// someone dug, or falling over a falling-labelled cell, which would leave calm or odd patches wherever a player
        /// touched the river. Covered sources and sources over air or flowing water are left to vanilla. Not in worlds whose
        /// query is version 1 (layout 8), whose sources Earth drew still. Query version 3 (Earth's pool layout 11, decision
        /// Earth 0046): in every river column, calm ones as ordinary water (a gentle current), so a placed block or a dug
        /// bank leaves no still patch in a calm river either.
        /// </summary>
        public static bool QueryFlowLabels = true;

        /// <summary>The label vanilla chose for <paramref name="pos"/> (<paramref name="result"/>), or the river's flow where
        /// the query labels the column (<see cref="QueryFlowLabels"/>): Earth's flow replaces any other label vanilla chose,
        /// a still one or one toward a hole alike.</summary>
        public static int RiverFlowLabel(IWorldAccessor world, BlockPos pos, int result, int liquidLevel)
        {
            // its own switch only: the labels are the river's geography, which Earth's layout 11 relies on Hydrology keeping
            // in calm columns whatever the rapids switches say
            if (!QueryFlowLabels || liquidLevel != Liquids.SourceLevel || !EarthRiverQuery.Active || EarthRiverQuery.Version < 2) return result;
            var r = world.Blocks[result];
            if (!Liquids.IsFresh(r)) return result;
            int flow = EarthRiverQuery.LabelledFlow(pos);
            var ba = world.BlockAccessor;
            if (flow < 0)
            {
                // rc.5: Earth's answer for this column is not ready yet: a source keeps the label it has (a neighbour's
                // update relabels it), as the column's own updates wait for the answer
                if (EarthRiverQuery.NotReady(pos))
                {
                    var current = Liquids.Fluid(ba, pos);
                    if (Liquids.IsFresh(current) && Liquids.IsSource(current) && Liquids.IsRapid(current) == Liquids.IsRapid(r) && current.Id != result)
                    {
                        HydrologyStats.LabelsKeptWaiting++;
                        return current.Id;
                    }
                }
                return result;
            }
            if (Liquids.FlowLabel(r) == flow) return result;
            if (Liquids.Same(Liquids.Fluid(ba, Liquids.Above(pos)), r)) return result;
            var below = Liquids.Below(pos);
            if (Liquids.Open(ba, below) && !Liquids.IsSource(Liquids.Fluid(ba, below))) return result;
            HydrologyStats.FlowLabelsKept++;
            return Liquids.SourceWithFlow(r, flow);
        }

        /// <summary>Turns a fresh source into a rapid source at an edge, and back when the edge is gone; in a rapids reach
        /// by the query, a source at the surface is rapid and stays so (a covered one is left alone).</summary>
        public static void CheckSource(IWorldAccessor world, BlockPos pos, Block b)
        {
            if (!Enabled || !Liquids.IsFresh(b) || !Liquids.IsSource(b)) return;
            var ba = world.BlockAccessor;
            if (RapidsByQuery(pos, source: true))
            {
                // (a block with no rapid counterpart would be written back as itself and wake itself for ever)
                if (!Liquids.IsRapid(b) && !Liquids.Same(Liquids.Fluid(ba, Liquids.Above(pos)), b) && Liquids.Counterpart(b, true) != b.Id)
                {
                    Liquids.SetAndWake(world, pos, Liquids.Counterpart(b, true));
                    HydrologyStats.RapidSourcesByQuery++;
                }
                return;
            }
            // the window would read a chunk column that is not loaded as air, a bottomless drop: no answer, so no change
            if (!WindowLoaded(ba, pos)) return;
            bool edge = IsEdge(ba, pos);
            bool rapid = Liquids.IsRapid(b);
            if (edge && !rapid)
            {
                Liquids.SetAndWake(world, pos, Liquids.RapidSource.Id);
                HydrologyStats.RapidSources++;
                WakeEdgesAround(world, pos);
            }
            else if (!edge && rapid && !Lone(ba, pos, b))
            {
                Liquids.SetAndWake(world, pos, Liquids.WaterSource.Id);
                HydrologyStats.RapidSourcesReverted++;
                WakeEdgesAround(world, pos);
            }
        }

        /// <summary>
        /// After an edge changed: schedule a vanilla update for every fresh source within <see cref="Reach"/> column steps
        /// and <see cref="WakeDepth"/> levels that is rapid or has an open side, so the other steps of a chute re-check.
        /// </summary>
        internal static void WakeEdgesAround(IWorldAccessor world, BlockPos pos)
        {
            var ba = world.BlockAccessor;
            // HY-0017: a cell is woken at most once in a window of WakeWindowMs, however many new sources appear near it
            // (filling a basin woke the same rim sources once per filled cell nearby), and a centre scanned in the window is
            // not scanned again. The window is shorter than the shortest delay of a liquid's own update (rapid water's
            // 50 ms), so a woken cell's update always runs after the changes the window merged: no wake is lost. (The
            // first build keyed it on the server's millisecond clock, which merged only within one millisecond.)
            long now = world.ElapsedMilliseconds;
            if (now - wokenTick >= WakeWindowMs || now < wokenTick || woken.Count > MaxWokenPerTick)
            {
                wokenTick = now;
                woken.Clear();
                scanned.Clear();
            }
            if (!scanned.Add((pos.dimension, pos.X, pos.Y, pos.Z))) return;
            var p = wakeP ??= new BlockPos(0);
            p.dimension = pos.dimension;
            for (int dx = -Reach; dx <= Reach; dx++)
                for (int dz = -Reach; dz <= Reach; dz++)
                {
                    if (Math.Abs(dx) + Math.Abs(dz) > Reach || (dx == 0 && dz == 0)) continue;
                    for (int dy = -WakeDepth; dy <= WakeDepth; dy++)
                    {
                        p.Set(pos.X + dx, pos.Y + dy, pos.Z + dz);
                        var f = Liquids.Fluid(ba, p);
                        if (!Liquids.IsFresh(f) || !Liquids.IsSource(f)) continue;
                        if (woken.Contains((p.dimension, p.X, p.Y, p.Z))) continue;
                        if (!Liquids.IsRapid(f) && !HasOpenSide(ba, p)) continue;
                        woken.Add((p.dimension, p.X, p.Y, p.Z));
                        HydrologyStats.EdgeWakes++;
                        f.OnNeighbourBlockChange(world, p.Copy(), pos);
                    }
                }
        }

        // HY-0017: the cells woken and the centres scanned in the current server tick (the main thread only)
        static readonly HashSet<(int, int, int, int)> woken = new(), scanned = new();
        // far in the past without overflowing now - wokenTick (long.MinValue overflowed: the window never reset, and the sets
        // only cleared at MaxWokenPerTick, so wakes were skipped for long stretches; found by the offline H4 case)
        static long wokenTick = long.MinValue / 2;
        const int MaxWokenPerTick = 65536;

        /// <summary>A new world: the wake window starts empty (Fable's pass, R1).</summary>
        public static void ResetWorld() { woken.Clear(); scanned.Clear(); wokenTick = long.MinValue / 2; }
        const long WakeWindowMs = 40;
        [ThreadStatic] static BlockPos wakeP, sideP;

        static bool HasOpenSide(IBlockAccessor ba, BlockPos pos)
        {
            var n = sideP ??= new BlockPos(0);
            n.dimension = pos.dimension;
            for (int i = 0; i < 4; i++)
            {
                n.Set(pos).Add(H[i]);
                if (!Liquids.Open(ba, n)) continue;
                var f = Liquids.Fluid(ba, n);
                if (!(f.IsLiquid() && Liquids.IsSource(f))) return true;
            }
            return false;
        }

        /// <summary>
        /// A cell has just become a source (H2, refill, vanilla's own rule): an edge that went rapid while a hole was open
        /// nearby is re-checked (0.1.5; else it stayed rapid until something woke it).
        /// </summary>
        public static void WakeEdgesOnNewSource(IWorldAccessor world, BlockPos pos)
        {
            if (Enabled && WakeOnNewSource) WakeEdgesAround(world, pos);
        }

        /// <summary>0.1.5: a rapid source that is part of a body of sources spreads to all sides for its update.</summary>
        public static bool SourceSpreadsAsSheet(IBlockAccessor ba, BlockPos pos)
        {
            if (!Enabled || !RapidSourceSheets) return false;
            var b = Liquids.Fluid(ba, pos);
            return Liquids.IsRapid(b) && Liquids.IsSource(b) && !Lone(ba, pos, b);
        }

        [ThreadStatic] static BlockPos loneN, sheetN, wantN;

        static bool Lone(IBlockAccessor ba, BlockPos pos, Block b)
        {
            var n = loneN ??= new BlockPos(0);
            n.dimension = pos.dimension;
            if (LoneVertical)
                for (int dy = -1; dy <= 1; dy += 2)
                {
                    var v = Liquids.Fluid(ba, n.Set(pos.X, pos.Y + dy, pos.Z));
                    if (Liquids.IsSource(v) && Liquids.Same(v, b)) return false;
                }
            for (int i = 0; i < 4; i++)
            {
                var f = Liquids.Fluid(ba, n.Set(pos).Add(H[i]));
                if (Liquids.IsSource(f) && Liquids.Same(f, b)) return false;
            }
            return true;
        }

        /// <summary>
        /// Vanilla rapid water spreads as a single stream (<c>multiplySpread: false</c>): a cell lowers itself when a
        /// neighbour has a better way down, and only one cell of a row may fall. That suits vanilla's 1-wide rivulets, but
        /// a fall or ramp wider than one block then keeps one column, and an irregular ramp keeps lowering and raising
        /// its cells (Earth 0.16.4's chutes: about 4 updates a second per cell, for ever). A rapid cell that is part of a
        /// sheet spreads like ordinary water instead, for the duration of its own update: it rests on water, has a
        /// neighbour of the same level, or is beside an H4 edge (a rapid source with sources beside it). Lone streams over
        /// dry ground, such as vanilla rivulets, stay vanilla.
        /// </summary>
        public static bool SpreadsAsSheet(IBlockAccessor ba, BlockPos pos)
        {
            if (!Enabled) return false;
            var b = Liquids.Fluid(ba, pos);
            if (!Liquids.IsRapid(b) || Liquids.IsSource(b)) return false;
            if (Liquids.Same(Liquids.Fluid(ba, Liquids.Below(pos)), b)) return true;
            var n = sheetN ??= new BlockPos(0);
            n.dimension = pos.dimension;
            for (int i = 0; i < 4; i++)
            {
                n.Set(pos).Add(H[i]);
                var f = Liquids.Fluid(ba, n);
                if (!Liquids.Same(f, b)) continue;
                if (f.LiquidLevel == b.LiquidLevel) return true;
                if (Liquids.IsSource(f) && Liquids.IsRapid(f) && !Lone(ba, n, f)) return true;
            }
            return false;
        }

        /// <summary>
        /// A flowing fresh-water cell takes the kind of the water that feeds it: the water above, else its highest
        /// neighbour (rapid wins a tie); in a column the query marks as rapids, always rapid. Elsewhere cells nothing feeds are
        /// left to vanilla, which lowers them.
        /// The change wakes only the cell itself; the cells it feeds follow through <see cref="Propagate"/>. Waking its
        /// neighbours too would wake an ordinary source beside it, which spreads ordinary water into the cell again: a cell
        /// fed by both an ordinary source and a rapid edge then flipped about a thousand times a second on an Earth river.
        /// </summary>
        public static void FollowFeeder(IWorldAccessor world, BlockPos pos)
        {
            if (!Enabled) return;
            var ba = world.BlockAccessor;
            var b = Liquids.Fluid(ba, pos);
            if (!Liquids.IsFresh(b) || Liquids.IsSource(b)) return;

            bool? wantRapid = WantRapid(ba, pos, b, out var ordinarySource);
            if (wantRapid == false && Liquids.IsRapid(b) && ordinarySource != null)
            {
                // a rapid cell fed by an ordinary source: the source may be an edge nothing has checked yet (Earth generates
                // its chutes with ordinary sources and rapid ramps), so check it before turning the cell ordinary
                CheckSource(world, ordinarySource, Liquids.Fluid(ba, ordinarySource));
                wantRapid = WantRapid(ba, pos, b, out _);
            }
            if (wantRapid == null || wantRapid == Liquids.IsRapid(b)) return;
            int id = Liquids.Counterpart(b, wantRapid.Value);
            if (id == b.Id) return; // no counterpart (a modded variant): writing it back would wake the cell for ever
            Liquids.SetAndWake(world, pos, id, neighbours: false);
            if (wantRapid.Value) HydrologyStats.MadeRapid++; else HydrologyStats.MadeOrdinary++;
        }

        /// <summary>The kind the feeding water asks for (null: nothing feeds the cell); <paramref name="ordinarySource"/>: an
        /// ordinary fresh source among the strongest feeders, if any.</summary>
        static bool? WantRapid(IBlockAccessor ba, BlockPos pos, Block b, out BlockPos ordinarySource)
        {
            ordinarySource = null;
            if (RapidsByQuery(pos)) return true;
            var above = Liquids.Fluid(ba, Liquids.Above(pos));
            if (Liquids.Same(above, b)) return Liquids.IsRapid(above);
            bool? wantRapid = null;
            int best = b.LiquidLevel;
            var n = wantN ??= new BlockPos(0);
            n.dimension = pos.dimension;
            for (int i = 0; i < 4; i++)
            {
                n.Set(pos).Add(H[i]);
                var f = Liquids.Fluid(ba, n);
                if (!Liquids.Same(f, b) || f.LiquidLevel <= b.LiquidLevel || !Liquids.Passes(ba, n, pos, H[i].Opposite)) continue;
                if (f.LiquidLevel > best) { best = f.LiquidLevel; wantRapid = Liquids.IsRapid(f); }
                else if (f.LiquidLevel == best && Liquids.IsRapid(f)) wantRapid = true;
                if (Liquids.IsSource(f) && !Liquids.IsRapid(f)) ordinarySource = n.Copy();
            }
            return wantRapid;
        }

        /// <summary>After the cell at <paramref name="pos"/> changed: the cells it feeds (below and lower beside) follow at once.</summary>
        public static void Propagate(IWorldAccessor world, BlockPos pos)
        {
            if (!Enabled) return;
            FollowFeeder(world, pos.DownCopy());
            var n = new BlockPos(pos.dimension);
            for (int i = 0; i < 4; i++) FollowFeeder(world, n.Set(pos).Add(H[i]));
        }

        /// <summary>
        /// A change in flowing or falling water can open or fill a drop whose edge is out of reach of the change: walk up
        /// the column of flowing water to its top and re-check the sources beside it.
        /// </summary>
        public static void RecheckEdgesAbove(IWorldAccessor world, BlockPos pos)
        {
            if (!Enabled) return;
            var ba = world.BlockAccessor;
            var b = Liquids.Fluid(ba, pos);
            if (!Liquids.IsFresh(b) || Liquids.IsSource(b)) return;
            var top = pos.Copy();
            for (int i = 0; i < MaxDropScan; i++)
            {
                var up = Liquids.Fluid(ba, top.Up());
                if (!Liquids.Same(up, b) || Liquids.IsSource(up)) { top.Down(); break; }
            }
            var n = new BlockPos(pos.dimension);
            for (int i = 0; i < 4; i++)
            {
                n.Set(top).Add(H[i]);
                var f = Liquids.Fluid(ba, n);
                if (Liquids.IsFresh(f) && Liquids.IsSource(f)) CheckSource(world, n, f);
            }
        }
    }
}
