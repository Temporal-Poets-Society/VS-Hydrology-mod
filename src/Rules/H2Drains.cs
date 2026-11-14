using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// H2 release, part 2 (HY-0018 split): the drains that let an opened pond's water out over time, their tick, and the
    /// save of drains and river pools. The rule itself: H2Release.cs.
    /// </summary>
    public static partial class H2Release
    {
        /// <summary>A pond letting its water out through its openings.</summary>
        sealed class Drain
        {
            public int dim, sill, rounds;
            public Block liquid;
            public int sourceId;
            public readonly List<(int x, int y, int z)> openings = new();
            /// <summary>The first opening (the log's name for the drain, also once every opening is closed).</summary>
            public (int x, int y, int z) origin;
            public HashSet<(int x, int y, int z)> body;
            public List<(int x, int y, int z)> drained, filled;
            public int next, placed, lost;
            public double credit, age;
            public string why;
        }

        static readonly List<Drain> jobs = new();

        /// <summary>Cells a drain has emptied, which H2 does not fill again while it runs: (dimension, x, y, z).</summary>
        static readonly HashSet<(int, int, int, int)> draining = new();

        /// <summary>Drains read from the save, started again when their opening's chunk is loaded: dimension, liquid code,
        /// openings, rounds.</summary>
        static readonly List<(int dim, string liquid, List<(int x, int y, int z)> openings, int rounds, long notBefore, int tries)> pending = new();

        /// <summary>HY-0013: a saved drain whose body reaches unloaded land is asked again this often, this many times.</summary>
        const long PendingRetryMs = 5000;

        const int PendingTries = 60;

        /// <summary>A drain has emptied the cell: H2 leaves it (the pond's flowing water runs over it to the opening).</summary>
        public static bool Draining(BlockPos pos) => draining.Count > 0 && draining.Contains((pos.dimension, pos.X, pos.Y, pos.Z));

        /// <summary>Drains running (labs, the stats line).</summary>
        public static int Running => jobs.Count;

        /// <summary>Saved drains waiting for their land (the offline suite).</summary>
        public static int Pending => pending.Count;

        /// <summary>HY-0012: a drain's tick threw; it has stopped (the game hands this to the release breaker). Null: the
        /// exception is rethrown after every other drain has had its tick (the offline suite).</summary>
        public static System.Action<System.Exception> Fault;

        /// <summary>
        /// HY-0012: the water rules went off (a game update, their breaker): every drain ends where it stands, as when its
        /// openings are closed but without the layer it was draining (H5's guard on its holes is gone), and nothing is
        /// guarded any more. Returns the drains ended.
        /// </summary>
        public static int EndAll(IWorldAccessor world)
        {
            int n = jobs.Count;
            // Fable's pass: each running drain is kept as a saved one (asked again once the rules are back, or at the next
            // start), and the saved ones stay: the rules going off must not lose them from the save
            foreach (var job in jobs.ToArray())
            {
                pending.Add((job.dim, job.liquid.LiquidCode, new List<(int x, int y, int z)>(job.openings), job.rounds, 0, 0));
                Finish(world, job, false);
            }
            draining.Clear();
            return n;
        }

        /// <summary>A running drain has emptied a cell in one of these columns (a pool must not be planned over it).</summary>
        public static bool DrainingIn(int dim, List<(int x, int z)> cols)
        {
            if (draining.Count == 0) return false;
            var set = new HashSet<(int, int)>(cols);
            foreach (var (d, x, y, z) in draining) if (d == dim && set.Contains((x, z))) return true;
            return false;
        }

        /// <summary>Lets <paramref name="k"/> more sources out of the drain: each goes, and one stands at the plan's next cell
        /// (none when the plan is spent: the rest joined the sea, a river or a large lake).</summary>
        static void Step(IWorldAccessor world, Drain job, int k)
        {
            var ba = world.BlockAccessor;
            var p = new BlockPos(job.dim);
            int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
            void Grow(int x, int y, int z)
            {
                if (x < x0) x0 = x; if (x > x1) x1 = x;
                if (y < y0) y0 = y; if (y > y1) y1 = y;
                if (z < z0) z0 = z; if (z > z1) z1 = z;
            }
            for (int i = 0; i < k && job.next < job.drained.Count; i++)
            {
                var (x, y, z) = job.drained[job.next];
                if (!Liquids.Loaded(ba, p.Set(x, y, z))) break;
                // HY-0013: the cell this source will stand at is in an unloaded chunk: wait, the source stays in the pond
                if (job.placed < job.filled.Count)
                {
                    var (wx, wy, wz) = job.filled[job.placed];
                    if (!Liquids.Loaded(ba, p.Set(wx, wy, wz))) break;
                    p.Set(x, y, z);
                }
                job.next++;
                // gone already (a player's bucket, a block placed in it): nothing moves for it
                var f = Liquids.Fluid(ba, p);
                if (!Liquids.IsSource(f) || !Liquids.Same(f, job.liquid)) continue;
                Liquids.SetAndWake(world, p, 0);
                draining.Add((job.dim, x, y, z));
                Grow(x, y, z);
                H2Filling.ForgetAround(job.dim, x, y, z);
                HydrologyStats.ReleasedCells++;
                if (job.placed >= job.filled.Count) { HydrologyStats.ReleaseAbsorbed++; continue; }
                var (fx, fy, fz) = job.filled[job.placed++];
                p.Set(fx, fy, fz);
                if (!Liquids.Loaded(ba, p) || !Liquids.CanTake(ba, p, job.liquid))
                {
                    // the cell was built on or filled meanwhile: the water stands nowhere (counted)
                    job.lost++;
                    continue;
                }
                Liquids.SetAndWake(world, p, job.sourceId);
                Grow(fx, fy, fz);
                H2Filling.ForgetAround(job.dim, fx, fy, fz);
            }
            // (Fable re-review, finding 10: the memo is forgotten by search beside each changed cell above, not by a scan of
            // the whole memo every step)
        }

        /// <summary>The drain is spent: its cells may fill again; water standing at or above an opening again drains in
        /// a new round.</summary>
        static void Finish(IWorldAccessor world, Drain job, bool again)
        {
            jobs.Remove(job);
            var ba = world.BlockAccessor;
            var p = new BlockPos(job.dim);
            foreach (var (x, y, z) in job.openings) draining.Remove((job.dim, x, y, z));
            draining.Remove((job.dim, job.origin.x, job.origin.y, job.origin.z));
            // what H2 remembered about these cells while the drain guarded them (held, refused, asked again later) no longer
            // holds: the cells are asked afresh below
            int x0 = job.origin.x, y0 = job.origin.y, z0 = job.origin.z, x1 = x0, y1 = y0, z1 = z0;
            foreach (var (x, y, z) in job.drained)
            {
                if (x < x0) x0 = x; if (x > x1) x1 = x;
                if (y < y0) y0 = y; if (y > y1) y1 = y;
                if (z < z0) z0 = z; if (z > z1) z1 = z;
            }
            H2Filling.ForgetHeldNear(job.dim, x0 - MaxRun, y0 - MaxRun, z0 - MaxRun, x1 + MaxRun, y1 + MaxRun, z1 + MaxRun);
            foreach (var (x, y, z) in job.drained)
            {
                draining.Remove((job.dim, x, y, z));
                // flowing water left in an emptied cell is asked again: what still holds it there (a dam's lower course,
                // a lake feeding a channel) fills it
                var f = Liquids.Fluid(ba, p.Set(x, y, z));
                if (Liquids.Handles(f) && !Liquids.IsSource(f)) f.OnNeighbourBlockChange(world, p.Copy(), p.Copy());
            }
            Log?.Invoke($"Hydrology: a pond opened at {job.origin.x},{job.origin.y},{job.origin.z} has let out {job.next} sources" +
                        (job.lost > 0 ? $" ({job.lost} reached cells the stream had filled already)" : ""));
            if (!again || job.rounds + 1 >= MaxRounds) return;
            // the first opening that finds water above it starts the round; the others join it
            foreach (var (x, y, z) in job.openings) Opened(world, new BlockPos(x, y, z, job.dim), job.rounds + 1, true, job.openings);
        }

        /// <summary>Every second or so (the server's tick): each drain lets out what its openings pass in
        /// <paramref name="dt"/> seconds; drains read from the save start once their opening's chunk is loaded.</summary>
        public static void Tick(IWorldAccessor world, float dt)
        {
            var ba = world.BlockAccessor;
            var p = new BlockPos(0);
            long now = world.ElapsedMilliseconds;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var (dim, liquid, openings, rounds, notBefore, tries) = pending[i];
                if (now < notBefore) continue;
                p.dimension = dim;
                var (x, y, z) = openings[0];
                if (!Liquids.Loaded(ba, p.Set(x, y, z))) continue;
                pending.RemoveAt(i);
                bool started = false, unknown = false;
                try
                {
                    foreach (var o in openings)
                    {
                        if (Opened(world, new BlockPos(o.x, o.y, o.z, dim), rounds, true, openings) > 0) started = true;
                        unknown |= lastUnknown;
                    }
                }
                catch (System.Exception e)
                {
                    // Fable re-review: one saved drain's error no longer skips the running drains' tick
                    if (Fault != null) { Fault(e); continue; }
                    throw;
                }
                // HY-0013: the land round the pond is still loading: ask again in a few seconds (it was measured smaller than
                // it is, and a lake could have drained)
                if (!started && unknown)
                {
                    if (tries + 1 < PendingTries) pending.Add((dim, liquid, openings, rounds, now + PendingRetryMs, tries + 1));
                    else LogKept?.Invoke($"Hydrology: a saved drain at {x},{y},{z} gives up: its pond's land did not load within {PendingTries * PendingRetryMs / 1000} s");
                }
            }
            System.Exception failed = null;
            foreach (var job in jobs.ToArray())
            {
                try { TickJob(world, job, dt, p); }
                catch (System.Exception e)
                {
                    // this drain stops: its emptied cells, openings and origin may fill again (HY-0012: the openings stayed
                    // guarded for the session); the others run on this tick
                    jobs.Remove(job);
                    foreach (var (x, y, z) in job.drained) draining.Remove((job.dim, x, y, z));
                    foreach (var (x, y, z) in job.openings) draining.Remove((job.dim, x, y, z));
                    draining.Remove((job.dim, job.origin.x, job.origin.y, job.origin.z));
                    if (Fault != null) Fault(e); else failed ??= e;
                }
            }
            if (failed != null) throw failed;
        }

        static void TickJob(IWorldAccessor world, Drain job, float dt, BlockPos p)
        {
            var ba = world.BlockAccessor;
            {
                p.dimension = job.dim;
                var (ox, oy, oz) = job.origin;
                // paused while the opening's chunk is unloaded
                if (!Liquids.Loaded(ba, p.Set(ox, oy, oz))) return;
                // an opening closed again (a block placed in it) passes nothing
                job.openings.RemoveAll(o => !Liquids.Open(ba, p.Set(o.x, o.y, o.z)));
                if (job.openings.Count == 0)
                {
                    // the layer it was draining goes all the same (its holes would fill again from the sources left, an
                    // endless supply): volume is kept to the layer
                    if (job.next < job.drained.Count)
                    {
                        int y = job.drained[job.next].y, rest = 0;
                        for (int i = job.next; i < job.drained.Count && job.drained[i].y == y; i++) rest++;
                        Step(world, job, rest);
                    }
                    Finish(world, job, false);
                    return;
                }
                int top = job.next < job.drained.Count ? job.drained[job.next].y : job.sill;
                job.credit += DrainRate * Width(job.openings) * System.Math.Max(1, top - job.sill + 1) * dt;
                int k = (int)job.credit;
                job.credit -= k;
                if (k > 0) Step(world, job, k);
                // a line in the verbose debug log every 30 s of a drain
                if ((int)((job.age + dt) / 30) > (int)(job.age / 30))
                    LogKept?.Invoke($"Hydrology: the pond opened at {ox},{oy},{oz} has let out {job.next} of {job.drained.Count} sources through {job.openings.Count} cells ({job.placed} stood below)");
                job.age += dt;
                if (job.next >= job.drained.Count) Finish(world, job, true);
            }
        }

        /// <summary>Lets every drain run to its end now (the offline suite).</summary>
        public static void RunAll(IWorldAccessor world)
        {
            for (int guard = 0; jobs.Count > 0 && guard < 64; guard++)
                foreach (var job in jobs.ToArray())
                {
                    Step(world, job, int.MaxValue);
                    if (job.next >= job.drained.Count) Finish(world, job, true);
                }
        }

        /// <summary>Fable's pass (R4): the first line of the saved data, "hydrology-drains 1"; a save without it is the same
        /// format (rc.9 to rc.13). A later format bumps the number, so an older Hydrology refuses it on purpose.</summary>
        public const string SaveHeader = "hydrology-drains";
        public const int SaveVersion = 1;

        /// <summary>The drains to keep with the world: a version line, then one line each, "dimension liquid rounds
        /// x,y,z;x,y,z" (and the river pools' "pool dimension x,y,z").</summary>
        public static byte[] Save()
        {
            if (jobs.Count == 0 && pending.Count == 0 && H2DamPools.Running == 0 && !H2DamPools.HasPending) return System.Array.Empty<byte>();
            var sb = new StringBuilder();
            sb.Append(SaveHeader).Append(' ').Append(SaveVersion).Append('\n');
            H2DamPools.Save(sb);
            foreach (var j in jobs)
                sb.Append(j.dim).Append(' ').Append(j.liquid.LiquidCode).Append(' ').Append(j.rounds).Append(' ')
                  .Append(string.Join(";", j.openings.Select(o => $"{o.x},{o.y},{o.z}"))).Append('\n');
            foreach (var (dim, liquid, openings, rounds, _, _) in pending)
                sb.Append(dim).Append(' ').Append(liquid).Append(' ').Append(rounds).Append(' ')
                  .Append(string.Join(";", openings.Select(o => $"{o.x},{o.y},{o.z}"))).Append('\n');
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>Drains saved with the world, to start again when their chunks are loaded.</summary>
        public static void Load(byte[] data)
        {
            ResetWorld();
            if (data == null) return;
            var lines = Encoding.UTF8.GetString(data).Split('\n');
            if (lines.Length > 0 && lines[0].StartsWith(SaveHeader + " ", System.StringComparison.Ordinal))
            {
                if (!int.TryParse(lines[0].Substring(SaveHeader.Length + 1), out int version) || version > SaveVersion)
                {
                    Log?.Invoke($"Hydrology: the saved pond drains and river pools are in a format this Hydrology does not read ({lines[0]}); they do not restart and are dropped at the next save");
                    return;
                }
            }
            foreach (var line in lines)
            {
                if (line.StartsWith(SaveHeader, System.StringComparison.Ordinal)) continue;
                var parts = line.Split(' ');
                if (H2DamPools.Load(parts)) continue;
                if (parts.Length != 4 || !int.TryParse(parts[0], out int dim) || !int.TryParse(parts[2], out int rounds)) continue;
                var openings = new List<(int x, int y, int z)>();
                foreach (var o in parts[3].Split(';'))
                {
                    var c = o.Split(',');
                    if (c.Length == 3 && int.TryParse(c[0], out int x) && int.TryParse(c[1], out int y) && int.TryParse(c[2], out int z)) openings.Add((x, y, z));
                }
                if (openings.Count > 0) pending.Add((dim, parts[1], openings, rounds, 0, 0));
            }
        }
    }
}
