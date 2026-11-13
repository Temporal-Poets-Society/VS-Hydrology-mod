using System.Collections.Generic;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CylinderWorld
{
    /// <summary>The geometry Earth Map reads from Cylinder World (EarthMap.CylinderCompat), as plain fields.</summary>
    public class SeamParams
    {
        public double JumpInset, Circumference, EquatorZ, PoleDistance, SeamHalfWidth;
        public int SeabedY, SpawnX, SpawnZ, WallDepth, Version;
        public bool PolarClimate, ExtendTemperature;
    }

    /// <summary>The switches Earth's preflight reads from Cylinder World's config (EarthMap.CylinderCompat.HasEarthConfiguration,
    /// Earth 2.0): an Earth world is made only with all three on, as Cylinder World's defaults have them.</summary>
    public class CylinderWorldConfig
    {
        public bool Enabled = true, PolarWalls = true, PolarClimate = true;
    }

    /// <summary>
    /// Hydrology tests only (the Congo revert investigation, 2026-10-25): Cylinder World's geometry without any of its
    /// runtime systems (no seam jumps, chunk pre-sending, walls, sea ice, weather sync), so Earth Map can make its world
    /// while Cylinder World's own code is absent. The values come from a real run's dump (the water audit's cylinder.json),
    /// written by mkrun to ModConfig/fakecylinder.json. Never install it for play.
    /// </summary>
    public class CylinderWorldCore : ModSystem
    {
        public SeamParams Params { get; private set; }
        public CylinderWorldConfig Config { get; } = new();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override double ExecuteOrder() => 0;

        public override void StartServerSide(ICoreServerAPI api)
        {
            var path = System.IO.Path.Combine(api.GetOrCreateDataPath("ModConfig"), "fakecylinder.json");
            var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(System.IO.File.ReadAllText(path));
            var p = new SeamParams();
            foreach (var f in typeof(SeamParams).GetFields())
            {
                if (!d.TryGetValue(f.Name, out var v)) { Mod.Logger.Warning("fake Cylinder World: no {0} in fakecylinder.json", f.Name); continue; }
                object val = f.FieldType == typeof(bool) ? v.GetBoolean() : f.FieldType == typeof(int) ? (object)(int)v.GetDouble() : v.GetDouble();
                f.SetValue(p, val);
            }
            Params = p;
            Mod.Logger.Notification("fake Cylinder World (tests only): geometry v{0}, circumference {1}, no runtime systems", p.Version, p.Circumference);
        }

        /// <summary>As Cylinder World's: takes the world's spawn (it would also move vanilla's story chain there).</summary>
        public bool SetSpawn(int x, int z)
        {
            if (Params == null) return false;
            Params.SpawnX = x; Params.SpawnZ = z;
            return true;
        }
    }
}
