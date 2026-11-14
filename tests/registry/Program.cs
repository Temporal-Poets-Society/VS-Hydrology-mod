// The game-patch registry (Hydrology's src/GamePatches.cs) against the installed game's own DLLs: every real target
// resolves and is patched (H5's water rules and refinements, H8, C1), the chunk-pack fix's targets resolve, and a
// missing, changed or refused target switches off only its feature, logged once, with nothing left half-patched.
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Hydrology;
using Hydrology.Rules;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace RegistryTests
{
    sealed class TestLogger : LoggerBase
    {
        public readonly List<(EnumLogType type, string text)> Lines = new();
        protected override void LogImpl(EnumLogType logType, string format, params object[] args) =>
            Lines.Add((logType, args == null || args.Length == 0 ? format : string.Format(format, args)));
        public int Warnings(string about) => Lines.Count(l => l.type == EnumLogType.Warning && l.text.Contains(about));
    }

    public class Target
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public int A(int x) => x + 1;
        [MethodImpl(MethodImplOptions.NoInlining)] public int B(int y) => y * 2;
    }

    public static class FakePatches
    {
        public static void PrefixA(int x) { }
        public static void PrefixB(int y) { }
        // binds a parameter the target does not have: Harmony refuses the patch
        public static void PrefixRenamed(int notAParameter) { }
    }

    public static class Program
    {
        static int checks, failures;
        static void Check(bool ok, string name)
        {
            checks++;
            if (!ok) { failures++; Console.WriteLine("FAIL " + name); }
        }

        static Patches Info(MethodBase m) => Harmony.GetPatchInfo(m);
        static bool PatchedBy(MethodBase m, string owner, string kind)
        {
            var p = Info(m);
            if (p == null) return false;
            var list = kind switch { "prefix" => p.Prefixes, "postfix" => p.Postfixes, _ => p.Finalizers };
            return list.Any(x => x.owner == owner);
        }

        public static int Main()
        {
            RealTargets();
            Failures();
            RiverSpan();
            QueryFailures();
            Console.WriteLine($"{checks - failures}/{checks} checks passed; {failures} failed");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// HY-0015: the bodies of the vanilla methods Hydrology patches or re-implements (H7's feed check replaces
        /// GetMaxNeighbourLiquidLevel; H5's prefixes encode vanilla's control flow). A game update can keep a signature and
        /// change what a method does: each body's IL is hashed and compared with il-hashes.txt for the running game version.
        /// A version not in the file is recorded there (commit it after re-reading the methods in devtools/vs-ref).
        /// </summary>
        static void ILBodies((MethodInfo m, string name)[] methods)
        {
            string version = GamePatches.RunningGameVersion();
            string file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "il-hashes.txt"));
            var known = new Dictionary<string, string>();
            if (File.Exists(file))
                foreach (var line in File.ReadAllLines(file))
                {
                    var parts = line.Split(' ');
                    if (parts.Length == 3 && parts[0] == version) known[parts[1]] = parts[2];
                }
            var record = new List<string>();
            foreach (var (m, name) in methods)
            {
                if (m == null) continue;
                var il = m.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(il)).ToLowerInvariant();
                if (known.TryGetValue(name, out var expected))
                    Check(expected == hash, $"game {version}: {name}'s body is the one Hydrology was written against" +
                        (expected == hash ? "" : $" (it changed: re-read {name} in devtools/vs-ref before releasing)"));
                else record.Add($"{version} {name} {hash}");
            }
            if (record.Count > 0)
            {
                File.AppendAllLines(file, record);
                // Fable re-review: a new game version is the case this test exists for: the run fails until the bodies are
                // read again (the next run compares against what this one recorded)
                Check(false, $"game {version}: {record.Count} method bodies recorded in {file}; re-read them in devtools/vs-ref, then commit the file and run again");
            }
        }

        static void RealTargets()
        {
            var log = new TestLogger();
            var t = typeof(BlockBehaviorFiniteSpreadingLiquid);
            MethodInfo M(string name, params Type[] args) => AccessTools.DeclaredMethod(t, name, args);
            var spread = M("TrySpreadDownwards", typeof(IWorldAccessor), typeof(Block), typeof(Block), typeof(BlockPos));
            var remove = M("RemoveOtherLowerNeighbours", typeof(Block), typeof(BlockPos), typeof(BlockFacing), typeof(IWorldAccessor));
            var update = M("OnDelayedWaterUpdateCheck", typeof(IWorldAccessor), typeof(BlockPos), typeof(float));
            var label = M("GetLiquidBlockId", typeof(IWorldAccessor), typeof(BlockPos), typeof(Block), typeof(int));
            var feed = M("GetMaxNeighbourLiquidLevel", typeof(Block), typeof(IWorldAccessor), typeof(BlockPos), typeof(bool).MakeByRefType());
            var flowDir = M("updateOwnFlowDir", typeof(Block), typeof(IWorldAccessor), typeof(BlockPos));
            var wheel = AccessTools.DeclaredMethod(typeof(BEBehaviorMPWaterWheel), "CheckWater", new[] { typeof(float) });
            foreach (var (m, n) in new[] { (spread, "TrySpreadDownwards"), (remove, "RemoveOtherLowerNeighbours"), (update, "OnDelayedWaterUpdateCheck"),
                         (label, "GetLiquidBlockId"), (feed, "GetMaxNeighbourLiquidLevel"), (flowDir, "updateOwnFlowDir"), (wheel, "CheckWater") })
                Check(m != null, "game 1.22: " + n + " exists with the signature Hydrology patches");
            ILBodies(new[] { (spread, "TrySpreadDownwards"), (remove, "RemoveOtherLowerNeighbours"), (update, "OnDelayedWaterUpdateCheck"),
                (label, "GetLiquidBlockId"), (feed, "GetMaxNeighbourLiquidLevel"), (flowDir, "updateOwnFlowDir"), (wheel, "CheckWater") });

            H5LiquidHooks.Apply(log);
            const string Id = "hydrology.liquids";
            Check(PatchedBy(spread, Id, "prefix"), "H5: TrySpreadDownwards prefix");
            Check(PatchedBy(remove, Id, "prefix"), "H5: RemoveOtherLowerNeighbours prefix");
            Check(PatchedBy(update, Id, "prefix") && PatchedBy(update, Id, "postfix") && PatchedBy(update, Id, "finalizer"),
                "H5: OnDelayedWaterUpdateCheck prefix, postfix and finalizer");
            Check(PatchedBy(label, Id, "postfix"), "H5: GetLiquidBlockId postfix");
            Check(PatchedBy(feed, Id, "prefix"), "H5: GetMaxNeighbourLiquidLevel prefix");
            Check(PatchedBy(flowDir, Id, "postfix"), "H5: updateOwnFlowDir postfix");
            Check(PatchedBy(wheel, Id, "postfix"), "H8: CheckWater postfix (flowRate bound)");
            Check(!log.Lines.Any(l => l.type == EnumLogType.Warning || l.type == EnumLogType.Error), "no warning or error on 1.22: " +
                string.Join(" | ", log.Lines.Where(l => l.type != EnumLogType.Notification).Select(l => l.text)));
            Check(log.Lines.Any(l => l.text.Contains("liquid hooks applied")), "the start-up line says the hooks are applied");
            Check(H7Settling.FallLabel && H7Settling.NoSideFeedFromFalling && H7Settling.WakeOnFallLabel && H4Rapids.QueryFlowLabels,
                "every refinement's switch stays on");
            // HY-0015: the side-feed hook's breaker trips: level relax goes off with it
            var feedBreaker = (GamePatches.Breaker)typeof(H5LiquidHooks).GetField("Feed", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            H7Settling.LevelRelax = true;
            for (int i = 0; i < GamePatches.MaxFaults; i++) feedBreaker.Fault(new InvalidOperationException("test"));
            Check(!H7Settling.NoSideFeedFromFalling && !H7Settling.LevelRelax, "HY-0015: with the side-feed rule off, level relax is off too");
            H7Settling.NoSideFeedFromFalling = true; H7Settling.LevelRelax = true;
            Check(H5LiquidHooks.WaterRulesOn, "HY-0012: the water rules are on once applied");
            // their breaker trips (50 errors within a minute): H2 release, which relies on them, sees them off
            var water = (GamePatches.Breaker)typeof(H5LiquidHooks).GetField("Water", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            for (int i = 0; i < GamePatches.MaxFaults; i++) water.Fault(new InvalidOperationException("test"));
            Check(water.Off && !H5LiquidHooks.WaterRulesOn, "HY-0012: the water rules' breaker tripped: they read as off");
            H5LiquidHooks.Apply(log);
            Check(H5LiquidHooks.WaterRulesOn, "HY-0012: a new world's apply resets the breaker");
            H5LiquidHooks.Remove();
            Check(!H5LiquidHooks.WaterRulesOn, "HY-0012: removed, the water rules read as off");
            Check(new[] { spread, remove, update, label, feed, flowDir, wheel }.All(m => !PatchedBy(m, Id, "prefix") && !PatchedBy(m, Id, "postfix")),
                "Remove unpatches every server hook");

            var tick = AccessTools.DeclaredMethod(typeof(BlockWaterflowing), nameof(BlockWaterflowing.OnAsyncClientParticleTick),
                new[] { typeof(Vintagestory.API.Client.IAsyncParticleManager), typeof(BlockPos), typeof(float), typeof(float) });
            C1RapidFoam.Apply(log);
            Check(PatchedBy(tick, "hydrology.client", "prefix"), "C1: BlockWaterflowing.OnAsyncClientParticleTick prefix");
            C1RapidFoam.Remove();
            Check(!PatchedBy(tick, "hydrology.client", "prefix"), "C1: Remove unpatches it");

            // the chunk pack fix's targets (applying it needs a running server's events); the game has the engine assembly
            // loaded, this process loads it first
            Assembly.Load("VintagestoryLib");
            var chunk = GamePatches.TypeByName(log, "chunk test", "Vintagestory.Common.WorldChunk");
            Check(chunk != null, "chunk fix: WorldChunk found");
            Check(chunk != null && AccessTools.DeclaredMethod(chunk, "Pack", Type.EmptyTypes)?.ReturnType == typeof(void)
                && AccessTools.DeclaredMethod(chunk, "TryCommitPackAndFree", new[] { typeof(int) })?.ReturnType == typeof(bool)
                && AccessTools.DeclaredMethod(chunk, "MarkModified", Type.EmptyTypes) != null, "chunk fix: Pack, TryCommitPackAndFree, MarkModified");
            Check(chunk != null && GamePatches.Field<object>(log, "chunk test", chunk, "packUnpackLock") != null
                && GamePatches.Field<long>(log, "chunk test", chunk, "lastReadOrWrite") != null
                && new[] { "blocksCompressedTmp", "lightCompressedTmp", "lightSatCompressedTmp", "fluidsCompressedTmp" }
                    .All(f => GamePatches.Field<byte[]>(log, "chunk test", chunk, f) != null), "chunk fix: its six fields");
            Check(log.Warnings("chunk test") == 0, "chunk fix: nothing logged off");
            Console.WriteLine("real targets: H5 water rules and refinements, H8, C1 patched and removed; chunk fix targets found");
        }

        // Earth's query, bound by hand: a river column (x 10) with its bed at 96 and its water surface at 114
        delegate bool RiverAtFn(int x, int z, out bool rapids, out double slope, out double discharge);
        delegate bool RiverSpanFn(int x, int z, out int bed, out int waterTop);
        static bool FakeRiverAt(int x, int z, out bool rapids, out double slope, out double discharge)
        { rapids = false; slope = 0.001; discharge = 100; return x == 10; }
        static int FakeFlowAt(int x, int z) => x == 10 ? 2 : -1;
        static bool FakeSpanAt(int x, int z, out int bed, out int waterTop) { bed = 96; waterTop = 114; return x == 10; }
        static int spanCalls;
        // fails on its first call only (a transient error in Earth)
        static bool FlakySpanAt(int x, int z, out int bed, out int waterTop)
        {
            if (spanCalls++ == 0) throw new InvalidOperationException("transient");
            return FakeSpanAt(x, z, out bed, out waterTop);
        }
        // a chute column whose bed Earth puts over its surface
        static bool InvertedSpanAt(int x, int z, out int bed, out int waterTop) { bed = 115; waterTop = 114; return x == 10; }
        // Earth 2.0.0-rc.3's "not ready yet", matched by name (Earth's is EarthMap.Core.RiverQueryNotReadyException)
        sealed class RiverQueryNotReadyException : InvalidOperationException { public RiverQueryNotReadyException() : base("not ready") { } }
        static int notReadyCalls;
        // not ready on its first two calls, then answers
        static bool NotReadyThenRiverAt(int x, int z, out bool rapids, out double slope, out double discharge)
        {
            if (notReadyCalls++ < 2) throw new RiverQueryNotReadyException();
            return FakeRiverAt(x, z, out rapids, out slope, out discharge);
        }
        static bool NeverReadyAt(int x, int z, out bool rapids, out double slope, out double discharge) => throw new RiverQueryNotReadyException();

        /// <summary>With Earth's RiverSpanAt the query answers only in the river's own water: for flowing water from its
        /// bed to two blocks above its surface (the ramp and a block of slack), for a source only up to the surface (rc.4);
        /// a wheel's answer needs its rim to reach that water (rc.4); a span that failed is asked again, not kept (rc.4); a
        /// span whose bed lies over its surface is no span (rc.4); without the span every height of a river column answers,
        /// as before.</summary>
        static int throwCalls;
        static bool ThrowingRiverAt(int x, int z, out bool rapids, out double slope, out double discharge)
        {
            throwCalls++;
            throw new InvalidOperationException("Earth broke");
        }
        static int countCalls;
        static bool CountingRiverAt(int x, int z, out bool rapids, out double slope, out double discharge)
        {
            countCalls++;
            rapids = false; slope = 0; discharge = 1;
            return true;
        }

        /// <summary>
        /// HY-0016: a column whose RiverAt throws is not asked again for a few seconds; 50 failures within a minute unbind the
        /// query (the rules run as without Earth), logged once; answers are kept per chunk and the least recently used chunk
        /// goes past the cache's size, so a wide area no longer empties the whole cache.
        /// </summary>
        static void QueryFailures()
        {
            var q = typeof(EarthRiverQuery);
            void Set(string field, object value) => q.GetField(field, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);
            var riverAtType = q.GetNestedType("RiverAtFn", BindingFlags.NonPublic);
            var log = new TestLogger();
            EarthRiverQuery.Unbind();
            Set("log", log);
            ((GamePatches.Breaker)q.GetField("Failures", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Reset(log);
            Set("riverAt", Delegate.CreateDelegate(riverAtType, typeof(Program).GetMethod(nameof(ThrowingRiverAt), BindingFlags.NonPublic | BindingFlags.Static)));
            throwCalls = 0;
            for (int i = 0; i < 10; i++) EarthRiverQuery.River(new BlockPos(5, 100, 5));
            Check(throwCalls == 1, $"query failure: a throwing column is asked once, then rests ({throwCalls} calls for 10 questions)");
            Check(EarthRiverQuery.Active, "query failure: one failing column leaves the query bound");
            // Fable re-review: the legacy vessels answer (a world without hydrologyFlooding, vessels from Earth's "with the
            // river query" flag) is read again when the query is unbound, not kept on without the river guards
            var h2 = typeof(Hydrology.Rules.H2Filling);
            h2.GetField("worldVessels", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
            h2.GetField("worldVesselsCheckedMs", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0L);
            for (int x = 0; x < GamePatches.MaxFaults; x++) EarthRiverQuery.River(new BlockPos(100 + x, 100, 5));
            Check(!EarthRiverQuery.Active, "query failure: 50 failures within a minute unbind the query");
            Check(!(bool)h2.GetField("worldVessels", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)
                && (long)h2.GetField("worldVesselsCheckedMs", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == long.MinValue,
                "query failure: the cached vessels answer is dropped at once (the third re-review); the memo reset waits for the tick");
            Check(EarthRiverQuery.TakeUnbound() && !EarthRiverQuery.TakeUnbound(),
                "query failure: the game's next tick learns once that the query was unbound (it re-reads the vessels answer and tells the clients)");
            Hydrology.Rules.H2Filling.ResetWorld();
            Check(!(bool)h2.GetField("worldVessels", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null),
                "query failure: the reset the tick runs drops the remembered vessels answer");
            Check(log.Lines.Count(l => l.text.Contains("keeps failing")) == 1, "query failure: the unbinding is logged once");

            EarthRiverQuery.Unbind();
            Set("riverAt", Delegate.CreateDelegate(riverAtType, typeof(Program).GetMethod(nameof(CountingRiverAt), BindingFlags.NonPublic | BindingFlags.Static)));
            countCalls = 0;
            // 300 chunks of 4 columns each: more chunks than the cache keeps
            for (int c = 0; c < 300; c++) for (int k = 0; k < 4; k++) EarthRiverQuery.River(new BlockPos(c * 32 + k, 100, 0));
            int first = countCalls;
            EarthRiverQuery.River(new BlockPos(299 * 32, 100, 0));
            Check(countCalls == first, "cache: a recent chunk is still answered from the cache");
            EarthRiverQuery.River(new BlockPos(0, 100, 0));
            Check(countCalls == first + 1, "cache: the least recently used chunk was evicted and is asked again");
            // 70,000 columns in a few chunks no longer empty the cache: 4 x 4 chunks hold 16,384 columns
            countCalls = 0;
            EarthRiverQuery.Unbind();
            Set("riverAt", Delegate.CreateDelegate(riverAtType, typeof(Program).GetMethod(nameof(CountingRiverAt), BindingFlags.NonPublic | BindingFlags.Static)));
            for (int x = 0; x < 128; x++) for (int z = 0; z < 128; z++) EarthRiverQuery.River(new BlockPos(x, 100, z));
            int filled = countCalls;
            for (int x = 0; x < 128; x++) for (int z = 0; z < 128; z++) EarthRiverQuery.River(new BlockPos(x, 100, z));
            Check(filled == 16384 && countCalls == filled, $"cache: 16,384 columns asked once each ({filled}, then {countCalls - filled} more)");
            EarthRiverQuery.Unbind();
        }

        static void RiverSpan()
        {
            var q = typeof(EarthRiverQuery);
            void Set(string field, object value) => q.GetField(field, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);
            var riverAtType = q.GetNestedType("RiverAtFn", BindingFlags.NonPublic);
            var spanType = q.GetNestedType("RiverSpanFn", BindingFlags.NonPublic);
            EarthRiverQuery.Unbind();
            Set("riverAt", Delegate.CreateDelegate(riverAtType, typeof(Program).GetMethod(nameof(FakeRiverAt), BindingFlags.NonPublic | BindingFlags.Static)));
            Set("riverFlowAt", (System.Func<int, int, int>)FakeFlowAt);
            Set("riverSpanAt", Delegate.CreateDelegate(spanType, typeof(Program).GetMethod(nameof(FakeSpanAt), BindingFlags.NonPublic | BindingFlags.Static)));
            q.GetProperty("Version").SetValue(null, 3);
            Check(EarthRiverQuery.River(new BlockPos(10, 114, 0)) && EarthRiverQuery.River(new BlockPos(10, 97, 0)), "span: the river's own water is river water");
            Check(EarthRiverQuery.River(new BlockPos(10, 115, 0)) && EarthRiverQuery.River(new BlockPos(10, 116, 0)), "span: a ramp and one block of slack above the surface count");
            Check(!EarthRiverQuery.River(new BlockPos(10, 117, 0)) && EarthRiverQuery.LabelledFlow(new BlockPos(10, 130, 0)) == -1, "span: a channel on a bridge above the river is no river water");
            Check(!EarthRiverQuery.River(new BlockPos(10, 90, 0)), "span: a cave pool under the bed is no river water");
            Check(EarthRiverQuery.LabelledFlow(new BlockPos(10, 114, 0)) == 2, "span: the river's surface keeps its label");
            Check(EarthRiverQuery.LabelledFlow(new BlockPos(10, 115, 0)) == -1, "span: a source one block over the surface (a bridge deck's water) takes no label");
            Check(EarthRiverQuery.AtColumn(new BlockPos(10, 117, 0), 3, out _, out _, out _), "span: a wheel whose rim reaches the river answers");
            Check(!EarthRiverQuery.AtColumn(new BlockPos(10, 130, 0), 3, out _, out _, out _), "span: a wheel in an aqueduct high over the river does not");
            Check(!EarthRiverQuery.River(new BlockPos(11, 114, 0)), "span: another column is no river");
            EarthRiverQuery.Unbind();
            spanCalls = 0;
            Set("riverAt", Delegate.CreateDelegate(riverAtType, typeof(Program).GetMethod(nameof(FakeRiverAt), BindingFlags.NonPublic | BindingFlags.Static)));
            Set("riverSpanAt", Delegate.CreateDelegate(spanType, typeof(Program).GetMethod(nameof(FlakySpanAt), BindingFlags.NonPublic | BindingFlags.Static)));
            q.GetProperty("Version").SetValue(null, 3);
            Check(EarthRiverQuery.River(new BlockPos(10, 130, 0)) && !EarthRiverQuery.River(new BlockPos(10, 130, 0)),
                "span: a span that failed once answers at any height that time only, then is asked again");
            EarthRiverQuery.Unbind();
            Set("riverAt", Delegate.CreateDelegate(riverAtType, typeof(Program).GetMethod(nameof(FakeRiverAt), BindingFlags.NonPublic | BindingFlags.Static)));
            Set("riverSpanAt", Delegate.CreateDelegate(spanType, typeof(Program).GetMethod(nameof(InvertedSpanAt), BindingFlags.NonPublic | BindingFlags.Static)));
            q.GetProperty("Version").SetValue(null, 3);
            Check(EarthRiverQuery.River(new BlockPos(10, 114, 0)) && EarthRiverQuery.River(new BlockPos(10, 130, 0)), "span: a bed over the surface is no span (any height)");
            EarthRiverQuery.Unbind();
            Set("riverAt", Delegate.CreateDelegate(riverAtType, typeof(Program).GetMethod(nameof(FakeRiverAt), BindingFlags.NonPublic | BindingFlags.Static)));
            q.GetProperty("Version").SetValue(null, 3);
            Check(EarthRiverQuery.River(new BlockPos(10, 130, 0)), "no span (an older Earth): every height of the column answers, as before");
            EarthRiverQuery.Unbind();

            // rc.5: Earth 2.0.0-rc.3's "not ready yet" (found by its type name): no answer and nothing kept, no failure
            // logged, the column's water waits; Earth's next answer is used and kept; a column waits a minute at most
            var qlog = new TestLogger();
            notReadyCalls = 0;
            Set("riverAt", Delegate.CreateDelegate(riverAtType, typeof(Program).GetMethod(nameof(NotReadyThenRiverAt), BindingFlags.NonPublic | BindingFlags.Static)));
            Set("log", qlog);
            q.GetProperty("Version").SetValue(null, 3);
            var at = new BlockPos(10, 114, 0);
            Check(!EarthRiverQuery.River(at) && EarthRiverQuery.NotReady(at), "not ready: no answer, and the column is noted as not ready");
            Check(EarthRiverQuery.Defer(at), "not ready: the column's water update waits");
            Check(qlog.Lines.Count(l => l.text.Contains("failed")) == 0 && qlog.Lines.Count(l => l.text.Contains("not ready yet")) == 1,
                "not ready: no failure logged, one note");
            Check(EarthRiverQuery.River(at) && !EarthRiverQuery.NotReady(at) && !EarthRiverQuery.Defer(at), "not ready: Earth's next answer is used and the water updates again");
            int calls = notReadyCalls;
            Check(EarthRiverQuery.River(at) && notReadyCalls == calls, "not ready: that answer is kept");
            EarthRiverQuery.Unbind();
            Set("riverAt", Delegate.CreateDelegate(riverAtType, typeof(Program).GetMethod(nameof(NeverReadyAt), BindingFlags.NonPublic | BindingFlags.Static)));
            Set("log", qlog);
            q.GetProperty("Version").SetValue(null, 3);
            Check(EarthRiverQuery.Defer(at), "never ready: waits at first");
            var waiting = (System.Collections.IDictionary)q.GetField("notReady", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            long key = ((long)at.X << 32) ^ (uint)at.Z;
            var since = ((long last, long first))waiting[key];
            waiting[key] = (since.last, since.first - EarthRiverQuery.MaxWaitMs - 1000);
            Check(!EarthRiverQuery.Defer(at) && qlog.Warnings("not been ready for a minute") == 1, "never ready: after a minute the water updates again, with one warning");
            EarthRiverQuery.Unbind();
            Set("log", null);
            Console.WriteLine("river span: the query answers only in the river's own water when Earth gives its span");
        }

        static void Failures()
        {
            var log = new TestLogger();
            var h = new Harmony("hydrology.registrytest");
            var a = AccessTools.DeclaredMethod(typeof(Target), nameof(Target.A));
            var b = AccessTools.DeclaredMethod(typeof(Target), nameof(Target.B));
            var fp = typeof(FakePatches);

            // a missing target: nothing patched, logged once per feature
            Check(!GamePatches.Apply(h, log, "feature gone",
                GamePatches.On(typeof(Target), nameof(Target.A), new[] { typeof(int) }, typeof(int), fp, prefix: nameof(FakePatches.PrefixA)),
                GamePatches.On(typeof(Target), "Renamed", new[] { typeof(int) }, typeof(int), fp, prefix: nameof(FakePatches.PrefixB))), "missing target: false");
            Check(!PatchedBy(a, h.Id, "prefix"), "missing target: the other hook of the feature is not patched either");
            GamePatches.Apply(h, log, "feature gone",
                GamePatches.On(typeof(Target), "Renamed", new[] { typeof(int) }, typeof(int), fp, prefix: nameof(FakePatches.PrefixB)));
            Check(log.Warnings("feature gone") == 1, "missing target: one warning, however often asked");
            Check(log.Lines.Any(l => l.text.Contains("feature gone switched off") && l.text.Contains("Renamed")), "the warning names the feature and the target");

            // a changed signature or return type
            Check(!GamePatches.Apply(h, log, "signature changed",
                GamePatches.On(typeof(Target), nameof(Target.A), new[] { typeof(long) }, typeof(int), fp, prefix: nameof(FakePatches.PrefixA))), "changed parameters: false");
            Check(!GamePatches.Apply(h, log, "return changed",
                GamePatches.On(typeof(Target), nameof(Target.A), new[] { typeof(int) }, typeof(long), fp, prefix: nameof(FakePatches.PrefixA))), "changed return type: false");

            // Harmony refuses the second hook (a renamed parameter): the first one, already patched, is removed again
            Check(!GamePatches.Apply(h, log, "parameter renamed",
                GamePatches.On(typeof(Target), nameof(Target.A), new[] { typeof(int) }, typeof(int), fp, prefix: nameof(FakePatches.PrefixA)),
                GamePatches.On(typeof(Target), nameof(Target.B), new[] { typeof(int) }, typeof(int), fp, prefix: nameof(FakePatches.PrefixRenamed))), "refused patch: false");
            Check(!PatchedBy(a, h.Id, "prefix") && !PatchedBy(b, h.Id, "prefix"), "refused patch: the feature's earlier hook is rolled back");
            Check(log.Warnings("parameter renamed") == 1, "refused patch: logged");

            // a feature whose targets are all there is patched whole
            Check(GamePatches.Apply(h, log, "all there",
                GamePatches.On(typeof(Target), nameof(Target.A), new[] { typeof(int) }, typeof(int), fp, prefix: nameof(FakePatches.PrefixA)),
                GamePatches.On(typeof(Target), nameof(Target.B), new[] { typeof(int) }, typeof(int), fp, prefix: nameof(FakePatches.PrefixB))), "all targets: true");
            Check(PatchedBy(a, h.Id, "prefix") && PatchedBy(b, h.Id, "prefix"), "all targets: both patched");
            h.UnpatchAll(h.Id);

            // HY-0019: errors spread over two one-minute windows never switch a feature off
            int spreadOffs = 0;
            var spread2 = new GamePatches.Breaker("breaker spread", () => spreadOffs++);
            spread2.Reset(log);
            var window = typeof(GamePatches.Breaker).GetField("windowStart", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < 40; i++) spread2.Fault(new InvalidOperationException("test"));
            window.SetValue(spread2, (long)window.GetValue(spread2) - GamePatches.FaultWindowMs - 1);
            for (int i = 0; i < 40; i++) spread2.Fault(new InvalidOperationException("test"));
            Check(!spread2.Off && spreadOffs == 0, "breaker: 80 errors over two windows leave the feature on");

            // fields and members a feature's own code uses
            Check(GamePatches.Field<BEBehaviorMPWaterWheel, float>(log, "field gone", "flowRateRenamed") == null && log.Warnings("field gone") == 1, "missing field: null, logged");
            Check(GamePatches.Field<BEBehaviorMPWaterWheel, double>(log, "field retyped", "flowRate") == null && log.Warnings("field retyped") == 1, "retyped field: null, logged");
            Check(GamePatches.HasField(log, "member", typeof(BlockBehaviorFiniteSpreadingLiquid), "multiplySpread", typeof(bool), false)
                && GamePatches.HasField(log, "member", typeof(BlockBehaviorFiniteSpreadingLiquid), "ReplacableThreshold", typeof(int), true), "the water rules' public fields exist");
            Check(!GamePatches.HasField(log, "member gone", typeof(BlockBehaviorFiniteSpreadingLiquid), "multiplySpread", typeof(bool), true), "a field that became static is refused");
            Check(GamePatches.TypeByName(log, "type gone", "Vintagestory.Common.WorldChunkRenamed") == null && log.Warnings("type gone") == 1, "missing type: null, logged");

            // the runtime breaker: the first error logged with its stack, the feature off (once) after MaxFaults
            int offs = 0;
            var breaker = new GamePatches.Breaker("breaker test", () => offs++);
            breaker.Reset(log);
            for (int i = 0; i < GamePatches.MaxFaults - 1; i++) breaker.Fault(new InvalidOperationException("test " + i));
            Check(!breaker.Off && offs == 0, "breaker: still on below the limit");
            // each distinct error once, five at most (Fable's pass: only the very first was logged)
            Check(log.Lines.Count(l => l.type == EnumLogType.Error && l.text.Contains("breaker test threw")) == 5
                && log.Lines.Any(l => l.text.Contains("InvalidOperationException")), "breaker: distinct errors are logged once each, five at most, with their exceptions");
            var same = new GamePatches.Breaker("breaker same", null);
            same.Reset(log);
            for (int i = 0; i < 10; i++) same.Fault(new InvalidOperationException("the same"));
            Check(log.Lines.Count(l => l.text.Contains("breaker same threw")) == 1, "breaker: the same error is logged once");
            breaker.Fault(new InvalidOperationException("last"));
            breaker.Fault(new InvalidOperationException("after"));
            Check(breaker.Off && offs == 1, "breaker: off at the limit, its switch-off action run once");
            Check(log.Lines.Count(l => l.type == EnumLogType.Error && l.text.Contains("breaker test switched off")) == 1, "breaker: the switch-off is logged once");
            // rc.4: the errors are counted per minute: as many again after the window has passed leave it on
            var spread = new GamePatches.Breaker("window test");
            spread.Reset(log);
            for (int i = 0; i < GamePatches.MaxFaults - 1; i++) spread.Fault(new InvalidOperationException("early " + i));
            typeof(GamePatches.Breaker).GetField("windowStart", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(spread, Environment.TickCount64 - GamePatches.FaultWindowMs - 1);
            for (int i = 0; i < GamePatches.MaxFaults - 1; i++) spread.Fault(new InvalidOperationException("late " + i));
            Check(!spread.Off, "breaker: errors spread over two minutes do not add up");
            breaker.Reset(log);
            Check(!breaker.Off, "breaker: a new world starts it on again");
            Console.WriteLine("failures: missing, changed and refused targets switch off only their feature, logged once, rolled back");
        }
    }
}
