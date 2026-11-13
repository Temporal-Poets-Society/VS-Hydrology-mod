using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// H3: a 1-block drop is flattened, and flowing water has no standing faces in the air.
    ///
    /// Water leaving a pool onto lower water one block down settles into a ramp of flowing water, heights 6 to 1, on the
    /// lower water, and the lower pool keeps its sources (H1). No vertical face. Vanilla treats flowing water resting on
    /// a same-liquid source as "spreading down" even when nothing can go down, so it never spreads sideways there: it
    /// either drains the source below or stops as a single cell. With <see cref="SpreadsOnLowerWater"/> such water carries
    /// on sideways exactly as it does on solid ground, which draws the ramp; falling water landing in a pool does the same
    /// (the ramp below a fall, H4). H2's ground rule never fills such a ramp (it needs solid ground); its refill, vessels and
    /// pocket cases fill on water only where the ramp is not a river step's (refill: 3 source sides; vessels: never in
    /// natural river water; pockets: a step's ramp is never enclosed).
    ///
    /// Vertical faces belong to falling water only (<see cref="FallsInAir"/>, which has no switch of its own; the config's
    /// Flatten switches only the sideways spreading).
    /// </summary>
    public static class H3Flattening
    {
        public static bool SpreadsOnLowerWater = true;

        /// <summary>
        /// Water standing in the air is falling water. When a non-source cell updates, the run of non-source water of its
        /// liquid in its column (up and down to <see cref="H4Rapids.MaxDropScan"/>) is checked: every still (directionless)
        /// cell with water above it and air or flowing water of its liquid below it becomes the falling variant of its level
        /// and kind. The whole run at once: vanilla labels a cell still when it has no sideways flow and neither neighbour
        /// above or below is falling, so a cell fixed alone was relabelled still from its still neighbours. Cells with a flow
        /// direction are left to vanilla, and so is a cell resting on the ground or a pool: vanilla labels it falling itself
        /// once the water above it falls, and relabelled it still at once otherwise (an update loop in Earth's rock tubes).
        ///
        /// Seen at Victoria Falls with Hydrology 0.1.2 (2026-10-22): stones put into the curtain and taken out again left
        /// columns of <c>rapidwater-still-6</c> standing in the air, each cell held up by the water under it.
        /// </summary>
        public static bool FallsInAir(IWorldAccessor world, BlockPos pos, Block b)
        {
            if (!Liquids.Handles(b) || Liquids.IsSource(b)) return false;
            var ba = world.BlockAccessor;
            var p = pos.Copy();
            int top = pos.Y, bottom = pos.Y;
            for (int i = 0; i < H4Rapids.MaxDropScan; i++)
            {
                var f = Liquids.Fluid(ba, p.Set(pos.X, top + 1, pos.Z));
                if (!Liquids.Same(f, b) || Liquids.IsSource(f)) break;
                top++;
            }
            for (int i = 0; i < H4Rapids.MaxDropScan; i++)
            {
                var f = Liquids.Fluid(ba, p.Set(pos.X, bottom - 1, pos.Z));
                if (!Liquids.Same(f, b) || Liquids.IsSource(f)) break;
                bottom--;
            }
            int changed = 0;
            var q = pos.Copy();
            for (int y = top; y >= bottom; y--)
            {
                p.Set(pos.X, y, pos.Z);
                var c = Liquids.Fluid(ba, p);
                if (c.Variant["flow"] != "still") continue;
                // water in a block with its own floor (a bottom slab, stairs) is held up, as H7's fall label and vanilla
                // read it (rc.4, audit: a pool floored with slabs over air flipped between still and falling for ever)
                if (H7Settling.FloorInCell(ba, p)) continue;
                if (!Liquids.Same(Liquids.Fluid(ba, q.Set(pos.X, y + 1, pos.Z)), c)) continue;
                q.Set(pos.X, y - 1, pos.Z);
                if (!Liquids.Open(ba, q)) continue;
                var under = Liquids.Fluid(ba, q);
                if (under.IsLiquid() && (Liquids.IsSource(under) || !Liquids.Same(under, c))) continue;
                var falling = world.GetBlock(c.CodeWithParts("d", c.LiquidLevel.ToString()));
                if (falling == null || falling.Id == 0) continue;
                ba.SetBlock(falling.Id, p, BlockLayersAccess.Fluid);
                changed++;
            }
            if (changed == 0) return false;
            // the bottom of the run spreads on (down into air, or sideways on what it rests on)
            p.Set(pos.X, bottom, pos.Z);
            Liquids.Fluid(ba, p).OnNeighbourBlockChange(world, p.Copy(), pos);
            HydrologyStats.MadeFalling += changed;
            return true;
        }
    }
}
