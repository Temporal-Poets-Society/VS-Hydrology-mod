using System;
using System.Collections.Generic;

namespace WaterSim
{
    /// <summary>
    /// Earth's planned read-only river flow query (decision 0022 part A), as a stub a scene fills in: which columns are
    /// natural river and at what water level, the river's ends, and how much flows in. Nothing here tracks what players did.
    /// </summary>
    public sealed class RiverStub
    {
        /// <summary>Natural water top of the column, or -1 where Earth laid out no river or lake water.</summary>
        public Func<int, int, int> NaturalTop = (x, z) => -1;
        /// <summary>One cell in each natural water body (each pool between drops), and the river's far downstream cell.</summary>
        public List<(int x, int y, int z, bool external)> Seeds = new();
        public (int x, int y, int z) Up, Down;
        /// <summary>Discharge at gameplay scale, in water cells per second.</summary>
        public double Inflow = 20;
        /// <summary>Discharge in m³/s as HydroRIVERS gives it (for the power read-out only).</summary>
        public double DischargeM3 = 1200;
    }

    /// <summary>
    /// Crude prototypes of the later targets (decision 0024 section 4, 0022 part B), behind <see cref="RuleSet"/> switches.
    /// They exist to find which version-1 rules stand in the way, not to be built as they are.
    /// - <see cref="RuleSet.ProtoLevels"/>: managed water bodies. A body of sources that contains natural river water is
    ///   kept at a target level, one layer at a time (bounded searches, a timer only while something changes):
    ///   - fill: open cells beside it on ground or on its sources fill up to the natural level (a dug canal, a lock
    ///     chamber); natural columns fill only up to their own natural level;
    ///   - pond: when no path of sources leads from the river's upstream end to its downstream end (blocked), the body
    ///     rises above its natural level until water can spill (a cell beside its top with nothing under it);
    ///   - drain: cells above the level the body can hold (its natural level when it reaches the upstream end or is fed by
    ///     falling water, else nothing) are removed a layer at a time: a pond behind a broken dam, a lock chamber opened
    ///     to the lower side, a stretch whose supply was diverted.
    /// - <see cref="RuleSet.ProtoSubLevels"/>: flowing water with K sub-levels per height on the way down, so water runs
    ///   6 × K blocks (Realistic Water's long runs). Kept in a side array here; in the game that would be new blocks.
    /// </summary>
    public sealed partial class Sim
    {
        public RiverStub River;
        /// <summary>Columns (x + z * SX) a managed body drained; with ProtoLocalOff the fills and level relax skip them.</summary>
        readonly HashSet<int> drainedColumns = new();

        public bool LocallyOff(int x, int z) => R.ProtoLocalOff && drainedColumns.Contains(x + z * W.SX);
        long featureDue = -1;
        public readonly List<string> FeatureLog = new();

        void ScheduleFeatures(long delay)
        {
            if (!R.ProtoLevels || River == null) return;
            long due = Now + delay;
            if (featureDue < 0 || due < featureDue) featureDue = due;
        }

        void RunFeaturesIfDue()
        {
            if (featureDue < 0 || Now < featureDue) return;
            featureDue = -1;
            int changed = 0;
            foreach (var s in River.Seeds) changed += ManageBody(s.x, s.y, s.z);
            if (changed == 0 && River.Seeds.Count > 0) FeatureLog.Add($"{Now / 1000.0:F1} s: bodies at their levels");
            if (changed > 0) ScheduleFeatures(Math.Max(1000, (long)(1000 * changed / Math.Max(1, River.Inflow))));
        }

        int NatTop(int x, int z) => River.NaturalTop(x, z);

        /// <summary>
        /// One step of the managed-body rule for the body containing the seed; returns the cells written.
        /// The level a body holds: its highest natural level when it reaches the river's upstream end or a seed the stub
        /// marks as supplied from outside the area; else the natural level where falling water lands on it; else none.
        /// A blocked river (no path of sources from the upstream to the downstream end) holds any level until it spills.
        /// </summary>
        int ManageBody(int sx, int sy, int sz)
        {
            int seed = Fl(sx, sy, sz);
            if (!F.IsSource(seed) || Solid(sx, sy, sz)) return 0;
            var body = SourceBody(sx, sy, sz, 8000);
            var inBody = new HashSet<int>(body);
            bool Has((int x, int y, int z) p) => inBody.Contains(W.Idx(p.x, p.y, p.z));
            bool hasUp = Has(River.Up);
            bool supplied = hasUp || River.Seeds.Exists(t => t.external && Has((t.x, t.y, t.z)));
            int natural = -1, top = -1, landing = -1;
            foreach (int i in body)
            {
                W.Pos(i, out int x, out int y, out int z);
                int own = NatTop(x, z);
                natural = Math.Max(natural, own);
                top = Math.Max(top, y);
                int above = Fl(x, y + 1, z), above2 = Fl(x, y + 2, z);
                bool column = F.Same(above, seed) && !F.IsSource(above) && (F.Flow(above) == F.D || F.Same(above2, seed));
                if (column && own >= 0) landing = Math.Max(landing, own);
            }
            // water leaves below the surface (a gap in a dam, an open lock gate): the body is not blocked
            bool openBelow = false;
            foreach (int i in body)
            {
                if (openBelow) break;
                W.Pos(i, out int x, out int y, out int z);
                if (y >= top) continue;
                for (int h = 0; h < 4 && !openBelow; h++)
                {
                    int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                    if (W.In(nx, y, nz) && !Solid(nx, y, nz) && !F.IsSource(Fl(nx, y, nz))) openBelow = true;
                }
            }
            bool blocked = hasUp && !Has(River.Down) && !openBelow;
            if (natural < 0) return 0;
            int hold = blocked ? int.MaxValue : supplied ? natural : landing;
            // a cell outside the river (a canal, a lock chamber) holds the level of the nearest natural water it is joined to
            var near = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (int i in body) { W.Pos(i, out int x, out _, out int z); if (NatTop(x, z) >= 0) { near[i] = NatTop(x, z); q.Enqueue(i); } }
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                W.Pos(i, out int x, out int y, out int z);
                for (int f = 0; f < 6; f++)
                {
                    int nx = x + Dir.AllX[f], ny = y + Dir.AllY[f], nz = z + Dir.AllZ[f];
                    if (!W.In(nx, ny, nz)) continue;
                    int j = W.Idx(nx, ny, nz);
                    if (!inBody.Contains(j) || near.ContainsKey(j)) continue;
                    near[j] = near[i];
                    q.Enqueue(j);
                }
            }
            int Keep(int x, int y, int z)
            {
                if (hold == int.MaxValue) return hold;
                int own = NatTop(x, z);
                if (own >= 0) return Math.Min(own, hold);
                if (hold < 0) return -1;
                return near.TryGetValue(W.Idx(x, y, z), out int n) ? Math.Min(n, hold) : hold;
            }

            // drain the highest layer above what the body holds
            int drainY = -1;
            foreach (int i in body)
            {
                W.Pos(i, out int x, out int y, out int z);
                if (y > Keep(x, y, z)) drainY = Math.Max(drainY, y);
            }
            if (drainY >= 0)
            {
                int n = 0;
                foreach (int i in body)
                {
                    W.Pos(i, out int x, out int y, out int z);
                    if (y != drainY || y <= Keep(x, y, z)) continue;
                    SetAndWake(x, y, z, 0, true);
                    drainedColumns.Add(x + z * W.SX);
                    n++;
                }
                FeatureLog.Add($"{Now / 1000.0:F1} s: drained layer {drainY} ({n} cells)");
                return n;
            }

            // a blocked body that can spill does not rise further
            if (blocked && Spills(body, top)) return 0;

            if (R.ProtoBasinFill && !blocked) return BasinFill(body, inBody, near, hold, seed);
            // fill the lowest layer of open cells beside or above the body that rest on ground or on the body
            int fillY = int.MaxValue;
            var cand = new List<(int x, int y, int z)>();
            var seen = new HashSet<int>();
            foreach (int i in body)
            {
                W.Pos(i, out int x, out int y, out int z);
                for (int f = 0; f < 5; f++)
                {
                    int nx = x + Dir.AllX[f], ny = y + Dir.AllY[f], nz = z + Dir.AllZ[f];
                    if (!W.In(nx, ny, nz) || Solid(nx, ny, nz) || F.IsSource(Fl(nx, ny, nz))) continue;
                    if (!seen.Add(W.Idx(nx, ny, nz))) continue;
                    if (!Solid(nx, ny - 1, nz) && !inBody.Contains(W.Idx(nx, ny - 1, nz))) continue;
                    int lim = hold == int.MaxValue ? int.MaxValue : NatTop(nx, nz) >= 0 ? Math.Min(NatTop(nx, nz), hold) : NearestOf(near, x, y, z, hold);
                    if (ny > lim) continue;
                    if (ny < fillY) { fillY = ny; cand.Clear(); }
                    if (ny == fillY) cand.Add((nx, ny, nz));
                }
            }
            if (cand.Count == 0) return 0;
            foreach (var (x, y, z) in cand) { SetAndWake(x, y, z, F.Make(F.Kind(seed), F.Still, 7), true); drainedColumns.Remove(x + z * W.SX); }
            FeatureLog.Add($"{Now / 1000.0:F1} s: filled layer {fillY} ({cand.Count} cells){(blocked ? ", pond" : "")}");
            return cand.Count;
        }

        /// <summary>
        /// Basin fill: the open cells (air or flowing water) joined to the body through open cells, sideways or down, each
        /// at or below the level its entry point holds (bounded); the lowest layer of them that rests on ground or sources
        /// fills. Natural river columns are left to their own level (a step's ramp never fills).
        /// </summary>
        int BasinFill(List<int> body, HashSet<int> inBody, Dictionary<int, int> near, int hold, int seed)
        {
            if (hold < 0) return 0;
            var lim = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (int i in body)
            {
                W.Pos(i, out int x, out int y, out int z);
                int level = NatTop(x, z) >= 0 ? Math.Min(NatTop(x, z), hold) : near.TryGetValue(i, out int nn) ? Math.Min(nn, hold) : hold;
                for (int f = 0; f < 6; f++)
                {
                    if (f == Dir.Up) continue;
                    int nx = x + Dir.AllX[f], ny = y + Dir.AllY[f], nz = z + Dir.AllZ[f];
                    if (!OpenForFill(nx, ny, nz) || ny > level || NatTop(nx, nz) >= 0) continue;
                    int j = W.Idx(nx, ny, nz);
                    if (lim.ContainsKey(j)) continue;
                    lim[j] = level;
                    q.Enqueue(j);
                }
            }
            while (q.Count > 0 && lim.Count < 6000)
            {
                int i = q.Dequeue();
                W.Pos(i, out int x, out int y, out int z);
                for (int f = 0; f < 6; f++)
                {
                    if (f == Dir.Up) continue;
                    int nx = x + Dir.AllX[f], ny = y + Dir.AllY[f], nz = z + Dir.AllZ[f];
                    if (!OpenForFill(nx, ny, nz) || NatTop(nx, nz) >= 0) continue;
                    int j = W.Idx(nx, ny, nz);
                    if (lim.ContainsKey(j)) continue;
                    lim[j] = lim[i];
                    q.Enqueue(j);
                }
            }
            int fillY = int.MaxValue;
            var cand = new List<int>();
            foreach (var kv in lim)
            {
                W.Pos(kv.Key, out int x, out int y, out int z);
                if (y > kv.Value) continue;
                bool supported = Solid(x, y - 1, z) || F.IsSource(Fl(x, y - 1, z));
                if (!supported) continue;
                if (R.ProtoWideOnly && !InWideSquare(x, y, z)) continue;
                if (y < fillY) { fillY = y; cand.Clear(); }
                if (y == fillY) cand.Add(kv.Key);
            }
            foreach (int i in cand)
            {
                W.Pos(i, out int x, out int y, out int z);
                SetAndWake(x, y, z, F.Make(F.Kind(seed), F.Still, 7), true);
            }
            if (cand.Count > 0) FeatureLog.Add($"{Now / 1000.0:F1} s: basin layer {fillY} ({cand.Count} cells)");
            return cand.Count;
        }

        bool OpenForFill(int x, int y, int z) => W.In(x, y, z) && !Solid(x, y, z) && !F.IsSource(Fl(x, y, z));

        /// <summary>The cell lies in a 2 x 2 square of cells that are open or sources (not a 1-wide passage).</summary>
        bool InWideSquare(int x, int y, int z)
        {
            for (int dx = -1; dx <= 0; dx++)
                for (int dz = -1; dz <= 0; dz++)
                {
                    bool ok = true;
                    for (int a = 0; a <= 1 && ok; a++)
                        for (int b = 0; b <= 1 && ok; b++)
                            ok = !Solid(x + dx + a, y, z + dz + b);
                    if (ok) return true;
                }
            return false;
        }

        /// <summary>The level a new cell beside body cell (x, y, z) holds: that cell's nearest natural level.</summary>
        int NearestOf(Dictionary<int, int> near, int x, int y, int z, int hold) =>
            hold < 0 ? -1 : near.TryGetValue(W.Idx(x, y, z), out int n) ? Math.Min(n, hold) : hold;

        /// <summary>A cell beside the body's top layer that is open and has nothing under it: water spills there.</summary>
        bool Spills(List<int> body, int top)
        {
            foreach (int i in body)
            {
                W.Pos(i, out int x, out int y, out int z);
                if (y != top) continue;
                for (int h = 0; h < 4; h++)
                {
                    int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                    if (Solid(nx, y, nz) || F.IsSource(Fl(nx, y, nz))) continue;
                    if (!Solid(nx, y - 1, nz) && !F.IsSource(Fl(nx, y - 1, nz))) return true;
                }
            }
            return false;
        }

        List<int> SourceBody(int sx, int sy, int sz, int cap)
        {
            int liquid = Fl(sx, sy, sz);
            var list = new List<int>();
            var seen = new HashSet<int> { W.Idx(sx, sy, sz) };
            var q = new Queue<int>();
            q.Enqueue(W.Idx(sx, sy, sz));
            while (q.Count > 0 && list.Count < cap)
            {
                int i = q.Dequeue();
                list.Add(i);
                W.Pos(i, out int x, out int y, out int z);
                for (int f = 0; f < 6; f++)
                {
                    int nx = x + Dir.AllX[f], ny = y + Dir.AllY[f], nz = z + Dir.AllZ[f];
                    if (!W.In(nx, ny, nz)) continue;
                    int c = Fl(nx, ny, nz);
                    if (!F.IsSource(c) || !F.Same(c, liquid) || Solid(nx, ny, nz)) continue;
                    if (seen.Add(W.Idx(nx, ny, nz))) q.Enqueue(W.Idx(nx, ny, nz));
                }
            }
            return list;
        }

        // ---------------------------------------------------------------- sub-levels

        /// <summary>The potential of a cell under <see cref="RuleSet.ProtoSubLevels"/> K: (height - 1) × K + (K - sub); a
        /// source is 6K + 1, so the first cell beside it is height 6, sub 0.</summary>
        int Pot(int c, int i)
        {
            int k = R.ProtoSubLevels;
            if (F.IsSource(c)) return 6 * k + 1;
            return (F.Level(c) - 1) * k + (k - W.Sub[i]);
        }

        int PotAt(int x, int y, int z) => W.In(x, y, z) ? Pot(Fl(x, y, z), W.Idx(x, y, z)) : 0;

        static void FromPot(int p, int k, out int level, out int sub)
        {
            level = (p + k - 1) / k;
            sub = level * k - p;
        }
    }
}
