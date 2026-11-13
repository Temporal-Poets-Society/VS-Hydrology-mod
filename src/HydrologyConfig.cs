namespace Hydrology
{
    /// <summary>ModConfig/hydrology.json. Each rule can be switched off on its own (H1 and H5 always apply).
    /// Bucket source transport is controlled entirely by vanilla's world setting.</summary>
    public class HydrologyConfig
    {
        /// <summary>H2: flowing water between sources fills in (2-wide channels fill).</summary>
        public bool Fill = true;
        /// <summary>H2, communicating vessels: deep channels fill layer by layer on water. Off by default: Earth 0.16.x's
        /// river steps up to 0.16.7 have no sill and would flood (see H2Filling.Vessels). Earth worlds whose steps have a sill
        /// (0.16.8, pool layout 5) turn it on through the world-config attribute "hydrologyFillVessels".</summary>
        // When present, the user-facing world setting hydrologyFlooding overrides this and WorldMayFillVessels.
        // Neither fallback field is overwritten or saved with a world's override.
        public bool FillVessels = false;
        /// <summary>H2: the world-config attribute "hydrologyFillVessels" (set by Earth) may turn the vessels case on. False
        /// keeps it off on every world unless FillVessels is true.</summary>
        public bool WorldMayFillVessels = true;
        /// <summary>H2, vessels (0.1.7): fill only from water at least as deep as the water under the cell.</summary>
        public bool VesselsDepth = true;
        /// <summary>H2, vessels (0.1.7): fill only a cell with no dry ledge beside it.</summary>
        public bool VesselsContained = true;

        // 0.1.8: rules that read Earth's river query (decision Earth 0027); off by themselves where the world has no query
        /// <summary>H2: no vessels fill in natural river water (Earth's layout 8 steps have no sills). Always on where Earth's
        /// river query answers; false is ignored there and only logs a warning (kept so older files stay readable).</summary>
        public bool QueryNoVessels = true;
        /// <summary>H2 (0.6.2), with the vessels case on: an enclosed pocket of flowing water on sources or its floor fills, in
        /// natural river water too (a basin dug beside a river; a nook a block wide beside the water).</summary>
        public bool FillPockets = true;
        /// <summary>H4: where the query marks rapids (versions 1 and 2: a whole rapids reach; 3: a rapids reach's drops),
        /// surface sources and flowing water are rapid and stay rapid.</summary>
        public bool QueryRapids = true;
        /// <summary>H4 (0.1.10): a surface source keeps the river's flow label from Earth's query, as Earth draws it, so the
        /// river looks like and pushes as running water after it is disturbed: version 2 (pool layout 9) in rapids reaches,
        /// version 3 (pool layout 11, 0.6.2) in every river column, calm ones as ordinary water. Its own switch: since
        /// 0.6.2 it no longer needs <see cref="Rapids"/> and <see cref="QueryRapids"/> on.</summary>
        public bool QueryFlowLabels = true;
        /// <summary>C1 (client, 0.1.11): the share of a rapid-water source's particle ticks that spawn foam (1: vanilla). Earth's
        /// rapids reaches are rapid sources across their whole width, and at vanilla's rate their foam overran the client's
        /// particle pool and pulsed.</summary>
        public double RapidSourceFoam = 0.2;
        /// <summary>C1 (client, 0.1.11): the same for flowing rapid water (ramps, rivulets); 1: vanilla.</summary>
        public double RapidFlowingFoam = 0.5;
        /// <summary>C1 (client, 0.6.2): the same for ordinary sources labelled with a flow (Earth's calm rivers); 1: vanilla. 0:
        /// a calm river's surface throws none of vanilla's white specks (the author, 2026-11-03: "slow water makes bubbles"),
        /// as a lake's still sources throw none; its texture still shows the current. (Replaces 0.6.2-rivers.1's
        /// CalmSourceRipples = 0.2, so a config written by a playtest build does not keep the specks.)</summary>
        public double CalmSourceSpecks = 0;
        /// <summary>H8: a water wheel over natural river water takes more torque from the river's slope × discharge.</summary>
        public bool QueryWheels = true;
        /// <summary>H8: slope (block per block) × discharge (m³/s) that doubles a wheel's torque.</summary>
        public double WheelPowerRef = 10;
        /// <summary>H8: the most a river raises a wheel's torque.</summary>
        public double WheelMaxBoost = 3;

        /// <summary>0.1.9, not a water rule: the workaround for the engine bug that loses block edits when a chunk's packed
        /// copy is committed late (VintageStory-Issues#10177). "auto": on game versions up to 1.22.x; "on": any version;
        /// "off": never. Harmless beside a fixed engine; set "off" once the game's fix is confirmed.</summary>
        public string ChunkPackFix = "auto";
        /// <summary>H3: water spreads sideways on lower water (1-block drops become ramps). H3's falling water in the air has
        /// no switch.</summary>
        public bool Flatten = true;
        /// <summary>H4: drops of 2 or more get a rapid source, rapid falls and a rapid ramp.</summary>
        public bool Rapids = true;
        /// <summary>H4: the deepest drop measured below an edge, in blocks; also bounds H3's and H4's walks along a column of
        /// water.</summary>
        public int MaxDropScan = 384;

        // 0.1.5: the water rules study's settling rules (docs/water-rules-study-2026-10-23.md), each on by default
        /// <summary>H7: a source with nothing under it (air or flowing water) turns into flowing water and drains.</summary>
        public bool Survival = true;
        /// <summary>H7: water resting on neither a block nor a source is labelled falling.</summary>
        public bool FallLabel = true;
        /// <summary>H7: falling water still in the air feeds only the cell below it, no side.</summary>
        public bool NoSideFeedFromFalling = true;
        /// <summary>H7: a cell that turns falling wakes the lower flowing cells beside it.</summary>
        public bool WakeOnFallLabel = true;
        /// <summary>H7: a flowing cell lower than its feeders allow is raised.</summary>
        public bool LevelRelax = true;
        /// <summary>H2: a flowing cell on a source with this many source sides becomes a source (0: off).</summary>
        public int RefillOnSource = 3;
        /// <summary>H4: a rapid source with a source beside it spreads to all sides.</summary>
        public bool RapidSourceSheets = true;
        /// <summary>H4: a cell that has just become a source wakes the edges near it.</summary>
        public bool WakeEdgesOnNewSource = true;
        /// <summary>H4: a rapid source with a source above or below it may turn back to ordinary water.</summary>
        public bool LoneVertical = true;
    }
}
