using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace WaterSim
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "calibrate";
            var sw = Stopwatch.StartNew();
            switch (cmd)
            {
                case "calibrate": Calibrate.Run(); break;
                case "scene": ShowScene(args[1], args.Length > 2 ? args[2] : "hydrology-0.1.3"); break;
                case "sweep": Sweep.Run(args.Skip(1).ToArray()); break;
                case "shape": { var sc = Scenes.ByNameAny(args[1]); var (cw, _, _) = Study.Canonical(sc, Rules(args[2])); WhyDebug.Diff(sc.Generated, cw, 400, labels: true); break; }
                case "export": Export.Run(args[1], args.Skip(2).Select(Rules).ToList()); break;
                case "verify-shape": VerifyShape(args.Length > 1 ? args[1] : "recommended"); break;
                case "play": Playback.Play(args); break;
                case "features": FeatureCheck.Run(args.Skip(1).ToArray()); break;
                case "canals": Canals.Run(args.Skip(1).ToArray()); break;
                case "rapidrule": RapidRule.Run(); RapidRule.Check(); break;
                case "flood": Flood.Run(args.Skip(1).ToArray()); break;
                case "whitewater": WhiteWater.Run(args.Skip(1).ToArray()); break;
                case "whitewater-spec": WhiteWater.Spec(args.Skip(1).ToArray()); break;
                case "query": QueryStudy.Run(args.Skip(1).ToArray()); break;
                case "why": WhyDebug.Run(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 3); break;
                default: Console.WriteLine("watersim calibrate | scene <name> [rules] | sweep [options]"); return 1;
            }
            Console.Error.WriteLine($"[{sw.Elapsed.TotalSeconds:F1} s]");
            return 0;
        }

        public static RuleSet Rules(string name)
        {
            if (name == "recommended") return Sweep.Recommended();
            return RuleSet.Parse(name);
        }

        /// <summary>
        /// Earth's drops drawn in the Hydrology shape: every fluid cell woken once, and a poke at every site beside the water,
        /// must leave the generated world as it was (labels included, except under water, where vanilla's label of a
        /// source depends on the update order).
        /// </summary>
        static void VerifyShape(string rules)
        {
            var r = Rules(rules);
            foreach (bool hydro in new[] { false, true })
            {
                Columns.HydrologyShape = hydro;
                Console.WriteLine(hydro ? "== drops in the Hydrology shape" : "== drops as Earth 0.16.5 places them");
                foreach (var name in new[] { "step", "chute", "fall", "victoria" })
                {
                    var s = Scenes.ByName(name);
                    if (FlowQuery.RiverEverywhere) FlowQuery.Current = FlowQuery.AllWaterIsRiver(s.Generated);
                    var (canon, ok, upd) = Study.Canonical(s, r);
                    int cells = canon.CountWaterDiff(s.Generated), labels = VisibleLabelDiff(canon, s.Generated);
                    int pokes = 0, changed = 0, changedLabels = 0, loops = 0;
                    foreach (var (x, y, z) in Study.PokeSites(s))
                    {
                        var sim = Study.Poke(s.Generated, r, 7 + pokes, x, y, z, 2500, out bool settled);
                        pokes++;
                        if (!settled) loops++;
                        if (sim.W.CountWaterDiff(s.Generated) != 0) changed++;
                        else if (VisibleLabelDiff(sim.W, s.Generated) != 0) changedLabels++;
                    }
                    Console.WriteLine($"  {name,-9} first update: {cells} cells, {labels} visible labels changed; pokes: {changed} of {pokes} change cells, {changedLabels} more change only visible labels, {loops} never stop");
                    if (hydro && cells + labels > 0) WhyDebug.Diff(s.Generated, canon, 20, labels: true);
                }
            }
            Columns.HydrologyShape = false;
        }

        /// <summary>Label differences of cells a player can see: not of sources with water above them.</summary>
        static int VisibleLabelDiff(World a, World b)
        {
            int n = 0;
            for (int i = 0; i < a.Fluid.Length; i++)
            {
                if (a.Fluid[i] == b.Fluid[i] || (a.Fluid[i] & ~0xF0) != (b.Fluid[i] & ~0xF0)) continue;
                a.Pos(i, out int x, out int y, out int z);
                if (F.IsSource(a.Fluid[i]) && a.Get(x, y + 1, z) != 0) continue;
                n++;
            }
            return n;
        }

        static void ShowScene(string name, string rules)
        {
            var s = Scenes.ByName(name) ?? throw new ArgumentException("unknown scene " + name);
            var r = Rules(rules);
            Console.WriteLine($"== {s.Title} ({s.Name}), {r.Name}");
            Console.WriteLine("generated, section z=" + s.SectionZ);
            Console.Write(s.Generated.Section(s.SectionZ));
            var res = Study.Run(s, r, 0, 0, 1, keepWorlds: true);
            Console.WriteLine($"canonical: settled={res.CanonicalSettled} updates={res.CanonicalUpdates} diff={res.CanonicalDiff} labels={res.CanonicalLabelDiff} floating: {res.CanonicalFloating}");
            Console.Write(res.CanonicalWorld.Section(s.SectionZ));
            Console.WriteLine($"after script ({s.Script.Count} edits): settled={res.ScriptSettled} updates={res.ScriptUpdates} floating: {res.ScriptFloating}");
            Console.Write(res.ScriptWorld.Section(s.SectionZ));
            foreach (var f in res.CheckFailures) Console.WriteLine("  FAIL " + f);
            var sliceY = Environment.GetEnvironmentVariable("SLICE_Y");
            if (sliceY != null) Console.Write(res.ScriptWorld.Slice(int.Parse(sliceY)));
            if (sliceY != null) { var ws = new Sim(res.ScriptWorld.Clone(), r, 1); ws.WakeAll(); ws.Settle(); Console.WriteLine("after waking every cell:"); Console.Write(ws.W.Slice(int.Parse(sliceY))); }
        }
    }
}
