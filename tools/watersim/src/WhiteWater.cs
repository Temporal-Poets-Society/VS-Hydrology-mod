using System;
using System.Collections.Generic;
using System.Linq;

namespace WaterSim
{
    /// <summary>
    /// `watersim whitewater`: which shape of a rapids reach looks like continuous rapid water and stays as generated (Earth
    /// pool layout 7, 2026-10-24). A reach of 1-block steps `spacing` apart, `width` wide, pools 2 deep between the steps
    /// (1 at the edge rows when tapered), banks flush with the water (Earth raises them beside ramps), sills under the steps,
    /// drawn in the Hydrology shape with the rapid rule's marks and spills from ramp height 2. Rules: recommended with
    /// 0.1.7's vessels (Earth worlds). For each: the first update (every cell woken), pokes, single breaks (the flood
    /// check), the share of the reach's surface that is rapid water, and the water's depth in the reach.
    /// Variants over a reach with steps far apart: "rapid-top" (the pools' top layer drawn as rapid sources) and "flat1"
    /// (rapid flowing water of height 1 over the pool beyond each ramp).
    /// </summary>
    public static class WhiteWater
    {
        public static RuleSet Rules() => RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1"
            + (Environment.GetEnvironmentVariable("WW_RULES") ?? ""));

        /// <summary>Depth of the pools between the steps (WW_POOL, default 2; Earth's layout 7 sections: 1).</summary>
        static int PoolDepth => int.TryParse(Environment.GetEnvironmentVariable("WW_POOL"), out int d) ? d : 2;

        public sealed class Reach
        {
            public Columns C;
            public Scene S;
            public int X0, X1, Z0, Z1, Spacing, Steps;
        }

        public static Reach Build(int spacing, int steps, int width, bool taper, string variant = null) =>
            Build(Enumerable.Repeat(spacing, steps - 1).ToArray(), width, taper, variant);

        /// <summary>A reach whose steps are gaps[i] apart (steps = gaps + 1).</summary>
        public static Reach Build(int[] gaps, int width, bool taper, string variant = null)
        {
            int steps = gaps.Length + 1, spacing = gaps.Max();
            var at = new int[steps];
            int z0 = 4, z1 = z0 + width - 1, H = z1 + 5, x0 = 11, W = x0 + gaps.Sum() + 18;
            at[0] = x0;
            for (int i = 1; i < steps; i++) at[i] = at[i - 1] + gaps[i - 1];
            var c = new Columns(W, H, 2);
            int top = 6 + steps;
            int Depth(int z, int full) => taper ? Math.Min(full, Math.Min(z - z0, z1 - z) + 1) : full;
            for (int z = z0; z <= z1; z++)
            {
                c.Pool(1, x0 - 1, z, z, top, top - Depth(z, 3));
                for (int s = 1; s < steps; s++)
                    c.Pool(at[s - 1], at[s] - 1, z, z, top - s, top - s - Depth(z, PoolDepth), rapid: true);
                c.Pool(at[steps - 1], W - 2, z, z, top - steps, top - steps - Depth(z, 3), rapid: true);
            }
            for (int i = 0; i < W * H; i++)
            {
                if (c.Water[i] != 0) continue;
                int x = i % W, z = i / W;
                int wt = c.WaterTop[c.K(Math.Clamp(x, 1, W - 2), z0)];
                int d = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
                c.Top[i] = wt + Math.Max(0, d - 1);
                if (x == 0 || x == W - 1) c.Top[i] = Math.Max(c.Top[i], top + 1);
            }
            bool oldSills = Columns.Sills; int oldSpill = Columns.SpillFrom;
            Columns.Sills = true; Columns.SpillFrom = 2;
            c.SettleSteps();
            RapidRule.MarkRapid(c);
            var w = c.Place(top + 6, true);
            Columns.Sills = oldSills; Columns.SpillFrom = oldSpill;
            int rx0 = x0 - 1, rx1 = at[steps - 1] - 1;
            if (variant == "rapid-top" || variant == "flat1")
                for (int z = z0; z <= z1; z++)
                    for (int x = rx0 + 1; x <= rx1; x++)
                    {
                        int k = c.K(x, z), y = c.WaterTop[k];
                        if (variant == "rapid-top")
                        {
                            if (F.IsSource(w.Get(x, y, z)) && !F.IsRapid(w.Get(x, y, z))) w.SetFluid(x, y, z, F.Make(F.Rapid, F.E, 7));
                        }
                        else if (w.Get(x, y + 1, z) == 0 && !w.IsSolid(x, y + 1, z)) w.SetFluid(x, y + 1, z, F.Make(F.Rapid, F.E, 1));
                    }
            var scene = new Scene
            {
                Name = $"reach-{(gaps.Distinct().Count() == 1 ? "s" + spacing : "g" + string.Join("-", gaps))}-w{width}{(taper ? "-tapered" : "")}{(variant != null ? "-" + variant : "")}", Generated = w, EarthShape = true,
                SectionZ = (z0 + z1) / 2, EX0 = rx0 - 2, EX1 = rx1 + 3, EY0 = top - steps - 4, EY1 = top + 1, EZ0 = z0 - 1, EZ1 = z1 + 1,
            };
            return new Reach { C = c, S = scene, X0 = rx0, X1 = rx1, Z0 = z0, Z1 = z1, Spacing = spacing, Steps = steps };
        }

        /// <summary>Of the reach's columns (first step's edge to the last step), how many show rapid water on top, how many
        /// ordinary water; the fewest and most water cells in a column (the depth a boat has).</summary>
        public static (int rapid, int ordinary, int cols, int minDepth, int maxDepth) Surface(World w, Reach r, bool edgeRows = true)
        {
            int rapid = 0, ord = 0, n = 0, minD = int.MaxValue, maxD = 0;
            for (int z = r.Z0; z <= r.Z1; z++)
                for (int x = r.X0; x <= r.X1; x++)
                {
                    int y = w.SY - 1;
                    while (y > 0 && w.Get(x, y, z) == 0) y--;
                    int c = w.Get(x, y, z);
                    if (c == 0) continue;
                    n++;
                    if (F.IsRapid(c)) rapid++; else ord++;
                    if (!edgeRows && (z == r.Z0 || z == r.Z1)) continue;
                    int d = 0;
                    for (int yy = y; yy > 0 && w.Get(x, yy, z) != 0; yy--) d++;
                    minD = Math.Min(minD, d); maxD = Math.Max(maxD, d);
                }
            return (rapid, ord, n, minD == int.MaxValue ? 0 : minD, maxD);
        }

        public static void Run(string[] args)
        {
            var r = Rules();
            bool full = args.Contains("full");
            Console.WriteLine("| Reach | First update: cells / visible labels | Pokes changing it | Breaks adding water (worst) | Rapid surface | Depth (middle rows) |");
            Console.WriteLine("|---|---|---|---|---|---|");
            var cases = new List<(int s, int w, bool t, string v)>();
            foreach (int s in new[] { 2, 3, 4, 5, 6, 7, 9 })
                foreach (int w in new[] { 3, 6, 10 })
                    foreach (bool t in new[] { false, true })
                        cases.Add((s, w, t, null));
            foreach (int s in new[] { 7, 9 })
                foreach (string v in new[] { "rapid-top", "flat1" })
                    cases.Add((s, 6, false, v));
            var mixed = new[] { 4, 4, 14, 4, 4 };
            foreach (var (s, wd, t, v) in cases.Concat(new[] { (0, 6, false, (string)null), (0, 6, true, (string)null) }))
            {
                var reach = s == 0 ? Build(mixed, wd, t, v) : Build(s, 6, wd, t, v);
                var gen = reach.S.Generated;
                var (canon, settled, _) = Study.Canonical(reach.S, r);
                int cells = canon.CountWaterDiff(gen), labels = VisibleLabels(canon, gen);
                var sites = Study.PokeSites(reach.S).Where(p => p.x >= reach.S.EX0 && p.x <= reach.S.EX1).ToList();
                var rng = new Random(s * 100 + wd);
                if (!full && sites.Count > 120) sites = sites.OrderBy(_ => rng.Next()).Take(120).ToList();
                int changed = 0;
                foreach (var (x, y, z) in sites)
                {
                    var sim = Study.Poke(gen, r, 7 + x * 31 + z, x, y, z, 2500, out _);
                    if (sim.W.CountWaterDiff(gen) != 0) changed++;
                }
                int breaks = 0, flooding = 0, worst = 0;
                for (int x = reach.S.EX0; x <= reach.S.EX1; x++)
                    for (int y = reach.S.EY0; y <= reach.S.EY1; y++)
                        for (int z = reach.S.EZ0; z <= reach.S.EZ1; z++)
                        {
                            if (!gen.IsSolid(x, y, z) || !WetSolid(gen, x, y, z)) continue;
                            if (!full && (x + y + z) % 3 != 0) continue;
                            var sim = new Sim(gen.Clone(), r, 3 + breaks);
                            sim.BreakBlock(x, y, z);
                            // WW_DOUBLE=1: the block under it too (a hole 2 deep)
                            if (Environment.GetEnvironmentVariable("WW_DOUBLE") == "1" && gen.IsSolid(x, y - 1, z)) sim.BreakBlock(x, y - 1, z);
                            sim.Settle(Study.SettleMs, Study.SettleUpdates);
                            sim.WakeAll();
                            sim.Settle(Study.SettleMs, Study.SettleUpdates);
                            int n = Flood.NewWater(sim.W, gen, x, y, z);
                            breaks++;
                            if (n > 2) flooding++;
                            worst = Math.Max(worst, n);
                        }
                var sg = Surface(gen, reach); var sc = Surface(canon, reach, false);
                Console.WriteLine($"| {reach.S.Name} | {cells} / {labels}{(settled ? "" : " (never stops)")} | {changed} of {sites.Count} | {flooding} of {breaks} ({worst}) | " +
                                  $"{100 * sg.rapid / Math.Max(1, sg.cols)} % generated, {100 * sc.rapid / Math.Max(1, sc.cols)} % after | {sc.minDepth}–{sc.maxDepth} |");
            }
            if (Environment.GetEnvironmentVariable("WW_SHOW") is string show)
            {
                var p = show.Split(',');
                var reach = Build(int.Parse(p[0]), 6, int.Parse(p[1]), p.Length > 2 && p[2] == "t", p.Length > 3 ? p[3] : null);
                var (canon, _, _) = Study.Canonical(reach.S, r);
                Console.Write($"generated z={reach.S.SectionZ}\n" + reach.S.Generated.Section(reach.S.SectionZ) + "after waking every cell\n" + canon.Section(reach.S.SectionZ));
                Console.Write("top view, generated (top fluid cell)\n" + TopView(reach.S.Generated) + "after\n" + TopView(canon));
            }
        }

        /// <summary>The reach as Earth should draw it, column by column along the middle row: bed top, water top, the cells
        /// from the bed up (S/R ordinary/rapid source, rN rapid flowing N, wN flowing N, rd rapid falling), rapid mark, ramp.</summary>
        public static void Spec(string[] args)
        {
            int s = args.Length > 0 ? int.Parse(args[0]) : 5, wd = args.Length > 1 ? int.Parse(args[1]) : 6;
            var reach = s == 0 ? Build(new[] { 4, 4, 14, 4, 4 }, wd, false) : Build(s, 6, wd, false);
            var c = reach.C; var w = reach.S.Generated;
            var (canon, _, _) = Study.Canonical(reach.S, Rules());
            int z = reach.Z0 + 1;
            Console.WriteLine($"{reach.S.Name}: middle row z={z}; x from the upper lake's last columns to the lower pool");
            Console.WriteLine("| x | bed top | water top | ramp | rapid | cells from bed+1 up (generated) | same after waking every cell |");
            Console.WriteLine("|---|---|---|---|---|---|---|");
            string Cell(int v) => v == 0 ? "." : F.IsSource(v) ? (F.IsRapid(v) ? "R" : "S") + (F.Flow(v) == F.Still ? "" : ":" + F.FlowNames[F.Flow(v)])
                : (F.IsRapid(v) ? "r" : "w") + (F.Flow(v) == F.D ? "d" : "") + F.Level(v) + (F.Flow(v) == F.D ? "" : ":" + F.FlowNames[F.Flow(v)]);
            for (int x = reach.X0 - 2; x <= reach.X1 + 8; x++)
            {
                int k = c.K(x, z);
                var gen = new List<string>(); var aft = new List<string>();
                for (int y = c.Top[k] + 1; y <= c.WaterTop[k] + 2; y++) { gen.Add(Cell(w.Get(x, y, z))); aft.Add(Cell(canon.Get(x, y, z))); }
                while (gen.Count > 0 && gen[^1] == "." && aft[^1] == ".") { gen.RemoveAt(gen.Count - 1); aft.RemoveAt(aft.Count - 1); }
                string g = string.Join(" ", gen), a = string.Join(" ", aft);
                Console.WriteLine($"| {x} | {c.Top[k]} | {c.WaterTop[k]} | {c.Ramp[k]} | {(c.Rapid[k] ? "yes" : "")} | {g} | {(a == g ? "=" : a)} |");
            }
            Console.Write("top view (R/S source, a-f rapid flowing 1-6, ! falling)\n" + TopView(w));
        }

        static bool WetSolid(World w, int x, int y, int z)
        {
            if (w.Get(x, y + 1, z) != 0) return true;
            for (int h = 0; h < 4; h++) if (w.Get(x + Dir.HX[h], y, z + Dir.HZ[h]) != 0) return true;
            return false;
        }

        static int VisibleLabels(World a, World b)
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

        /// <summary>Per column, the top fluid cell: R/S rapid/ordinary source, a-f rapid flowing 1-6, 1-6 flowing, ! falling, # dry.</summary>
        public static string TopView(World w)
        {
            var sb = new System.Text.StringBuilder();
            for (int z = 0; z < w.SZ; z++)
            {
                for (int x = 0; x < w.SX; x++)
                {
                    int y = w.SY - 1;
                    while (y > 0 && w.Get(x, y, z) == 0) y--;
                    int c = w.Get(x, y, z);
                    sb.Append(c == 0 ? '#' : F.Flow(c) == F.D ? '!' : F.IsSource(c) ? (F.IsRapid(c) ? 'R' : 'S')
                        : F.IsRapid(c) ? (char)('a' + F.Level(c) - 1) : (char)('0' + F.Level(c)));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
