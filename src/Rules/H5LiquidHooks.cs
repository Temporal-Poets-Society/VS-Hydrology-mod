using System;
using System.Diagnostics;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hydrology.Rules
{
    /// <summary>
    /// H5: local and cheap, on vanilla's event model. Hydrology keeps vanilla's <c>FiniteSpreadingLiquid</c> behaviour
    /// (a neighbour change schedules a delayed update of that cell, on the server) and hooks six of its methods:
    /// - before vanilla spreads a cell downwards: H1's guard and H3's sideways spreading;
    /// - before vanilla's rapid water removes its other neighbours: H1's guard;
    /// - around vanilla's delayed update of a cell: H4 lets a rapid sheet spread like ordinary water during the update;
    ///   after it, H7's survival (sources), H3's falling water in the air, H2, H7's level relax, then H4, on that cell and
    ///   the cells it feeds;
    /// - after GetLiquidBlockId (H7's fall label, H4's river flow labels), instead of GetMaxNeighbourLiquidLevel (H7's
    ///   side-feed rule), and after updateOwnFlowDir (H7's wake on the fall label).
    /// Every hook reads a bounded neighbourhood: a few cells, one column (bounded by <see cref="H4Rapids.MaxDropScan"/>) or
    /// H4's and H2's small searches; still water is never ticked. Clients only get ordinary block changes.
    /// </summary>
    public static class H5LiquidHooks
    {
        const string HarmonyId = "hydrology.liquids";
        static Harmony harmony;

        const string WaterRules = "Hydrology's water rules (H1-H4, H7)";
        const string LabelsFeature = "H7's fall label and H4's river flow labels";
        const string FeedFeature = "H7's side-feed rule";
        const string WakeFeature = "H7's wake on the fall label";
        static readonly Type[] LabelArgs = { typeof(IWorldAccessor), typeof(BlockPos), typeof(Block), typeof(int) };

        // the runtime breakers (GamePatches.Breaker): a hook that keeps throwing switches off its feature only; the three
        // refinements also stop when the water rules stop, and their rule switches go off so the rest agrees with vanilla
        static readonly GamePatches.Breaker Water = new(WaterRules);
        static readonly GamePatches.Breaker Labels = new(LabelsFeature, () => { H7Settling.FallLabel = false; H4Rapids.QueryFlowLabels = false; });
        static readonly GamePatches.Breaker Feed = new(FeedFeature, () => H7Settling.NoSideFeedFromFalling = false);
        static readonly GamePatches.Breaker Wake = new(WakeFeature, () => H7Settling.WakeOnFallLabel = false);

        /// <summary>
        /// The server's patches, each feature through <see cref="GamePatches"/>: the water rules, then the label, side-feed
        /// and wake hooks that only make sense with them, H8's wheels and the engine's chunk-pack fix (both independent).
        /// </summary>
        public static void Apply(ILogger log, Vintagestory.API.Server.ICoreServerAPI sapi = null, string chunkPackFix = "auto")
        {
            // rc.4 (audit): a previous world in this process whose Dispose never ran (the game skips every mod's Dispose when
            // a shutdown handler throws) left its patches and tripped breakers: take them off and apply this world's afresh
            if (harmony != null) Remove();
            harmony = new Harmony(HarmonyId);
            // each feature in its own try: a game class that is renamed or moved makes the method naming it fail to compile,
            // which would otherwise skip the features after it (the chunk-pack fix among them)
            try { ApplyWaterRules(log); }
            catch (Exception e) { log.Error("Hydrology: its water rules could not be applied, water behaves as in vanilla in this world: {0}", e); }
            try { H8Wheels.Apply(harmony, log); }
            catch (Exception e) { log.Error("Hydrology: H8's wheels on rivers could not be applied: {0}", e); }
            if (sapi != null)
                try { EngineChunkPackFix.Apply(harmony, sapi, chunkPackFix, log); }
                catch (Exception e) { log.Error("Hydrology: the chunk pack fix could not be applied: {0}", e); }
        }

        static void ApplyWaterRules(ILogger log)
        {
            var t = typeof(BlockBehaviorFiniteSpreadingLiquid);
            var me = typeof(H5LiquidHooks);
            Water.Reset(log); Labels.Reset(log); Feed.Reset(log); Wake.Reset(log);
            // All or nothing: H2's and H4's sources survive only with H1's guards, and the update's prefix switches a rapid
            // cell to sheet spreading that its postfix switches back. The hooks' own code also uses these public members.
            bool water = GamePatches.HasField(log, WaterRules, t, nameof(BlockBehaviorFiniteSpreadingLiquid.multiplySpread), typeof(bool), false)
                && GamePatches.HasField(log, WaterRules, t, nameof(BlockBehaviorFiniteSpreadingLiquid.ReplacableThreshold), typeof(int), true)
                && GamePatches.HasMethod(log, WaterRules, t, nameof(BlockBehaviorFiniteSpreadingLiquid.GetLiquidBlockId), LabelArgs, typeof(int))
                && GamePatches.Apply(harmony, log, WaterRules,
                    GamePatches.On(t, "TrySpreadDownwards", new[] { typeof(IWorldAccessor), typeof(Block), typeof(Block), typeof(BlockPos) }, typeof(bool),
                        me, prefix: nameof(TrySpreadDownwardsPrefix)),
                    GamePatches.On(t, "RemoveOtherLowerNeighbours", new[] { typeof(Block), typeof(BlockPos), typeof(BlockFacing), typeof(IWorldAccessor) }, typeof(void),
                        me, prefix: nameof(RemoveOtherLowerNeighboursPrefix)),
                    GamePatches.On(t, "OnDelayedWaterUpdateCheck", new[] { typeof(IWorldAccessor), typeof(BlockPos), typeof(float) }, typeof(void),
                        me, prefix: nameof(BeforeUpdate), postfix: nameof(AfterUpdate), finalizer: nameof(UpdateFinalizer)));
            if (!water)
            {
                log.Error("Hydrology: its water rules are not applied: water behaves as in vanilla in this world (see the warning above)");
                return;
            }
            // each of these only refines the rules above; without its hook the rule's own switch goes off, so the rest agrees
            // with vanilla (H7's level relax counts the same feeders as vanilla's lowering once the side-feed rule is off)
            if (!GamePatches.Apply(harmony, log, LabelsFeature,
                    GamePatches.On(t, "GetLiquidBlockId", LabelArgs, typeof(int), me, postfix: nameof(LabelPostfix))))
            {
                H7Settling.FallLabel = false;
                H4Rapids.QueryFlowLabels = false;
            }
            if (!GamePatches.Apply(harmony, log, FeedFeature,
                    GamePatches.On(t, "GetMaxNeighbourLiquidLevel", new[] { typeof(Block), typeof(IWorldAccessor), typeof(BlockPos), typeof(bool).MakeByRefType() }, typeof(int),
                        me, prefix: nameof(MaxNeighbourPrefix))))
                H7Settling.NoSideFeedFromFalling = false;
            if (!GamePatches.Apply(harmony, log, WakeFeature,
                    GamePatches.On(t, "updateOwnFlowDir", new[] { typeof(Block), typeof(IWorldAccessor), typeof(BlockPos) }, typeof(void),
                        me, postfix: nameof(UpdateOwnFlowDirPostfix))))
                H7Settling.WakeOnFallLabel = false;
            log.Notification("Hydrology: liquid hooks applied");
        }

        /// <summary>The server's patches (the client's are C1's).</summary>
        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
        }

        // Each hook below is a thin wrapper: the feature's breaker, then its work in a method of its own inside a try (so
        // even a failure to compile that method after a game update lands in the catch), then vanilla's way on an error.

        static bool TrySpreadDownwardsPrefix(IWorldAccessor world, Block ourBlock, BlockPos pos, ref bool __result)
        {
            if (world.Side != EnumAppSide.Server || Water.Off) return true;
            try { return SpreadDownwards(world, ourBlock, pos, ref __result); }
            catch (Exception e) { Water.Fault(e); return true; }
        }

        static bool SpreadDownwards(IWorldAccessor world, Block ourBlock, BlockPos pos, ref bool __result)
        {
            var below = world.BlockAccessor.GetBlock(Liquids.Below(pos), BlockLayersAccess.Fluid);
            if (!H1SourcesStay.WouldDrain(ourBlock, below)) return true;
            HydrologyStats.DrainsRefused++;
            // false: "nothing went down", so vanilla spreads the cell sideways as on the ground (H3); true: it stops there
            __result = !H3Flattening.SpreadsOnLowerWater;
            return false;
        }

        static void RemoveOtherLowerNeighboursPrefix(Block liquidBlock, ref BlockFacing pathFacing)
        {
            if (Water.Off) return;
            try
            {
                if (!H1SourcesStay.RemovesSameLevel(liquidBlock, pathFacing)) return;
                // vanilla removes same-level neighbours only for DOWN, and skips the neighbour on the path's side: UP is
                // neither, so only lower horizontal neighbours go
                pathFacing = BlockFacing.UP;
                HydrologyStats.RemovalsRefused++;
            }
            catch (Exception e) { Water.Fault(e); }
        }

        /// <summary>What the update's prefix hands its postfix: whether it switched the instance to sheet spreading, and the
        /// cell's fluid before the update (H7's level relax skips a cell vanilla has just lowered; H4 wakes edges around a
        /// cell that has just become a source).</summary>
        public struct UpdateState
        {
            public bool Sheet;
            public Block Before;
            /// <summary>rc.5: the update waits for Earth's answer (vanilla's did not run).</summary>
            public bool Deferred;
        }

        static bool IsWater(BlockBehaviorFiniteSpreadingLiquid beh) => beh.block?.LiquidCode is "water" or "saltwater";

        static bool BeforeUpdate(BlockBehaviorFiniteSpreadingLiquid __instance, IWorldAccessor world, BlockPos pos, out UpdateState __state)
        {
            __state = default;
            // lava and boiling water use the same behaviour: their updates are none of Hydrology's (vanilla's rivulet springs,
            // liquid code water, turn into water sources on their first update and stay Hydrology's)
            if (world.Side != EnumAppSide.Server || Water.Off || !IsWater(__instance)) return true;
            try
            {
                // rc.5: Earth's answer for this column is not ready yet: the water stays as it is (vanilla's update too) and
                // the update is tried again a second later
                if (EarthRiverQuery.Defer(pos))
                {
                    __state.Deferred = true;
                    HydrologyStats.QueryWaits++;
                    TryAgainLater(world, pos);
                    return false;
                }
                Before(__instance, world, pos, ref __state);
            }
            catch (Exception e)
            {
                if (__state.Sheet) { __instance.multiplySpread = false; __state.Sheet = false; }
                __state.Deferred = false;
                Water.Fault(e);
            }
            return true;
        }

        /// <summary>Wakes the cell again after <see cref="EarthRiverQuery.RetryMs"/>, as a neighbour change does.</summary>
        static void TryAgainLater(IWorldAccessor world, BlockPos pos)
        {
            var p = pos.Copy();
            world.RegisterCallback(_ =>
            {
                if (Water.Off) return;
                try
                {
                    var f = world.BlockAccessor.GetBlock(p, BlockLayersAccess.Fluid);
                    if (Liquids.Handles(f)) f.OnNeighbourBlockChange(world, p, p);
                }
                catch (Exception e) { Water.Fault(e); }
            }, EarthRiverQuery.RetryMs);
        }

        static void Before(BlockBehaviorFiniteSpreadingLiquid __instance, IWorldAccessor world, BlockPos pos, ref UpdateState state)
        {
            var ba = world.BlockAccessor;
            state.Before = ba.GetBlock(pos, BlockLayersAccess.Fluid);
            if (__instance.multiplySpread) return;
            if (!H4Rapids.SpreadsAsSheet(ba, pos) && !H4Rapids.SourceSpreadsAsSheet(ba, pos)) return;
            __instance.multiplySpread = true; // the server's main thread runs one liquid update at a time
            state.Sheet = true;
        }

        /// <summary>
        /// Runs after the update and its postfix whatever happened: when vanilla's update (or a hook inside it) throws, the
        /// postfix does not run, and the rapid block type the prefix switched to sheet spreading (one shared behaviour per
        /// block variant) would spread as ordinary water until the server restarts. The postfix's own reset stays first.
        /// </summary>
        static void UpdateFinalizer(BlockBehaviorFiniteSpreadingLiquid __instance, UpdateState __state)
        {
            if (__state.Sheet) __instance.multiplySpread = false;
        }

        /// <summary>H7's fall label: vanilla's label function, with water in the air labelled falling; then H4's flow labels in
        /// a rapids reach (0.1.10).</summary>
        static void LabelPostfix(IWorldAccessor world, BlockPos pos, int liquidLevel, ref int __result)
        {
            if (world.Side != EnumAppSide.Server || Water.Off || Labels.Off) return;
            int vanilla = __result;
            try { __result = Label(world, pos, liquidLevel, __result); }
            catch (Exception e) { __result = vanilla; Labels.Fault(e); }
        }

        static int Label(IWorldAccessor world, BlockPos pos, int liquidLevel, int id) =>
            H4Rapids.RiverFlowLabel(world, pos, H7Settling.Label(world, pos, id, liquidLevel), liquidLevel);

        /// <summary>H7's side-feed rule: vanilla's feed check, except that falling water in the air feeds no side.</summary>
        static bool MaxNeighbourPrefix(Block ourblock, IWorldAccessor world, BlockPos pos, out bool oneNeighbourFeedsThis, ref int __result)
        {
            oneNeighbourFeedsThis = false;
            if (world.Side != EnumAppSide.Server || Water.Off || Feed.Off || !H7Settling.NoSideFeedFromFalling || !Liquids.Handles(ourblock)) return true;
            try
            {
                __result = MaxNeighbour(ourblock, world, pos, out oneNeighbourFeedsThis);
                return false;
            }
            catch (Exception e) { oneNeighbourFeedsThis = false; Feed.Fault(e); return true; }
        }

        static int MaxNeighbour(Block ourblock, IWorldAccessor world, BlockPos pos, out bool feeds) =>
            H7Settling.MaxNeighbourLevel(ourblock, world, pos, out feeds);

        /// <summary>H7's wake rule: after vanilla relabels a cell, a cell that turned falling wakes the cells it no longer feeds.</summary>
        static void UpdateOwnFlowDirPostfix(Block block, IWorldAccessor world, BlockPos pos)
        {
            if (world.Side != EnumAppSide.Server || Water.Off || Wake.Off) return;
            try { H7Settling.AfterLabel(world, pos, block); }
            catch (Exception e) { Wake.Fault(e); }
        }

        /// <summary>
        /// A cell vanilla has just emptied: the sources beside it spread into it again on their next update. Vanilla wakes
        /// them each time it lowers the cell, but as unique callbacks; a source whose last update ran while the cell still
        /// held a little water was never woken again, and stayed beside an empty cell (seen after filling a chute's step
        /// back in under load, 2026-10-22).
        /// </summary>
        static void WakeSourcesBeside(IWorldAccessor world, BlockPos pos)
        {
            var n = new BlockPos(pos.dimension);
            foreach (var face in BlockFacing.HORIZONTALS)
            {
                n.Set(pos).Add(face);
                var f = world.BlockAccessor.GetBlock(n, BlockLayersAccess.Fluid);
                if (Liquids.Handles(f) && Liquids.IsSource(f)) f.OnNeighbourBlockChange(world, n.Copy(), pos);
            }
        }

        static void AfterUpdate(BlockBehaviorFiniteSpreadingLiquid __instance, IWorldAccessor world, BlockPos pos, UpdateState __state)
        {
            // vanilla's single stream (rapid water, lone rivulets): it lowers cells on purpose, so level relax keeps out
            bool singleStream = !__instance.multiplySpread;
            if (__state.Sheet) __instance.multiplySpread = false;
            if (__state.Deferred || world.Side != EnumAppSide.Server || Water.Off || !IsWater(__instance)) return;
            long t0 = Stopwatch.GetTimestamp();
            try { After(__instance, world, pos, __state, singleStream); }
            catch (Exception e) { Water.Fault(e); }
            finally
            {
#if DEBUG
                string key = pos.X + "," + pos.Y + "," + pos.Z;
                HydrologyStats.Hot[key] = HydrologyStats.Hot.TryGetValue(key, out int hot) ? hot + 1 : 1;
#endif
                HydrologyStats.Updates++;
                HydrologyStats.Ticks += Stopwatch.GetTimestamp() - t0;
            }
        }

        /// <summary>The rules after vanilla's update of a cell, in their order (see the class summary).</summary>
        static void After(BlockBehaviorFiniteSpreadingLiquid __instance, IWorldAccessor world, BlockPos pos, UpdateState __state, bool singleStream)
        {
            var b = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
            if (!Liquids.Handles(b))
            {
                // the cell emptied: the water above it may have lost or gained a drop
                H4Rapids.RecheckEdgesAbove(world, pos.UpCopy());
                WakeSourcesBeside(world, pos);
                return;
            }
            var before = __state.Before;
            if (Liquids.IsSource(b))
            {
                if (H7Settling.TrySurvival(__instance, world, pos, b)) return;
                // a source vanilla's own rule has just made (H2 and refill wake the edges themselves)
                if (before != null && !Liquids.IsSource(before))
                {
                    // Vanilla's bulk commit wakes only the changed cell. Its new source can now support
                    // filling above and beside it; those cells may already have exhausted their callbacks.
                    world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
                    H4Rapids.WakeEdgesOnNewSource(world, pos);
                }
                H4Rapids.CheckSource(world, pos, b);
                H4Rapids.Propagate(world, pos);
                return;
            }
            if (H3Flattening.FallsInAir(world, pos, b)) b = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
            if (H2Filling.TryFill(world, pos, b)) return;
            if (!singleStream && H7Settling.TryRelax(__instance, world, pos, b, before)) return;
            H4Rapids.FollowFeeder(world, pos);
            H4Rapids.Propagate(world, pos);
            H4Rapids.RecheckEdgesAbove(world, pos);
        }
    }
}
