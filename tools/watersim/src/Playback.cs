using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WaterSim
{
    /// <summary>A frame of the playback: the cells that changed since the previous frame (index, value).</summary>
    public sealed class Frame
    {
        public long T;
        /// <summary>The edit applied just before this frame, or -1 for a frame taken at a 100 ms pulse.</summary>
        public int Edit = -1;
        /// <summary>Pairs: cell index, new value (<see cref="Playback.Value"/>).</summary>
        public List<int> Changes = new();
        /// <summary>Updates run since the previous frame.</summary>
        public long Updates;
    }

    /// <summary>
    /// Records a run over simulated time: a frame at every 100 ms pulse that changed something, and one right after each
    /// player edit, until the water settles (or 180 s pass after the last edit).
    /// </summary>
    public static class Playback
    {
        /// <summary>A cell as the page reads it: -1 stone, 0 empty, else kind * 1000 + flow * 10 + height.</summary>
        public static int Value(World w, int i)
        {
            if (w.Solid[i] != 0) return -1;
            int c = w.Fluid[i];
            return c == 0 ? 0 : F.Kind(c) * 1000 + F.Flow(c) * 10 + F.Level(c);
        }

        public sealed class Result
        {
            public List<Frame> Frames = new();
            public World Final;
            public bool Settled;
            public long Updates;
            public List<string> Log = new();
        }

        public static Result Record(World start, RuleSet r, List<Edit> edits, int seed = 1, int maxFrames = 480, RiverStub river = null,
            Action<int, World> beforeEdit = null)
        {
            var sim = new Sim(start.Clone(), r, seed) { River = river };
            var res = new Result();
            var last = new int[start.Fluid.Length];
            for (int i = 0; i < last.Length; i++) last[i] = Value(start, i);
            long updates0 = 0;
            void Capture(int edit)
            {
                var f = new Frame { T = sim.Now, Edit = edit, Updates = sim.Updates - updates0 };
                for (int i = 0; i < last.Length; i++)
                {
                    int v = Value(sim.W, i);
                    if (v == last[i]) continue;
                    f.Changes.Add(i); f.Changes.Add(v);
                    last[i] = v;
                }
                if (f.Changes.Count == 0 && edit < 0) return;
                updates0 = sim.Updates;
                res.Frames.Add(f);
            }
            sim.AfterPulse = () => Capture(-1);
            for (int k = 0; k < edits.Count; k++)
            {
                sim.Run(edits[k].WaitMs);
                beforeEdit?.Invoke(k, sim.W);
                edits[k].Apply(sim);
                Capture(k);
            }
            res.Settled = sim.Settle(Study.SettleMs, Study.SettleUpdates);
            Capture(-1);
            sim.AfterPulse = null;
            res.Final = sim.W;
            res.Updates = sim.Updates;
            res.Log = sim.FeatureLog;
            if (!res.Settled)
                foreach (var kv in sim.UpdatesAt.OrderByDescending(k => k.Value).Take(4))
                {
                    sim.W.Pos(kv.Key, out int x, out int y, out int z);
                    res.Log.Add($"busy cell {x},{y},{z}: {kv.Value} updates, {F.Name(sim.W.Get(x, y, z))}");
                }
            res.Frames = Downsample(res.Frames, maxFrames);
            return res;
        }

        /// <summary>Merges neighbouring pulse frames (never an edit's frame) until at most <paramref name="max"/> remain.</summary>
        static List<Frame> Downsample(List<Frame> frames, int max)
        {
            while (frames.Count > max)
            {
                var merged = new List<Frame>();
                for (int i = 0; i < frames.Count; i++)
                {
                    var f = frames[i];
                    if (f.Edit < 0 && i + 1 < frames.Count && frames[i + 1].Edit < 0)
                    {
                        var g = frames[i + 1];
                        var map = new Dictionary<int, int>();
                        var order = new List<int>();
                        foreach (var fr in new[] { f, g })
                            for (int k = 0; k < fr.Changes.Count; k += 2)
                            {
                                if (!map.ContainsKey(fr.Changes[k])) order.Add(fr.Changes[k]);
                                map[fr.Changes[k]] = fr.Changes[k + 1];
                            }
                        var m = new Frame { T = g.T, Edit = -1, Updates = f.Updates + g.Updates };
                        foreach (int idx in order) { m.Changes.Add(idx); m.Changes.Add(map[idx]); }
                        merged.Add(m);
                        i++;
                    }
                    else merged.Add(f);
                }
                if (merged.Count == frames.Count) break;
                frames = merged;
            }
            return frames;
        }

        /// <summary>The edits a playback of the scene uses: its script, or the study's demo sequence (edits, then undone).</summary>
        public static List<Edit> EditsFor(Scene s)
        {
            if (s.Script.Count > 0) return s.Script;
            var (edits, undo) = Export.WorstSequence(s, RuleSet.Hydrology013());
            var all = new List<Edit>(edits);
            if (undo.Count > 0)
            {
                var first = undo[0];
                first.WaitMs += 4000; // the water moves a while before the player undoes
                undo[0] = first;
            }
            all.AddRange(undo);
            return all;
        }

        /// <summary>`watersim play scene rules [--frames dir] [--alt]`: records the run and writes one text file per frame.</summary>
        public static void Play(string[] args)
        {
            var s = Scenes.ByNameAny(args[1]) ?? throw new ArgumentException("unknown scene " + args[1]);
            var r = Program.Rules(args.Length > 2 && !args[2].StartsWith("--") ? args[2] : "recommended");
            bool alt = args.Contains("--alt") && s.AltGenerated != null;
            int fi = Array.IndexOf(args, "--frames");
            string dir = fi >= 0 && fi + 1 < args.Length ? args[fi + 1] : null;
            var start = alt ? s.AltGenerated : s.Generated;
            var edits = EditsFor(s);
            var res = Record(start, r, edits, maxFrames: 100000, river: s.River);
            foreach (var l in res.Log.Where(l => l.StartsWith("busy"))) Console.WriteLine(l);
            Console.WriteLine($"{s.Name}{(alt ? " (" + s.AltLabel + ")" : "")} under {r.Name}: {res.Frames.Count} frames, {res.Updates} updates, settled={res.Settled}, ends {res.Final.CountWaterDiff(start)} cells from the start, {Invariants.Floating(res.Final)}");
            if (dir == null) return;
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "frame_*.txt")) File.Delete(old);
            var w = start.Clone();
            int y0 = 0, y1 = w.SY - 1;
            for (int n = 0; n < res.Frames.Count; n++)
            {
                var f = res.Frames[n];
                var changed = new List<string>();
                for (int k = 0; k < f.Changes.Count; k += 2)
                {
                    int i = f.Changes[k], v = f.Changes[k + 1];
                    w.Pos(i, out int x, out int y, out int z);
                    string before = w.Solid[i] != 0 ? "stone" : F.Name(w.Fluid[i]);
                    if (v == -1) { w.Solid[i] = 1; w.Fluid[i] = 0; }
                    else
                    {
                        w.Solid[i] = 0;
                        w.Fluid[i] = v == 0 ? 0 : F.Make(v / 1000, (v / 10) % 100, v % 10);
                    }
                    string after = v == -1 ? "stone" : F.Name(w.Fluid[i]);
                    changed.Add($"{x},{y},{z} {before} -> {after}");
                }
                var sb = new StringBuilder();
                sb.Append($"frame {n}  t={f.T / 1000.0:F2} s  updates {f.Updates}  changed {changed.Count}");
                if (f.Edit >= 0) sb.Append($"  after edit {f.Edit}: {edits[f.Edit].Verb} {edits[f.Edit].X},{edits[f.Edit].Y},{edits[f.Edit].Z}");
                sb.Append('\n');
                sb.Append($"section z={s.SectionZ} (# stone, . air, S source, R rapid source, Z salt source, 1-6 flowing, a-f rapid flowing, | falling, ! rapid falling, u/o still)\n");
                sb.Append(w.Section(s.SectionZ, y0, y1));
                foreach (var line in changed.Take(60)) sb.Append("  ").Append(line).Append('\n');
                if (changed.Count > 60) sb.Append($"  ... {changed.Count - 60} more\n");
                File.WriteAllText(Path.Combine(dir, $"frame_{n:D4}.txt"), sb.ToString());
            }
            Console.WriteLine($"wrote {res.Frames.Count} frames to {dir}");
        }
    }
}
