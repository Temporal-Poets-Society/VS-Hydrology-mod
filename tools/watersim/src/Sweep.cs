using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WaterSim
{
    /// <summary>
    /// The parametric study: a grid of rule sets around Hydrology 0.1.3 (candidate rules on and off), a light round over
    /// every scene, then a heavy round (thousands of edit sequences, several seeds) for the leaders.
    /// </summary>
    public static class Sweep
    {
        /// <summary>The rule set the study recommends (filled in from the greedy search's result).</summary>
        public static RuleSet Recommended() => RuleSet.Parse(RecommendedSpec);
        public static string RecommendedSpec = "hydrology-0.1.3+Survival=1,SurvivalContained=0,FallLabel=1,FallingFeedsSideways=0,LevelRelax=1,WakeEdgesOnNewSource=1,RapidSourceSheet=1,LoneVertical=1,RefillOnSource=3,WakeOnFallLabel=1";

        /// <summary>The dimensions of the search: each rule switch or parameter and the values tried.</summary>
        static readonly (string field, object[] values)[] Dims =
        {
            ("Survival", new object[] { false, true }),
            ("SurvivalContained", new object[] { true, false }),
            ("SurvivalSourceSupports", new object[] { true, false }),
            ("SurvivalBirth", new object[] { 0, 1, 2 }),
            ("FallLabel", new object[] { false, true }),
            ("AboveFeeds", new object[] { 0, 1, 2 }),
            ("FallingFeedsSideways", new object[] { true, false }),
            ("FillDiagonal", new object[] { true, false }),
            ("FillOnSource", new object[] { false, true }),
            ("FillSources", new object[] { 2, 3 }),
            ("H3FallsInAir", new object[] { true, false }),
            ("H3SpreadOnLowerWater", new object[] { true, false }),
            ("H1DrainGuard", new object[] { true, false }),
            ("RapidSourceSheet", new object[] { false, true }),
            ("LoneVertical", new object[] { false, true }),
            ("WakeEdgesOnNewSource", new object[] { false, true }),
            ("LevelRelax", new object[] { false, true }),
            ("RefillOnSource", new object[] { 0, 3, 4 }),
            ("WakeOnFallLabel", new object[] { false, true }),
            ("WakeLowerSides", new object[] { false, true }),
            ("RapidReach", new object[] { 6, 4, 5, 7 }),
            ("RapidDrop", new object[] { 2, 3 }),
            ("WakeSourcesBeside", new object[] { true, false }),
            ("H4Sheets", new object[] { true, false }),
        };

        static RuleSet With(RuleSet r, string field, object v)
        {
            var c = r.Clone();
            typeof(RuleSet).GetField(field).SetValue(c, v);
            c.Name = c.Spec();
            return c;
        }

        /// <summary>Every rule set one change away from <paramref name="r"/>.</summary>
        public static List<RuleSet> Neighbours(RuleSet r)
        {
            var list = new List<RuleSet>();
            foreach (var (field, values) in Dims)
            {
                object cur = typeof(RuleSet).GetField(field).GetValue(r);
                foreach (var v in values) if (!Equals(v, cur)) list.Add(With(r, field, v));
            }
            return list;
        }

        /// <summary>Each switch of <paramref name="r"/> that differs from 0.1.3, set back to 0.1.3's value (what each rule adds).</summary>
        public static List<RuleSet> Ablations(RuleSet r)
        {
            var b = RuleSet.Hydrology013();
            var list = new List<RuleSet>();
            foreach (var (field, _) in Dims)
            {
                var f = typeof(RuleSet).GetField(field);
                if (Equals(f.GetValue(r), f.GetValue(b))) continue;
                list.Add(With(r, field, f.GetValue(b)));
            }
            return list;
        }

        public sealed class Score
        {
            public string Rules;
            public RuleSet R;
            public double Total;
            public int CheckFailures, Loops, ScriptFloating, CanonicalFloating, CanonicalDiff, CanonicalLabelDiff, Pokes, PokesChangedGenerated, PokesChangedCanonical,
                Fuzz, FuzzChanged, FuzzSourcesGained, FuzzSourcesLost;
            public double PokeFloatingPerRun, FuzzFloatingPerRun, UpdatesPerEdit;
            public long MaxUpdates;
            public List<string> Failures = new();
            public List<SceneResult> Scenes = new();
        }

        public static Score Rate(RuleSet r, IEnumerable<SceneResult> results)
        {
            var s = new Score { Rules = r.Name, R = r };
            long pokeFl = 0, fuzzFl = 0, upd = 0, edits = 0;
            foreach (var x in results)
            {
                s.Scenes.Add(x);
                s.CheckFailures += x.CheckFailures.Count;
                foreach (var f in x.CheckFailures) s.Failures.Add(x.Scene + ": " + f);
                s.Loops += (x.CanonicalSettled ? 0 : 1) + (x.ScriptSettled ? 0 : 1) + x.PokeLoops + x.FuzzLoops;
                s.ScriptFloating += x.ScriptFloating.Total;
                s.CanonicalFloating += x.CanonicalFloating.Total;
                var scene = Scenes.ByName(x.Scene);
                if (scene.EarthShape) { s.CanonicalDiff += x.CanonicalDiff; s.CanonicalLabelDiff += x.CanonicalLabelDiff; }
                s.Pokes += x.Pokes; s.PokesChangedGenerated += scene.EarthShape ? x.PokesChangedGenerated : 0; s.PokesChangedCanonical += x.PokesChangedCanonical;
                s.Fuzz += x.Fuzz; s.FuzzChanged += x.FuzzChanged; s.FuzzSourcesGained += x.FuzzSourcesGained; s.FuzzSourcesLost += x.FuzzSourcesLost;
                pokeFl += x.PokeFloating.Total; fuzzFl += x.FuzzFloating.Total;
                upd += x.PokeUpdates + x.FuzzUpdates; edits += x.Pokes * 2 + x.Fuzz * 7;
                s.MaxUpdates = Math.Max(s.MaxUpdates, x.MaxUpdatesOneRun);
            }
            s.PokeFloatingPerRun = s.Pokes > 0 ? (double)pokeFl / s.Pokes : 0;
            s.FuzzFloatingPerRun = s.Fuzz > 0 ? (double)fuzzFl / s.Fuzz : 0;
            s.UpdatesPerEdit = edits > 0 ? (double)upd / edits : 0;
            double pokeFrac = s.Pokes > 0 ? (double)s.PokesChangedCanonical / s.Pokes : 0;
            double pokeGenFrac = s.Pokes > 0 ? (double)s.PokesChangedGenerated / s.Pokes : 0;
            double fuzzFrac = s.Fuzz > 0 ? (double)s.FuzzChanged / s.Fuzz : 0;
            double gained = s.Fuzz > 0 ? (double)s.FuzzSourcesGained / s.Fuzz : 0;
            s.Total = 1000 * s.CheckFailures + 500 * s.Loops + 50 * (s.ScriptFloating + s.CanonicalFloating)
                + 400 * (s.PokeFloatingPerRun + s.FuzzFloatingPerRun) + 300 * fuzzFrac + 300 * pokeFrac + 50 * pokeGenFrac
                + 1.0 * s.CanonicalDiff + 0.1 * s.CanonicalLabelDiff + 100 * gained + 0.2 * s.UpdatesPerEdit;
            return s;
        }

        public static void Run(string[] args)
        {
            int lightFuzz = Arg(args, "--light-fuzz", 40), lightPokes = Arg(args, "--light-pokes", 40), rounds = Arg(args, "--rounds", 8);
            int heavyFuzz = Arg(args, "--heavy-fuzz", 1000), heavyPokes = Arg(args, "--heavy-pokes", 400), seeds = Arg(args, "--seeds", 3);
            string outDir = ArgS(args, "--out", "out");
            Directory.CreateDirectory(outDir);
            var scenes = Scenes.All();
            var log = new StringBuilder();
            void Log(string line) { Console.WriteLine(line); log.AppendLine(line); }

            // greedy coordinate search from Hydrology 0.1.3, one change at a time, on common random numbers
            var current = ArgS(args, "--start", null) is string st ? RuleSet.Parse(st) : RuleSet.Hydrology013();
            var seen = new Dictionary<string, Score>();
            void Eval(List<RuleSet> rs, int fuzz, int pokes, int seed)
            {
                var todo = rs.Where(r => !seen.ContainsKey(r.Name)).GroupBy(r => r.Name).Select(g => g.First()).ToList();
                var res = Study.RunAll(scenes, todo, fuzz, pokes, seed);
                foreach (var r in todo) seen[r.Name] = Rate(r, res.Where(x => x.Rules == r.Name));
            }
            Eval(new List<RuleSet> { current, RuleSet.Vanilla(), RuleSet.Hydrology013() }, lightFuzz, lightPokes, 1);
            Log($"light: vanilla {seen["vanilla"].Total:F1}, hydrology-0.1.3 {seen["hydrology-0.1.3"].Total:F1}");
            var trace = new List<Score> { seen["vanilla"], seen["hydrology-0.1.3"] };
            for (int round = 1; round <= rounds; round++)
            {
                var nb = Neighbours(current);
                Eval(nb, lightFuzz, lightPokes, 1);
                var ranked = nb.Select(r => seen[r.Name]).OrderBy(x => x.Total).ToList();
                var cur = seen[current.Name];
                Log($"round {round}: current {cur.Total:F1} ({current.Name})");
                foreach (var x in ranked.Take(5)) Log($"   {x.Total,9:F1}  {x.Rules}");
                if (ranked[0].Total >= cur.Total - 0.5) { Log("   no better change: stop"); break; }
                current = ranked[0].R;
                trace.Add(ranked[0]);
            }
            WriteTable(Path.Combine(outDir, "light.md"), seen.Values.OrderBy(x => x.Total).Take(40).ToList(), "Light round (greedy search, best 40 of " + seen.Count + ")");

            // heavy round: vanilla, 0.1.3, the result, its ablations, and the runners-up of the last round
            var best = current;
            var heavyRules = new List<RuleSet> { RuleSet.Vanilla(), RuleSet.Hydrology013(), best };
            heavyRules.AddRange(Ablations(best));
            heavyRules.AddRange(Neighbours(best).Select(r => seen.TryGetValue(r.Name, out var sc) ? sc : null).Where(x => x != null)
                .OrderBy(x => x.Total).Take(4).Select(x => x.R));
            heavyRules = heavyRules.GroupBy(r => r.Name).Select(g => g.First()).ToList();
            Log($"heavy round: {heavyRules.Count} rule sets, {seeds} seeds x ({heavyPokes} pokes + {heavyFuzz} sequences) per scene");
            var heavy = new List<SceneResult>();
            for (int seed = 1; seed <= seeds; seed++) heavy.AddRange(Study.RunAll(scenes, heavyRules, heavyFuzz, heavyPokes, 100 + seed));
            var heavyScores = heavyRules.Select(r => Rate(r, heavy.Where(x => x.Rules == r.Name))).OrderBy(s => s.Total).ToList();
            WriteTable(Path.Combine(outDir, "heavy.md"), heavyScores, "Heavy round");
            foreach (var s in heavyScores) Log($"  {s.Total,9:F1}  {s.Rules}  fail={s.CheckFailures} loops={s.Loops} float={s.ScriptFloating}+{s.CanonicalFloating} seq={s.FuzzChanged}/{s.Fuzz} pokes={s.PokesChangedCanonical}/{s.PokesChangedGenerated}/{s.Pokes} diff={s.CanonicalDiff}");
            WriteJson(Path.Combine(outDir, "scores.json"), heavyScores, trace);
            File.WriteAllText(Path.Combine(outDir, "search.log"), log.ToString());
        }

        static int Arg(string[] a, string k, int d) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? int.Parse(a[i + 1]) : d; }
        static string ArgS(string[] a, string k, string d) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : d; }

        public static void WriteTable(string path, List<Score> scores, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {title}").AppendLine();
            sb.AppendLine("| # | Rules | Score | Check failures | Loops | Floating (script+settled) | Floating per poke / sequence | Pokes changed (settled / Earth) | Sequences changed | Sources gained / lost | Earth shape diff (cells / labels) | Updates per edit | Max updates one run |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            int i = 0;
            foreach (var s in scores)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| {0} | {1} | {2:F1} | {3} | {4} | {5}+{6} | {7:F2} / {8:F2} | {9}/{10} / {11} | {12}/{13} | {14} / {15} | {16} / {17} | {18:F1} | {19} |",
                    ++i, s.Rules, s.Total, s.CheckFailures, s.Loops, s.ScriptFloating, s.CanonicalFloating, s.PokeFloatingPerRun, s.FuzzFloatingPerRun,
                    s.PokesChangedCanonical, s.Pokes, s.PokesChangedGenerated, s.FuzzChanged, s.Fuzz, s.FuzzSourcesGained, s.FuzzSourcesLost,
                    s.CanonicalDiff, s.CanonicalLabelDiff, s.UpdatesPerEdit, s.MaxUpdates));
            }
            sb.AppendLine().AppendLine("Rule set names: S survival (0 off, 1 support, 2 support + contained), L fall label, A water above feeds (0 any, 1 not still, 2 source or falling), F falling water feeds sideways, B fill (0 as 0.1.3, 1 no diagonal, 2 also on a source, 3 on a source without diagonal), H 0.1.3's falling-water fix.");
            sb.AppendLine();
            foreach (var s in scores.Take(12))
            {
                sb.AppendLine($"## {s.Rules}").AppendLine().AppendLine("`" + s.R.Describe() + "`").AppendLine();
                foreach (var f in s.Failures.Distinct().Take(20)) sb.AppendLine("- " + f);
                sb.AppendLine();
                sb.AppendLine("| Scene | Settled diff | Script floating | Pokes changed | Sequences changed | Loops |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var g in s.Scenes.GroupBy(x => x.Scene))
                    sb.AppendLine($"| {g.Key} | {g.Max(x => x.CanonicalDiff)} | {g.Sum(x => x.ScriptFloating.Total)} | {g.Sum(x => x.PokesChangedCanonical)}/{g.Sum(x => x.Pokes)} | {g.Sum(x => x.FuzzChanged)}/{g.Sum(x => x.Fuzz)} | {g.Sum(x => x.PokeLoops + x.FuzzLoops + (x.ScriptSettled ? 0 : 1))} |");
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }

        static void WriteJson(string path, List<Score> heavy, List<Score> light)
        {
            object Row(Score s) => new
            {
                rules = s.Rules, total = Math.Round(s.Total, 1), s.CheckFailures, s.Loops, s.ScriptFloating, s.CanonicalFloating,
                s.CanonicalDiff, s.CanonicalLabelDiff, s.Pokes, s.PokesChangedCanonical, s.PokesChangedGenerated, s.Fuzz, s.FuzzChanged,
                s.FuzzSourcesGained, s.FuzzSourcesLost, poke_floating = Math.Round(s.PokeFloatingPerRun, 3), fuzz_floating = Math.Round(s.FuzzFloatingPerRun, 3),
                updates_per_edit = Math.Round(s.UpdatesPerEdit, 1), s.MaxUpdates, failures = s.Failures.Distinct().ToList(), config = s.R.ToDict(),
                scenes = s.Scenes.GroupBy(x => x.Scene).Select(g => new
                {
                    scene = g.Key, settled_diff = g.Max(x => x.CanonicalDiff), script_floating = g.Sum(x => x.ScriptFloating.Total),
                    pokes = g.Sum(x => x.Pokes), pokes_changed = g.Sum(x => x.PokesChangedCanonical), fuzz = g.Sum(x => x.Fuzz),
                    fuzz_changed = g.Sum(x => x.FuzzChanged), loops = g.Sum(x => x.PokeLoops + x.FuzzLoops),
                }).ToList(),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(new { heavy = heavy.Select(Row), light = light.Select(Row) }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
