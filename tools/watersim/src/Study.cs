using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WaterSim
{
    /// <summary>One scene under one rule set: the settled reference, the script, the pokes and the fuzzed edit sequences.</summary>
    public sealed class SceneResult
    {
        public string Scene, Rules;
        // the reference: Earth's generated world, every cell woken once, settled
        public bool CanonicalSettled;
        public long CanonicalUpdates;
        public int CanonicalDiff;           // cells (water, labels ignored) the settling changed from Earth's generated shape
        public int CanonicalLabelDiff;      // cells whose label alone changed
        public FloatingCount CanonicalFloating;
        // the script (trench digging, Victoria attack), then settled
        public bool ScriptSettled;
        public long ScriptUpdates;
        public FloatingCount ScriptFloating;
        public List<string> CheckFailures = new();
        // pokes next to generated water (place then remove, or remove then place)
        public int Pokes, PokesChangedGenerated, PokesChangedCanonical, PokeLoops;
        public long PokeUpdates;
        public FloatingCount PokeFloating;
        // random edit sequences, undone
        public int Fuzz, FuzzChanged, FuzzLoops, FuzzSourcesGained, FuzzSourcesLost;
        public long FuzzUpdates, FuzzDiffCells;
        public FloatingCount FuzzFloating;
        public long MaxUpdatesOneRun;
        // states for the page
        public World CanonicalWorld, ScriptWorld, FuzzSampleBefore, FuzzSampleAfter, FuzzSampleEdited;
    }

    public static class Study
    {
        public const long SettleMs = 180_000, SettleUpdates = 300_000;

        static void Add(ref FloatingCount a, FloatingCount b)
        {
            a.Hanging += b.Hanging; a.Standing += b.Standing; a.UnsupportedSources += b.UnsupportedSources;
        }

        /// <summary>The reference state of a rule set: Earth's generated world with every fluid cell woken once, settled.</summary>
        public static (World w, bool settled, long updates) Canonical(Scene s, RuleSet r, int seed = 1)
        {
            var sim = new Sim(s.Generated.Clone(), r, seed);
            sim.WakeAll();
            bool ok = sim.Settle(SettleMs, SettleUpdates);
            return (sim.W, ok, sim.Updates);
        }

        public static void ApplyScript(Sim sim, List<Edit> script)
        {
            foreach (var e in script)
            {
                sim.Run(e.WaitMs);
                e.Apply(sim);
            }
        }

        public static SceneResult Run(Scene s, RuleSet r, int fuzzCount, int pokeLimit, int seed, bool keepWorlds = false)
        {
            var res = new SceneResult { Scene = s.Name, Rules = r.Name };
            var (canon, ok, upd) = Canonical(s, r, seed);
            res.CanonicalSettled = ok;
            res.CanonicalUpdates = upd;
            res.CanonicalDiff = canon.CountWaterDiff(s.Generated);
            res.CanonicalLabelDiff = canon.CountWaterDiff(s.Generated, ignoreLabels: false) - res.CanonicalDiff;
            res.CanonicalFloating = Invariants.Floating(canon);
            res.MaxUpdatesOneRun = upd;
            res.CanonicalWorld = canon;

            // the script starts from Earth's generated world, as a player meets it
            var sim = new Sim(s.Generated.Clone(), r, seed);
            ApplyScript(sim, s.Script);
            res.ScriptSettled = sim.Settle(SettleMs, SettleUpdates);
            res.ScriptUpdates = sim.Updates;
            res.MaxUpdatesOneRun = Math.Max(res.MaxUpdatesOneRun, sim.Updates);
            // a scene without a script is checked as the rules leave it once every cell has been touched
            var after = s.Script.Count == 0 ? canon : sim.W;
            res.ScriptFloating = Invariants.Floating(after);
            foreach (var (name, check) in s.Checks)
            {
                string f = check(after, canon);
                if (f != null) res.CheckFailures.Add(name + ": " + f);
            }
            if (!res.ScriptSettled) res.CheckFailures.Add("updates never stop after the script");
            if (keepWorlds) res.ScriptWorld = after.Clone();

            // pokes (invariant b): next to Earth's generated water
            if (pokeLimit > 0) Pokes(s, r, res, pokeLimit, seed);
            // random sequences (invariant c), from the settled state after the script
            if (fuzzCount > 0) FuzzRuns(s, r, res, after, fuzzCount, seed, keepWorlds);
            if (!keepWorlds) res.CanonicalWorld = null;
            return res;
        }

        public static List<(int x, int y, int z)> PokeSites(Scene s)
        {
            var w = s.Generated;
            var sites = new List<(int, int, int)>();
            for (int y = 1; y < w.SY - 1; y++)
                for (int z = 0; z < w.SZ; z++)
                    for (int x = 0; x < w.SX; x++)
                    {
                        if (w.Get(x, y, z) != 0) continue;
                        bool next = false;
                        for (int f = 0; f < 6 && !next; f++) next = w.Get(x + Dir.AllX[f], y + Dir.AllY[f], z + Dir.AllZ[f]) != 0;
                        if (!next) continue;
                        // a stone with air above it (a floor or bank a player can reach) or any air cell
                        if (w.IsSolid(x, y, z) && w.IsSolid(x, y + 1, z) && w.Get(x, y + 1, z) == 0) continue;
                        sites.Add((x, y, z));
                    }
            return sites;
        }

        static void Pokes(Scene s, RuleSet r, SceneResult res, int limit, int seed)
        {
            var sites = PokeSites(s);
            var rng = new Random(seed * 7919 + 17);
            if (sites.Count > limit) sites = sites.OrderBy(_ => rng.Next()).Take(limit).ToList();
            var canon = res.CanonicalWorld ?? Canonical(s, r, seed).w;
            foreach (var (x, y, z) in sites)
            {
                int wait = 1500 + rng.Next(3000);
                // Earth's generated shape, poked: does it come back as generated?
                var sim = Poke(s.Generated, r, seed + x * 31 + z * 131 + y, x, y, z, wait, out bool ok);
                res.Pokes++;
                if (!ok) res.PokeLoops++;
                res.PokeUpdates += sim.Updates;
                res.MaxUpdatesOneRun = Math.Max(res.MaxUpdatesOneRun, sim.Updates);
                if (sim.W.CountWaterDiff(s.Generated) != 0) res.PokesChangedGenerated++;
                Add(ref res.PokeFloating, Invariants.Floating(sim.W));
                // the rule set's own settled shape, poked: is it a fixed point?
                sim = Poke(canon, r, seed + x * 37 + z * 139 + y, x, y, z, wait, out ok);
                if (!ok) res.PokeLoops++;
                res.PokeUpdates += sim.Updates;
                res.MaxUpdatesOneRun = Math.Max(res.MaxUpdatesOneRun, sim.Updates);
                if (sim.W.CountWaterDiff(canon) != 0) res.PokesChangedCanonical++;
                Add(ref res.PokeFloating, Invariants.Floating(sim.W));
            }
        }

        /// <summary>Place then remove a block (or remove then put back), and settle.</summary>
        public static Sim Poke(World start, RuleSet r, int seed, int x, int y, int z, int waitMs, out bool settled)
        {
            var sim = new Sim(start.Clone(), r, seed);
            bool solid = sim.W.IsSolid(x, y, z);
            if (solid) sim.BreakBlock(x, y, z); else sim.PlaceStone(x, y, z);
            sim.Run(waitMs);
            if (solid) sim.PlaceStone(x, y, z); else sim.BreakBlock(x, y, z);
            settled = sim.Settle(SettleMs, SettleUpdates);
            return sim;
        }

        static void FuzzRuns(Scene s, RuleSet r, SceneResult res, World start, int count, int seed, bool keep)
        {
            var rng = new Random(seed * 104729 + 3);
            int sources0 = start.CountSources();
            long worstDiff = -1;
            for (int t = 0; t < count; t++)
            {
                var sim = new Sim(start.Clone(), r, rng.Next());
                int n = 1 + rng.Next(6);
                var done = new List<(int x, int y, int z, bool placed)>();
                for (int i = 0; i < n; i++)
                {
                    int x = rng.Next(s.EX0, s.EX1 + 1), y = rng.Next(s.EY0, s.EY1 + 1), z = rng.Next(s.EZ0, s.EZ1 + 1);
                    if (done.Exists(d => d.x == x && d.y == y && d.z == z)) continue;
                    sim.Run(rng.Next(0, 3000));
                    bool solid = sim.W.IsSolid(x, y, z);
                    if (solid) sim.BreakBlock(x, y, z); else sim.PlaceStone(x, y, z);
                    done.Add((x, y, z, !solid));
                }
                // a while for the water to move (sometimes until it settles), then undo in reverse order
                if (rng.Next(3) == 0) sim.Settle(SettleMs, SettleUpdates); else sim.Run(rng.Next(500, 15000));
                World edited = keep ? sim.W.Clone() : null;
                for (int i = done.Count - 1; i >= 0; i--)
                {
                    var (x, y, z, placed) = done[i];
                    sim.Run(rng.Next(0, 3000));
                    if (placed) sim.BreakBlock(x, y, z); else sim.PlaceStone(x, y, z);
                }
                bool ok = sim.Settle(SettleMs, SettleUpdates);
                res.Fuzz++;
                if (!ok) res.FuzzLoops++;
                res.FuzzUpdates += sim.Updates;
                res.MaxUpdatesOneRun = Math.Max(res.MaxUpdatesOneRun, sim.Updates);
                int diff = sim.W.CountWaterDiff(start);
                if (diff != 0) { res.FuzzChanged++; res.FuzzDiffCells += diff; }
                int src = sim.W.CountSources();
                if (src > sources0) res.FuzzSourcesGained += src - sources0;
                if (src < sources0) res.FuzzSourcesLost += sources0 - src;
                var fl = Invariants.Floating(sim.W);
                Add(ref res.FuzzFloating, fl);
                if (keep && diff + fl.Total * 4 > worstDiff)
                {
                    worstDiff = diff + fl.Total * 4;
                    res.FuzzSampleBefore = start;
                    res.FuzzSampleEdited = edited;
                    res.FuzzSampleAfter = sim.W.Clone();
                }
            }
        }

        /// <summary>Runs every scene under every rule set, in parallel.</summary>
        public static List<SceneResult> RunAll(List<Scene> scenes, List<RuleSet> rules, int fuzz, int pokes, int seed, bool keep = false)
        {
            var jobs = new List<(Scene s, RuleSet r)>();
            foreach (var r in rules) foreach (var s in scenes) jobs.Add((s, r));
            var results = new SceneResult[jobs.Count];
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                results[i] = Run(jobs[i].s, jobs[i].r, fuzz, pokes, seed, keep);
            });
            return results.ToList();
        }
    }
}
