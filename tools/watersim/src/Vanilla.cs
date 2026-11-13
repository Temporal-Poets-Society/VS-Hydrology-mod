using System;
using System.Collections.Generic;
using System.Linq;

namespace WaterSim
{
    /// <summary>
    /// Vanilla 1.22.7's BlockBehaviorFiniteSpreadingLiquid for full stone blocks and the water family, written from its
    /// behaviour (not copied). Every quirk that changes where water goes is kept: sources never lower themselves, flowing
    /// water stays only when a neighbour is exactly one higher or water is above it, downward paths are searched within a
    /// 7 x 7 square, rapid water (multiplySpread false) spreads into one cell and removes its other lower neighbours, and
    /// the flow label of each cell is recomputed from its 8 neighbours (still / direction / falling).
    /// Hook points for Hydrology's Harmony patches are marked "hook".
    /// </summary>
    public sealed partial class Sim
    {
        /// <summary>ShapeUtil.GetSquarePointsSortedByMDist(3): (x, z) offsets without the origin, stably sorted by Manhattan distance.</summary>
        static readonly (int dx, int dz)[] DownPaths = Enumerable.Range(-3, 7)
            .SelectMany(i => Enumerable.Range(-3, 7).Select(j => (i, j)))
            .Where(p => p.i != 0 || p.j != 0)
            .OrderBy(p => Math.Abs(p.i) + Math.Abs(p.j)).ToArray();

        static readonly byte[] ShuffleTable = { 228, 180, 216, 120, 156, 108, 225, 177, 201, 57, 141, 45, 210, 114, 198, 54, 78, 30, 147, 99, 135, 39, 75, 27 };

        /// <summary>OnDelayedWaterUpdateCheck.</summary>
        void DelayedUpdate(int x, int y, int z, U u)
        {
            SpreadAndUpdate(x, y, z, u);
            Commit();
            if (Fl(x, y, z) != 0) UpdateOwnFlowDir(x, y, z);
            for (int c = 1; c <= 8; c++)
            {
                int nx = x + F.FlowDX[c], nz = z + F.FlowDZ[c];
                if (Fl(nx, y, nz) != 0) UpdateOwnFlowDir(nx, y, nz);
            }
        }

        void SpreadAndUpdate(int x, int y, int z, U u)
        {
            int b = Fl(x, y, z);
            int level = F.Level(b);
            if (level <= 0 || TryLowering(b, x, y, z, u)) return;
            // only when neither the block below nor this cell's own solid block holds liquid up (no solids in liquid cells here)
            if (!Solid(x, y - 1, z) && TrySpreadDownwards(b, x, y, z, u)) return;
            if (level <= 1 && !(R.ProtoSubLevels > 0 && PotAt(x, y, z) > 1)) return; // hook: sub-levels
            if (!u.Multiply)
            {
                string flow = F.FlowNames[F.Flow(b)];
                if (OtherLiquidOnSide(b, x, y, z, flow[0]) || (flow.Length > 1 && OtherLiquidOnSide(b, x, y, z, flow[1]))) return;
            }
            var paths = FindDownwardPaths(b, x, y, z);
            if (paths.Count > 0)
            {
                FlowTowardDownwardPaths(paths, b, x, y, z, u);
                return;
            }
            TrySpreadHorizontal(b, x, y, z, u);
            if (F.IsSource(b)) return;
            int n = CountSources(x, y, z, b);
            if (n < 3 && (n != 2 || CountDiagonalSources(x, y, z, b) < 3)) return;
            if (!R.VanillaSourceRule || LocallyOff(x, z)) return; // switch for the study only; vanilla always has it
            SetDirect(x, y, z, LiquidId(x, y, z, b, Math.Min(7, level + 1)));
            Stats.VanillaSources++;
            for (int h = 0; h < 4; h++)
            {
                int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                if (Fl(nx, y, nz) != 0) UpdateOwnFlowDir(nx, y, nz);
            }
        }

        bool OtherLiquidOnSide(int b, int x, int y, int z, char dir)
        {
            // BlockFacing.FromFirstLetter: "still" reads as south, "d" as down
            int dx = 0, dy = 0, dz = 0;
            switch (dir)
            {
                case 'n': dz = -1; break;
                case 'e': dx = 1; break;
                case 's': dz = 1; break;
                case 'w': dx = -1; break;
                case 'u': dy = 1; break;
                case 'd': dy = -1; break;
                default: return false;
            }
            int c = Fl(x + dx, y + dy, z + dz);
            return c != 0 && !F.Same(b, c);
        }

        int CountSources(int x, int y, int z, int b)
        {
            int n = 0;
            for (int h = 0; h < 4; h++)
            {
                int c = Fl(x + Dir.HX[h], y, z + Dir.HZ[h]);
                if (F.Same(b, c) && F.IsSource(c)) n++;
            }
            return n;
        }

        int CountDiagonalSources(int x, int y, int z, int b)
        {
            int n = 0;
            for (int c = 2; c <= 8; c += 2)
            {
                int f = Fl(x + F.FlowDX[c], y, z + F.FlowDZ[c]);
                if (F.Same(b, f) && F.IsSource(f)) n++;
            }
            return n;
        }

        struct Path { public int X, Z, Dist; }

        void FlowTowardDownwardPaths(List<Path> paths, int b, int x, int y, int z, U u)
        {
            if (!u.Multiply)
            {
                // GameMath.Shuffle with world.Rand
                for (int n = paths.Count; n > 1;)
                {
                    int k = Rand.Next(n);
                    n--;
                    (paths[n], paths[k]) = (paths[k], paths[n]);
                }
            }
            foreach (var p in paths)
            {
                int face = FaceTo(p.X - x, p.Z - z);
                if (!CanSpreadInto(b, x, y, z, p.X, y, p.Z, face)) continue;
                SpreadLiquid(LiquidId(p.X, y, p.Z, b, F.Level(b) - 1), p.X, y, p.Z, u);
                if (!u.Multiply)
                {
                    RemoveOtherLowerNeighbours(b, x, y, z, face, u);
                    break;
                }
            }
        }

        static int FaceTo(int dx, int dz)
        {
            if (Math.Abs(dx) >= Math.Abs(dz)) return dx > 0 ? 1 : 3;
            return dz > 0 ? 2 : 0;
        }

        void RemoveOtherLowerNeighbours(int b, int x, int y, int z, int pathFace, U u)
        {
            // hook: H1 (Hydrology): water flowing down removes only lower neighbours, never same-level ones
            if (R.H1RemovalGuard && pathFace == Dir.Down && F.IsLiquid(b))
            {
                pathFace = Dir.Up;
                Stats.RemovalsRefused++;
            }
            int level = F.Level(b);
            for (int h = 0; h < 4; h++)
            {
                if (h == pathFace) continue;
                int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                int c = Fl(nx, y, nz);
                if (c != 0 && (F.Level(c) < level || (pathFace == Dir.Down && F.Level(c) == level)) && F.Same(b, c))
                {
                    SetBulk(nx, y, nz, 0);
                    UpdateNeighbouringLiquids(nx, y, nz, x, z, 0, u);
                }
            }
        }

        bool TrySpreadDownwards(int b, int x, int y, int z, U u)
        {
            int below = Fl(x, y - 1, z);
            // hook: H1/H3 (Hydrology): flowing water on a same-liquid source does not fall into it; "false" lets it spread sideways
            if (R.H1DrainGuard && !F.IsSource(b) && F.IsSource(below) && F.Same(b, below))
            {
                Stats.DrainsRefused++;
                return !R.H3SpreadOnLowerWater;
            }
            if (CanSpreadInto(b, x, y, z, x, y - 1, z, Dir.Down))
            {
                bool keepSource = false;
                if (F.IsSource(b))
                {
                    if (CountSources(x, y - 1, z, b) > 1) keepSource = true;
                    else if (Solid(x, y - 2, z) || F.IsSource(Fl(x, y - 2, z)))
                        keepSource = CountSources(x, y, z, b) >= 2;
                }
                SpreadLiquid(keepSource ? b : F.Make(F.Kind(b), F.D, 6), x, y - 1, z, u);
                if (!u.Multiply) RemoveOtherLowerNeighbours(b, x, y, z, Dir.Down, u);
                return true;
            }
            if (F.IsSource(b)) return !F.IsSource(below);
            return true;
        }

        void TrySpreadHorizontal(int b, int x, int y, int z, U u)
        {
            if (!u.Multiply)
            {
                int sh = ShuffleTable[Mod(MurmurHash3(x, y, z), ShuffleTable.Length)];
                int chosen = -1;
                for (int i = 0; i < 4; i++)
                {
                    int h = (sh >> (i * 2)) & 3;
                    if (TrySpreadInto(b, x, y, z, h, u)) { chosen = h; break; }
                }
                if (chosen >= 0) RemoveOtherLowerNeighbours(b, x, y, z, chosen, u);
            }
            else
            {
                for (int h = 0; h < 4; h++) TrySpreadInto(b, x, y, z, h, u);
            }
        }

        bool TrySpreadInto(int b, int x, int y, int z, int h, U u)
        {
            int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
            if (!CanSpreadInto(b, x, y, z, nx, y, nz, h)) return false;
            if (R.ProtoSubLevels > 0) // hook: sub-levels, one potential less per block
            {
                FromPot(PotAt(x, y, z) - 1, R.ProtoSubLevels, out int lv, out int sb);
                SetBulk(nx, y, nz, LiquidId(nx, y, nz, b, lv), sb);
                if (lv > 0) Register(nx, y, nz, u.Kind, u.Delay);
                return true;
            }
            SpreadLiquid(LiquidId(nx, y, nz, b, F.Level(b) - 1), nx, y, nz, u);
            return true;
        }

        void SpreadLiquid(int id, int x, int y, int z, U u)
        {
            SetBulk(x, y, z, id);
            if (id != 0) Register(x, y, z, u.Kind, u.Delay);
        }

        void UpdateNeighbouringLiquids(int x, int y, int z, int skipX, int skipZ, int higher, U u)
        {
            readStaged = true;
            int c = FlBulk(x, y - 1, z);
            if (c != 0) Register(x, y - 1, z, F.Kind(c), u.Delay);
            c = FlBulk(x, y + 1, z);
            if (c != 0) Register(x, y + 1, z, F.Kind(c), u.Delay);
            for (int k = 1; k <= 8; k++)
            {
                int nx = x + F.FlowDX[k], nz = z + F.FlowDZ[k];
                if (nx == skipX && nz == skipZ) continue;
                c = FlBulk(nx, y, nz);
                if (higher == 0 || F.Level(c) < F.Level(higher))
                    if (c != 0) Register(nx, y, nz, F.Kind(c), u.Delay);
            }
        }

        void UpdateOwnFlowDir(int x, int y, int z)
        {
            int b = Fl(x, y, z);
            int id = LiquidId(x, y, z, b, F.Level(b));
            if (id == b) return;
            SetDirect(x, y, z, id);
            // hook: the candidate wake rule
            if (R.WakeOnFallLabel && F.Flow(id) == F.D && F.Flow(b) != F.D)
                for (int h = 0; h < 4; h++)
                {
                    int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                    int c = Fl(nx, y, nz);
                    if (F.Same(c, b) && !F.IsSource(c) && F.Level(c) < F.Level(b)) Wake(nx, y, nz);
                }
        }

        /// <summary>TryLoweringLiquidLevel.</summary>
        bool TryLowering(int b, int x, int y, int z, U u)
        {
            if (F.IsSource(b)) return false;
            int level = F.Level(b);
            if (R.ProtoSubLevels > 0) // hook: sub-levels, fed by a neighbour exactly one potential higher (or water above)
            {
                int k = R.ProtoSubLevels, p = PotAt(x, y, z), maxP = 0;
                bool fed = false;
                int above = Fl(x, y + 1, z);
                if (F.Same(b, above) && AboveFeeds(b, x, y, z, above)) return false;
                for (int h = 0; h < 4; h++)
                {
                    int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                    int c = Fl(nx, y, nz);
                    if (!F.Same(b, c)) continue;
                    if (!R.FallingFeedsSideways && FallingInAir(c, x + Dir.HX[h], y, z + Dir.HZ[h])) continue;
                    int pc = Pot(c, W.Idx(nx, y, nz));
                    if (pc == p + 1) fed = true;
                    maxP = Math.Max(maxP, pc);
                }
                if (maxP > p && fed) return false;
                FromPot(p - 1, k, out int lv, out int sb);
                SetBulk(x, y, z, LiquidId(x, y, z, b, lv), sb);
                if (lv > 0) Register(x, y, z, u.Kind, u.Delay);
                for (int f = 0; f < 6; f++) Wake(x + Dir.AllX[f], y + Dir.AllY[f], z + Dir.AllZ[f]);
                return true;
            }
            if (MaxNeighbourLevel(b, x, y, z, out bool feeds) <= level || !feeds)
            {
                LowerAndNotify(b, x, y, z, u);
                return true;
            }
            if (!u.Multiply && F.Flow(b) != F.D)
            {
                for (int h = 0; h < 4; h++)
                {
                    int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                    if (HasBetterFlowDirection(nx, y, nz, b) && HasOneFeed(x, y, z, b, Dir.Opposite(h)))
                    {
                        LowerAndNotify(b, x, y, z, u);
                        return true;
                    }
                }
            }
            return false;
        }

        void LowerAndNotify(int b, int x, int y, int z, U u)
        {
            SpreadLiquid(LiquidId(x, y, z, b, F.Level(b) - 1), x, y, z, u);
            for (int f = 0; f < 6; f++) Wake(x + Dir.AllX[f], y + Dir.AllY[f], z + Dir.AllZ[f]);
        }

        bool HasBetterFlowDirection(int x, int y, int z, int ours)
        {
            int c = Fl(x, y, z);
            if (!F.Same(ours, c)) return false;
            if (F.Flow(c) != F.D && F.Level(c) < 6) return false;
            // nothing solid under that neighbour: it can fall (water under it counts as no barrier)
            return !Solid(x, y - 1, z);
        }

        bool HasOneFeed(int x, int y, int z, int ours, int skip)
        {
            int n = 0;
            for (int h = 0; h < 4; h++)
            {
                // the skipped side reads the block two above this cell (PosUtil.GetBlockAbove(pos, 2), default layer)
                int c = h == skip ? (Solid(x, y + 2, z) ? 0 : Fl(x, y + 2, z)) : Fl(x + Dir.HX[h], y, z + Dir.HZ[h]);
                if (F.Same(c, ours) && F.Level(c) > F.Level(ours)) n++;
            }
            return n == 1;
        }

        /// <summary>GetMaxNeighbourLiquidLevel: water of the liquid above feeds the cell fully (7).</summary>
        int MaxNeighbourLevel(int b, int x, int y, int z, out bool feeds)
        {
            int above = Fl(x, y + 1, z);
            if (F.Same(b, above) && AboveFeeds(b, x, y, z, above))
            {
                feeds = true;
                return 7;
            }
            int max = 0, level = F.Level(b);
            feeds = false;
            for (int h = 0; h < 4; h++)
            {
                int c = Fl(x + Dir.HX[h], y, z + Dir.HZ[h]);
                if (!F.Same(b, c)) continue;
                if (!R.FallingFeedsSideways && FallingInAir(c, x + Dir.HX[h], y, z + Dir.HZ[h])) continue; // hook: the candidate feed rule
                int l = F.Level(c);
                if (l == level + 1) feeds = true;
                max = Math.Max(max, l);
            }
            return max;
        }

        /// <summary>
        /// FindDownwardPaths: cells within the 7 x 7 square whose block below lets liquid in, which have no liquid at least as
        /// high, and from which a path of open cells leads back to this cell (BfsSearchPath, with vanilla's shared queue).
        /// </summary>
        List<Path> FindDownwardPaths(int b, int x, int y, int z)
        {
            var list = new List<Path>();
            var queue = new Queue<(int x, int z)>();
            int level = F.Level(b), min = 99;
            bool rapid = F.IsRapid(b);
            foreach (var (dx, dz) in DownPaths)
            {
                int px = x + dx, pz = z + dz;
                if (Solid(px, y - 1, pz)) continue;
                int at = Fl(px, y, pz);
                if (F.Level(at) >= level || Solid(px, y, pz)) continue;
                if (rapid && F.Kind(at) == F.Water) continue;
                queue.Enqueue((px, pz));
                var step = BfsSearchPath(queue, x, z, level);
                if (step == null) continue;
                int dist = Math.Abs(dx) + Math.Abs(dz);
                var p = new Path { X = step.Value.x, Z = step.Value.z, Dist = dist };
                if (dist == 1 && level < 7)
                {
                    list.Clear();
                    list.Add(p);
                    return list;
                }
                list.Add(p);
                min = Math.Min(min, dist);
            }
            list.RemoveAll(p => p.Dist > min);
            return list;

            (int x, int z)? BfsSearchPath(Queue<(int x, int z)> q, int ox, int oz, int lvl)
            {
                while (q.Count > 0)
                {
                    var (cx, cz) = q.Dequeue();
                    int num = Math.Abs(cx - ox) + Math.Abs(cz - oz);
                    bool openHere = !Solid(cx, y, cz);
                    for (int h = 0; h < 4; h++)
                    {
                        int nx = cx + Dir.HX[h], nz = cz + Dir.HZ[h];
                        int num2 = Math.Abs(nx - ox) + Math.Abs(nz - oz);
                        // a stone side (1) always blocks; an open side (0) blocks once the liquid would have run out
                        bool allowed = lvl - num > 0;
                        if (num2 > num || !openHere || Solid(nx, y, nz) || !allowed) continue;
                        if (num2 == 0) return (cx, cz);
                        q.Enqueue((nx, nz));
                    }
                }
                return null;
            }
        }

        /// <summary>CanSpreadIntoBlock (the layered overload), for full stone blocks.</summary>
        bool CanSpreadInto(int b, int x, int y, int z, int nx, int ny, int nz, int face)
        {
            int level = F.Level(b);
            if (Solid(x, y, z) || Solid(nx, ny, nz)) return false;
            int c = Fl(nx, ny, nz);
            if (R.ProtoSubLevels > 0 && face < 4 && F.Same(b, c) && F.FlowRate(c) == F.FlowRate(b))
                return Pot(c, W.Idx(nx, ny, nz)) < PotAt(x, y, z) - 1; // hook: sub-levels
            if (F.Same(b, c))
            {
                if (!F.IsSource(b))
                {
                    int rn = F.FlowRate(c), ro = F.FlowRate(b);
                    if (rn == ro)
                    {
                        if (F.Level(c) >= level) return face == Dir.Down && F.Flow(c) != F.D;
                        return true;
                    }
                    if (ro < rn)
                    {
                        if (F.Level(c) <= level) return true;
                        if (ny < y) return true;
                    }
                    return false;
                }
                return F.Level(c) < level;
            }
            if (F.IsSource(c)) return false; // another liquid's source (no collisions between the water liquids)
            if (c != 0) return true;          // another liquid's flowing water: same replaceability
            if (level <= 1) return face == Dir.Down || (R.ProtoSubLevels > 0 && PotAt(x, y, z) > 1);
            return true;
        }

        /// <summary>GetLiquidBlockId: the flow label from the 8 neighbours' heights.</summary>
        public int LiquidId(int x, int y, int z, int b, int level)
        {
            if (level < 1) return 0;
            int vx = 0, vz = 0;
            bool open = false;
            for (int k = 1; k <= 8; k++)
            {
                int nx = x + F.FlowDX[k], nz = z + F.FlowDZ[k];
                int c = Fl(nx, y, nz);
                if (c == 0 || F.Level(c) == level) continue;
                int sx = F.Level(c) < level ? F.FlowDX[k] : -F.FlowDX[k];
                int sz = F.Level(c) < level ? F.FlowDZ[k] : -F.FlowDZ[k];
                if ((k & 1) == 1) open |= !Solid(nx, y, nz) || !Solid(x, y, z);
                vx += sx; vz += sz;
            }
            if (Math.Abs(vx) > Math.Abs(vz)) vz = 0;
            else if (Math.Abs(vz) > Math.Abs(vx)) vx = 0;
            vx = Math.Sign(vx); vz = Math.Sign(vz);
            int flow;
            if (vx == 0 && vz == 0)
            {
                int below = Fl(x, y - 1, z), above = Fl(x, y + 1, z);
                if ((F.Same(below, b) && F.Flow(below) == F.D) || (F.Same(above, b) && F.Flow(above) == F.D)) flow = F.D;
                else if (open) flow = F.D;
                else flow = F.Still;
            }
            else
            {
                flow = 0;
                for (int k = 1; k <= 8; k++) if (F.FlowDX[k] == vx && F.FlowDZ[k] == vz) flow = k;
            }
            flow = LabelHook(x, y, z, flow, level); // hook: the candidate label rule
            return F.Make(F.Kind(b), flow, level);
        }

        static int Mod(int k, int n) { int r = k % n; return r < 0 ? r + n : r; }

        static uint Rotl(uint x, int r) => (x << r) | (x >> (32 - r));

        static uint Fmix(uint h)
        {
            h = (h ^ (h >> 16)) * 2246822507u;
            h = (h ^ (h >> 13)) * 3266489909u;
            return h ^ (h >> 16);
        }

        /// <summary>GameMath.MurmurHash3 of three ints (seed 0x90).</summary>
        static int MurmurHash3(int x, int y, int z)
        {
            uint Mix(uint h, int k) => Rotl(h ^ (Rotl((uint)(k * -862048943), 15) * 461845907), 13) * 5 + unchecked((uint)-430675100);
            uint h = Mix(0x90, x);
            h = Mix(h, y);
            h = Mix(h, z);
            return (int)Fmix(h ^ 3);
        }
    }
}
