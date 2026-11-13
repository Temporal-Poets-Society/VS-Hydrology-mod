namespace Hydrology
{
    /// <summary>Counters since the world started, for <c>/hydrology stats</c> and the server tests. Updated on the server's
    /// main thread, but for <see cref="PackCommitsRefused"/> (also the chunk threads, counted with Interlocked).</summary>
    public static class HydrologyStats
    {
        /// <summary>Zeroes every counter: a world started in the same process (singleplayer) counts from 0.</summary>
        public static void Reset()
        {
            foreach (var f in typeof(HydrologyStats).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                if (f.FieldType == typeof(long)) f.SetValue(null, 0L);
#if DEBUG
            Hot.Clear();
#endif
        }

        /// <summary>Liquid updates Hydrology looked at, and the time it spent on them (Stopwatch ticks).</summary>
        public static long Updates, Ticks;
        /// <summary>H1: falls into a source refused; water flowing down kept from removing same-level neighbours.</summary>
        public static long DrainsRefused, RemovalsRefused;
        /// <summary>H2: flowing cells turned into sources.</summary>
        public static long Filled;
        /// <summary>H2's vessels case (part of <see cref="Filled"/>): cells filled on a source.</summary>
        public static long FilledVessels;
        /// <summary>H2: vessels fills refused because the side water was shallower than the water under the cell (0.1.7).</summary>
        public static long VesselsRefused;
        /// <summary>H2 (0.6.2, part of <see cref="Filled"/>): cells filled in an enclosed pocket (Pockets).</summary>
        public static long FilledPockets;
        /// <summary>H2 (0.6.2-rc.3): fills refused because nothing holds water at the cell's level (an outlet, or open
        /// water or land beyond the search).</summary>
        public static long Unheld;
        /// <summary>H2 (0.6.2-rc.7, HY-0007): fills refused because the water bounding a held space is too little to fill it
        /// (a pond breaking into a hollow below it).</summary>
        public static long Unsupplied;
        /// <summary>H2 release (0.6.2-rc.8, HY-0007): ponds a player opened that let their water out; the sources they lost;
        /// of those, the sources the sea, a river or a large lake took; openings beside a pond whose water stayed.</summary>
        public static long Released, ReleasedCells, ReleaseAbsorbed, ReleaseKept;
        /// <summary>0.1.8, Earth's river query: vessels fills refused in natural river columns; sources made rapid in a
        /// rapids reach; wheel readings raised on rivers.</summary>
        public static long VesselsInRiverRefused, RapidSourcesByQuery, FlowLabelsKept, WheelsRaised;
        /// <summary>rc.5: water updates that waited for Earth's answer (not ready yet), and labels kept meanwhile.</summary>
        public static long QueryWaits, LabelsKeptWaiting;
        /// <summary>0.1.9: packed chunk copies dropped because the chunk was used after packing began (EngineChunkPackFix).</summary>
        public static long PackCommitsRefused;
        /// <summary>H2: cells filled on a source with 3 source sides (refill).</summary>
        public static long Refilled;
        /// <summary>H7: sources with nothing under them turned into flowing water; cells labelled falling in the air;
        /// flowing cells raised to their feeders' level.</summary>
        public static long SourcesDrained, Labelled, Relaxed;
#if DEBUG
        /// <summary>Debug builds: updates per cell ("x,y,z"), for the server tests' list of the busiest cells.</summary>
        public static readonly System.Collections.Generic.Dictionary<string, int> Hot = new();
#endif
        /// <summary>H3: still cells in the air turned into falling water.</summary>
        public static long MadeFalling;
        /// <summary>H4: edges turned rapid and back; flowing cells that changed kind.</summary>
        public static long RapidSources, RapidSourcesReverted, MadeRapid, MadeOrdinary;

        public static string Summary() =>
            $"since the world started: updates {Updates} ({Ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0} ms), filled {Filled} ({FilledVessels} on water, {VesselsRefused} refused as too shallow, {VesselsInRiverRefused} refused in river water, {FilledPockets} in pockets, {Unheld} refused as not held, {Unsupplied} as too little water), ponds let out {Released} ({ReleasedCells} sources, {ReleaseAbsorbed} into large water; {ReleaseKept} kept their water), made falling {MadeFalling}, " +
            $"rapid edges {RapidSources} (reverted {RapidSourcesReverted}), cells made rapid {MadeRapid} / ordinary {MadeOrdinary}, " +
            $"drains refused {DrainsRefused}, same-level removals refused {RemovalsRefused}, refilled {Refilled}, " +
            $"unsupported sources drained {SourcesDrained}, labelled falling {Labelled}, relaxed {Relaxed}; " +
            $"river query reads {EarthRiverQuery.Reads} (+{EarthRiverQuery.CacheHits} cached), rapids-reach sources {RapidSourcesByQuery}, river flow labels {FlowLabelsKept}, wheel readings raised {WheelsRaised}, updates waiting for Earth {QueryWaits} (labels kept {LabelsKeptWaiting}); stale chunk packs refused {PackCommitsRefused}";
    }
}
