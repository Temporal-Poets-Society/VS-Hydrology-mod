using System;
using System.Collections.Generic;
using System.Linq;

namespace WaterSim
{
    /// <summary>
    /// Scenes for the later targets (Earth 0024 section 4, 0022 part B): a pond behind a dam, a diversion that should dry a
    /// stretch, a dam that fails, a lock, a long run over flat ground, power from drop × flow. Each has a river-query stub.
    /// </summary>
    public static partial class Scenes
    {
        public static List<Scene> FeatureScenes() => new() { PondDam(), Diversion(), DamFailure(), Lock(), LongRun(), Power() };

        /// <summary>A river in a canyon: segments (x0, x1, water top, bed) across z 4..9, walls at <paramref name="wall"/>.</summary>
        static Columns Canyon(int w, int h, int wall, params (int x0, int x1, int top, int bed)[] segs)
        {
            var c = new Columns(w, h, wall);
            foreach (var (x0, x1, top, bed) in segs) c.Pool(x0, x1, 4, 9, top, bed, rapid: false);
            c.SettleSteps();
            for (int i = 0; i < w * h; i++) if (c.Water[i] == 0) c.Top[i] = Math.Max(c.Top[i], wall);
            return c;
        }

        static RiverStub Stub(Columns c, (int, int, int) up, (int, int, int) down, double inflow, params (int x, int y, int z, bool ext)[] seeds)
        {
            var tops = (int[])c.WaterTop.Clone();
            var water = (byte[])c.Water.Clone();
            int w = c.W;
            var s = new RiverStub
            {
                NaturalTop = (x, z) => x < 0 || z < 0 || x >= w || z >= c.H || water[z * w + x] == 0 ? -1 : tops[z * w + x],
                Up = up, Down = down, Inflow = inflow,
            };
            foreach (var t in seeds) s.Seeds.Add((t.x, t.y, t.z, t.ext));
            return s;
        }

        static List<Run> ProtoRuns(Func<RuleSet, RuleSet> proto, string label) => new()
        {
            new Run { Key = "h013", Label = "Hydrology 0.1.3", Rules = RuleSet.Hydrology013() },
            new Run { Key = "rec", Label = "Recommended", Rules = Sweep.Recommended() },
            new Run { Key = "proto", Label = "Recommended + " + label, Rules = proto(Sweep.Recommended().Clone(Sweep.RecommendedSpec + "+" + label)) },
        };

        static RuleSet WithLevels(RuleSet r) { r.ProtoLevels = true; r.ProtoBasinFill = true; return r; }

        static void Wall(Scene s, int x, int y0, int y1, int z0, int z1, bool place, int first, int each)
        {
            bool f = true;
            for (int y = place ? y0 : y1; place ? y <= y1 : y >= y0; y += place ? 1 : -1)
                for (int z = z0; z <= z1; z++) { s.Script.Add(new Edit { X = x, Y = y, Z = z, Place = place, WaitMs = f ? first : each }); f = false; }
        }

        static int SourcesAt(World w, int x0, int x1, int y, int z0, int z1)
        {
            int n = 0;
            for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) if (F.IsSource(w.Get(x, y, z))) n++;
            return n;
        }

        static Columns PondColumns() => Canyon(30, 14, 14, (1, 14, 8, 5), (15, 28, 7, 4));

        /// <summary>A dam across the river below a step; the pond should rise to the crest and spill over it.</summary>
        public static Scene PondDam()
        {
            var c = PondColumns();
            var s = new Scene
            {
                Name = "pond", Title = "Pond behind a dam",
                Description = "A river in a canyon, upper pool at 8, lower river at 7. A dam goes across at x 17 up to 10, then 100 s pass. The pond should rise to the crest and spill over it.",
                Generated = c.Place(18), SectionZ = 6, EarthShape = false,
                River = Stub(c, (3, 7, 6), (26, 6, 6), 30, (3, 7, 6, false), (26, 6, 6, false)),
                EX0 = 12, EX1 = 20, EY0 = 5, EY1 = 12, EZ0 = 3, EZ1 = 10,
                Runs = ProtoRuns(WithLevels, "managed bodies"),
            };
            Wall(s, 17, 5, 10, 4, 9, true, 2000, 100);
            s.Script.Add(new Edit { X = 0, Y = 17, Z = 0, Place = true, WaitMs = 100_000 }); // a wait (a stone in the sky corner)
            s.Checks.Add(("pond reaches the crest", (w, r) => SourcesAt(w, 15, 16, 10, 4, 9) > 0 ? null : "no water at the crest's level behind the dam"));
            s.Checks.Add(("water spills over the dam", (w, r) => Enumerable.Range(18, 4).Any(x => w.Get(x, 9, 6) != 0 || w.Get(x, 8, 6) != 0 && !F.IsSource(w.Get(x, 8, 6))) ? null : "nothing below the dam above the river"));
            s.Checks.Add(("no water standing in the air", (w, r) => Floating(w)));
            return s;
        }

        /// <summary>A dam whose middle is broken out once the pond is full: the pond should drain through the gap.</summary>
        public static Scene DamFailure()
        {
            var s = PondDam();
            s.Name = "dam-failure"; s.Title = "Dam failure";
            s.Description = "The pond scene; 60 s after the dam is built its middle (2 wide, bed to crest) is broken out, then 80 s pass. The pond should drain through the gap to the river's own level.";
            s.Script.RemoveAt(s.Script.Count - 1);
            s.Script.Add(new Edit { X = 0, Y = 17, Z = 0, Place = true, WaitMs = 60_000 });
            Wall(s, 17, 5, 10, 6, 7, false, 500, 100);
            s.Script.Add(new Edit { X = 0, Y = 17, Z = 1, Place = true, WaitMs = 80_000 });
            s.Checks.Clear();
            s.MidChecks.Add((37, "pond is full before the dam breaks", (w, r) => SourcesAt(w, 15, 16, 10, 4, 9) > 0 ? null : "no pond at the crest when the dam breaks"));
            s.Checks.Add(("pond drains to the river's level", (w, r) => SourcesAt(w, 1, 16, 9, 4, 9) + SourcesAt(w, 1, 16, 10, 4, 9) == 0 ? null : $"{SourcesAt(w, 1, 16, 9, 4, 9) + SourcesAt(w, 1, 16, 10, 4, 9)} pond sources above 8 left"));
            s.Checks.Add(("no water standing in the air", (w, r) => Floating(w)));
            return s;
        }

        /// <summary>
        /// Three pools (10, 9, 8). A channel at 10 is dug from the upper pool around the middle stretch to the lower river,
        /// then a dam closes the middle stretch's inflow: that stretch should dry; the lower river stays.
        /// </summary>
        public static Scene Diversion()
        {
            var c = Canyon(32, 14, 12, (1, 9, 10, 7), (10, 18, 9, 6), (19, 30, 8, 5));
            var s = new Scene
            {
                Name = "diversion", Title = "Diversion drying",
                Description = "Pools at 10, 9 and 8. A channel at 10 is dug through the canyon wall from the upper pool around the middle pool to the lower river; then a dam at x 10 closes the middle pool's inflow, and 100 s pass. The middle pool's own water (above the lower river's level) should dry; the rest stays.",
                Generated = c.Place(16), SectionZ = 6, EarthShape = false,
                River = Stub(c, (3, 9, 6), (28, 7, 6), 30, (3, 9, 6, false), (14, 8, 6, false), (26, 7, 6, false)),
                EX0 = 8, EX1 = 22, EY0 = 7, EY1 = 12, EZ0 = 1, EZ1 = 9,
                Runs = ProtoRuns(WithLevels, "managed bodies"),
            };
            bool first = true;
            void Dig(int x, int z) { for (int y = 12; y >= 10; y--) { s.Script.Add(new Edit { X = x, Y = y, Z = z, Place = false, WaitMs = first ? 2000 : 60 }); first = false; } }
            for (int x = 7; x <= 8; x++) Dig(x, 3);
            for (int x = 7; x <= 21; x++) { Dig(x, 1); Dig(x, 2); }
            for (int x = 20; x <= 21; x++) Dig(x, 3);
            Wall(s, 10, 7, 12, 4, 9, true, 10_000, 100);
            s.Script.Add(new Edit { X = 0, Y = 15, Z = 0, Place = true, WaitMs = 100_000 });
            s.Checks.Add(("middle stretch dries", (w, r) => SourcesAt(w, 11, 18, 9, 4, 9) == 0 ? null : $"{SourcesAt(w, 11, 18, 9, 4, 9)} middle sources left at 9"));
            s.Checks.Add(("lower river stays", (w, r) => SourcesAt(w, 22, 30, 8, 4, 9) == 54 ? null : $"{SourcesAt(w, 22, 30, 8, 4, 9)} of 54 lower sources"));
            s.Checks.Add(("upper pool stays", (w, r) => SourcesAt(w, 1, 9, 10, 4, 9) == 54 ? null : $"{SourcesAt(w, 1, 9, 10, 4, 9)} of 54 upper sources"));
            s.Checks.Add(("no water standing in the air", (w, r) => Floating(w)));
            return s;
        }

        /// <summary>A lock chamber (2 wide) beside a weir: it fills from the upper river and empties to the lower one.</summary>
        public static Scene Lock()
        {
            var c = new Columns(32, 14, 12);
            c.Pool(1, 10, 4, 9, 10, 6);
            c.Pool(11, 14, 7, 9, 10, 6);
            c.Pool(15, 30, 6, 9, 7, 4);
            c.Pool(16, 30, 4, 5, 7, 4);
            c.SettleSteps();
            for (int i = 0; i < c.W * c.H; i++) if (c.Water[i] == 0) c.Top[i] = Math.Max(c.Top[i], 12);
            var w = c.Place(16);
            for (int x = 12; x <= 14; x++)
                for (int z = 4; z <= 5; z++)
                {
                    for (int y = 5; y <= 12; y++) w.SetSolid(x, y, z, false);
                    for (int y = 5; y <= 7; y++) w.SetFluid(x, y, z, F.Make(F.Water, F.Still, 7));
                }
            var s = new Scene
            {
                Name = "lock", Title = "Lock beside a weir",
                Description = "Upper river at 10, lower at 7, a 3-block weir at x 15 (z 7–9). A 2-wide chamber (x 12–14, z 4–5) between gates at x 11 and x 15. The upper gate opens at 3 s and closes at 33 s; the lower gate opens at 37 s and closes at 67 s.",
                Generated = w, SectionZ = 4, EarthShape = false,
                River = Stub(c, (3, 9, 6), (28, 6, 7), 30, (3, 9, 6, false), (28, 6, 7, false)),
                EX0 = 10, EX1 = 16, EY0 = 5, EY1 = 11, EZ0 = 3, EZ1 = 6,
                Runs = ProtoRuns(WithLevels, "managed bodies"),
            };
            Wall(s, 11, 7, 10, 4, 5, false, 3000, 100);
            Wall(s, 11, 7, 10, 4, 5, true, 30_000, 100);
            Wall(s, 15, 5, 10, 4, 5, false, 3000, 100);
            Wall(s, 15, 5, 10, 4, 5, true, 30_000, 100);
            s.Script.Add(new Edit { X = 0, Y = 15, Z = 0, Place = true, WaitMs = 10_000 });
            s.MidChecks.Add((8, "chamber fills to the upper level", (ww, r) => SourcesAt(ww, 12, 14, 10, 4, 5) == 6 ? null : $"{SourcesAt(ww, 12, 14, 10, 4, 5)} of 6 chamber sources at 10 before the upper gate closes"));
            s.Checks.Add(("chamber ends at the lower level", (ww, r) => SourcesAt(ww, 12, 14, 7, 4, 5) == 6 && SourcesAt(ww, 12, 14, 8, 4, 5) == 0 ? null : $"{SourcesAt(ww, 12, 14, 8, 4, 5)} chamber sources above 7, {SourcesAt(ww, 12, 14, 7, 4, 5)} of 6 at 7"));
            s.Checks.Add(("rivers keep their levels", (ww, r) => PoolsKeptOutside(ww, w, 11, 15, 4, 5)));
            s.Checks.Add(("no water standing in the air", (ww, r) => Floating(ww)));
            s.Info = (ww, st) => "";
            return s;
        }

        static string PoolsKeptOutside(World w, World gen, int x0, int x1, int z0, int z1)
        {
            int lost = 0;
            for (int i = 0; i < w.Fluid.Length; i++)
            {
                w.Pos(i, out int x, out int y, out int z);
                if (x >= x0 && x <= x1 && z >= z0 && z <= z1) continue;
                if (F.IsSource(gen.Fluid[i]) && w.Solid[i] == 0 && !F.IsSource(w.Fluid[i])) lost++;
            }
            return lost == 0 ? null : $"{lost} river sources lost";
        }

        /// <summary>A river spilling over a low bank onto flat ground: how far does it run?</summary>
        public static Scene LongRun()
        {
            var w = new World(72, 6, 13);
            w.FillSolid(0, 0, 0, 71, 1, 12);                       // the plain: ground top at 1, water runs at 2
            w.FillSolid(0, 2, 0, 3, 3, 12);                        // the ledge, top at 3
            w.FillSolid(0, 2, 0, 71, 3, 0); w.FillSolid(0, 2, 12, 71, 3, 12);
            w.FillSolid(1, 3, 5, 2, 3, 7, false);                  // a pool on the ledge, water at 3
            for (int x = 1; x <= 2; x++) for (int z = 5; z <= 7; z++) w.SetFluid(x, 3, z, F.Make(F.Water, F.Still, 7));
            w.FillSolid(0, 3, 4, 0, 3, 8); w.FillSolid(1, 4, 4, 3, 4, 8, false);
            var s = new Scene
            {
                Name = "long-run", Title = "Long run over flat ground",
                Description = "A pool on a ledge, walled at its east side. The wall is broken at 2 s; the water spills one block down onto a plain 11 wide and 68 long and runs east as far as its levels allow.",
                Generated = w, SectionZ = 6, EarthShape = false,
                EX0 = 3, EX1 = 70, EY0 = 2, EY1 = 3, EZ0 = 1, EZ1 = 11,
                Runs = new()
                {
                    new Run { Key = "h013", Label = "Hydrology 0.1.3", Rules = RuleSet.Hydrology013() },
                    new Run { Key = "rec", Label = "Recommended", Rules = Sweep.Recommended() },
                    new Run { Key = "proto", Label = "Recommended + 8 sub-levels", Rules = SubLevels(Sweep.Recommended(), 8, true) },
                    new Run { Key = "proto-norelax", Label = "Recommended + 8 sub-levels, no level relax", Rules = SubLevels(Sweep.Recommended(), 8, false) },
                },
            };
            for (int z = 5; z <= 7; z++) s.Script.Add(new Edit { X = 3, Y = 3, Z = z, Place = false, WaitMs = z == 5 ? 2000 : 100 });
            s.Info = (ww, st) => { int reach = 0; for (int x = 4; x < ww.SX; x++) for (int z = 1; z < 12; z++) if (ww.Get(x, 2, z) != 0) reach = Math.Max(reach, x - 3); return $"water runs {reach} blocks over the plain"; };
            s.Checks.Add(("no water standing in the air", (ww, r) => Floating(ww)));
            return s;
        }

        static RuleSet SubLevels(RuleSet r, int k, bool relax)
        {
            var c = r.Clone(Sweep.RecommendedSpec + $"+ProtoSubLevels={k}" + (relax ? "" : ",LevelRelax=0"));
            c.ProtoSubLevels = k; c.LevelRelax = relax;
            return c;
        }

        /// <summary>The fall with a wheel in the curtain: power from drop height × discharge, read from the query stub.</summary>
        public static Scene Power()
        {
            var c = FallColumns(6);
            var s = new Scene
            {
                Name = "power", Title = "Power from drop × flow",
                Description = "The fall with a wheel cell in the curtain. A stone goes into the curtain at 3 s and out at 6 s. Power is read from the river query (drop and discharge), not from the water blocks.",
                Generated = c.Place(22, true), SectionZ = 6, EarthShape = false,
                River = Stub(c, (3, 15, 6), (26, 6, 6), 30, (3, 15, 6, false), (26, 6, 6, false)),
                EX0 = 10, EX1 = 18, EY0 = 8, EY1 = 18, EZ0 = 2, EZ1 = 11,
                Runs = ProtoRuns(WithLevels, "managed bodies"),
            };
            s.River.DischargeM3 = 1200;
            Poke(s, 13, 12, 6, 3000, 3000);
            s.Wheels.Add((13, 12, 6));
            s.Info = (w, st) =>
            {
                int up = st.NaturalTop(12, 6), down = st.NaturalTop(13, 6);
                int h = up - down;
                double mw = 1000 * 9.81 * st.DischargeM3 * h / 1e6;
                return $"drop {h} blocks (query: {up} to {down}), discharge {st.DischargeM3} m³/s: {mw:F0} MW at full scale; wheel cell holds {F.Name(w.Get(13, 12, 6))}";
            };
            s.Checks.Add(("wheel gets rapid water", (w, r) => WheelsRapid(w, s.Wheels)));
            return s;
        }
    }
}
