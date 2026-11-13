using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WaterSim
{
    /// <summary>
    /// `watersim export file.json`: what the results page plays. Per scene: Earth's generated blocks (and the alternative
    /// blocks of a shape comparison), the edits, and per run (a rule set on one set of blocks) the settled world (every
    /// cell woken once) and the frames of the playback. Scenes with a script use it; the others replay one random edit
    /// sequence that is undone again: the one of 300 that leaves Hydrology 0.1.3 furthest from where it started, the same
    /// one for every rule set.
    /// </summary>
    public static class Export
    {
        public static void Run(string path, List<RuleSet> rules)
        {
            var scenes = Scenes.Playback();
            var outScenes = new object[scenes.Count];
            System.Threading.Tasks.Parallel.For(0, scenes.Count, si =>
            {
                var s = scenes[si];
                var runs = s.Runs ?? rules.Select(r => new Run { Key = r.Name, Label = Label(r), Rules = r }).ToList();
                var edits = Playback.EditsFor(s);
                var outRuns = new List<object>();
                foreach (var run in runs)
                {
                    var start = run.Alt ? s.AltGenerated : s.Generated;
                    var sim = new Sim(start.Clone(), run.Rules, 1) { River = s.River };
                    sim.WakeAll();
                    sim.Settle(Study.SettleMs, Study.SettleUpdates);
                    var canon = sim.W;
                    var mid = new List<(string name, string fail)>();
                    var rec = Playback.Record(start, run.Rules, edits, river: s.River,
                        beforeEdit: (k, w) => { foreach (var m in s.MidChecks) if (m.edit == k) mid.Add((m.name, m.check(w, null))); });
                    var fl = Invariants.Floating(rec.Final);
                    var checkOn = s.Script.Count == 0 ? canon : rec.Final;
                    outRuns.Add(new
                    {
                        key = run.Key, label = run.Label, alt = run.Alt,
                        settled = Encode(canon, start),
                        frames = rec.Frames.Select(f => new { t = f.T, e = f.Edit, u = f.Updates, c = f.Changes }).ToList(),
                        settled_vs_start = canon.CountWaterDiff(start),
                        after_vs_start = rec.Final.CountWaterDiff(start),
                        floating = fl.Total, floating_text = fl.ToString(),
                        updates = rec.Updates, stopped = rec.Settled,
                        checks = s.Checks.Select(c => new { name = c.name, fail = c.check(checkOn, canon) }).Concat(mid.Select(m => new { name = m.name, fail = m.fail })).ToList(),
                        info = s.Info?.Invoke(rec.Final, s.River),
                    });
                }
                outScenes[si] = new
                {
                    name = s.Name, title = s.Title, description = s.Description, how = s.Script.Count > 0 ? "script" : "sequence",
                    study = Scenes.All().Exists(x => x.Name == s.Name),
                    edits = edits.Select(e => new { v = e.Verb, x = e.X, y = e.Y, z = e.Z }).ToList(),
                    sx = s.Generated.SX, sy = s.Generated.SY, sz = s.Generated.SZ, section = s.SectionZ,
                    earth = Encode(s.Generated, null),
                    alt = s.AltGenerated == null ? null : Encode(s.AltGenerated, null),
                    runs = outRuns,
                };
            });
            var doc = new { recommended = Sweep.RecommendedSpec, scenes = outScenes };
            File.WriteAllText(path, JsonSerializer.Serialize(doc));
            Console.WriteLine($"wrote {path} ({new FileInfo(path).Length / 1024} KB)");
        }

        static string Label(RuleSet r) => r.Name == "hydrology-0.1.3" ? "Hydrology 0.1.3" : r.Name == "vanilla" ? "Vanilla 1.22.7" : r.Name == Sweep.RecommendedSpec ? "Recommended" : r.Name;

        /// <summary>The worst of 300 random edit sequences (from Earth's generated world) for <paramref name="r"/>.</summary>
        public static (List<Edit> edits, List<Edit> undo) WorstSequence(Scene s, RuleSet r)
        {
            var rng = new Random(2026);
            List<Edit> bestE = null, bestU = null;
            int worst = -1;
            for (int t = 0; t < 300; t++)
            {
                var edits = new List<Edit>();
                var undo = new List<Edit>();
                int n = 2 + rng.Next(4);
                var w = s.Generated;
                for (int i = 0; i < n; i++)
                {
                    int x = rng.Next(s.EX0, s.EX1 + 1), y = rng.Next(s.EY0, s.EY1 + 1), z = rng.Next(s.EZ0, s.EZ1 + 1);
                    if (edits.Exists(e => e.X == x && e.Y == y && e.Z == z)) continue;
                    bool place = !w.IsSolid(x, y, z);
                    edits.Add(new Edit { X = x, Y = y, Z = z, Place = place, WaitMs = rng.Next(200, 3000) });
                }
                for (int i = edits.Count - 1; i >= 0; i--) undo.Add(new Edit { X = edits[i].X, Y = edits[i].Y, Z = edits[i].Z, Place = !edits[i].Place, WaitMs = rng.Next(200, 3000) });
                var sim = new Sim(w.Clone(), r, 1);
                foreach (var e in edits) { sim.Run(e.WaitMs); e.Apply(sim); }
                sim.Run(4000);
                foreach (var e in undo) { sim.Run(e.WaitMs); e.Apply(sim); }
                sim.Settle(Study.SettleMs, Study.SettleUpdates);
                int d = sim.W.CountWaterDiff(w) + 4 * Invariants.Floating(sim.W).Total;
                if (d > worst) { worst = d; bestE = edits; bestU = undo; }
            }
            return (bestE, bestU);
        }

        /// <summary>Cells that differ from <paramref name="baseWorld"/> (every non-empty cell when null), as index, value pairs
        /// (<see cref="Playback.Value"/>).</summary>
        static List<int> Encode(World w, World baseWorld)
        {
            var list = new List<int>();
            for (int i = 0; i < w.Fluid.Length; i++)
            {
                int v = Playback.Value(w, i);
                if (baseWorld == null ? v == 0 : v == Playback.Value(baseWorld, i)) continue;
                list.Add(i); list.Add(v);
            }
            return list;
        }
    }
}
