using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hydrology
{
    /// <summary>
    /// Block facts the rules share: which liquids Hydrology handles (vanilla's water, salt water and rapid water), sources,
    /// open cells, solid ground, and the ordinary/rapid counterpart of each fresh-water block.
    /// </summary>
    public static class Liquids
    {
        public const int SourceLevel = 7;

        /// <summary>water-still-7, saltwater-still-7, rapidwater-still-7</summary>
        public static Block WaterSource, SaltSource, RapidSource;

        // indexed by block id: the same flow and height in the other kind of fresh water, 0 if none
        static int[] toRapid = new int[0], toWater = new int[0];
        // indexed by block id: the falling ("d") variant of the same liquid and height, 0 if none
        static int[] toFalling = new int[0];
        // indexed by block id: a fresh-water source's flow label (1..8 for n, ne, e, se, s, sw, w, nw; 0 still, falling or
        // not a fresh source); per flow label, the ordinary and the rapid source with it (0.1.10)
        static byte[] flowOf = new byte[0];
        // HY-0017 (finding 9): indexed by block id, a handled liquid's flow kind: 1 still, 2 falling ("d"), 3 directional;
        // replaces Variant["flow"] (a string lookup) in H3's and H7's per-cell loops
        static byte[] kindOf = new byte[0];
        // Fable's pass (F4): indexed by block id, 1 not handled, 2 handled salt water, 3 handled fresh water (ordinary or
        // rapid); Handles read the code's first part (a string split) about 15-25 times per liquid update and up to 765 times
        // per wake scan. With R5 the rules ask Liquids every fact about a block (handled, fresh, salt, rapid, still, falling,
        // flow label); no Variant, code part or liquid code test is left in them (C1, client side, reads its own code)
        static byte[] handledOf = new byte[0];
        static readonly int[] waterSourceByFlow = new int[9], rapidSourceByFlow = new int[9];
        static readonly string[] Flows = { "n", "ne", "e", "se", "s", "sw", "w", "nw" };

        public static void Init(IWorldAccessor world)
        {
            WaterSource = world.GetBlock(new AssetLocation("game:water-still-7"));
            SaltSource = world.GetBlock(new AssetLocation("game:saltwater-still-7"));
            RapidSource = world.GetBlock(new AssetLocation("game:rapidwater-still-7"));
            int max = 0;
            foreach (var b in world.Blocks) if (b != null && b.Id > max) max = b.Id;
            toRapid = new int[max + 1];
            toWater = new int[max + 1];
            toFalling = new int[max + 1];
            flowOf = new byte[max + 1];
            kindOf = new byte[max + 1];
            var handled = new byte[max + 1];
            foreach (var b in world.Blocks)
                if (b != null && b.Id > 0) handled[b.Id] = (byte)(!HandlesByCode(b) ? 1 : b.LiquidCode == "water" ? 3 : 2);
            handledOf = handled;
            // a world in the same process may lack a variant the previous one had
            System.Array.Clear(waterSourceByFlow);
            System.Array.Clear(rapidSourceByFlow);
            foreach (var b in world.Blocks)
            {
                if (b == null || !Handles(b)) continue;
                string flow = b.Variant["flow"];
                kindOf[b.Id] = (byte)(flow == "still" ? 1 : flow == "d" ? 2 : flow == null ? 0 : 3);
                if (b.Variant["height"] == null) continue;
                var d = world.GetBlock(b.CodeWithParts("d", b.Variant["height"]));
                if (d != null && d.Id > 0) toFalling[b.Id] = d.Id;
            }
            foreach (var b in world.Blocks)
            {
                if (b == null || !IsFresh(b) || b.Variant["height"] != "7") continue;
                int f = System.Array.IndexOf(Flows, b.Variant["flow"]) + 1;
                if (f <= 0) continue;
                flowOf[b.Id] = (byte)f;
                (b.FirstCodePart() == "rapidwater" ? rapidSourceByFlow : waterSourceByFlow)[f] = b.Id;
            }
            foreach (var b in world.Blocks)
            {
                if (b?.Code == null || b.Code.Domain != "game" || b.FirstCodePart() != "water") continue;
                var rapid = world.GetBlock(new AssetLocation("game", "rapid" + b.Code.Path));
                if (rapid == null || rapid.Id == 0) continue;
                toRapid[b.Id] = rapid.Id;
                toWater[rapid.Id] = b.Id;
            }
        }

        /// <summary>Vanilla water, salt water or rapid water (rapid water's liquid code is "water").</summary>
        public static bool Handles(Block b)
        {
            if (b == null || b.Id == 0) return false;
            var t = handledOf;
            return b.Id < t.Length && t[b.Id] != 0 ? t[b.Id] >= 2 : HandlesByCode(b);
        }

        /// <summary>The test itself, by the block's code (before <see cref="Init"/>, and to build its table).</summary>
        static bool HandlesByCode(Block b)
        {
            if (b == null || b.Id == 0 || b.Code?.Domain != "game" || !b.IsLiquid()) return false;
            string first = b.FirstCodePart();
            return first == "water" || first == "saltwater" || first == "rapidwater";
        }

        /// <summary>A cell water of <paramref name="liquid"/> may stand in: open, and empty or flowing water of that liquid
        /// (Fable's pass, R3: shared by a drain's step and a river pool's plan and step).</summary>
        public static bool CanTake(IBlockAccessor ba, BlockPos p, Block liquid)
        {
            if (!Open(ba, p)) return false;
            var f = Fluid(ba, p);
            return f.Id == 0 || (Same(f, liquid) && !IsSource(f));
        }

        /// <summary>Fresh water, ordinary or rapid. Only fresh water makes rapids: vanilla has no salt rapid water.</summary>
        public static bool IsFresh(Block b)
        {
            if (b == null || b.Id == 0) return false;
            var t = handledOf;
            return b.Id < t.Length && t[b.Id] != 0 ? t[b.Id] == 3 : HandlesByCode(b) && b.LiquidCode == "water";
        }

        /// <summary>Vanilla salt water (Fable's pass, R5).</summary>
        public static bool IsSalt(Block b)
        {
            if (b == null || b.Id == 0) return false;
            var t = handledOf;
            return b.Id < t.Length && t[b.Id] != 0 ? t[b.Id] == 2 : HandlesByCode(b) && b.LiquidCode == "saltwater";
        }

        /// <summary>The still source of a handled liquid's kind: salt water's for salt water, ordinary water's otherwise.</summary>
        public static Block SourceOf(Block b) => IsSalt(b) ? SaltSource : WaterSource;

        public static bool IsRapid(Block b) => b.Id < toWater.Length && toWater[b.Id] != 0;

        /// <summary>The falling ("d") variant of a handled liquid block id, of the same liquid and height; 0 if none.</summary>
        public static int FallingOf(int id) => id > 0 && id < toFalling.Length ? toFalling[id] : 0;

        public static bool IsSource(Block b) => b.LiquidLevel == SourceLevel;

        /// <summary>A handled liquid labelled still (its flow variant "still").</summary>
        public static bool IsStill(Block b) => b.Id < kindOf.Length && kindOf[b.Id] == 1;

        /// <summary>A handled liquid labelled falling (its flow variant "d").</summary>
        public static bool IsFalling(Block b) => b.Id < kindOf.Length && kindOf[b.Id] == 2;

        /// <summary>A fresh-water source's flow label, 0..7 for n, ne, e, se, s, sw, w, nw (rapidwater-s-7: 4); -1 for still,
        /// falling or not a fresh source (0.1.10).</summary>
        public static int FlowLabel(Block b) => (b.Id < flowOf.Length ? flowOf[b.Id] : 0) - 1;

        /// <summary>The fresh-water source of the kind of <paramref name="kind"/> (ordinary or rapid) labelled with flow
        /// <paramref name="flow"/> (0..7, n to nw); <paramref name="kind"/>'s own id if there is none.</summary>
        public static int SourceWithFlow(Block kind, int flow)
        {
            int id = flow < 0 || flow > 7 ? 0 : (IsRapid(kind) ? rapidSourceByFlow : waterSourceByFlow)[flow + 1];
            return id != 0 ? id : kind.Id;
        }

        public static bool Same(Block a, Block b) => a.IsLiquid() && b.IsLiquid() && a.LiquidCode == b.LiquidCode;

        /// <summary>The same flow and height as ordinary water (rapid = false) or rapid water (rapid = true); the block itself
        /// when it already is that kind or has no counterpart.</summary>
        public static int Counterpart(Block b, bool rapid)
        {
            if (b.Id >= toRapid.Length) return b.Id;
            int id = rapid ? toRapid[b.Id] : toWater[b.Id];
            return id != 0 ? id : b.Id;
        }

        public static Block Fluid(IBlockAccessor ba, BlockPos p) => ba.GetBlock(p, BlockLayersAccess.Fluid);

        // Scratch positions for the hot paths (labels, feed checks, survival, fills): reused instead of a new BlockPos per
        // call, one set per thread. Each rule takes the ones it names; none holds one across a call that takes the same
        // (GroundBelow keeps its own). Set(p) copies the coordinates only, so the dimension is copied too (Codex review, 0.1.7).
        [System.ThreadStatic] static BlockPos below, above, side, side2, ground;

        /// <summary>The cell under <paramref name="p"/>, in a reused position.</summary>
        public static BlockPos Below(BlockPos p) { var s = below ??= new BlockPos(0); s.Set(p); s.dimension = p.dimension; return s.Down(); }
        /// <summary>The cell above <paramref name="p"/>, in a reused position.</summary>
        public static BlockPos Above(BlockPos p) { var s = above ??= new BlockPos(0); s.Set(p); s.dimension = p.dimension; return s.Up(); }
        /// <summary>A reused position for a loop over a cell's sides.</summary>
        public static BlockPos Side(BlockPos p) { var s = side ??= new BlockPos(0); s.Set(p); s.dimension = p.dimension; return s; }
        /// <summary>A second reused position for a loop over sides (H2's refill and vessels cases).</summary>
        public static BlockPos Side2(BlockPos p) { var s = side2 ??= new BlockPos(0); s.Set(p); s.dimension = p.dimension; return s; }

        /// <summary>
        /// The block that holds water in or out at <paramref name="p"/>, as vanilla reads floors (GetMostSolidBlock): lake ice
        /// in the fluid layer, else the solid blocks layer. Where the fluid layer holds a liquid this is the solid blocks
        /// layer. 0.6.2: ice used to read as air, so a source on a frozen pond drained (H7) and water spreading over ice was
        /// labelled falling and lowered and spread again for ever.
        /// </summary>
        public static Block Solid(IBlockAccessor ba, BlockPos p) => ba.GetBlock(p, BlockLayersAccess.MostSolid);

        /// <summary>
        /// A cell liquid can be in: no solid block, one liquids replace (land plants), as vanilla's downward paths test, or a
        /// plant block liquid passes through up and down that already holds liquid (a water plant: seaweed, reeds; 0.1.5,
        /// replaceable 3000, below vanilla's threshold). Vanilla's world generation puts water plants into Earth's pools
        /// after the water; read as solid, a plant under a ramp hid the drop beyond it from H4's window and turned a chute
        /// ordinary (Earth 0.16.10 at the Congo, server test with seaweed under a chute's ramps).
        /// </summary>
        public static bool Open(IBlockAccessor ba, BlockPos p)
        {
            var s = Solid(ba, p);
            if (s.Id == 0 || s.Replaceable >= BlockBehaviorFiniteSpreadingLiquid.ReplacableThreshold) return true;
            // only plants: other blocks that hold water (clutter, chiseled blocks) stay solid, as vanilla treats them (a
            // rivulet over waterlogged clutter looped when they counted as open, server test 0.1.5)
            return s.BlockMaterial == EnumBlockMaterial.Plant && Fluid(ba, p).IsLiquid()
                && s.GetLiquidBarrierHeightOnSide(BlockFacing.UP, p) < 1f
                && s.GetLiquidBarrierHeightOnSide(BlockFacing.DOWN, p) < 1f;
        }

        /// <summary>The chunk holding <paramref name="p"/> is loaded (an unloaded chunk reads as air).</summary>
        public static bool Loaded(IBlockAccessor ba, BlockPos p) => ba.GetChunkAtBlockPos(p) != null;

        /// <summary>The block under <paramref name="p"/> holds liquid up (solid ground).</summary>
        public static bool GroundBelow(IBlockAccessor ba, BlockPos p)
        {
            var below = ground ??= new BlockPos(0);
            below.Set(p);
            below.dimension = p.dimension;
            below.Down();
            return Solid(ba, below).GetLiquidBarrierHeightOnSide(BlockFacing.UP, below) >= 1f;
        }

        /// <summary>Liquid can pass from <paramref name="p"/> to its neighbour on <paramref name="face"/> (no wall on either side).</summary>
        public static bool Passes(IBlockAccessor ba, BlockPos p, BlockPos n, BlockFacing face) => Passes(ba, Solid(ba, p), p, n, face);

        /// <summary><see cref="Passes(IBlockAccessor, BlockPos, BlockPos, BlockFacing)"/> with <paramref name="p"/>'s solid
        /// block read once by the caller for all its faces (Fable's pass, F13: a third fewer reads in the largest searches).</summary>
        public static bool Passes(IBlockAccessor ba, Block pSolid, BlockPos p, BlockPos n, BlockFacing face)
        {
            return pSolid.GetLiquidBarrierHeightOnSide(face, p) < 1f
                && Solid(ba, n).GetLiquidBarrierHeightOnSide(face.Opposite, n) < 1f;
        }

        /// <summary>
        /// Writes a fluid block and wakes it, and unless <paramref name="neighbours"/> is false its neighbours, the vanilla
        /// way (a delayed update each), so a changed cell spreads on by itself. It lowers a source only for H7's survival
        /// rule (a source with nothing under it); H1 keeps every other source.
        /// </summary>
        public static void SetAndWake(IWorldAccessor world, BlockPos pos, int blockId, bool neighbours = true)
        {
            pos = pos.Copy(); // callers reuse their positions; the scheduled update keeps this one
            world.BlockAccessor.SetBlock(blockId, pos, BlockLayersAccess.Fluid);
            world.GetBlock(blockId).OnNeighbourBlockChange(world, pos, pos);
            if (neighbours) world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        }
    }
}
