using System;
using Hydrology;
using Hydrology.Rules;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using WaterSim;

namespace Issue20Tests
{
    public static class Suite
    {
        static int checks, failures;
        static void Check(bool ok, string name)
        {
            checks++;
            if (!ok) { failures++; Console.WriteLine("FAIL " + name); }
        }

        public static int Main()
        {
            WorldOptions();
            foreach (string bed in new[] { "hump", "pit", "stairs" })
            foreach (int kind in new[] { F.Salt, F.Water })
            foreach (int width in new[] { 2, 6 })
            foreach (bool mass in new[] { true, false })
                Canal(bed, kind, width, mass, query: true, expectFull: true);
            // Salt water also works without Earth; fresh water without a geography query keeps the old safeguards.
            Canal("pit", F.Salt, 6, true, query: false, expectFull: true);
            Canal("pit", F.Water, 6, true, query: false, expectFull: false);
            Canal("pit", F.Water, 6, true, query: true, expectFull: false, river: true);
            foreach (int kind in new[] { F.Salt, F.Water })
            {
                Canal("pit", kind, 1, true, query: true, expectFull: false);
                Canal("pit", kind, 6, true, query: true, expectFull: false, vessels: false);
                Canal("pit", kind, 6, true, query: true, expectFull: false, fill: false);
                Canal("pit", kind, 6, true, query: true, expectFull: true, vessels: false, flooding: true);
                Canal("pit", kind, 6, true, query: true, expectFull: false, flooding: false);
                Canal("pit", kind, 6, true, query: true, expectFull: false, fill: false, flooding: true);
            }
            Canal("pit", F.Water, 6, true, query: true, expectFull: false, river: true, flooding: true);
            DirectGuards();
#if !BASELINE
            // (run.ps1 -Baseline builds the suite against an older H2Filling.cs, which has none of these)
            PocketCases();
            BarrierCases();
            HeldCases();
            HeldTrenchAndLongCanal();
            PondCases();
            ReleaseCases();
#endif
            RiverRegressions();
            Console.WriteLine($"{checks - failures}/{checks} checks passed; {failures} failed");
            return failures == 0 ? 0 : 1;
        }

        static void WorldOptions()
        {
            foreach (bool forced in new[] { false, true })
            foreach (bool allowed in new[] { false, true })
            foreach (bool legacy in new[] { false, true })
            foreach (bool queryFlag in new[] { false, true })
            foreach (bool query in new[] { false, true })
            foreach (bool? flooding in new bool?[] { null, false, true })
            {
                var sim = new Sim(new World(4, 4, 4), Sweep.Recommended(), 1);
                var world = new GameWorld(sim);
                H2Filling.Vessels = forced;
                H2Filling.WorldMayFillVessels = allowed;
                EarthRiverQuery.Active = query;
                world.Config.Vessels = legacy;
                world.Config.VesselsWithQuery = queryFlag;
                world.Config.Flooding = flooding;
                bool fallback = forced || (allowed && (legacy || (query && queryFlag)));
                // rc.4: a world that asks for vessels only with Earth's query keeps them off, flooding or not, until it binds
                bool expected = flooding == true && queryFlag && !query ? false : (flooding ?? fallback);
                Check(H2Filling.VesselsActive(world) == expected,
                    $"flooding={flooding} forced={forced} allowed={allowed} legacy={legacy} queryFlag={queryFlag} query={query}");
                // Populate a legacy true cache, then prove an explicit false takes precedence over it.
                world.Config.Flooding = null;
                H2Filling.ResetWorld();
                _ = H2Filling.VesselsActive(world);
                world.Config.Flooding = false;
                Check(!H2Filling.VesselsActive(world), "explicit false overrides cached legacy state");
            }
            H2Filling.Vessels = false;
            H2Filling.WorldMayFillVessels = true;
            H2Filling.ResetWorld();
        }

        static int Bed(string bed, int x) => bed switch
        {
            "hump" => x >= 18 && x <= 24 ? 8 : 6,
            "pit" => x >= 18 && x <= 24 ? 4 : 6,
            _ => x < 18 ? 8 : x < 25 ? 6 : 4,
        };

        static void Canal(string bed, int kind, int width, bool mass, bool query, bool expectFull,
            bool river = false, bool vessels = true, bool fill = true, bool? flooding = null)
        {
            EarthRiverQuery.Active = query;
            EarthRiverQuery.IsRiver = _ => river;
            H2Filling.Enabled = fill;
            H2Filling.Vessels = false;
            var w = new World(40, 16, 16);
            w.FillSolid(0, 0, 0, 39, 10, 15);
            for (int x = 1; x <= 10; x++)
            for (int z = 1; z <= 14; z++)
            for (int y = 5; y <= 10; y++)
            {
                w.SetSolid(x, y, z, false);
                w.SetFluid(x, y, z, F.Make(kind, F.Still, 7));
            }
            var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
            r.WakeOnNewSource = true;
            var sim = new Sim(w, r, 1);
            var world = new GameWorld(sim);
            world.Config.Vessels = vessels;
            world.Config.Flooding = flooding;
            // All removals schedule neighbour updates, including the single-callback mass edit. A missed WorldEdit
            // notification cannot account for this reproducer. Row-wise digging lets the water run between edits.
            for (int x = 11; x <= 34; x++)
            {
                for (int z = 5; z < 5 + width; z++)
                for (int y = Bed(bed, x); y <= 10; y++) sim.BreakBlock(x, y, z);
                if (!mass) sim.Run(1000);
            }
            string name = $"{bed} kind={kind} width={width} mass={mass} query={query} river={river} vessels={vessels} fill={fill} flooding={flooding}";
            Check(sim.Settle(), name + " settles");
            int sources = 0, total = 0;
            for (int x = 11; x <= 34; x++)
            for (int z = 5; z < 5 + width; z++)
            for (int y = Bed(bed, x); y <= 10; y++)
            {
                total++;
                if (F.IsSource(w.Get(x, y, z))) sources++;
            }
            Check(expectFull ? sources == total : sources < total, name + $" sources {sources}/{total}");
            var before = w.Clone();
            sim.WakeAll();
            Check(sim.Settle(), name + " settles after wake-all");
            if (expectFull) Check(w.CountWaterDiff(before) == 0, name + " stable levels after wake-all");
            for (int x = 11; x <= 34; x++)
            for (int z = 5; z < 5 + width; z++)
            {
                Check(w.Get(x, 11, z) == 0, name + " no water above feed surface");
                Check(w.IsSolid(x, Bed(bed, x) - 1, z), name + " bed preserved");
            }
            Console.WriteLine($"{name}: sources={sources}/{total}, updates={sim.Updates}");
        }

        static void DirectGuards()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = true;
            EarthRiverQuery.Active = false;
            EarthRiverQuery.IsRiver = _ => false;
            var w = new World(12, 24, 12);
            var sim = new Sim(w, Sweep.Recommended(), 1);
            var world = new GameWorld(sim);
            int salt = F.Make(F.Salt, F.Still, 6), source = F.Make(F.Salt, F.Still, 7);
            // A falling sheet fed by two adjacent source columns at y=8.
            for (int y = 4; y <= 8; y++)
            for (int z = 4; z <= 5; z++) w.SetFluid(5, y, z, salt);
            for (int z = 4; z <= 5; z++) w.SetFluid(4, 8, z, source);
            var p = new BlockPos(5, 4, 4);
            Check(!H2Filling.TryFill(world, p, new Block { Id = salt }), "no source seeded in unsupported sheet");
            // the sheet lands in a walled basin, so its level is held (0.6.2-rc.3) and each check below is about the scan
            for (int x = 4; x <= 6; x++) for (int z = 3; z <= 6; z++) w.SetSolid(x, 3, z, true);
            foreach (var (x, z) in new[] { (4, 4), (4, 5), (6, 4), (6, 5), (5, 3), (5, 6) }) w.SetSolid(x, 4, z, true);
            w.SetFluid(5, 6, 4, 0);
            Check(!H2Filling.TryFill(world, p, new Block { Id = salt }), "air gap interrupts upward scan");
            w.SetFluid(5, 6, 4, F.Make(F.Water, F.Still, 6));
            Check(!H2Filling.TryFill(world, p, new Block { Id = salt }), "other liquid interrupts upward scan");
            w.SetFluid(5, 6, 4, salt);
            w.SetSolid(5, 6, 4, true);
            Check(!H2Filling.TryFill(world, p, new Block { Id = salt }), "solid interrupts upward scan");
            w.SetSolid(5, 6, 4, false);
            w.SetFluid(5, 6, 4, salt);
            w.SetSolid(4, 8, 4, true);
            w.SetFluid(4, 8, 4, source); // waterlogged full barrier in the adapter
            Check(!H2Filling.TryFill(world, p, new Block { Id = salt }), "source behind barrier cannot feed sheet");
            w.SetSolid(4, 8, 4, false);
            w.SetFluid(4, 8, 4, source);
            // HY-0007: the space up to the sheet's top (the open air around the sheet, in this unwalled world) is far more
            // than its two sources could fill
            Check(!H2Filling.TryFill(world, p, new Block { Id = salt }), "a sheet fed by 2 sources does not fill the open space around it");
            // walled round, the sheet and its basin are 10 cells, and a pool of 6 sources feeds them
            for (int y = 5; y <= 8; y++)
                foreach (var (x, z) in new[] { (6, 4), (6, 5), (5, 3), (5, 6) }) w.SetSolid(x, y, z, true);
            for (int y = 5; y <= 7; y++) { w.SetSolid(4, y, 4, true); w.SetSolid(4, y, 5, true); }
            foreach (var (x, z) in new[] { (3, 4), (3, 5), (2, 4), (2, 5) }) w.SetFluid(x, 8, z, source);
            H2Filling.ResetWorld();
            Check(H2Filling.TryFill(world, p, new Block { Id = salt }), "supported salt sheet seeds lower layer");
            // (a sheet landing on a level that is not held seeds nothing: HeldCases)
            // A column longer than the fixed scan cannot cause an unbounded search.
            w.SetFluid(5, 4, 4, salt);
            for (int y = 5; y <= 22; y++) w.SetFluid(5, y, 4, salt);
            w.SetFluid(4, 8, 4, 0);
            w.SetFluid(4, 8, 5, 0);
            for (int z = 4; z <= 5; z++)
            { w.SetFluid(4, 22, z, source); w.SetFluid(5, 22, z, salt); }
            Check(!H2Filling.TryFill(world, p, new Block { Id = salt }), "upward search bounded to 16 cells");
            // Earth only provides geography in dimension 0. An active binding is not evidence for other dimensions.
            for (int i = 0; i < w.Fluid.Length; i++)
                if (w.Fluid[i] != 0) w.Fluid[i] = F.WithKind(w.Fluid[i], F.Water);
            for (int z = 4; z <= 5; z++) w.SetFluid(4, 8, z, F.Make(F.Water, F.Still, 7));
            EarthRiverQuery.Active = true;
            world = new GameWorld(sim, 1);
            Check(!H2Filling.TryFill(world, new BlockPos(5, 4, 4, 1), world.Fluid(p)), "fresh basin requires geography in its dimension");
        }

#if !BASELINE
        /// <summary>
        /// 0.6.2's enclosed pockets (H2Filling.Pockets), called directly: a region of flowing water on the ground at one
        /// level, walled in, with a source beside the cell asked. The region fills when it is enclosed and has at most 128
        /// cells; a dry cell on the ground, another liquid at its rim, or one cell more refuses it.
        /// </summary>
        static void PocketCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = true;
            EarthRiverQuery.Active = false;
            EarthRiverQuery.IsRiver = _ => false;
            int flowing = F.Make(F.Water, F.Still, 3), source = F.Make(F.Water, F.Still, 7);
            const int Y = 5;

            // A walled region of sx by sz cells at y = 5 over solid ground, all flowing water, with sources at the given cells.
            (World w, GameWorld world) Region(int sx, int sz, params (int x, int z)[] sources)
            {
                var w = new World(sx + 4, 10, sz + 4);
                for (int x = 0; x < w.SX; x++)
                for (int z = 0; z < w.SZ; z++)
                {
                    for (int y = 0; y < Y; y++) w.SetSolid(x, y, z, true);
                    bool inside = x >= 2 && x < sx + 2 && z >= 2 && z < sz + 2;
                    if (!inside) w.SetSolid(x, Y, z, true);
                    else w.SetFluid(x, Y, z, flowing);
                }
                foreach (var (x, z) in sources) w.SetFluid(x + 2, Y, z + 2, source);
                return (w, new GameWorld(new Sim(w, Sweep.Recommended(), 1)));
            }
            bool Fill(GameWorld world, World w, int x, int z) => H2Filling.TryFill(world, new BlockPos(x + 2, Y, z + 2), new Block { Id = w.Get(x + 2, Y, z + 2) });

            // a nook a block wide, two long, beside one source: the delta pit's case
            var (w1, g1) = Region(3, 1, (0, 0));
            long before = HydrologyStats.FilledPockets;
            Check(Fill(g1, w1, 1, 0) && F.IsSource(w1.Get(3, Y, 2)), "pocket: a walled 1-wide nook beside a source fills");
            Check(HydrologyStats.FilledPockets == before + 1, "pocket: counted as a pocket fill");
            H2Filling.Pockets = false;
            var (w2, g2) = Region(3, 1, (0, 0));
            Check(!Fill(g2, w2, 1, 0), "pocket: FillPockets=false leaves the nook flowing");
            H2Filling.Pockets = true;
            var (w3, g3) = Region(3, 1, (0, 0));
            w3.SetFluid(4, Y, 2, 0); // the far end is dry ground
            Check(!Fill(g3, w3, 1, 0), "pocket: a dry cell on the ground ends the pocket");
            var (w4, g4) = Region(3, 1, (0, 0));
            w4.SetSolid(5, Y, 2, false);
            w4.SetFluid(5, Y, 2, F.Make(F.Salt, F.Still, 7)); // the rim beyond the far end is salt water
            Check(!Fill(g4, w4, 1, 0), "pocket: another liquid at the rim refuses it");
            // 13 x 10 = 130 cells: with two sources 128 flowing cells fill, with one source 129 do not
            var (w5, g5) = Region(13, 10, (0, 0), (12, 9));
            Check(Fill(g5, w5, 1, 0), "pocket: 128 cells fill");
            var (w6, g6) = Region(13, 10, (0, 0));
            Check(!Fill(g6, w6, 1, 0), "pocket: 129 cells are refused");
            // a nook three cells deep beside a column of sources, flowing water in it: waking only its top cell reaches the
            // bottom cell (on the ground), which fills, and the cells above fill on it (the Nile and delta cases)
            var w7 = new World(7, 12, 7);
            for (int x = 0; x < 7; x++)
            for (int z = 0; z < 7; z++)
            for (int y = 0; y <= 8; y++) w7.SetSolid(x, y, z, y < 5 || !(x == 3 && z == 3 || x == 2 && z == 3));
            for (int y = 5; y <= 7; y++) { w7.SetFluid(2, y, 3, source); w7.SetFluid(3, y, 3, flowing); }
            w7.SetSolid(2, 8, 3, false); w7.SetSolid(3, 8, 3, false);
            var sim7 = new Sim(w7, Sweep.Recommended(), 1);
            _ = new GameWorld(sim7);
            sim7.Wake(3, 7, 3);
            Check(sim7.Settle(), "pocket chain: settles");
            Check(F.IsSource(w7.Get(3, 5, 3)) && F.IsSource(w7.Get(3, 6, 3)) && F.IsSource(w7.Get(3, 7, 3)),
                "pocket chain: a poke at the top fills a 3-deep nook beside sources");
            Console.WriteLine("pockets: nook, switch, dry end, other liquid, 128/129 bound, 3-deep chain");
        }

        /// <summary>
        /// 0.6.2: a source behind a liquid-tight face (in the adapter, a solid block with water in its fluid layer, like a
        /// waterlogged stair's back) feeds no fill: not the ground rule, not refill, not a pocket's source.
        /// </summary>
        static void BarrierCases()
        {
            H2Filling.Enabled = true;
            EarthRiverQuery.Active = false;
            EarthRiverQuery.IsRiver = _ => false;
            int flowing = F.Make(F.Water, F.Still, 3), source = F.Make(F.Water, F.Still, 7);
            const int Y = 5;
            // a flowing cell at (3, 5, 3) on the ground, sources west (2,3) and east (4,3); walls north and south
            (World w, GameWorld world) Cell(bool eastBehindBarrier)
            {
                var w = new World(7, 10, 7);
                for (int x = 0; x < 7; x++)
                for (int z = 0; z < 7; z++)
                {
                    for (int y = 0; y < Y; y++) w.SetSolid(x, y, z, true);
                    w.SetSolid(x, Y, z, true);
                }
                w.SetSolid(3, Y, 3, false); w.SetFluid(3, Y, 3, flowing);
                w.SetSolid(2, Y, 3, false); w.SetFluid(2, Y, 3, source);
                w.SetSolid(4, Y, 3, eastBehindBarrier); w.SetFluid(4, Y, 3, source);
                return (w, new GameWorld(new Sim(w, Sweep.Recommended(), 1)));
            }
            bool Fill(GameWorld world, World w) => H2Filling.TryFill(world, new BlockPos(3, Y, 3), new Block { Id = w.Get(3, Y, 3) });

            // the ground rule alone: no vessels from the config or the world (so no pocket either)
            H2Filling.Vessels = false;
            var (w1, g1) = Cell(false);
            g1.Config.Vessels = false;
            Check(Fill(g1, w1), "barrier: two open source sides fill (ground rule)");
            var (w2, g2) = Cell(true);
            g2.Config.Vessels = false;
            Check(!Fill(g2, w2), "barrier: a source behind a liquid-tight face does not count");
            // with vessels on, the cell is a 1-cell pocket beside the open source; with only the barred source beside it, none
            H2Filling.Vessels = true;
            var (w3, g3) = Cell(true);
            Check(Fill(g3, w3), "barrier: the open source still makes it a pocket");
            var (w4, g4) = Cell(true);
            w4.SetFluid(2, Y, 3, 0); w4.SetSolid(2, Y, 3, true);
            Check(!Fill(g4, w4), "barrier: a pocket whose only source is behind a liquid-tight face does not fill");
            Console.WriteLine("barriers: ground rule, pocket source");
        }

        /// <summary>
        /// 0.6.2-rc.3 (a Mod DB report: a source spilled into the sea and the sea climbed to it): a salt sea 3 deep (y 4 to
        /// 6) with land around it whose top is level with the sea's surface (its open cells one block above the sea's), the
        /// whole world played with production H2 and Water flooding on. With an outlet (beyond x = 24 the land drops 5 blocks to a lowland of more open space than H2 follows, so
        /// water at the sea's next level would run off there, as on open land) a bucket emptied onto the sea, a pool two
        /// wide at the top of a cliff over it and two sources on the land raise nothing; in a smaller world walled all
        /// round, the same basin fills to the spring's level, as a closed basin under an endless spring does.
        /// </summary>
        static void HeldCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = false;
            EarthRiverQuery.Active = false;
            EarthRiverQuery.IsRiver = _ => false;
            int salt = F.Make(F.Salt, F.Still, 7);
            const int L = 6;
            foreach (string shape in new[] { "bucket", "cliff", "field" })
            foreach (bool outlet in new[] { true, false })
            {
                int size = outlet ? 64 : 24;
                var w = new World(size, 14, size);
                w.FillSolid(0, 0, 0, size - 1, L, size - 1);
                for (int x = 2; x <= 13; x++) for (int z = 2; z <= 21; z++) for (int y = 4; y <= L; y++)
                { w.SetSolid(x, y, z, false); w.SetFluid(x, y, z, salt); }
                if (outlet) w.FillSolid(24, 2, 0, size - 1, L, size - 1, false);
                // the cliff: a plateau 3 high east of the sea, the pool at its edge, its spill falling into the sea (with the
                // outlet, land beyond z = 23 leads around it to the lowland at every level)
                if (shape == "cliff") w.FillSolid(14, L + 1, 0, 23, L + 3, 23);
                var springs = shape switch
                {
                    "bucket" => new[] { (7, L + 1, 11) },
                    "cliff" => new[] { (14, L + 4, 11), (14, L + 4, 12) },
                    _ => new[] { (18, L + 1, 11), (18, L + 1, 12) },
                };
                var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
                r.WakeOnNewSource = true;
                var sim = new Sim(w, r, 1);
                var world = new GameWorld(sim);
                world.Config.Flooding = true;
                foreach (var (x, y, z) in springs) { w.SetFluid(x, y, z, salt); sim.Wake(x, y, z); }
                string name = $"held {shape} outlet={outlet}";
                Check(sim.Settle(), name + " settles");
                int raised = 0;
                for (int x = 0; x < w.SX; x++) for (int z = 0; z < w.SZ; z++) for (int y = L + 1; y < w.SY; y++)
                    if (F.IsSource(w.Get(x, y, z))) raised++;
                raised -= springs.Length;
                // walled all round, the levels over the sea are held, but only the springs' water stands at them, far too
                // little to fill them (HY-0007, decision 0006; rc.3 to rc.6 raised the sea to the springs' level here)
                Check(raised == 0, name + $": {raised} sources raised");
                Console.WriteLine($"{name}: {raised} sources raised, updates={sim.Updates}");
            }
        }

        /// <summary>
        /// rc.4: what the held levels must still fill. (1) A trench 2 wide and 3 deep, dug out whole from the salt sea into
        /// land that drains to a lowland (as HeldCases' outlet world) and then opened: its own space is closed, so it fills
        /// to the sea's level, whatever lies beyond its walls. (2) A canal 3 wide, 3 deep and 500 long dug out whole from a
        /// reservoir and then opened (the author's issue #20 "Suez" use): 4,500 open cells in all, 1,500 at its surface. The
        /// first held check counted them all against one limit of 4,096, refused the canal's surface while its depths were
        /// unfilled and never asked again; the level and the whole space now have their own limits.
        /// </summary>
        static void HeldTrenchAndLongCanal()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = false;
            EarthRiverQuery.Active = false;
            EarthRiverQuery.IsRiver = _ => false;
            int salt = F.Make(F.Salt, F.Still, 7);
            var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
            r.WakeOnNewSource = true;
            int Sources(World w, int x0, int x1, int z0, int z1, int y0, int y1)
            {
                int s = 0;
                for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) for (int y = y0; y <= y1; y++)
                    if (F.IsSource(w.Get(x, y, z))) s++;
                return s;
            }

            // (1) the trench beside open land
            const int L = 6;
            var w1 = new World(64, 14, 64);
            w1.FillSolid(0, 0, 0, 63, L, 63);
            for (int x = 2; x <= 13; x++) for (int z = 2; z <= 21; z++) for (int y = 4; y <= L; y++)
            { w1.SetSolid(x, y, z, false); w1.SetFluid(x, y, z, salt); }
            w1.FillSolid(24, 2, 0, 63, L, 63, false);
            var sim1 = new Sim(w1, r, 1);
            var g1 = new GameWorld(sim1);
            g1.Config.Flooding = true;
            for (int x = 20; x >= 14; x--) for (int z = 10; z <= 11; z++) for (int y = 4; y <= L; y++) sim1.BreakBlock(x, y, z);
            Check(sim1.Settle(), "held trench: settles");
            int t = Sources(w1, 14, 20, 10, 11, 4, L);
            Check(t == 7 * 2 * 3, $"held trench: a trench dug from the sea beside open land fills ({t}/42)");
            Check(Sources(w1, 0, 63, 0, 63, L + 1, 13) == 0, "held trench: nothing rises above the sea");
            Console.WriteLine($"held trench beside open land: {t}/42 sources, updates={sim1.Updates}");

            // (2) the long canal
            int fresh = F.Make(F.Water, F.Still, 7);
            var w2 = new World(520, 10, 10);
            w2.FillSolid(0, 0, 0, 519, 5, 9);
            for (int x = 1; x <= 9; x++) for (int z = 1; z <= 8; z++) for (int y = 3; y <= 5; y++)
            { w2.SetSolid(x, y, z, false); w2.SetFluid(x, y, z, fresh); }
            // HY-0007: the reservoir (216 sources) is too small to fill the canal; its water flows in and stays flowing
            var w3 = w2.Clone();
            var sim3 = new Sim(w3, r, 1);
            var g3 = new GameWorld(sim3);
            g3.Config.Flooding = true;
            EarthRiverQuery.Active = true;
            for (int x = 510; x >= 10; x--) for (int z = 3; z <= 5; z++) for (int y = 3; y <= 5; y++) sim3.BreakBlock(x, y, z);
            Check(sim3.Settle(1_800_000, 5_000_000), "long canal from a small reservoir: settles");
            int c3 = Sources(w3, 10, 510, 3, 5, 3, 5);
            Check(c3 <= 2 * 216, $"long canal from a small reservoir: fills no more than its water allows ({c3}/{501 * 9})");
            Console.WriteLine($"long canal 3 x 3 x 501 from a reservoir of 216: {c3}/{501 * 9} sources, updates={sim3.Updates}");
            // as from a river (the issue #20 use): the reservoir's columns are natural river water
            EarthRiverQuery.IsRiver = q => q.X <= 9;
            var sim2 = new Sim(w2, r, 1);
            var g2 = new GameWorld(sim2);
            g2.Config.Flooding = true;
            EarthRiverQuery.Active = true; // as on Earth (fresh water seeds an uneven basin only with the query)
            // dug out whole, far end first, the reservoir's side last
            for (int x = 510; x >= 10; x--) for (int z = 3; z <= 5; z++) for (int y = 3; y <= 5; y++) sim2.BreakBlock(x, y, z);
            Check(sim2.Settle(1_800_000, 5_000_000), "long canal: settles");
            int c = Sources(w2, 10, 510, 3, 5, 3, 5);
            Check(c == 501 * 9, $"long canal: 3 x 3 x 501 dug out whole and opened fills ({c}/{501 * 9})");
            Console.WriteLine($"long canal 3 x 3 x 501: {c}/{501 * 9} sources, updates={sim2.Updates}");
            EarthRiverQuery.Active = false;
            EarthRiverQuery.IsRiver = _ => false;
        }
#endif

#if !BASELINE
        /// <summary>
        /// HY-0007 (decision 0006): a pond on high ground whose rim is broken. A pond 7 x 7 x 2 (98 sources) in a mound
        /// whose top is 4 above a walled lowland, the rim broken 2 wide at the pond's level: the lowland is a closed hollow
        /// ("hollow"), or raised to the pond's bed so the rim opens onto flat land ("flat"). With the supply rule off (rc.6)
        /// the hollow fills to the pond's level; with it on, neither the hollow nor the flat land takes a source. A lake
        /// (1,628 sources) breaking into a smaller hollow still fills it.
        /// </summary>
        static void PondCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = false;
            EarthRiverQuery.Active = true; // as on Earth: fresh water seeds an uneven basin under a falling sheet
            EarthRiverQuery.IsRiver = _ => false;
            int fresh = F.Make(F.Water, F.Still, 7);
            foreach (var (shape, factor) in new[] { ("hollow", 0), ("hollow", 2), ("flat", 2), ("lake", 2) })
            {
                H2Filling.SupplyFactor = factor;
                H2Filling.ResetWorld();
                var w = new World(50, 14, 30);
                w.FillSolid(0, 0, 0, 49, 3, 29);
                w.FillSolid(0, 4, 0, 49, 12, 0); w.FillSolid(0, 4, 29, 49, 12, 29);
                w.FillSolid(0, 4, 0, 0, 12, 29); w.FillSolid(49, 4, 0, 49, 12, 29);
                bool lake = shape == "lake";
                int x0 = 3, x1 = lake ? 41 : 11, z0 = lake ? 3 : 11, z1 = lake ? 26 : 19;
                if (shape == "flat") w.FillSolid(1, 4, 1, 48, 6, 28);
                w.FillSolid(x0, 4, z0, x1, 7, z1);
                int pond = 0;
                for (int x = x0 + 1; x < x1; x++) for (int z = z0 + 1; z < z1; z++) for (int y = 6; y <= 7; y++)
                { w.SetSolid(x, y, z, false); w.SetFluid(x, y, z, fresh); pond++; }
                var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
                r.WakeOnNewSource = true;
                var sim = new Sim(w, r, 1);
                var g = new GameWorld(sim);
                g.Config.Flooding = true;
                sim.BreakBlock(x1, 7, 15);
                sim.BreakBlock(x1, 7, 16);
                string name = $"pond {shape} ({pond} sources) supply factor {factor}";
                Check(sim.Settle(600_000, 3_000_000), name + ": settles");
                int outside = 0, inPond = 0;
                for (int x = 1; x <= 48; x++) for (int z = 1; z <= 28; z++) for (int y = 4; y <= 8; y++)
                {
                    if (!F.IsSource(w.Get(x, y, z))) continue;
                    if (x > x0 && x < x1 && z > z0 && z < z1) inPond++; else outside++;
                }
                Check(inPond == pond, name + $": the pond keeps its water ({inPond}/{pond})");
                if (lake) Check(outside > 500, name + $": the hollow fills ({outside} sources outside the lake)");
                else if (factor == 0) Check(outside > 1000, name + $": the hollow floods without the rule ({outside} sources)");
                else Check(outside == 0, name + $": no source outside the pond ({outside})");
                Console.WriteLine($"{name}: {outside} sources outside, updates={sim.Updates}");
            }
            H2Filling.SupplyFactor = 2;
            H2Filling.ResetWorld();
            EarthRiverQuery.Active = false;
        }
#endif

#if !BASELINE
        /// <summary>
        /// H2 release (rc.8, decision 0007): the pond of <see cref="PondCases"/>, its rim broken 2 wide at its surface, each
        /// break followed by the server's break event. Hollow: the top layer (49) collects at the lowland's floor, the
        /// pond keeps its lower layer. Flat land at the pond's level: nothing moves. A lake of 1,628 sources (below the
        /// unlimited size): its top layer fills the hollow around it, volume kept. A lowland of river water (the query
        /// calls part of it river): the river takes the water and does not rise. Land at the pond's level beyond the opening:
        /// the water runs to an edge 5 blocks away and falls (ledge); an edge 20 blocks away is beyond its run (plateau).
        /// </summary>
        static void ReleaseCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = false;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            EarthRiverQuery.Active = true;
            int fresh = F.Make(F.Water, F.Still, 7);
            foreach (var shape in new[] { "hollow", "flat", "lake", "river", "ledge", "plateau" })
            {
                EarthRiverQuery.IsRiver = shape == "river" ? q => q.X >= 40 : _ => false;
                H2Filling.ResetWorld();
                var w = new World(50, 14, 30);
                w.FillSolid(0, 0, 0, 49, 3, 29);
                w.FillSolid(0, 4, 0, 49, 12, 0); w.FillSolid(0, 4, 29, 49, 12, 29);
                w.FillSolid(0, 4, 0, 0, 12, 29); w.FillSolid(49, 4, 0, 49, 12, 29);
                bool lake = shape == "lake";
                int x0 = 3, x1 = lake ? 41 : 11, z0 = lake ? 3 : 11, z1 = lake ? 26 : 19;
                if (shape == "flat") w.FillSolid(1, 4, 1, 48, 6, 28);
                // land at the pond's level beyond the opening, its edge 5 (ledge) or 20 (plateau) blocks away
                if (shape == "ledge") w.FillSolid(1, 4, 1, 15, 6, 28);
                if (shape == "plateau") w.FillSolid(1, 4, 1, 30, 6, 28);
                w.FillSolid(x0, 4, z0, x1, 7, z1);
                int lowland = 0;
                if (shape == "river")
                    for (int x = 1; x <= 48; x++) for (int z = 1; z <= 28; z++)
                        if (!(x >= x0 && x <= x1 && z >= z0 && z <= z1)) { w.SetFluid(x, 4, z, fresh); lowland++; }
                int pond = 0, top = 0;
                for (int x = x0 + 1; x < x1; x++) for (int z = z0 + 1; z < z1; z++) for (int y = 6; y <= 7; y++)
                { w.SetSolid(x, y, z, false); w.SetFluid(x, y, z, fresh); pond++; if (y == 7) top++; }
                var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
                r.WakeOnNewSource = true;
                var sim = new Sim(w, r, 1);
                var g = new GameWorld(sim);
                g.Config.Flooding = true;
                long absorbed0 = HydrologyStats.ReleaseAbsorbed;
                sim.BreakBlock(x1, 7, 15);
                int moved = H2Release.Opened(g, new BlockPos(x1, 7, 15));
                sim.BreakBlock(x1, 7, 16);
                moved += H2Release.Opened(g, new BlockPos(x1, 7, 16));
                string name = $"release {shape} ({pond} sources)";
                Check(sim.Settle(600_000, 3_000_000), name + ": settles");
                int outside = 0, inPond = 0, above = 0;
                for (int x = 1; x <= 48; x++) for (int z = 1; z <= 28; z++) for (int y = 4; y <= 8; y++)
                {
                    if (!F.IsSource(w.Get(x, y, z))) continue;
                    if (x > x0 && x < x1 && z > z0 && z < z1) inPond++; else { outside++; if (y > 4) above++; }
                }
                switch (shape)
                {
                    case "hollow":
                        Check(moved == top && inPond == pond - top, name + $": the top layer lets out ({moved} moved, {inPond} stay)");
                        Check(outside == top && above == 0, name + $": the water collects on the lowland's floor ({outside} sources, {above} above it)");
                        break;
                    case "ledge":
                        Check(moved == top && inPond == pond - top, name + $": the water runs 5 blocks to the edge and falls ({moved} moved, {inPond} stay)");
                        Check(outside == top && above == 0, name + $": it collects at the foot ({outside} sources, {above} above the floor)");
                        break;
                    case "plateau":
                    case "flat":
                        Check(moved == 0 && inPond == pond && outside == 0, name + $": nothing moves onto land at the pond's level ({moved} moved, {inPond} stay, {outside} outside)");
                        break;
                    case "lake":
                        Check(moved == top && inPond == pond - top, name + $": the lake's top layer lets out ({moved} moved, {inPond} stay)");
                        // the last, partly filled layer may be completed by H2 when decision 0006 allows: within a layer
                        Check(outside >= top && outside <= top + 408, name + $": the hollow takes the water ({outside} sources for {top})");
                        break;
                    case "river":
                        Check(moved == top && inPond == pond - top, name + $": the top layer lets out ({moved} moved, {inPond} stay)");
                        Check(outside == lowland && above == 0, name + $": the river takes it and does not rise ({outside} sources for {lowland}, {above} above)");
                        Check(HydrologyStats.ReleaseAbsorbed - absorbed0 == top, name + ": counted as absorbed");
                        break;
                }
                Console.WriteLine($"{name}: moved {moved}, {inPond} in the pond, {outside} outside ({above} above the floor), updates={sim.Updates}");
            }
            EarthRiverQuery.IsRiver = _ => false;
            EarthRiverQuery.Active = false;
            H2Filling.ResetWorld();
        }
#endif

        static void RiverRegressions()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = true;
            EarthRiverQuery.Active = true;
            foreach (int width in new[] { 2, 8 })
            foreach (int depth in new[] { 2, 3 })
            {
                var reach = QueryStudy.Build(new[] { 7, 9 }, width, depth, true, false, true, lead: 12);
                FlowQuery.Current = reach.Q;
                EarthRiverQuery.IsRiver = p => reach.Q.River(p.X, p.Z);
                var r = QueryStudy.WithQuery();
                r.WakeOnNewSource = true;
                var sim = new Sim(reach.S.Generated.Clone(), r, 1);
                _ = new GameWorld(sim);
                sim.WakeAll();
                Check(sim.Settle(), $"river width={width} depth={depth} settles");
                // Remove a solid bed cell beside a step, then wake every cell again.
                int bx = reach.At[0] - 1, bz = reach.Z0;
                int by = reach.C.WaterTop[reach.C.K(bx, bz)] - 1;
                sim.BreakBlock(bx, by, bz);
                Check(sim.Settle(), "river bed break settles");
                sim.WakeAll();
                Check(sim.Settle(), "river wake-all settles");
                for (int x = 0; x < sim.W.SX; x++)
                for (int z = 0; z < sim.W.SZ; z++)
                {
                    if (!reach.Q.River(x, z)) continue;
                    int naturalTop = reach.C.WaterTop[reach.C.K(x, z)];
                    for (int y = naturalTop + 1; y < sim.W.SY; y++)
                        Check(!F.IsSource(sim.W.Get(x, y, z)), "natural river step has no raised source");
                }
                Console.WriteLine($"natural river width={width} depth={depth}: no raised sources, updates={sim.Updates}");
            }
            FlowQuery.Current = null;
        }
    }
}
