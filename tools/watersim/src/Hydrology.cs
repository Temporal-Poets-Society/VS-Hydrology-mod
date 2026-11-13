using System;
using System.Collections.Generic;

namespace WaterSim
{
    /// <summary>
    /// Hydrology 0.1.3's rules (src/Rules, f6f1f25) on top of the vanilla port, each behind a switch of <see cref="RuleSet"/>,
    /// and the candidate rules of the study. The hook points are those of H5LiquidHooks: before and after a cell's
    /// delayed update, and inside vanilla's TrySpreadDownwards and RemoveOtherLowerNeighbours (in Vanilla.cs).
    /// </summary>
    public sealed partial class Sim
    {
        // ---------------------------------------------------------------- H5: around the update

        void BeforeUpdate(int x, int y, int z, ref U u)
        {
            if (u.Multiply || !R.H4Rapids) return;
            if (R.H4Sheets && SpreadsAsSheet(x, y, z)) u.Multiply = true;
            int b = Fl(x, y, z);
            if (R.RapidSourceSheet && F.IsRapid(b) && F.IsSource(b) && !Lone(x, y, z, b)) u.Multiply = true;
        }

        void AfterUpdate(int x, int y, int z, int before, bool multiply = true)
        {
            int b = Fl(x, y, z);
            if (R.WakeLowerSides && b != 0)
                for (int h = 0; h < 4; h++)
                {
                    int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                    int c = Fl(nx, y, nz);
                    if (F.Same(c, b) && !F.IsSource(c) && F.Level(c) < F.Level(b) - 1) Wake(nx, y, nz);
                }
            if (b == 0)
            {
                if (R.H4Rapids) RecheckEdgesAbove(x, y + 1, z);
                if (R.WakeSourcesBeside) WakeSourcesBesideCell(x, y, z);
                return;
            }
            if (F.IsSource(b))
            {
                if (R.Survival && !SourceSurvives(x, y, z, b))
                {
                    SetAndWake(x, y, z, LiquidId(x, y, z, b, 6), true);
                    Stats.SurvivalDrained++;
                    return;
                }
                if (R.WakeOnNewSource && !F.IsSource(before)) QueueNeighbourUpdate(x, y, z);
                if (R.H4Rapids)
                {
                    if (R.WakeEdgesOnNewSource && !F.IsSource(before)) WakeEdgesAround(x, y, z);
                    CheckSource(x, y, z, b);
                    Propagate(x, y, z);
                }
                return;
            }
            if (R.H3FallsInAir && FallsInAir(x, y, z, b)) b = Fl(x, y, z);
            if (FillOverride != null ? FillOverride(x, y, z, b) : TryFill(x, y, z, b)) return;
            // not after vanilla's single stream (rapid water that is no sheet: lone rivulets), which lowers cells on purpose
            if (R.LevelRelax && multiply && !(F.Same(before, b) && F.Level(before) > F.Level(b)) && Relax(x, y, z, b)) return;
            if (R.H4Rapids)
            {
                FollowFeeder(x, y, z);
                Propagate(x, y, z);
                RecheckEdgesAbove(x, y, z);
            }
        }

        /// <summary>Liquids.SetAndWake: a direct write, the cell's own update, and (optionally) a queued neighbour update.</summary>
        void SetAndWake(int x, int y, int z, int id, bool neighbours)
        {
            SetDirect(x, y, z, id);
            RuleWrites++;
            Wake(x, y, z);
            if (neighbours) QueueNeighbourUpdate(x, y, z);
        }

        void WakeSourcesBesideCell(int x, int y, int z)
        {
            for (int h = 0; h < 4; h++)
            {
                int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                if (F.IsSource(Fl(nx, y, nz))) Wake(nx, y, nz);
            }
        }

        // ---------------------------------------------------------------- candidates

        /// <summary>The feed rule: vanilla counts any water of the liquid above as feeding the cell.</summary>
        bool AboveFeeds(int b, int x, int y, int z, int above)
        {
            switch (R.AboveFeeds)
            {
                case 1: return F.IsSource(above) || F.Flow(above) != F.Still;
                case 2: return F.IsSource(above) || F.Flow(above) == F.D;
                default: return true;
            }
        }

        /// <summary>Survival: supported from below (solid, or a same-liquid source) and contained on its four sides (solid,
        /// or water of any kind at that level: a source, or water it flows into). Open air at its own level is not.</summary>
        bool SourceSurvives(int x, int y, int z, int b)
        {
            int below = Fl(x, y - 1, z);
            bool supported = Solid(x, y - 1, z) || (R.SurvivalSourceSupports && F.Same(below, b) && F.IsSource(below));
            if (!supported) return false;
            if (R.SurvivalContained)
                for (int h = 0; h < 4; h++)
                {
                    int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                    if (Solid(nx, y, nz)) continue;
                    if (Fl(nx, y, nz) == 0) return false;
                }
            if (R.SurvivalBirth == 0 || Solid(x, y - 1, z)) return true;
            // resting on water: birth's neighbours, or walls on every side that is not a source (a shaft)
            var src = new bool[4];
            var wet = new bool[4];
            int sources = 0;
            bool walled = true;
            for (int h = 0; h < 4; h++)
            {
                int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                int c = Fl(nx, y, nz);
                if (F.Same(c, b)) { wet[h] = true; if (F.IsSource(c)) { src[h] = true; sources++; } }
                if (!Solid(nx, y, nz) && !(F.Same(c, b) && F.IsSource(c))) walled = false;
            }
            if (walled || sources >= 2) return true;
            if (R.SurvivalBirth == 2) return false;
            for (int h = 0; h < 4; h++)
            {
                int j = (h + 1) % 4;
                if (!(src[h] && wet[j]) && !(src[j] && wet[h])) continue;
                int d = Fl(x + Dir.HX[h] + Dir.HX[j], y, z + Dir.HZ[h] + Dir.HZ[j]);
                if (F.IsSource(d) && F.Same(d, b)) return true;
            }
            return false;
        }

        /// <summary>Falling water still in the air (nothing solid and no source under it): it feeds only the cell below.
        /// Falling water that has landed spreads sideways and feeds what it spreads into.</summary>
        bool FallingInAir(int c, int x, int y, int z) =>
            F.Flow(c) == F.D && !F.IsSource(c) && !Solid(x, y - 1, z) && !F.IsSource(Fl(x, y - 1, z));

        /// <summary>The level rule: raise a flowing cell to what its feeders give it.</summary>
        bool Relax(int x, int y, int z, int b)
        {
            if (LocallyOff(x, z)) return false;
            int target;
            int above = Fl(x, y + 1, z);
            if (F.Same(above, b) && AboveFeeds(b, x, y, z, above)) target = 6;
            else
            {
                int max = 0;
                for (int h = 0; h < 4; h++)
                {
                    int c = Fl(x + Dir.HX[h], y, z + Dir.HZ[h]);
                    if (!F.Same(c, b)) continue;
                    if (!R.FallingFeedsSideways && FallingInAir(c, x + Dir.HX[h], y, z + Dir.HZ[h])) continue;
                    max = Math.Max(max, F.Level(c));
                }
                target = max - 1;
            }
            if (target > 6) target = 6;
            if (F.Level(b) >= target) return false;
            SetAndWake(x, y, z, LiquidId(x, y, z, b, target), true);
            Stats.Relaxed++;
            return true;
        }

        /// <summary>The label rule, inside GetLiquidBlockId: water resting on neither ground nor a source is falling water.</summary>
        int LabelHook(int x, int y, int z, int flow, int level)
        {
            if (!R.FallLabel || level >= 7 || flow == F.D || Solid(x, y - 1, z)) return flow;
            if (F.IsSource(Fl(x, y - 1, z))) return flow;
            return F.D;
        }

        // ---------------------------------------------------------------- H2: filling

        bool TryFill(int x, int y, int z, int b)
        {
            if (!R.H2Fill || F.IsSource(b) || LocallyOff(x, z)) return false;
            int below = Fl(x, y - 1, z);
            bool ground = Solid(x, y - 1, z);
            bool belowSource = F.Same(below, b) && F.IsSource(below);
            bool onSource = R.FillOnSource && belowSource;
            bool refill = R.RefillOnSource > 0 && belowSource;
            if (R.FillVessels && belowSource && !ground && (!R.VesselsContained || Contained(x, y, z, b))
                && !(R.QueryNoVessels && FlowQuery.Current != null && FlowQuery.Current.River(x, z))
                && (!R.VesselsCalm || !((R.VesselsCalmMask & 1) != 0 && F.IsRapid(b) || (R.VesselsCalmMask & 2) != 0 && F.IsRapid(below))))
                for (int h = 0; h < 4; h++)
                {
                    int sx = x + Dir.HX[h], sz = z + Dir.HZ[h];
                    int side = Fl(sx, y, sz), sideBelow = Fl(sx, y - 1, sz);
                    if (F.IsSource(side) && F.Same(side, b) && F.IsSource(sideBelow) && F.Same(sideBelow, b)
                        && (!R.VesselsDepth || SourceDepth(sx, y - 1, sz, b) >= SourceDepth(x, y - 1, z, b) + (R.VesselsRapidDeeper && F.IsRapid(side) ? 1 : 0))
                        && (!R.VesselsCalm || !((R.VesselsCalmMask & 4) != 0 && F.IsRapid(side) || (R.VesselsCalmMask & 8) != 0 && F.IsRapid(sideBelow))))
                    {
                        SetAndWake(x, y, z, F.Make(F.Kind(b) == F.Salt ? F.Salt : F.Water, F.Still, 7), true);
                        Stats.Filled++;
                        return true;
                    }
                }
            if (!ground && !onSource && !refill) return false;
            var src = new bool[4];
            var wet = new bool[4];
            int sources = 0;
            for (int h = 0; h < 4; h++)
            {
                int c = Fl(x + Dir.HX[h], y, z + Dir.HZ[h]);
                if (!F.Same(c, b)) continue;
                wet[h] = true;
                if (F.IsSource(c)) { src[h] = true; sources++; }
            }
            bool fill = (ground || onSource) && sources >= R.FillSources;
            if (refill && sources >= R.RefillOnSource) fill = true;
            for (int h = 0; h < 4 && !fill && R.FillDiagonal && (ground || onSource); h++)
            {
                int j = (h + 1) % 4;
                if (!(src[h] && wet[j]) && !(src[j] && wet[h])) continue;
                int d = Fl(x + Dir.HX[h] + Dir.HX[j], y, z + Dir.HZ[h] + Dir.HZ[j]);
                fill = F.IsSource(d) && F.Same(d, b);
            }
            if (!fill) return false;
            int source = F.Make(F.Kind(b) == F.Salt ? F.Salt : F.Water, F.Still, 7);
            SetAndWake(x, y, z, source, true);
            Stats.Filled++;
            if (R.WakeEdgesOnNewSource && R.H4Rapids) WakeEdgesAround(x, y, z);
            return true;
        }

        /// <summary>Same-liquid sources straight down from y (at most 16).</summary>
        int SourceDepth(int x, int y, int z, int b)
        {
            int n = 0;
            for (; n < 16; n++)
            {
                int c = Fl(x, y - n, z);
                if (!F.IsSource(c) || !F.Same(c, b)) break;
            }
            return n;
        }

        /// <summary>No side of the cell is a dry ledge: each is solid, water of its liquid, or open over water of its liquid.</summary>
        bool Contained(int x, int y, int z, int b)
        {
            for (int h = 0; h < 4; h++)
            {
                int sx = x + Dir.HX[h], sz = z + Dir.HZ[h];
                if (Solid(sx, y, sz) || F.Same(Fl(sx, y, sz), b)) continue;
                if (!F.Same(Fl(sx, y - 1, sz), b)) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- H3: falling water in the air

        bool FallsInAir(int x, int y, int z, int b)
        {
            int top = y, bottom = y;
            for (int i = 0; i < R.MaxDropScan; i++)
            {
                int f = Fl(x, top + 1, z);
                if (!F.Same(f, b) || F.IsSource(f)) break;
                top++;
            }
            for (int i = 0; i < R.MaxDropScan; i++)
            {
                int f = Fl(x, bottom - 1, z);
                if (!F.Same(f, b) || F.IsSource(f)) break;
                bottom--;
            }
            int changed = 0;
            for (int yy = top; yy >= bottom; yy--)
            {
                int c = Fl(x, yy, z);
                if (F.Flow(c) != F.Still) continue;
                if (!F.Same(Fl(x, yy + 1, z), c)) continue;
                if (Solid(x, yy - 1, z)) continue;
                int under = Fl(x, yy - 1, z);
                if (under != 0 && (F.IsSource(under) || !F.Same(under, c))) continue;
                SetDirect(x, yy, z, F.WithFlow(c, F.D));
                RuleWrites++;
                changed++;
            }
            if (changed == 0) return false;
            Wake(x, bottom, z);
            Stats.MadeFalling += changed;
            return true;
        }

        // ---------------------------------------------------------------- H4: rapids

        int DropAt(int x, int y, int z, out bool onWater)
        {
            onWater = false;
            for (int d = 1; d <= R.MaxDropScan; d++)
            {
                int yy = y - d;
                if (yy < 1) return d - 1;
                if (Solid(x, yy, z)) return d - 1;
                int f = Fl(x, yy, z);
                if (f != 0 && F.IsSource(f)) { onWater = true; return d; }
            }
            return R.MaxDropScan;
        }

        bool IsEdge(int x0, int y0, int z0)
        {
            int reach = R.RapidReach, drop = R.RapidDrop;
            var down = new int[reach + 1];
            for (int d = 0; d <= reach; d++) down[d] = int.MaxValue;
            var seen = new HashSet<(int, int)> { (x0, z0) };
            var queue = new Queue<(int x, int z, int level, bool wet, int dist)>();
            for (int h = 0; h < 4; h++)
            {
                int nx = x0 + Dir.HX[h], nz = z0 + Dir.HZ[h];
                if (Solid(nx, y0, nz)) continue;
                int f = Fl(nx, y0, nz);
                if (f != 0 && F.IsSource(f)) continue;
                seen.Add((nx, nz));
                int dr = DropAt(nx, y0, nz, out bool onWater);
                queue.Enqueue((nx, nz, y0 - dr, onWater || dr == 0, 1));
            }
            if (queue.Count == 0) return false;
            while (queue.Count > 0)
            {
                var (x, z, level, wet, dist) = queue.Dequeue();
                down[dist] = Math.Min(down[dist], level);
                if (dist == reach || !wet) continue;
                for (int h = 0; h < 4; h++)
                {
                    int bx = x + Dir.HX[h], bz = z + Dir.HZ[h];
                    if (seen.Contains((bx, bz))) continue;
                    if (Solid(bx, level, bz)) continue;
                    seen.Add((bx, bz));
                    int f = Fl(bx, level, bz);
                    if (f != 0 && F.IsSource(f)) queue.Enqueue((bx, bz, level, true, dist + 1));
                    else
                    {
                        int dr = DropAt(bx, level, bz, out bool landsOnWater);
                        queue.Enqueue((bx, bz, level - dr, landsOnWater || dr == 0, dist + 1));
                    }
                }
            }
            for (int d = 2; d <= reach; d++) down[d] = Math.Min(down[d], down[d - 1]);
            if (y0 - down[1] >= drop) return true;

            var up = new int[reach];
            for (int d = 0; d < reach; d++) up[d] = y0;
            int b = Fl(x0, y0, z0);
            var seenUp = new HashSet<(int, int)> { (x0, z0) };
            var queueUp = new Queue<(int x, int z, int level, int dist)>();
            queueUp.Enqueue((x0, z0, y0, 0));
            while (queueUp.Count > 0)
            {
                var (x, z, level, dist) = queueUp.Dequeue();
                up[dist] = Math.Max(up[dist], level);
                if (dist == reach - 1) continue;
                for (int h = 0; h < 4; h++)
                {
                    int bx = x + Dir.HX[h], bz = z + Dir.HZ[h];
                    if (seenUp.Contains((bx, bz))) continue;
                    int surface = Surface(bx, level, bz, b);
                    if (surface < level) continue;
                    seenUp.Add((bx, bz));
                    queueUp.Enqueue((bx, bz, surface, dist + 1));
                }
            }
            for (int d = 1; d < reach; d++) up[d] = Math.Max(up[d], up[d - 1]);
            for (int i = 0; i < reach; i++)
                for (int j = 1; i + j <= reach; j++)
                    if (up[i] - down[j] >= drop) return true;
            return false;
        }

        int Surface(int x, int level, int z, int b)
        {
            for (int y = level; y <= level + R.RapidDrop; y++)
            {
                if (!F.Same(Fl(x, y, z), b)) continue;
                while (F.Same(Fl(x, y + 1, z), b)) y++;
                return y;
            }
            return int.MinValue;
        }

        void CheckSource(int x, int y, int z, int b)
        {
            if (!F.IsFresh(b) || !F.IsSource(b)) return;
            if (R.QueryRapids && FlowQuery.Current != null && FlowQuery.Current.Rapids(x, z))
            {
                // a rapids reach by the query: the surface source is rapid and stays rapid; covered sources are left alone
                if (!F.IsRapid(b) && !F.Same(Fl(x, y + 1, z), b))
                {
                    SetAndWake(x, y, z, F.Make(F.Rapid, F.Still, 7), true);
                    Stats.RapidSources++;
                }
                return;
            }
            bool edge = IsEdge(x, y, z);
            bool rapid = F.IsRapid(b);
            if (edge && !rapid)
            {
                SetAndWake(x, y, z, F.Make(F.Rapid, F.Still, 7), true);
                Stats.RapidSources++;
                WakeEdgesAround(x, y, z);
            }
            else if (!edge && rapid && !Lone(x, y, z, b))
            {
                SetAndWake(x, y, z, F.Make(F.Water, F.Still, 7), true);
                Stats.RapidReverted++;
                WakeEdgesAround(x, y, z);
            }
        }

        void WakeEdgesAround(int x, int y, int z)
        {
            int reach = R.RapidReach;
            for (int dx = -reach; dx <= reach; dx++)
                for (int dz = -reach; dz <= reach; dz++)
                {
                    if (Math.Abs(dx) + Math.Abs(dz) > reach || (dx == 0 && dz == 0)) continue;
                    for (int dy = -4; dy <= 4; dy++)
                    {
                        int px = x + dx, py = y + dy, pz = z + dz;
                        int f = Fl(px, py, pz);
                        if (!F.IsFresh(f) || !F.IsSource(f)) continue;
                        if (!F.IsRapid(f) && !HasOpenSide(px, py, pz)) continue;
                        Wake(px, py, pz);
                    }
                }
        }

        bool HasOpenSide(int x, int y, int z)
        {
            for (int h = 0; h < 4; h++)
            {
                int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                if (Solid(nx, y, nz)) continue;
                int f = Fl(nx, y, nz);
                if (!(f != 0 && F.IsSource(f))) return true;
            }
            return false;
        }

        bool Lone(int x, int y, int z, int b)
        {
            if (R.LoneVertical)
                for (int dy = -1; dy <= 1; dy += 2)
                {
                    int f = Fl(x, y + dy, z);
                    if (F.IsSource(f) && F.Same(f, b)) return false;
                }
            for (int h = 0; h < 4; h++)
            {
                int f = Fl(x + Dir.HX[h], y, z + Dir.HZ[h]);
                if (F.IsSource(f) && F.Same(f, b)) return false;
            }
            return true;
        }

        bool SpreadsAsSheet(int x, int y, int z)
        {
            int b = Fl(x, y, z);
            if (!F.IsRapid(b) || F.IsSource(b)) return false;
            if (F.Same(Fl(x, y - 1, z), b)) return true;
            for (int h = 0; h < 4; h++)
            {
                int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                int f = Fl(nx, y, nz);
                if (!F.Same(f, b)) continue;
                if (F.Level(f) == F.Level(b)) return true;
                if (F.IsSource(f) && F.IsRapid(f) && !Lone(nx, y, nz, f)) return true;
            }
            return false;
        }

        void FollowFeeder(int x, int y, int z)
        {
            if (!R.H4FollowFeeder) return;
            int b = Fl(x, y, z);
            if (!F.IsFresh(b) || F.IsSource(b)) return;
            bool? want = WantRapid(x, y, z, b, out var ordinarySource);
            if (want == false && F.IsRapid(b) && ordinarySource != null)
            {
                var (sx, sy, sz) = ordinarySource.Value;
                CheckSource(sx, sy, sz, Fl(sx, sy, sz));
                want = WantRapid(x, y, z, b, out _);
            }
            if (want == null || want == F.IsRapid(b)) return;
            SetAndWake(x, y, z, F.WithKind(b, want.Value ? F.Rapid : F.Water), false);
            if (want.Value) Stats.MadeRapid++; else Stats.MadeOrdinary++;
        }

        bool? WantRapid(int x, int y, int z, int b, out (int, int, int)? ordinarySource)
        {
            ordinarySource = null;
            if (R.QueryRapids && FlowQuery.Current != null && FlowQuery.Current.Rapids(x, z)) return true;
            int above = Fl(x, y + 1, z);
            if (F.Same(above, b)) return F.IsRapid(above);
            bool? want = null;
            int best = F.Level(b);
            for (int h = 0; h < 4; h++)
            {
                int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                int f = Fl(nx, y, nz);
                if (!F.Same(f, b) || F.Level(f) <= F.Level(b)) continue;
                if (F.Level(f) > best) { best = F.Level(f); want = F.IsRapid(f); }
                else if (F.Level(f) == best && F.IsRapid(f)) want = true;
                if (F.IsSource(f) && !F.IsRapid(f)) ordinarySource = (nx, y, nz);
            }
            return want;
        }

        void Propagate(int x, int y, int z)
        {
            FollowFeeder(x, y - 1, z);
            for (int h = 0; h < 4; h++) FollowFeeder(x + Dir.HX[h], y, z + Dir.HZ[h]);
        }

        void RecheckEdgesAbove(int x, int y, int z)
        {
            int b = Fl(x, y, z);
            if (!F.IsFresh(b) || F.IsSource(b)) return;
            int top = y;
            for (int i = 0; i < R.MaxDropScan; i++)
            {
                int up = Fl(x, top + 1, z);
                if (!F.Same(up, b) || F.IsSource(up)) break;
                top++;
            }
            for (int h = 0; h < 4; h++)
            {
                int nx = x + Dir.HX[h], nz = z + Dir.HZ[h];
                int f = Fl(nx, top, nz);
                if (F.IsFresh(f) && F.IsSource(f)) CheckSource(nx, top, nz, f);
            }
        }
    }
}
