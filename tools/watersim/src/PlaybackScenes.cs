using System;
using System.Collections.Generic;

namespace WaterSim
{
    /// <summary>Synthetic cases for the playback (2026-10-23, second round): not part of the sweep's 12 scenes.</summary>
    public static partial class Scenes
    {
        /// <summary>The study's scenes, then the playback cases.</summary>
        public static List<Scene> Playback()
        {
            var list = All();
            list.Add(PartialBasin());
            list.Add(LakeRim());
            list.Add(ChuteShapes());
            list.Add(FallShapes());
            list.Add(Dam());
            list.Add(Bucket());
            list.AddRange(FeatureScenes());
            list.Add(DeepCanal());
            list.Add(LabDrop(2)); list.Add(LabDrop(3)); list.Add(LabDrop(5));
            list.Add(StepDiagonal());
            return list;
        }

        /// <summary>A canal 3 wide and 2 deep dug from a river only 1 deep at its bank (`watersim canals` has the matrix).</summary>
        public static Scene DeepCanal()
        {
            var s = Canals.Make(3, 2, shallowLake: true, outward: false);
            s.Name = "deep-canal";
            s.Title = "Deep canal from a shallow bank";
            s.Runs = new()
            {
                new Run { Key = "h013", Label = "Hydrology 0.1.3", Rules = RuleSet.Hydrology013() },
                new Run { Key = "rec", Label = "Recommended", Rules = Sweep.Recommended() },
                new Run { Key = "basin", Label = "Recommended + basin fill (prototype)", Rules = RuleSet.Parse(Sweep.RecommendedSpec + ",ProtoLevels=1,ProtoBasinFill=1") },
            };
            return s;
        }

        /// <summary>The server harness's lab_drop (tests/server): a 1-deep pool, a 2-wide channel dug outward from it, its
        /// end dropping <paramref name="h"/> blocks into a dry 2-wide channel below.</summary>
        public static Scene LabDrop(int h)
        {
            int ly = 12;
            var w = new World(34, 18, 12);
            w.FillSolid(0, 0, 0, 33, ly, 11);
            int ox = 9, oz = 4;
            for (int k = -8; k <= 0; k++) for (int ww = -3; ww <= 4; ww++) { w.SetSolid(ox + k, ly, oz + ww, false); w.SetFluid(ox + k, ly, oz + ww, F.Make(F.Water, F.Still, 7)); }
            for (int k = 7; k <= 20; k++) for (int ww = 0; ww <= 1; ww++) for (int y = ly - h; y <= ly; y++) w.SetSolid(ox + k, y, oz + ww, false);
            var s = new Scene { Name = $"lab-drop{h}", Title = $"Harness lab_drop {h}", Description = "tests/server lab_drop", Generated = w, SectionZ = oz, EarthShape = false,
                EX0 = ox, EX1 = ox + 20, EY0 = ly - h, EY1 = ly, EZ0 = oz, EZ1 = oz + 1 };
            for (int k = 1; k <= 6; k++) for (int ww = 0; ww <= 1; ww++) s.Script.Add(new Edit { X = ox + k, Y = ly, Z = oz + ww, Place = false, WaitMs = ww == 0 ? 1000 : 0 });
            return s;
        }

        /// <summary>A 1-block step on a river running diagonally (south-east), as Earth 0.16.8 draws it with sills.</summary>
        public static Scene StepDiagonal()
        {
            var c = new Columns(34, 34, 12);
            for (int z = 0; z < 34; z++)
                for (int x = 0; x < 34; x++)
                {
                    if (Math.Abs(x - z) > 3 || x < 1 || z < 1 || x > 32 || z > 32) continue;
                    bool upper = x + z <= 33;
                    c.Pool(x, x, z, z, upper ? 8 : 7, upper ? 5 : 4);
                    int k = c.K(x, z); c.FlowX[k] = 1; c.FlowZ[k] = 1;
                }
            for (int i = 0; i < c.W * c.H; i++) if (c.Water[i] == 0) c.Top[i] = 9;
            bool old = Columns.Sills; Columns.Sills = true;
            c.SettleSteps();
            Columns.Sills = old;
            return new Scene { Name = "step-diagonal", Title = "Diagonal 1-block step", Description = "Earth 0.16.8 step with a sill on a diagonal river",
                Generated = c.Place(14, true), SectionZ = 17, EarthShape = true, EX0 = 10, EX1 = 24, EY0 = 6, EY1 = 10, EZ0 = 10, EZ1 = 24 };
        }

        public static Scene ByNameAny(string name) => Playback().Find(s => s.Name == name);

        static List<Run> ShapeRuns() => new()
        {
            new Run { Key = "earth-rec", Label = "Earth 0.16.5 blocks, recommended rules", Rules = Sweep.Recommended() },
            new Run { Key = "shape-rec", Label = "Hydrology shape, recommended rules", Rules = Sweep.Recommended(), Alt = true },
            new Run { Key = "earth-013", Label = "Earth 0.16.5 blocks, Hydrology 0.1.3", Rules = RuleSet.Hydrology013() },
            new Run { Key = "shape-013", Label = "Hydrology shape, Hydrology 0.1.3", Rules = RuleSet.Hydrology013(), Alt = true },
        };

        static void Poke(Scene s, int x, int y, int z, int at, int hold)
        {
            s.Script.Add(new Edit { X = x, Y = y, Z = z, Place = true, WaitMs = at });
            s.Script.Add(new Edit { X = x, Y = y, Z = z, Place = false, WaitMs = hold });
        }

        /// <summary>
        /// A fall whose lip is wider than the plunge pool (seen at Niagara in a 1 M world): the lip spans 8 columns, the plunge
        /// pool only the first 4; under the rest the lower ground is dry at the pool's level. Earth places a curtain only over
        /// water, so there the lip's sources stand beside air until something wakes them.
        /// </summary>
        public static Scene PartialBasin()
        {
            var c = FallColumns(8);                     // lip and curtain z 4..11, plunge pool x 13..16
            for (int z = 8; z <= 11; z++)
                for (int x = 13; x < c.W; x++)
                {
                    int k = c.K(x, z);
                    c.Water[k] = 0; c.River[k] = false; c.Ramp[k] = 0; c.Fall[k] = 0; c.WaterTop[k] = -1;
                }
            c.SettleSteps();
            // Earth's large-scale layout leaves the dry part low (no bank raised under the lip)
            for (int z = 8; z <= 11; z++) for (int x = 13; x < c.W - 1; x++) c.Top[c.K(x, z)] = 8;
            for (int z = 12; z < c.H; z++) for (int x = 13; x < c.W - 1; x++) c.Top[c.K(x, z)] = 10;
            var s = new Scene
            {
                Name = "partial-basin", Title = "Lip over a partial basin",
                Description = "An 8-wide lip at 16 over a plunge pool only 4 wide; the other 4 columns of the drop land on dry ground at 8. One stone placed at the lip at 3 s and taken out again wakes it.",
                Generated = c.Place(22), SectionZ = 9,
                EX0 = 11, EX1 = 18, EY0 = 8, EY1 = 17, EZ0 = 3, EZ1 = 12,
            };
            Poke(s, 13, 16, 9, 3000, 1000);
            s.Checks.Add(("no water standing in the air", (w, r) => Floating(w)));
            s.Checks.Add(("lakes keep their level", (w, r) => PoolsKept(w, s.Generated)));
            return s;
        }

        /// <summary>
        /// Pool layout 4 (Earth 0.16.6, in progress, modelled from its description): a lake 11 above a river that runs beside
        /// it, held by a 2-block land rim at lake level + 1; the lake meets the river only at the drop (x 12..15), where it
        /// falls as a curtain into the river.
        /// </summary>
        public static Scene LakeRim()
        {
            const int W = 28, H = 20, river = 9, lake = 20;
            var c = new Columns(W, H, 11);
            c.Pool(1, 26, 2, 5, river, 6, rapid: true);
            c.Pool(12, 15, 6, 6, river, 6, rapid: true);          // the river widens under the drop
            c.Pool(2, 25, 9, 17, lake, 16, river: false);
            c.Pool(12, 15, 7, 8, lake, 18, river: false);         // the lake reaches the drop: a 2-deep lip
            for (int x = 0; x < W; x++)
                for (int z = 0; z < H; z++)
                {
                    int k = c.K(x, z);
                    if (c.Water[k] != 0) continue;
                    c.Top[k] = z <= 1 ? 11 : z == 6 ? river + 1 : z <= 8 ? lake + 1 : 22;   // bank, rim, hills
                    if (x == 0 || x == W - 1) c.Top[k] = 22;
                }
            c.SettleSteps();
            for (int x = 1; x < W - 1; x++) for (int z = 7; z <= 8; z++) if (c.Water[c.K(x, z)] == 0) c.Top[c.K(x, z)] = lake + 1;
            var s = new Scene
            {
                Name = "lake-rim", Title = "Lake rim beside a lower river",
                Description = "A lake at 20 beside a river at 9, held by a 2-block rim at 21; they meet only at the drop (x 12–15) as an 11-block curtain. Pokes at the curtain at 3 s and on the rim at 10 s.",
                Generated = c.Place(26), SectionZ = 13,
                EX0 = 4, EX1 = 23, EY0 = 9, EY1 = 22, EZ0 = 5, EZ1 = 9,
            };
            Poke(s, 13, 19, 6, 3000, 2000);
            Poke(s, 6, 22, 8, 5000, 2000);
            Poke(s, 20, 21, 7, 2000, 2000);
            s.Checks.Add(("rim never leaks, curtain only at the drop", (w, r) => RimDry(w, 12, 15)));
            s.Checks.Add(("no water standing in the air", (w, r) => Floating(w)));
            s.Checks.Add(("lakes keep their level", (w, r) => PoolsKept(w, s.Generated)));
            return s;
        }

        static string RimDry(World w, int dropX0, int dropX1)
        {
            int leak = 0, curtain = 0;
            for (int x = 0; x < w.SX; x++)
                for (int z = 6; z <= 8; z++)
                    for (int y = 11; y < w.SY; y++)
                    {
                        if (w.Get(x, y, z) == 0) continue;
                        if (x >= dropX0 && x <= dropX1) continue;
                        if (z == 6) curtain++; else leak++;
                    }
            return leak + curtain == 0 ? null : $"{leak} cells of water on the rim, {curtain} curtain cells outside the drop";
        }

        /// <summary>The chute as Earth 0.16.5 generates it, against the Hydrology shape; a stone in a ramp at 3 s, out at 6 s.</summary>
        public static Scene ChuteShapes()
        {
            var c = ChuteColumns();
            var s = new Scene
            {
                Name = "chute-shapes", Title = "Chute: Earth's blocks vs the Hydrology shape",
                Description = "The same chute generated twice: Earth 0.16.5's blocks (ordinary edge sources) and the Hydrology shape (rapid edge sources, spill cells). A stone goes into a ramp at 3 s and comes out at 6 s.",
                Generated = c.Place(16, false), AltGenerated = c.Place(16, true), AltLabel = "Hydrology shape",
                SectionZ = 6, Runs = ShapeRuns(),
                EX0 = 9, EX1 = 21, EY0 = 7, EY1 = 13, EZ0 = 2, EZ1 = 11,
            };
            Poke(s, 13, 11, 6, 3000, 3000);
            s.Checks.Add(("chute edges rapid", (w, r) => EdgesRapid(w, new[] { 11, 14, 17 }, new[] { 11, 10, 9 }, 5, 8)));
            return s;
        }

        /// <summary>The fall as Earth 0.16.5 generates it, against the Hydrology shape; a stone in the curtain at 3 s, out at 6 s.</summary>
        public static Scene FallShapes()
        {
            var c = FallColumns(6);
            var s = new Scene
            {
                Name = "fall-shapes", Title = "Fall: Earth's blocks vs the Hydrology shape",
                Description = "The same fall generated twice: Earth 0.16.5's blocks (ordinary lip, ordinary curtain) and the Hydrology shape (rapid lip and curtain). A stone goes into the curtain at 3 s and comes out at 6 s.",
                Generated = c.Place(22, false), AltGenerated = c.Place(22, true), AltLabel = "Hydrology shape",
                SectionZ = 6, Runs = ShapeRuns(),
                EX0 = 10, EX1 = 18, EY0 = 8, EY1 = 18, EZ0 = 2, EZ1 = 11,
            };
            Poke(s, 13, 12, 6, 3000, 3000);
            s.Checks.Add(("waterfall edge rapid", (w, r) => EdgesRapid(w, new[] { 12 }, new[] { 16 }, 5, 8)));
            s.Checks.Add(("curtain falls", (w, r) => CurtainFalls(w, 13, 10, 15, 4, 9)));
            return s;
        }

        /// <summary>A stone wall across the river just below a pool's step, built bottom up, left 25 s, taken down again.</summary>
        public static Scene Dam()
        {
            var st = Step();
            var s = new Scene
            {
                Name = "dam", Title = "Dam below a step",
                Description = "The 1-block step; a wall across the lower river at x 18, from the bed to 9, built from 2 s, left 25 s, then taken down top first.",
                Generated = st.Generated, SectionZ = 6,
                EX0 = 11, EX1 = 23, EY0 = 6, EY1 = 10, EZ0 = 2, EZ1 = 11,
            };
            bool first = true;
            for (int y = 5; y <= 9; y++)
                for (int z = 4; z <= 9; z++)
                {
                    s.Script.Add(new Edit { X = 18, Y = y, Z = z, Place = true, WaitMs = first ? 2000 : 120 });
                    first = false;
                }
            first = true;
            for (int y = 9; y >= 5; y--)
                for (int z = 4; z <= 9; z++)
                {
                    s.Script.Add(new Edit { X = 18, Y = y, Z = z, Place = false, WaitMs = first ? 25000 : 120 });
                    first = false;
                }
            s.Checks.Add(("lakes keep their level", (w, r) => PoolsKept(w, s.Generated)));
            s.Checks.Add(("back to the step", (w, r) => Same(w, s.Generated)));
            return s;
        }

        /// <summary>
        /// Buckets: on flat ground, four into a 2 x 2 pit, two into a dry 2-wide trench, and one into a hole beside a pond.
        /// With H6 a bucket spills water-still-3 (no source); vanilla's default world places a source.
        /// </summary>
        public static Scene Bucket()
        {
            var w = new World(20, 8, 16);
            w.FillSolid(0, 0, 0, 19, 3, 15);
            w.FillSolid(9, 3, 3, 10, 3, 4, false);                        // pit 2 x 2, floor at 2
            w.FillSolid(3, 3, 9, 12, 3, 10, false);                       // dry trench 2 wide
            w.FillSolid(14, 3, 9, 16, 3, 11, false);                      // pond 3 x 3
            for (int x = 14; x <= 16; x++) for (int z = 9; z <= 11; z++) w.SetFluid(x, 3, z, F.Make(F.Water, F.Still, 7));
            w.SetSolid(17, 3, 10, false);                                 // a hole beside the pond, walled from it
            w.SetSolid(17, 3, 9, true);
            w.SetSolid(17, 3, 11, true);
            var s = new Scene
            {
                Name = "bucket", Title = "Placing water with a bucket",
                Description = "Buckets poured: one on flat ground, four into a 2 x 2 pit, two into a dry 2-wide trench, one into a hole beside a pond. With H6 a bucket spills water-still-3; vanilla's default places a source.",
                Generated = w, SectionZ = 10, EarthShape = false,
                EX0 = 2, EX1 = 17, EY0 = 3, EY1 = 5, EZ0 = 2, EZ1 = 12,
            };
            void Pour(int x, int y, int z, int wait) => s.Script.Add(new Edit { X = x, Y = y, Z = z, Pour = true, WaitMs = wait });
            Pour(4, 4, 4, 2000);
            Pour(9, 3, 3, 4000); Pour(10, 3, 3, 400); Pour(9, 3, 4, 400); Pour(10, 3, 4, 400);
            Pour(5, 3, 9, 4000); Pour(5, 3, 10, 400);
            Pour(17, 3, 10, 4000);
            s.Checks.Add(("no source from nothing", (ww, r) => NoNewSources(ww, s.Generated, 17, 3, 10)));
            return s;
        }

        /// <summary>No sources beyond the generated ones, except the hole beside the pond (a fill between real sources).</summary>
        static string NoNewSources(World w, World gen, int okX, int okY, int okZ)
        {
            int extra = 0;
            for (int i = 0; i < w.Fluid.Length; i++)
            {
                if (!F.IsSource(w.Fluid[i]) || F.IsSource(gen.Fluid[i])) continue;
                w.Pos(i, out int x, out int y, out int z);
                if (x == okX && y == okY && z == okZ) continue;
                extra++;
            }
            return extra == 0 ? null : $"{extra} sources made from bucket water";
        }
    }
}
