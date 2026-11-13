using System;
using System.Collections.Generic;
using System.Linq;

namespace WaterSim
{
    /// <summary>
    /// Earth's planned river flow query (decision 0022 part A) as a per-column stub: natural river yes/no, flow direction,
    /// discharge, and the average slope of the river around the column, with "rapids" where that slope is over a threshold.
    /// Read-only, set once per scene; the rules read <see cref="Current"/> (RuleSet.QueryNoVessels, QueryRapids).
    /// </summary>
    public sealed class FlowQuery
    {
        public static FlowQuery Current;

        public readonly int SX, SZ;
        public readonly bool[] IsRiver, IsRapids;
        public readonly double[] Slope, Discharge;
        public readonly sbyte[] FlowX, FlowZ;
        /// <summary>How often the rules read the query (the performance question).</summary>
        public long Reads;

        public FlowQuery(int sx, int sz)
        {
            SX = sx; SZ = sz;
            IsRiver = new bool[sx * sz]; IsRapids = new bool[sx * sz];
            Slope = new double[sx * sz]; Discharge = new double[sx * sz];
            FlowX = new sbyte[sx * sz]; FlowZ = new sbyte[sx * sz];
        }

        bool In(int x, int z) => x >= 0 && z >= 0 && x < SX && z < SZ;

        /// <summary>Every column with water in it is natural river, none is a rapids reach (WATERSIM_QUERY_RIVER=1 for the
        /// older scenes: verify-shape, flood).</summary>
        public static FlowQuery AllWaterIsRiver(World w)
        {
            var q = new FlowQuery(w.SX, w.SZ);
            for (int x = 0; x < w.SX; x++)
                for (int z = 0; z < w.SZ; z++)
                    for (int y = 0; y < w.SY; y++)
                        if (w.Get(x, y, z) != 0) { q.IsRiver[x + z * w.SX] = true; break; }
            return q;
        }

        public static bool RiverEverywhere => Environment.GetEnvironmentVariable("WATERSIM_QUERY_RIVER") == "1";
        public bool River(int x, int z) { Reads++; return In(x, z) && IsRiver[x + z * SX]; }
        public bool Rapids(int x, int z) { Reads++; return In(x, z) && IsRapids[x + z * SX]; }
    }

    /// <summary>
    /// `watersim query`: the flow-query rules (study 2026-10-25). Congo-like reaches as Earth would draw them without sills
    /// (1-block steps where the valley falls, pools at their natural depth), the query's rapids marked by average slope, and
    /// the checks of the earlier studies: first update, pokes, single breaks, canals, and player edits.
    /// </summary>
    public static class QueryStudy
    {
        /// <summary>Rapids where the average slope is at least 1 block in this many columns (Congo at 256 k: 1 in 35–46).</summary>
        public static int RapidsOneIn = 50;

        public sealed class Reach
        {
            public Scene S;
            public Columns C;
            public FlowQuery Q;
            public int[] At;
            public int Z0, Z1, Top;
        }

        public static RuleSet Base() => RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
        public static RuleSet WithQuery() => RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1,QueryNoVessels=1,QueryRapids=1");

        /// <summary>
        /// A river `width` wide: a calm stretch of `lead` columns, 1-block steps `gaps` apart, a calm stretch of `lead`
        /// columns. Pools `pool` deep (tapered: 1 less per row towards the banks, at least 1), banks flush with the water.
        /// With <paramref name="sills"/> the steps get Earth's sills (layouts 5–7); without, the edges are as deep as their
        /// pools. Drawn in the Hydrology shape (rapid rule marks, spills from ramp height 2). With
        /// <paramref name="queryRapids"/> the query's rapids columns get rapid surface sources and rapid ramps.
        /// </summary>
        public static Reach Build(int[] gaps, int width, int pool, bool taper, bool sills, bool queryRapids, int lead = 64, int bank = 4)
        {
            int steps = gaps.Length + 1, z0 = 4, z1 = z0 + width - 1, H = z1 + 1 + bank;
            var at = new int[steps];
            at[0] = 1 + lead;
            for (int i = 1; i < steps; i++) at[i] = at[i - 1] + gaps[i - 1];
            int W = at[steps - 1] + lead + 1, top = 4 + pool + steps;
            var c = new Columns(W, H, 2);
            int Depth(int z) => taper ? Math.Max(1, Math.Min(pool, Math.Min(z - z0, z1 - z) + 1)) : pool;
            for (int z = z0; z <= z1; z++)
                for (int s = 0; s <= steps; s++)
                {
                    int xa = s == 0 ? 1 : at[s - 1], xb = s == steps ? W - 2 : at[s] - 1, wt = top - s;
                    c.Pool(xa, xb, z, z, wt, wt - Depth(z), rapid: s > 0);
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
            var q = new FlowQuery(W, H);
            for (int x = 0; x < W; x++)
            {
                // the reach's slope, constant along the pool the column is in: one block over the distance between the
                // steps above and below it (none above or below: calm). A sliding count over a window put rapids strips
                // into the middle of calm pools between steps 80-90 apart.
                int up = at.Where(a => a <= x).DefaultIfEmpty(-1).Max(), down = at.Where(a => a > x).DefaultIfEmpty(-1).Min();
                double slope = up < 0 || down < 0 ? 0 : 1.0 / (down - up);
                for (int z = 0; z < H; z++)
                {
                    int k = c.K(x, z);
                    if (c.Water[k] == 0) continue;
                    q.IsRiver[k] = true; q.Slope[k] = slope; q.Discharge[k] = 1200; q.FlowX[k] = 1;
                    q.IsRapids[k] = slope * RapidsOneIn >= 1;
                }
            }
            bool oldSills = Columns.Sills; int oldSpill = Columns.SpillFrom;
            Columns.Sills = sills; Columns.SpillFrom = 2;
            c.SettleSteps();
            RapidRule.MarkRapid(c);
            if (queryRapids)
                for (int k = 0; k < W * H; k++)
                {
                    if (c.Water[k] == 0 || c.Fall[k] != 0) continue;
                    // a ramp follows the step's edge above it: rapid when the edge column is in a rapids reach
                    int x = k % W, z = k / W;
                    bool feederRapids = false;
                    for (int h = 0; h < 4; h++)
                    {
                        int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                        if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                        int m = c.K(nx, nz);
                        if (c.Water[m] != 0 && c.WaterTop[m] == c.WaterTop[k] + 1 && q.IsRapids[m]) feederRapids = true;
                    }
                    if (q.IsRapids[k] || (c.Ramp[k] > 0 && feederRapids)) c.Rapid[k] = true;
                }
            // ... and so does the rest of the ramp, cell by cell down from its top
            if (queryRapids)
                for (int pass = 0; pass < 6; pass++)
                    for (int k = 0; k < W * H; k++)
                    {
                        if (c.Rapid[k] || c.Ramp[k] == 0) continue;
                        int x = k % W, z = k / W;
                        for (int h = 0; h < 4; h++)
                        {
                            int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                            if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                            int m = c.K(nx, nz);
                            if (c.Rapid[m] && c.Ramp[m] == c.Ramp[k] + 1 && c.WaterTop[m] == c.WaterTop[k]) c.Rapid[k] = true;
                        }
                    }
            var w = c.Place(top + 6, true);
            Columns.Sills = oldSills; Columns.SpillFrom = oldSpill;
            if (queryRapids)
                for (int z = 0; z < H; z++)
                    for (int x = 0; x < W; x++)
                    {
                        if (!q.IsRapids[c.K(x, z)]) continue;
                        int y = w.SY - 1;
                        while (y > 0 && w.Get(x, y, z) == 0) y--;
                        int f = w.Get(x, y, z);
                        if (F.IsSource(f) && !F.IsRapid(f)) w.SetFluid(x, y, z, F.WithKind(f, F.Rapid));
                    }
            // a step's edge in a calm column above a rapids pool stays ordinary unless H4's window makes it rapid
            if (queryRapids)
                for (int z = 0; z < H; z++)
                    for (int x = 0; x < W; x++)
                    {
                        int k = c.K(x, z);
                        if (q.IsRapids[k] || c.Water[k] == 0 || RapidRule.EdgeRapid(c, k)) continue;
                        int y = c.WaterTop[k], f = w.Get(x, y, z);
                        if (F.IsSource(f) && F.IsRapid(f)) w.SetFluid(x, y, z, F.WithKind(f, F.Water));
                    }
            string name = $"g{string.Join("-", gaps)}-w{width}-p{pool}{(taper ? "-tapered" : "")}{(sills ? "-sills" : "")}{(queryRapids ? "-qr" : "")}";
            var scene = new Scene
            {
                Name = name, Generated = w, EarthShape = true, SectionZ = (z0 + z1) / 2,
                EX0 = at[0] - 8, EX1 = at[steps - 1] + 10, EY0 = top - steps - pool - 1, EY1 = top + 1, EZ0 = z0 - 1, EZ1 = z1 + 1,
            };
            return new Reach { S = scene, C = c, Q = q, At = at, Z0 = z0, Z1 = z1, Top = top };
        }

        /// <summary>Top fluid cells of the river columns: rapid share in the query's rapids columns and in calm columns.</summary>
        static (int rapidIn, int nIn, int rapidOut, int nOut) Surface(World w, Reach r)
        {
            int ri = 0, ni = 0, ro = 0, no = 0;
            for (int z = r.Z0; z <= r.Z1; z++)
                for (int x = 1; x < w.SX - 1; x++)
                {
                    int y = w.SY - 1;
                    while (y > 0 && w.Get(x, y, z) == 0) y--;
                    int c = w.Get(x, y, z);
                    if (c == 0) continue;
                    if (r.Q.IsRapids[x + z * r.Q.SX]) { ni++; if (F.IsRapid(c)) ri++; }
                    else { no++; if (F.IsRapid(c)) ro++; }
                }
            return (ri, ni, ro, no);
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

        static string Check(Reach reach, RuleSet r, bool full)
        {
            FlowQuery.Current = reach.Q;
            reach.Q.Reads = 0;
            var gen = reach.S.Generated;
            var sim0 = new Sim(gen.Clone(), r, 1);
            sim0.WakeAll();
            bool settled = sim0.Settle(Study.SettleMs, Study.SettleUpdates);
            var canon = sim0.W;
            long reads = reach.Q.Reads, updates = sim0.Updates;
            int cells = canon.CountWaterDiff(gen), labels = VisibleLabels(canon, gen);
            var rng = new Random(11);
            var sites = Study.PokeSites(reach.S).Where(p => p.x >= reach.S.EX0 && p.x <= reach.S.EX1).ToList();
            if (!full && sites.Count > 150) sites = sites.OrderBy(_ => rng.Next()).Take(150).ToList();
            int changed = 0;
            foreach (var (x, y, z) in sites)
                if (Study.Poke(gen, r, 7 + x * 31 + z, x, y, z, 2500, out _).W.CountWaterDiff(gen) != 0) changed++;
            int breaks = 0, flooding = 0, worst = 0, doubleFlood = 0, doubleWorst = 0;
            for (int x = reach.S.EX0; x <= reach.S.EX1; x++)
                for (int y = reach.S.EY0; y <= reach.S.EY1; y++)
                    for (int z = reach.S.EZ0; z <= reach.S.EZ1; z++)
                    {
                        if (!gen.IsSolid(x, y, z) || !Wet(gen, x, y, z)) continue;
                        if (!full && (x * 7 + y * 3 + z) % 10 != 0) continue;
                        foreach (bool dbl in new[] { false, true })
                        {
                            var sim = new Sim(gen.Clone(), r, 3 + breaks);
                            sim.BreakBlock(x, y, z);
                            if (dbl) { if (!gen.IsSolid(x, y - 1, z)) continue; sim.BreakBlock(x, y - 1, z); }
                            sim.Settle(Study.SettleMs, Study.SettleUpdates);
                            sim.WakeAll();
                            sim.Settle(Study.SettleMs, Study.SettleUpdates);
                            int n = Flood.NewWater(sim.W, gen, x, y, z) - (dbl ? 1 : 0);
                            if (!dbl) { breaks++; if (n > 2) flooding++; worst = Math.Max(worst, n); }
                            else { if (n > 2) doubleFlood++; doubleWorst = Math.Max(doubleWorst, n); }
                        }
                    }
            var sg = Surface(gen, reach); var sc = Surface(canon, reach);
            string Pct(int a, int b) => b == 0 ? "–" : $"{100 * a / b} %";
            return $"| {reach.S.Name} | {(r.QueryRapids ? "query" : "0.1.7")} | {cells} / {labels}{(settled ? "" : " (never stops)")} | {changed} of {sites.Count} | " +
                   $"{flooding} of {breaks} ({worst}) | {doubleFlood} ({doubleWorst}) | {Pct(sc.rapidIn, sc.nIn)} | {Pct(sc.rapidOut, sc.nOut)} | {(updates > 0 ? (double)reads / updates : 0):0.00} |";
        }

        static bool Wet(World w, int x, int y, int z)
        {
            if (w.Get(x, y + 1, z) != 0) return true;
            for (int h = 0; h < 4; h++) if (w.Get(x + Dir.HX[h], y, z + Dir.HZ[h]) != 0) return true;
            return false;
        }

        public static void Run(string[] args)
        {
            bool full = args.Contains("full");
            if (args.Contains("canals")) { Canals(); return; }
            if (args.Contains("edits")) { Edits(); return; }
            if (args.FirstOrDefault(a => a.StartsWith("show=")) is string show) { Show(show.Substring(5)); return; }
            if (args.FirstOrDefault(a => a.StartsWith("spec=")) is string sp) { Spec(sp.Substring(5)); return; }
            Console.WriteLine($"Rapids where the reach's slope (1 block over the distance between the steps around the pool) is at least 1 in {RapidsOneIn}.");
            Console.WriteLine("| Reach | Rules | First update: cells / visible labels | Pokes changing it | Single breaks adding water (worst) | 2-deep holes adding water (worst) | Rapid surface, query's rapids columns | Rapid surface, calm columns | Query reads per update |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
            var reaches = new List<(int[] g, int w, int p, bool t)>
            {
                (new[] { 40, 35, 46, 38 }, 10, 3, false),
                (new[] { 40, 35, 46, 38 }, 10, 3, true),
                (new[] { 40, 5, 5, 5, 5, 40 }, 10, 3, true),
                (new[] { 12, 25, 8, 30 }, 6, 2, true),
                (new[] { 90, 80 }, 10, 3, true),
            };
            foreach (var (g, wd, p, t) in reaches)
            {
                Console.WriteLine(Check(Build(g, wd, p, t, sills: true, queryRapids: false), Base(), full));
                Console.WriteLine(Check(Build(g, wd, p, t, sills: false, queryRapids: true), WithQuery(), full));
            }
            FlowQuery.Current = null;
        }

        /// <summary>The reach as generated and after waking every cell: top view and a section, and the column table.</summary>
        static void Show(string spec)
        {
            var p = spec.Split(',');
            var gaps = p[0].Split('-').Select(int.Parse).ToArray();
            var reach = Build(gaps, int.Parse(p[1]), int.Parse(p[2]), p.Length > 3 && p[3] == "t", sills: false, queryRapids: true);
            FlowQuery.Current = reach.Q;
            var (canon, _, _) = Study.Canonical(reach.S, WithQuery());
            int z = (reach.Z0 + reach.Z1) / 2;
            Console.WriteLine($"{reach.S.Name}: steps at x = {string.Join(", ", reach.At)}; query rapids columns: {string.Join(",", Ranges(reach))}");
            Console.Write("top view generated (R/S source, a-f rapid flowing, 1-6 flowing, ! falling)\n" + WhiteWater.TopView(reach.S.Generated));
            Console.Write("after waking every cell\n" + WhiteWater.TopView(canon));
            Console.Write($"section z={z} generated\n" + reach.S.Generated.Section(z) + "after\n" + canon.Section(z));
            FlowQuery.Current = null;
        }

        /// <summary>Column table of the middle row around chosen x ranges (spec=gaps,width,pool[,t]:x0-x1;x0-x1).</summary>
        static void Spec(string spec)
        {
            var parts = spec.Split(':');
            var p = parts[0].Split(',');
            var gaps = p[0].Split('-').Select(int.Parse).ToArray();
            var reach = Build(gaps, int.Parse(p[1]), int.Parse(p[2]), p.Length > 3 && p[3] == "t", sills: false, queryRapids: true);
            FlowQuery.Current = reach.Q;
            var (canon, _, _) = Study.Canonical(reach.S, WithQuery());
            var c = reach.C; var w = reach.S.Generated;
            int z = (reach.Z0 + reach.Z1) / 2;
            string Cell(int v) => v == 0 ? "." : F.IsSource(v) ? (F.IsRapid(v) ? "R" : "S") + (F.Flow(v) == F.Still ? "" : ":" + F.FlowNames[F.Flow(v)])
                : (F.IsRapid(v) ? "r" : "w") + (F.Flow(v) == F.D ? "d" : "") + F.Level(v) + (F.Flow(v) == F.D ? "" : ":" + F.FlowNames[F.Flow(v)]);
            Console.WriteLine($"{reach.S.Name}, middle row z={z}; steps at x = {string.Join(", ", reach.At)}; query rapids: {string.Join(",", Ranges(reach))}");
            Console.WriteLine("| x | bed top | water top | query rapids | ramp | cells from bed+1 up (generated) | after waking every cell |");
            Console.WriteLine("|---|---|---|---|---|---|---|");
            foreach (var range in parts[1].Split(';'))
            {
                var ab = range.Split('-').Select(int.Parse).ToArray();
                for (int x = ab[0]; x <= ab[1]; x++)
                {
                    int k = c.K(x, z);
                    var gen = new List<string>(); var aft = new List<string>();
                    for (int y = c.Top[k] + 1; y <= c.WaterTop[k] + 2; y++) { gen.Add(Cell(w.Get(x, y, z))); aft.Add(Cell(canon.Get(x, y, z))); }
                    while (gen.Count > 0 && gen[^1] == "." && aft[^1] == ".") { gen.RemoveAt(gen.Count - 1); aft.RemoveAt(aft.Count - 1); }
                    string g = string.Join(" ", gen), a = string.Join(" ", aft);
                    Console.WriteLine($"| {x} | {c.Top[k]} | {c.WaterTop[k]} | {(reach.Q.IsRapids[k] ? "yes" : "")} | {c.Ramp[k]} | {g} | {(a == g ? "=" : a)} |");
                }
                Console.WriteLine("| … | | | | | | |");
            }
            FlowQuery.Current = null;
        }

        static IEnumerable<string> Ranges(Reach r)
        {
            int z = r.Z0, a = -1;
            for (int x = 0; x <= r.Q.SX; x++)
            {
                bool on = x < r.Q.SX && r.Q.IsRapids[x + z * r.Q.SX];
                if (on && a < 0) a = x;
                if (!on && a >= 0) { yield return $"{a}-{x - 1}"; a = -1; }
            }
        }

        /// <summary>Canals off a lake and off a river (the lake's columns marked as river), with the query rules.</summary>
        static void Canals()
        {
            Console.WriteLine("| Off | Width | Depth | 0.1.7 | query rules |");
            Console.WriteLine("|---|---|---|---|---|");
            foreach (bool river in new[] { false, true })
                foreach (int depth in new[] { 1, 2, 3 })
                    foreach (int width in new[] { 2, 3, 6 })
                    {
                        var s = WaterSim.Canals.Make(width, depth, shallowLake: false, outward: false);
                        var q = new FlowQuery(s.Generated.SX, s.Generated.SZ);
                        for (int x = 1; x <= 10; x++) for (int z = 0; z < s.Generated.SZ; z++) q.IsRiver[x + z * q.SX] = river;
                        FlowQuery.Current = q;
                        string Pct(RuleSet r)
                        {
                            var m = WaterSim.Canals.Measure(s, r);
                            return $"{100 * m.sources / m.total}% src" + (m.flowing > 0 ? $", {100 * m.flowing / m.total}% flowing" : "") + (m.settled ? "" : ", never stops");
                        }
                        Console.WriteLine($"| {(river ? "a river" : "a lake")} | {width} | {depth} | {Pct(Base())} | {Pct(WithQuery())} |");
                    }
            FlowQuery.Current = null;
        }

        /// <summary>
        /// Player edits in a Congo-like rapids reach without sills, with the query rules: a canal dug off a pool into the
        /// bank (2 wide, 3 deep, 16 long); a bypass canal joining the pool above a step to the pool below it; a dam across
        /// the river (stone up to one above the surface) and its removal 30 s later; a row of stones across a rapids pool.
        /// </summary>
        static void Edits()
        {
            var r = WithQuery();
            foreach (string what in new[] { "canal", "bypass", "dam", "dam-removed", "stones-row" })
            {
                var reach = Build(new[] { 40, 35, 46, 38 }, 10, 3, false, sills: false, queryRapids: true, bank: 22);
                FlowQuery.Current = reach.Q;
                var gen = reach.S.Generated;
                var sim = new Sim(gen.Clone(), r, 5);
                int s1 = reach.At[1], top1 = reach.Top - 1, zb = reach.Z1 + 1;
                var dug = new HashSet<(int, int)>();
                switch (what)
                {
                    case "canal":
                        // off the pool between steps 1 and 2, south into the bank: x s1+10..s1+11, z from the bank 16 long
                        for (int z = zb; z < Math.Min(gen.SZ - 1, zb + 16); z++)
                            for (int x = s1 + 10; x <= s1 + 12; x++) { dug.Add((x, z)); for (int y = top1 + 1; y >= top1 - 2; y--) sim.BreakBlock(x, y, z); sim.Run(200); }
                        break;
                    case "bypass":
                        // 2 wide, 3 deep, from the pool above step 2 to the pool below it, along the south bank
                        {
                            int s2 = reach.At[2];
                            for (int z = zb; z <= zb + 1; z++)
                                for (int x = s2 - 12; x <= s2 + 12; x++) { dug.Add((x, z)); for (int y = top1 + 1; y >= top1 - 2; y--) sim.BreakBlock(x, y, z); sim.Run(60); }
                            break;
                        }
                    case "dam":
                    case "dam-removed":
                        for (int z = reach.Z0; z <= reach.Z1; z++)
                            for (int y = top1 - 3; y <= top1 + 1; y++) if (!sim.W.IsSolid(s1 + 15, y, z)) sim.PlaceStone(s1 + 15, y, z);
                        if (what == "dam-removed")
                        {
                            sim.Run(30000);
                            for (int z = reach.Z0; z <= reach.Z1; z++) for (int y = top1 - 3; y <= top1 + 1; y++) sim.BreakBlock(s1 + 15, y, z);
                        }
                        break;
                    case "stones-row":
                        for (int z = reach.Z0; z <= reach.Z1; z++) sim.PlaceStone(s1 + 20, top1, z);
                        sim.Run(20000);
                        for (int z = reach.Z0; z <= reach.Z1; z++) sim.BreakBlock(s1 + 20, top1, z);
                        break;
                }
                bool ok = sim.Settle(Study.SettleMs, Study.SettleUpdates);
                sim.WakeAll();
                ok &= sim.Settle(Study.SettleMs, Study.SettleUpdates);
                // river columns: cells changed; dug columns: sources at or below the river's surface
                int riverDiff = 0, canalCells = 0, canalSrc = 0;
                for (int i = 0; i < gen.Fluid.Length; i++)
                {
                    gen.Pos(i, out int x, out int y, out int z);
                    if (dug.Contains((x, z))) { if (y <= top1 && y >= top1 - 2 && !sim.W.IsSolid(x, y, z)) { canalCells++; if (F.IsSource(sim.W.Fluid[i])) canalSrc++; } continue; }
                    if (what.StartsWith("dam") && x == s1 + 15 && what == "dam") continue;
                    if ((gen.Fluid[i] & ~0xF0) != (sim.W.Fluid[i] & ~0xF0))
                    {
                        riverDiff++;
                        if (Environment.GetEnvironmentVariable("Q_CELLS") == what) Console.WriteLine($"   {x},{y},{z}: {F.Name(gen.Fluid[i])} -> {F.Name(sim.W.Fluid[i])}");
                    }
                }
                var sc = Surface(sim.W, reach);
                Console.WriteLine($"{what,-12} settled={ok} river cells changed: {riverDiff}; dug cells at or below the surface: {canalSrc} of {canalCells} sources; rapid surface in rapids columns {100 * sc.rapidIn / Math.Max(1, sc.nIn)} %");
                if (Environment.GetEnvironmentVariable("Q_SHOW") == what)
                    Console.Write(WhiteWater.TopView(sim.W) + sim.W.Section(reach.Z1 + 1));
            }
            FlowQuery.Current = null;
        }
    }
}
