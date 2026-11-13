using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace WaterAudit
{
    public class Scenario
    {
        /// <summary>
        /// Lab kinds, built from stone and water in the air above the terrain, so they run the same on any world:
        /// lab_trench, lab_step, lab_stepchannel, lab_drop, lab_bucket, lab_chute, lab_fall, lab_ice, lab_spill.
        /// Earth kinds, on Earth's real rivers and coasts (need Earth Map): river_trench, sea_trench, ramp, rapids_poke,
        /// chute_poke, fall_poke, wheel, step_break.
        /// </summary>
        public string Kind;
        public string Name;
        public int Width = 2;
        public int Length = 30;
        /// <summary>Trenches: "in" digs from the far end and opens to the water last (all at once); "out" digs away from the water, one row per second.</summary>
        public string Order = "in";
        /// <summary>lab_trench: water or saltwater.</summary>
        public string Liquid = "water";
        /// <summary>lab_trench: how deep the trench is dug (blocks below and at the water level).</summary>
        public int Depth = 1;
        /// <summary>lab_trench: how deep the pool is at the trench's mouth.</summary>
        public int PoolDepth = 1;
        /// <summary>lab_step: the upper pool is 2 deep and touches the lower pool below the step (Earth 0.16.x, no sill).</summary>
        public bool Touching;
        /// <summary>lab_step, lab_chute: a water plant (block code) in the solid layer of every step's edge cell, the water
        /// kept, as vanilla's world generation puts reeds and seaweed after Earth has placed its water.</summary>
        public string EdgePlant;
        /// <summary>Lab kinds: record the lab as built, wake every fluid cell, and apply the lab's operation 20 s later
        /// (shape.json), as WakeAllSec does for Earth kinds.</summary>
        public bool ShapeCheck;
        /// <summary>lab_chute: a water plant (block code) in every top cell of the lower pools, under where the ramps form.</summary>
        public string PoolPlant;
        /// <summary>step_break: the block a player breaks at a river step (for good): "sill" (under the upper pool's edge
        /// source), "ramp_bed" (the lower pool's bed under the ramp cell), "bank" (the lower pool's bank at its surface level,
        /// beside the ramp), "mid_bed" (the lower pool's bed 4 cells downstream of the step).</summary>
        public string Target = "sill";
        /// <summary>lab_drop: how far the channel's end drops into the dry channel below.</summary>
        public int Drop = 2;
        /// <summary>lab_spill: the liquid of the source poured on the shore (water or saltwater; default the sea's,
        /// <see cref="Liquid"/>). Its height above the sea is <see cref="Drop"/>, the shore rises one block every
        /// <see cref="Spacing"/> columns, and <see cref="Length"/> (at most 13) is how far the sea reaches from the shore.</summary>
        public string SpillLiquid;
        /// <summary>lab_spill: a gap through the rim wall at both ends (w = 0), so water at any level above the sea's runs off
        /// the slab into the open air beside it, as off open land; without it the lab is a closed basin.</summary>
        public bool Outlet;
        /// <summary>lab_drop: a vanilla 3 m water wheel with its rim in the fall.</summary>
        public bool Wheel;
        /// <summary>lab_drop: when to fill the drop back to 1 block; lab_chute: when to fill its first step (0 = never).</summary>
        public int RefillAtSec;
        /// <summary>lab_chute: blocks between its three 1-block steps (Earth's pool layout 2: 3).</summary>
        public int Spacing = 3;
        /// <summary>lab_fall, fall_poke: when to put stones into the curtain (they come out again 20 s later; 0 = never).</summary>
        public int StonesAtSec;
        /// <summary>Lab kinds: block offset [dx, dz] from the lab base (Earth: <see cref="AuditConfig.LabBase"/>; otherwise the world spawn).</summary>
        public int[] Offset = { 0, 0 };
        /// <summary>Lab kinds, with the fake river query mod (tests/server/fakeriverquery, FAKE_QUERY=1): register the lab as
        /// natural river water, "lab" (all of it) or "pool" (lab_trench: the pool only, not the trench).</summary>
        public string QueryRiver;
        /// <summary>With <see cref="QueryRiver"/>: the region is a rapids reach; its slope (block per block) and discharge (m³/s).</summary>
        public bool QueryRapids;
        public double QuerySlope = 0.02, QueryDischarge = 100;
        /// <summary>Earth kinds: candidate sites as [lat, lon]; the first one where the feature is found is used.</summary>
        public List<double[]> Sites = new();
        /// <summary>river_poke, watch, river_block: how far from a site (blocks) to look for the river (0: 40, river_block 80).
        /// The chunks that far out are loaded too. At 1:10 a site's coordinate can be a few hundred metres off the river.</summary>
        public int SearchRadius;
    }

    public class AuditConfig
    {
        public bool Enabled = true;
        public int StartDelayMs = 5000;
        public bool Shutdown = true;
        public int Radius = 3;
        public int LabRadius = 2;
        public int[] DumpAtSec = { 10, 30, 60, 90, 120, 180, 300 };
        /// <summary>[lat, lon] of the lab base on Earth worlds.</summary>
        public double[] LabBase = { 56.62, 25.05 };
        public List<Scenario> Scenarios = new();
        /// <summary>Set the world-config attribute "hydrologyFillVessels" at start, as Earth 0.16.8 does on its worlds.</summary>
        public bool SetVesselsAttr;
        /// <summary>Earth kinds: before the pokes, record the water around each site as generated, wake every fluid cell there
        /// once, and poke this many seconds later; shape.json lists the cells the first update and the poke changed (0 = off).</summary>
        public int WakeAllSec;
        /// <summary>Half-width of the area recorded around each site for <see cref="WakeAllSec"/>.</summary>
        public int ShapeRadius = 14;
        /// <summary>Probe mode: no scenarios; load these block positions [x, y, z] (and a column of 3 below and above), log
        /// their solid and fluid blocks to probe.json, and shut down. For reading a finished run's saved world.</summary>
        public List<int[]> Probe = new();
        /// <summary>Areas [x, z, half, yLow, yHigh]; every column's fluid blocks and highest solid block in that range go to
        /// area.json (for reading a river's water as generated or as a player left it). Without scenarios this is probe mode
        /// (load, write, stop); with scenarios they are written before the first operation (area-t0.json) and at the end
        /// (area-end.json).</summary>
        public List<int[]> ProbeArea = new();
        /// <summary>Probe mode with <see cref="ProbeArea"/>: after area.json, wake every fluid cell of each area once and write
        /// area-woken.json this many seconds later (whether water a player left settles when disturbed), 0 = off.</summary>
        public int WakeProbeAreaSec;
        /// <summary>With <see cref="WakeProbeAreaSec"/>: instead of waking the areas, place a block at [x, y, z] and break it
        /// 3 s later, as a player would.</summary>
        public int[] ProbePoke;
        /// <summary>With scenarios: at the end, every plan's square of this half-width (fluids per column, as ProbeArea) goes
        /// to area-plans.json, 0 = off.</summary>
        public int PlanAreaRadius;
        /// <summary>Squares [x, z, half]: Earth's river query (RiverAt, RiverFlowAt) and its model column (river fields) for
        /// every column, to query.json, written with the probe (for comparing what the query says with what was drawn).</summary>
        public List<int[]> QueryProbe = new();
        /// <summary>HY-0004, probe mode (Earth worlds): squares [lat, lon, half]. Every column's fresh water from its top down
        /// is checked for open air (no fluid, an air or replaceable solid) under its lowest cell, or beside a cell below the
        /// neighbouring column's ground (a cave opening); the cells, their river answers and the squares' water totals go to
        /// bedscan.json.</summary>
        public List<double[]> BedScan = new();
        /// <summary>With <see cref="BedScan"/>: wake every water cell found at an opening, and write bedscan-woken.json this
        /// many seconds later (whether the water drains into the cave), 0 = off.</summary>
        public int BedScanWakeSec;
        /// <summary>With <see cref="BedScan"/> (a positive control and the HY-0004 case itself): before the first scan, carve a
        /// 3 x 3 x 3 cave whose roof is the bed under the river column nearest each square's middle with water at least 2
        /// deep, as a cave opening under a river would be.</summary>
        public bool BedScanCaves;
    }

    /// <summary>
    /// Test-only helper for Hydrology's server tests (moved from Earth's realistic-water test, 2026-10-22). It builds small
    /// test basins (labs) or finds river and coast features in an Earth world, does what a player would (dig trenches,
    /// open drops, fill them back, poke river steps, place water wheels, spill a full bucket), and records the water as
    /// short profiles at fixed times, the wheels' flow every 4 s, the server tick time and Hydrology's own counters.
    /// </summary>
    public class WaterAuditSystem : ModSystem
    {
        ICoreServerAPI api;
        AuditConfig cfg;
        string outDir;
        object geometry;
        IBlockAccessor ba => api.World.BlockAccessor;
        int H;
        int unloads;
        int cobble, stone;
        readonly Stopwatch clock = new();
        readonly List<double> tickDts = new();
        readonly List<Dictionary<string, object>> plans = new();
        readonly List<(int x, int z)> used = new();
        readonly List<(string name, BlockPos pos)> wheels = new();
        readonly List<object> wheelLog = new();
        readonly List<object> lagLog = new();
        readonly List<object> statsLog = new();
        readonly Dictionary<string, Dictionary<string, object>> profiles = new();

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
        public override double ExecuteOrder() => 2;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            try { cfg = api.LoadModConfig<AuditConfig>("wateraudit.json"); } catch (Exception e) { Mod.Logger.Error("WATERAUDIT config: {0}", e); }
            if (cfg == null || !cfg.Enabled) return;
            outDir = Path.Combine(GamePaths.DataPath, "wateraudit");
            Directory.CreateDirectory(outDir);
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => api.Event.RegisterCallback(_ => Begin(), cfg.StartDelayMs));
            api.Event.BeginChunkColumnLoadChunkThread += (mapChunk, cx, cz, chunks) => Chunk("load-thread", cx, cz, null);
            api.Event.ChunkDirty += (coord, chunk, reason) =>
            {
                if (reason != EnumChunkDirtyReason.MarkedDirty) Chunk("dirty-" + reason, coord.X, coord.Z, coord.Y);
                else if (NearPlan(coord.X, coord.Z)) Chunk("dirty-marked", coord.X, coord.Z, coord.Y, aggregate: true);
            };
            api.Event.GameWorldSave += () => Chunk("world-save", 0, 0, null);
            api.Event.ChunkColumnUnloaded += pos =>
            {
                Chunk("unload", pos.X, pos.Y, null);
                if (NearPlan(pos.X, pos.Z)) Log("chunk column near a site unloaded {0},{1} at {2:0.0}s", pos.X, pos.Z, clock.Elapsed.TotalSeconds);
                else if (unloads++ < 50) Log("chunk column unloaded {0},{1} at {2:0}s", pos.X, pos.Z, clock.Elapsed.TotalSeconds);
            };
            api.Event.ChunkColumnLoaded += (pos, chunks) => { Chunk("load", pos.X, pos.Y, null); if (NearPlan(pos.X, pos.Y)) Log("chunk column near a site loaded {0},{1} at {2:0.0}s", pos.X, pos.Y, clock.Elapsed.TotalSeconds); };
            // the last generation pass a mod can register for (Done is a count, not a pass)
            try
            {
                api.Event.ChunkColumnGeneration(req =>
                {
                    Chunk("generate", req.ChunkX, req.ChunkZ, null);
                    if (NearPlan(req.ChunkX, req.ChunkZ)) Log("chunk column near a site generated {0},{1} at {2:0.0}s", req.ChunkX, req.ChunkZ, clock.Elapsed.TotalSeconds);
                    else if (gens++ < 5) Log("chunk column generated {0},{1} at {2:0.0}s", req.ChunkX, req.ChunkZ, clock.Elapsed.TotalSeconds);
                }, EnumWorldGenPass.PreDone, "standard");
            }
            catch (Exception e) { Log("generation log not registered: {0}", e.Message); }
        }

        int gens;

        // every chunk event (load, load thread, unload, generate, dirty reasons, world saves) with its time; marked-dirty
        // events near the sites counted per chunk and second. Events come from the chunk thread too.
        readonly List<object> chunkEvents = new();
        readonly Dictionary<(int, int, int, long), int> dirtyCounts = new();
        readonly object chunkGate = new();

        void Chunk(string what, int cx, int cz, int? cy, bool aggregate = false)
        {
            double t = clock.IsRunning ? clock.Elapsed.TotalSeconds : -1;
            lock (chunkGate)
            {
                if (aggregate) { var key = (cx, cy ?? 0, cz, (long)Math.Floor(t)); dirtyCounts[key] = dirtyCounts.GetValueOrDefault(key) + 1; return; }
                bool near;
                near = siteChunkSet.ContainsKey((cx, cz));
                if (chunkEvents.Count < 400000) chunkEvents.Add(new { t = Math.Round(t, 2), what, cx, cz, cy, near });
            }
        }

        void WriteChunkEvents()
        {
            lock (chunkGate)
            {
                var dirty = dirtyCounts.Select(kv => new { t = kv.Key.Item4, cx = kv.Key.Item1, cy = kv.Key.Item2, cz = kv.Key.Item3, n = kv.Value });
                File.WriteAllText(Path.Combine(outDir, "chunkevents.json"), JsonSerializer.Serialize(new { events = chunkEvents, dirtyNearSites = dirty }));
            }
        }

        void Log(string fmt, params object[] args) => Mod.Logger.Notification("WATERAUDIT " + fmt, args);

        /// <summary>A chunk column within 3 columns of a planned site (chunk loads, unloads and generation are logged there).</summary>
        bool NearPlan(int cx, int cz) => siteChunkSet.ContainsKey((cx, cz));

        // chunk columns within 3 of a planned site, filled on the main thread as sites are planned (read by chunk events
        // from the chunk thread too: a concurrent set, never a list being iterated)
        readonly System.Collections.Concurrent.ConcurrentDictionary<(int, int), byte> siteChunkSet = new();

        void AddSiteChunks(int x, int z)
        {
            for (int dx = -3; dx <= 3; dx++) for (int dz = -3; dz <= 3; dz++) siteChunkSet[(x / 32 + dx, z / 32 + dz)] = 0;
        }

        void Begin()
        {
            H = api.WorldManager.MapSizeY;
            cobble = api.World.GetBlock(new AssetLocation("game:cobblestone-granite")).Id;
            stone = api.World.GetBlock(new AssetLocation("game:rock-granite")).Id;
            var core = api.ModLoader.GetModSystem("EarthMap.EarthMapSystem");
            var model = core?.GetType().GetProperty("Model", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(core);
            geometry = model?.GetType().GetField("Geometry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(model);
            bool hy = api.ModLoader.IsModEnabled("hydrology"), rw = api.ModLoader.IsModEnabled("realisticwater"), rr = api.ModLoader.IsModEnabled("realisticrapids");
            var (spill, spillFull) = BucketSpill();
            File.WriteAllText(Path.Combine(outDir, "world.json"), JsonSerializer.Serialize(new
            {
                sea = api.World.SeaLevel, h = H, seed = api.World.Seed, earth = geometry != null, hydrology = hy, realisticwater = rw, realisticrapids = rr,
                noLiquidSourceTransport = api.World.Config.GetBool("noLiquidSourceTransport", false),
                noLiquidSourceTransportSet = api.World.Config.HasAttribute("noLiquidSourceTransport"),
                bucketSpills = spill, fullBucketSpills = spillFull
            }));
            if (cfg.SetVesselsAttr) api.World.Config.SetBool("hydrologyFillVessels", true);
            DumpCylinderParams();
            Log("start: earth={0} hydrology={1} noLiquidSourceTransport={2} full bucket spills {3}", geometry != null, hy, api.World.Config.GetBool("noLiquidSourceTransport", false), spillFull);

            if (cfg.BedScan.Count > 0)
            {
                if (geometry == null) { Log("bed scan: no Earth"); if (cfg.Shutdown) api.Server.ShutDown(); return; }
                var squares = cfg.BedScan.Select(q => { var (x, z) = ToBlock(q[0], q[1]); return (x, z, half: (int)q[2], lat: q[0], lon: q[1]); }).ToList();
                LoadSpots(squares.Select(q => (q.x, q.z, q.half / 32 + 1)).Distinct().ToList(), 0, () => api.Event.EnqueueMainThreadTask(() =>
                {
                    if (cfg.BedScanCaves) CarveCaves(squares);
                    var openings = ScanBeds(squares, "bedscan.json");
                    if (cfg.BedScanWakeSec <= 0 || openings.Count == 0) { if (cfg.Shutdown) api.Server.ShutDown(); return; }
                    foreach (var p in openings) { var f = ba.GetBlock(p, BlockLayersAccess.Fluid); if (f.Id != 0) f.OnNeighbourBlockChange(api.World, p, p); }
                    Log("bed scan: {0} cells at openings woken, writing bedscan-woken.json in {1} s", openings.Count, cfg.BedScanWakeSec);
                    api.Event.RegisterCallback(_ =>
                    {
                        ScanBeds(squares, "bedscan-woken.json");
                        SampleStats("woken"); File.WriteAllText(Path.Combine(outDir, "stats.json"), JsonSerializer.Serialize(statsLog));
                        if (cfg.Shutdown) api.Server.ShutDown();
                    }, cfg.BedScanWakeSec * 1000);
                }, "wateraudit"));
                return;
            }

            if (cfg.Probe.Count > 0 || cfg.ProbeArea.Count > 0 && cfg.Scenarios.Count == 0)
            {
                var probeSpots = cfg.Probe.Select(q => (q[0], q[2], 0)).Concat(cfg.ProbeArea.Select(a => (a[0], a[1], a[2] / 32 + 1))).Distinct().ToList();
                LoadSpots(probeSpots, 0, () => api.Event.EnqueueMainThreadTask(() =>
                {
                    var rows = new List<object>();
                    foreach (var q in cfg.Probe)
                        for (int dy = 3; dy >= -3; dy--)
                        {
                            var pos = new BlockPos(q[0], q[1] + dy, q[2]);
                            rows.Add(new { x = q[0], y = q[1] + dy, z = q[2], solid = ba.GetBlock(pos, BlockLayersAccess.SolidBlocks).Code?.ToString(), fluid = ba.GetBlock(pos, BlockLayersAccess.Fluid).Code?.ToString() });
                        }
                    File.WriteAllText(Path.Combine(outDir, "probe.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
                    Log("probe: {0} positions written", cfg.Probe.Count);
                    WriteAreas("area.json");
                    WriteQueryProbe();
                    if (cfg.WakeProbeAreaSec > 0)
                    {
                        if (cfg.ProbePoke is { Length: 3 } q)
                        {
                            Place(q[0], q[1], q[2], cobble);
                            api.Event.RegisterCallback(_ => Break(q[0], q[1], q[2]), 3000);
                        }
                        else foreach (var a in cfg.ProbeArea) WakeArea((a[0] - a[2], a[0] + a[2], a[3], a[4], a[1] - a[2], a[1] + a[2]));
                        Log("probe: areas woken, writing area-woken.json in {0} s", cfg.WakeProbeAreaSec);
                        api.Event.RegisterCallback(_ =>
                        {
                            WriteAreas("area-woken.json");
                            SampleStats("woken"); File.WriteAllText(Path.Combine(outDir, "stats.json"), JsonSerializer.Serialize(statsLog));
                            if (cfg.Shutdown) api.Server.ShutDown();
                        }, cfg.WakeProbeAreaSec * 1000);
                        return;
                    }
                    if (cfg.Shutdown) api.Server.ShutDown();
                }, "wateraudit"));
                return;
            }
            var spots = new List<(int x, int z, int r)>();
            foreach (var a in cfg.ProbeArea) spots.Add((a[0], a[1], a[2] / 32 + 1));
            var labBase = LabBase();
            foreach (var s in cfg.Scenarios)
            {
                if (s.Kind.StartsWith("lab_")) spots.Add((labBase.x + s.Offset[0], labBase.z + s.Offset[1], cfg.LabRadius));
                else if (geometry != null) foreach (var site in s.Sites) { var (x, z) = ToBlock(site[0], site[1]); spots.Add((x, z, Math.Max(cfg.Radius, s.SearchRadius > 0 ? (s.SearchRadius + 14) / 32 + 1 : 0))); }
            }
            LoadSpots(spots.Distinct().ToList(), 0, () => api.Event.EnqueueMainThreadTask(Locate, "wateraudit"));
        }

        (int x, int z) LabBase()
        {
            if (geometry != null) return ToBlock(cfg.LabBase[0], cfg.LabBase[1]);
            var sp = api.World.DefaultSpawnPosition;
            return ((int)sp.X + 64, (int)sp.Z + 64);
        }

        (int x, int z) ToBlock(double lat, double lon)
        {
            var args = new object[] { lat, lon, 0.0, 0.0 };
            geometry.GetType().GetMethod("FromGeo").Invoke(geometry, args);
            return ((int)(double)args[2], (int)(double)args[3]);
        }

        void LoadSpots(List<(int x, int z, int r)> spots, int i, Action done)
        {
            if (i >= spots.Count) { done(); return; }
            var (x, z, r) = spots[i];
            int ccx = x / 32, ccz = z / 32, total = (2 * r + 1) * (2 * r + 1), loaded = 0;
            var t0 = DateTime.UtcNow;
            for (int cx = ccx - r; cx <= ccx + r; cx++)
                for (int cz = ccz - r; cz <= ccz + r; cz++)
                    api.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions
                    {
                        KeepLoaded = true,
                        OnLoaded = () =>
                        {
                            if (++loaded == total)
                            {
                                Log("spot {0},{1} loaded in {2:0}s", x, z, (DateTime.UtcNow - t0).TotalSeconds);
                                api.Event.RegisterCallback(_ => LoadSpots(spots, i + 1, done), 50);
                            }
                        }
                    });
        }

        // ---------- block helpers ----------

        Block Fluid(int x, int y, int z) => ba.GetBlock(new BlockPos(x, y, z), BlockLayersAccess.Fluid);
        Block Solid(int x, int y, int z) => ba.GetBlock(new BlockPos(x, y, z), BlockLayersAccess.SolidBlocks);
        int Terrain(int x, int z) => ba.GetTerrainMapheightAt(new BlockPos(x, 0, z));
        bool Open(int x, int y, int z) { var b = Solid(x, y, z); return b.Id == 0 || b.Replaceable >= 6000; }
        bool SolidTop(int x, int y, int z) => Solid(x, y, z).SideSolid[BlockFacing.UP.Index];

        int TopFluid(int x, int z, out Block b)
        {
            int t = Terrain(x, z), y = t + 1;
            b = null;
            while (y < H - 1 && Fluid(x, y, z).IsLiquid()) y++;
            if (y - 1 <= t) return -1;
            b = Fluid(x, y - 1, z);
            return y - 1;
        }

        bool StillAt(int x, int z, int wt, string liquid)
        {
            int y = TopFluid(x, z, out var b);
            return y == wt && b.LiquidLevel == 7 && b.LiquidCode == liquid && Open(x, wt + 1, z);
        }

        bool Land(int x, int z, int wt) => Terrain(x, z) >= wt && TopFluid(x, z, out _) < 0;

        IEnumerable<(int x, int z)> Around(int cx, int cz, int r)
        {
            var list = new List<(int, int, int)>();
            for (int dx = -r; dx <= r; dx++) for (int dz = -r; dz <= r; dz++) list.Add((dx * dx + dz * dz, cx + dx, cz + dz));
            return list.OrderBy(t => t.Item1).Select(t => (t.Item2, t.Item3));
        }

        bool Free(int x, int z) => used.All(u => Math.Abs(u.x - x) + Math.Abs(u.z - z) >= 48);

        static readonly (int dx, int dz)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        /// <summary>Short code of a fluid cell: '.' none; S source, R rapid source, s salt source; w/r/t flowing water/rapid/salt
        /// with level; 'd' for falling (wd6, rd6, td6).</summary>
        static string Code(Block f)
        {
            if (f == null || f.Id == 0 || !f.IsLiquid()) return ".";
            string p = f.Code.Path, first = f.FirstCodePart();
            char k = first == "rapidwater" ? 'r' : first == "saltwater" ? 't' : first == "water" ? 'w' : '?';
            if (f.LiquidLevel == 7) return k == 'r' ? "R" : k == 't' ? "s" : k == 'w' ? "S" : "?7";
            return k + (f.Variant["flow"] == "d" ? "d" : "") + f.LiquidLevel;
        }

        Dictionary<string, int> Census(int x0, int x1, int y0, int y1, int z0, int z1)
        {
            var c = new Dictionary<string, int>();
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    for (int y = y0; y <= y1; y++)
                    {
                        var f = Fluid(x, y, z);
                        if (f.Id == 0 || !f.IsLiquid()) continue;
                        string first = f.FirstCodePart();
                        string fam = (first == "rapidwater" ? "rapid" : first) + (f.LiquidLevel == 7 ? "-source" : f.Variant["flow"] == "d" ? "-falling" : "-flowing");
                        c[fam] = c.GetValueOrDefault(fam) + 1;
                    }
            return c;
        }

        (string normal, string full) BucketSpill()
        {
            var item = api.World.GetItem(new AssetLocation("game:waterportion"));
            var spilled = item?.Attributes?["waterTightContainerProps"]?["whenSpilled"];
            if (spilled == null || !spilled.Exists) return (null, null);
            string normal = spilled["stack"]["code"].AsString();
            var byFill = spilled["stackByFillLevel"]["10"];
            return (normal, byFill.Exists ? byFill["code"].AsString() : normal);
        }

        /// <summary>The lab's area (or its pool) as river water in the fake river query (tests only).</summary>
        void RegisterRiver(Scenario s, Dictionary<string, object> plan)
        {
            var fake = api.ModLoader.GetModSystem("EarthMap.EarthMapSystem");
            var add = fake?.GetType().GetMethod("AddRegion");
            if (add == null) { Log("{0}: no fake river query to register with", s.Name); return; }
            int x = I(plan, "x"), z = I(plan, "z");
            int k0 = I(plan, "k0"), k1 = I(plan, "k1"), w0 = I(plan, "w0"), w1 = I(plan, "w1");
            if (s.QueryRiver == "pool") { k0 = -8; k1 = 0; w0 = -4; w1 = s.Width + 3; }
            add.Invoke(fake, new object[] { x + k0, z + w0, x + k1, z + w1, s.QueryRapids, s.QuerySlope, s.QueryDischarge });
            plan["queryRiver"] = s.QueryRiver; plan["queryRapids"] = s.QueryRapids;
            Log("{0}: river query region {1},{2} .. {3},{4} rapids={5}", s.Name, x + k0, z + w0, x + k1, z + w1, s.QueryRapids);
        }

        /// <summary>Cylinder World's SeamParams (public fields and properties) to cylinder.json, for the fake one
        /// (tests/server/fakecylinder) to serve the same geometry.</summary>
        void DumpCylinderParams()
        {
            var core = api.ModLoader.Systems.FirstOrDefault(sys => sys.GetType().FullName == "CylinderWorld.CylinderWorldCore");
            var prm = core?.GetType().GetProperty("Params", BindingFlags.Public | BindingFlags.Instance)?.GetValue(core);
            if (prm == null) return;
            var d = new Dictionary<string, object>();
            foreach (var f in prm.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.FieldType.IsPrimitive) d[f.Name] = f.GetValue(prm);
            foreach (var pr in prm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (pr.PropertyType.IsPrimitive && pr.GetIndexParameters().Length == 0) { try { d[pr.Name] = pr.GetValue(prm); } catch { } }
            d["_coreType"] = core.GetType().FullName;
            File.WriteAllText(Path.Combine(outDir, "cylinder.json"), JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary><see cref="AuditConfig.QueryProbe"/>: per column, Earth's RiverAt and RiverFlowAt answers and the model
        /// column's river fields, read by reflection.</summary>
        void WriteQueryProbe()
        {
            if (cfg.QueryProbe.Count == 0) return;
            var core = api.ModLoader.GetModSystem("EarthMap.EarthMapSystem");
            if (core == null) { Log("query probe: no Earth"); return; }
            var riverAt = core.GetType().GetMethod("RiverAt");
            var flowAt = core.GetType().GetMethod("RiverFlowAt");
            var model = core.GetType().GetProperty("Model", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(core);
            var getColumn = model?.GetType().GetMethod("GetColumn", new[] { typeof(int), typeof(int) });
            string[] fields = { "River", "RapidsReach", "Rapid", "WaterTop", "Top", "Ramp", "Fall", "Edge", "Spill", "Water", "FlowX", "FlowZ" };
            var rows = new List<object>();
            foreach (var q in cfg.QueryProbe)
                for (int z = q[1] - q[2]; z <= q[1] + q[2]; z++)
                    for (int x = q[0] - q[2]; x <= q[0] + q[2]; x++)
                    {
                        var args = new object[] { x, z, false, 0.0, 0.0 };
                        bool river = (bool)riverAt.Invoke(core, args);
                        var col = getColumn?.Invoke(model, new object[] { x, z });
                        var d = new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["river"] = river, ["rapids"] = args[2], ["flow"] = flowAt?.Invoke(core, new object[] { x, z }) };
                        if (col != null)
                            foreach (var f in fields)
                            {
                                var fi = col.GetType().GetField(f);
                                if (fi != null) d["c" + f] = fi.GetValue(col);
                            }
                        rows.Add(d);
                    }
            File.WriteAllText(Path.Combine(outDir, "query.json"), JsonSerializer.Serialize(rows));
            Log("query probe: {0} columns written", rows.Count);
        }

        readonly List<((int, int) square, BlockPos pos)> carved = new();

        /// <summary><see cref="AuditConfig.BedScanCaves"/>: a 3 x 3 x 3 cave under the river column nearest each square's middle
        /// with water at least 2 deep; its roof is the bed under that water, so 9 river columns stand over open air.</summary>
        void CarveCaves(List<(int x, int z, int half, double lat, double lon)> squares)
        {
            foreach (var q in squares)
            {
                foreach (var (x, z) in Around(q.x, q.z, Math.Min(q.half, 48)))
                {
                    int top = TopFluid(x, z, out var b);
                    if (top < 0 || b.LiquidCode != "water" || Fluid(x, top - 1, z).LiquidCode != "water") continue;
                    int bed = top;
                    while (Fluid(x, bed, z).LiquidCode == "water") bed--;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dy = 0; dy < 3; dy++)
                            {
                                var pos = new BlockPos(x + dx, bed - dy, z + dz);
                                if (ba.GetBlock(pos, BlockLayersAccess.Fluid).Id != 0) continue; // the river's own water stays
                                ba.SetBlock(0, pos);
                            }
                    carved.Add(((q.x, q.z), new BlockPos(x, bed, z)));
                    Log("bed scan: cave carved under the river at {0},{1},{2} (water {3} deep)", x, bed, z, top - bed);
                    break;
                }
            }
        }

        /// <summary>
        /// <see cref="AuditConfig.BedScan"/>: every column of each square, its fresh water from the top down; an opening is
        /// open air (no fluid, an air or replaceable solid) under the lowest water cell ("below"), or beside a water cell and
        /// under the neighbouring column's ground ("side": a cave wall, not a bank or a drop at the surface). Returns the water
        /// cells at openings.
        /// </summary>
        List<BlockPos> ScanBeds(List<(int x, int z, int half, double lat, double lon)> squares, string file)
        {
            var core = api.ModLoader.GetModSystem("EarthMap.EarthMapSystem");
            var riverAt = core?.GetType().GetMethod("RiverAt");
            bool River(int x, int z)
            {
                try { return riverAt != null && (bool)riverAt.Invoke(core, new object[] { x, z, false, 0.0, 0.0 }); }
                catch { return false; }
            }
            var cells = new List<BlockPos>();
            var result = new List<object>();
            foreach (var q in squares)
            {
                int columns = 0, riverColumns = 0, waterCells = 0, sources = 0;
                var openings = new List<object>();
                // per river column: solid cells between its water and the first open air below (0: an opening; 32: none)
                var gaps = new int[33];
                for (int z = q.z - q.half; z <= q.z + q.half; z++)
                    for (int x = q.x - q.half; x <= q.x + q.half; x++)
                    {
                        // the highest fluid between the rain height and the terrain height (TopFluid starts at the terrain
                        // height, which over a hole in the bed is the cave's floor, under the water)
                        var colPos = new BlockPos(x, 0, z);
                        int top = -1, ground = Terrain(x, z);
                        for (int yy = Math.Min(H - 2, Math.Max(ba.GetRainMapHeightAt(colPos), ground) + 1); yy > ground; yy--)
                            if (Fluid(x, yy, z).Id != 0) { top = yy; break; }
                        if (top < 0 || Fluid(x, top, z).LiquidCode != "water") continue;
                        columns++;
                        bool river = River(x, z);
                        if (river) riverColumns++;
                        int y = top;
                        for (; y > 0; y--)
                        {
                            var f = Fluid(x, y, z);
                            if (f.LiquidCode != "water") break;
                            waterCells++;
                            if (f.LiquidLevel == 7) sources++;
                            foreach (var (dx, dz) in Dirs)
                            {
                                int nx = x + dx, nz = z + dz;
                                if (Fluid(nx, y, nz).Id != 0 || !Open(nx, y, nz) || y >= Terrain(nx, nz)) continue;
                                openings.Add(new { x, y, z, side = $"{dx},{dz}", depth = top - y, river, block = f.Code.ToShortString() });
                                cells.Add(new BlockPos(x, y, z));
                            }
                        }
                        // y: the first cell under the column's water
                        if (river)
                        {
                            int gap = 0;
                            while (gap < 32 && y - gap > 0 && !(Fluid(x, y - gap, z).Id == 0 && Open(x, y - gap, z))) gap++;
                            gaps[gap]++;
                        }
                        if (y > 0 && Fluid(x, y, z).Id == 0 && Open(x, y, z))
                        {
                            int air = 0;
                            while (y - air > 0 && Fluid(x, y - air, z).Id == 0 && Open(x, y - air, z) && air < 64) air++;
                            openings.Add(new { x, y = y + 1, z, side = "below", depth = top - y - 1, air, river, block = Fluid(x, y + 1, z).Code.ToShortString() });
                            cells.Add(new BlockPos(x, y + 1, z));
                        }
                    }
                result.Add(new { q.lat, q.lon, q.x, q.z, q.half, columns, riverColumns, waterCells, sources, openingCount = openings.Count, gapsUnderRiver = gaps, carved = carved.Where(c => c.square == (q.x, q.z)).Select(c => new[] { c.pos.X, c.pos.Y, c.pos.Z }), openings });
                Log("bed scan {0:0.###},{1:0.###}: {2} water columns ({3} river), {4} cells ({5} sources), {6} openings",
                    q.lat, q.lon, columns, riverColumns, waterCells, sources, openings.Count);
            }
            File.WriteAllText(Path.Combine(outDir, file), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return cells;
        }

        // ---------- locating ----------

        /// <summary>The <see cref="AuditConfig.ProbeArea"/> squares as they are now, to <paramref name="file"/>.</summary>
        void WriteAreas(string file)
        {
            var areas = new List<object>();
            foreach (var a in cfg.ProbeArea)
            {
                var cols = new List<object>();
                for (int z = a[1] - a[2]; z <= a[1] + a[2]; z++)
                    for (int x = a[0] - a[2]; x <= a[0] + a[2]; x++)
                    {
                        int solidTop = int.MinValue;
                        var fluids = new List<object>();
                        for (int y = a[4]; y >= a[3]; y--)
                        {
                            var pos = new BlockPos(x, y, z);
                            var f = ba.GetBlock(pos, BlockLayersAccess.Fluid);
                            if (f.Id != 0) fluids.Add(new object[] { y, f.Code.ToShortString() });
                            if (solidTop == int.MinValue && SolidTop(x, y, z)) solidTop = y;
                        }
                        cols.Add(new object[] { x, z, solidTop, fluids });
                    }
                areas.Add(new { x = a[0], z = a[1], half = a[2], yLow = a[3], yHigh = a[4], cols });
            }
            if (areas.Count > 0) File.WriteAllText(Path.Combine(outDir, file), JsonSerializer.Serialize(areas));
        }

        void Locate()
        {
            WriteAreas("area-t0.json");
            var labBase = LabBase();
            foreach (var s in cfg.Scenarios)
            {
                Dictionary<string, object> plan = null;
                if (s.Kind.StartsWith("lab_"))
                {
                    try { plan = BuildLab(s, labBase.x + s.Offset[0], labBase.z + s.Offset[1]); }
                    catch (Exception e) { Mod.Logger.Error("WATERAUDIT {0}: {1}", s.Name, e); }
                }
                else if (geometry == null) { Log("{0}: skipped (no Earth)", s.Name); continue; }
                else foreach (var site in s.Sites)
                    {
                        var (cx, cz) = ToBlock(site[0], site[1]);
                        try
                        {
                            plan = s.Kind switch
                            {
                                "river_trench" => FindTrench(cx, cz, "water", s.Length, s.Width),
                                "sea_trench" => FindTrench(cx, cz, "saltwater", s.Length, s.Width),
                                "ramp" => FindRamp(cx, cz),
                                "rapids_poke" => FindCurtain(cx, cz),
                                "chute_poke" => FindChuteStep(cx, cz),
                                "fall_poke" => FindFall(cx, cz),
                                "wheel" => FindRapid(cx, cz),
                                "river_poke" => FindRiverWater(cx, cz, s.SearchRadius > 0 ? s.SearchRadius : 40),
                                // river_block (Earth 0046): a line of 5 columns of muddy gravel from the bed to the surface, mid-river, left
                                // there (a player's report: "if you place any blocks in the water, 2 blocks around it turn into still water")
                                "river_block" => FindRiverMiddle(cx, cz, s.SearchRadius > 0 ? s.SearchRadius : 80),
                                // watch: the river water nearest a site, left alone (area dumps before and after show what changes on its own)
                                "watch" => FindRiverWater(cx, cz, s.SearchRadius > 0 ? s.SearchRadius : 40),
                                "step_break" => FindStepEdge(cx, cz, s.Target),
                                _ => null
                            };
                        }
                        catch (Exception e) { Mod.Logger.Error("WATERAUDIT {0} at {1},{2}: {3}", s.Name, site[0], site[1], e); }
                        if (plan != null) { plan["lat"] = site[0]; plan["lon"] = site[1]; break; }
                        Log("{0}: nothing at {1},{2}", s.Name, site[0], site[1]);
                    }
                if (plan == null) { Log("{0}: NOT FOUND", s.Name); continue; }
                plan["name"] = s.Name; plan["kind"] = s.Kind; plan["width"] = s.Width; plan["length"] = s.Length; plan["order"] = s.Order;
                plan["shapeCheck"] = s.ShapeCheck;
                if (!plan.ContainsKey("depth")) plan["depth"] = s.Depth;
                if (s.QueryRiver != null) RegisterRiver(s, plan);
                plan["liquid"] = plan.GetValueOrDefault("liquid") ?? s.Liquid; plan["drop"] = s.Drop; plan["wheel"] = s.Wheel; plan["refillAtSec"] = s.RefillAtSec; plan["spacing"] = s.Spacing; plan["stonesAtSec"] = s.StonesAtSec;
                used.Add(((int)plan["x"], (int)plan["z"]));
                AddSiteChunks((int)plan["x"], (int)plan["z"]);
                plans.Add(plan);
                Log("{0}: {1}", s.Name, JsonSerializer.Serialize(plan));
            }
            File.WriteAllText(Path.Combine(outDir, "plan.json"), JsonSerializer.Serialize(plans, new JsonSerializerOptions { WriteIndented = true }));
            if (plans.Count == 0) { Finish(); return; }
            foreach (var p in plans) Profile(p, "t0");
            api.Event.RegisterGameTickListener(dt => tickDts.Add(dt), 20);
            clock.Start();
            foreach (var p in plans)
            {
                bool shapeLab = ((string)p["kind"]).StartsWith("lab_") && p.ContainsKey("shapeCheck") && (bool)p["shapeCheck"];
                if ((cfg.WakeAllSec > 0 && !((string)p["kind"]).StartsWith("lab_")) || shapeLab)
                {
                    // the shape check: as generated, every cell woken once, then the poke
                    var area = ShapeArea(p);
                    shapes[(string)p["name"]] = new List<Dictionary<(int, int, int), (int, int)>> { Capture(area) };
                    WakeArea(area);
                    var pp = p;
                    api.Event.RegisterCallback(_ =>
                    {
                        shapes[(string)pp["name"]].Add(Capture(area));
                        try { Apply(pp); } catch (Exception e) { Mod.Logger.Error("WATERAUDIT apply {0}: {1}", pp["name"], e); }
                    }, Math.Max(cfg.WakeAllSec, 20) * 1000);
                    shapeAreas[(string)p["name"]] = area;
                    continue;
                }
                try { Apply(p); } catch (Exception e) { Mod.Logger.Error("WATERAUDIT apply {0}: {1}", p["name"], e); }
            }
            Log("ops applied in {0} ms", clock.ElapsedMilliseconds);
            SampleLag("ops");
            SampleStats("ops");
            foreach (int sec in cfg.DumpAtSec)
            {
                int t = sec;
                api.Event.RegisterCallback(_ =>
                {
                    SampleLag("t" + t);
                    SampleStats("t" + t);
                    foreach (var p in plans) Profile(p, "t" + t);
                    if (t == cfg.DumpAtSec.Max()) Finish();
                }, t * 1000);
            }
            for (int t = 2; t <= cfg.DumpAtSec.Max(); t += 4) { int tt = t; api.Event.RegisterCallback(_ => SampleWheels(tt), tt * 1000); }
        }

        Dictionary<string, object> FindTrench(int cx, int cz, string liquid, int len, int width)
        {
            foreach (var (x, z) in Around(cx, cz, 48))
            {
                if (!Free(x, z)) continue;
                int wt = TopFluid(x, z, out var b);
                if (wt < 0 || b.LiquidLevel != 7 || b.LiquidCode != liquid || !Open(x, wt + 1, z)) continue;
                foreach (var (dx, dz) in Dirs)
                {
                    int px = -dz, pz = dx;
                    if (Math.Abs(x + dx * len - cx) > 90 || Math.Abs(z + dz * len - cz) > 90) continue;
                    bool ok = true;
                    for (int w = 0; w < width && ok; w++)
                        for (int k = -3; k <= 0 && ok; k++)
                            ok = StillAt(x + dx * k + px * w, z + dz * k + pz * w, wt, liquid);
                    for (int k = 1; k <= len && ok; k++)
                        for (int w = -1; w <= width && ok; w++)
                            ok = Land(x + dx * k + px * w, z + dz * k + pz * w, wt);
                    if (ok) return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["wt"] = wt, ["dx"] = dx, ["dz"] = dz, ["px"] = px, ["pz"] = pz, ["liquid"] = liquid };
                }
            }
            return null;
        }

        Dictionary<string, object> FindRamp(int cx, int cz)
        {
            foreach (var (x, z) in Around(cx, cz, 80))
            {
                if (!Free(x, z)) continue;
                int y = TopFluid(x, z, out var b);
                if (y < 0 || b.LiquidCode != "water" || b.LiquidLevel >= 7 || !b.Code.Path.StartsWith("water-")) continue;
                string flow = b.Variant["flow"];
                if (flow == "still" || flow == "d") continue;
                if (Fluid(x, y - 1, z).LiquidLevel != 7) continue;
                return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["y"] = y, ["wt"] = y - 1, ["block"] = b.Code.ToString() };
            }
            return null;
        }

        /// <summary>The fresh river water nearest a site (any kind: a pool, a ramp, rapid or not), for a poke and the shape
        /// check around it (river_poke).</summary>
        Dictionary<string, object> FindRiverWater(int cx, int cz, int radius)
        {
            foreach (var (x, z) in Around(cx, cz, radius))
            {
                if (!Free(x, z)) continue;
                int y = TopFluid(x, z, out var b);
                if (y < 0 || b.LiquidCode != "water") continue;
                return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["y"] = y, ["wt"] = y, ["block"] = b.Code.ToString() };
            }
            return null;
        }

        /// <summary>River water at least 4 columns from any bank across a 5-column line along x (river_block).</summary>
        Dictionary<string, object> FindRiverMiddle(int cx, int cz, int radius)
        {
            foreach (var (x, z) in Around(cx, cz, radius))
            {
                if (!Free(x, z)) continue;
                int y = TopFluid(x, z, out var b);
                if (y < 0 || b.LiquidCode != "water") continue;
                bool open = true;
                for (int dx = -6; dx <= 6 && open; dx++)
                    for (int dz = -4; dz <= 4 && open; dz++)
                        open = TopFluid(x + dx, z + dz, out var n) == y && n.LiquidCode == "water";
                if (open) return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["y"] = y, ["wt"] = y, ["block"] = b.Code.ToString() };
            }
            return null;
        }

        /// <summary>Earth 0.16.3 draws drops of 2+ as falling water beside the upper pool: a source with a falling column of 2+ beside it.</summary>
        Dictionary<string, object> FindCurtain(int cx, int cz)
        {
            foreach (var (x, z) in Around(cx, cz, 80))
            {
                if (!Free(x, z)) continue;
                int y = TopFluid(x, z, out var b);
                if (y < 0 || b.LiquidCode != "water" || b.LiquidLevel != 7) continue;
                foreach (var (dx, dz) in Dirs)
                {
                    int nx = x + dx, nz = z + dz, n = 0;
                    if (!Open(nx, y, nz)) continue;
                    var top = Fluid(nx, y, nz);
                    if (top.IsLiquid() && top.LiquidLevel == 7) continue;
                    for (int yy = y - 1; yy > y - 40 && Fluid(nx, yy, nz).IsLiquid() && Fluid(nx, yy, nz).Variant["flow"] == "d"; yy--) n++;
                    if (n < 2) continue;
                    return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["y"] = y, ["wt"] = y, ["nx"] = nx, ["nz"] = nz, ["fall"] = n, ["block"] = b.Code.ToString(), ["top"] = top.Code?.ToString() };
                }
            }
            return null;
        }

        /// <summary>
        /// Earth 0.16.4 draws a 2–4 block fall as a chute of 1-block steps with rapid ramps: a rapid flowing cell on top of a
        /// lower pool's source, with a fresh source of the upper pool beside it at the same height (the step's edge).
        /// </summary>
        Dictionary<string, object> FindChuteStep(int cx, int cz)
        {
            foreach (var (x, z) in Around(cx, cz, 80))
            {
                if (!Free(x, z)) continue;
                int y = TopFluid(x, z, out var b);
                if (y < 0 || !b.Code.Path.StartsWith("rapidwater-") || b.LiquidLevel >= 7 || b.Variant["flow"] == "d") continue;
                if (Fluid(x, y - 1, z).LiquidLevel != 7) continue;
                foreach (var (dx, dz) in Dirs)
                {
                    var e = Fluid(x + dx, y, z + dz);
                    if (e.LiquidCode != "water" || e.LiquidLevel != 7) continue;
                    return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["y"] = y, ["wt"] = y, ["ex"] = x + dx, ["ez"] = z + dz, ["block"] = b.Code.ToString(), ["edge"] = e.Code.ToString() };
                }
            }
            return null;
        }

        /// <summary>
        /// A 1-block river step (Earth 0.16.8+, with a sill): a flowing ramp cell (ordinary or rapid) on a source of the lower
        /// pool, beside a source of the upper pool at the same height that stands on a block (the sill). Returns the block to
        /// break for <paramref name="target"/> as tx, ty, tz. Searches 200 blocks around the site.
        /// </summary>
        Dictionary<string, object> FindStepEdge(int cx, int cz, string target)
        {
            bool Fresh(Block f) => f.IsLiquid() && (f.FirstCodePart() == "water" || f.FirstCodePart() == "rapidwater");
            bool SolidAt(int x, int y, int z) => ba.GetBlock(new BlockPos(x, y, z), BlockLayersAccess.SolidBlocks).Id != 0;
            foreach (var (x, z) in Around(cx, cz, 200))
            {
                if (!Free(x, z)) continue;
                int y = TopFluid(x, z, out var b);
                if (y < 0 || !Fresh(b) || b.LiquidLevel >= 7 || b.Variant["flow"] == "d") continue;
                if (Fluid(x, y - 1, z).LiquidLevel != 7) continue;
                foreach (var (dx, dz) in Dirs)
                {
                    int ux = x - dx, uz = z - dz;
                    var u = Fluid(ux, y, uz);
                    if (!Fresh(u) || u.LiquidLevel != 7) continue;
                    // a sill (layouts 5-7), or the edge column's water going down (layout 8, no sill)
                    bool sill = SolidAt(ux, y - 1, uz) && !Fluid(ux, y - 1, uz).IsLiquid();
                    if (!sill && Fluid(ux, y - 1, uz).LiquidLevel != 7) continue;
                    int tx = ux, ty = y - 1, tz = uz;
                    if (!sill)
                    {
                        while (ty > y - 20 && !SolidAt(tx, ty, tz)) ty--;
                        if (!SolidAt(tx, ty, tz)) continue;
                    }
                    if (target == "ramp_bed" || target == "mid_bed")
                    {
                        tx = target == "mid_bed" ? x + 4 * dx : x; tz = target == "mid_bed" ? z + 4 * dz : z;
                        ty = y - 1;
                        while (ty > y - 20 && !SolidAt(tx, ty, tz) && Fluid(tx, ty, tz).LiquidLevel == 7) ty--;
                        if (!SolidAt(tx, ty, tz)) continue;
                    }
                    else if (target == "bank")
                    {
                        int px = -dz, pz = dx, k = 1;
                        for (; k <= 16; k++) if (SolidAt(x + px * k, y - 1, z + pz * k)) break;
                        if (k > 16) continue;
                        tx = x + px * k; tz = z + pz * k;
                    }
                    return new Dictionary<string, object> { ["x"] = ux, ["z"] = uz, ["y"] = y, ["wt"] = y, ["nx"] = x, ["nz"] = z, ["sill"] = sill,
                        ["tx"] = tx, ["ty"] = ty, ["tz"] = tz, ["target"] = target, ["block"] = b.Code.ToString(), ["edge"] = u.Code.ToString(),
                        ["targetBlock"] = ba.GetBlock(new BlockPos(tx, ty, tz), BlockLayersAccess.SolidBlocks).Code?.ToString() };
                }
            }
            return null;
        }

        /// <summary>
        /// A waterfall's lip: a fresh source with a side open (not a source) where water falls 4 or more blocks before it
        /// lands, whatever Earth drew in the fall. Searches 160 blocks around the site.
        /// </summary>
        Dictionary<string, object> FindFall(int cx, int cz)
        {
            foreach (var (x, z) in Around(cx, cz, 160))
            {
                if (!Free(x, z)) continue;
                int y = TopFluid(x, z, out var b);
                if (y < 0 || b.LiquidCode != "water" || b.LiquidLevel != 7) continue;
                foreach (var (dx, dz) in Dirs)
                {
                    int nx = x + dx, nz = z + dz;
                    if (!Open(nx, y, nz) || Fluid(nx, y, nz).LiquidLevel == 7) continue;
                    int n = 0;
                    for (int yy = y - 1; yy > y - 60 && Open(nx, yy, nz) && Fluid(nx, yy, nz).LiquidLevel != 7; yy--) n++;
                    if (n < 4) continue;
                    return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["y"] = y, ["wt"] = y, ["nx"] = nx, ["nz"] = nz, ["fall"] = n, ["block"] = b.Code.ToString(), ["top"] = Fluid(nx, y, nz).Code?.ToString() };
                }
            }
            return null;
        }

        /// <summary>Fresh sources with a side open onto water or air that is not a source (edges), as "rapid/ordinary".</summary>
        string EdgeCount(int x0, int x1, int y0, int y1, int z0, int z1)
        {
            int rapid = 0, ordinary = 0;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    for (int y = y0; y <= y1; y++)
                    {
                        var f = Fluid(x, y, z);
                        if (f.LiquidCode != "water" || f.LiquidLevel != 7) continue;
                        bool edge = Dirs.Any(d => Open(x + d.dx, y, z + d.dz) && Fluid(x + d.dx, y, z + d.dz).LiquidLevel != 7);
                        if (!edge) continue;
                        if (f.FirstCodePart() == "rapidwater") rapid++; else ordinary++;
                    }
            return rapid + " rapid / " + ordinary + " ordinary";
        }

        Dictionary<string, object> FindRapid(int cx, int cz)
        {
            foreach (var (x, z) in Around(cx, cz, 80))
            {
                if (!Free(x, z)) continue;
                int y = TopFluid(x, z, out var b);
                if (y < 0 || !b.Code.Path.StartsWith("rapidwater-") || b.LiquidLevel >= 7) continue;
                string flow = b.Variant["flow"];
                int ax, az;
                string side;
                if (flow == "e" || flow == "w") { ax = 1; az = 0; side = "north"; }
                else if (flow == "n" || flow == "s") { ax = 0; az = 1; side = "east"; }
                else continue;
                bool ok = true;
                for (int a = -1; a <= 1 && ok; a++)
                    for (int dy = 0; dy <= 2 && ok; dy++)
                        ok = Open(x + ax * a, y + dy, z + az * a);
                if (!ok) continue;
                return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["y"] = y, ["wt"] = y, ["block"] = b.Code.ToString(), ["side"] = side, ["wheelY"] = y + 1 };
            }
            return null;
        }

        // ---------- labs ----------
        // Lab coordinates: k along +x, w along +z, from the lab origin (x, z); the water level Ly is the slab's top layer.

        int I(Dictionary<string, object> p, string k) => (int)p[k];
        static int X(Dictionary<string, object> p, int k) => (int)p["x"] + k;
        static int Z(Dictionary<string, object> p, int w) => (int)p["z"] + w;

        void SetCell(int x, int y, int z, int solid, int fluid)
        {
            var pos = new BlockPos(x, y, z);
            ba.SetBlock(solid, pos);
            ba.SetBlock(fluid, pos, BlockLayersAccess.Fluid);
        }

        /// <summary>A plant in the solid layer of a cell, its water kept (no updates, as world generation).</summary>
        void PlantAt(int x, int y, int z, string code)
        {
            var plant = api.World.GetBlock(new AssetLocation(code));
            if (plant == null || plant.Id == 0) { Log("unknown plant {0}", code); return; }
            ba.SetBlock(plant.Id, new BlockPos(x, y, z), BlockLayersAccess.SolidBlocks);
        }

        Dictionary<string, object> BuildLab(Scenario s, int ox, int oz)
        {
            // lab extent in (k, w) and depth below the water level
            int k0 = -10, k1 = s.Length + 3, w0 = -6, w1 = Math.Max(s.Width, 2) + 6, depth = 8;
            switch (s.Kind)
            {
                case "lab_step": k1 = 17; w1 = 10; break;
                case "lab_stepchannel": k1 = 9; w1 = 20; break;
                case "lab_drop": k1 = 23; w1 = 8; depth = Math.Max(8, s.Drop + 4); break;
                case "lab_bucket": k0 = -4; k1 = 8; w0 = -4; w1 = 4; break;
                case "lab_ice": k1 = 16; w1 = 8; break;
                case "lab_chute": k1 = 23; w1 = 8; depth = 10; break;
                case "lab_fall": k1 = 16; w1 = 8; depth = 12; break;
                case "lab_spill": k0 = -14; k1 = 14; w0 = -8; w1 = 8; break;
            }
            int top = 0;
            for (int k = k0; k <= k1; k++)
                for (int w = w0; w <= w1; w++)
                    top = Math.Max(top, ba.GetRainMapHeightAt(new BlockPos(ox + k, 0, oz + w)));
            int ly = Math.Min(top + depth + 4, H - 20);
            var p = new Dictionary<string, object> { ["x"] = ox, ["z"] = oz, ["wt"] = ly, ["k0"] = k0, ["k1"] = k1, ["w0"] = w0, ["w1"] = w1 };
            for (int k = k0; k <= k1; k++)
                for (int w = w0; w <= w1; w++)
                {
                    for (int y = ly - depth; y <= ly; y++) SetCell(ox + k, y, oz + w, stone, 0);
                    for (int y = ly + 1; y <= ly + 8; y++) SetCell(ox + k, y, oz + w, 0, 0);
                }
            int water = api.World.GetBlock(new AssetLocation("game:" + (s.Liquid == "saltwater" ? "saltwater" : "water") + "-still-7")).Id;
            void Pool(int ka, int kb, int wa, int wb, int y) { for (int k = ka; k <= kb; k++) for (int w = wa; w <= wb; w++) SetCell(ox + k, y, oz + w, 0, water); }
            void Carve(int ka, int kb, int wa, int wb, int ya, int yb) { for (int k = ka; k <= kb; k++) for (int w = wa; w <= wb; w++) for (int y = ya; y <= yb; y++) SetCell(ox + k, y, oz + w, 0, 0); }
            switch (s.Kind)
            {
                case "lab_trench":
                    for (int d = 0; d < Math.Max(1, s.PoolDepth); d++) Pool(-8, 0, -4, s.Width + 3, ly - d);
                    p["depth"] = Math.Max(1, s.Depth);
                    break;
                case "lab_step":
                    // upper pool at ly; lower pool one block down beyond a 1-block lip at k = 1, open air above it
                    Pool(-8, 0, 0, 7, ly);
                    Pool(1, 14, 0, 7, ly - 1);
                    Carve(2, 14, 0, 7, ly, ly);
                    if (s.Touching) Pool(-8, 0, 0, 7, ly - 1);
                    if (s.EdgePlant != null) for (int w = 0; w <= 7; w++) PlantAt(ox, ly, oz + w, s.EdgePlant);
                    break;
                case "lab_stepchannel":
                    Pool(-8, 0, 0, 5, ly);
                    Pool(-8, 0, 12, 17, ly - 1);
                    Carve(-8, 0, 12, 17, ly, ly);
                    break;
                case "lab_drop":
                    {
                        int h = s.Drop;
                        Pool(-8, 0, -3, 4, ly);
                        Carve(7, 20, 0, 1, ly - h, ly);
                        if (s.Wheel)
                        {
                            int wy = ly - h + 1;
                            PlaceWheel(s.Name, ox + 8, wy, oz, "north");
                            p["wheelX"] = ox + 8; p["wheelY"] = wy; p["wheelZ"] = oz;
                        }
                        break;
                    }
                case "lab_chute":
                    {
                        // as Earth's pool layout 2 draws a 3-block fall: a pool, then 3 steps of 1 block every Spacing
                        // blocks (each pool one block lower), then a calm channel; 2 wide, all source water, built without
                        // updates like generated terrain
                        int sp = s.Spacing;
                        Pool(-8, 0, -3, 4, ly);
                        Pool(1, sp, 0, 1, ly - 1);
                        Pool(sp + 1, 2 * sp, 0, 1, ly - 2);
                        Pool(2 * sp + 1, 20, 0, 1, ly - 3);
                        Carve(1, 20, 0, 1, ly, ly);
                        Carve(sp + 1, 20, 0, 1, ly - 1, ly - 1);
                        Carve(2 * sp + 1, 20, 0, 1, ly - 2, ly - 2);
                        if (s.PoolPlant != null)
                            for (int w = 0; w <= 1; w++)
                            {
                                for (int k = 1; k <= sp; k++) PlantAt(ox + k, ly - 1, oz + w, s.PoolPlant);
                                for (int k = sp + 1; k <= 2 * sp; k++) PlantAt(ox + k, ly - 2, oz + w, s.PoolPlant);
                                for (int k = 2 * sp + 1; k <= 20; k++) PlantAt(ox + k, ly - 3, oz + w, s.PoolPlant);
                            }
                        if (s.EdgePlant != null)
                            for (int w = 0; w <= 1; w++)
                            {
                                PlantAt(ox, ly, oz + w, s.EdgePlant);
                                PlantAt(ox + sp, ly - 1, oz + w, s.EdgePlant);
                                PlantAt(ox + 2 * sp, ly - 2, oz + w, s.EdgePlant);
                            }
                        if (s.Wheel)
                        {
                            // axle above the second step: its lower rim sits in the ramp below the second step's edge
                            PlaceWheel(s.Name, ox + sp + 1, ly, oz, "north");
                            p["wheelX"] = ox + sp + 1; p["wheelY"] = ly; p["wheelZ"] = oz;
                        }
                        break;
                    }
                case "lab_fall":
                    // a pool 2 deep up to its lip, an 8-block fall into a plunge pool, 8 wide; built without updates
                    Pool(-8, 0, -3, 4, ly);
                    Pool(-8, 0, -3, 4, ly - 1);
                    Carve(1, 14, -3, 4, ly - 7, ly);
                    Pool(1, 14, -3, 4, ly - 8);
                    break;
                case "lab_bucket":
                    // a dry ledge cell at the water level beside a 3-deep dry pit
                    Carve(0, 0, 0, 0, ly, ly);
                    Carve(1, 4, -1, 1, ly - 3, ly);
                    break;
                case "lab_ice":
                    {
                        // a pool 2 deep whose surface is frozen beyond k = 0: lake ice in the fluid layer, as vanilla freezes
                        // still water (it melts above 2 °C: put the lab base somewhere cold); a stone pillar in the ice at its
                        // far side carries the stream's source; built without updates
                        int ice = api.World.GetBlock(new AssetLocation("game:lakeice"))?.Id ?? 0;
                        Pool(-8, 12, -3, 4, ly - 1);
                        Pool(-8, 0, -3, 4, ly);
                        for (int k = 1; k <= 12; k++) for (int w = -3; w <= 4; w++) SetCell(ox + k, ly, oz + w, 0, ice);
                        SetCell(ox + 12, ly, oz + 3, stone, 0);
                        // a rim wall two high around the slab: its edge is otherwise a cliff within H4's 6-block window,
                        // which makes any source near it a rapid edge whatever the ice does
                        for (int k = k0; k <= k1; k++)
                            for (int w = w0; w <= w1; w++)
                                if (k == k0 || k == k1 || w == w0 || w == w1)
                                    for (int y = ly + 1; y <= ly + 2; y++) SetCell(ox + k, y, oz + w, stone, 0);
                        p["iceCells"] = 12 * 8 - 1;
                        break;
                    }
                case "lab_spill":
                    {
                        // a player's source spilling into the sea (a Mod DB report, 2026-11-08: "the sea climbed to the water
                        // source"): a sea 3 deep at k <= 0, up to Length columns out, cliffs beyond it; a shore rising one block
                        // every Spacing columns from k = 1 (its first columns flush with the sea's surface); a rim wall 8 high
                        // around it all, so whatever rises stays in the lab; built without updates
                        // Order "cliff": the shore is a plateau Drop - 1 high instead, a vertical cliff over the sea
                        int sp = Math.Max(1, s.Spacing), sea = Math.Clamp(s.Length, 1, 13);
                        for (int d = 0; d < 3; d++) Pool(-sea, 0, -7, 7, ly - d);
                        for (int k = 1; k <= 13; k++)
                            for (int w = -7; w <= 7; w++)
                                for (int y = ly + 1; y <= ly + (s.Order == "cliff" ? s.Drop - 1 : (k - 1) / sp); y++) SetCell(ox + k, y, oz + w, stone, 0);
                        for (int k = k0; k <= k1; k++)
                            for (int w = w0; w <= w1; w++)
                                if (k == k0 || k == k1 || w == w0 || w == w1 || k < -sea)
                                    for (int y = ly + 1; y <= ly + 8; y++) SetCell(ox + k, y, oz + w, stone, 0);
                        // Order "field": a dyke 2 high at k = 1 keeps the shore (made flat with a large Spacing) apart from the sea
                        if (s.Order == "field")
                            for (int w = -7; w <= 7; w++) for (int y = ly + 1; y <= ly + 2; y++) SetCell(ox + 1, y, oz + w, stone, 0);
                        // the outlet: a slot at w = 0 through the rim at both ends, and through the cliffs west of a short sea
                        if (s.Outlet)
                            for (int y = ly + 1; y <= ly + 8; y++)
                            {
                                for (int k = k0; k < -sea; k++) SetCell(ox + k, y, oz, 0, 0);
                                SetCell(ox + k1, y, oz, 0, 0);
                            }
                        // Order "slabs" (rc.4, audit): a pool 5 x 5 floored with bottom slabs over air, walled 2 high, on the
                        // flat shore (a large Spacing), its floor 4 above the sea; Apply pours a source onto it
                        if (s.Order == "slabs")
                        {
                            int slab = api.World.GetBlock(new AssetLocation("game:cobblestoneslab-granite-down-free"))?.Id ?? stone;
                            for (int k = 2; k <= 8; k++)
                                for (int w = -3; w <= 3; w++)
                                {
                                    bool rim = k == 2 || k == 8 || w == -3 || w == 3;
                                    SetCell(ox + k, ly + 4, oz + w, rim ? stone : slab, 0);
                                    if (rim) SetCell(ox + k, ly + 5, oz + w, stone, 0);
                                }
                        }
                        p["outlet"] = s.Outlet;
                        p["spillLiquid"] = s.SpillLiquid ?? s.Liquid;
                        p["seaLength"] = sea;
                        break;
                    }
            }
            return p;
        }

        // ---------- player actions ----------

        int broken;
        void Break(int x, int y, int z)
        {
            var pos = new BlockPos(x, y, z);
            if (ba.GetBlock(pos, BlockLayersAccess.SolidBlocks).Id == 0) return;
            ba.BreakBlock(pos, null, 0);
            broken++;
        }

        void Place(int x, int y, int z, int id)
        {
            var pos = new BlockPos(x, y, z);
            ba.SetBlock(id, pos);
            ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
            ba.TriggerNeighbourBlockUpdate(pos);
        }

        /// <summary>At StonesAtSec puts cobblestone into the cells (as a player would), and breaks it out again 20 s later.</summary>
        void Stones(Dictionary<string, object> p, List<(int x, int y, int z)> cells)
        {
            int at = I(p, "stonesAtSec");
            if (at <= 0) return;
            api.Event.RegisterCallback(_ => { foreach (var (x, y, z) in cells) Place(x, y, z, cobble); Log("{0}: {1} stones placed", p["name"], cells.Count); }, at * 1000);
            api.Event.RegisterCallback(_ => { foreach (var (x, y, z) in cells) Break(x, y, z); Log("{0}: stones removed", p["name"]); }, (at + 20) * 1000);
        }

        void EnsureFloor(int x, int y, int z) { if (!SolidTop(x, y, z)) ba.SetBlock(cobble, new BlockPos(x, y, z)); }

        /// <summary>Runs the rows in order, one per second, or all at once.</summary>
        void Rows(List<Action> rows, bool oneBySecond)
        {
            if (!oneBySecond) { foreach (var r in rows) r(); return; }
            for (int i = 0; i < rows.Count; i++) { var r = rows[i]; api.Event.RegisterCallback(_ => r(), i * 1000); }
        }

        void Apply(Dictionary<string, object> p)
        {
            broken = 0;
            int x = I(p, "x"), z = I(p, "z"), wt = I(p, "wt"), ly = wt;
            switch ((string)p["kind"])
            {
                case "river_trench":
                case "sea_trench":
                    DigLevel(p, I(p, "length"), I(p, "width"), (string)p["order"] == "out");
                    break;
                case "ramp":
                case "rapids_poke":
                case "chute_poke":
                case "fall_poke":
                case "river_poke":
                    {
                        int px = p.ContainsKey("nx") ? I(p, "nx") : x, pz = p.ContainsKey("nz") ? I(p, "nz") : z, y = I(p, "y");
                        Place(px, y, pz, cobble);
                        api.Event.RegisterCallback(_ => { Break(px, y, pz); Log("{0}: block removed again", p["name"]); }, 3000);
                        if ((string)p["kind"] == "fall_poke")
                        {
                            // stones into the curtain beside the lip: the top cell and two lower ones, three columns wide
                            int ddx = px - x, ddz = pz - z, ppx = -ddz, ppz = ddx, fall = I(p, "fall");
                            var cells = new List<(int, int, int)>();
                            foreach (int yy in new[] { y, y - 1, y - Math.Min(3, fall - 1) })
                                for (int w = -1; w <= 1; w++) cells.Add((px + ppx * w, yy, pz + ppz * w));
                            Stones(p, cells);
                        }
                        break;
                    }
                case "river_block":
                    {
                        int muddy = api.World.GetBlock(new AssetLocation("game:muddygravel"))?.Id ?? cobble;
                        for (int dx = -2; dx <= 2; dx++)
                            for (int y = Terrain(x + dx, z) + 1; y <= wt; y++) Place(x + dx, y, z, muddy);
                        Log("{0}: a line of 5 gravel columns left in the river", p["name"]);
                        break;
                    }
                case "wheel":
                    PlaceWheel((string)p["name"], x, I(p, "wheelY"), z, (string)p["side"]);
                    break;
                case "step_break":
                    Break(I(p, "tx"), I(p, "ty"), I(p, "tz"));
                    Log("{0}: broke {1} at {2},{3},{4}", p["name"], p["target"], I(p, "tx"), I(p, "ty"), I(p, "tz"));
                    break;
                case "lab_fall":
                    {
                        // poke the lip so the fall starts, then stones into the curtain at three heights and out again
                        Place(X(p, 1), ly, Z(p, 1), cobble);
                        api.Event.RegisterCallback(_ => Break(X(p, 1), ly, Z(p, 1)), 2000);
                        var cells = new List<(int, int, int)>();
                        foreach (int y in new[] { ly, ly - 1, ly - 4 })
                            for (int w = 0; w <= 2; w++) cells.Add((X(p, 1), y, Z(p, w)));
                        Stones(p, cells);
                        break;
                    }
                case "lab_trench":
                    {
                        int len = I(p, "length"), width = I(p, "width");
                        bool outward = (string)p["order"] == "out";
                        var rows = new List<Action>();
                        int depth = p.ContainsKey("depth") ? I(p, "depth") : 1;
                        foreach (int k in outward ? Enumerable.Range(1, len) : Enumerable.Range(1, len).Reverse())
                        { int kk = k; rows.Add(() => { for (int w = 0; w < width; w++) for (int d = 0; d < depth; d++) Break(X(p, kk), ly - d, Z(p, w)); }); }
                        Rows(rows, outward);
                        break;
                    }
                case "lab_step":
                    for (int w = 0; w <= 7; w++) Break(X(p, 1), ly, Z(p, w));
                    break;
                case "lab_stepchannel":
                    {
                        // a 2-wide channel from the upper pool around to the lower pool, with a 1-block step half way:
                        // the lower half is dug first, outward from the lower pool, then the upper half from the upper pool
                        var rows = new List<Action>();
                        void Lower(int k, int w) { Break(X(p, k), ly, Z(p, w)); Break(X(p, k), ly - 1, Z(p, w)); }
                        for (int k = 1; k <= 6; k++) { int kk = k; rows.Add(() => { Lower(kk, 14); Lower(kk, 15); }); }
                        for (int w = 13; w >= 9; w--) { int ww = w; rows.Add(() => { Lower(5, ww); Lower(6, ww); }); }
                        for (int k = 1; k <= 6; k++) { int kk = k; rows.Add(() => { Break(X(p, kk), ly, Z(p, 0)); Break(X(p, kk), ly, Z(p, 1)); }); }
                        for (int w = 2; w <= 8; w++) { int ww = w; rows.Add(() => { Break(X(p, 5), ly, Z(p, ww)); Break(X(p, 6), ly, Z(p, ww)); }); }
                        Rows(rows, true);
                        break;
                    }
                case "lab_drop":
                    {
                        // dig a 2-wide channel outward from the pool; its 6th row breaks through to the drop
                        var rows = new List<Action>();
                        for (int k = 1; k <= 6; k++) { int kk = k; rows.Add(() => { Break(X(p, kk), ly, Z(p, 0)); Break(X(p, kk), ly, Z(p, 1)); }); }
                        Rows(rows, true);
                        int refill = I(p, "refillAtSec"), h = I(p, "drop");
                        if (refill > 0)
                            api.Event.RegisterCallback(_ =>
                            {
                                // fill the drop back to 1 block: raise the lower channel's floor to ly - 2
                                for (int y = ly - h; y <= ly - 2; y++)
                                    for (int k = 7; k <= 20; k++)
                                        for (int w = 0; w <= 1; w++) Place(X(p, k), y, Z(p, w), cobble);
                                Log("{0}: drop filled back to 1 block", p["name"]);
                            }, refill * 1000);
                        break;
                    }
                case "lab_chute":
                    {
                        // poke the first step like a player (place a block on the ramp spot and take it away again)
                        Place(X(p, 1), ly, Z(p, 0), cobble);
                        api.Event.RegisterCallback(_ => { Break(X(p, 1), ly, Z(p, 0)); Log("{0}: block removed again", p["name"]); }, 3000);
                        int refill = I(p, "refillAtSec");
                        if (refill > 0)
                            api.Event.RegisterCallback(_ =>
                            {
                                // fill the first step back in: raise its bed to the pool's floor, so the pool reaches it
                                for (int k = 1; k <= I(p, "spacing"); k++)
                                    for (int w = 0; w <= 1; w++) Place(X(p, k), ly - 1, Z(p, w), cobble);
                                Log("{0}: first step filled back in", p["name"]);
                            }, refill * 1000);
                        break;
                    }
                case "lab_ice":
                    {
                        // a bucket of water emptied onto the ice, and a source on the pillar whose water spreads over the ice
                        // (as a stream or a waterfall onto a frozen pond); vanilla holds both up on the ice
                        int src = api.World.GetBlock(new AssetLocation("game:water-still-7")).Id;
                        foreach (var (k, w) in new[] { (4, -2), (12, 3) })
                        {
                            var pos = new BlockPos(X(p, k), ly + 1, Z(p, w));
                            ba.SetBlock(src, pos, BlockLayersAccess.Fluid);
                            ba.TriggerNeighbourBlockUpdate(pos);
                            api.World.GetBlock(src).OnNeighbourBlockChange(api.World, pos, pos.UpCopy());
                        }
                        break;
                    }
                case "lab_spill":
                    {
                        // a source Drop blocks above the sea on the shore's centre line, set and woken as a bucket spills it;
                        // Order "sea": on the sea's surface, 3 columns out (a bucket emptied onto the sea); "cliff": Width
                        // sources side by side at the cliff's edge (a pool at the top of a waterfall into the sea)
                        int sp = Math.Max(1, I(p, "spacing")), drop = Math.Clamp(I(p, "drop"), 1, 7);
                        string order = (string)p["order"];
                        int src = api.World.GetBlock(new AssetLocation("game:" + (string)p["spillLiquid"] + "-still-7")).Id;
                        // "hole" (rc.4, audit): no source; the block under the middle of the sea's bottom layer is broken,
                        // as a player digging up into a pond from a cave
                        if (order == "hole") { Break(X(p, -6), ly - 3, Z(p, 0)); break; }
                        // "field": Width sources side by side in the middle of the flat shore behind the dyke; "slabs": a
                        // source on the slab floor
                        var cells = order == "slabs" ? new[] { (5, 0, 5) }
                            : order == "sea" ? new[] { (-3, 0, 1) }
                            : order == "cliff" ? Enumerable.Range(0, Math.Max(1, I(p, "width"))).Select(w => (1, w, drop)).ToArray()
                            : order == "field" ? Enumerable.Range(0, Math.Max(1, I(p, "width"))).Select(w => (7, w, 1)).ToArray()
                            : new[] { (Math.Min(13, 1 + (drop - 1) * sp), 0, drop) };
                        foreach (var (k, w, dy) in cells)
                        {
                            var pos = new BlockPos(X(p, k), ly + dy, Z(p, w));
                            ba.SetBlock(src, pos, BlockLayersAccess.Fluid);
                            ba.TriggerNeighbourBlockUpdate(pos);
                            api.World.GetBlock(src).OnNeighbourBlockChange(api.World, pos, pos.UpCopy());
                        }
                        break;
                    }
                case "lab_bucket":
                    {
                        // what a full bucket emptied on the ledge places (vanilla's spill: set the block, wake it and its neighbours)
                        var code = BucketSpill().full;
                        var block = api.World.GetBlock(new AssetLocation(code));
                        var pos = new BlockPos(X(p, 0), ly, Z(p, 0));
                        ba.SetBlock(block.Id, pos, BlockLayersAccess.Fluid);
                        ba.TriggerNeighbourBlockUpdate(pos);
                        block.OnNeighbourBlockChange(api.World, pos, pos.UpCopy());
                        p["spilled"] = code;
                        break;
                    }
            }
            p["broken"] = broken;
            Log("{0}: applied, {1} blocks broken", p["name"], broken);
        }

        void DigLevel(Dictionary<string, object> p, int len, int width, bool outward)
        {
            int x = I(p, "x"), z = I(p, "z"), wt = I(p, "wt"), dx = I(p, "dx"), dz = I(p, "dz"), px = I(p, "px"), pz = I(p, "pz");
            int depth = p.ContainsKey("depth") ? Math.Max(1, I(p, "depth")) : 1;
            void Row(int k)
            {
                for (int w = 0; w < width; w++)
                {
                    int qx = x + dx * k + px * w, qz = z + dz * k + pz * w;
                    EnsureFloor(qx, wt - depth, qz);
                    for (int y = Terrain(qx, qz) + 3; y > wt - depth; y--) Break(qx, y, qz);
                }
            }
            if (!outward) { for (int k = len; k >= 1; k--) Row(k); return; }
            for (int k = 1; k <= len; k++) { int kk = k; api.Event.RegisterCallback(_ => Row(kk), (k - 1) * 1000); }
        }

        static readonly FieldInfo RccField = typeof(BEBehaviorRightClickConstructable).GetField("rcc", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo FlowRate = typeof(BEBehaviorMPWaterWheel).GetField("flowRate", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo SourceCount = typeof(BEBehaviorMPWaterWheel).GetField("suitablePowerSourceBlockCount", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo Blocked = typeof(BEBehaviorMPWaterWheel).GetField("blocked", BindingFlags.NonPublic | BindingFlags.Instance);

        void PlaceWheel(string name, int x, int y, int z, string side)
        {
            var pos = new BlockPos(x, y, z);
            var block = api.World.GetBlock(new AssetLocation("game:waterwheel-3m-" + side));
            ba.SetBlock(block.Id, pos);
            ba.TriggerNeighbourBlockUpdate(pos);
            var be = ba.GetBlockEntity(pos);
            var rc = be?.GetBehavior<BEBehaviorRightClickConstructable>();
            var rcc = rc == null ? null : (RightClickConstruction)RccField.GetValue(rc);
            if (rcc == null) { Mod.Logger.Error("WATERAUDIT {0}: wheel has no construction behaviour", name); return; }
            rcc.CurrentCompletedStage = rcc.Stages.Length - 1;
            be.MarkDirty(true);
            wheels.Add((name, pos));
            Log("{0}: wheel {1} at {2},{3},{4} complete={5}", name, block.Code, x, y, z, rc.IsComplete);
        }

        void SampleWheels(int t)
        {
            foreach (var (name, pos) in wheels)
            {
                var wb = ba.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPWaterWheel>();
                if (wb == null) { wheelLog.Add(new { t, name, missing = true }); continue; }
                wheelLog.Add(new { t, name, flowRate = (float)FlowRate.GetValue(wb), sources = (int)SourceCount.GetValue(wb), blocked = (bool)Blocked.GetValue(wb) });
            }
        }

        void SampleLag(string tag)
        {
            if (tickDts.Count == 0) { lagLog.Add(new { tag, n = 0 }); return; }
            var d = tickDts.OrderBy(v => v).ToList();
            lagLog.Add(new { tag, n = d.Count, mean = d.Average(), p99 = d[(int)(d.Count * 0.99)], max = d[^1], over100ms = d.Count(v => v > 0.1) });
            tickDts.Clear();
        }

        void SampleStats(string tag)
        {
            var t = Type.GetType("Hydrology.HydrologyStats, Hydrology");
            if (t == null) return;
            var row = new Dictionary<string, object> { ["tag"] = tag };
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static)) row[f.Name] = f.GetValue(null);
            statsLog.Add(row);
        }

        // ---------- profiles ----------

        void Profile(Dictionary<string, object> p, string tag)
        {
            var r = new Dictionary<string, object>();
            int x = I(p, "x"), z = I(p, "z"), ly = I(p, "wt");
            string Row(IEnumerable<string> cells) => string.Join(" ", cells);
            switch ((string)p["kind"])
            {
                case "lab_trench":
                    {
                        int len = I(p, "length"), width = I(p, "width");
                        r["trench"] = TrenchProfile(Enumerable.Range(1, len).Select(k => Enumerable.Range(0, width).Select(w => Fluid(X(p, k), ly, Z(p, w))).ToList()));
                        int depth = p.ContainsKey("depth") ? I(p, "depth") : 1;
                        for (int d = 1; d < depth; d++)
                        { int dd = d; r[$"trench-{d}"] = TrenchProfile(Enumerable.Range(1, len).Select(k => Enumerable.Range(0, width).Select(w => Fluid(X(p, k), ly - dd, Z(p, w))).ToList())); }
                        r["pool"] = Census(X(p, -8), X(p, 0), ly, ly, Z(p, -4), Z(p, width + 3));
                        break;
                    }
                case "lab_step":
                    r["center"] = Row(Enumerable.Range(0, 12).Select(k => Code(Fluid(X(p, k), ly, Z(p, 3)))));
                    r["centerBelow"] = Row(Enumerable.Range(0, 12).Select(k => Code(Fluid(X(p, k), ly - 1, Z(p, 3)))));
                    r["upperPool"] = Census(X(p, -8), X(p, 0), ly, ly, Z(p, 0), Z(p, 7));
                    r["aboveLowerPool"] = Census(X(p, 1), X(p, 14), ly, ly, Z(p, 0), Z(p, 7));
                    r["lowerPool"] = Census(X(p, 1), X(p, 14), ly - 1, ly - 1, Z(p, 0), Z(p, 7));
                    break;
                case "lab_stepchannel":
                    r["leg1"] = Row(Enumerable.Range(1, 6).Select(k => Code(Fluid(X(p, k), ly, Z(p, 0)))));
                    r["cross"] = Row(Enumerable.Range(2, 14).Select(w => Code(Fluid(X(p, 5), ly, Z(p, w)))));
                    r["crossBelow"] = Row(Enumerable.Range(9, 7).Select(w => Code(Fluid(X(p, 5), ly - 1, Z(p, w)))));
                    r["leg3"] = Row(Enumerable.Range(1, 6).Select(k => Code(Fluid(X(p, k), ly - 1, Z(p, 14)))));
                    r["leg3Above"] = Row(Enumerable.Range(1, 6).Select(k => Code(Fluid(X(p, k), ly, Z(p, 14)))));
                    r["upperPool"] = Census(X(p, -8), X(p, 0), ly, ly, Z(p, 0), Z(p, 5));
                    r["lowerPool"] = Census(X(p, -8), X(p, 0), ly - 1, ly - 1, Z(p, 12), Z(p, 17));
                    r["aboveLowerHalf"] = Census(X(p, 1), X(p, 6), ly, ly, Z(p, 9), Z(p, 15));
                    break;
                case "lab_drop":
                    {
                        int h = I(p, "drop");
                        r["channel"] = Row(Enumerable.Range(1, 6).Select(k => Code(Fluid(X(p, k), ly, Z(p, 0)))));
                        r["edge"] = Row(new[] { Code(Fluid(X(p, 6), ly, Z(p, 0))), Code(Fluid(X(p, 6), ly, Z(p, 1))) });
                        r["fall"] = Row(Enumerable.Range(0, h + 1).Select(d => Code(Fluid(X(p, 7), ly - d, Z(p, 0)))));
                        r["fall1"] = Row(Enumerable.Range(0, h + 1).Select(d => Code(Fluid(X(p, 7), ly - d, Z(p, 1)))));
                        r["fallSolid"] = Row(Enumerable.Range(0, h + 1).Select(d => Solid(X(p, 7), ly - d, Z(p, 0)).Code?.Path ?? "?"));
                        r["below"] = Row(Enumerable.Range(7, 14).Select(k => Code(Fluid(X(p, k), ly - h, Z(p, 0)))));
                        r["belowUp1"] = Row(Enumerable.Range(7, 14).Select(k => Code(Fluid(X(p, k), ly - 1, Z(p, 0)))));
                        r["lower"] = Census(X(p, 7), X(p, 20), ly - h, ly, Z(p, 0), Z(p, 1));
                        r["pool"] = Census(X(p, -8), X(p, 0), ly, ly, Z(p, -3), Z(p, 4));
                        break;
                    }
                case "lab_chute":
                    {
                        // one row per level along w = 0, k = 0..3 spacing + 2; edges: the last source of each pool
                        int sp = I(p, "spacing");
                        for (int d = 0; d <= 3; d++)
                        {
                            int dd = d;
                            r["y-" + d] = Row(Enumerable.Range(0, 3 * sp + 3).Select(k => Code(Fluid(X(p, k), ly - dd, Z(p, 0)))));
                        }
                        r["edges"] = Row(new[] { (0, 0), (sp, 1), (2 * sp, 2) }.Select(e => Code(Fluid(X(p, e.Item1), ly - e.Item2, Z(p, 0))) + Code(Fluid(X(p, e.Item1), ly - e.Item2, Z(p, 1)))));
                        r["all"] = Census(X(p, -8), X(p, 20), ly - 3, ly, Z(p, -3), Z(p, 4));
                        // both block layers at the end of the first pool and just beyond it, ly + 1 down to ly - 3
                        r["cells"] = Row(new[] { sp - 1, sp, sp + 1 }.SelectMany(k => Enumerable.Range(-1, 5).Select(d =>
                            $"k{k}y-{d}:{Solid(X(p, k), ly - d, Z(p, 0)).Code?.Path}/{Code(Fluid(X(p, k), ly - d, Z(p, 0)))}")));
                        break;
                    }
                case "lab_fall":
                    {
                        // sources left in the fall (k = 1..3, above the plunge pool): none should hang there
                        int hang = 0;
                        for (int k = 1; k <= 3; k++) for (int w = -3; w <= 4; w++) for (int y = ly - 7; y <= ly; y++)
                                    if (Fluid(X(p, k), y, Z(p, w)).LiquidLevel == 7) hang++;
                        r["hanging"] = hang;
                        r["col1"] = Row(Enumerable.Range(0, 9).Select(d => Code(Fluid(X(p, 1), ly - d, Z(p, 1)))));
                        r["col1raw"] = Row(Enumerable.Range(0, 9).Select(d => Fluid(X(p, 1), ly - d, Z(p, 1)).Code?.Path ?? "air"));
                        r["col2raw"] = Row(Enumerable.Range(0, 9).Select(d => Fluid(X(p, 2), ly - d, Z(p, 1)).Code?.Path ?? "air"));
                        r["col0"] = Row(Enumerable.Range(0, 9).Select(d => Code(Fluid(X(p, 1), ly - d, Z(p, 0)))));
                        r["lipTop"] = Row(Enumerable.Range(-3, 8).Select(w => Code(Fluid(X(p, 0), ly, Z(p, w)))));
                        r["lipLow"] = Row(Enumerable.Range(-3, 8).Select(w => Code(Fluid(X(p, 0), ly - 1, Z(p, w)))));
                        break;
                    }
                case "lab_spill":
                    {
                        // per layer from the sea's surface (L+0) up: sources and flowing cells of salt (S, s) and fresh (W, w)
                        // water inside the rim, and the highest layer holding a source
                        int highest = 0;
                        for (int d = 0; d <= 8; d++)
                        {
                            int sS = 0, sF = 0, wS = 0, wF = 0;
                            for (int k = -13; k <= 13; k++)
                                for (int w = -7; w <= 7; w++)
                                {
                                    var f = Fluid(X(p, k), ly + d, Z(p, w));
                                    if (!f.IsLiquid()) continue;
                                    bool salt = f.LiquidCode == "saltwater", source = f.LiquidLevel == 7;
                                    if (salt) { if (source) sS++; else sF++; } else if (source) wS++; else wF++;
                                    if (source) highest = Math.Max(highest, d);
                                }
                            r["L+" + d] = $"{sS}S {sF}s {wS}W {wF}w";
                        }
                        r["highestSource"] = highest;
                        break;
                    }
                case "lab_bucket":
                    r["ledge"] = Code(Fluid(X(p, 0), ly, Z(p, 0)));
                    r["pit"] = Census(X(p, 1), X(p, 4), ly - 3, ly, Z(p, -1), Z(p, 1));
                    break;
                case "river_trench":
                case "sea_trench":
                    {
                        int dx = I(p, "dx"), dz = I(p, "dz"), px = I(p, "px"), pz = I(p, "pz"), len = I(p, "length"), width = I(p, "width");
                        r["trench"] = TrenchProfile(Enumerable.Range(1, len).Select(k => Enumerable.Range(0, width).Select(w => Fluid(x + dx * k + px * w, ly, z + dz * k + pz * w)).ToList()));
                        for (int d = 1; d < (p.ContainsKey("depth") ? I(p, "depth") : 1); d++)
                        {
                            int yy = ly - d;
                            r["trench-" + d] = TrenchProfile(Enumerable.Range(1, len).Select(k => Enumerable.Range(0, width).Select(w => Fluid(x + dx * k + px * w, yy, z + dz * k + pz * w)).ToList()));
                        }
                        r["near"] = Census(x - 8, x + 8, ly - 3, ly, z - 8, z + 8);
                        break;
                    }
                case "ramp":
                    r["near"] = Census(x - 10, x + 10, ly - 4, ly + 2, z - 10, z + 10);
                    break;
                case "step_break":
                    r["near"] = Census(x - 14, x + 14, ly - 6, ly + 3, z - 14, z + 14);
                    break;
                case "wheel":
                    r["near"] = Census(x - 10, x + 10, ly - 4, ly + 2, z - 10, z + 10);
                    // top view, 13 x 13 around the wheel: the top water cell of each column (code and height relative to the
                    // wheel's water cell), '#' for the wheel itself
                    r["map"] = Enumerable.Range(-6, 13).Select(dz => string.Join(" ", Enumerable.Range(-6, 13).Select(dx =>
                    {
                        if (dx == 0 && dz == 0) return "#";
                        int yy = TopFluid(x + dx, z + dz, out var tb);
                        return yy < 0 ? "." : Code(tb) + "@" + (yy - ly);
                    }))).ToArray();
                    break;
                case "chute_poke":
                    r["edge"] = Code(Fluid(I(p, "ex"), ly, I(p, "ez"))) + " " + Code(Fluid(x, ly, z));
                    r["edges"] = EdgeCount(x - 12, x + 12, ly - 6, ly + 3, z - 12, z + 12);
                    r["near"] = Census(x - 12, x + 12, ly - 6, ly + 3, z - 12, z + 12);
                    break;
                case "fall_poke":
                    {
                        int nx = I(p, "nx"), nz = I(p, "nz"), fall = I(p, "fall"), ppx = -(nz - z), ppz = nx - x, hang = 0;
                        for (int w = -2; w <= 2; w++) for (int y = ly - fall + 1; y <= ly; y++)
                                if (Fluid(nx + ppx * w, y, nz + ppz * w).LiquidLevel == 7) hang++;
                        r["hanging"] = hang;
                        r["edge"] = Code(Fluid(x, ly, z));
                        r["fall"] = Row(Enumerable.Range(0, fall + 2).Select(d => Code(Fluid(nx, ly - d, nz))));
                        r["fallRaw"] = Row(Enumerable.Range(0, fall + 2).Select(d => Fluid(nx, ly - d, nz).Code?.Path ?? "air"));
                        r["fallSide"] = Row(Enumerable.Range(0, fall + 2).Select(d => Code(Fluid(nx + ppx, ly - d, nz + ppz))));
                        r["fallSideRaw"] = Row(Enumerable.Range(0, 6).Select(d => Fluid(nx + ppx, ly - d, nz + ppz).Code?.Path ?? "air"));
                        r["near"] = Census(x - 6, x + 6, ly - fall - 2, ly + 1, z - 6, z + 6);
                        break;
                    }
                case "rapids_poke":
                    {
                        int nx = I(p, "nx"), nz = I(p, "nz"), fall = I(p, "fall");
                        r["edge"] = Code(Fluid(x, ly, z));
                        r["fall"] = Row(Enumerable.Range(0, fall + 2).Select(d => Code(Fluid(nx, ly - d, nz))));
                        r["near"] = Census(x - 6, x + 6, ly - fall - 2, ly + 1, z - 6, z + 6);
                        break;
                    }
            }
            if (!profiles.TryGetValue((string)p["name"], out var byTag)) profiles[(string)p["name"]] = byTag = new Dictionary<string, object>();
            byTag[tag] = r;
        }

        /// <summary>One character per trench row: S all sources, f some water, . dry.</summary>
        static string TrenchProfile(IEnumerable<List<Block>> rows) =>
            string.Concat(rows.Select(row => row.All(c => c.IsLiquid() && c.LiquidLevel == 7) ? 'S' : row.Any(c => c.IsLiquid()) ? 'f' : '.'));

        // ---------- shape check (WakeAllSec) ----------

        readonly Dictionary<string, List<Dictionary<(int, int, int), (int, int)>>> shapes = new();
        readonly Dictionary<string, (int x0, int x1, int y0, int y1, int z0, int z1)> shapeAreas = new();

        (int x0, int x1, int y0, int y1, int z0, int z1) ShapeArea(Dictionary<string, object> p)
        {
            int x = I(p, "x"), z = I(p, "z"), y = p.ContainsKey("y") ? I(p, "y") : I(p, "wt"), r = cfg.ShapeRadius;
            int fall = p.ContainsKey("fall") ? I(p, "fall") : 2;
            return (x - r, x + r, Math.Max(1, y - fall - 6), Math.Min(H - 2, y + 4), z - r, z + r);
        }

        /// <summary>Every cell of the area: fluid block id and solid block id.</summary>
        Dictionary<(int, int, int), (int, int)> Capture((int x0, int x1, int y0, int y1, int z0, int z1) a)
        {
            var d = new Dictionary<(int, int, int), (int, int)>();
            var pos = new BlockPos(0);
            for (int x = a.x0; x <= a.x1; x++)
                for (int z = a.z0; z <= a.z1; z++)
                    for (int y = a.y0; y <= a.y1; y++)
                    {
                        pos.Set(x, y, z);
                        d[(x, y, z)] = (ba.GetBlock(pos, BlockLayersAccess.Fluid).Id, ba.GetBlock(pos, BlockLayersAccess.SolidBlocks).Id);
                    }
            return d;
        }

        void WakeArea((int x0, int x1, int y0, int y1, int z0, int z1) a)
        {
            for (int x = a.x0; x <= a.x1; x++)
                for (int z = a.z0; z <= a.z1; z++)
                    for (int y = a.y0; y <= a.y1; y++)
                    {
                        var pos = new BlockPos(x, y, z);
                        var f = ba.GetBlock(pos, BlockLayersAccess.Fluid);
                        if (f.Id != 0) f.OnNeighbourBlockChange(api.World, pos, pos);
                    }
        }

        /// <summary>The cells that differ between two captures: water (presence, source, liquid, height) and labels apart.</summary>
        object ShapeDiff(Dictionary<(int, int, int), (int, int)> a, Dictionary<(int, int, int), (int, int)> b, int ox, int oy, int oz)
        {
            var cells = new List<string>();
            int water = 0, labels = 0, solids = 0;
            foreach (var kv in a)
            {
                var (fa, sa) = kv.Value;
                var (fb, sb) = b[kv.Key];
                if (sa != sb) solids++;
                if (fa == fb) continue;
                var ba1 = api.World.GetBlock(fa); var bb1 = api.World.GetBlock(fb);
                bool labelOnly = fa != 0 && fb != 0 && ba1.LiquidCode == bb1.LiquidCode && ba1.LiquidLevel == bb1.LiquidLevel
                                 && ba1.FirstCodePart() == bb1.FirstCodePart();
                if (labelOnly) labels++; else water++;
                if (cells.Count < 60) cells.Add($"{kv.Key.Item1 - ox},{kv.Key.Item2 - oy},{kv.Key.Item3 - oz}: {ba1.Code?.Path ?? "air"} -> {bb1.Code?.Path ?? "air"}{(labelOnly ? " (label)" : "")}");
            }
            return new { water, labels, solids, cells };
        }

        /// <summary>One character per cell: # solid, . air, S/R/Z source (water, rapid, salt), 1-6 flowing water, a-f rapid
        /// flowing, | falling, ! rapid falling, u still (ordinary, not a source), o still rapid.</summary>
        char Glyph((int fluid, int solid) c)
        {
            if (c.solid != 0) return '#';
            if (c.fluid == 0) return '.';
            var b = api.World.GetBlock(c.fluid);
            string first = b.FirstCodePart(), flow = b.Variant["flow"];
            bool rapid = first == "rapidwater", salt = first == "saltwater";
            if (b.LiquidLevel == 7) return rapid ? 'R' : salt ? 'Z' : 'S';
            if (flow == "d") return rapid ? '!' : '|';
            if (flow == "still") return rapid ? 'o' : 'u';
            return (char)((rapid ? 'a' - 1 : '0') + b.LiquidLevel);
        }

        void WriteShapes()
        {
            if (shapes.Count == 0) return;
            var outShapes = new Dictionary<string, object>();
            foreach (var p in plans)
            {
                string name = (string)p["name"];
                if (!shapes.TryGetValue(name, out var caps) || caps.Count < 2) continue;
                caps.Add(Capture(shapeAreas[name]));
                int ox = I(p, "x"), oy = p.ContainsKey("y") ? I(p, "y") : I(p, "wt"), oz = I(p, "z");
                var area = shapeAreas[name];
                outShapes[name] = new
                {
                    origin = new[] { ox, oy, oz },
                    maps = new[] { "generated", "woken", "end" }.Select((tag, i) => new
                    {
                        tag,
                        layers = Enumerable.Range(-2, 4).Select(dy => new
                        {
                            dy,
                            rows = Enumerable.Range(area.z0, area.z1 - area.z0 + 1).Select(z => string.Concat(Enumerable.Range(area.x0, area.x1 - area.x0 + 1)
                                .Select(x => caps[i].TryGetValue((x, oy + dy, z), out var c) ? Glyph(c) : ' '))).ToList(),
                        }).ToList(),
                    }).ToList(),
                    firstUpdate = ShapeDiff(caps[0], caps[1], ox, oy, oz),
                    afterPoke = ShapeDiff(caps[1], caps[2], ox, oy, oz),
                    generatedToEnd = ShapeDiff(caps[0], caps[2], ox, oy, oz),
                };
            }
            File.WriteAllText(Path.Combine(outDir, "shape.json"), JsonSerializer.Serialize(outShapes, new JsonSerializerOptions { WriteIndented = true }));
        }

        void Finish()
        {
            WriteAreas("area-end.json");
            if (cfg.PlanAreaRadius > 0)
            {
                var keep = cfg.ProbeArea;
                cfg.ProbeArea = plans.Select(p => new[] { I(p, "x"), I(p, "z"), cfg.PlanAreaRadius, Math.Max(1, I(p, "wt") - 12), Math.Min(H - 2, I(p, "wt") + 12) }).ToList();
                WriteAreas("area-plans.json");
                cfg.ProbeArea = keep;
            }
            WriteShapes();
            WriteChunkEvents();
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(outDir, "plan.json"), JsonSerializer.Serialize(plans, opts));
            File.WriteAllText(Path.Combine(outDir, "profiles.json"), JsonSerializer.Serialize(profiles, opts));
            File.WriteAllText(Path.Combine(outDir, "wheels.json"), JsonSerializer.Serialize(wheelLog, opts));
            File.WriteAllText(Path.Combine(outDir, "lag.json"), JsonSerializer.Serialize(lagLog, opts));
            File.WriteAllText(Path.Combine(outDir, "stats.json"), JsonSerializer.Serialize(statsLog, opts));
            // debug builds of Hydrology count updates per cell: the 40 busiest, with the water around them
            if (Type.GetType("Hydrology.HydrologyStats, Hydrology")?.GetField("Hot")?.GetValue(null) is Dictionary<string, int> hot)
            {
                var top = hot.OrderByDescending(kv => kv.Value).Take(40).Select(kv =>
                {
                    var c = kv.Key.Split(',').Select(int.Parse).ToArray();
                    int x = c[0], y = c[1], z = c[2];
                    return new
                    {
                        pos = kv.Key, updates = kv.Value,
                        here = Fluid(x, y, z).Code?.Path, below = Fluid(x, y - 1, z).Code?.Path, above = Fluid(x, y + 1, z).Code?.Path,
                        solidBelow = Solid(x, y - 1, z).Code?.Path,
                        sides = Dirs.Select(d => Fluid(x + d.dx, y, z + d.dz).Code?.Path + "/" + Solid(x + d.dx, y, z + d.dz).Code?.Path).ToArray()
                    };
                });
                File.WriteAllText(Path.Combine(outDir, "hot.json"), JsonSerializer.Serialize(top, opts));
            }
            Log("done");
            if (cfg.Shutdown) api.Server.ShutDown();
        }
    }
}
