namespace Hydrology.Rules
{
    /// <summary>
    /// Fable's pass (R1): what a new world (or a second world in the same game process) starts without, in one list. The rules'
    /// state is static; before, it was reset in four places and H4's wake window not at all. Called from
    /// <c>HydrologySystem.StartPre</c>; <see cref="Liquids.Init"/> (the block tables) runs when the world's blocks are known,
    /// and <see cref="H2Release.Load"/> resets the drains, marks and pools again before it reads the save.
    /// </summary>
    public static class RuleState
    {
        public static void ResetWorld()
        {
            GamePatches.NewWorld();      // the "feature off" lines are logged again
            H2Filling.ResetWorld();      // held answers, refusals, retries, the world's vessels answer
            H2Release.ResetWorld();      // drains, pending drains, dam marks, large bodies, river pools
            H4Rapids.ResetWorld();       // the edge wake window
            EarthRiverQuery.Unbind();    // the query, its caches and failures (bound again at RunGame)
            HydrologyStats.Reset();      // counters, the Debug hot map
        }
    }
}
