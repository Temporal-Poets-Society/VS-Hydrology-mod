using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WaterSim
{
    /// <summary>
    /// `watersim features [out.md]`: each later-target scene under Hydrology 0.1.3, the recommended set, the recommended set
    /// with the prototype, and the prototype with one version-1 rule switched off at a time (globally, standing in for a
    /// local switch-off) or with ProtoLocalOff. A rule blocks a feature when switching it off turns failed checks into
    /// passes.
    /// </summary>
    public static class FeatureCheck
    {
        static readonly (string field, object off)[] V1 =
        {
            ("Survival", false), ("FallLabel", false), ("FallingFeedsSideways", true), ("WakeOnFallLabel", false),
            ("LevelRelax", false), ("RefillOnSource", 0), ("H2Fill", false), ("VanillaSourceRule", false),
            ("H4Rapids", false), ("H1DrainGuard", false), ("H3SpreadOnLowerWater", false),
        };

        public static void Run(string[] args)
        {
            var sb = new StringBuilder();
            foreach (var s in Scenes.FeatureScenes())
            {
                var proto = s.Runs.Last(r => r.Key.StartsWith("proto")).Rules;
                if (s.Name == "long-run") proto = s.Runs.First(r => r.Key == "proto").Rules;
                var variants = new List<(string label, RuleSet r)>
                {
                    ("Hydrology 0.1.3", RuleSet.Hydrology013()),
                    ("Recommended", Sweep.Recommended()),
                    ("Recommended + prototype", proto),
                };
                var local = proto.Clone(); local.ProtoLocalOff = true;
                variants.Add(("  + ProtoLocalOff (fills and relax off where it drains)", local));
                foreach (var (field, off) in V1)
                {
                    var f = typeof(RuleSet).GetField(field);
                    if (Equals(f.GetValue(proto), off)) continue;
                    var v = proto.Clone(); f.SetValue(v, off);
                    variants.Add(($"  − {field}", v));
                }
                sb.AppendLine($"## {s.Title} ({s.Name})").AppendLine().AppendLine(s.Description).AppendLine();
                sb.AppendLine("| Rules | Failed checks | Water in the air | Updates | Stopped | Note |").AppendLine("|---|---|---|---|---|---|");
                foreach (var (label, r) in variants)
                {
                    var mid = new List<(string name, string f)>();
                    var res = Playback.Record(s.Generated, r, s.Script, river: s.River, maxFrames: 100000,
                        beforeEdit: (k, w) => { foreach (var m in s.MidChecks) if (m.edit == k) mid.Add((m.name, m.check(w, null))); });
                    var fails = s.Checks.Select(c => (c.name, f: c.check(res.Final, null))).Concat(mid).Where(c => c.f != null).ToList();
                    string info = s.Info?.Invoke(res.Final, s.River) ?? "";
                    var fl = Invariants.Floating(res.Final);
                    string note = string.Join("; ", fails.Select(c => c.name + ": " + c.f));
                    if (info.Length > 0) note = (note.Length > 0 ? note + "; " : "") + info;
                    sb.AppendLine($"| {label} | {fails.Count} | {fl.Total} | {res.Updates} | {(res.Settled ? "yes" : "**no**")} | {note} |");
                    if (label == "Recommended + prototype" && res.Log.Count > 0)
                        Console.WriteLine($"{s.Name} prototype log: " + string.Join(" | ", res.Log.Where(l => !l.Contains("at their levels")).Take(14)));
                }
                sb.AppendLine();
            }
            string text = sb.ToString();
            Console.Write(text);
            if (args.Length > 0) File.WriteAllText(args[0], "# Later targets against the version-1 rules\n\n" + text);
        }
    }
}
