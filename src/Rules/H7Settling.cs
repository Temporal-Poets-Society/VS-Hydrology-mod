using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hydrology.Rules
{
    /// <summary>
    /// H7: settling rules from the water rules study (docs/water-rules-study-2026-10-23.md). Local and event-driven: each
    /// runs inside a cell's own update, or inside one of vanilla's functions it patches, and reads at most the cell's
    /// neighbours. Together they keep water from standing in the air after blocks are put into and taken out of a fall
    /// (the Victoria Falls reports), and make the shape water settles to independent of the order of updates.
    /// - Survival: a source stays a source only while something holds it from below.
    /// - Fall label: water resting on neither a block nor a source is labelled falling, in vanilla's own label function.
    /// - No side feed from falling water: falling water still in the air feeds only the cell below it.
    /// - Wake on the fall label: a cell that turns falling wakes the lower flowing cells beside it, which it no longer feeds.
    /// - Level relax: a flowing cell lower than its feeders allow is raised.
    /// </summary>
    public static class H7Settling
    {
        public static bool Survival = true, FallLabel = true, NoSideFeedFromFalling = true, WakeOnFallLabel = true, LevelRelax = true;

        static readonly BlockFacing[] H = BlockFacing.HORIZONTALS;

        /// <summary>Something holds the cell up: a block that is not open (not air, not a replaceable plant), or a source of
        /// the same liquid.</summary>
        static bool Supported(IBlockAccessor ba, BlockPos pos, Block b)
        {
            if (FloorInCell(ba, pos)) return true;
            var below = Liquids.Below(pos);
            if (!Liquids.Open(ba, below)) return true;
            var f = Liquids.Fluid(ba, below);
            return Liquids.IsSource(f) && Liquids.Same(f, b);
        }

        /// <summary>
        /// Survival: a source with nothing holding it from below (air, or flowing water) turns into flowing water of height 6
        /// and drains the vanilla way. A source H2 made on a floor that is then taken away no longer feeds a column of water
        /// in the air for good (the Victoria columns), and no source is left hanging.
        /// </summary>
        public static bool TrySurvival(BlockBehaviorFiniteSpreadingLiquid beh, IWorldAccessor world, BlockPos pos, Block b)
        {
            if (!Survival || !Liquids.Handles(b) || !Liquids.IsSource(b)) return false;
            if (Supported(world.BlockAccessor, pos, b)) return false;
            int id = beh.GetLiquidBlockId(world, pos, b, 6);
            if (id == 0) return false;
            Liquids.SetAndWake(world, pos, id);
            HydrologyStats.SourcesDrained++;
            return true;
        }

        /// <summary>The cell's own block closes its bottom face (water in a waterlogged block with a solid floor, such as a
        /// chiseled block): vanilla counts that as holding the water up.</summary>
        internal static bool FloorInCell(IBlockAccessor ba, BlockPos pos) =>
            Liquids.Solid(ba, pos).GetLiquidBarrierHeightOnSide(BlockFacing.DOWN, pos) >= 1f;

        /// <summary>Falling water still in the air: labelled falling, not a source, and nothing holding it from below.</summary>
        public static bool FallingInAir(IBlockAccessor ba, BlockPos pos, Block f)
        {
            if (!Liquids.Handles(f) || Liquids.IsSource(f) || f.Variant["flow"] != "d") return false;
            if (FloorInCell(ba, pos)) return false;
            var below = Liquids.Below(pos);
            if (!Liquids.Open(ba, below)) return false;
            var u = Liquids.Fluid(ba, below);
            return !(u.IsLiquid() && Liquids.IsSource(u));
        }

        /// <summary>
        /// Fall label (postfix of GetLiquidBlockId): non-source water that rests on neither a block nor a source is falling
        /// water, so a column of water in the air is always a falling column, whatever its neighbours' heights.
        /// </summary>
        public static int Label(IWorldAccessor world, BlockPos pos, int id, int level)
        {
            if (!FallLabel || id == 0 || level >= Liquids.SourceLevel) return id;
            int fall = Liquids.FallingOf(id);
            if (fall == 0 || fall == id) return id;
            var ba = world.BlockAccessor;
            if (FloorInCell(ba, pos)) return id;
            var below = Liquids.Below(pos);
            if (!Liquids.Open(ba, below)) return id;
            var u = Liquids.Fluid(ba, below);
            if (u.IsLiquid() && Liquids.IsSource(u)) return id;
            HydrologyStats.Labelled++;
            return fall;
        }

        /// <summary>
        /// GetMaxNeighbourLiquidLevel with the side-feed rule: as vanilla (water above feeds fully; else the highest
        /// neighbour of the liquid that no wall separates, and whether one is exactly one higher), except that a neighbour
        /// that is falling water still in the air does not count. Written from vanilla's behaviour.
        /// </summary>
        public static int MaxNeighbourLevel(Block ourblock, IWorldAccessor world, BlockPos pos, out bool feeds)
        {
            var ba = world.BlockAccessor;
            var solid = Liquids.Solid(ba, pos);
            var up = Liquids.Above(pos);
            if (Liquids.Fluid(ba, up).LiquidCode == ourblock.LiquidCode
                && solid.GetLiquidBarrierHeightOnSide(BlockFacing.UP, pos) == 0f
                && Liquids.Solid(ba, up).GetLiquidBarrierHeightOnSide(BlockFacing.DOWN, up) == 0f)
            {
                feeds = true;
                return Liquids.SourceLevel;
            }
            int max = 0, level = ourblock.LiquidLevel;
            feeds = false;
            var n = Liquids.Side(pos);
            for (int i = 0; i < 4; i++)
            {
                n.Set(pos).Add(H[i]);
                var f = Liquids.Fluid(ba, n);
                if (f.LiquidCode != ourblock.LiquidCode) continue;
                if (NoSideFeedFromFalling && FallingInAir(ba, n, f)) continue;
                int l = f.LiquidLevel;
                if (l == level + 1) feeds = true;
                if (solid.GetLiquidBarrierHeightOnSide(H[i], pos) >= l / 7f) continue;
                if (Liquids.Solid(ba, n).GetLiquidBarrierHeightOnSide(H[i].Opposite, n) >= l / 7f) continue;
                max = Math.Max(max, l);
            }
            return max;
        }

        /// <summary>
        /// Wake on the fall label (postfix of vanilla's updateOwnFlowDir): a cell whose label has just turned falling no
        /// longer feeds its sides, so the lower flowing cells beside it are woken to re-check. Vanilla writes labels
        /// without waking anyone; a stray column beside a curtain was otherwise never re-checked.
        /// </summary>
        public static void AfterLabel(IWorldAccessor world, BlockPos pos, Block before)
        {
            if (!WakeOnFallLabel || !NoSideFeedFromFalling) return;
            var ba = world.BlockAccessor;
            var now = Liquids.Fluid(ba, pos);
            if (now.Id == before.Id || !Liquids.Handles(now) || now.Variant["flow"] != "d" || before.Variant["flow"] == "d") return;
            var n = new BlockPos(pos.dimension);
            for (int i = 0; i < 4; i++)
            {
                n.Set(pos).Add(H[i]);
                var f = Liquids.Fluid(ba, n);
                if (!Liquids.Same(f, now) || Liquids.IsSource(f) || f.LiquidLevel >= now.LiquidLevel) continue;
                f.OnNeighbourBlockChange(world, n.Copy(), pos);
            }
        }

        /// <summary>
        /// Level relax: after its update, a flowing cell lower than its feeders allow (6 under water of its liquid, else one
        /// less than its highest neighbour) is raised to that, unless vanilla has just lowered it. Ramps then settle to the
        /// same heights whatever the order of updates was. Not after an update in vanilla's single-stream mode (rapid water
        /// that is no sheet, such as a lone rivulet): that mode lowers cells on purpose, and raising them again looped on
        /// vanilla worlds' rivulets (server test 0.1.5).
        /// </summary>
        public static bool TryRelax(BlockBehaviorFiniteSpreadingLiquid beh, IWorldAccessor world, BlockPos pos, Block b, Block before)
        {
            if (!LevelRelax || !Liquids.Handles(b) || Liquids.IsSource(b)) return false;
            if (before != null && Liquids.Same(before, b) && before.LiquidLevel > b.LiquidLevel) return false;
            // the same feeders vanilla's lowering counts (water above feeds fully: 7), so the two never disagree
            int target = Math.Min(6, MaxNeighbourLevel(b, world, pos, out _) - 1);
            if (b.LiquidLevel >= target) return false;
            int id = beh.GetLiquidBlockId(world, pos, b, target);
            if (id == 0 || id == b.Id) return false;
            Liquids.SetAndWake(world, pos, id);
            HydrologyStats.Relaxed++;
            return true;
        }
    }
}
