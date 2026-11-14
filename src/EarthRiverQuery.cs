using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hydrology
{
    /// <summary>
    /// Earth's river flow query (decision Earth 0027; versions 1 to 3), bound by reflection so Hydrology runs without Earth
    /// (Earth's public interface, not the game's: <see cref="GamePatches"/> covers the game). Earth's mod system
    /// <c>EarthMap.EarthMapSystem</c> offers:
    /// - <c>int RiverQueryVersion { get; }</c>: the world's version, fixed once it is loaded: 1 for pool layout 8 (steps
    ///   without sills), 2 for layouts 9 and 10, 3 for layout 11; 0 for older layouts, or where Earth is off;
    /// - <c>bool RiverAt(int x, int z, out bool rapids, out double slope, out double discharge)</c>: true where the column
    ///   is natural river water as generated (answers are kept per column: they are geography); <c>rapids</c> where its
    ///   reach is a rapids reach (constant along the reach; from version 3 where Earth drew rapid water: a rapids reach's
    ///   drops, the ramps below its steps and the steps' edges, and the lips of falls, so the pools between them stay
    ///   ordinary water); the reach's water fall per block; discharge in m³/s. Server side. Hydrology keeps every answer,
    ///   so a column costs one call; Earth's first answer in a chunk can cost more (Earth 2.0.0-rc.2 settles the chunk's
    ///   columns to answer from what it drew).
    /// - version 2 (Earth's pool layout 9, Hydrology 0.1.10): <c>int RiverFlowAt(int x, int z)</c>, the river's flow at a
    ///   river column as Earth labels its sources (0..7 for n, ne, e, se, s, sw, w, nw), -1 elsewhere.
    /// - version 3 (Earth's pool layout 11, Hydrology 0.6.2, decision Earth 0046): the same call; Earth labels the top source
    ///   of every river column with it, calm reaches' as ordinary water, so Hydrology keeps the label in every river column.
    /// The other way, Earth reads <see cref="HydrologySystem.EarthRiverQueryVersion"/>. Where the version is below 1, or
    /// Earth is absent, nothing here answers and Hydrology keeps 0.1.7's rules (Earth's older layouts keep their sills).
    /// - optional, any version (Earth after 2.0.0-rc.1): <c>bool RiverSpanAt(int x, int z, out int bed, out int waterTop)</c>,
    ///   the river column's bed and water surface; with it the answers hold only in the river's own water (see SpanAbove),
    ///   without it at any height in the column (a channel on a bridge over a calm river then took the river's label).
    /// Only dimension 0 (the world itself) has rivers.
    /// </summary>
    public static class EarthRiverQuery
    {
        delegate bool RiverAtFn(int x, int z, out bool rapids, out double slope, out double discharge);
        delegate bool RiverSpanFn(int x, int z, out int bed, out int waterTop);

        static RiverAtFn riverAt;
        static System.Func<int, int, int> riverFlowAt;
        static RiverSpanFn riverSpanAt;
        static ILogger log;
        static bool failureLogged;

        /// <summary>The world's query version as Hydrology reads it (0: no query; at most
        /// <see cref="HydrologySystem.EarthRiverQueryVersion"/>).</summary>
        public static int Version { get; private set; }

        /// <summary>The query answers: Earth is present and the world's version is at least 1.</summary>
        public static bool Active => riverAt != null;

        /// <summary>Calls to Earth so far, and lookups answered from the cache.</summary>
        public static long Reads, CacheHits;

        // The answers are geography, fixed for the world: kept per column, cleared when large. Every caller is on the
        // server's main thread (the rules, the label hook and the wheels); the lock only keeps that from mattering.
        struct Answer { public bool River, Rapids; public double Slope, Discharge; public sbyte Flow; public int Bed, WaterTop; }

        /// <summary>
        /// With Earth's RiverSpanAt (Earth 2.0.0-rc.2 and later): the rules for flowing water apply only from the column's
        /// bed to this many blocks above its water surface: the ramp below a step lies one block above it, and water spilt
        /// over a step's edge in Earth's layouts 8 and 9 one more. The rules for sources (their flow label, rapid sources)
        /// apply only up to the surface itself: a river's sources never stand above it (rc.4, audit: a source on a bridge
        /// deck one block over a calm river took the river's label). Water above or below in the same column (a channel on a
        /// bridge over the river, a cave pool under its bed) is no river water.
        /// </summary>
        const int SpanAbove = 2;
        // HY-0016: answers kept per chunk column (32 x 32), the least recently used chunk evicted past MaxChunks (about 10 MB),
        // instead of the whole cache emptied at 65,536 columns
        sealed class ChunkAnswers
        {
            public readonly Answer[] answers = new Answer[1024];
            public readonly ulong[] has = new ulong[16];
            public long used;
        }
        static readonly Dictionary<long, ChunkAnswers> cache = new(new ColumnKey());
        const int MaxChunks = 256;
        static long useClock;
        /// <summary>HY-0016: columns whose RiverAt threw (not "not ready"), not asked again until the time kept (ms).</summary>
        static readonly Dictionary<long, long> failedUntil = new(new ColumnKey());
        const long FailedRetryMs = 5000;
        /// <summary>HY-0016: Earth's errors in a breaker: 50 within a minute unbind the query (the rules run as without Earth).</summary>
        static readonly GamePatches.Breaker Failures = new("Earth's river query", () =>
        {
            log?.Error("Hydrology: Earth's river query keeps failing: it is unbound for this world, and the water rules run as without Earth");
            Unbind();
            // Fable re-review: a world without the hydrologyFlooding key whose vessels came from Earth's "with the river
            // query" flag kept them on (cached) while the river guards went off with the query: the sill-less steps could
            // flood. The vessels answer is read again (now without the query) and the clients are told, on the game's next
            // tick (the second re-review: not in the middle of the liquid update whose query call failed)
            // (Fable's third re-review) the cached vessels answer goes at once: two field writes, safe inside a liquid update;
            // without them vessels could fill river columns for up to half a second with the river guards already off
            Rules.H2Filling.ForgetWorldVessels();
            unboundPending = true;
        });

        static volatile bool unboundPending;

        /// <summary>The query was unbound at run time (its breaker) since the last call: the game re-reads what depends on
        /// it. True once per unbinding.</summary>
        public static bool TakeUnbound()
        {
            if (!unboundPending) return false;
            unboundPending = false;
            return true;
        }

        static bool CacheGet(long key, int x, int z, out Answer a)
        {
            long ck = ((long)(x >> 5) << 32) ^ (uint)(z >> 5);
            int i = ((z & 31) << 5) | (x & 31);
            if (cache.TryGetValue(ck, out var c) && (c.has[i >> 6] & (1UL << (i & 63))) != 0)
            {
                c.used = ++useClock;
                a = c.answers[i];
                return true;
            }
            a = default;
            return false;
        }

        static void CachePut(int x, int z, Answer a)
        {
            long ck = ((long)(x >> 5) << 32) ^ (uint)(z >> 5);
            int i = ((z & 31) << 5) | (x & 31);
            if (!cache.TryGetValue(ck, out var c))
            {
                if (cache.Count >= MaxChunks)
                {
                    long oldest = 0, when = long.MaxValue;
                    foreach (var kv in cache) if (kv.Value.used < when) { when = kv.Value.used; oldest = kv.Key; }
                    cache.Remove(oldest);
                }
                cache[ck] = c = new ChunkAnswers();
            }
            c.answers[i] = a;
            c.has[i >> 6] |= 1UL << (i & 63);
            c.used = ++useClock;
        }

        /// <summary>A column's key, X in the high half and Z in the low; hashed with a multiply, since long's own hash
        /// (X ^ Z) gives one bucket to every column on a diagonal and long chains in a compact area.</summary>
        sealed class ColumnKey : IEqualityComparer<long>
        {
            public bool Equals(long a, long b) => a == b;
            public int GetHashCode(long k) => (int)((ulong)(k * unchecked((long)0x9E3779B97F4A7C15)) >> 32);
        }

        /// <summary>
        /// Binds to Earth's mod system for the loaded world. Called once the game runs (after every mod's SaveGameLoaded,
        /// so Earth has fixed the world's version). Never throws: an Earth whose interface does not match stays unbound.
        /// </summary>
        public static void Bind(ICoreServerAPI api, ILogger logger)
        {
            Unbind();
            log = logger;
            Failures.Reset(logger);
            unboundPending = false;
            try
            {
                var earth = api.ModLoader.GetModSystem("EarthMap.EarthMapSystem");
                if (earth == null) return;
                var t = earth.GetType();
                var versionProp = t.GetProperty("RiverQueryVersion", BindingFlags.Public | BindingFlags.Instance);
                var dbl = typeof(double).MakeByRefType();
                var method = t.GetMethod("RiverAt", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int), typeof(bool).MakeByRefType(), dbl, dbl }, null);
                if (versionProp == null || versionProp.PropertyType != typeof(int) || method == null || method.ReturnType != typeof(bool))
                {
                    log.Notification("Hydrology: Earth offers no river query; its sills keep the steps");
                    return;
                }
                int version;
                try { version = (int)versionProp.GetValue(earth); }
                catch (Exception e) { log.Warning("Hydrology: Earth's river query version unreadable: {0}", e.Message); return; }
                if (version < 1)
                {
                    log.Notification("Hydrology: this world's river query version is {0}: the steps have sills, the query rules are off", version);
                    return;
                }
                var at = (RiverAtFn)Delegate.CreateDelegate(typeof(RiverAtFn), earth, method);
                var flow = t.GetMethod("RiverFlowAt", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int), typeof(int) }, null);
                System.Func<int, int, int> flowAt = null;
                // the optional methods each in their own try: one that cannot be bound costs only itself (rc.4, audit)
                if (version >= 2 && flow != null && flow.ReturnType == typeof(int))
                    try { flowAt = (System.Func<int, int, int>)Delegate.CreateDelegate(typeof(System.Func<int, int, int>), earth, flow); }
                    catch (Exception e) { log.Warning("Hydrology: Earth's RiverFlowAt could not be bound, rivers keep no flow labels: {0}", e.Message); }
                int read = flowAt != null ? version : Math.Min(version, 1);
                if (read > HydrologySystem.EarthRiverQueryVersion)
                {
                    log.Warning("Hydrology: this world's river query is version {0}, made for a newer Hydrology; this one reads it as version {1}. Install the Hydrology the world was made with.",
                        read, HydrologySystem.EarthRiverQueryVersion);
                    read = HydrologySystem.EarthRiverQueryVersion;
                }
                var span = t.GetMethod("RiverSpanAt", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() }, null);
                RiverSpanFn spanAt = null;
                if (span != null && span.ReturnType == typeof(bool))
                    try { spanAt = (RiverSpanFn)Delegate.CreateDelegate(typeof(RiverSpanFn), earth, span); }
                    catch (Exception e) { log.Warning("Hydrology: Earth's RiverSpanAt could not be bound, river rules hold at any height: {0}", e.Message); }
                riverAt = at;
                riverFlowAt = flowAt;
                riverSpanAt = spanAt;
                Version = read;
                log.Notification("Hydrology: Earth's river query version {0} bound{1}{2}", Version, riverFlowAt != null ? " (with the river's flow)" : "",
                    riverSpanAt != null ? " (with its span: river rules only in the river's own water)" : " (no span: river rules at any height in a river column)");
            }
            catch (Exception e)
            {
                Unbind();
                log.Warning("Hydrology: Earth's river query could not be bound, the query rules are off: {0}", e.Message);
            }
        }

        /// <summary>Forgets the binding, its answers and its counters (a new world, or the server stopping).</summary>
        public static void Unbind()
        {
            riverAt = null;
            riverFlowAt = null;
            riverSpanAt = null;
            Version = 0;
            failureLogged = false;
            notReadyLogged = maxWaitLogged = false;
            lock (cache) { cache.Clear(); failedUntil.Clear(); }
            lock (notReady) notReady.Clear();
            Reads = CacheHits = 0;
        }

        /// <summary>
        /// rc.5 (Earth 2.0.0-rc.3, its RC session's proposal): Earth throws its <c>RiverQueryNotReadyException</c> (found by its
        /// type name) where its drawn answer for a column is not ready yet, a height tile still to come (rare). Hydrology then
        /// leaves the water there as it is, instead of taking the column for no river for that update: the column's water
        /// updates wait (<see cref="Defer"/>) and are tried again every <see cref="RetryMs"/>, and a source beside it keeps
        /// its river label (<see cref="NotReady"/>). For at most <see cref="MaxWaitMs"/> per column: after that the water
        /// updates without an answer, as before rc.5, and Earth is asked again each time.
        /// </summary>
        public const string NotReadyException = "RiverQueryNotReadyException";
        /// <summary>How long a waiting update waits before it is tried again, and the longest a column's water waits.</summary>
        public const int RetryMs = 1000;
        public const long MaxWaitMs = 60000;
        // column key -> when Earth last and first said "not ready" (milliseconds, real time); a "not ready" stands this long
        const long NotReadyMemoMs = RetryMs;
        static readonly Dictionary<long, (long last, long first)> notReady = new(new ColumnKey());
        static bool notReadyLogged, maxWaitLogged;

        static bool IsNotReady(Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
                if (x.GetType().Name == NotReadyException) return true;
            return false;
        }

        static void MarkNotReady(long key)
        {
            long now = Environment.TickCount64;
            lock (notReady) notReady[key] = notReady.TryGetValue(key, out var n) ? (now, n.first) : (now, now);
            if (notReadyLogged) return;
            notReadyLogged = true;
            log?.Notification("Hydrology: Earth's river answer is not ready yet in some columns (a height tile still to come); the water there waits for it (logged once)");
        }

        /// <summary>Earth said within the last second that <paramref name="pos"/>'s column is not ready yet.</summary>
        public static bool NotReady(BlockPos pos)
        {
            if (riverAt == null || pos.dimension != 0) return false;
            long key = ((long)pos.X << 32) ^ (uint)pos.Z;
            lock (notReady) return notReady.Count > 0 && notReady.TryGetValue(key, out var n) && Environment.TickCount64 - n.last < NotReadyMemoMs;
        }

        /// <summary>The water update at <paramref name="pos"/> should wait: Earth's answer for its column is not ready yet, and
        /// has not been for longer than <see cref="MaxWaitMs"/>. Asks Earth where the column has no kept answer.</summary>
        public static bool Defer(BlockPos pos)
        {
            if (riverAt == null || pos.dimension != 0) return false;
            ColumnAnswer(pos, out _);
            long key = ((long)pos.X << 32) ^ (uint)pos.Z, now = Environment.TickCount64;
            lock (notReady)
            {
                if (notReady.Count == 0 || !notReady.TryGetValue(key, out var n) || now - n.last >= NotReadyMemoMs) return false;
                if (now - n.first <= MaxWaitMs) return true;
            }
            if (!maxWaitLogged)
            {
                maxWaitLogged = true;
                log?.Warning("Hydrology: Earth's river answer has not been ready for a minute in some columns; the water there updates without the river rules until Earth answers (logged once)");
            }
            return false;
        }

        /// <summary>The query for a column; false where it does not answer (no query, another dimension, an error).</summary>
        public static bool At(BlockPos pos, out bool rapids, out double slope, out double discharge) =>
            At(pos, out rapids, out slope, out discharge, out _);

        /// <summary><see cref="At(BlockPos, out bool, out double, out double)"/> with the river's flow (-1: none or version 1).
        /// With Earth's span, only for a position in the river's own water: up to <paramref name="above"/> blocks over its
        /// surface (<see cref="SpanAbove"/> for flowing water, 0 for a source).</summary>
        public static bool At(BlockPos pos, out bool rapids, out double slope, out double discharge, out int flow, int above = SpanAbove)
        {
            rapids = false; slope = 0; discharge = 0; flow = -1;
            if (!ColumnAnswer(pos, out var a) || !a.River) return false;
            if (a.Bed != NoSpan && (pos.Y < a.Bed || pos.Y > a.WaterTop + above)) return false;
            rapids = a.Rapids; slope = a.Slope; discharge = a.Discharge; flow = a.Flow;
            return true;
        }

        /// <summary>The column's answer for a block reaching <paramref name="reach"/> blocks up and down from
        /// <paramref name="pos"/> (H8: a wheel's axle stands above the water its rim dips into). With Earth's span the block
        /// must reach the river's own water (rc.4, audit: a wheel in an aqueduct over a big river, or in a cave stream under
        /// it, took the river's power); without it, at any height in the column.</summary>
        public static bool AtColumn(BlockPos pos, int reach, out bool rapids, out double slope, out double discharge)
        {
            rapids = false; slope = 0; discharge = 0;
            if (!ColumnAnswer(pos, out var a) || !a.River) return false;
            if (a.Bed != NoSpan && (pos.Y + reach < a.Bed || pos.Y - reach > a.WaterTop + SpanAbove)) return false;
            rapids = a.Rapids; slope = a.Slope; discharge = a.Discharge;
            return true;
        }

        const int NoSpan = int.MinValue;

        /// <summary>The answer for <paramref name="pos"/>'s column, from the cache or from Earth; false where the query does not
        /// answer (no query, another dimension, an error).</summary>
        static bool ColumnAnswer(BlockPos pos, out Answer a)
        {
            a = default;
            var f = riverAt;
            var flowAt = riverFlowAt;
            var spanAt = riverSpanAt;
            if (f == null || pos.dimension != 0) return false;
            long key = ((long)pos.X << 32) ^ (uint)pos.Z;
            lock (cache)
            {
                if (CacheGet(key, pos.X, pos.Z, out a))
                {
                    CacheHits++;
                    return true;
                }
                // HY-0016: a column whose RiverAt threw is not asked again for a few seconds (each throw costs more than an update)
                if (failedUntil.Count > 0 && failedUntil.TryGetValue(key, out long until))
                {
                    if (Environment.TickCount64 < until) return false;
                    failedUntil.Remove(key);
                }
            }
            Reads++;
            // an Earth that throws gives no answer this time, and nothing is kept: the column is asked again next time;
            // "not ready yet" (rc.5) is no failure: it is noted, so the column's water waits (Defer)
            try { a.River = f(pos.X, pos.Z, out a.Rapids, out a.Slope, out a.Discharge); }
            catch (Exception e)
            {
                if (IsNotReady(e)) MarkNotReady(key);
                else
                {
                    lock (cache)
                    {
                        if (failedUntil.Count >= MaxChunks * 1024) failedUntil.Clear();
                        failedUntil[key] = Environment.TickCount64 + FailedRetryMs;
                    }
                    Failed("RiverAt", e);
                }
                return false;
            }
            a.Flow = -1;
            a.Bed = NoSpan;
            bool keep = true;
            if (a.River)
            {
                // a failing flow costs only the label, a failing span only the height check, for this answer: the column
                // stays a river (H2's refusals keep protecting it), and is asked again next time (rc.4, Codex: a span that
                // failed once was kept, and the column answered at any height until the cache was cleared)
                if (flowAt != null)
                    try { a.Flow = (sbyte)flowAt(pos.X, pos.Z); }
                    catch (Exception e) { if (!IsNotReady(e)) Failed("RiverFlowAt", e); keep = false; }
                if (spanAt != null)
                    try
                    {
                        // a span whose bed lies over its surface (Earth draws about one chute column in 8 so) is no span
                        if (!spanAt(pos.X, pos.Z, out a.Bed, out a.WaterTop) || a.Bed > a.WaterTop) a.Bed = NoSpan;
                    }
                    catch (Exception e) { a.Bed = NoSpan; if (!IsNotReady(e)) Failed("RiverSpanAt", e); keep = false; }
            }
            else { a = default; a.Flow = -1; a.Bed = NoSpan; }
            if (notReady.Count > 0) lock (notReady) notReady.Remove(key);
            if (!keep) return true;
            lock (cache) CachePut(pos.X, pos.Z, a);
            return true;
        }

        static void Failed(string call, Exception e)
        {
            Failures.Fault(e);
            if (failureLogged) return;
            failureLogged = true;
            // the breaker has logged the error with its stack trace; this says which call and what happens next
            log?.Warning("Hydrology: Earth's river query ({0}) failed (the error is logged above); a failing column rests 5 s, and 50 failures within a minute unbind the query", call);
        }

        /// <summary>The column is natural river water as Earth generated it.</summary>
        public static bool River(BlockPos pos) => At(pos, out _, out _, out _);

        /// <summary>
        /// rc.11 (EA-0066, decision 0009): <paramref name="pos"/> is in the river's own water, at or below its generated
        /// surface (with Earth's span; without it, at any height in the column, as before). Water a dam holds above the
        /// surface is not: Earth's query describes the river as generated, and what players raise over it is ordinary water
        /// that stands where it is held and drains when the dam opens.
        /// </summary>
        public static bool RiverWater(BlockPos pos) => At(pos, out _, out _, out _, out _, 0);

        /// <summary>The column is a natural river's, at any height (rc.11: water a dam raised over the river stands in it).</summary>
        public static bool RiverColumn(BlockPos pos) => ColumnAnswer(pos, out var a) && a.River;

        /// <summary>The column is natural river water in a rapids reach (version 3: where Earth drew rapid water); for a source
        /// (<paramref name="source"/>) only up to the river's surface.</summary>
        public static bool Rapids(BlockPos pos, bool source = false) =>
            At(pos, out bool rapids, out _, out _, out _, source ? 0 : SpanAbove) && rapids;

        /// <summary>The flow a source here keeps (0..7, n to nw): from version 3 in every natural river column, in version 2
        /// only in a rapids reach, up to the river's surface; -1 elsewhere.</summary>
        public static int LabelledFlow(BlockPos pos) =>
            At(pos, out bool rapids, out _, out _, out int flow, 0) && (rapids || Version >= 3) ? flow : -1;
    }
}
