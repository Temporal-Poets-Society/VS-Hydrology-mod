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
            DamCases();
            BasinCases();
            LoadedLandCases();
            SaveLoadCases();
            LinkedSearchCases();
            H4H7Cases();
            PoolOverDrainCase();
            LargeBodyCase();
            DriverCase();
            FloodingKeyCase();
            LongPoolCase();
            StepsUpstreamCase();
            AllocCases();
#endif
            RiverRegressions();
#if !BASELINE
            RiverDamCases();
            RiverFlowDamCases();
#endif
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
            // HY-0008 (rc.9): the basin is measured at its own level (rc.7 measured the open air up to the sheet's top), and
            // the two sources feeding the sheet are counted through it: they fill its two cells
            Check(H2Filling.TryFill(world, p, new Block { Id = salt }), "a sheet fed by 2 sources fills the 2-cell basin it lands in");
            w.SetFluid(5, 4, 4, salt);
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
        /// <summary>rc.9 (decision 0008): taking a dam's top course away lets the water it raised drain past it.</summary>
        static void DamCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = false;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            H2Release.DrainRate = 0;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
            EarthRiverQuery.Active = true;
            EarthRiverQuery.IsRiver = _ => false;
            int fresh = F.Make(F.Water, F.Still, 7);
            // a trench 2 wide (z 4..5) and 4 deep (y 4..7) along x 1..38; a dam 2 high at x = 20; the water upstream raised
            // to the dam's top (y 4..5), downstream one layer (y 4), which runs out at x = 38 over the edge of a walled
            // lowland (x 39..58, y 2..7) 18 blocks from the dam
            var w = new World(60, 12, 10);
            w.FillSolid(0, 0, 0, 59, 7, 9);
            for (int x = 1; x <= 38; x++) for (int z = 4; z <= 5; z++) for (int y = 4; y <= 7; y++) w.SetSolid(x, y, z, x == 20 && y <= 5);
            for (int x = 39; x <= 58; x++) for (int z = 1; z <= 8; z++) for (int y = 2; y <= 7; y++) w.SetSolid(x, y, z, false);
            int up = 0;
            for (int x = 1; x <= 19; x++) for (int z = 4; z <= 5; z++) for (int y = 4; y <= 5; y++) { w.SetFluid(x, y, z, fresh); if (y == 5) up++; }
            for (int x = 21; x <= 38; x++) for (int z = 4; z <= 5; z++) w.SetFluid(x, 4, z, fresh);
            var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
            var sim = new Sim(w, r, 1);
            var g = new GameWorld(sim);
            g.Config.Flooding = true;
            sim.BreakBlock(20, 5, 4);
            int moved = H2Release.Opened(g, new BlockPos(20, 5, 4));
            sim.BreakBlock(20, 5, 5);
            moved += H2Release.Opened(g, new BlockPos(20, 5, 5));
            H2Release.RunAll(g);
            sim.Settle(600_000, 3_000_000);
            int upNow = 0, downAbove = 0, lowland = 0;
            for (int x = 1; x <= 19; x++) for (int z = 4; z <= 5; z++) if (F.IsSource(w.Get(x, 5, z))) upNow++;
            for (int x = 21; x <= 38; x++) for (int z = 4; z <= 5; z++) if (F.IsSource(w.Get(x, 5, z))) downAbove++;
            for (int x = 39; x <= 58; x++) for (int z = 1; z <= 8; z++) for (int y = 2; y <= 7; y++) if (F.IsSource(w.Get(x, y, z))) lowland++;
            Check(moved == up, $"dam: its top course taken away lets the raised layer out ({moved} of {up})");
            Check(upNow == 0, $"dam: no raised water stays over the remaining crest ({upNow})");
            Check(downAbove == 0, $"dam: the reach below does not rise ({downAbove})");
            Check(lowland >= up && lowland <= up + 160, $"dam: the water runs along the reach below and collects in the lowland ({lowland} for {up})");
            Console.WriteLine($"dam: moved {moved}, {upNow} upstream over the crest, {downAbove} over the reach below, {lowland} in the lowland");
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
            EarthRiverQuery.Active = false;
            H2Filling.ResetWorld();
        }

        /// <summary>rc.11 (HY-0009): a closed pit beside the pond takes what it holds and no more (the pond keeps the rest);
        /// water falling down an open valley that leaves the loaded land runs off there.</summary>
        static void BasinCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = false;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            H2Release.DrainRate = 0;
            EarthRiverQuery.Active = true;
            EarthRiverQuery.IsRiver = _ => false;
            int fresh = F.Make(F.Water, F.Still, 7);
            foreach (var shape in new[] { "pit", "pit-small", "pit-deep", "valley", "valley-loaded" })
            {
                H2Release.ResetWorld();
                H2Filling.ResetWorld();
                var w = new World(50, 14, 30);
                w.FillSolid(0, 0, 0, 49, 3, 29);
                w.FillSolid(0, 4, 0, 49, 12, 0); w.FillSolid(0, 4, 29, 49, 12, 29);
                w.FillSolid(0, 4, 0, 0, 12, 29); w.FillSolid(49, 4, 0, 49, 12, 29);
                int x0 = 3, x1 = 11, z0 = 11, z1 = 19, pit = 0;
                bool small = shape == "pit-small", deep = shape == "pit-deep";
                int pitX = small ? 15 : 18, pitZ0 = small ? 14 : 13, pitZ1 = small ? 17 : 18, pitY = deep ? 4 : 5;
                if (shape.StartsWith("pit"))
                {
                    // flat land at the opening's floor, a pit 2 deep 3 blocks from the opening: 5 x 6 (60 cells, more than the
                    // pond's top layer of 49, less than its 98) or 2 x 4 (16 cells, less than a layer). "pit-deep": the pit
                    // a block lower, reached by a trench from a hole in the pond's lower layer (98 sources above the hole)
                    w.FillSolid(1, 4, 1, 48, 6, 28);
                    for (int x = 14; x <= pitX; x++) for (int z = pitZ0; z <= pitZ1; z++) for (int y = pitY; y <= pitY + 1; y++) { w.SetSolid(x, y, z, false); pit++; }
                    if (deep) for (int x = 12; x <= 14; x++) w.SetSolid(x, 6, 15, false);
                }
                else
                {
                    // a valley 4 wide stepping down from the opening: floors at 5, 4 and 3; past x 23 the chunks are unloaded
                    // ("valley"), or loaded with a closed end at x 48 ("valley-loaded": the water stands at the valley's foot)
                    w.FillSolid(1, 4, 1, 48, 7, 28);
                    for (int x = 12; x <= 48; x++) for (int z = 14; z <= 17; z++)
                        for (int y = x <= 15 ? 6 : x <= 19 ? 5 : 4; y <= 7; y++) w.SetSolid(x, y, z, false);
                }
                w.FillSolid(x0, 4, z0, x1, 7, z1);
                int pond = 0, top = 0;
                for (int x = x0 + 1; x < x1; x++) for (int z = z0 + 1; z < z1; z++) for (int y = 6; y <= 7; y++)
                { w.SetSolid(x, y, z, false); w.SetFluid(x, y, z, fresh); pond++; if (y == 7) top++; }
                var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
                r.WakeOnNewSource = true;
                var sim = new Sim(w, r, 1);
                var g = new GameWorld(sim);
                g.Config.Flooding = true;
                if (shape == "valley") g.Unloaded = q => q.X >= 24;
                long absorbed0 = HydrologyStats.ReleaseAbsorbed;
                int moved;
                if (deep)
                {
                    sim.BreakBlock(x1, 6, 15);
                    moved = H2Release.Opened(g, new BlockPos(x1, 6, 15));
                }
                else
                {
                    sim.BreakBlock(x1, 7, 15);
                    moved = H2Release.Opened(g, new BlockPos(x1, 7, 15));
                    sim.BreakBlock(x1, 7, 16);
                    moved += H2Release.Opened(g, new BlockPos(x1, 7, 16));
                }
                H2Release.RunAll(g);
                g.Unloaded = null;
                string name = $"basin {shape} ({pond} sources)";
                Check(sim.Settle(600_000, 3_000_000), name + ": settles");
                int inPond = 0, inPit = 0, near = 0, far = 0;
                for (int x = 1; x <= 48; x++) for (int z = 1; z <= 28; z++) for (int y = 4; y <= 8; y++)
                {
                    if (!F.IsSource(w.Get(x, y, z))) continue;
                    if (x > x0 && x < x1 && z > z0 && z < z1) inPond++;
                    else if (pit > 0 && x >= 14 && x <= pitX && z >= pitZ0 && z <= pitZ1 && y <= pitY + 1) inPit++;
                    else if (x < 24) near++; else far++;
                }
                long absorbed = HydrologyStats.ReleaseAbsorbed - absorbed0;
                switch (shape)
                {
                    case "pit":
                        Check(moved == top && inPond == pond - top, name + $": the pit takes the top layer, the pond keeps the rest ({moved} moved, {inPond} stay)");
                        Check(inPit >= top && inPit <= pit && near + far == 0, name + $": the water stands in the pit, nothing over the land ({inPit} in the pit of {pit}, {near + far} elsewhere)");
                        break;
                    case "pit-deep":
                        // 98 would leave through the hole, the pit holds 60: the pond lets out its top layer (49) and keeps
                        // its lower one (a layer let out in part would fill again from the sources left)
                        Check(moved == top && inPond == pond - top, name + $": the pit takes whole layers it holds, the pond keeps the rest ({moved} moved, {inPond} stay)");
                        Check(inPit >= top && inPit <= pit, name + $": the water stands in the pit ({inPit} of {pit})");
                        break;
                    case "pit-small":
                        Check(moved == 0 && inPond == pond, name + $": a pit smaller than a layer lets nothing out of the pond ({moved} moved, {inPond} stay)");
                        Check(near + far == 0, name + $": nothing over the land ({near + far})");
                        break;
                    case "valley":
                        Check(moved == top && inPond == pond - top, name + $": the top layer lets out ({moved} moved, {inPond} stay)");
                        Check(absorbed == top && near == 0, name + $": it runs off down the valley past the loaded land ({absorbed} ran off, {near} stand in the loaded valley)");
                        break;
                    case "valley-loaded":
                        Check(moved == top && inPond == pond - top, name + $": the top layer lets out ({moved} moved, {inPond} stay)");
                        int foot = 0;
                        for (int x = 20; x <= 48; x++) for (int z = 14; z <= 17; z++) if (F.IsSource(w.Get(x, 4, z))) foot++;
                        Check(absorbed == 0 && foot >= top && foot == near + far, name + $": it collects on the valley's lowest floor ({foot} there of {near + far})");
                        break;
                }
                Console.WriteLine($"{name}: moved {moved}, {inPond} in the pond, {inPit} in the pit, {near} near, {far} far, {absorbed} ran off, updates={sim.Updates}");
            }
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
            EarthRiverQuery.Active = false;
            H2Filling.ResetWorld();
        }

        /// <summary>
        /// rc.13 (HY-0011, decision 0010): a dam across a long calm river with no step anywhere (surface 9, x 1..90, 6 wide,
        /// banks to 11, then 12; it runs out into an open lowland). Before, nothing rose: no water stood above the surface to
        /// fill from. Now the river fills the pool from its own flow: layer by layer to the crest, then one layer over it,
        /// which runs off past the dam; the river below keeps its level; breaking the top course drains it all again.
        /// </summary>
        static void RiverFlowDamCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = true;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            H2Release.DrainRate = 0;
            int fresh = F.Make(F.Water, F.Still, 7);
            const int z0 = 37, z1 = 42, damX = 65, surface = 9;
            foreach (int highCase in new[] { 1, 2, -1 })
            {
                // -1: the dam stands already (built at once, as WorldEdit did in game) and one crest block is placed by hand
                bool atOnce = highCase < 0;
                int high = System.Math.Abs(highCase);
                H2Release.ResetWorld();
                H2Filling.ResetWorld();
                int crest = surface + high, bank = crest + 1;
                var w = new World(160, 16, 100);
                w.FillSolid(0, 0, 0, 159, bank, 99);
                for (int x = 1; x <= 90; x++) for (int z = z0; z <= z1; z++)
                    for (int y = surface - 2; y <= bank; y++) { w.SetSolid(x, y, z, false); if (y <= surface) w.SetFluid(x, y, z, fresh); }
                w.FillSolid(91, 3, 1, 158, bank, 98, false);
                EarthRiverQuery.Active = true;
                EarthRiverQuery.IsRiver = q => q.X >= 1 && q.X <= 90 && q.Z >= z0 && q.Z <= z1;
                EarthRiverQuery.Surface = q => surface;
                var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
                r.WakeOnNewSource = true;
                var sim = new Sim(w, r, 1);
                var g = new GameWorld(sim);
                g.Config.Flooding = true;
                sim.WakeAll();
                Check(sim.Settle(600_000, 3_000_000), "river flow dam: the river settles");
                int Sources(int xa, int xb, int y)
                {
                    int n = 0;
                    for (int x = xa; x <= xb; x++) for (int z = z0 - 1; z <= z1 + 1; z++) if (F.IsSource(w.Get(x, y, z))) n++;
                    return n;
                }
                int Raised(int xa, int xb) { int n = 0; for (int y = surface + 1; y < w.SY; y++) n += Sources(xa, xb, y); return n; }
                int river0 = 0;
                for (int y = surface - 2; y <= surface; y++) river0 += Sources(1, 90, y) - Sources(damX, damX, y);
                // the dam, a block at a time from the bed up
                for (int y = surface - 2; y <= crest; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        sim.PlaceStone(damX, y, z);
                        if (!atOnce || (y == crest && z == z1)) H2Release.Placed(g, new BlockPos(damX, y, z));
                        sim.Run(100);
                    }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                H2DamPools.RunAll(g);
                double ms = watch.Elapsed.TotalMilliseconds;
                Check(sim.Settle(600_000, 3_000_000), $"river flow dam {high} high: settles");
                int layers = 0;
                for (int y = surface + 1; y <= crest; y++) if (Sources(1, damX - 1, y) == (damX - 1) * (z1 - z0 + 1)) layers++;
                int over = Sources(1, damX, crest + 1), down = Raised(damX + 1, 90);
                Check(layers == high, $"river flow dam {high} high: the pool fills to the crest ({layers} full layers)");
                Check(over >= damX * (z1 - z0 + 1) - z1 + z0 - 1 && Sources(1, damX, crest + 2) == 0, $"river flow dam {high} high: one layer runs over the crest ({over} cells, {Sources(1, damX, crest + 2)} above)");
                Check(down == 0, $"river flow dam {high} high: the river below keeps its level ({down} raised)");
                Console.WriteLine($"river flow dam {high} high{(atOnce ? " built at once" : "")}: {layers} layers to the crest, {over} over it, {down} below, planned and placed in {ms:0.0} ms, updates={sim.Updates}");
                int moved = 0;
                for (int z = z0; z <= z1; z++)
                {
                    sim.BreakBlock(damX, crest, z);
                    moved += H2Release.Opened(g, new BlockPos(damX, crest, z));
                }
                H2Release.RunAll(g);
                H2DamPools.RunAll(g);
                Check(sim.Settle(600_000, 3_000_000), $"river flow dam {high} high: settles after the top course goes");
                // what the remaining courses hold stays (a 2-high dam keeps a 1-high pool)
                int keep = high - 1, left = 0;
                for (int y = surface + 1 + keep; y < w.SY; y++) left += Sources(1, 90, y);
                Check(moved > 0 && left == 0, $"river flow dam {high} high: the water over the remaining course drains ({moved} let out, {left} left)");
                Console.WriteLine($"river flow dam {high} high: top course gone: {moved} let out, {left} left above the remaining course, {Raised(1, damX - 1)} raised behind it");
            }
            EarthRiverQuery.Surface = null;
            EarthRiverQuery.IsRiver = _ => false;
            EarthRiverQuery.Active = false;
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
        }

        /// <summary>
        /// HY-0013 (review findings 2 and 16): a pond is measured only on loaded land. The pond (x 26..37, z 24..31, two layers)
        /// straddles the chunk edge at x 32; its rim opens at z 32 onto a lowland in the next chunk south (z 33..68). (a) The
        /// chunks west of x 32 unloaded at the break: the body reaches unloaded land, nothing drains. (b) A drain saved and loaded
        /// again while the pond's land is still unloaded waits, then starts once it has loaded. (c) The lowland's chunk unloaded
        /// while a drain runs: the drain holds its water until the chunk is back, then every source stands below (none lost).
        /// </summary>
        static void LoadedLandCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = false;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            EarthRiverQuery.Active = false;
            int fresh = F.Make(F.Water, F.Still, 7);
            foreach (var shape in new[] { "unloaded-at-break", "saved-while-loading", "lowland-unloaded" })
            {
                H2Release.DrainRate = 1;
                H2Release.ResetWorld();
                H2Filling.ResetWorld();
                var w = new World(70, 14, 70);
                w.FillSolid(0, 0, 0, 69, 3, 69);
                w.FillSolid(0, 4, 0, 69, 12, 0); w.FillSolid(0, 4, 69, 69, 12, 69);
                w.FillSolid(0, 4, 0, 0, 12, 69); w.FillSolid(69, 4, 0, 69, 12, 69);
                // land at 4..7 west of the lowland, the pond dug into it; the lowland x 32..48 at floor 4 (open above 3)
                w.FillSolid(1, 4, 1, 68, 7, 32);
                int pond = 0, top = 0;
                for (int x = 26; x <= 37; x++) for (int z = 24; z <= 31; z++) for (int y = 6; y <= 7; y++)
                { w.SetSolid(x, y, z, false); w.SetFluid(x, y, z, fresh); pond++; if (y == 7) top++; }
                var sim = new Sim(w, RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1"), 1);
                var g = new GameWorld(sim);
                g.Config.Flooding = true;
                int Pond() { int n = 0; for (int x = 26; x <= 37; x++) for (int z = 24; z <= 31; z++) for (int y = 6; y <= 7; y++) if (F.IsSource(w.Get(x, y, z))) n++; return n; }
                int Low() { int n = 0; for (int x = 1; x <= 68; x++) for (int z = 33; z <= 68; z++) for (int y = 4; y <= 7; y++) if (F.IsSource(w.Get(x, y, z))) n++; return n; }
                string name = "loaded land " + shape;
                if (shape == "unloaded-at-break") g.Unloaded = q => q.X < 32;
                sim.BreakBlock(34, 7, 32);
                int moved = H2Release.Opened(g, new BlockPos(34, 7, 32));
                if (shape == "unloaded-at-break")
                {
                    Check(moved == 0 && H2Release.Running == 0 && Pond() == pond, name + $": a pond reaching unloaded land keeps its water ({moved} moved, {Pond()} of {pond} stay)");
                    continue;
                }
                Check(moved == top && H2Release.Running == 1, name + $": the top layer drains ({moved} of {top})");
                if (shape == "saved-while-loading")
                {
                    var data = H2Release.Save();
                    H2Release.Load(data);
                    g.Unloaded = q => q.X < 32;
                    H2Release.Tick(g, 1f);
                    Check(H2Release.Running == 0 && H2Release.Pending == 1 && Pond() == pond, name + $": loaded while the pond's land is still unloaded, the drain waits ({H2Release.Running} running, {H2Release.Pending} waiting)");
                    g.Unloaded = null;
                    H2Release.Tick(g, 1f);
                    Check(H2Release.Running == 0 && H2Release.Pending == 1, name + ": it is not asked again before its wait is over");
                    sim.Run(6000);
                    H2Release.Tick(g, 1f);
                    Check(H2Release.Running == 1 && H2Release.Pending == 0, name + $": once the land has loaded, the drain starts again ({H2Release.Running} running)");
                }
                else
                {
                    g.Unloaded = q => q.Z >= 33;
                    for (int t = 0; t < 5; t++) H2Release.Tick(g, 1f);
                    Check(Pond() == pond && H2Release.Running == 1, name + $": with the lowland unloaded nothing leaves the pond ({Pond()} of {pond})");
                    g.Unloaded = null;
                }
                for (int t = 0; t < 120 && H2Release.Running > 0; t++) H2Release.Tick(g, 1f);
                sim.Settle(600_000, 3_000_000);
                Check(H2Release.Running == 0 && Pond() == pond - top && Low() >= top, name + $": the drain ends, the water stands below ({Pond()} in the pond, {Low()} below)");
                Console.WriteLine($"{name}: {Pond()} in the pond, {Low()} below");
            }
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
        }

        /// <summary>
        /// HY-0019: the drains' and river pools' save round trip. Two drains in two dimensions (fresh and salt water, one with
        /// two openings), a liquid this game may not have, a pool record, and lines that are not one (garbage, a bad number, a
        /// pool without coordinates): the good ones load, the bad ones are skipped, and saving again writes the same records.
        /// </summary>
        static void SaveLoadCases()
        {
            H2Release.ResetWorld();
            string text = string.Join("\n", new[]
            {
                "0 water 0 10,20,30;11,20,30",
                "2 saltwater 3 -5,7,9",
                "0 somemodliquid 1 1,2,3",
                "not a drain at all",
                "0 water x 1,2,3",
                "0 water 0 1,2",
                "pool 0 100,64,200",
                "pool 0 1,2",
                "pool x 1,2,3",
                ""
            });
            H2Release.Load(System.Text.Encoding.UTF8.GetBytes(text));
            Check(H2Release.Pending == 3, $"save/load: three drains load, the broken lines are skipped ({H2Release.Pending})");
            Check(H2DamPools.HasPending, "save/load: the river pool's record loads");
            var again = System.Text.Encoding.UTF8.GetString(H2Release.Save()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Check(again.Length == 5 && again[0] == "hydrology-drains 1", $"save/load: saving again writes the version line and the four records ({again.Length}: {again[0]})");
            Check(Array.Exists(again, l => l == "0 water 0 10,20,30;11,20,30") && Array.Exists(again, l => l == "2 saltwater 3 -5,7,9")
                && Array.Exists(again, l => l == "0 somemodliquid 1 1,2,3") && Array.Exists(again, l => l == "pool 0 100,64,200"),
                "save/load: each record comes back as it was (dimension, liquid, rounds, openings)");
            // Fable's pass (R4): the saved text loads again; a newer format is refused on purpose and said so
            H2Release.Load(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", again)));
            Check(H2Release.Pending == 3 && H2DamPools.HasPending, $"save/load: the versioned save loads again ({H2Release.Pending})");
            string said = null;
            var logBefore = H2Release.Log;
            H2Release.Log = l => said = l;
            H2Release.Load(System.Text.Encoding.UTF8.GetBytes("hydrology-drains 2\n0 water 0 10,20,30\npool 0 1,2,3\n"));
            H2Release.Log = logBefore;
            Check(H2Release.Pending == 0 && !H2DamPools.HasPending && said != null && said.Contains("hydrology-drains 2"),
                $"save/load: a newer format is refused and logged ({said})");
            H2Release.Load(System.Text.Encoding.UTF8.GetBytes(""));
            Check(H2Release.Pending == 0 && !H2DamPools.HasPending && H2Release.Save().Length == 0, "save/load: an empty save loads as nothing");
            H2Release.Load(null);
            Check(H2Release.Pending == 0, "save/load: no save data loads as nothing");
            H2Release.ResetWorld();
        }

        /// <summary>Fable re-review of HY-0014: two remembered searches that share a cell (a T-junction searched from two
        /// branches) are forgotten together: a change beside a cell only the newer search covers also forgets the older
        /// search's other cells.</summary>
        static void LinkedSearchCases()
        {
            H2Filling.ResetWorld();
            var t = typeof(H2Filling);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var remember = t.GetMethod("RememberHeld", flags);
            var live = (System.Collections.Generic.HashSet<int>)t.GetField("liveSearches", flags).GetValue(null);
            var water = Liquids.WaterSource;
            var a = new System.Collections.Generic.HashSet<(int, int, int)> { (0, 5, 0), (1, 5, 0), (2, 5, 0) };
            var b = new System.Collections.Generic.HashSet<(int, int, int)> { (2, 5, 0), (2, 5, 1), (2, 5, 2), (2, 5, 3) };
            long until = long.MaxValue;
            remember.Invoke(null, new object[] { a, 0, water, until, 0 });
            remember.Invoke(null, new object[] { b, 0, water, until, 0 });
            Check(live.Count == 2, $"linked searches: two searches live ({live.Count})");
            // beside (2, 5, 3), a cell only the newer search covers
            H2Filling.ForgetAround(0, 2, 5, 4);
            Check(live.Count == 0, $"linked searches: a change beside the newer one forgets the older one too ({live.Count} live)");
            remember.Invoke(null, new object[] { a, 0, water, until, 0 });
            var c = new System.Collections.Generic.HashSet<(int, int, int)> { (9, 5, 9) };
            remember.Invoke(null, new object[] { c, 0, water, until, 0 });
            H2Filling.ForgetAround(0, 9, 5, 10);
            Check(live.Count == 1, $"linked searches: a change beside an unrelated search leaves the other ({live.Count} live)");
            // the second re-review: a chain of 100,000 searches (search i covers cells i and i + 1) is forgotten from its newest
            // end without overflowing the stack
            H2Filling.ResetWorld();
            for (int i = 0; i < 100000; i++)
            {
                if (i % 50000 == 0) H2Filling.ResetWorld();
                var cells = new System.Collections.Generic.HashSet<(int, int, int)> { (i % 50000, 5, 20), (i % 50000 + 1, 5, 20) };
                remember.Invoke(null, new object[] { cells, 0, water, until, 0 });
            }
            int before = live.Count;
            H2Filling.ForgetAround(0, 50000, 5, 21);
            Check(before >= 50000 && live.Count == 0, $"linked searches: a chain of {before} is forgotten at once, without overflowing the stack ({live.Count} live)");
            // an answer that has run out links nothing
            H2Filling.ResetWorld();
            remember.Invoke(null, new object[] { a, 0, water, 0L, 0 });
            remember.Invoke(null, new object[] { b, 0, water, until, 0 });
            H2Filling.ForgetAround(0, 2, 5, 4);
            Check(live.Count == 1, $"linked searches: a run-out answer is not linked ({live.Count} live)");
            H2Filling.ResetWorld();
        }

        /// <summary>
        /// HY-0019 (c): the production H4 and H7 rules, driven directly on small scenes (the sim's engine still runs
        /// watersim's ports of them; these check the real code). A pool on a ledge beside a 3-block drop: its edge source
        /// turns rapid, and back when the drop is filled; a 1-block step stays ordinary; a lone rapid source is never
        /// reverted; nothing changes while the window's chunks are unloaded; H4's wakes merge within their 40 ms window.
        /// H7: a source over air drains (survival), one on the ground stays; flowing water over air is labelled falling, over
        /// a source not; a neighbour falling in the air feeds no side (and does with the rule off); level relax raises a
        /// cell its feeders allow higher; HeldByBlock reads a floor and the water under a cell.
        /// </summary>
        static void H4H7Cases()
        {
            int src = F.Make(F.Water, F.Still, 7), rapid = F.Make(F.Rapid, F.Still, 7);
            bool wakeBefore = H4Rapids.WakeOnNewSource;
            var beh = new Vintagestory.GameContent.BlockBehaviorFiniteSpreadingLiquid();
            // the ledge (x 0..9 solid to 6), the pool on it (x 5..9, z 3..6, water at 7, walls round it), the floor below (3)
            (World w, Sim sim, GameWorld g) Ledge(int floorTop)
            {
                var w = new World(40, 14, 40);
                w.FillSolid(0, 0, 0, 39, 3, 39);
                w.FillSolid(0, 4, 0, 9, 6, 39);
                if (floorTop > 3) w.FillSolid(10, 4, 0, 39, floorTop, 39);
                w.FillSolid(4, 7, 12, 4, 8, 17); w.FillSolid(5, 7, 12, 9, 8, 12); w.FillSolid(5, 7, 17, 9, 8, 17);
                for (int x = 5; x <= 9; x++) for (int z = 13; z <= 16; z++) w.SetFluid(x, 7, z, src);
                var sim = new Sim(w, RuleSet.Parse(Sweep.RecommendedSpec), 1);
                return (w, sim, new GameWorld(sim));
            }
            Block B(GameWorld g, int x, int y, int z) => g.Fluid(new BlockPos(x, y, z));
            var edge = new BlockPos(9, 7, 14);
            {
                var (w, sim, g) = Ledge(3);
                H4Rapids.CheckSource(g, edge, B(g, 9, 7, 14));
                Check(w.Get(9, 7, 14) == rapid, $"H4: a source beside a 3-block drop turns rapid ({F.Name(w.Get(9, 7, 14))})");
                Check(w.Get(5, 7, 14) == src, "H4: a source away from the edge stays ordinary");
                // the drop filled to the pool's level: the edge goes back to ordinary water (it has sources beside it)
                for (int x = 10; x <= 38; x++) for (int z = 1; z <= 38; z++) for (int y = 4; y <= 7; y++) w.SetFluid(x, y, z, src);
                H4Rapids.CheckSource(g, edge, B(g, 9, 7, 14));
                Check(w.Get(9, 7, 14) == src, $"H4: the drop filled, the edge is ordinary again ({F.Name(w.Get(9, 7, 14))})");
            }
            {
                var (w, sim, g) = Ledge(5);
                H4Rapids.CheckSource(g, edge, B(g, 9, 7, 14));
                Check(w.Get(9, 7, 14) == src, $"H4: a 1-block step stays ordinary ({F.Name(w.Get(9, 7, 14))})");
            }
            {
                var (w, sim, g) = Ledge(3);
                g.Unloaded = q => true;
                H4Rapids.CheckSource(g, new BlockPos(9, 7, 14), B(g, 9, 7, 14));
                Check(w.Get(9, 7, 14) == src, "H4: no change while a chunk column the window reads is unloaded");
                g.Unloaded = null;
            }
            {
                // a lone rapid source (a rivulet spring) on flat ground: never reverted
                var w = new World(20, 10, 20);
                w.FillSolid(0, 0, 0, 19, 3, 19);
                w.SetFluid(10, 4, 10, rapid);
                var sim = new Sim(w, RuleSet.Parse(Sweep.RecommendedSpec), 1);
                var g = new GameWorld(sim);
                H4Rapids.CheckSource(g, new BlockPos(10, 4, 10), B(g, 10, 4, 10));
                Check(w.Get(10, 4, 10) == rapid, "H4: a lone rapid source is not reverted");
            }
            {
                // the wake window: two new sources near the edge within 40 ms wake it once; after the window, again
                var (w, sim, g) = Ledge(3);
                H4Rapids.CheckSource(g, edge, B(g, 9, 7, 14));
                H4Rapids.WakeOnNewSource = true;
                sim.Run(100);
                long w0 = HydrologyStats.EdgeWakes;
                H4Rapids.WakeEdgesOnNewSource(g, new BlockPos(7, 7, 14));
                long w1 = HydrologyStats.EdgeWakes;
                H4Rapids.WakeEdgesOnNewSource(g, new BlockPos(7, 7, 15));
                long w2 = HydrologyStats.EdgeWakes;
                sim.Now += 100; // the clock alone: no update of the sim's may wake the edge meanwhile
                H4Rapids.WakeEdgesOnNewSource(g, new BlockPos(7, 7, 14));
                long w3 = HydrologyStats.EdgeWakes;
                Check(w1 > w0 && w2 == w1 && w3 > w2, $"H4: wakes merge within the window and run again after it ({w1 - w0}, {w2 - w1}, {w3 - w2})");
            }
            H4Rapids.WakeOnNewSource = wakeBefore;
            {
                // H7 on a slab of ground with air beside it
                var w = new World(20, 12, 20);
                w.FillSolid(0, 0, 0, 19, 3, 19);
                w.SetFluid(5, 8, 5, src);                       // a source in the air
                w.SetFluid(8, 4, 8, src);                       // a source on the ground
                w.SetFluid(12, 7, 12, F.Make(F.Water, F.E, 5)); // flowing water in the air
                w.SetFluid(14, 5, 14, src);                     // a source, and flowing water on it
                w.SetFluid(14, 6, 14, F.Make(F.Water, F.E, 5));
                var sim = new Sim(w, RuleSet.Parse(Sweep.RecommendedSpec), 1);
                var g = new GameWorld(sim);
                bool drained = H7Settling.TrySurvival(beh, g, new BlockPos(5, 8, 5), B(g, 5, 8, 5));
                Check(drained && F.Level(w.Get(5, 8, 5)) == 6 && !F.IsSource(w.Get(5, 8, 5)), $"H7: a source in the air drains ({F.Name(w.Get(5, 8, 5))})");
                Check(!H7Settling.TrySurvival(beh, g, new BlockPos(8, 4, 8), B(g, 8, 4, 8)) && w.Get(8, 4, 8) == src, "H7: a source on the ground stays");
                int id = F.Make(F.Water, F.E, 5);
                Check(F.Flow(H7Settling.Label(g, new BlockPos(12, 7, 12), id, 5)) == F.D, "H7: flowing water over air is labelled falling");
                Check(H7Settling.Label(g, new BlockPos(14, 6, 14), id, 5) == id, "H7: flowing water over a source keeps its label");
                Check(H7Settling.HeldByBlock(g, new BlockPos(8, 4, 8), out _), "H7: the ground holds a cell up");
                Check(!H7Settling.HeldByBlock(g, new BlockPos(14, 6, 14), out var under) && under.Id == src, "H7: over water, the water under the cell is read");
            }
            {
                // side feed: a cell between a neighbour falling in the air (level 6) and one resting on the ground (level 4)
                var w = new World(20, 12, 20);
                w.FillSolid(0, 0, 0, 19, 3, 19);
                w.FillSolid(9, 4, 9, 11, 4, 11);                          // a platform the middle cell rests on
                w.SetSolid(9, 4, 10, false); w.SetSolid(11, 4, 10, false); // ...but not under its two side neighbours' columns
                w.SetFluid(10, 5, 10, F.Make(F.Water, F.E, 3));
                w.SetFluid(9, 5, 10, F.Make(F.Water, F.D, 6));            // falling in the air (nothing under it to 3)
                w.SetFluid(10, 5, 11, F.Make(F.Water, F.E, 4));           // resting on the platform
                var sim = new Sim(w, RuleSet.Parse(Sweep.RecommendedSpec), 1);
                var g = new GameWorld(sim);
                var cell = new BlockPos(10, 5, 10);
                bool rule = H7Settling.NoSideFeedFromFalling;
                H7Settling.NoSideFeedFromFalling = true;
                int with = H7Settling.MaxNeighbourLevel(B(g, 10, 5, 10), g, cell, out _);
                H7Settling.NoSideFeedFromFalling = false;
                int without = H7Settling.MaxNeighbourLevel(B(g, 10, 5, 10), g, cell, out _);
                H7Settling.NoSideFeedFromFalling = rule;
                Check(with == 4 && without == 6, $"H7: a neighbour falling in the air feeds no side ({with}), and does with the rule off ({without})");
                // level relax: the cell (3) beside a resting 4 may stand at 3, beside a source at 6
                w.SetFluid(10, 5, 9, src);
                w.SetSolid(10, 4, 9, true);
                bool relaxed = H7Settling.TryRelax(beh, g, cell, B(g, 10, 5, 10), null);
                Check(relaxed && F.Level(w.Get(10, 5, 10)) == 6, $"H7: level relax raises a cell to what its feeders allow ({F.Name(w.Get(10, 5, 10))})");
            }
        }

        /// <summary>
        /// Fable's pass (critical): a river pool planned over cells a drain is emptying looped for ever on the main thread.
        /// The two-pool river of RiverDamCases with a 1-high dam built by hand; its top course broken (a drain at 1 source a
        /// second, far cells first); six seconds later a block placed beside the pool. Run on a thread with a time limit: the
        /// step must come back, and no pool may stand over the draining cells.
        /// </summary>
        static void PoolOverDrainCase()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = true;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            H2Release.DrainRate = 0;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
            int fresh = F.Make(F.Water, F.Still, 7);
            const int z0 = 37, z1 = 42, damX = 65, crest = 9;
            int Surface(int x) => x <= 40 ? 9 : 8;
            var w = new World(160, 16, 100);
            w.FillSolid(0, 0, 0, 159, 10, 99);
            for (int x = 1; x <= 90; x++) for (int z = z0; z <= z1; z++)
                for (int y = Surface(x) - 2; y <= 10; y++) { w.SetSolid(x, y, z, false); if (y <= Surface(x)) w.SetFluid(x, y, z, fresh); }
            w.FillSolid(91, 3, 1, 158, 10, 98, false);
            EarthRiverQuery.Active = true;
            EarthRiverQuery.IsRiver = q => q.X >= 1 && q.X <= 90 && q.Z >= z0 && q.Z <= z1;
            EarthRiverQuery.Surface = q => Surface(q.X);
            var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
            r.WakeOnNewSource = true;
            var sim = new Sim(w, r, 1);
            var g = new GameWorld(sim);
            g.Config.Flooding = true;
            sim.WakeAll();
            sim.Settle(600_000, 3_000_000);
            for (int y = 6; y <= crest; y++)
                for (int z = z0; z <= z1; z++)
                {
                    sim.PlaceStone(damX, y, z);
                    H2Release.Placed(g, new BlockPos(damX, y, z));
                    sim.Run(100);
                }
            H2DamPools.RunAll(g);
            sim.Settle(600_000, 3_000_000);
            H2Release.DrainRate = 1;
            sim.BreakBlock(damX, crest, 39);
            int moved = H2Release.Opened(g, new BlockPos(damX, crest, 39));
            bool done = false;
            var t = new System.Threading.Thread(() =>
            {
                for (int i = 0; i < 6; i++) { H2Release.Tick(g, 1f); H2DamPools.Tick(g, 1f); sim.Run(500); }
                sim.PlaceStone(50, 10, 39);
                H2Release.Placed(g, new BlockPos(50, 10, 39));
                for (int i = 0; i < 3; i++) { H2Release.Tick(g, 1f); H2DamPools.Tick(g, 1f); sim.Run(500); }
                done = true;
            }, 64 * 1024 * 1024);
            t.IsBackground = true;
            t.Start();
            bool returned = t.Join(60_000);
            Check(moved > 0, $"pool over a drain: the dam's top course lets the raised water out ({moved})");
            Check(returned && done, "pool over a drain: a block placed beside a draining pool does not hang the tick");
            if (!returned)
            {
                Console.WriteLine("pool over a drain: the tick did not come back within 60 s; stopping the suite");
                Console.WriteLine($"{checks - failures}/{checks} checks passed; {failures} failed");
                Environment.Exit(1);
            }
            Console.WriteLine($"pool over a drain: {moved} let out, the ticks came back, {H2DamPools.Running} pools filling, {H2Release.Running} drains");
            EarthRiverQuery.Surface = null;
            EarthRiverQuery.IsRiver = _ => false;
            EarthRiverQuery.Active = false;
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
        }

        /// <summary>Fable's pass (F3): a block broken on the shore of a sea of 90,000 sources measured the whole body (to its
        /// 65,536-source cap) every time; a body found large is remembered for 30 s, so the next break beside it is answered
        /// at once.</summary>
        static void LargeBodyCase()
        {
            H2Filling.Enabled = true;
            H2Release.Enabled = true;
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
            EarthRiverQuery.Active = false;
            int salt = F.Make(F.Salt, F.Still, 7);
            var w = new World(310, 8, 310);
            w.FillSolid(0, 0, 0, 309, 4, 309);
            for (int x = 2; x <= 301; x++) for (int z = 2; z <= 301; z++) { w.SetSolid(x, 4, z, false); w.SetFluid(x, 4, z, salt); }
            var sim = new Sim(w, RuleSet.Parse(Sweep.RecommendedSpec), 1);
            var g = new GameWorld(sim);
            sim.BreakBlock(302, 4, 100);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int first = H2Release.Opened(g, new BlockPos(302, 4, 100));
            double ms1 = sw.Elapsed.TotalMilliseconds;
            sim.BreakBlock(302, 4, 101);
            sw.Restart();
            int second = H2Release.Opened(g, new BlockPos(302, 4, 101));
            double ms2 = sw.Elapsed.TotalMilliseconds;
            Check(first == 0 && second == 0, "large body: the sea keeps its water");
            Check(ms2 < ms1 / 5, $"large body: the second break beside it is answered from memory ({ms1:0.0} ms, then {ms2:0.00} ms)");
            Console.WriteLine($"large body: first break {ms1:0.0} ms, second {ms2:0.00} ms");
            sim.Now += 31000;
            sim.BreakBlock(302, 4, 102);
            sw.Restart();
            H2Release.Opened(g, new BlockPos(302, 4, 102));
            double ms3 = sw.Elapsed.TotalMilliseconds;
            Check(ms3 > ms2, $"large body: after 30 s it is measured again ({ms3:0.0} ms)");
            // the reviewer's re-review (high): through the driver, as in the game, a second shore break is answered from memory
            // (forgetting on breaks walked the sea at every break)
            {
                var none = new FakeFaults();
                var driver = new ReleaseDriver(none, new FakeFaults(), () => true, () => true, _ => { }, _ => { });
                sim.Now += 31000;
                sim.BreakBlock(302, 4, 110);
                driver.OnBreak(g, new BlockPos(302, 4, 110));
                sw.Restart();
                sim.BreakBlock(302, 4, 111);
                driver.OnBreak(g, new BlockPos(302, 4, 111));
                double d2 = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                sim.BreakBlock(302, 4, 112);
                driver.OnBreak(g, new BlockPos(302, 4, 112));
                double d3 = sw.Elapsed.TotalMilliseconds;
                Check(d2 < ms1 / 5 && d3 < ms1 / 5, $"large body: shore breaks through the driver are answered from memory ({d2:0.00}, {d3:0.00} ms; first {ms1:0.0})");
                // a block placed beside it forgets it
                sim.PlaceStone(302, 4, 113);
                driver.OnPlace(g, new BlockPos(302, 4, 113));
                sw.Restart();
                sim.BreakBlock(302, 4, 114);
                driver.OnBreak(g, new BlockPos(302, 4, 114));
                double d4 = sw.Elapsed.TotalMilliseconds;
                Check(d4 > d3 * 5, $"large body: a block placed beside it forgets it ({d4:0.0} ms)");
            }
            // Fable's re-review (low 6): a player's change beside a remembered body forgets it at once
            sim.BreakBlock(302, 4, 103);
            H2Release.Opened(g, new BlockPos(302, 4, 103));
            sw.Restart();
            sim.BreakBlock(302, 4, 104);
            H2Release.Opened(g, new BlockPos(302, 4, 104));
            double ms4 = sw.Elapsed.TotalMilliseconds;
            H2Release.ForgetLargeBodyNear(0, 302, 4, 105);
            sw.Restart();
            sim.BreakBlock(302, 4, 105);
            H2Release.Opened(g, new BlockPos(302, 4, 105));
            double ms5 = sw.Elapsed.TotalMilliseconds;
            Check(ms5 > ms4 * 5, $"large body: a player's change beside it forgets it ({ms4:0.00} ms remembered, {ms5:0.0} ms measured again)");
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
        }

        sealed class FakeFaults : ReleaseDriver.IFaults
        {
            public bool off; public int faults;
            public bool Off => off;
            public void Fault(Exception e) { faults++; }
        }

        /// <summary>
        /// Fable's pass (R2): the break, place and tick policy the game runs, driven offline. A dam's top course broken through
        /// the driver starts a drain; ticks run it; with the release breaker off a break starts nothing, and the next tick ends
        /// the running drain once (logged) and keeps it for the save; the rules off do the same; Earth's query unbound at run
        /// time is answered once on the next tick; with H2's fill off in the config nothing runs.
        /// </summary>
        static void DriverCase()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = false;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
            EarthRiverQuery.Active = true;
            EarthRiverQuery.IsRiver = _ => false;
            int fresh = F.Make(F.Water, F.Still, 7);
            (World w, Sim sim, GameWorld g) Scene()
            {
                // the trench and 2-high dam of DamCases
                var w = new World(60, 12, 10);
                w.FillSolid(0, 0, 0, 59, 7, 9);
                for (int x = 1; x <= 38; x++) for (int z = 4; z <= 5; z++) for (int y = 4; y <= 7; y++) w.SetSolid(x, y, z, x == 20 && y <= 5);
                for (int x = 39; x <= 58; x++) for (int z = 1; z <= 8; z++) for (int y = 2; y <= 7; y++) w.SetSolid(x, y, z, false);
                for (int x = 1; x <= 19; x++) for (int z = 4; z <= 5; z++) for (int y = 4; y <= 5; y++) w.SetFluid(x, y, z, fresh);
                for (int x = 21; x <= 38; x++) for (int z = 4; z <= 5; z++) w.SetFluid(x, 4, z, fresh);
                var sim = new Sim(w, RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1"), 1);
                return (w, sim, new GameWorld(sim));
            }
            var release = new FakeFaults();
            var retries = new FakeFaults();
            bool fill = true, rules = true;
            var warned = new System.Collections.Generic.List<string>();
            var driver = new ReleaseDriver(release, retries, () => fill, () => rules, l => warned.Add(l), l => warned.Add(l));
            int unbound = 0;
            driver.Unbound = _ => unbound++;
            var (w, sim, g) = Scene();
            sim.BreakBlock(20, 5, 4);
            driver.OnBreak(g, new BlockPos(20, 5, 4));
            Check(H2Release.Running == 1, $"driver: a break through the driver starts the drain ({H2Release.Running})");
            for (int i = 0; i < 3; i++) { driver.OnTick(g, 0.5f); sim.Run(500); }
            Check(H2Release.Running == 1 && warned.Count == 0, "driver: ticks run the drain");
            release.off = true;
            sim.BreakBlock(20, 5, 5);
            driver.OnBreak(g, new BlockPos(20, 5, 5));
            Check(!driver.On && H2Release.Running == 1, "driver: with the release breaker off a break starts nothing");
            driver.OnTick(g, 0.5f);
            Check(H2Release.Running == 0 && H2Release.Pending == 1 && warned.Count == 1 && warned[0].Contains("1 pond drains"),
                $"driver: the next tick ends the drain once, logged, and keeps it for the save ({H2Release.Running} running, {H2Release.Pending} kept, {warned.Count} lines)");
            driver.OnTick(g, 0.5f);
            Check(warned.Count == 1, "driver: ending is logged once");
            // the water rules off: the same
            release.off = false;
            H2Release.ResetWorld();
            (w, sim, g) = Scene();
            sim.BreakBlock(20, 5, 4);
            driver.OnBreak(g, new BlockPos(20, 5, 4));
            rules = false;
            driver.OnTick(g, 0.5f);
            Check(H2Release.Running == 0 && warned.Count == 2, $"driver: the water rules off end the drain too ({H2Release.Running}, {warned.Count} lines)");
            rules = true;
            // Earth's query unbound at run time: answered once
            EarthRiverQuery.UnboundPending = true;
            driver.OnTick(g, 0.5f);
            driver.OnTick(g, 0.5f);
            Check(unbound == 1, $"driver: an unbound query is answered once ({unbound})");
            // H2's fill off in the config: a break starts nothing
            fill = false;
            H2Release.ResetWorld();
            (w, sim, g) = Scene();
            sim.BreakBlock(20, 5, 4);
            driver.OnBreak(g, new BlockPos(20, 5, 4));
            Check(H2Release.Running == 0, "driver: with H2's fill off nothing runs");
            Check(release.faults == 0 && retries.faults == 0, "driver: no errors on the way");
            EarthRiverQuery.Active = false;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
        }

        /// <summary>Decision 0011 (option 3): a world without the Water flooding setting gets it written with the legacy
        /// policy's answer (off and on), and a second start leaves it alone; an explicit setting is never touched.</summary>
        static void FloodingKeyCase()
        {
            bool vesselsBefore = H2Filling.Vessels, mayBefore = H2Filling.WorldMayFillVessels;
            H2Filling.ResetWorld();
            H2Filling.Vessels = false;
            H2Filling.WorldMayFillVessels = true;
            EarthRiverQuery.Active = false;
            var g = new GameWorld(new Sim(new World(4, 4, 4), RuleSet.Parse(Sweep.RecommendedSpec), 1));
            // the legacy answer off: the world asks for nothing
            g.Config.Flooding = null; g.Config.Vessels = false; g.Config.VesselsWithQuery = false;
            var w1 = H2Filling.WriteMissingFlooding(g);
            Check(w1 == false && g.Config.Flooding == false, $"flooding key: a world without it gets the legacy answer, off ({w1})");
            g.Config.Vessels = true; // the world's legacy flag changes later: the written setting stands
            var w2 = H2Filling.WriteMissingFlooding(g);
            Check(w2 == null && g.Config.Flooding == false, "flooding key: a second start leaves the written setting alone");
            // the legacy answer on: an old save that asked for vessels
            H2Filling.ResetWorld();
            g.Config.Flooding = null; g.Config.Vessels = true;
            var w3 = H2Filling.WriteMissingFlooding(g);
            Check(w3 == true && g.Config.Flooding == true && H2Filling.VesselsActive(g), $"flooding key: an old save that asked for vessels gets on, and keeps them ({w3})");
            // Fable's re-review (low 7): a world that asks for vessels only with the query, at a start without it, is not written;
            // at a start with it, on is written
            H2Filling.ResetWorld();
            g.Config.Flooding = null; g.Config.Vessels = false; g.Config.VesselsWithQuery = true;
            EarthRiverQuery.Active = false;
            var w4 = H2Filling.WriteMissingFlooding(g);
            Check(w4 == null && g.Config.Flooding == null, $"flooding key: a query-dependent world is not written while the query is unbound ({w4})");
            EarthRiverQuery.Active = true;
            H2Filling.ResetWorld();
            var w5 = H2Filling.WriteMissingFlooding(g);
            Check(w5 == true && g.Config.Flooding == true, $"flooding key: ...and gets on at a start with the query ({w5})");
            EarthRiverQuery.Active = false;
            g.Config.VesselsWithQuery = false;
            // an explicit setting is never touched
            H2Filling.ResetWorld();
            g.Config.Flooding = false; g.Config.Vessels = true;
            Check(H2Filling.WriteMissingFlooding(g) == null && g.Config.Flooding == false, "flooding key: an explicit setting is left alone");
            H2Filling.Vessels = vesselsBefore;
            H2Filling.WorldMayFillVessels = mayBefore;
            H2Filling.ResetWorld();
        }

        /// <summary>
        /// Fable's re-review (medium, low 2): a calm reach 219 long dammed at x 220 while the land beyond x 160 upstream is
        /// unloaded (the 3 x 3 chunks round the dam are loaded): the dam waits instead of starting a pool over a part of the
        /// river, logs its wait once however often it is asked, and once the land loads the pool fills the whole layer, as
        /// with all of it loaded.
        /// </summary>
        static void LongPoolCase()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = true;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            H2Release.DrainRate = 0;
            int fresh = F.Make(F.Water, F.Still, 7);
            const int z0 = 37, z1 = 42, damX = 220, crest = 9;
            int Raised(World w) { int n = 0; for (int x = 1; x < damX; x++) for (int z = z0; z <= z1; z++) if (F.IsSource(w.Get(x, crest, z))) n++; return n; }
            (World w, Sim sim, GameWorld g) Scene()
            {
                H2Release.ResetWorld();
                H2Filling.ResetWorld();
                var w = new World(300, 16, 80);
                w.FillSolid(0, 0, 0, 299, 10, 79);
                for (int x = 1; x <= 260; x++) for (int z = z0; z <= z1; z++)
                    for (int y = 6; y <= 10; y++) { w.SetSolid(x, y, z, false); if (y <= 8) w.SetFluid(x, y, z, fresh); }
                w.FillSolid(261, 3, 1, 298, 10, 78, false);
                EarthRiverQuery.Active = true;
                EarthRiverQuery.IsRiver = q => q.X >= 1 && q.X <= 260 && q.Z >= z0 && q.Z <= z1;
                EarthRiverQuery.Surface = q => 8;
                var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
                var sim = new Sim(w, r, 1);
                var g = new GameWorld(sim);
                g.Config.Flooding = true;
                return (w, sim, g);
            }
            void Dam(Sim sim, GameWorld g)
            {
                for (int y = 6; y <= crest; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        sim.PlaceStone(damX, y, z);
                        H2Release.Placed(g, new BlockPos(damX, y, z));
                    }
            }
            // control: all loaded
            var (w, sim, g) = Scene();
            Dam(sim, g);
            H2DamPools.RunAll(g);
            int control = Raised(w);
            Check(control == (damX - 1) * (z1 - z0 + 1), $"long pool: with all its land loaded the layer over the river fills ({control})");
            // the land upstream of x 160 unloaded
            (w, sim, g) = Scene();
            var lines = new System.Collections.Generic.List<string>();
            var logBefore = H2DamPools.Log;
            H2DamPools.Log = l => lines.Add(l);
            g.Unloaded = q => q.X < 160;
            Dam(sim, g);
            for (int i = 0; i < 10; i++) { sim.Now += 500; H2DamPools.Tick(g, 0.5f); }
            Check(H2DamPools.Running == 1 && Raised(w) == 0,
                $"long pool: with the river running on into unloaded land the pool starts partial and waits ({H2DamPools.Running} filling, {Raised(w)} raised)");
            for (int i = 0; i < 6; i++) { sim.Now += 6000; H2DamPools.Tick(g, 0.5f); }
            int waitLines = lines.FindAll(l => l.Contains("waits")).Count;
            Check(waitLines == 0 && H2DamPools.Running == 1 && Raised(w) == 0,
                $"long pool: ticked while the land is unloaded, it keeps waiting, quietly ({waitLines} wait lines, {Raised(w)} raised)");
            // the reviewer's re-review: five minutes more on unloaded land (the pool cycles between waiting and starting)
            // logs its start and its stop once, not two lines a minute
            for (int i = 0; i < 600; i++) { sim.Now += 500; H2DamPools.Tick(g, 0.5f); }
            int starts = lines.FindAll(l => l.Contains("backs up")).Count, stops = lines.FindAll(l => l.Contains("stops filling")).Count;
            Check(starts == 1 && stops <= 1 && Raised(w) == 0, $"long pool: five minutes on unloaded land log one start and at most one stop ({starts}, {stops})");
            g.Unloaded = null;
            sim.Now += 6000;
            H2DamPools.Tick(g, 0.5f);
            H2DamPools.RunAll(g);
            H2DamPools.Log = logBefore;
            int after = Raised(w);
            Check(!H2DamPools.HasPending && H2DamPools.Running == 0 && after == control,
                $"long pool: once the land loads it marks the rest and fills the whole layer ({after} of {control})");
            Console.WriteLine($"long pool: control {control}, after loading {after}, {waitLines} wait lines");
            EarthRiverQuery.Surface = null;
            EarthRiverQuery.IsRiver = _ => false;
            EarthRiverQuery.Active = false;
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
        }

        /// <summary>
        /// The reviewer's re-review (medium): three river pools 3 deep, stepping down (A2 x 1..40 at 10, A 41..80 at 9, B
        /// 81..130 at 8), a dam in B at x 110 with its crest at 9, and the far end of A2 (x below 20) unloaded. The dam's mark
        /// walk meets the unloaded water upstream, but the layers it fills are closed by A2's own water long before that: the
        /// pool starts, fills and runs over its crest as with all the land loaded (waiting for all of it kept the dam from
        /// ever starting).
        /// </summary>
        static void StepsUpstreamCase()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = true;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            H2Release.DrainRate = 0;
            int fresh = F.Make(F.Water, F.Still, 7);
            const int z0 = 37, z1 = 42, damX = 110, crest = 9;
            int Surface(int x) => x <= 40 ? 10 : x <= 80 ? 9 : 8;
            (World w, Sim sim, GameWorld g, int raised) Run(bool partlyUnloaded)
            {
                H2Release.ResetWorld();
                H2Filling.ResetWorld();
                var w = new World(200, 16, 80);
                w.FillSolid(0, 0, 0, 199, 12, 79);
                for (int x = 1; x <= 160; x++) for (int z = z0; z <= z1; z++)
                    for (int y = Surface(x) - 2; y <= 12; y++) { w.SetSolid(x, y, z, false); if (y <= Surface(x)) w.SetFluid(x, y, z, fresh); }
                w.FillSolid(161, 3, 1, 198, 12, 78, false);
                EarthRiverQuery.Active = true;
                EarthRiverQuery.IsRiver = q => q.X >= 1 && q.X <= 160 && q.Z >= z0 && q.Z <= z1;
                EarthRiverQuery.Surface = q => Surface(q.X);
                var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
                var sim = new Sim(w, r, 1);
                var g = new GameWorld(sim);
                g.Config.Flooding = true;
                if (partlyUnloaded) g.Unloaded = q => q.X < 20;
                int before = (int)HydrologyStats.DamSources;
                for (int y = Surface(damX) - 2; y <= crest; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        sim.PlaceStone(damX, y, z);
                        H2Release.Placed(g, new BlockPos(damX, y, z));
                    }
                H2DamPools.RunAll(g);
                for (int i = 0; i < 4; i++) { sim.Now += 6000; H2DamPools.Tick(g, 1f); H2DamPools.RunAll(g); }
                return (w, sim, g, (int)HydrologyStats.DamSources - before);
            }
            var lines = new System.Collections.Generic.List<string>();
            var logBefore = H2DamPools.Log;
            H2DamPools.Log = l => lines.Add(l);
            var (_, _, _, control) = Run(false);
            bool controlOver = lines.Exists(l => l.Contains("running over its crest"));
            lines.Clear();
            var (_, _, g2, partial) = Run(true);
            bool partialOver = lines.Exists(l => l.Contains("running over its crest"));
            H2DamPools.Log = logBefore;
            Check(control > 0 && controlOver, $"steps upstream: with all the land loaded the dam's pool runs over its crest ({control} raised)");
            Check(partial == control && partialOver && H2DamPools.Running == 0 && !H2DamPools.HasPending,
                $"steps upstream: with the far end of the pools upstream unloaded it does the same ({partial} of {control}; {string.Join(" | ", lines)})");
            g2.Unloaded = null;
            EarthRiverQuery.Surface = null;
            EarthRiverQuery.IsRiver = _ => false;
            EarthRiverQuery.Active = false;
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
        }

        /// <summary>HY-0010: what H2Release allocates and takes per call (printed, with loose bounds): a block placed on dry
        /// land, a block placed beside a pond, a pond opened onto land at its level (kept), a pond opened over a lowland of
        /// small pools (planned, then dropped).</summary>
        static void AllocCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = false;
            H2Release.Enabled = true;
            H2Release.DrainRate = 1;
            EarthRiverQuery.Active = false;
            int fresh = F.Make(F.Water, F.Still, 7);
            foreach (var shape in new[] { "plateau", "pools" })
            {
                H2Release.ResetWorld();
                H2Filling.ResetWorld();
                var w = new World(50, 14, 30);
                w.FillSolid(0, 0, 0, 49, 3, 29);
                w.FillSolid(0, 4, 0, 49, 12, 0); w.FillSolid(0, 4, 29, 49, 12, 29);
                w.FillSolid(0, 4, 0, 0, 12, 29); w.FillSolid(49, 4, 0, 49, 12, 29);
                int x0 = 3, x1 = 11, z0 = 11, z1 = 19;
                if (shape == "plateau") w.FillSolid(1, 4, 1, 30, 6, 28);
                else
                    // single-cell pools in the lowland, each walled off from the next
                    for (int x = 13; x <= 47; x += 2) for (int z = 2; z <= 27; z += 2)
                    { w.FillSolid(x - 1, 4, z - 1, x + 1, 4, z + 1); w.SetSolid(x, 4, z, false); w.SetFluid(x, 4, z, fresh); }
                w.FillSolid(x0, 4, z0, x1, 7, z1);
                for (int x = x0 + 1; x < x1; x++) for (int z = z0 + 1; z < z1; z++) for (int y = 6; y <= 7; y++)
                { w.SetSolid(x, y, z, false); w.SetFluid(x, y, z, fresh); }
                var sim = new Sim(w, RuleSet.Parse(Sweep.RecommendedSpec), 1);
                var g = new GameWorld(sim);
                g.Config.Flooding = true;
                sim.BreakBlock(x1, 7, 15);
                const int n = 400;
                long Measure(Func<int> call, out double us)
                {
                    call();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < n; i++) call();
                    long bytes = (GC.GetAllocatedBytesForCurrentThread() - b0) / n;
                    us = sw.Elapsed.TotalMilliseconds * 1000 / n;
                    return bytes;
                }
                var open = new BlockPos(x1, 7, 15);
                int got = 0;
                long ob = Measure(() => { got = H2Release.Opened(g, open); H2Release.ResetWorld(); return got; }, out double ous);
                Check(shape == "plateau" ? got == 0 : got > 0, $"alloc {shape}: the opening {(shape == "plateau" ? "keeps" : "drains")} ({got})");
                var dry = new BlockPos(40, 13, 5);
                long db = Measure(() => H2Release.Placed(g, dry), out double dus);
                var wet = new BlockPos(x0 + 1, 8, z0 + 1);
                long wb = Measure(() => H2Release.Placed(g, wet), out double wus);
                Console.WriteLine($"alloc {shape}: Opened {ob} B {ous:0.0} us; Placed dry {db} B {dus:0.00} us; Placed by the pond {wb} B {wus:0.0} us");
                Check(db == 0, $"alloc {shape}: a block placed away from water allocates almost nothing ({db} B)");
            }
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
        }

        static void ReleaseCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = false;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            EarthRiverQuery.Active = true;
            int fresh = F.Make(F.Water, F.Still, 7);
            foreach (var shape in new[] { "hollow", "flat", "lake", "river", "ledge", "plateau", "hollow-rate", "hollow-closed" })
            {
                // rc.9: the drain runs over time ("hollow-rate"); the other shapes let it all out at once (rc.8)
                bool rate = shape == "hollow-rate" || shape == "hollow-closed";
                H2Release.DrainRate = rate ? 1 : 0;
                H2Release.ResetWorld();
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
                if (rate)
                {
                    // two opened cells, one block of water over their floor: 2 sources a second
                    Check(H2Release.Running == 1, name + ": one drain runs for both openings");
                    H2Release.Tick(g, 1f);
                    sim.Settle(600_000, 3_000_000);
                    int topNow = 0;
                    for (int x = x0 + 1; x < x1; x++) for (int z = z0 + 1; z < z1; z++) if (F.IsSource(w.Get(x, 7, z))) topNow++;
                    Check(topNow == top - 2, name + $": after a second, 2 of the top layer have gone and stay gone ({topNow} of {top} stand)");
                    if (shape == "hollow-closed")
                    {
                        // the player closes both openings again: the drain stops, what has gone stays gone
                        w.SetSolid(x1, 7, 15, true); w.SetSolid(x1, 7, 16, true);
                        H2Release.Tick(g, 1f);
                        Check(H2Release.Running == 0, name + ": closing the openings ends the drain");
                        sim.Settle(600_000, 3_000_000);
                        int left = 0;
                        for (int x = x0 + 1; x < x1; x++) for (int z = z0 + 1; z < z1; z++) if (F.IsSource(w.Get(x, 7, z))) left++;
                        int below = 0;
                        for (int x = 1; x <= 48; x++) for (int z = 1; z <= 28; z++) if (!(x >= x0 && x <= x1 && z >= z0 && z <= z1) && F.IsSource(w.Get(x, 4, z))) below++;
                        Check(left == 0 && below == top, name + $": the layer it was draining goes all the same, no more ({left} stand, {below} below)");
                        Console.WriteLine($"{name}: {left} of {top} stand in the top layer after the openings close, {below} below");
                        continue;
                    }
                    for (int t = 0; t < 30 && H2Release.Running > 0; t++) H2Release.Tick(g, 1f);
                    Check(H2Release.Running == 0, name + ": the drain ends within 30 s");
                }
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
                    case "hollow-rate":
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
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
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
                // rc.11: with Earth's span (its RiverSpanAt), as on every Earth 2.0 world
                EarthRiverQuery.Surface = p => reach.C.WaterTop[reach.C.K(p.X, p.Z)];
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
            EarthRiverQuery.Surface = null;
        }

#if !BASELINE
        /// <summary>
        /// rc.11 (EA-0066, decision 0009): a dam across a natural river. The river: pool A (x 1..40, surface 9) steps down to
        /// pool B (x 41..90, surface 8), 6 wide (z 37..42), banks at 10; B runs out over its end into a lowland too wide to be held (68 x 98, floor 2),
        /// as an Earth river runs on to the sea. A dam in B at x 65, from the bed to one block over the surface, built a block at
        /// a time: B behind the dam rises to the dam's top, which is A's level; B below the dam and the lowland keep what they
        /// had. Taking the dam's top course away lets the raised water go and the river is back at its own levels. Without a
        /// player's block nothing rises (the regressions above wake every cell of a river whose end is closed).
        /// </summary>
        static void RiverDamCases()
        {
            H2Filling.Enabled = true;
            H2Filling.Vessels = true;
            H2Filling.SupplyFactor = 2;
            H2Release.Enabled = true;
            H2Release.DrainRate = 0;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
            int fresh = F.Make(F.Water, F.Still, 7);
            const int z0 = 37, z1 = 42, damX = 65;
            foreach (int crestCase in new[] { 9, 10, -9 })
            {
            // -9: crest 9 with a strip of bank open at 9 on both sides from x 30 to 64 (rc.12: in game, the strip between a
            // levee and the Rhine filled to the upper pool's level beside it, and the dam's water then never drained)
            bool levee = crestCase < 0;
            int crest = System.Math.Abs(crestCase);
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
            int Surface(int x) => x <= 40 ? 9 : 8;
            var w = new World(160, 16, 100);
            w.FillSolid(0, 0, 0, 159, 10, 99);
            for (int x = 1; x <= 90; x++) for (int z = z0; z <= z1; z++)
                for (int y = Surface(x) - 2; y <= 10; y++) { w.SetSolid(x, y, z, false); if (y <= Surface(x)) w.SetFluid(x, y, z, fresh); }
            w.FillSolid(91, 3, 1, 158, 10, 98, false);
            if (levee) for (int x = 30; x <= 64; x++) { w.SetSolid(x, 9, z0 - 1, false); w.SetSolid(x, 9, z1 + 1, false); }
            EarthRiverQuery.Active = true;
            EarthRiverQuery.IsRiver = q => q.X >= 1 && q.X <= 90 && q.Z >= z0 && q.Z <= z1;
            EarthRiverQuery.Surface = q => Surface(q.X);
            var r = RuleSet.Parse(Sweep.RecommendedSpec + ",FillVessels=1,VesselsDepth=1,VesselsContained=1");
            r.WakeOnNewSource = true;
            var sim = new Sim(w, r, 1);
            var g = new GameWorld(sim);
            g.Config.Flooding = true;
            sim.WakeAll();
            Check(sim.Settle(600_000, 3_000_000), "river dam: the river settles");
            int Raised(int xa, int xb)
            {
                int n = 0;
                for (int x = xa; x <= xb; x++) for (int z = z0 - 1; z <= z1 + 1; z++) for (int y = Surface(x) + 1; y < w.SY; y++) if (F.IsSource(w.Get(x, y, z))) n++;
                return n;
            }
            int Lowland()
            {
                int n = 0;
                for (int x = 91; x <= 158; x++) for (int z = 1; z <= 98; z++) for (int y = 3; y <= 10; y++) if (F.IsSource(w.Get(x, y, z))) n++;
                return n;
            }
            int River()
            {
                int n = 0;
                for (int x = 1; x <= 90; x++) for (int z = z0; z <= z1; z++) for (int y = Surface(x) - 2; y <= Surface(x); y++) if (x != damX && F.IsSource(w.Get(x, y, z))) n++;
                return n;
            }
            int river0 = River(), lowland0 = Lowland();
            Check(Raised(1, 90) == 0, $"river dam: nothing stands over the river before the dam ({Raised(1, 90)})");
            // the dam, a block at a time from the bed up, as a player builds it
            for (int y = 6; y <= crest; y++)
                for (int z = z0; z <= z1; z++)
                {
                    sim.PlaceStone(damX, y, z);
                    H2Release.Placed(g, new BlockPos(damX, y, z));
                    sim.Run(200);
                }
            Check(sim.Settle(600_000, 3_000_000), "river dam: settles with the dam");
            if (crest == 10)
            {
                // two blocks over the surface: the pool rises to the water that feeds it (pool A's level) and no higher, as
                // a dug reach rises only as high as the water it is fed from
                int up2 = Raised(41, damX - 1), over = 0, a2 = Raised(1, 40);
                for (int x = 41; x < damX; x++) for (int z = z0; z <= z1; z++) if (F.IsSource(w.Get(x, 10, z))) over++;
                Check(up2 == (damX - 41) * (z1 - z0 + 1) && over == 0 && a2 == 0, $"river dam 2 high: the pool rises to pool A's level, no higher ({up2} raised, {over} at the crest, {a2} in pool A)");
                Console.WriteLine($"river dam 2 high: {up2} raised behind the dam, {over} at its crest, {a2} in pool A");
                continue;
            }
            int up = Raised(41, damX - 1), down = Raised(damX + 1, 90), above = Raised(1, 40);
            int reach = (damX - 41) * (z1 - z0 + 1);
            Check(levee ? up >= reach : up == reach, $"river dam: the pool behind the dam rises to its top, A's level ({up} raised of {reach})");
            Check(down == 0 && above == 0, $"river dam: the river below and the pool above keep their levels ({down} below, {above} above)");
            Check(River() == river0, $"river dam: the river's own water stays ({River()} of {river0})");
            Console.WriteLine($"river dam{(levee ? " with bank strips" : "")}: {up} raised behind the dam, {above} in pool A, {down} below, {Lowland() - lowland0} more in the lowland, updates={sim.Updates}");
            // the dam's top course taken away
            int moved = 0;
            for (int z = z0; z <= z1; z++)
            {
                sim.BreakBlock(damX, 9, z);
                moved += H2Release.Opened(g, new BlockPos(damX, 9, z));
            }
            H2Release.RunAll(g);
            Check(sim.Settle(600_000, 3_000_000), "river dam: settles after the top course goes");
            int left = Raised(1, 90);
            Check((levee ? moved >= reach : moved == up) && left == 0, $"river dam: the raised water drains and nothing stays raised ({moved} let out, {left} raised)");
            Check(River() == river0, $"river dam: the river is back at its own water ({River()} of {river0})");
            Check(Lowland() == lowland0, $"river dam: the river takes the raised water, the lowland gets none ({Lowland() - lowland0})");
            Console.WriteLine($"river dam{(levee ? " with bank strips" : "")}: top course gone: {moved} let out, {left} still raised, updates={sim.Updates}");
            }
            EarthRiverQuery.Surface = null;
            EarthRiverQuery.IsRiver = _ => false;
            EarthRiverQuery.Active = false;
            H2Release.DrainRate = 1;
            H2Release.ResetWorld();
            H2Filling.ResetWorld();
        }
#endif
    }
}
