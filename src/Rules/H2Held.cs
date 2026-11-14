using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// H2 filling, part 2 (HY-0018 split): whether a level is held (decision 0005) and fed (0006), fed channels (0008), the
    /// remembered answers and their search tokens (HY-0014), and the retries. The filling cases: H2Filling.cs.
    /// </summary>
    public static partial class H2Filling
    {
        /// <summary>Open cells <see cref="Held"/> follows at the cell's own level (the surface a basin would take), and in all
        /// (its depths too); beyond either the water counts as not held.</summary>
        const int MaxHeldLevel = 4096, MaxHeldSpace = 16384;

        /// <summary>How long a search's answer stands, in milliseconds: a held answer for every cell the search covered (the
        /// next fills in the same basin need no search of their own), a refusal for the cell asked (it is not searched again
        /// at once, as when the vessels case and the uneven basin both ask about it in one update).</summary>
        const long HeldMemoMs = 20000, UnheldMemoMs = 2000;

        [System.ThreadStatic] static HashSet<(int, int, int)> heldSeen;

        [System.ThreadStatic] static Queue<(int x, int y, int z)> heldQueue;

        // (dimension and liquid, x, y, z) -> (until, world generation[, search]); the main server thread only, as every H2
        // call. HY-0014: the liquid is part of the key (salt and fresh water shared answers), and each held answer carries the
        // search that made it: a player's break or placement beside any of its cells forgets the whole search
        // (ForgetAround), wherever its cells are, instead of the answer standing for its 20 s while the space is open.
        static readonly Dictionary<(int, int, int, int), (long until, int gen, int search)> heldMemo = new();

        static readonly Dictionary<(int, int, int, int), (long until, int gen)> unheldMemo = new();

        static readonly HashSet<int> liveSearches = new();
        /// <summary>Searches that share a remembered cell: forgotten together (a T-junction searched from two branches).</summary>
        static readonly Dictionary<int, HashSet<int>> linked = new();

        static void Link(int a, int b)
        {
            if (!linked.TryGetValue(a, out var la)) linked[a] = la = new HashSet<int>();
            if (!linked.TryGetValue(b, out var lb)) linked[b] = lb = new HashSet<int>();
            la.Add(b); lb.Add(a);
        }

        static readonly Stack<int> killStack = new();

        /// <summary>Forgets a search and every search linked to it. Iterative (Fable's second re-review: a chain of linked
        /// searches 20,000 long overflowed the stack when it recursed, which kills the server).</summary>
        static void Kill(int search)
        {
            killStack.Clear();
            killStack.Push(search);
            while (killStack.Count > 0)
            {
                int s = killStack.Pop();
                if (!liveSearches.Remove(s)) continue;
                if (!linked.TryGetValue(s, out var others)) continue;
                linked.Remove(s);
                foreach (var o in others) if (liveSearches.Contains(o)) killStack.Push(o);
            }
        }

        // HY-0017 (finding 9): scratch per search (Held, FedChannel, Supplied and TraceUp call one another, each its own)
        [System.ThreadStatic] static BlockPos heldP, heldN, channelP, channelN, supplyP, supplyN, traceP, traceN;

        static BlockPos Pos(ref BlockPos s, int dim) { s ??= new BlockPos(0); s.dimension = dim; return s; }

        static int heldGeneration, nextSearch;

        /// <summary>The memo's dimension field: the dimension and the liquid (salt water odd).</summary>
        static int MemoDim(int dim, Block b) => dim * 2 + (Liquids.IsSalt(b) ? 1 : 0);

        /// <summary>Remembers every cell of a held search under one search token.</summary>
        static void RememberHeld(HashSet<(int, int, int)> cells, int dim, Block b, long until, int gen)
        {
            if (heldMemo.Count + cells.Count > 65536 || liveSearches.Count >= 65536) { heldMemo.Clear(); liveSearches.Clear(); linked.Clear(); }
            int search = ++nextSearch;
            liveSearches.Add(search);
            int md = MemoDim(dim, b);
            long now = System.Environment.TickCount64;
            foreach (var (x, y, z) in cells)
            {
                // Fable re-review: a cell remembered by an older search links the two: forgetting one forgets both, so the
                // older search's other cells cannot outlive a change next to this one. Only an answer that still counts (not
                // run out, this world's) is linked, once (the second re-review: a re-search over 16,000 remembered cells
                // linked them 16,000 times, and stale answers grew chains without end)
                if (heldMemo.TryGetValue((md, x, y, z), out var old) && old.search != search && old.until > now && old.gen == gen
                    && liveSearches.Contains(old.search))
                    Link(old.search, search);
                heldMemo[(md, x, y, z)] = (until, gen, search);
            }
        }

        /// <summary>
        /// HY-0014: a player broke or placed a block at (x, y, z): every held search that covered a cell within one block of it
        /// is forgotten, and the refusals there too (the space may have opened or closed). The server's break and place
        /// events call this for every change, whether or not water is beside it.
        /// </summary>
        public static void ForgetAround(int dim, int x, int y, int z)
        {
            if (heldMemo.Count == 0 && unheldMemo.Count == 0) return;
            for (int salt = 0; salt <= 1; salt++)
            {
                int md = dim * 2 + salt;
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                {
                    var key = (md, x + dx, y + dy, z + dz);
                    if (heldMemo.TryGetValue(key, out var held)) { Kill(held.search); heldMemo.Remove(key); }
                    unheldMemo.Remove(key);
                }
            }
        }

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
        /// hillside, a floor open to a cave below are not. A held space must also be fed (rc.7, <see cref="Supplied"/>): a
        /// pond in a hollow closed above its surface is held, but a source over it no longer fills the hollow to its level.
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
            int top = pos.Y;
            long now = System.Environment.TickCount64;
            int gen = heldGeneration;
            var key = (MemoDim(pos.dimension, b), pos.X, pos.Y, pos.Z);
            if (heldMemo.TryGetValue(key, out var held) && held.gen == gen && held.until > now && liveSearches.Contains(held.search)) return true;
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
            var seeds = heldSeeds ??= new List<(int x, int y, int z)>();
            seeds.Clear();
            var traces = heldTraces ??= new List<(int x, int y, int z)>();
            traces.Clear();
            int atLevel = pos.Y == top ? 1 : 0, floor = pos.Y;
            var p = Pos(ref heldP, pos.dimension);
            var n = Pos(ref heldN, pos.dimension);
            while (queue.Count > 0)
            {
                var (x, y, z) = queue.Dequeue();
                p.Set(x, y, z);
                var ps = Liquids.Solid(ba, p); // read once for the six faces (F13)
                foreach (var face in BlockFacing.ALLFACES)
                {
                    n.Set(p).Add(face);
                    // nothing above the level, but flowing water of the liquid falling or running onto the level is a feed
                    // to follow upstream (HY-0008); the world's floor and a liquid-tight face are walls
                    if (n.Y > top)
                    {
                        if (traces.Count < MaxTraces && Liquids.Passes(ba, ps, p, n, face))
                        {
                            var fa = Liquids.Fluid(ba, n);
                            if (Liquids.Same(fa, b) && !Liquids.IsSource(fa)) traces.Add((n.X, n.Y, n.Z));
                        }
                        continue;
                    }
                    if (n.Y < 0 || seen.Contains((n.X, n.Y, n.Z)) || !Liquids.Passes(ba, ps, p, n, face)) continue;
                    var f = Liquids.Fluid(ba, n);
                    // a source of the liquid is water already held (and part of a body that would feed the space: HY-0007);
                    // a source of another liquid is a wall
                    if (Liquids.IsSource(f))
                    {
                        if (Liquids.Same(f, b) && seeds.Count < MaxSeeds) seeds.Add((n.X, n.Y, n.Z));
                        continue;
                    }
                    if ((n.Y == top && ++atLevel > MaxHeldLevel) || seen.Count >= MaxHeldSpace)
                    {
                        // open at or below the level; a channel fed by a large body still carries its water (HY-0008)
                        if (FedChannel(ba, pos, b, top, now, gen)) return true;
                        if (unheldMemo.Count > 4096) unheldMemo.Clear();
                        unheldMemo[key] = (now + UnheldMemoMs, gen);
                        HydrologyStats.Unheld++;
                        return false;
                    }
                    seen.Add((n.X, n.Y, n.Z));
                    queue.Enqueue((n.X, n.Y, n.Z));
                    if (n.Y < floor) floor = n.Y;
                }
            }
            if (SupplyFactor > 0 && !Supplied(ba, b, pos.dimension, seeds, traces, floor, (seen.Count + SupplyFactor - 1) / SupplyFactor))
            {
                if (unheldMemo.Count > 4096) unheldMemo.Clear();
                unheldMemo[key] = (now + UnheldMemoMs, gen);
                HydrologyStats.Unsupplied++;
                return false;
            }
            // every cell the search covered is held at its own height too: what it reaches below that is part of this space
            RememberHeld(seen, pos.dimension, b, now + HeldMemoMs, gen);
            return true;
        }

        /// <summary>Cells a fed channel's level may have (<see cref="FedChannel"/>), and the water that counts as a body
        /// feeding it without end: a lake or the sea (as H2 release's large body), or a natural river.</summary>
        const int MaxChannel = 4096, UnlimitedBody = 4096;

        /// <summary>Edge cells a fed channel may fall from: its outlet's width. A terrace whose rim is all edge holds no water
        /// at its level; flowing water runs over it.</summary>
        const int MaxChannelEdges = 8;

        [System.ThreadStatic] static HashSet<(int, int, int)> channelSeen;

        [System.ThreadStatic] static Queue<(int x, int y, int z, bool ground)> channelQueue;

        [System.ThreadStatic] static List<(int x, int y, int z)> channelSeeds, channelTraces;

        /// <summary>Cells a fed channel refused while the water under its level may still be filling, asked again after
        /// 3, 6, 12 and 24 s (<see cref="Retry"/>), then left: (dimension, x, y, z) -> (when, tries, first asked).</summary>
        static readonly Dictionary<(int, int, int, int), (long at, int tries, long born)> retries = new();

        const int MaxRetries = 4;

        static void AskAgainLater(BlockPos pos, long now)
        {
            var key = (pos.dimension, pos.X, pos.Y, pos.Z);
            retries.TryGetValue(key, out var r);
            if (r.tries >= MaxRetries || (r.tries == 0 && retries.Count >= 4096)) return;
            retries[key] = (now + (3000L << r.tries), r.tries + 1, r.tries == 0 ? now : r.born);
        }

        /// <summary>The server's tick: wakes the flowing cells whose time to be asked again has come.</summary>
        public static void Retry(IWorldAccessor world)
        {
            if (retries.Count == 0) return;
            long now = System.Environment.TickCount64;
            List<(int, int, int, int)> due = null, old = null;
            foreach (var (key, r) in retries)
            {
                if (r.at <= now) (due ??= new()).Add(key);
                else if (now - r.born > 120000) (old ??= new()).Add(key);
            }
            if (old != null) foreach (var key in old) retries.Remove(key);
            if (due == null) return;
            var ba = world.BlockAccessor;
            foreach (var key in due)
            {
                var r = retries[key];
                // waiting for its next refusal; given up after the last try
                if (r.tries >= MaxRetries) retries.Remove(key); else retries[key] = (long.MaxValue, r.tries, r.born);
                var p = new BlockPos(key.Item2, key.Item3, key.Item4, key.Item1);
                if (!Liquids.Loaded(ba, p)) continue;
                var f = Liquids.Fluid(ba, p);
                if (Liquids.Handles(f) && !Liquids.IsSource(f)) f.OnNeighbourBlockChange(world, p, p);
            }
        }

        /// <summary>
        /// HY-0008 (rc.9, decision 0008; the author dug a channel from a large lake to a drop, and the water stopped after a few
        /// blocks): a channel at the level of a large body carries that body's water to where it falls, though the space
        /// below its level is open (the drop, the slope beyond). The cells at <paramref name="top"/> joined to the cell
        /// sideways, through cells that rest on ground or on the liquid (an edge with nothing under it is where the water
        /// falls: it ends the channel there and is not followed, and there may be <see cref="MaxChannelEdges"/> of them),
        /// must be closed within <see cref="MaxChannel"/> cells by liquid-tight walls and sources, every edge must be a lip:
        /// reached from a cell resting on ground (a channel's floor ending at a drop, a dam's crest), not on the liquid
        /// (water over water that already runs out below stands no higher: a trench with a free outfall carries one
        /// layer, not its depth), and the water joined to them, followed upstream across falls and rapids, must be
        /// a body of <see cref="UnlimitedBody"/> sources or more, or a natural river. Then the channel's cells are held
        /// (remembered as <see cref="Held"/>'s answers are); the water falls at its edges as flowing water, and the reach
        /// below fills by its own rules. A pond's channel stays flowing water (decision 0006: a pond does not feed a stream
        /// for ever); a cell on open land at the level (no walls within the limit) is refused as before.
        /// </summary>
        static bool FedChannel(IBlockAccessor ba, BlockPos pos, Block b, int top, long now, int gen)
        {
            var seen = channelSeen ??= new HashSet<(int, int, int)>();
            var queue = channelQueue ??= new Queue<(int x, int y, int z, bool ground)>();
            // HY-0018: its own lists (it runs inside Held's search, whose lists it used to clear)
            var seeds = channelSeeds ??= new List<(int x, int y, int z)>();
            var traces = channelTraces ??= new List<(int x, int y, int z)>();
            seen.Clear();
            queue.Clear();
            seeds.Clear();
            traces.Clear();
            seen.Add((pos.X, pos.Y, pos.Z));
            queue.Enqueue((pos.X, pos.Y, pos.Z, true));
            var p = Pos(ref channelP, pos.dimension);
            var n = Pos(ref channelN, pos.dimension);
            int edges = 0;
            bool first = true;
            while (queue.Count > 0)
            {
                var (x, y, z, fromGround) = queue.Dequeue();
                p.Set(x, y, z);
                // the water under the cell (a reach's own sources) and flowing water onto it from above join the count
                n.Set(p).Down();
                var under = Liquids.Fluid(ba, n);
                var ps = Liquids.Solid(ba, p); // read once for its faces (F13)
                bool onGround = Liquids.GroundBelow(ba, p) || !Liquids.Passes(ba, ps, p, n, BlockFacing.DOWN);
                bool resting = onGround;
                if (Liquids.IsSource(under) && Liquids.Same(under, b))
                {
                    if (seeds.Count < MaxSeeds) seeds.Add((n.X, n.Y, n.Z));
                    resting = true;
                }
                if (first && !resting) return false;
                first = false;
                n.Set(p).Add(BlockFacing.UP);
                if (traces.Count < MaxTraces && Liquids.Passes(ba, ps, p, n, BlockFacing.UP))
                {
                    var above = Liquids.Fluid(ba, n);
                    if (Liquids.Same(above, b) && !Liquids.IsSource(above)) traces.Add((n.X, n.Y, n.Z));
                }
                // an edge: the water falls here; the channel is not followed beyond it
                if (!resting)
                {
                    // the water leaves over a lip of ground; off water that runs out below, this level does not stand (or
                    // not yet: the layer below may still be filling, as behind a new dam, so the cell is asked again)
                    if (!fromGround) { AskAgainLater(pos, now); return false; }
                    if (++edges > MaxChannelEdges) return false;
                    continue;
                }
                foreach (var face in H)
                {
                    n.Set(p).Add(face);
                    if (seen.Contains((n.X, n.Y, n.Z)) || !Liquids.Passes(ba, ps, p, n, face)) continue;
                    var f = Liquids.Fluid(ba, n);
                    if (Liquids.IsSource(f))
                    {
                        if (Liquids.Same(f, b) && seeds.Count < MaxSeeds) seeds.Add((n.X, n.Y, n.Z));
                        continue;
                    }
                    if (seen.Count >= MaxChannel) return false;
                    seen.Add((n.X, n.Y, n.Z));
                    queue.Enqueue((n.X, n.Y, n.Z, onGround));
                }
            }
            if (seeds.Count + traces.Count == 0 || !Supplied(ba, b, pos.dimension, seeds, traces, 0, UnlimitedBody)) return false;
            RememberHeld(seen, pos.dimension, b, now + HeldMemoMs, gen);
            HydrologyStats.FedChannel++;
            return true;
        }

        /// <summary>
        /// HY-0007 (decision 0006; a player broke the rim of a small pond on high ground, and the land below flooded several
        /// blocks deep): a held space fills only from water that could fill it. Sources stay endless, as in vanilla, so this
        /// bounds what one opening floods; it does not balance volumes. The water of the bodies bounding the space, counted
        /// from the space's floor up (water below the floor cannot rise into it), must be at least 1/<see cref="SupplyFactor"/>
        /// of the open space. A body with a natural river column (Earth's query) feeds any space, as a river is fed from
        /// upstream; the sea and a large lake pass by their size. So a pond breaking into a hollow below it, or onto closed
        /// flat land at its level, leaves flowing water there; a canal dug from a lake or a pool fills as before; a bucket
        /// emptied over a closed pond no longer raises it (the water at the new level is the bucket's). 0 turns it off.
        /// </summary>
        public static int SupplyFactor = 2;

        const int MaxSeeds = 1024;

        /// <summary>HY-0008: feeds followed upstream per question (<see cref="MaxTraces"/>), and the flowing cells one feed
        /// may cross (<see cref="MaxTraceCells"/>): a fall, rapids, a ramp of flowing water between a reach and the body
        /// above it.</summary>
        const int MaxTraces = 16, MaxTraceCells = 256;

        [System.ThreadStatic] static List<(int x, int y, int z)> heldSeeds, heldTraces, supplyTraces;

        [System.ThreadStatic] static HashSet<(int, int, int)> traceSeen;

        [System.ThreadStatic] static Queue<(int x, int y, int z)> traceQueue;

        [System.ThreadStatic] static HashSet<(int, int, int)> bodySeen;

        [System.ThreadStatic] static HashSet<(int, int)> bodyColumns;

        [System.ThreadStatic] static Queue<(int x, int y, int z)> bodyQueue;

        /// <summary>The sources of <paramref name="b"/>'s liquid joined to <paramref name="seeds"/> at or above
        /// <paramref name="floor"/> are at least <paramref name="space"/> / <see cref="SupplyFactor"/>, or one of them stands
        /// in a natural river column. HY-0008 (rc.9): when the sources joined directly are too few, the count follows the
        /// flowing water that feeds them upstream (<paramref name="traces"/>, the flowing water over the space, and the
        /// flowing water over any source counted) across falls, rapids and ramps to the body above, and counts that body
        /// too: a reach below a drop is fed by the lake the channel comes from, not by the few sources under the fall.</summary>
        static bool Supplied(IBlockAccessor ba, Block b, int dim, List<(int x, int y, int z)> seeds, List<(int x, int y, int z)> traces, int floor, int need)
        {
            var seen = bodySeen ??= new HashSet<(int, int, int)>();
            var columns = bodyColumns ??= new HashSet<(int, int)>();
            var queue = bodyQueue ??= new Queue<(int x, int y, int z)>();
            var feeds = supplyTraces ??= new List<(int x, int y, int z)>();
            var traced = traceSeen ??= new HashSet<(int, int, int)>();
            seen.Clear();
            columns.Clear();
            queue.Clear();
            feeds.Clear();
            traced.Clear();
            bool river = EarthRiverQuery.Active && dim == 0;
            foreach (var c in seeds) if (c.y >= floor && seen.Add(c)) queue.Enqueue(c);
            foreach (var c in traces) feeds.Add(c);
            var p = Pos(ref supplyP, dim);
            var n = Pos(ref supplyN, dim);
            int nextFeed = 0;
            while (true)
            {
                while (queue.Count > 0)
                {
                    if (seen.Count >= need) return true;
                    var (x, y, z) = queue.Dequeue();
                    p.Set(x, y, z);
                    if (river && columns.Add((x, z)) && EarthRiverQuery.River(p)) return true;
                    foreach (var face in BlockFacing.ALLFACES)
                    {
                        n.Set(p).Add(face);
                        if (n.Y < floor || seen.Contains((n.X, n.Y, n.Z)) || !Liquids.Passes(ba, p, n, face)) continue;
                        var f = Liquids.Fluid(ba, n);
                        if (!Liquids.Same(f, b)) continue;
                        if (!Liquids.IsSource(f))
                        {
                            // flowing water running onto a counted source from above or beside: a feed to follow later
                            if (face != BlockFacing.DOWN && feeds.Count < MaxTraces * 4 && !traced.Contains((n.X, n.Y, n.Z))) feeds.Add((n.X, n.Y, n.Z));
                            continue;
                        }
                        seen.Add((n.X, n.Y, n.Z));
                        queue.Enqueue((n.X, n.Y, n.Z));
                    }
                }
                if (seen.Count >= need) return true;
                // too few joined directly: follow the next feed upstream; the sources it reaches join the count
                if (nextFeed >= feeds.Count || nextFeed >= MaxTraces) return false;
                var start = feeds[nextFeed++];
                TraceUp(ba, b, dim, start, floor, seen, queue, traced);
            }
        }

        /// <summary>From flowing water at <paramref name="start"/>, the flowing water of the liquid it comes from, at its
        /// height or above (water does not run uphill), up to <see cref="MaxTraceCells"/> cells; the sources of the liquid
        /// it touches at or above <paramref name="floor"/> join <paramref name="seen"/> and <paramref name="queue"/>.</summary>
        static void TraceUp(IBlockAccessor ba, Block b, int dim, (int x, int y, int z) start, int floor,
            HashSet<(int, int, int)> seen, Queue<(int x, int y, int z)> queue, HashSet<(int, int, int)> traced)
        {
            if (!traced.Add(start)) return;
            var q = traceQueue ??= new Queue<(int x, int y, int z)>();
            q.Clear();
            q.Enqueue(start);
            int cells = 1;
            var p = Pos(ref traceP, dim);
            var n = Pos(ref traceN, dim);
            while (q.Count > 0)
            {
                var (x, y, z) = q.Dequeue();
                p.Set(x, y, z);
                foreach (var face in BlockFacing.ALLFACES)
                {
                    if (face == BlockFacing.DOWN) continue;
                    n.Set(p).Add(face);
                    var key = (n.X, n.Y, n.Z);
                    if (n.Y < start.y || seen.Contains(key) || traced.Contains(key) || !Liquids.Passes(ba, p, n, face)) continue;
                    var f = Liquids.Fluid(ba, n);
                    if (!Liquids.Same(f, b)) continue;
                    if (Liquids.IsSource(f))
                    {
                        if (n.Y >= floor) { seen.Add(key); queue.Enqueue(key); }
                        continue;
                    }
                    if (cells >= MaxTraceCells) continue;
                    traced.Add(key);
                    cells++;
                    q.Enqueue(key);
                }
            }
        }

        /// <summary>A new world in the same process (singleplayer): no remembered answer carries over.</summary>
        static void ForgetHeld() { System.Threading.Interlocked.Increment(ref heldGeneration); retries.Clear(); liveSearches.Clear(); linked.Clear(); }

        /// <summary>The water moved in a box (H2 release): what was held or fed there a moment ago may not be. Only the box:
        /// forgetting every answer would change fills elsewhere that are still running on remembered answers.</summary>
        public static void ForgetHeldNear(int dim, int x0, int y0, int z0, int x1, int y1, int z1)
        {
            bool In((int, int, int, int) key) => key.Item1 >> 1 == dim && key.Item2 >= x0 && key.Item2 <= x1 && key.Item3 >= y0 && key.Item3 <= y1
                && key.Item4 >= z0 && key.Item4 <= z1;
            var drop = new List<(int, int, int, int)>();
            foreach (var key in heldMemo.Keys) if (In(key)) drop.Add(key);
            foreach (var key in drop) heldMemo.Remove(key);
            drop.Clear();
            foreach (var key in unheldMemo.Keys) if (In(key)) drop.Add(key);
            foreach (var key in drop) unheldMemo.Remove(key);
        }
    }
}
