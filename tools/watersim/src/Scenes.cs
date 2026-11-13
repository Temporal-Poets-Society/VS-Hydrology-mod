using System;
using System.Collections.Generic;

namespace WaterSim
{
    /// <summary>A planned edit: a player places (true) or breaks (false) a block, after waiting <see cref="WaitMs"/>.</summary>
    public struct Edit
    {
        public int X, Y, Z;
        /// <summary>Place a stone (true) or break the block (false); ignored when <see cref="Pour"/>.</summary>
        public bool Place;
        /// <summary>Empty a bucket of water into the cell.</summary>
        public bool Pour;
        public int WaitMs;
        public string Verb => Pour ? "pour" : Place ? "place" : "break";
        public override string ToString() => $"{Verb} {X},{Y},{Z} after {WaitMs} ms";

        public void Apply(Sim sim)
        {
            if (Pour) sim.Pour(X, Y, Z);
            else if (Place) sim.PlaceStone(X, Y, Z);
            else sim.BreakBlock(X, Y, Z);
        }
    }

    /// <summary>One column of the playback: a rule set on Earth's blocks or on the alternative blocks of the scene.</summary>
    public sealed class Run
    {
        public string Key, Label;
        public RuleSet Rules;
        public bool Alt;
    }

    /// <summary>A function check on a settled world: returns a failure text, or null when it holds.</summary>
    public delegate string Check(World w, World reference);

    public sealed class Scene
    {
        public string Name, Title, Description;
        public World Generated;
        /// <summary>Cells a player may edit in the random sequences (place/break), as a box.</summary>
        public int EX0, EY0, EZ0, EX1, EY1, EZ1;
        /// <summary>Scripted edits applied before the checks (dig a trench, the Victoria attack); none for a plain scene.</summary>
        public List<Edit> Script = new();
        /// <summary>Checks after the script, on the settled world.</summary>
        public List<(string name, Check check)> Checks = new();
        /// <summary>Generated water the poke test (invariant b) is about: true for Earth's shapes, false for the sandbox.</summary>
        public bool EarthShape = true;
        /// <summary>Where a vanilla water wheel would sit (rapid non-still water wanted), if any.</summary>
        public List<(int x, int y, int z)> Wheels = new();
        /// <summary>Columns shown in the page's close-up (x, z) and the vertical section row.</summary>
        public int SectionZ;
        public bool Salt;
        /// <summary>A second set of generated blocks to compare with (the Hydrology shape of a drop), for <see cref="Runs"/>.</summary>
        public World AltGenerated;
        public string AltLabel;
        /// <summary>The playback's columns; null for the usual three (0.1.3, recommended, vanilla on Earth's blocks).</summary>
        public List<Run> Runs;
        /// <summary>The river flow query's answers for this scene (Earth 0022 part A, as a stub); null if none.</summary>
        public RiverStub River;
        /// <summary>A text the feature runs print (the power read-out).</summary>
        public Func<World, RiverStub, string> Info;
        /// <summary>Checks on the world just before an edit (by its index in the script): states the end cannot show.</summary>
        public List<(int edit, string name, Check check)> MidChecks = new();
    }

    /// <summary>
    /// Earth 0.16.5's river columns (pool layout 3), reduced to what places blocks: per column the ground top, the water top
    /// and kind, and the ramp/curtain record. <see cref="SettleSteps"/> and <see cref="Place"/> follow
    /// TerrainModel.SettleSteps and EarthWorldGen.PlaceRamps: ramps of flowing water 6..1 on the lower pool, a curtain of
    /// water-d-6 from the lower pool's surface up to the upper water's level with a flowing (rapid where marked) top cell,
    /// banks as high as the water beside them (one more beside a ramp, the curtain's top beside a curtain).
    /// </summary>
    public sealed class Columns
    {
        public readonly int W, H;
        public readonly int[] Top, WaterTop, Ramp, Fall, FlowX, FlowZ;
        public readonly byte[] Water; // 0 none, 1 sea (salt), 2 lake / river
        public readonly bool[] River, Rapid;

        public Columns(int w, int h, int ground)
        {
            W = w; H = h;
            int n = w * h;
            Top = new int[n]; WaterTop = new int[n]; Ramp = new int[n]; Fall = new int[n]; FlowX = new int[n]; FlowZ = new int[n];
            Water = new byte[n]; River = new bool[n]; Rapid = new bool[n];
            for (int i = 0; i < n; i++) { Top[i] = ground; WaterTop[i] = -1; }
        }

        public int K(int x, int z) => z * W + x;

        public void Pool(int x0, int x1, int z0, int z1, int waterTop, int top, bool river = true, bool rapid = false, byte water = 2)
        {
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int k = K(x, z);
                    Water[k] = water; WaterTop[k] = waterTop; Top[k] = top; River[k] = river; Rapid[k] = rapid;
                    FlowX[k] = 1; FlowZ[k] = 0;
                }
        }

        public void Ground(int x0, int x1, int z0, int z1, int top)
        {
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) Top[K(x, z)] = top;
        }

        /// <summary>
        /// Experiment (2026-10-23, the canal question): give each 1-block step a sill, so the upper pool's edge column holds
        /// water only at the top (its bed at the lower pool's surface) and the two pools no longer touch below it. Set by
        /// the environment variable WATERSIM_SILLS=1.
        /// </summary>
        public static bool Sills = System.Environment.GetEnvironmentVariable("WATERSIM_SILLS") == "1";

        /// <summary>TerrainModel.SettleSteps(curtainBanks: true), as of Earth 0.16.5.</summary>
        public void SettleSteps()
        {
            if (Sills)
                for (int k = 0; k < W * H; k++)
                {
                    if (Water[k] == 0) continue;
                    int x = k % W, z = k / W;
                    foreach (int d in new[] { x > 0 ? k - 1 : -1, x < W - 1 ? k + 1 : -1, z > 0 ? k - W : -1, z < H - 1 ? k + W : -1 })
                        if (d >= 0 && Water[d] != 0 && WaterTop[d] == WaterTop[k] - 1) Top[k] = Math.Max(Top[k], WaterTop[d]);
                }
            int n = W * H;
            var level = new int[n];
            var from = new int[n];
            var fall = new int[n];
            IEnumerable<int> Nb(int k)
            {
                int x = k % W, z = k / W;
                if (x > 0) yield return k - 1;
                if (x < W - 1) yield return k + 1;
                if (z > 0) yield return k - W;
                if (z < H - 1) yield return k + W;
            }
            for (int k = 0; k < n; k++)
            {
                if (Water[k] != 2 || WaterTop[k] < 0) continue;
                foreach (int d in Nb(k))
                {
                    if (Water[d] == 0 || WaterTop[d] <= WaterTop[k]) continue;
                    if (WaterTop[d] - WaterTop[k] >= 2)
                    {
                        fall[k] = Math.Max(fall[k], WaterTop[d] - WaterTop[k]);
                        level[k] = 6; from[k] = d;
                        continue;
                    }
                    if (Top[d] > WaterTop[k]) continue;
                    level[k] = 6; from[k] = d;
                }
            }
            for (int l = 6; l > 1; l--)
                for (int k = 0; k < n; k++)
                {
                    if (level[k] != l) continue;
                    foreach (int m in Nb(k))
                    {
                        if (Water[m] != 2 || WaterTop[m] != WaterTop[k] || level[m] >= l - 1) continue;
                        level[m] = l - 1; from[m] = k;
                    }
                }
            for (int k = 0; k < n; k++)
            {
                if (fall[k] > 0) Fall[k] = fall[k];
                if (level[k] <= Ramp[k]) continue;
                Ramp[k] = level[k];
                if (!River[k] || (FlowX[k] == 0 && FlowZ[k] == 0)) { FlowX[k] = k % W - from[k] % W; FlowZ[k] = k / W - from[k] / W; }
            }
            for (int k = 0; k < n; k++)
            {
                if (Water[k] != 0) continue;
                int need = int.MinValue;
                foreach (int d in Nb(k))
                {
                    if (Water[d] == 0 || (!River[d] && Ramp[d] == 0)) continue;
                    need = Math.Max(need, WaterTop[d] + Math.Max(Ramp[d] > 0 ? 1 : 0, Fall[d]));
                }
                if (need > Top[k]) Top[k] = need;
            }
        }

        /// <summary>
        /// Draw drops in the shape the recommended rules settle to (study 2026-10-23) instead of Earth 0.16.5's: set by
        /// `watersim verify-shape`, read by <see cref="Place"/>.
        /// </summary>
        public static bool HydrologyShape;

        /// <summary>The lowest ramp height that spills over the next step (Earth 0.16.8: 3; the rapid-rule check: 2).</summary>
        public static int SpillFrom = 3;

        /// <summary>The blocks: ground, source water, then EarthWorldGen.PlaceRamps (and the Hydrology shape when asked).</summary>
        public World Place(int sy) => Place(sy, HydrologyShape);

        public World Place(int sy, bool hydrologyShape)
        {
            var w = new World(W, sy, H);
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    int k = K(x, z);
                    for (int y = 0; y <= Top[k] && y < sy; y++) w.SetSolid(x, y, z, true);
                    if (Water[k] == 0) continue;
                    int src = F.Make(Water[k] == 1 ? F.Salt : F.Water, F.Still, 7);
                    for (int y = Top[k] + 1; y <= WaterTop[k]; y++) w.SetFluid(x, y, z, src);
                }
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    int k = K(x, z);
                    if (Ramp[k] == 0 && Fall[k] == 0) continue;
                    int h = Math.Max(Ramp[k], 1);
                    if (h < 1 || h > 6 || Water[k] != 2) continue;
                    int y0 = WaterTop[k] + 1;
                    // only on open water: the pool top below is (ordinary) water
                    if (w.Get(x, y0 - 1, z) != F.Make(F.Water, F.Still, 7)) continue;
                    int dir = FlowDir(FlowX[k], FlowZ[k]);
                    int yTop = Fall[k] >= 2 ? y0 + Fall[k] - 1 : y0;
                    for (int y = y0; y <= yTop && y < sy - 1; y++)
                    {
                        if (w.IsSolid(x, y, z) || w.Get(x, y, z) != 0) break;
                        int id = y < yTop ? F.Make(F.Water, F.D, 6) : F.Make(Rapid[k] ? F.Rapid : F.Water, dir, yTop > y0 ? 6 : h);
                        w.SetFluid(x, y, z, id);
                    }
                }
            if (hydrologyShape) ShapeDrops(w, sy);
            return w;
        }

        /// <summary>
        /// The drops as the recommended rules leave them, so that the first update changes nothing:
        /// - every upper-pool source beside the lower column's ramp or curtain (the step edge; both layers of a 2-deep lip)
        ///   is a source labelled with the flow direction, rapid where the lower pool is rapid;
        /// - a curtain is rapid falling water from one above the lower pool up to the upper water's level, top cell included,
        ///   and its bottom cell, on the plunge pool, is rapid flowing water of height 6 in the flow direction;
        /// - a rapid ramp cell of height h of 2 or more beside a column whose water is one lower (the next chute step)
        ///   spills: falling water of height h - 1 above that column's ramp.
        /// </summary>
        void ShapeDrops(World w, int sy)
        {
            int[] dx = { 0, 1, 0, -1 }, dz = { -1, 0, 1, 0 };
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    int k = K(x, z);
                    if (Water[k] != 2 || (Ramp[k] == 0 && Fall[k] == 0)) continue;
                    int y0 = WaterTop[k] + 1;
                    int kind = Rapid[k] ? F.Rapid : F.Water;
                    int dir = FlowDir(FlowX[k], FlowZ[k]);
                    if (F.Level(w.Get(x, y0, z)) == 0 || F.IsSource(w.Get(x, y0, z))) continue;
                    if (Fall[k] >= 2)
                    {
                        int yTop = y0 + Fall[k] - 1;
                        for (int y = y0; y <= yTop && y < sy - 1; y++)
                            if (!w.IsSolid(x, y, z)) w.SetFluid(x, y, z, y == y0 ? F.Make(kind, dir, 6) : F.Make(kind, F.D, 6));
                    }
                    for (int h = 0; h < 4; h++)
                    {
                        int ux = x + dx[h], uz = z + dz[h];
                        if (ux < 0 || uz < 0 || ux >= W || uz >= H) continue;
                        int d = K(ux, uz);
                        if (Water[d] == 0 || WaterTop[d] <= WaterTop[k]) continue;
                        // the upper pool's sources beside this column's flowing water: the edge
                        int edgeDir = FlowDir(x - ux, z - uz);
                        for (int y = y0; y <= WaterTop[d]; y++)
                        {
                            int up = w.Get(ux, y, uz), here = w.Get(x, y, z);
                            if (!F.IsSource(up) || here == 0 || F.IsSource(here)) continue;
                            w.SetFluid(ux, y, uz, F.Make(Rapid[k] ? F.Rapid : F.Water, edgeDir, 7));
                        }
                    }
                }
            // spill cells at chute steps
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    int k = K(x, z);
                    if (Water[k] != 2 || !Rapid[k] || Ramp[k] < SpillFrom || Fall[k] > 0) continue;
                    int y0 = WaterTop[k] + 1;
                    int c = w.Get(x, y0, z);
                    if (c == 0 || F.IsSource(c)) continue;
                    for (int h = 0; h < 4; h++)
                    {
                        int nx = x + dx[h], nz = z + dz[h];
                        if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                        int m = K(nx, nz);
                        if (Water[m] != 2 || WaterTop[m] != WaterTop[k] - 1 || Ramp[m] == 0) continue;
                        if (w.IsSolid(nx, y0, nz) || w.Get(nx, y0, nz) != 0) continue;
                        w.SetFluid(nx, y0, nz, F.Make(F.Rapid, F.D, F.Level(c) - 1));
                    }
                }
        }

        /// <summary>EarthWorldGen.FlowDirIndex mapped to the flow states (0 = north, clockwise).</summary>
        static int FlowDir(int fx, int fz)
        {
            double a = Math.Atan2(fx, -fz);
            int i = (int)Math.Round(a / (Math.PI / 4));
            return ((i % 8) + 8) % 8 + 1;
        }
    }

    public static partial class Scenes
    {
        public static List<Scene> All() => new()
        {
            Step(), Chute(), Fall(), Trench(2, false, false), Trench(2, false, true), Trench(2, true, false), Trench(2, true, true),
            Trench(1, false, false), Trench(1, true, false), Channel(), Victoria(), Sandbox(),
        };

        public static Scene ByName(string name) => All().Find(s => s.Name == name);

        static void RiverBanks(Columns c, int z0, int z1, int land)
        {
            for (int z = 0; z < c.H; z++)
                for (int x = 0; x < c.W; x++)
                {
                    if (c.Water[c.K(x, z)] != 0) continue;
                    int d = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
                    c.Top[c.K(x, z)] = land + Math.Max(0, d - 1);
                }
        }

        /// <summary>A lake with a 1-block river step and the 6..1 ramp on the lower pool.</summary>
        public static Scene Step()
        {
            var c = new Columns(30, 14, 4);
            c.Pool(1, 14, 4, 9, 8, 5);
            c.Pool(15, 28, 4, 9, 7, 4);
            RiverBanks(c, 4, 9, 0);
            // banks flush with the water (ApplyRiver's bankTop), the valley rising beyond
            for (int x = 0; x < 30; x++)
                for (int z = 0; z < 14; z++)
                {
                    int k = c.K(x, z);
                    if (c.Water[k] != 0) continue;
                    int wt = x <= 14 ? 8 : 7;
                    int d = z < 4 ? 4 - z : z > 9 ? z - 9 : 0;
                    c.Top[k] = wt + Math.Max(0, d - 1);
                    if (x == 0 || x == 29) c.Top[k] = Math.Max(c.Top[k], 9);
                }
            c.SettleSteps();
            var s = new Scene
            {
                Name = "step", Title = "Lake with a 1-block step",
                Description = "Upper pool at 8, lower pool at 7, Earth's 6..1 ramp of flowing water on the lower pool.",
                Generated = c.Place(14), SectionZ = 6,
                EX0 = 11, EX1 = 23, EY0 = 6, EY1 = 10, EZ0 = 2, EZ1 = 11,
            };
            s.Checks.Add(("lakes keep their level", (w, r) => PoolsKept(w, s.Generated)));
            return s;
        }

        /// <summary>A chute: three 1-block steps 3 apart, rapid ramps, then the lower pool.</summary>
        internal static Columns ChuteColumns()
        {
            var c = new Columns(32, 14, 4);
            c.Pool(1, 8, 4, 9, 11, 8);
            c.Pool(9, 11, 4, 9, 11, 9);                 // shoal before the chute: 2 deep
            c.Pool(12, 14, 4, 9, 10, 8, rapid: true);   // chute: 2 deep, 1 at the edges
            c.Pool(15, 17, 4, 9, 9, 7, rapid: true);
            c.Pool(18, 21, 4, 9, 8, 6, rapid: true);
            c.Pool(22, 30, 4, 9, 8, 5, rapid: true);
            for (int x = 12; x <= 17; x++) { c.Top[c.K(x, 4)]++; c.Top[c.K(x, 9)]++; }
            for (int x = 0; x < 32; x++)
                for (int z = 0; z < 14; z++)
                {
                    int k = c.K(x, z);
                    if (c.Water[k] != 0) continue;
                    int wt = c.WaterTop[c.K(Math.Clamp(x, 1, 30), 6)];
                    int d = z < 4 ? 4 - z : z > 9 ? z - 9 : 0;
                    c.Top[k] = wt + Math.Max(0, d - 1);
                    if (x == 0 || x == 31) c.Top[k] = Math.Max(c.Top[k], 12);
                }
            c.SettleSteps();
            return c;
        }

        public static Scene Chute()
        {
            var c = ChuteColumns();
            var s = new Scene
            {
                Name = "chute", Title = "Chute: three 1-block steps 3 apart",
                Description = "Earth's chute for a 3-block drop (pool layout 3): 1-block steps 3 apart, 2 deep (1 at the edges), rapid ramps.",
                Generated = c.Place(16), SectionZ = 6,
                EX0 = 9, EX1 = 21, EY0 = 7, EY1 = 13, EZ0 = 2, EZ1 = 11,
            };
            s.Wheels.Add((13, 11, 6));
            s.Wheels.Add((16, 10, 6));
            s.Checks.Add(("chute edges rapid", (w, r) => EdgesRapid(w, new[] { 11, 14, 17 }, new[] { 11, 10, 9 }, 5, 8)));
            s.Checks.Add(("lakes keep their level", (w, r) => PoolsKept(w, s.Generated)));
            s.Checks.Add(("wheel gets rapid water", (w, r) => WheelsRapid(w, s.Wheels)));
            return s;
        }

        /// <summary>An 8-block waterfall with a 2-deep lip, a curtain and a plunge pool.</summary>
        public static Scene Fall()
        {
            var c = FallColumns(6);
            var s = new Scene
            {
                Name = "fall", Title = "8-block waterfall, 2-deep lip, plunge pool",
                Description = "Upper water at 16 (2 deep at the lip), curtain of water-d-6 with a rapid flowing top cell, plunge pool at 8.",
                Generated = c.Place(22), SectionZ = 6,
                EX0 = 10, EX1 = 18, EY0 = 8, EY1 = 18, EZ0 = 2, EZ1 = 11,
            };
            s.Wheels.Add((13, 12, 6));
            s.Checks.Add(("waterfall edge rapid", (w, r) => EdgesRapid(w, new[] { 12 }, new[] { 16 }, 5, 8)));
            s.Checks.Add(("lakes keep their level", (w, r) => PoolsKept(w, s.Generated)));
            s.Checks.Add(("wheel gets rapid water", (w, r) => WheelsRapid(w, s.Wheels)));
            s.Checks.Add(("curtain falls", (w, r) => CurtainFalls(w, 13, 10, 15, 4, 9)));
            return s;
        }

        internal static Columns FallColumns(int width)
        {
            int z0 = 4, z1 = 4 + width - 1, H = z1 + 5;
            var c = new Columns(30, H, 2);
            c.Pool(1, 9, z0, z1, 16, 13);
            c.Pool(10, 12, z0, z1, 16, 14);              // the lip: 2 deep
            c.Pool(13, 16, z0, z1, 8, 3, rapid: true);   // plunge pool
            c.Pool(17, 28, z0, z1, 8, 5, rapid: true);
            for (int x = 0; x < 30; x++)
                for (int z = 0; z < H; z++)
                {
                    int k = c.K(x, z);
                    if (c.Water[k] != 0) continue;
                    int wt = x <= 12 ? 16 : 8;
                    int d = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
                    c.Top[k] = wt + Math.Max(0, d - 1);
                    if (x == 0 || x == 29) c.Top[k] = Math.Max(c.Top[k], wt + 1);
                }
            c.SettleSteps();
            return c;
        }

        /// <summary>The Victoria attack: stones put into the curtain at the lip and taken out, three times.</summary>
        public static Scene Victoria()
        {
            var c = FallColumns(8);
            var s = new Scene
            {
                Name = "victoria", Title = "Victoria attack",
                Description = "A wider fall (8). Stones go into the curtain at the lip, one and four below it (3 wide), and on a ledge beside it; out again 20 s later; three rounds.",
                Generated = c.Place(22), SectionZ = 7,
                EX0 = 11, EX1 = 15, EY0 = 9, EY1 = 17, EZ0 = 3, EZ1 = 12,
            };
            for (int round = 0; round < 3; round++)
            {
                bool first = true;
                foreach (int y in new[] { 16, 15, 12 })
                    for (int z = 6; z <= 8; z++)
                    {
                        s.Script.Add(new Edit { X = 13, Y = y, Z = z, Place = true, WaitMs = first ? 20_000 : 250 });
                        first = false;
                    }
                for (int z = 6; z <= 8; z++) s.Script.Add(new Edit { X = 14, Y = 13, Z = z, Place = true, WaitMs = 250 });
                first = true;
                foreach (int y in new[] { 16, 15, 12 })
                    for (int z = 6; z <= 8; z++)
                    {
                        s.Script.Add(new Edit { X = 13, Y = y, Z = z, Place = false, WaitMs = first ? 20_000 : 250 });
                        first = false;
                    }
                for (int z = 6; z <= 8; z++) s.Script.Add(new Edit { X = 14, Y = 13, Z = z, Place = false, WaitMs = 250 });
            }
            s.Wheels.Add((13, 12, 7));
            s.Checks.Add(("no water standing in the air", (w, r) => Floating(w)));
            s.Checks.Add(("back to the settled fall", (w, r) => Same(w, r)));
            s.Checks.Add(("curtain falls", (w, r) => CurtainFalls(w, 13, 10, 15, 4, 11)));
            return s;
        }

        /// <summary>A trench from a lake (or the sea), 2 or 1 wide, dug dry and opened last, or dug outward from the water.</summary>
        public static Scene Trench(int width, bool outward, bool salt)
        {
            var c = new Columns(26, 16, 9);
            c.Pool(1, 10, 1, 14, 8, 5, river: false, water: (byte)(salt ? 1 : 2));
            c.Ground(0, 0, 0, 15, 10);
            c.Ground(0, 25, 0, 0, 10);
            c.Ground(0, 25, 15, 15, 10);
            c.SettleSteps();
            string name = $"trench{width}{(outward ? "-outward" : "-opened")}{(salt ? "-salt" : "")}";
            var s = new Scene
            {
                Name = name,
                Title = $"{width}-wide trench from {(salt ? "the sea" : "a lake")}, {(outward ? "dug outward" : "dug dry, opened last")}",
                Description = $"Lake at 8, land at 9; a trench {width} wide, floor at 7, 11 long.",
                Generated = c.Place(12), SectionZ = 7, Salt = salt,
                EX0 = 9, EX1 = 22, EY0 = 6, EY1 = 10, EZ0 = 5, EZ1 = 10,
            };
            var cells = new List<(int x, int z)>();
            for (int x = 11; x <= 21; x++) for (int z = 7; z < 7 + width; z++) cells.Add((x, z));
            var order = new List<(int x, int z)>(cells);
            if (!outward) { order.RemoveAll(p => p.x == 11); order.AddRange(cells.FindAll(p => p.x == 11)); }
            bool firstOpen = true;
            foreach (var (x, z) in order)
            {
                bool opens = x == 11 && !outward;
                int wait = outward ? 600 : opens && firstOpen ? 5_000 : 150;
                if (opens) firstOpen = false;
                s.Script.Add(new Edit { X = x, Y = 9, Z = z, Place = false, WaitMs = wait });
                s.Script.Add(new Edit { X = x, Y = 8, Z = z, Place = false, WaitMs = 150 });
            }
            if (width >= 2) s.Checks.Add(("2-wide trench fills", (w, r) => TrenchFull(w, cells, true)));
            else s.Checks.Add(("1-wide trench stays flowing", (w, r) => TrenchFull(w, cells, false)));
            s.Checks.Add(("lakes keep their level", (w, r) => PoolsKept(w, s.Generated)));
            return s;
        }

        /// <summary>A 2-wide channel dug through the bank around a 1-block step, from the upper pool to the lower one.</summary>
        public static Scene Channel()
        {
            var st = Step();
            var s = new Scene
            {
                Name = "channel", Title = "Channel dug around a step",
                Description = "The step scene with a 2-wide channel at the upper level through the bank, from beside the upper pool to beside the ramp.",
                Generated = st.Generated, SectionZ = 3,
                EX0 = 9, EX1 = 21, EY0 = 6, EY1 = 10, EZ0 = 1, EZ1 = 5,
            };
            for (int x = 10; x <= 19; x++)
                for (int z = 2; z <= 3; z++)
                    for (int y = 10; y >= 8; y--)
                        if (s.Generated.IsSolid(x, y, z)) s.Script.Add(new Edit { X = x, Y = y, Z = z, Place = false, WaitMs = 300 });
            s.Checks.Add(("lakes keep their level", (w, r) => PoolsKept(w, s.Generated)));
            return s;
        }

        /// <summary>Water that should stay: a tank, a 1 x 1 shaft and a canal between walls.</summary>
        public static Scene Sandbox()
        {
            var w = new World(18, 11, 16);
            w.FillSolid(0, 0, 0, 17, 2, 15);
            // tank 4 x 4, water 3..6, walls to 7
            w.FillSolid(1, 3, 1, 6, 7, 6);
            w.FillSolid(2, 3, 2, 5, 7, 5, false);
            for (int y = 3; y <= 6; y++) for (int z = 2; z <= 5; z++) for (int x = 2; x <= 5; x++) w.SetFluid(x, y, z, F.Make(F.Water, F.Still, 7));
            // shaft 1 x 1, water 3..7, walls to 8
            w.FillSolid(9, 3, 1, 11, 8, 3);
            w.SetSolid(10, 3, 2, false);
            for (int y = 3; y <= 8; y++) w.SetSolid(10, y, 2, false);
            for (int y = 3; y <= 7; y++) w.SetFluid(10, y, 2, F.Make(F.Water, F.Still, 7));
            // canal 2 wide, 10 long, water 3..4, walls to 5
            w.FillSolid(1, 3, 9, 14, 5, 12);
            for (int x = 2; x <= 13; x++) for (int z = 10; z <= 11; z++) for (int y = 3; y <= 5; y++) w.SetSolid(x, y, z, false);
            for (int x = 2; x <= 13; x++) for (int z = 10; z <= 11; z++) for (int y = 3; y <= 4; y++) w.SetFluid(x, y, z, F.Make(F.Water, F.Still, 7));
            var s = new Scene
            {
                Name = "sandbox", Title = "Sandbox: tank, shaft, canal",
                Description = "Water placed as sources where it should stay: a 4 x 4 tank, a 1 x 1 shaft 5 deep, a 2-wide canal between walls.",
                Generated = w, SectionZ = 2, EarthShape = false,
                EX0 = 1, EX1 = 14, EY0 = 3, EY1 = 8, EZ0 = 1, EZ1 = 12,
            };
            s.Checks.Add(("water stays", (ww, r) => PoolsKept(ww, s.Generated)));
            return s;
        }

        // ---------------------------------------------------------------- checks

        /// <summary>Every source of the generated world is still a source (pools keep their level).</summary>
        public static string PoolsKept(World w, World gen)
        {
            int lost = 0;
            for (int i = 0; i < w.Fluid.Length; i++)
                if (F.IsSource(gen.Fluid[i]) && w.Solid[i] == 0 && !F.IsSource(w.Fluid[i])) lost++;
            return lost == 0 ? null : $"{lost} pool sources lost";
        }

        public static string TrenchFull(World w, List<(int x, int z)> cells, bool full)
        {
            int src = 0, wet = 0;
            foreach (var (x, z) in cells)
            {
                int c = w.Get(x, 8, z);
                if (c != 0) wet++;
                if (F.IsSource(c)) src++;
            }
            if (full) return src == cells.Count ? null : $"{src} of {cells.Count} trench cells are sources";
            return src == 0 && wet > 0 ? null : $"{src} sources, {wet} wet of {cells.Count}";
        }

        public static string EdgesRapid(World w, int[] xs, int[] ys, int z0, int z1)
        {
            int n = 0, rapid = 0;
            for (int i = 0; i < xs.Length; i++)
                for (int z = z0; z <= z1; z++)
                {
                    int c = w.Get(xs[i], ys[i], z);
                    if (!F.IsSource(c)) continue;
                    n++;
                    if (F.IsRapid(c)) rapid++;
                }
            return n > 0 && rapid == n ? null : $"{rapid} of {n} edge sources rapid";
        }

        public static string WheelsRapid(World w, List<(int x, int y, int z)> wheels)
        {
            foreach (var (x, y, z) in wheels)
            {
                int c = w.Get(x, y, z);
                if (!F.IsRapid(c) || F.Flow(c) == F.Still) return $"wheel at {x},{y},{z}: {F.Name(c)}";
            }
            return null;
        }

        public static string CurtainFalls(World w, int x, int y0, int y1, int z0, int z1)
        {
            int bad = 0;
            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                {
                    int c = w.Get(x, y, z);
                    if (c == 0 || F.Flow(c) != F.D) bad++;
                }
            return bad == 0 ? null : $"{bad} curtain cells not falling";
        }

        public static string Same(World w, World reference)
        {
            int d = w.CountWaterDiff(reference);
            return d == 0 ? null : $"{d} cells differ from the settled fall";
        }

        public static string Floating(World w)
        {
            var f = Invariants.Floating(w);
            return f.Total == 0 ? null : f.ToString();
        }
    }
}
