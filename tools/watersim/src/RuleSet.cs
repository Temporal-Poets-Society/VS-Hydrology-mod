using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace WaterSim
{
    /// <summary>
    /// Every rule as a switch or parameter. <see cref="Vanilla"/> is the game alone; <see cref="Hydrology013"/> is
    /// Hydrology 0.1.3 (src/Rules at f6f1f25). The candidate rules of the 2026-10-23 study are off in both.
    /// </summary>
    public sealed class RuleSet
    {
        public string Name = "custom";

        // vanilla
        /// <summary>Vanilla's own source rule (3 source neighbours, or 2 and 3 diagonals). Always on in the game.</summary>
        public bool VanillaSourceRule = true;

        // Hydrology 0.1.3
        /// <summary>H1: flowing water does not fall into a same-liquid source (TrySpreadDownwards prefix).</summary>
        public bool H1DrainGuard;
        /// <summary>H1: rapid water flowing down removes only lower neighbours (RemoveOtherLowerNeighbours prefix).</summary>
        public bool H1RemovalGuard;
        /// <summary>H3: flowing water on lower water spreads sideways (the ramp).</summary>
        public bool H3SpreadOnLowerWater;
        /// <summary>H3 (0.1.3): runs of still cells in the air become falling water.</summary>
        public bool H3FallsInAir;
        /// <summary>H2: flowing water between sources fills.</summary>
        public bool H2Fill;
        /// <summary>H2: sources needed among the 4 sides.</summary>
        public int FillSources = 2;
        /// <summary>H2: one side source plus the diagonal source beside it, the square's 4th cell wet.</summary>
        public bool FillDiagonal = true;
        /// <summary>H2: also when resting on a same-liquid source (0024's "or on a source"; 0.1.3 does not).</summary>
        public bool FillOnSource;
        /// <summary>H4: rapids at drops.</summary>
        public bool H4Rapids;
        public int RapidReach = 6;
        public int RapidDrop = 2;
        public int MaxDropScan = 64;
        /// <summary>H4: rapid sheets spread like ordinary water during their update.</summary>
        public bool H4Sheets = true;
        /// <summary>H4: flowing water takes the kind of the water that feeds it.</summary>
        public bool H4FollowFeeder = true;
        /// <summary>H5: an emptied cell wakes the sources beside it.</summary>
        public bool WakeSourcesBeside;
        /// <summary>0.1.12: vanilla's newly created sources notify adjacent water that they can support filling.</summary>
        public bool WakeOnNewSource;

        // candidates (the user's Game-of-Life rules and variants)
        /// <summary>Survival: a source stays only when supported from below and contained on its four sides.</summary>
        public bool Survival;
        /// <summary>Survival: a same-liquid source below counts as support (else only solid ground).</summary>
        public bool SurvivalSourceSupports = true;
        /// <summary>Survival: each side must be solid, a source, or water this source flows into (else only support).</summary>
        public bool SurvivalContained = true;
        /// <summary>Label: non-source water that rests on nothing solid and no source is labelled falling by vanilla's own
        /// label function (GetLiquidBlockId), so a column of water in the air is always a falling column.</summary>
        public bool FallLabel;
        /// <summary>Feed: water above feeds a cell only when it is a source or falling (vanilla: any water above).</summary>
        public int AboveFeeds; // 0 vanilla, 1 source or falling or flowing on this cell, 2 source or falling only
        /// <summary>Feed: falling water beside a cell feeds it (vanilla). Off: falling water still in the air feeds only the cell
        /// below it; once it has landed (ground or a source under it) it feeds its sides as vanilla.</summary>
        public bool FallingFeedsSideways = true;
        /// <summary>Survival also needs birth's neighbours when the source rests on water: 0 off, 1 two sources or the
        /// diagonal square, 2 two sources; a source whose non-source sides are all solid (a shaft) always survives.</summary>
        public int SurvivalBirth;
        /// <summary>Rapid sources with a source beside them spread like ordinary water (vanilla: one stream).</summary>
        public bool RapidSourceSheet;
        /// <summary>A source with a same-liquid source above or below it is not "lone" (H4 then reverts it).</summary>
        public bool LoneVertical;
        /// <summary>A cell that has just become a source (fill, vanilla's rule) wakes the edges within H4's reach.</summary>
        public bool WakeEdgesOnNewSource;
        /// <summary>A flowing cell lower than its feeders allow is raised (6 under water, else the highest feeder - 1), unless
        /// vanilla just lowered it.</summary>
        public bool LevelRelax;
        /// <summary>Refill: a flowing cell resting on a same-liquid source becomes a source when at least this many of its
        /// sides are sources (0: off). Vanilla has the same rule (3) but never reaches it beside a drop.</summary>
        public int RefillOnSource;
        /// <summary>H6 off: a bucket places a source (vanilla's default world setting). On (H6): it spills water-still-3.</summary>
        public bool BucketPlacesSource;
        /// <summary>Communicating vessels: a flowing cell resting on a source fills when a side source also rests on a
        /// source and those two sources below touch (the 2 x 2 vertical square under and beside it is all source water).
        /// Purely local; fills a canal's upper layers once its bottom layer is full.</summary>
        public bool FillVessels;
        /// <summary>Vessels (0.1.7, the flood at a broken sill, 2026-10-24): the side source's water reaches at least as deep
        /// as the water under the cell (the canal fills from deeper water; a one-block hole under a step's edge does not
        /// raise the pool below it).</summary>
        public bool VesselsDepth;
        /// <summary>Vessels (0.1.7): only a cell with no dry ledge beside it (an open cell on ground, or over a drop) fills:
        /// a canal's walls hold it, a river's flush banks do not.</summary>
        public bool VesselsContained;
        /// <summary>Rejected (2026-10-24, docs/whitewater-2026-10-24.md, layout 7 floods): no fill when the cell, the source under it or the side source is
        /// rapid water. A canal fills from calm water; a rapids reach is moving water, and its pools may be 1 deep, where the
        /// depth rule cannot tell a hole under a step's edge from a canal dug from water as deep as itself.</summary>
        public bool VesselsCalm;
        /// <summary>Which cells VesselsCalm looks at (bits): 1 the cell, 2 the source under it, 4 the side source, 8 the source under that.</summary>
        public int VesselsCalmMask = 15;
        /// <summary>Rejected (2026-10-24, same): a rapid side source must reach strictly deeper than the water under the cell (a canal's
        /// mouth stands on the lake; a hole under a rapids step's edge is no deeper than the pool beside it).</summary>
        public bool VesselsRapidDeeper;

        // rules that read Earth's planned river flow query (0022 part A; study 2026-10-25, FlowQuery.cs)
        /// <summary>No vessels fill in a column the query calls natural river water: Earth's steps need no sills.</summary>
        public bool QueryNoVessels;
        /// <summary>In a column the query marks as a rapids reach (average slope over the threshold), surface sources and
        /// flowing water are rapid and stay rapid, whether or not a step is near.</summary>
        public bool QueryRapids;
        /// <summary>A cell whose label turns falling (it no longer feeds its sides) wakes its lower flowing neighbours.</summary>
        public bool WakeOnFallLabel;
        /// <summary>After its update, a cell wakes its flowing neighbours more than one level below it (downhill only, so
        /// the wakes end): a cell that vanilla raised lets the cells it feeds catch up.</summary>
        public bool WakeLowerSides;

        // prototypes of the later targets (0024 section 4): not candidates for version 1
        /// <summary>Managed water bodies: fill to the natural level, ponds behind a blockage, draining (Features.cs).</summary>
        public bool ProtoLevels;
        /// <summary>Sub-levels per flowing height (0: off): water runs 6 × this many blocks.</summary>
        public int ProtoSubLevels;
        /// <summary>Hydrology's fills (H2, refill, vanilla's source rule) and level relax switched off in cells a managed
        /// body is draining or filling (the "local switch-off" of a v1 rule).</summary>
        public bool ProtoLocalOff;
        /// <summary>Managed bodies fill a dug basin from its bottom up: open cells joined to the body below its level, found
        /// by a bounded search, filled one layer at a time (else only cells right beside or above the body).</summary>
        public bool ProtoBasinFill;
        /// <summary>The fill leaves 1-wide passages flowing (a cell fills only inside a 2 x 2 square of open cells), as H2.</summary>
        public bool ProtoWideOnly = true;

        public static RuleSet Vanilla() => new RuleSet { Name = "vanilla", BucketPlacesSource = true };

        public static RuleSet Hydrology013() => new RuleSet
        {
            Name = "hydrology-0.1.3",
            H1DrainGuard = true, H1RemovalGuard = true,
            H3SpreadOnLowerWater = true, H3FallsInAir = true,
            H2Fill = true, FillSources = 2, FillDiagonal = true, FillOnSource = false,
            H4Rapids = true, RapidReach = 6, RapidDrop = 2, H4Sheets = true, H4FollowFeeder = true,
            WakeSourcesBeside = true,
        };

        /// <summary>"vanilla", "hydrology-0.1.3", or a base followed by changes: "hydrology-0.1.3+Survival=1,FallLabel=1".</summary>
        public static RuleSet Parse(string spec)
        {
            string[] parts = spec.Split('+', 2);
            var r = parts[0] == "vanilla" ? Vanilla() : Hydrology013();
            if (parts.Length > 1)
                foreach (var kv in parts[1].Split(',', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = kv.Split('=');
                    var f = typeof(RuleSet).GetField(p[0]) ?? throw new System.ArgumentException("no rule " + p[0]);
                    object v = f.FieldType == typeof(bool) ? (object)(p[1] == "1" || p[1] == "true") : int.Parse(p[1]);
                    f.SetValue(r, v);
                }
            r.Name = spec;
            return r;
        }

        /// <summary>The changes from Hydrology 0.1.3, as Parse reads them.</summary>
        public string Spec()
        {
            var b = Hydrology013();
            var ch = new List<string>();
            foreach (var f in typeof(RuleSet).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name == nameof(Name)) continue;
                object v = f.GetValue(this), d = f.GetValue(b);
                if (Equals(v, d)) continue;
                ch.Add(f.Name + "=" + (v is bool bb ? (bb ? "1" : "0") : v.ToString()));
            }
            return ch.Count == 0 ? "hydrology-0.1.3" : "hydrology-0.1.3+" + string.Join(",", ch);
        }

        public RuleSet Clone(string name = null)
        {
            var r = (RuleSet)MemberwiseClone();
            if (name != null) r.Name = name;
            return r;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            var d = new RuleSet();
            foreach (var f in typeof(RuleSet).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name == nameof(Name)) continue;
                object v = f.GetValue(this);
                if (Equals(v, f.GetValue(d)) && !(v is bool b && b)) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(f.Name).Append('=').Append(v is bool bb ? (bb ? "1" : "0") : v);
            }
            return sb.ToString();
        }

        public Dictionary<string, object> ToDict()
        {
            var o = new Dictionary<string, object>();
            foreach (var f in typeof(RuleSet).GetFields(BindingFlags.Public | BindingFlags.Instance)) o[f.Name] = f.GetValue(this);
            return o;
        }
    }

    public sealed class RuleStats
    {
        public long DrainsRefused, RemovalsRefused, Filled, MadeFalling, RapidSources, RapidReverted, MadeRapid, MadeOrdinary,
            VanillaSources, SurvivalDrained, Labelled, Relaxed;
    }
}
