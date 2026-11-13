using System;

namespace WaterSim
{
    /// <summary>
    /// Known in-game behaviours the simulator must reproduce before its sweeps mean anything:
    /// - vanilla: a placed source spreads 6 blocks on flat ground (heights 6..1);
    /// - vanilla: poking an Earth river step drains the lower pool (2026-10-22 test: 48-153 pool sources became flowing);
    /// - Hydrology 0.1.3: a 2-wide trench fills, a 1-wide one stays flowing;
    /// - Hydrology 0.1.3: a touched chute's edges are rapid;
    /// - Hydrology 0.1.2 (0.1.3 without the falling-water fix) and 0.1.3: stones in a waterfall leave water standing in the air.
    /// </summary>
    public static class Calibrate
    {
        public static void Run()
        {
            SixBlockSpread();
            StepPoke(RuleSet.Vanilla());
            StepPoke(RuleSet.Hydrology013());
            TrenchFill(RuleSet.Vanilla(), "trench2-opened");
            TrenchFill(RuleSet.Hydrology013(), "trench2-opened");
            TrenchFill(RuleSet.Hydrology013(), "trench2-outward");
            TrenchFill(RuleSet.Hydrology013(), "trench2-opened-salt");
            TrenchFill(RuleSet.Hydrology013(), "trench1-opened");
            ChuteRapid(RuleSet.Hydrology013());
            var h012 = RuleSet.Hydrology013().Clone("hydrology-0.1.2");
            h012.H3FallsInAir = false;
            VictoriaColumns(RuleSet.Vanilla());
            VictoriaColumns(h012);
            VictoriaColumns(RuleSet.Hydrology013());
        }

        static void SixBlockSpread()
        {
            var w = new World(21, 5, 21);
            w.FillSolid(0, 0, 0, 20, 1, 20);
            var sim = new Sim(w, RuleSet.Vanilla(), 1);
            sim.SetDirect(10, 2, 10, F.Make(F.Water, F.Still, 7));
            sim.Wake(10, 2, 10);
            bool ok = sim.Settle();
            int r = 0;
            for (int d = 1; d < 10; d++) if (w.Get(10 + d, 2, 10) != 0) r = d;
            var row = "";
            for (int d = 0; d <= 8; d++) row += F.Level(w.Get(10 + d, 2, 10));
            Console.WriteLine($"vanilla spread: settled={ok} updates={sim.Updates} reach={r} heights east={row} (expect reach 6, 765432100)");
            Console.Write(w.Slice(2));
        }

        static void StepPoke(RuleSet r)
        {
            var s = Scenes.Step();
            var sim = new Sim(s.Generated.Clone(), r, 1);
            // a block placed into the ramp, then taken away
            sim.PlaceStone(16, 8, 6);
            sim.Run(3000);
            sim.BreakBlock(16, 8, 6);
            bool ok = sim.Settle();
            string lost = Scenes.PoolsKept(sim.W, s.Generated) ?? "0 pool sources lost";
            Console.WriteLine($"step poke, {r.Name}: settled={ok} updates={sim.Updates}; {lost}; cells changed {sim.W.CountWaterDiff(s.Generated)}");
            Console.Write(sim.W.Section(6, 3, 10));
        }

        static void TrenchFill(RuleSet r, string scene)
        {
            var s = Scenes.ByName(scene);
            var sim = new Sim(s.Generated.Clone(), r, 1);
            Study.ApplyScript(sim, s.Script);
            bool ok = sim.Settle();
            string f = s.Checks[0].check(sim.W, null) ?? "ok";
            Console.WriteLine($"{scene}, {r.Name}: settled={ok} updates={sim.Updates}; {s.Checks[0].name}: {f}");
            Console.Write(sim.W.Slice(8).Split('\n')[5] + "\n" + string.Join("\n", sim.W.Slice(8).Split('\n')[6..10]) + "\n");
        }

        static void ChuteRapid(RuleSet r)
        {
            var s = Scenes.Chute();
            var sim = new Sim(s.Generated.Clone(), r, 1);
            sim.PlaceStone(13, 11, 3);
            sim.Run(2000);
            sim.BreakBlock(13, 11, 3);
            bool ok = sim.Settle();
            string f = s.Checks[0].check(sim.W, null) ?? "all edge sources rapid";
            string wheel = Scenes.WheelsRapid(sim.W, s.Wheels) ?? "wheels in rapid flowing water";
            Console.WriteLine($"chute poke, {r.Name}: settled={ok} updates={sim.Updates}; {f}; {wheel}");
            Console.Write(sim.W.Section(6, 6, 13));
        }

        static void VictoriaColumns(RuleSet r)
        {
            var s = Scenes.Victoria();
            var sim = new Sim(s.Generated.Clone(), r, 1);
            Study.ApplyScript(sim, s.Script);
            bool ok = sim.Settle();
            var fl = Invariants.Floating(sim.W);
            Console.WriteLine($"victoria, {r.Name}: settled={ok} updates={sim.Updates}; {fl}");
            for (int z = 5; z <= 9; z++)
            {
                Console.WriteLine($"  curtain z={z}: {sim.W.Column(13, z, 8, 17)}");
                Console.WriteLine($"  beside  z={z}: {sim.W.Column(14, z, 8, 17)}");
            }
            Console.Write(sim.W.Section(7, 7, 18));
        }
    }
}
