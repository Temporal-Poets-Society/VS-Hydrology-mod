using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WaterSim
{
    /// <summary>
    /// `watersim canals`: how player-dug canals fill, by width and depth. A lake (water top 8) on the west, flat land at 9,
    /// and a canal dug east from it, 20 long, `width` wide and `depth` deep (floor at 8 - depth). The lake is deep (bed 4)
    /// or shallow at its bank (1 deep, like a river edge). The canal is dug dry and opened last, or dug outward from the
    /// water. Counts the canal's cells at or below the lake's surface that end as sources, flowing water or air.
    /// </summary>
    public static class Canals
    {
        public static Scene Make(int width, int depth, bool shallowLake, bool outward)
        {
            int sx = 34, sz = Math.Max(14, width + 8), sy = 12;
            var w = new World(sx, sy, sz);
            w.FillSolid(0, 0, 0, sx - 1, 9, sz - 1);                       // land, top 9
            int lakeBed = shallowLake ? 7 : 3;
            for (int x = 1; x <= 10; x++)
                for (int z = 1; z < sz - 1; z++)
                    for (int y = lakeBed + 1; y <= 9; y++)
                    {
                        w.SetSolid(x, y, z, false);
                        if (y <= 8) w.SetFluid(x, y, z, F.Make(F.Water, F.Still, 7));
                    }
            int z0 = sz / 2 - width / 2, z1 = z0 + width - 1;
            var s = new Scene
            {
                Name = $"canal-w{width}-d{depth}{(shallowLake ? "-shallow" : "")}{(outward ? "-outward" : "")}",
                Title = $"Canal {width} wide, {depth} deep",
                Description = $"A canal {width} wide and {depth} deep (floor at {8 - depth}), 20 long, from a {(shallowLake ? "lake only 1 deep at its bank" : "deep lake")}, {(outward ? "dug outward from the water" : "dug dry and opened last")}.",
                Generated = w, SectionZ = (z0 + z1) / 2, EarthShape = false,
                EX0 = 11, EX1 = 30, EY0 = 8 - depth, EY1 = 9, EZ0 = z0, EZ1 = z1,
                River = new RiverStub
                {
                    NaturalTop = (x, z) => x >= 1 && x <= 10 && z >= 1 && z < sz - 1 ? 8 : -1,
                    Up = (3, 8, sz / 2), Down = (3, 8, sz / 2), Inflow = 40,
                },
            };
            s.River.Seeds.Add((3, 8, sz / 2, true));
            var cols = new List<(int x, int z)>();
            for (int x = 11; x <= 30; x++) for (int z = z0; z <= z1; z++) cols.Add((x, z));
            var order = outward ? cols : cols.Where(c => c.x != 11).Concat(cols.Where(c => c.x == 11)).ToList();
            bool first = true;
            foreach (var (x, z) in order)
            {
                bool opening = !outward && x == 11;
                int wait = outward ? 400 : opening && first ? 5000 : 60;
                if (opening) first = false;
                for (int y = 9; y >= 9 - depth; y--)
                {
                    s.Script.Add(new Edit { X = x, Y = y, Z = z, Place = false, WaitMs = wait });
                    wait = 40;
                }
            }
            return s;
        }

        public static (int sources, int flowing, int dry, int total, bool settled, long updates, double seconds) Measure(Scene s, RuleSet r)
        {
            var res = Playback.Record(s.Generated, r, s.Script, river: s.River, maxFrames: 100000);
            int src = 0, flow = 0, dry = 0, total = 0;
            for (int x = s.EX0; x <= s.EX1; x++)
                for (int z = s.EZ0; z <= s.EZ1; z++)
                    for (int y = s.EY0 + 1; y <= 8; y++)
                    {
                        total++;
                        int c = res.Final.Get(x, y, z);
                        if (F.IsSource(c)) src++; else if (c != 0) flow++; else dry++;
                    }
            double t = res.Frames.Count > 0 ? res.Frames[^1].T / 1000.0 : 0;
            return (src, flow, dry, total, res.Settled, res.Updates, t);
        }

        public static void Run(string[] args)
        {
            if (Environment.GetEnvironmentVariable("CANAL_SHOW") is string show)
            {
                var q = show.Split(',');
                var sc = Make(int.Parse(q[0]), int.Parse(q[1]), false, q.Length > 2 && q[2] == "out");
                var rs = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1" + (Environment.GetEnvironmentVariable("WW_RULES") ?? ""));
                var res = Playback.Record(sc.Generated, rs, sc.Script, river: sc.River, maxFrames: 100000);
                Console.WriteLine("section z=" + sc.SectionZ);
                Console.Write(res.Final.Section(sc.SectionZ, 3, 10));
                for (int y = 5; y <= 8; y++) { Console.WriteLine("y=" + y); Console.Write(res.Final.Slice(y)); }
                return;
            }
            string R = Sweep.RecommendedSpec;
            var rules = new List<(string label, RuleSet r)>
            {
                ("0.1.3", RuleSet.Hydrology013()),
                ("recommended", Sweep.Recommended()),
                ("+ basin fill (needs the river query)", RuleSet.Parse(R + ",ProtoLevels=1,ProtoBasinFill=1")),
                ("+ vessels (local)", RuleSet.Parse(R + ",FillVessels=1")),
                ("+ vessels 0.1.7 (depth, contained)", RuleSet.Parse(R + ",FillVessels=1,VesselsDepth=1,VesselsContained=1")),
            };
            var sb = new StringBuilder();
            sb.AppendLine("| Lake | Dug | Width | Depth | " + string.Join(" | ", rules.Select(r => r.label)) + " |");
            sb.AppendLine("|---|---|---|---|" + string.Concat(rules.Select(_ => "---|")));
            foreach (bool shallow in new[] { false, true })
                foreach (bool outward in new[] { false, true })
                    foreach (int depth in new[] { 1, 2, 3 })
                        foreach (int width in new[] { 1, 2, 3, 6 })
                        {
                            var s = Make(width, depth, shallow, outward);
                            var cells = rules.Select(r =>
                            {
                                var m = Measure(s, r.r);
                                string pct = $"{100 * m.sources / m.total}% src";
                                if (m.flowing > 0) pct += $", {100 * m.flowing / m.total}% flowing";
                                if (m.dry > 0) pct += $", {100 * m.dry / m.total}% dry";
                                if (!m.settled) pct += ", **never stops**";
                                return pct;
                            });
                            sb.AppendLine($"| {(shallow ? "1 deep at the bank" : "deep")} | {(outward ? "outward" : "opened last")} | {width} | {depth} | " + string.Join(" | ", cells) + " |");
                        }
            Console.Write(sb.ToString());
            if (args.Length > 0) System.IO.File.WriteAllText(args[0], "# Canals by width and depth\n\nShare of the canal's cells at or below the lake's surface (8) that end as sources, flowing water or dry.\n\n" + sb);
        }
    }
}
