using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace RevertTest
{
    /// <summary>
    /// A minimal server-side test: do edits a player would do, with no player online, and check whether they stay. Loads a
    /// cluster of spots near spawn and then far spots (KeepLoaded), breaks blocks under water and digs a short canal from
    /// a shore (or a dry hole) at the cluster spots, then reads the edited blocks every 2 s and logs any that go back to
    /// their state before the edit. Every chunk event is logged too. Writes reverttest/results.json and shuts down.
    /// Built for the Congo revert investigation (Hydrology docs/congo-revert-2026-10-25.md); it changes no game rules.
    /// </summary>
    public class RevertTestSystem : ModSystem
    {
        public class Config
        {
            public bool Enabled = true;
            /// <summary>Cluster spots [dx, dz] from the world spawn: loaded first, edited.</summary>
            public List<int[]> Cluster = new() { new[] { 0, 0 }, new[] { 120, 40 }, new[] { -60, 130 } };
            /// <summary>Far spots [dx, dz] from the world spawn: loaded after the cluster, never edited.</summary>
            public List<int[]> Far = new()
            {
                new[] { 20000, 0 }, new[] { -20000, 5000 }, new[] { 6000, 25000 }, new[] { -9000, -24000 }, new[] { 40000, 40000 },
                new[] { -45000, 30000 }, new[] { 30000, -42000 }, new[] { -35000, -38000 }, new[] { 70000, -5000 }, new[] { -2000, 70000 },
            };
            public int SpotRadius = 3;
            /// <summary>More far spots on rings around spawn (15 000 blocks apart), on top of <see cref="Far"/>: a longer
            /// chunk-compression queue.</summary>
            public int ExtraFar = 0;
            /// <summary>Edit a spot only once the chunk at its water surface has been packed by the compression thread but not
            /// yet committed (WorldChunk.blocksCompressedTmp set): the window the edit is lost in.</summary>
            public bool EditWhenPendingPack = false;
            public int EditDelaySec = 20;
            public int RunSec = 240;
            public bool Shutdown = true;
        }

        ICoreServerAPI api;
        Config cfg;
        string outDir;
        readonly Stopwatch clock = new();
        readonly object gate = new();
        readonly List<object> chunkEvents = new();
        readonly List<object> reverts = new();
        readonly List<Edit> edits = new();
        readonly HashSet<(int, int)> editChunks = new();
        readonly Dictionary<(int, int, long), int> dirtyNear = new();

        class Edit
        {
            public string Kind;
            public int X, Y, Z;
            public int SolidBefore, SolidAfter;
            public bool Reverted;
            public double RevertedAt = -1;
        }

        IBlockAccessor ba => api.World.BlockAccessor;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            cfg = api.LoadModConfig<Config>("reverttest.json") ?? new Config();
            if (!cfg.Enabled) return;
            outDir = Path.Combine(GamePaths.DataPath, "reverttest");
            Directory.CreateDirectory(outDir);
            api.Event.ChunkColumnLoaded += (p, c) => Chunk("load", p.X, p.Y, null);
            api.Event.ChunkColumnUnloaded += p => Chunk("unload", p.X, p.Y, null);
            api.Event.BeginChunkColumnLoadChunkThread += (m, cx, cz, c) => Chunk("load-thread", cx, cz, null);
            api.Event.ChunkDirty += (coord, chunk, reason) =>
            {
                if (reason != EnumChunkDirtyReason.MarkedDirty) { Chunk("dirty-" + reason, coord.X, coord.Z, coord.Y); return; }
                if (!editChunks.Contains((coord.X, coord.Z))) return;
                lock (gate) { var k = (coord.X, coord.Z, (long)Math.Floor(clock.Elapsed.TotalSeconds)); dirtyNear[k] = dirtyNear.GetValueOrDefault(k) + 1; }
            };
            api.Event.GameWorldSave += () => Chunk("world-save", 0, 0, null);
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => api.Event.RegisterCallback(_ => Begin(), 5000));
        }

        void Log(string fmt, params object[] args) => Mod.Logger.Notification("REVERTTEST " + fmt, args);

        void Chunk(string what, int cx, int cz, int? cy)
        {
            lock (gate)
                if (chunkEvents.Count < 300000)
                    chunkEvents.Add(new { t = clock.IsRunning ? Math.Round(clock.Elapsed.TotalSeconds, 2) : -1, what, cx, cz, cy, edited = editChunks.Contains((cx, cz)) });
        }

        void Begin()
        {
            var spawn = api.World.DefaultSpawnPosition.AsBlockPos;
            var cluster = cfg.Cluster.Select(d => (spawn.X + d[0], spawn.Z + d[1])).ToList();
            var far = cfg.Far.Select(d => (spawn.X + d[0], spawn.Z + d[1])).ToList();
            for (int i = 0; i < cfg.ExtraFar; i++)
            {
                double a = i * 2.39996, r = 15000 * (1 + i / 8);
                far.Add((spawn.X + (int)(Math.Cos(a) * r), spawn.Z + (int)(Math.Sin(a) * r)));
            }
            Log("spawn {0},{1}; loading {2} cluster spots, then {3} far spots", spawn.X, spawn.Z, cluster.Count, far.Count);
            Load(cluster.Concat(far).ToList(), 0, () =>
            {
                clock.Start();
                api.Event.RegisterCallback(_ => DoEdits(cluster), cfg.EditDelaySec * 1000);
            });
        }

        void Load(List<(int x, int z)> spots, int i, Action done)
        {
            if (i >= spots.Count) { done(); return; }
            var (x, z) = spots[i];
            int r = cfg.SpotRadius, pending = (2 * r + 1) * (2 * r + 1);  // set before any request: loaded columns answer at once
            var t0 = DateTime.UtcNow;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    api.WorldManager.LoadChunkColumnPriority(x / 32 + dx, z / 32 + dz, new ChunkLoadOptions
                    {
                        KeepLoaded = true,
                        OnLoaded = () =>
                        {
                            if (--pending > 0) return;
                            Log("spot {0},{1} loaded in {2:0}s", x, z, (DateTime.UtcNow - t0).TotalSeconds);
                            api.Event.RegisterCallback(_ => Load(spots, i + 1, done), 50);
                        }
                    });
                }
        }

        int TopSolidY(int x, int z)
        {
            var p = new BlockPos(x, api.WorldManager.MapSizeY - 2, z);
            for (; p.Y > 1; p.Y--) if (ba.GetBlock(p, BlockLayersAccess.SolidBlocks).Id != 0) return p.Y;
            return -1;
        }

        bool WaterAbove(int x, int y, int z) => ba.GetBlock(new BlockPos(x, y + 1, z), BlockLayersAccess.Fluid).IsLiquid();

        /// <summary>The chunk at pos has a packed copy waiting to be committed (WorldChunk.blocksCompressedTmp, public).</summary>
        bool PackPending(BlockPos pos)
        {
            var chunk = ba.GetChunkAtBlockPos(pos);
            var f = chunk?.GetType().GetField("blocksCompressedTmp", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return f?.GetValue(chunk) != null;
        }

        void StartChecks()
        {
            Log("{0} blocks edited", edits.Count);
            api.Event.RegisterGameTickListener(_ => Check(), 2000);
            api.Event.RegisterCallback(_ => Finish(), cfg.RunSec * 1000);
        }

        void DoEdits(List<(int x, int z)> cluster)
        {
            if (!cfg.EditWhenPendingPack)
            {
                foreach (var (cx, cz) in cluster) EditSpot(cx, cz);
                StartChecks();
                return;
            }
            // edit each spot as soon as its water-surface chunk is packed but not committed (polled every 50 ms; the water
            // is found first, while the chunks are unpacked anyway)
            var todo = cluster.Select(c => (c, pos: FindWater(c.x, c.z) is (int wx, int wy, int wz) ? new BlockPos(wx, wy, wz)
                : new BlockPos(c.x, TopSolidY(c.x, c.z), c.z))).ToList();
            double t0 = clock.Elapsed.TotalSeconds;
            long listener = 0;
            listener = api.Event.RegisterGameTickListener(_ =>
            {
                foreach (var item in todo.ToList())
                {
                    bool pending = PackPending(item.pos);
                    if (!pending && clock.Elapsed.TotalSeconds - t0 < 120) continue;
                    Log("spot {0},{1}: pack pending={2} at {3:0.0}s, editing", item.c.x, item.c.z, pending, clock.Elapsed.TotalSeconds);
                    EditSpot(item.c.x, item.c.z);
                    todo.Remove(item);
                }
                if (todo.Count > 0) return;
                api.Event.UnregisterGameTickListener(listener);
                StartChecks();
            }, 50);
        }

        readonly Dictionary<(int, int), (int, int, int)?> waterAt = new();

        (int x, int y, int z)? FindWater(int cx, int cz)
        {
            if (waterAt.TryGetValue((cx, cz), out var w)) return w;
            (int x, int y, int z)? water = null;
            for (int rr = 0; rr <= 80 && water == null; rr++)
                for (int dx = -rr; dx <= rr && water == null; dx++)
                    foreach (int dz in new[] { -rr, rr })
                    {
                        int x = cx + dx, z = cz + dz, y = TopSolidY(x, z);
                        if (y > 0 && WaterAbove(x, y, z)) { water = (x, y, z); break; }
                    }
            waterAt[(cx, cz)] = water;
            return water;
        }

        void EditSpot(int cx, int cz)
        {
            {
                // the nearest column with water standing on its top solid block, within the loaded spot
                var water = FindWater(cx, cz);
                if (water is (int wx, int wy, int wz))
                {
                    // three bed blocks under the water (a player breaking the river or lake bed)
                    for (int k = 0; k < 3; k++) Break("bed", wx + k, TopSolidY(wx + k, wz), wz);
                    // a canal 2 wide, 2 deep below the water's surface, 6 long, from the nearest dry shore column outward
                    int surface = wy + 1;
                    while (WaterAbove(wx, surface, wz)) surface++;
                    foreach (var (sx, sz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int k0 = 1;
                        while (k0 < 24 && WaterAbove(wx + sx * k0, TopSolidY(wx + sx * k0, wz + sz * k0), wz + sz * k0)) k0++;
                        if (k0 >= 24) continue;
                        for (int k = k0; k < k0 + 6; k++)
                            for (int w = 0; w < 2; w++)
                            {
                                int x = wx + sx * k + sz * w, z = wz + sz * k + sx * w;
                                for (int y = TopSolidY(x, z); y >= surface - 2; y--) Break("canal", x, y, z);
                            }
                        break;
                    }
                    Log("edited at water {0},{1},{2}", wx, wy, wz);
                }
                else
                {
                    // no water near: a dry hole 2 x 2, 3 deep
                    for (int x = cx; x <= cx + 1; x++)
                        for (int z = cz; z <= cz + 1; z++)
                        {
                            int top = TopSolidY(x, z);
                            for (int y = top; y > top - 3; y--) Break("hole", x, y, z);
                        }
                    Log("no water near {0},{1}: dug a dry hole", cx, cz);
                }
            }
        }

        void Break(string kind, int x, int y, int z)
        {
            var pos = new BlockPos(x, y, z);
            int before = ba.GetBlock(pos, BlockLayersAccess.SolidBlocks).Id;
            if (before == 0) return;
            ba.BreakBlock(pos, null, 0);
            int after = ba.GetBlock(pos, BlockLayersAccess.SolidBlocks).Id;
            edits.Add(new Edit { Kind = kind, X = x, Y = y, Z = z, SolidBefore = before, SolidAfter = after });
            editChunks.Add((x / 32, z / 32));
        }

        void Check()
        {
            double t = clock.Elapsed.TotalSeconds;
            foreach (var e in edits)
            {
                if (e.Reverted) continue;
                int now = ba.GetBlock(new BlockPos(e.X, e.Y, e.Z), BlockLayersAccess.SolidBlocks).Id;
                if (now == e.SolidBefore && now != e.SolidAfter)
                {
                    e.Reverted = true; e.RevertedAt = t;
                    lock (gate) reverts.Add(new { t = Math.Round(t, 1), e.Kind, e.X, e.Y, e.Z, cx = e.X / 32, cz = e.Z / 32, block = api.World.GetBlock(now).Code?.ToString() });
                    Log("REVERTED at {0:0.0}s: {1} {2},{3},{4} is {5} again", t, e.Kind, e.X, e.Y, e.Z, api.World.GetBlock(now).Code);
                }
            }
        }

        void Finish()
        {
            Check();
            lock (gate)
                File.WriteAllText(Path.Combine(outDir, "results.json"), JsonSerializer.Serialize(new
                {
                    version = GameVersion.ShortGameVersion,
                    edits = edits.Count,
                    reverted = edits.Count(e => e.Reverted),
                    reverts,
                    editList = edits.Select(e => new { e.Kind, e.X, e.Y, e.Z, before = api.World.GetBlock(e.SolidBefore).Code?.ToString(), e.Reverted, e.RevertedAt }),
                    chunkEvents,
                    dirtyNearEdits = dirtyNear.Select(kv => new { cx = kv.Key.Item1, cz = kv.Key.Item2, t = kv.Key.Item3, n = kv.Value }),
                }, new JsonSerializerOptions { WriteIndented = false }));
            Log("done: {0} of {1} edited blocks reverted", edits.Count(e => e.Reverted), edits.Count);
            if (cfg.Shutdown) api.Server.ShutDown();
        }
    }
}
