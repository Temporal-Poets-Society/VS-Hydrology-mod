using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// Dammed river pools (0.6.2-rc.13, HY-0011, decision 0010): a river fills the pool behind a dam from its own flow.
    ///
    /// Decision 0009 lets water stand above an Earth river's generated surface behind a player's dam, but H2 fills a level
    /// only from water above it, so a calm pool with no step near its dam rose nothing (the Inn, 14 November). Here a block
    /// a player places beside a natural river (<see cref="H2Release.Placed"/>, which marks the pool and the pools upstream)
    /// starts a record of that pool. While it runs, the river's flow (Earth's discharge, m³/s, times
    /// <see cref="FillPerCubic"/>, from 1 to <see cref="FillMax"/> sources a second) fills the pool layer by layer above its
    /// current level:
    /// <list type="bullet">
    /// <item>A layer is the open cells at that height joined to the marked columns, resting on ground or on water. A part
    /// of it that reaches a river column that is not marked (the river running on below, a lower pool), a cell with nothing
    /// under it reached over water, or more than <see cref="MaxLayer"/> cells is open: it is not filled.</item>
    /// <item>A closed part is filled, and the pool rises a block.</item>
    /// <item>A part whose only openings are at most <see cref="MaxSpillEdges"/> edges reached from cells resting on ground
    /// (a dam's crest, a low bank) is the water over the crest: it is filled once and the pool stops rising. The water then
    /// falls past the crest as vanilla's flowing water, and H4 makes rapids where it drops two blocks or more.</item>
    /// </list>
    /// Nothing runs once a pool is full or open. Breaking the dam drains the raised water (decision 0009, rc.12) and ends
    /// the record. Records are kept with the world (the block that started them, marked again when its chunk loads); at
    /// most <see cref="MaxPools"/> fill at once. A pool whose next layer reaches unloaded land waits (a minute, then it goes
    /// back to the waiting dams, asked again every 5 s); a cell of a planned layer that is unloaded when its turn comes holds
    /// the pool where it is, quietly and without limit, until its chunk loads (its water is capped meanwhile).
    /// </summary>
    public static class H2DamPools
    {
        public static bool Enabled = true;
        /// <summary>Sources a second per m³/s of the river's discharge (0.1: a 160 m³/s river fills 16 cells a second).</summary>
        public static double FillPerCubic = 0.1;
        /// <summary>The most sources a second one pool takes.</summary>
        public static int FillMax = 64;
        public const int MaxLayer = 16384, MaxSpillEdges = 64, MaxPools = 8, MaxRise = 32;
        /// <summary>One line per layer and per pool started and ended (the game's log; null offline).</summary>
        public static System.Action<string> Log;

        static readonly BlockFacing[] H = BlockFacing.HORIZONTALS;

        sealed class Pool
        {
            public int dim, px, py, pz, level, top;
            public readonly HashSet<(int x, int z)> cols = new();
            public double rate, credit;
            public Block liquid;
            public int sourceId;
            public readonly List<(int x, int y, int z)> layer = new();
            public int next;
            /// <summary>The layer being placed is the one over the crest: the pool is full once it is placed.</summary>
            public bool spill;
            public int raised;
            /// <summary>Sources placed since the last plan, and steps spent waiting for unloaded land.</summary>
            public int placedSincePlan, waits;
            public bool planned;
            /// <summary>The walk over the river's columns met unloaded land: the river beyond its marked columns is not known
            /// yet, so an unmarked river column is no outlet but a wait, and the columns are marked again before the next plan
            /// (the reviewer's re-review: a dam whose upstream water was partly unloaded never started).</summary>
            public bool partial;
            public long remarkedMs = long.MinValue / 2;
        }

        static readonly List<Pool> pools = new();

        /// <summary>A dam waiting to start its pool: a record read from the save, or a block placed while the water beside it
        /// drains, all pools are busy or the river runs on into unloaded land.</summary>
        sealed class Waiting
        {
            public int dim, x, y, z;
            public long notBefore;
            /// <summary>Times it was asked again; it gives up after <see cref="MaxTries"/>.</summary>
            public int tries;
            /// <summary>Its wait was logged (once per waiting dam: Fable's re-review, low 2).</summary>
            public bool logged;
        }
        /// <summary>Waiting dams, one per dam (Fable's re-review, low 2: each block placed during a drain was its own entry,
        /// asked again through the whole placement and logged at every retry).</summary>
        static readonly List<Waiting> pending = new();
        /// <summary>A waiting dam is asked again this often.</summary>
        const long PendingRetryMs = 5000;
        /// <summary>Times a waiting dam is asked again (20 minutes) before it gives up, logged.</summary>
        const int MaxTries = 240;
        /// <summary>Waiting dams kept at most; more are dropped, logged once.</summary>
        const int MaxPending = 256;
        static bool pendingCapLogged;
        /// <summary>A waiting dam is being asked again: a wait is not logged again (the entry's logged flag is carried over).</summary>
        internal static bool Retrying;
        /// <summary>Plans one pool may make in one step (Fable's pass, critical: a layer that placed nothing was planned again
        /// for ever on the main thread).</summary>
        const int MaxPlansPerStep = 4;
        /// <summary>Steps a pool waits for unloaded land before it gives up.</summary>
        const int MaxWaits = 120;

        public static int Running => pools.Count;
        public static bool HasPending => pending.Count > 0;

        public static void ResetWorld() { pools.Clear(); pending.Clear(); pendingCapLogged = false; stopsLogged.Clear(); }

        /// <summary>Dams whose "stops filling and waits" line was logged: once per dam and world (the reviewer's re-review: a
        /// pool whose land stays unloaded cycled between waiting and starting, two lines a minute).</summary>
        static readonly HashSet<(int dim, int x, int y, int z)> stopsLogged = new();

        /// <summary>The water rules went off: the pools filling stop, the saved ones stay for the save (Fable's pass).</summary>
        public static int EndPools()
        {
            int n = pools.Count;
            foreach (var p in pools) Wait(p.dim, p.px, p.py, p.pz, 0, null);
            pools.Clear();
            return n;
        }

        /// <summary>Blocks of one dam: a waiting block within this many blocks of another waiting one is the same dam, one
        /// entry (asked again, and logged, once for the dam).</summary>
        const int SameDam = 8;

        /// <summary>The dam at the position waits (one entry per dam: the blocks of a dam within <see cref="SameDam"/> of a
        /// waiting one join it): asked again after <paramref name="notBefore"/>; <paramref name="why"/> is logged the first
        /// time it waits (null: say nothing, a saved record).</summary>
        static void Wait(int dim, int x, int y, int z, long notBefore, string why)
        {
            Waiting w = null;
            foreach (var e in pending)
                if (e.dim == dim && System.Math.Abs(e.x - x) <= SameDam && System.Math.Abs(e.y - y) <= SameDam && System.Math.Abs(e.z - z) <= SameDam) { w = e; break; }
            if (w == null)
            {
                if (pending.Count >= MaxPending)
                {
                    if (!pendingCapLogged) { pendingCapLogged = true; Log?.Invoke($"Hydrology: {MaxPending} dams are waiting to fill their river pools; a dam at {x},{y},{z} is not kept (place a block there again later)"); }
                    return;
                }
                w = new Waiting { dim = dim, x = x, y = y, z = z };
                pending.Add(w);
            }
            w.notBefore = notBefore;
            if (why != null && !w.logged && !Retrying) { w.logged = true; Log?.Invoke($"Hydrology: a dam at {x},{y},{z} waits: {why}"); }
            else if (why != null && Retrying) w.logged = true;
        }

        static void Wait(BlockPos pos, long now, string why) => Wait(pos.dimension, pos.X, pos.Y, pos.Z, now + PendingRetryMs, why);

        /// <summary>
        /// A block a player placed at <paramref name="pos"/> beside a natural river whose pool's surface is <paramref name="top"/>;
        /// <paramref name="cols"/> are the river columns it marked (the pool and the pools upstream). Starts a record of the
        /// pool, or adds to the one already filling it.
        /// </summary>
        /// <param name="unloaded">The walk over the river's columns met unloaded land: the pool starts with the columns it
        /// marked and is <see cref="Pool.partial"/> (a pool marked in part had ended as soon as the rest loaded; waiting for
        /// all of it kept a dam below a step from ever starting: the reviewer's re-reviews).</param>
        public static void Dammed(IWorldAccessor world, BlockPos pos, int top, List<(int x, int z)> cols, BlockPos river, bool unloaded = false)
        {
            if (!Enabled || cols.Count == 0) return;
            long now = world.ElapsedMilliseconds;
            // Fable's pass (critical): no pool over a body a drain is emptying (its next layer would be all guarded cells);
            // the block is asked again once the drain is over
            if (H2Release.DrainingIn(pos.dimension, cols))
            {
                Wait(pos, now, "the water beside it is draining");
                return;
            }
            foreach (var p in pools)
            {
                if (p.dim != pos.dimension) continue;
                bool shared = false;
                foreach (var c in cols) if (p.cols.Contains(c)) { shared = true; break; }
                if (!shared) continue;
                foreach (var c in cols) p.cols.Add(c);
                if (unloaded) p.partial = true;
                // planned again: the new block may close a layer that was open (and a plan that placed nothing yet does not
                // end the pool before it is planned with the block: Fable's re-review, low 4)
                p.layer.Clear();
                p.next = 0;
                p.spill = false;
                p.planned = false;
                p.placedSincePlan = 0;
                return;
            }
            if (pools.Count >= MaxPools)
            {
                Wait(pos, now, $"{MaxPools} river pools are filling already");
                return;
            }
            EarthRiverQuery.At(river, out _, out _, out double discharge);
            // Fable's pass: a discharge that is not a number made a pool whose credit never reached a source
            if (!double.IsFinite(discharge) || discharge < 0) discharge = 0;
            var b = Liquids.Fluid(world.BlockAccessor, river);
            var pool = new Pool
            {
                dim = pos.dimension, px = pos.X, py = pos.Y, pz = pos.Z, level = top, top = top, liquid = b,
                sourceId = Liquids.SourceOf(b).Id,
                rate = System.Math.Clamp(discharge * FillPerCubic, 1, FillMax)
            };
            foreach (var c in cols) pool.cols.Add(c);
            pool.partial = unloaded;
            pools.Add(pool);
            // said when a player's block starts it, not again each time a waiting dam is asked again
            if (!Retrying)
                Log?.Invoke($"Hydrology: a dam at {pos.X},{pos.Y},{pos.Z} backs up the river ({cols.Count} columns, surface {top}, {discharge:0} m³/s: {pool.rate:0.#} sources a second)");
        }

        /// <summary>A drain started in this body (the dam was opened): the pools over it stop filling.</summary>
        public static void Stop(int dim, HashSet<(int x, int y, int z)> body)
        {
            for (int i = pools.Count - 1; i >= 0; i--)
            {
                var p = pools[i];
                if (p.dim != dim) continue;
                foreach (var c in body)
                    if (p.cols.Contains((c.x, c.z)))
                    {
                        Log?.Invoke($"Hydrology: the dam at {p.px},{p.py},{p.pz} is open: its pool stops filling at {p.level} ({p.raised} sources raised)");
                        pools.RemoveAt(i);
                        break;
                    }
            }
        }

        /// <summary>Each pool takes what its river brings in <paramref name="dt"/> seconds; saved pools start again once their
        /// chunk is loaded.</summary>
        public static void Tick(IWorldAccessor world, float dt)
        {
            if (!Enabled) return;
            var ba = world.BlockAccessor;
            var p0 = new BlockPos(0);
            long now = world.ElapsedMilliseconds;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var w = pending[i];
                if (now < w.notBefore) continue;
                p0.dimension = w.dim;
                // Fable's pass: the chunks round the block loaded too, a cheap first gate (the mark walk itself says when the
                // river runs on into unloaded land)
                bool loaded = true;
                for (int dx = -32; dx <= 32 && loaded; dx += 32)
                    for (int dz = -32; dz <= 32 && loaded; dz += 32)
                        if (!Liquids.Loaded(ba, p0.Set(w.x + dx, w.y, w.z + dz))) loaded = false;
                if (!loaded) continue;
                if (++w.tries > MaxTries)
                {
                    Log?.Invoke($"Hydrology: the dam at {w.x},{w.y},{w.z} stops waiting after {MaxTries} tries; place a block there again to start its pool");
                    pending.RemoveAt(i);
                    continue;
                }
                // asked again with the river's marks and the pool alone (Fable's re-review, low 2: not the whole placement);
                // it waits again (the same entry) or starts its pool
                pending.RemoveAt(i);
                var pos = new BlockPos(w.x, w.y, w.z, w.dim);
                H2Release.RetryDam(world, pos);
                // waiting again: the new entry keeps the count and the logged wait
                foreach (var e in pending)
                    if (e.dim == w.dim && e.x == w.x && e.y == w.y && e.z == w.z && e != w) { e.tries = w.tries; e.logged |= w.logged; break; }
            }
            foreach (var pool in pools.ToArray())
            {
                p0.dimension = pool.dim;
                if (!Liquids.Loaded(ba, p0.Set(pool.px, pool.py, pool.pz))) continue;
                Step(world, pool, pool.rate * dt);
            }
        }

        /// <summary>Lets every pool fill to its end now (the offline suite).</summary>
        public static void RunAll(IWorldAccessor world)
        {
            for (int guard = 0; pools.Count > 0 && guard < 256; guard++)
                foreach (var pool in pools.ToArray()) Step(world, pool, 1 << 20);
        }

        static void Step(IWorldAccessor world, Pool pool, double water)
        {
            pool.credit += water;
            int raisedBefore = pool.raised;
            try { StepLayers(world, pool); }
            finally
            {
                // Fable's re-review (low 3): a step that placed nothing (waiting for land, a layer still to plan) keeps at most
                // one second of the river's water, not all it brought while it waited
                if (pool.raised == raisedBefore && pool.credit > pool.rate) pool.credit = pool.rate;
            }
        }

        static void StepLayers(IWorldAccessor world, Pool pool)
        {
            var ba = world.BlockAccessor;
            var p = new BlockPos(pool.dim);
            int plans = 0;
            while (pool.credit >= 1)
            {
                if (pool.next >= pool.layer.Count)
                {
                    // a planned layer none of whose cells could take water: planning it again would give the same layer
                    if (pool.planned && pool.placedSincePlan == 0 && !pool.spill)
                    {
                        Log?.Invoke($"Hydrology: the dam at {pool.px},{pool.py},{pool.pz} holds its pool at {pool.level} (no cell of the next layer could take water) ({pool.raised} sources raised)");
                        pools.Remove(pool);
                        return;
                    }
                    if (++plans > MaxPlansPerStep) return;
                    var plan = pool.spill ? PlanResult.None : Plan(ba, pool);
                    if (plan == PlanResult.Wait)
                    {
                        pool.layer.Clear();
                        pool.next = 0;
                        pool.planned = false;
                        // a partial pool marks its river again (every few seconds) before it plans again: the land may have loaded
                        long nowMs = world.ElapsedMilliseconds;
                        if (pool.partial && nowMs - pool.remarkedMs >= PendingRetryMs)
                        {
                            pool.remarkedMs = nowMs;
                            pool.partial = H2Release.Remark(world, new BlockPos(pool.px, pool.py, pool.pz, pool.dim), pool.cols);
                        }
                        if (++pool.waits > MaxWaits)
                        {
                            // Fable's re-review (low 5): back to waiting (its place among the filling pools freed), asked again
                            // until the land loads or the waiting gives up
                            if (stopsLogged.Count < 4096 && stopsLogged.Add((pool.dim, pool.px, pool.py, pool.pz)))
                                Log?.Invoke($"Hydrology: the dam at {pool.px},{pool.py},{pool.pz} stops filling at {pool.level} and waits: its land stayed unloaded ({openWhy})");
                            pools.Remove(pool);
                            Wait(pool.dim, pool.px, pool.py, pool.pz, world.ElapsedMilliseconds + PendingRetryMs, null);
                        }
                        return;
                    }
                    if (plan == PlanResult.None)
                    {
                        Log?.Invoke($"Hydrology: the dam at {pool.px},{pool.py},{pool.pz} holds its pool at {pool.level}" +
                                    (pool.spill ? ", the water running over its crest" : $" (the next layer is open: {(openWhys.Count > 0 ? string.Join("; ", openWhys) : openWhy)})") + $" ({pool.raised} sources raised)");
                        pools.Remove(pool);
                        return;
                    }
                    pool.planned = true;
                    pool.placedSincePlan = 0;
                    pool.waits = 0;
                    continue;
                }
                var (x, y, z) = pool.layer[pool.next++];
                p.Set(x, y, z);
                if (!Liquids.Loaded(ba, p)) { pool.next--; return; }
                // built on, filled meanwhile, or emptied by a drain: nothing stands there
                if (!CanTake(ba, p, pool.liquid)) continue;
                Liquids.SetAndWake(world, p, pool.sourceId);
                pool.placedSincePlan++;
                pool.credit--;
                pool.raised++;
                HydrologyStats.DamSources++;
                if (pool.next >= pool.layer.Count && !pool.spill) pool.level = y;
            }
        }

        /// <summary>A cell a pool may stand water in now: open, empty or flowing water of the pool's liquid, not a cell a running
        /// drain has emptied (Plan and Step use the same test, so a planned cell is one Step can fill).</summary>
        static bool CanTake(IBlockAccessor ba, BlockPos p, Block liquid) => !H2Release.Draining(p) && Liquids.CanTake(ba, p, liquid);

        enum PlanResult { Layer, None, Wait }

        // scratch for Plan (the server thread only)
        static readonly HashSet<(int x, int z)> seen = new();
        static readonly Queue<(int x, int z, bool lip)> queue = new();
        static readonly List<(int x, int z)> part = new();
        static BlockPos partR;
        /// <summary>Why the last layer planned was open: its first outlet (the log's line when a pool stops).</summary>
        static string openWhy = "";
        static readonly List<string> openWhys = new();
        static void NoteOpen(string why) { openWhy = why; if (openWhys.Count < 6 && !openWhys.Contains(why)) openWhys.Add(why); }

        /// <summary>The next layer to fill above the pool's level: None when there is none (open, or nothing to fill), Wait when
        /// it reaches unloaded land (asked again next step).</summary>
        static PlanResult Plan(IBlockAccessor ba, Pool pool)
        {
            pool.layer.Clear();
            pool.next = 0;
            openWhy = "nothing to fill"; openWhys.Clear();
            var p = new BlockPos(pool.dim);
            var q = new BlockPos(pool.dim);
            for (int guard = 0; guard < 64; guard++)
            {
                int y = pool.level + 1;
                // a dam at most MaxRise blocks over the river's surface
                if (y > pool.top + MaxRise) return PlanResult.None;
                seen.Clear();
                bool any = false, full = false, spill = false;
                foreach (var (cx, cz) in pool.cols)
                {
                    if (seen.Contains((cx, cz))) continue;
                    p.Set(cx, y, cz);
                    if (!Liquids.Loaded(ba, p)) { openWhy = $"unloaded land at {cx},{y},{cz}"; return PlanResult.Wait; }
                    var f = Liquids.Fluid(ba, p);
                    if (Liquids.IsSource(f) && Liquids.Same(f, pool.liquid))
                    {
                        // raised water already standing in the pool's own columns (a higher pool's own water is not)
                        if (!EarthRiverQuery.RiverWater(p)) full = true;
                        continue;
                    }
                    if (!CanTake(ba, p, pool.liquid)) continue;
                    var r = Part(ba, pool, cx, y, cz, p, q);
                    // more than MaxLayer cells at one height: the pool stops here (left unwalked, the rest is not judged)
                    if (r == PartKind.TooBig) { pool.layer.Clear(); return PlanResult.None; }
                    if (r == PartKind.Wait) { pool.layer.Clear(); return PlanResult.Wait; }
                    if (r == PartKind.Open) continue;
                    foreach (var (x, z) in part) pool.layer.Add((x, y, z));
                    if (r == PartKind.Spill) spill = true;
                    any = true;
                }
                if (any)
                {
                    pool.spill = spill;
                    HydrologyStats.DamLayers++;
                    Log?.Invoke($"Hydrology: the dam at {pool.px},{pool.py},{pool.pz} fills layer {y} ({pool.layer.Count} cells{(spill ? ", over its crest" : "")})");
                    return PlanResult.Layer;
                }
                // the layer stands full already (raised before, by H2 or an earlier record): look one higher
                if (full) { pool.level = y; continue; }
                return PlanResult.None;
            }
            return PlanResult.None;
        }

        enum PartKind { Open, Closed, Spill, TooBig, Wait }

        /// <summary>The open cells at height <paramref name="y"/> joined to (x, z), into <see cref="part"/>.</summary>
        static PartKind Part(IBlockAccessor ba, Pool pool, int x0, int y, int z0, BlockPos p, BlockPos q)
        {
            var r = partR ??= new BlockPos(0);
            r.dimension = pool.dim;
            part.Clear();
            queue.Clear();
            seen.Add((x0, z0));
            queue.Enqueue((x0, z0, false));
            bool open = false;
            int edges = 0;
            while (queue.Count > 0)
            {
                var (x, z, fromGround) = queue.Dequeue();
                p.Set(x, y, z);
                bool ground = Liquids.GroundBelow(ba, p);
                bool supported = ground;
                if (!supported)
                {
                    q.Set(x, y - 1, z);
                    var below = Liquids.Fluid(ba, q);
                    supported = (Liquids.IsSource(below) && Liquids.Same(below, pool.liquid)) || !Liquids.Open(ba, q) || !Liquids.Passes(ba, p, q, BlockFacing.DOWN)
                        // after rc.13 (in game): a hole of flowing water in the river's own water, at or below its generated surface,
                        // holds the layer up as the river does (scattered holes in a pool above a step read as edges, and the
                        // layer over the crest was taken for open)
                        || (Liquids.Same(below, pool.liquid) && EarthRiverQuery.Active && pool.dim == 0 && EarthRiverQuery.RiverWater(q));
                }
                if (!supported)
                {
                    // nothing under it: water falls here. Over the crest (reached from a cell on ground) it may spill
                    if (!fromGround && !open) { open = true; NoteOpen($"water falls at {x},{y},{z} beside water"); }
                    edges++;
                    continue;
                }
                part.Add((x, z));
                // an open part is still walked to its end, so none of its cells is taken for a closed part of its own
                if (part.Count > MaxLayer) { NoteOpen($"more than {MaxLayer} cells at {y}"); return PartKind.TooBig; }
                foreach (var face in H)
                {
                    q.Set(x, y, z).Add(face);
                    if (seen.Contains((q.X, q.Z)) || !Liquids.Passes(ba, p, q, face)) continue;
                    // Fable's pass: unloaded land is not an outlet: the pool waits for it
                    if (!Liquids.Loaded(ba, q)) { openWhy = $"unloaded land at {q.X},{y},{q.Z}"; return PartKind.Wait; }
                    // a cell a running drain has emptied: the part is open (the water is running out there)
                    if (H2Release.Draining(q)) { if (!open) NoteOpen($"a drain is emptying {q.X},{y},{q.Z}"); open = true; continue; }
                    var f = Liquids.Fluid(ba, q);
                    if (!Liquids.Open(ba, q) || (f.Id != 0 && (!Liquids.Same(f, pool.liquid) || Liquids.IsSource(f)))) continue;
                    // the river running on below the dam, or a lower pool: not this pool's
                    if (EarthRiverQuery.Active && pool.dim == 0 && !pool.cols.Contains((q.X, q.Z)) && EarthRiverQuery.RiverColumn(q)
                        && Liquids.Open(ba, r.Set(q.X, y - 1, q.Z)))
                    {
                        // a partial pool does not know yet whether this column is the river running on or its own water beyond
                        // the marked part: it waits and marks again
                        if (pool.partial) { openWhy = $"the river at {q.X},{y},{q.Z} is not marked yet (its walk met unloaded land)"; return PartKind.Wait; }
                        // a river column that is not marked, open under the cell: its water stands lower (marked columns reach
                        // the pool's surface). A river column built up to the layer (the dam itself, rc.13 in game) is no outlet
                        if (!open) NoteOpen($"the river runs on at {q.X},{y},{q.Z}");
                        open = true;
                        continue;
                    }
                    seen.Add((q.X, q.Z));
                    queue.Enqueue((q.X, q.Z, ground));
                }
            }
            if (open || part.Count == 0) return PartKind.Open;
            if (edges == 0) return PartKind.Closed;
            if (edges > MaxSpillEdges) NoteOpen($"{edges} edges at {y} (from {x0},{z0})");
            return edges <= MaxSpillEdges ? PartKind.Spill : PartKind.Open;
        }

        /// <summary>The records to keep with the world: "pool dimension x,y,z" per line (the block that started each).</summary>
        public static void Save(StringBuilder sb)
        {
            foreach (var p in pools) sb.Append("pool ").Append(p.dim).Append(' ').Append(p.px).Append(',').Append(p.py).Append(',').Append(p.pz).Append('\n');
            foreach (var w in pending) sb.Append("pool ").Append(w.dim).Append(' ').Append(w.x).Append(',').Append(w.y).Append(',').Append(w.z).Append('\n');
        }

        /// <summary>A saved line, when it is a pool's.</summary>
        public static bool Load(string[] parts)
        {
            if (parts.Length != 3 || parts[0] != "pool" || !int.TryParse(parts[1], out int dim)) return false;
            var c = parts[2].Split(',');
            if (c.Length == 3 && int.TryParse(c[0], out int x) && int.TryParse(c[1], out int y) && int.TryParse(c[2], out int z))
                Wait(dim, x, y, z, 0, null);
            return true;
        }
    }
}
