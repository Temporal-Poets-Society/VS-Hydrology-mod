using System;
using System.Collections.Generic;
using System.Linq;

namespace WaterSim
{
    /// <summary>
    /// `watersim flood [rules...]`: the in-game report of 2026-10-24 (Earth 0.16.10, Hydrology 0.1.6, Congo rapids: one block
    /// broken under water, and the banks flooded down to the next rapids). Earth's steps and chutes with sills, in the
    /// Hydrology shape; every solid block beside or under water inside the scene's box is broken once (each on a fresh copy),
    /// the world settles, and the cells that hold water now but were dry as generated are counted (the broken cell aside).
    /// </summary>
    public static class Flood
    {
        public static void Run(string[] args)
        {
            var rules = args.Length > 0 ? args.Select(Program.Rules).ToList()
                : new List<RuleSet> { Sweep.Recommended(), RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1") };
            bool oldSills = Columns.Sills, oldShape = Columns.HydrologyShape;
            Columns.Sills = Environment.GetEnvironmentVariable("WATERSIM_SILLS") != "0"; Columns.HydrologyShape = true;
            var scenes = new List<Scene> { Scenes.Step(), Scenes.Chute(), Scenes.StepDiagonal(), Tapered(false), Tapered(true) };
            Columns.Sills = oldSills; Columns.HydrologyShape = oldShape;
            bool verbose = Environment.GetEnvironmentVariable("FLOOD_VERBOSE") == "1";
            var only = Environment.GetEnvironmentVariable("FLOOD_BREAK");
            if (only != null)
            {
                var p = only.Split(',').Select(int.Parse).ToArray();
                var s = scenes.Find(q => q.Name == Environment.GetEnvironmentVariable("FLOOD_SCENE")) ?? scenes[0];
                foreach (var r in rules)
                {
                    var sim = new Sim(s.Generated.Clone(), r, 3);
                    sim.BreakBlock(p[0], p[1], p[2]);
                    sim.Settle(Study.SettleMs, Study.SettleUpdates);
                    sim.WakeAll();
                    sim.Settle(Study.SettleMs, Study.SettleUpdates);
                    Console.Write($"{s.Name} {r.Name}\n  generated z={p[2]}\n" + s.Generated.Section(p[2])
                        + $"  after the break ({NewWater(sim.W, s.Generated, p[0], p[1], p[2])} new)\n" + sim.W.Section(p[2]));
                }
                return;
            }
            foreach (var s in scenes)
                foreach (var r in rules)
                {
                    var gen = s.Generated;
                    if (FlowQuery.RiverEverywhere) FlowQuery.Current = FlowQuery.AllWaterIsRiver(gen);
                    // the generated shape itself first
                    var c0 = new Sim(gen.Clone(), r, 1); c0.WakeAll(); c0.Settle(Study.SettleMs, Study.SettleUpdates);
                    int base0 = NewWater(c0.W, gen, -1, -1, -1);
                    int sites = 0, flooding = 0, worst = 0; string worstAt = "";
                    var hist = new SortedDictionary<int, int>();
                    for (int x = s.EX0; x <= s.EX1; x++)
                        for (int y = s.EY0; y <= s.EY1; y++)
                            for (int z = s.EZ0; z <= s.EZ1; z++)
                            {
                                if (!gen.IsSolid(x, y, z) || !Wet(gen, x, y, z)) continue;
                                var sim = new Sim(gen.Clone(), r, 3 + sites);
                                sim.BreakBlock(x, y, z);
                                sim.Settle(Study.SettleMs, Study.SettleUpdates);
                                // later, anything may wake the cells nearby (a player swimming, a neighbour's update)
                                sim.WakeAll();
                                sim.Settle(Study.SettleMs, Study.SettleUpdates);
                                int n = NewWater(sim.W, gen, x, y, z);
                                sites++;
                                if (n > 0) flooding++;
                                if (n > worst) { worst = n; worstAt = $"{x},{y},{z}"; }
                                if (verbose && n > 0) Console.WriteLine($"     break {x},{y},{z}: {n} new water cells");
                            }
                    Console.WriteLine($"{s.Name,-14} {r.Name,-40} generated: {base0} new; {sites} breaks, {flooding} add water, worst {worst} at {worstAt}");
                    if (worst > 0 && Environment.GetEnvironmentVariable("FLOOD_SHOW") == "1")
                    {
                        var p = worstAt.Split(',').Select(int.Parse).ToArray();
                        var sim = new Sim(gen.Clone(), r, 3);
                        sim.BreakBlock(p[0], p[1], p[2]);
                        sim.Settle(Study.SettleMs, Study.SettleUpdates);
                        Console.Write("  generated section z=" + p[2] + "\n" + gen.Section(p[2]));
                        Console.Write("  after\n" + sim.W.Section(p[2]));
                    }
                }
        }

        /// <summary>
        /// A river 10 wide whose depth tapers to its banks (1 deep at the edge rows, 2, then 3 in the middle), banks flush
        /// with the water: a 1-block step, or a chute of three steps 4 apart (rapid). Sills under the steps.
        /// </summary>
        public static Scene Tapered(bool chute)
        {
            int W = 34, H = 16, z0 = 3, z1 = 12;
            var c = new Columns(W, H, 4);
            int[] xs = chute ? new[] { 1, 13, 17, 21, 25 } : new[] { 1, 16 };
            int top0 = chute ? 11 : 8;
            for (int i = 0; i < xs.Length; i++)
            {
                int xa = xs[i], xb = i + 1 < xs.Length ? xs[i + 1] - 1 : W - 2, wt = top0 - i;
                for (int z = z0; z <= z1; z++)
                {
                    int e = Math.Min(z - z0, z1 - z), depth = Math.Min(3, e + 1);
                    c.Pool(xa, xb, z, z, wt, wt - depth, rapid: chute && i > 0);
                }
            }
            for (int x = 0; x < W; x++)
                for (int z = 0; z < H; z++)
                {
                    int k = c.K(x, z);
                    if (c.Water[k] != 0) continue;
                    int wt = c.WaterTop[c.K(Math.Clamp(x, 1, W - 2), z0)];
                    int d = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
                    c.Top[k] = wt + Math.Max(0, d - 1);
                    if (x == 0 || x == W - 1) c.Top[k] = Math.Max(c.Top[k], top0 + 1);
                }
            c.SettleSteps();
            int lo = top0 - xs.Length + 1;
            return new Scene
            {
                Name = chute ? "tapered-chute" : "tapered-step", Title = chute ? "Tapered chute" : "Tapered step",
                Description = "River 10 wide, 1 deep at its flush banks, 3 in the middle, with sills",
                Generated = c.Place(top0 + 6), SectionZ = z0, EarthShape = true,
                EX0 = xs[1] - 3, EX1 = Math.Min(W - 2, xs[^1] + 3), EY0 = lo - 4, EY1 = top0 + 1, EZ0 = z0 - 1, EZ1 = z1 + 1,
            };
        }

        /// <summary>A solid block with water beside it or above it (a player under water can reach it).</summary>
        static bool Wet(World w, int x, int y, int z)
        {
            if (w.Get(x, y + 1, z) != 0) return true;
            for (int h = 0; h < 4; h++) if (w.Get(x + Dir.HX[h], y, z + Dir.HZ[h]) != 0) return true;
            return false;
        }

        /// <summary>Cells holding water now that were dry (no fluid, not solid) as generated.</summary>
        public static int NewWater(World now, World gen, int bx, int by, int bz)
        {
            int n = 0;
            for (int i = 0; i < now.Fluid.Length; i++)
            {
                if (now.Fluid[i] == 0 || gen.Fluid[i] != 0) continue;
                now.Pos(i, out int x, out int y, out int z);
                if (x == bx && y == by && z == bz) continue;
                n++;
            }
            return n;
        }
    }
}
