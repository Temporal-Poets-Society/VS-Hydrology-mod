using System;
using System.Collections.Generic;
using System.Linq;

namespace WaterSim
{
    /// <summary>`watersim why scene rules [n]`: the first edit sequences and pokes that do not come back, with what they left.</summary>
    public static class WhyDebug
    {
        public static void Run(string sceneName, string rules, int n)
        {
            var s = Scenes.ByName(sceneName);
            var r = Program.Rules(rules);
            var (canon, ok, _) = Study.Canonical(s, r);
            var start = canon;
            if (s.Script.Count > 0)
            {
                var sim0 = new Sim(s.Generated.Clone(), r, 1);
                Study.ApplyScript(sim0, s.Script);
                sim0.Settle();
                start = sim0.W;
            }
            Console.WriteLine($"{s.Name} under {r.Name}; settled reference ok={ok}");
            int shown = 0;
            foreach (var (x, y, z) in Study.PokeSites(s))
            {
                var sim = Study.Poke(canon, r, 5, x, y, z, 3000, out bool settled);
                int d = sim.W.CountWaterDiff(canon);
                if (d == 0 && settled) continue;
                Console.WriteLine($"poke {x},{y},{z} ({(canon.IsSolid(x, y, z) ? "solid" : "air")}): {d} cells differ, settled={settled}");
                Diff(canon, sim.W);
                if (++shown >= n) break;
            }
            var rng = new Random(7);
            shown = 0;
            for (int t = 0; t < 400 && shown < n; t++)
            {
                var sim = new Sim(start.Clone(), r, rng.Next());
                var done = new List<(int x, int y, int z, bool placed)>();
                var log = new List<string>();
                int k = 1 + rng.Next(4);
                for (int i = 0; i < k; i++)
                {
                    int x = rng.Next(s.EX0, s.EX1 + 1), y = rng.Next(s.EY0, s.EY1 + 1), z = rng.Next(s.EZ0, s.EZ1 + 1);
                    if (done.Exists(dd => dd.x == x && dd.y == y && dd.z == z)) continue;
                    int wait = rng.Next(0, 3000);
                    sim.Run(wait);
                    bool solid = sim.W.IsSolid(x, y, z);
                    if (solid) sim.BreakBlock(x, y, z); else sim.PlaceStone(x, y, z);
                    done.Add((x, y, z, !solid));
                    log.Add($"{(solid ? "break" : "place")} {x},{y},{z} (+{wait} ms)");
                }
                sim.Run(rng.Next(500, 15000));
                for (int i = done.Count - 1; i >= 0; i--)
                {
                    var (x, y, z, placed) = done[i];
                    sim.Run(rng.Next(0, 3000));
                    if (placed) sim.BreakBlock(x, y, z); else sim.PlaceStone(x, y, z);
                }
                bool settled = sim.Settle();
                int d = sim.W.CountWaterDiff(start);
                if (d == 0 && settled) continue;
                Console.WriteLine($"sequence {string.Join("; ", log)}, undone: {d} cells differ, settled={settled}");
                Diff(start, sim.W);
                shown++;
            }
        }

        public static void Diff(World a, World b, int max = 12, bool labels = false)
        {
            var cols = new SortedSet<(int, int)>();
            int lines = 0;
            for (int i = 0; i < a.Fluid.Length; i++)
            {
                if (labels ? a.Fluid[i] == b.Fluid[i] : (a.Fluid[i] & ~0xF0) == (b.Fluid[i] & ~0xF0)) continue;
                a.Pos(i, out int x, out int y, out int z);
                if (lines++ < max) Console.WriteLine($"    {x},{y},{z}: {F.Name(a.Fluid[i])} -> {F.Name(b.Fluid[i])}");
                cols.Add((x, z));
            }
            if (lines > max) Console.WriteLine($"    ... {lines} cells");
        }
    }
}
