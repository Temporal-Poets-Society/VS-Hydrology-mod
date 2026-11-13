using System;
using Hydrology.Rules;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Hydrology
{
    /// <summary>
    /// Hydrology: how water fills, steps down and becomes rapids (decision Earth 0024). Uses only vanilla's water, salt
    /// water and rapid water blocks; removing the mod leaves no unknown blocks. The water rules run on the server; the
    /// client (required since 0.6.1) only throws less foam (C1).
    /// </summary>
    public class HydrologySystem : ModSystem
    {
        /// <summary>Mods that replace the same spreading behaviour (no license; their ideas were studied, not their code).</summary>
        static readonly string[] Incompatible = { "realisticwater", "realisticrapids" };

        public HydrologyConfig Config { get; private set; } = new();
        /// <summary>The server's effective vessels setting, also sent to modded clients on joining.</summary>
        public bool Flooding { get; private set; }
        const string SettingsChannel = "hydrology-world-settings";

        /// <summary>
        /// The version of Earth's river query this Hydrology reads (decision Earth 0027). A cross-mod contract: Earth reads
        /// it by reflection as a public static int field of <c>Hydrology.HydrologySystem</c> (a const or static field works;
        /// a property, another name or class, or a non-public field reads as 0). Earth 2.0.0-rc.2 needs 3 (with Hydrology
        /// 0.6.2-rc.1 or newer; 2.0.0-rc.1 needed 2), and picks a new world's pool layout from it: steps without sills
        /// (layout 8) from 1. 2 (0.1.10): it also
        /// reads Earth's RiverFlowAt and keeps a rapids reach's sources labelled with the river's flow (H4's
        /// QueryFlowLabels), so Earth may draw them so (layout 9). 3 (0.6.2, decision Earth 0046): it keeps the river's flow
        /// on every river column's top source, so Earth may label calm rivers too (layout 11).
        /// </summary>
        public const int EarthRiverQueryVersion = 3;

        /// <summary>The side this instance runs on (singleplayer runs one of each in one process, sharing the statics).</summary>
        EnumAppSide? side;

        public override void StartPre(ICoreAPI api)
        {
            if (api is not ICoreServerAPI)
            {
                // the client's own copy of the config, read only (singleplayer: the same file): C1's shares
                HydrologyConfig c;
                try { c = api.LoadModConfig<HydrologyConfig>("hydrology.json") ?? new HydrologyConfig(); }
                catch (Exception e) { Mod.Logger.Warning("Hydrology: hydrology.json unreadable, the client uses the default foam shares: {0}", e.Message); c = new HydrologyConfig(); }
                C1RapidFoam.SourceShare = c.RapidSourceFoam;
                C1RapidFoam.FlowingShare = c.RapidFlowingFoam;
                C1RapidFoam.CalmSourceShare = c.CalmSourceSpecks;
                return;
            }
            // a new world in this process (singleplayer) starts clean, whatever happens below
            GamePatches.NewWorld();
            H2Filling.ResetWorld();
            EarthRiverQuery.Unbind();
            HydrologyStats.Reset();
            bool readable = true;
            try { Config = api.LoadModConfig<HydrologyConfig>("hydrology.json") ?? new HydrologyConfig(); }
            catch (Exception e)
            {
                // not stored back: the user's file keeps their settings for them to fix
                Mod.Logger.Error("Hydrology: ModConfig/hydrology.json unreadable, using defaults and leaving the file as it is: {0}", e.Message);
                Config = new HydrologyConfig();
                readable = false;
            }

            H2Filling.Enabled = Config.Fill;
            H2Filling.Vessels = Config.FillVessels;
            H2Filling.WorldMayFillVessels = Config.WorldMayFillVessels;
            H2Filling.VesselsDepth = Config.VesselsDepth;
            H2Filling.VesselsContained = Config.VesselsContained;
            H2Filling.Pockets = Config.FillPockets;
            H2Filling.RefillOnSource = Math.Max(0, Config.RefillOnSource);
            H2Filling.SupplyFactor = Math.Max(0, Config.SupplyFactor);
            H2Release.Enabled = Config.PondRelease;
            H7Settling.Survival = Config.Survival;
            H7Settling.FallLabel = Config.FallLabel;
            H7Settling.NoSideFeedFromFalling = Config.NoSideFeedFromFalling;
            H7Settling.WakeOnFallLabel = Config.WakeOnFallLabel;
            H7Settling.LevelRelax = Config.LevelRelax;
            H4Rapids.RapidSourceSheets = Config.RapidSourceSheets;
            H4Rapids.WakeOnNewSource = Config.WakeEdgesOnNewSource;
            H4Rapids.LoneVertical = Config.LoneVertical;
            H4Rapids.QueryRapids = Config.QueryRapids;
            H4Rapids.QueryFlowLabels = Config.QueryFlowLabels;
            H8Wheels.Enabled = Config.QueryWheels;
            H8Wheels.PowerRef = Config.WheelPowerRef;
            H8Wheels.MaxBoost = Config.WheelMaxBoost;
            H3Flattening.SpreadsOnLowerWater = Config.Flatten;
            H4Rapids.Enabled = Config.Rapids;
            H4Rapids.MaxDropScan = Math.Max(H4Rapids.RapidDrop, Config.MaxDropScan);
            // written back so new keys appear in the file; a read-only file only costs that
            if (readable)
                try { api.StoreModConfig(Config, "hydrology.json"); }
                catch (Exception e) { Mod.Logger.Warning("Hydrology: could not write ModConfig/hydrology.json: {0}", e.Message); }
        }

        public override void Start(ICoreAPI api)
        {
            side = api.Side;
            if (api.Side == EnumAppSide.Server) H5LiquidHooks.Apply(Mod.Logger, api as ICoreServerAPI, Config.ChunkPackFix);
            else C1RapidFoam.Apply(Mod.Logger);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            var channel = api.Network.RegisterChannel(SettingsChannel)
                .RegisterMessageType<HydrologySettingsRequest>()
                .RegisterMessageType<HydrologyWorldSettings>();
            channel.SetMessageHandler<HydrologySettingsRequest>((player, _) =>
            {
                Flooding = H2Filling.VesselsActive(api.World);
                channel.SendPacket(new HydrologyWorldSettings { Flooding = Flooding }, player);
            });
            Liquids.Init(api.World);
            foreach (var id in Incompatible)
                if (api.ModLoader.IsModEnabled(id))
                    Mod.Logger.Warning("Hydrology: {0} is installed. Hydrology replaces the spreading of water, salt water and rapid water and is " +
                                       "not compatible with it: remove one of the two, or water will behave unpredictably.", id);
            Mod.Logger.Notification("Hydrology: rules H2 fill={0} (on water, vessels={1}, may the world turn them on={5}, depth={6}, contained={7}), " +
                "H3 flatten={2}, H4 rapids={3} (drops measured to {4} blocks)",
                Config.Fill, Config.FillVessels, Config.Flatten, Config.Rapids, H4Rapids.MaxDropScan,
                Config.WorldMayFillVessels, Config.VesselsDepth, Config.VesselsContained);
            Mod.Logger.Notification("Hydrology: H7 survival={0}, fall label={1}, no side feed from falling={2}, wake on fall label={3}, level relax={4}; " +
                "H2 refill on {5} sources, supply factor {9}, pond release={10}; H4 rapid source sheets={6}, wake edges on a new source={7}, lone vertical={8}",
                Config.Survival, Config.FallLabel, Config.NoSideFeedFromFalling, Config.WakeOnFallLabel, Config.LevelRelax,
                Config.RefillOnSource, Config.RapidSourceSheets, Config.WakeEdgesOnNewSource, Config.LoneVertical, H2Filling.SupplyFactor,
                H2Release.Enabled);
            // HY-0007: a pond a player opens lets its water out downhill (the server's break event fires for players only)
            H2Release.Log = line => Mod.Logger.Notification(line);
            H2Release.LogKept = line => Mod.Logger.VerboseDebug(line);
            api.Event.DidBreakBlock += (player, oldBlockId, sel) =>
            {
                if (sel?.Position == null || !H2Release.Enabled || !Config.Fill) return;
                try { H2Release.Opened(api.World, sel.Position); }
                catch (Exception e) { Mod.Logger.Error("Hydrology: pond release at {0} failed: {1}", sel.Position, e); }
            };
            // after every mod's SaveGameLoaded: Earth has fixed the world's query version by then
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
            {
                EarthRiverQuery.Bind(api, Mod.Logger);
                // read the world's vessels flags again now the query is bound: SaveGameLoaded's read (query not yet bound)
                // would otherwise stand for up to a second
                H2Filling.ResetWorld();
                Flooding = H2Filling.VesselsActive(api.World);
                LogWorldFlooding(api);
                if (!EarthRiverQuery.Active) return;
                Mod.Logger.Notification("Hydrology: river query rules: no vessels in river water but enclosed pockets (always with the query), rapids by reach={0}, wheels on rivers={1} (x{2} at most, doubled at slope x discharge {3})",
                    Config.QueryRapids, Config.QueryWheels, Config.WheelMaxBoost, Config.WheelPowerRef);
                if (!Config.QueryNoVessels)
                    Mod.Logger.Warning("Hydrology: QueryNoVessels=false is ignored in this world: its river steps have no sills, and vessels in river water would flood them");
            });
            // an early answer for clients that join before the game runs; RunGame reads and logs it again (HY-0003: Earth sets
            // its attributes at WorldReady, after SaveGameLoaded, so a first start logged nothing here)
            api.Event.SaveGameLoaded += () => Flooding = H2Filling.VesselsActive(api.World);

            api.ChatCommands.Create("hydrology")
                .WithDescription("Hydrology water rules")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("stats")
                    .WithDescription("What the water rules did since the server started")
                    .HandleWith(_ => TextCommandResult.Success(HydrologyStats.Summary()))
                .EndSubCommand();
        }

        /// <summary>
        /// One line at RunGame on what decides deep channels here (HY-0003): by then Earth has set its world attributes
        /// (WorldReady) and its river query is bound, so the line states what the water actually does, on a world's first start
        /// too.
        /// </summary>
        void LogWorldFlooding(ICoreServerAPI api)
        {
            var wc = api.World.Config;
            string why;
            if (wc?.HasAttribute(H2Filling.FloodingKey) == true)
            {
                bool asked = wc.GetAsBool(H2Filling.FloodingKey, false);
                why = "world setting " + H2Filling.FloodingKey + "=" + asked + ", which overrides the legacy vessels switches";
                if (asked && !Flooding) why += "; kept off: the world asks for them only with Earth's river query (" + H2Filling.WorldVesselsWithQueryKey + "), which is not bound";
            }
            else if (Config.FillVessels) why = "config FillVessels=true";
            else if (Flooding && wc?.GetAsBool(H2Filling.WorldVesselsKey, false) == true) why = "the world asks for them (" + H2Filling.WorldVesselsKey + ")";
            else if (Flooding) why = "the world asks for them with Earth's river query (" + H2Filling.WorldVesselsWithQueryKey + ")";
            else if (!Config.WorldMayFillVessels && (wc?.GetAsBool(H2Filling.WorldVesselsKey, false) == true || wc?.GetAsBool(H2Filling.WorldVesselsWithQueryKey, false) == true))
                why = "the world asks for them, but config WorldMayFillVessels=false";
            else if (wc?.GetAsBool(H2Filling.WorldVesselsWithQueryKey, false) == true) why = "the world asks for them only with Earth's river query (" + H2Filling.WorldVesselsWithQueryKey + "), which is not bound";
            else why = "neither the world nor the config asks for them";
            Mod.Logger.Notification("Hydrology: Water flooding (H2's vessels case: deep channels fill layer by layer) is {0} in this world: {1}",
                Flooding ? "on" : "off", why);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            var channel = api.Network.RegisterChannel(SettingsChannel)
                .RegisterMessageType<HydrologySettingsRequest>()
                .RegisterMessageType<HydrologyWorldSettings>()
                .SetMessageHandler<HydrologyWorldSettings>(settings => Flooding = settings.Flooding);
            api.Event.LevelFinalize += () => { if (channel.Connected) channel.SendPacket(new HydrologySettingsRequest()); };
        }

        public override void Dispose()
        {
            // each side removes only its own patches: in singleplayer the client disposes while the server thread still
            // runs its last ticks and saves
            if (side == EnumAppSide.Server)
            {
                H5LiquidHooks.Remove();
                EarthRiverQuery.Unbind(); // lets go of the world's Earth mod system
            }
            else if (side == EnumAppSide.Client) C1RapidFoam.Remove();
        }
    }
}
