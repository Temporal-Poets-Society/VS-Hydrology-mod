using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hydrology.Rules
{
    /// <summary>
    /// H1: supported source water never moves or drains (a source with nothing under it drains by H7's survival rule).
    /// Vanilla never lowers a source itself, but two spreading paths still replace one:
    /// - flowing or falling water resting on a source of the same liquid falls "into" it and turns it into falling water
    ///   (the pool cells that turned to flowing water when the 2026-10-22 test poked a river step);
    /// - rapid water flowing down removes its neighbours of the same level: for a source those are other sources, and
    ///   for the top cell of a fall the cell beside it, so a fall wider than one block keeps only one falling column.
    /// Both are refused here: water flowing down removes only lower neighbours, as water flowing sideways does.
    /// <see cref="H3Flattening"/> decides what flowing water resting on a source does instead of draining it.
    /// </summary>
    public static class H1SourcesStay
    {
        /// <summary>Vanilla would put <paramref name="ourBlock"/>'s falling water into the source <paramref name="below"/>.</summary>
        public static bool WouldDrain(Block ourBlock, Block below)
        {
            return Liquids.Handles(ourBlock) && !Liquids.IsSource(ourBlock)
                && Liquids.IsSource(below) && Liquids.Same(ourBlock, below);
        }

        /// <summary>Vanilla's RemoveOtherLowerNeighbours would also remove neighbours of the same level (water flowing down).</summary>
        public static bool RemovesSameLevel(Block liquidBlock, BlockFacing pathFacing)
        {
            return pathFacing == BlockFacing.DOWN && Liquids.Handles(liquidBlock);
        }
    }
}
