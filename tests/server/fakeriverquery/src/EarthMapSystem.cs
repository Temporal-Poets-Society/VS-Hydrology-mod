using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// Hydrology tests only: Earth's river query (decision Earth 0027, interface version 1) without Earth, over rectangles
    /// the water audit registers around its labs (<see cref="AddRegion"/>). Never install it beside Earth: it has Earth's
    /// mod system name. ModConfig/fakeriverquery.json {"Version": 1} sets the world's query version (0 by default); from
    /// version 2 it also answers RiverFlowAt with "Flow" (0..7, n to nw; 2 = east by default) inside its regions.
    /// </summary>
    public class EarthMapSystem : ModSystem
    {
        /// <summary>NotReadyMs (Hydrology 0.6.2-rc.5's test): for this long after the server started, RiverAt throws
        /// <see cref="Core.RiverQueryNotReadyException"/> inside the regions, as Earth 2.0.0-rc.3 does where its drawn
        /// answer is not ready yet.</summary>
        public class FakeConfig { public int Version; public int Flow = 2; public int NotReadyMs; }

        ICoreServerAPI sapi;
        int notReadyMs;

        struct Region { public int X0, Z0, X1, Z1; public bool Rapids; public double Slope, Discharge; }

        readonly List<Region> regions = new();
        readonly object gate = new();

        public int RiverQueryVersion { get; private set; }
        int flow;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            var cfg = api.LoadModConfig<FakeConfig>("fakeriverquery.json") ?? new FakeConfig();
            RiverQueryVersion = cfg.Version;
            flow = cfg.Flow;
            sapi = api;
            notReadyMs = cfg.NotReadyMs;
            Mod.Logger.Notification("fake river query: version {0}, not ready for the first {1} ms", RiverQueryVersion, notReadyMs);
        }

        public void AddRegion(int x0, int z0, int x1, int z1, bool rapids, double slope, double discharge)
        {
            lock (gate) regions.Add(new Region { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Rapids = rapids, Slope = slope, Discharge = discharge });
        }

        public bool RiverAt(int x, int z, out bool rapids, out double slope, out double discharge)
        {
            lock (gate)
                foreach (var r in regions)
                    if (x >= r.X0 && x <= r.X1 && z >= r.Z0 && z <= r.Z1)
                    {
                        if (notReadyMs > 0 && sapi?.World.ElapsedMilliseconds < notReadyMs)
                            throw new Core.RiverQueryNotReadyException($"fake: column {x},{z} not ready yet");
                        rapids = r.Rapids; slope = r.Slope; discharge = r.Discharge;
                        return true;
                    }
            rapids = false; slope = 0; discharge = 0;
            return false;
        }

        public int RiverFlowAt(int x, int z) => RiverQueryVersion >= 2 && RiverAt(x, z, out _, out _, out _) ? flow : -1;
    }
}

namespace EarthMap.Core
{
    /// <summary>As Earth 2.0.0-rc.3's: its drawn answer for a column is not ready yet.</summary>
    public class RiverQueryNotReadyException : System.InvalidOperationException
    {
        public RiverQueryNotReadyException(string message) : base(message) { }
    }
}
