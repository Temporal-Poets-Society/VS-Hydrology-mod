using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WaterSim
{
    /// <summary>
    /// `watersim rapidrule`: which step edges and ramps Earth should mark rapid so that H4 leaves them as generated.
    /// Builds chutes with steps 1..9 columns apart (2..4 steps), lets the recommended rules settle them (every cell woken
    /// once), and compares H4's result at each step edge with <see cref="EdgeRapid"/>, H4's window computed on Earth's
    /// columns (water top, bed and ramp per column), which Earth can evaluate at generation.
    /// </summary>
    public static class RapidRule
    {
        /// <summary>
        /// H4's window on columns. The edge is upper column k (water top y0) beside lower water. Downstream: a breadth-first
        /// search over 4-neighbour columns, up to 6 steps, of the level where water from the edge lands (a column's water
        /// top, or one above a dry bed); through water at or above the current level it carries on at that level, onto dry
        /// ground it stops. Upstream: up to 5 steps through columns whose water reaches the current level (or starts at most
        /// 2 above it), the highest surface (a ramp cell counts: water top + 1). Rapid when some surface within i steps
        /// upstream is 2 or more above some landing level within j steps downstream, i + j &lt;= 6, j &gt;= 1.
        /// </summary>
        public static bool EdgeRapid(Columns c, int k, int reach = 6, int drop = 2)
        {
            int W = c.W, H = c.H;
            int y0 = c.WaterTop[k];
            bool Water(int m) => c.Water[m] != 0;
            IEnumerable<int> Nb(int m)
            {
                int x = m % W, z = m / W;
                if (z > 0) yield return m - W;
                if (x < W - 1) yield return m + 1;
                if (z < H - 1) yield return m + W;
                if (x > 0) yield return m - 1;
            }
            var down = Enumerable.Repeat(int.MaxValue, reach + 1).ToArray();
            var seen = new HashSet<int> { k };
            var q = new Queue<(int m, int level, bool wet, int dist)>();
            foreach (int n in Nb(k))
            {
                if (c.Top[n] >= y0) continue;                        // a bank: no open side
                if (Water(n) && c.WaterTop[n] >= y0) continue;       // a source beside it
                seen.Add(n);
                if (Water(n)) q.Enqueue((n, c.WaterTop[n], true, 1));
                else q.Enqueue((n, c.Top[n] + 1, c.Top[n] + 1 == y0, 1));
            }
            if (q.Count == 0) return false;
            while (q.Count > 0)
            {
                var (m, level, wet, dist) = q.Dequeue();
                down[dist] = Math.Min(down[dist], level);
                if (dist == reach || !wet) continue;
                foreach (int n in Nb(m))
                {
                    if (seen.Contains(n) || c.Top[n] >= level) continue;
                    seen.Add(n);
                    if (Water(n) && c.WaterTop[n] >= level) q.Enqueue((n, level, true, dist + 1));
                    else if (Water(n)) q.Enqueue((n, c.WaterTop[n], true, dist + 1));
                    else q.Enqueue((n, c.Top[n] + 1, c.Top[n] + 1 == level, dist + 1));
                }
            }
            for (int d = 2; d <= reach; d++) down[d] = Math.Min(down[d], down[d - 1]);
            if (y0 - down[1] >= drop) return true;

            var up = Enumerable.Repeat(y0, reach).ToArray();
            var seenUp = new HashSet<int> { k };
            var qu = new Queue<(int m, int level, int dist)>();
            qu.Enqueue((k, y0, 0));
            while (qu.Count > 0)
            {
                var (m, level, dist) = qu.Dequeue();
                up[dist] = Math.Max(up[dist], level);
                if (dist == reach - 1) continue;
                foreach (int n in Nb(m))
                {
                    if (seenUp.Contains(n) || !Water(n)) continue;
                    int bottom = c.Top[n] + 1, surface = c.WaterTop[n] + (c.Ramp[n] > 0 ? 1 : 0);
                    if (surface < level || bottom > level + drop) continue;
                    seenUp.Add(n);
                    qu.Enqueue((n, surface, dist + 1));
                }
            }
            for (int d = 1; d < reach; d++) up[d] = Math.Max(up[d], up[d - 1]);
            for (int i = 0; i < reach; i++)
                for (int j = 1; i + j <= reach; j++)
                    if (up[i] - down[j] >= drop) return true;
            return false;
        }

        static Columns Chute(int spacing, int steps, int width)
        {
            int z0 = 4, z1 = 4 + width - 1, H = z1 + 5, x0 = 11, W = x0 + spacing * steps + 16;
            var c = new Columns(W, H, 4);
            int top = 8 + steps;
            c.Pool(1, x0 - 1, z0, z1, top, top - 3);
            for (int s = 1; s < steps; s++) c.Pool(x0 + (s - 1) * spacing, x0 + s * spacing - 1, z0, z1, top - s, top - s - 2, rapid: true);
            c.Pool(x0 + (steps - 1) * spacing, W - 2, z0, z1, top - steps, top - steps - 3, rapid: true);
            for (int i = 0; i < W * H; i++)
            {
                if (c.Water[i] != 0) continue;
                int x = i % W, z = i / W;
                int wt = c.WaterTop[c.K(Math.Clamp(x, 1, W - 2), z0)];
                int d = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
                c.Top[i] = wt + 1 + Math.Max(0, d - 1);
            }
            bool old = Columns.Sills; Columns.Sills = true;
            c.SettleSteps();
            Columns.Sills = old;
            return c;
        }

        /// <summary>Earth's marks from the rule: a lower pool's ramp (and spill) is rapid when an edge of the step above it is.</summary>
        internal static void MarkRapid(Columns c)
        {
            var rapidTops = new HashSet<int>();
            for (int k = 0; k < c.W * c.H; k++)
                if (c.Water[k] != 0 && Enumerable.Range(0, 4).Any(h => IsLowerSeed(c, k, h)) && EdgeRapid(c, k)) rapidTops.Add(c.WaterTop[k] - 1);
            for (int k = 0; k < c.W * c.H; k++)
                if (c.Water[k] != 0 && c.Fall[k] == 0) c.Rapid[k] = rapidTops.Contains(c.WaterTop[k]);
        }

        public static void Check()
        {
            var r = Sweep.Recommended();
            int old = Columns.SpillFrom;
            foreach (int spillFrom in new[] { 3, 2 })
            {
                Columns.SpillFrom = spillFrom;
                int clean = 0, n = 0;
                var bad = new List<string>();
                foreach (int width in new[] { 3, 6 })
                    foreach (int steps in new[] { 2, 3, 4 })
                        foreach (int spacing in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })
                        {
                            var c = Chute(spacing, steps, width);
                            MarkRapid(c);
                            var gen = c.Place(20, true);
                            var sim = new Sim(gen.Clone(), r, 1);
                            sim.WakeAll();
                            sim.Settle(Study.SettleMs, Study.SettleUpdates);
                            int d = sim.W.CountWaterDiff(gen);
                            n++;
                            if (d == 0) clean++; else bad.Add($"spacing {spacing}, {steps} steps, {width} wide: {d} cells");
                        }
                Console.WriteLine($"spills from ramp height {spillFrom}, rapid marks from the rule: {clean} of {n} chutes stay as generated");
                foreach (var b in bad.Take(12)) Console.WriteLine("   " + b);
            }
            Columns.SpillFrom = old;
        }

        public static void Run()
        {
            var r = Sweep.Recommended();
            var sb = new StringBuilder();
            int agree = 0, total = 0;
            sb.AppendLine("| Spacing | Steps | Width | Step: H4 rapid edge sources / Earth rule | Ramp cells H4 leaves rapid, per step |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (int width in new[] { 3, 6 })
                foreach (int steps in new[] { 2, 3, 4 })
                    foreach (int spacing in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })
                    {
                        var c = Chute(spacing, steps, width);
                        var gen = c.Place(20, true);
                        var sim = new Sim(gen.Clone(), r, 1);
                        sim.WakeAll();
                        sim.Settle(Study.SettleMs, Study.SettleUpdates);
                        var w = sim.W;
                        var stepText = new List<string>();
                        var rampText = new List<string>();
                        // steps: edge columns grouped by their water top
                        foreach (var g in Enumerable.Range(0, c.W * c.H)
                                     .Where(k => c.Water[k] != 0 && Enumerable.Range(0, 4).Any(h => IsLowerSeed(c, k, h)))
                                     .GroupBy(k => c.WaterTop[k]).OrderByDescending(g => g.Key))
                        {
                            int h4 = 0, rule = 0, n = 0;
                            foreach (int k in g)
                            {
                                int x = k % c.W, z = k / c.W, y = c.WaterTop[k];
                                int cell = w.Get(x, y, z);
                                if (!F.IsSource(cell)) continue;
                                n++;
                                bool a = F.IsRapid(cell), b = EdgeRapid(c, k);
                                if (a) h4++;
                                if (b) rule++;
                                total++;
                                if (a == b) agree++;
                            }
                            stepText.Add($"{h4}/{rule} of {n}");
                            // ramp cells of the lower pool this step feeds
                            int lowTop = g.Key - 1, rr = 0, rn = 0;
                            for (int k = 0; k < c.W * c.H; k++)
                            {
                                if (c.Water[k] == 0 || c.WaterTop[k] != lowTop || c.Ramp[k] == 0) continue;
                                int cell = w.Get(k % c.W, lowTop + 1, k / c.W);
                                if (cell == 0 || F.IsSource(cell)) continue;
                                rn++;
                                if (F.IsRapid(cell)) rr++;
                            }
                            rampText.Add($"{rr}/{rn}");
                        }
                        sb.AppendLine($"| {spacing} | {steps} | {width} | {string.Join("; ", stepText)} | {string.Join("; ", rampText)} |");
                    }
            sb.AppendLine().AppendLine($"Edge sources where the rule agrees with H4: {agree} of {total}.");
            Console.Write(sb.ToString());
        }

        /// <summary>Column k has a neighbour on side h whose water is exactly one lower: k is a step's upper edge.</summary>
        static bool IsLowerSeed(Columns c, int k, int h)
        {
            int x = k % c.W, z = k / c.W;
            int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
            if (nx < 0 || nz < 0 || nx >= c.W || nz >= c.H) return false;
            int n = c.K(nx, nz);
            return c.Water[n] != 0 && c.WaterTop[n] == c.WaterTop[k] - 1;
        }
    }
}
