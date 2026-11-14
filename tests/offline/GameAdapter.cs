// Minimal API facts for compiling the actual H2 rule (H2Filling, H2Held, H2Release and its parts, H2DamPools) and, since
// HY-0019, the actual H4Rapids and H7Settling without game DLLs. The surrounding engine is watersim's calibrated port, not
// the engine itself. A block is the sim's fluid code (kind, flow, level); a solid cell is a full liquid barrier. No Harmony,
// chunk packing or Earth binding is emulated here.
using System.Linq;
using WaterSim;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.MathTools
{
    public sealed class Vec3i { public int X, Y, Z; public Vec3i(int x, int y, int z) { X = x; Y = y; Z = z; } }
    public sealed class BlockFacing
    {
        public int X, Y, Z;
        public Vec3i Normali => new(X, Y, Z);
        public BlockFacing Opposite => X == 1 ? HORIZONTALS[3] : X == -1 ? HORIZONTALS[1] : Z == 1 ? HORIZONTALS[0] : Z == -1 ? HORIZONTALS[2] : Y == 1 ? DOWN : UP;
        BlockFacing(int x, int y, int z) { X = x; Y = y; Z = z; }
        public static readonly BlockFacing[] HORIZONTALS = { new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0) };
        public static readonly BlockFacing UP = new(0, 1, 0);
        public static readonly BlockFacing DOWN = new(0, -1, 0);
        public static readonly BlockFacing[] ALLFACES = { HORIZONTALS[0], HORIZONTALS[1], HORIZONTALS[2], HORIZONTALS[3], UP, DOWN };
    }
    public sealed class BlockPos
    {
        public int X, Y, Z, dimension;
        public BlockPos(int dim) { dimension = dim; }
        public BlockPos(int x, int y, int z, int dim = 0) { X = x; Y = y; Z = z; dimension = dim; }
        public BlockPos Set(BlockPos p) { X = p.X; Y = p.Y; Z = p.Z; return this; }
        public BlockPos Set(int x, int y, int z) { X = x; Y = y; Z = z; return this; }
        public BlockPos Add(BlockFacing f) { X += f.X; Y += f.Y; Z += f.Z; return this; }
        public BlockPos AddCopy(BlockFacing f) => Copy().Add(f);
        public BlockPos Copy() => new(X, Y, Z, dimension);
        public BlockPos Down() { Y--; return this; }
        public BlockPos DownCopy() => Copy().Down();
        public BlockPos Up() { Y++; return this; }
        public BlockPos UpCopy() => Copy().Up();
    }
}
namespace Vintagestory.API.Common
{
    public sealed class Block
    {
        public int Id;
        /// <summary>The solid layer's block of a cell (Liquids.Solid): a full liquid barrier when the cell is solid.</summary>
        public float Barrier;
        public int LiquidLevel => F.Level(Id);
        public string LiquidCode => Id == 0 ? null : F.Kind(Id) == F.Salt ? "saltwater" : "water";
        public bool IsLiquid() => Id != 0;
        public float GetLiquidBarrierHeightOnSide(Vintagestory.API.MathTools.BlockFacing face, BlockPos pos) => Barrier;
        public void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neib) => ((Issue20Tests.GameWorld)world).Sim.Wake(pos.X, pos.Y, pos.Z);
    }
    public interface IBlockAccessor { Block Fluid(BlockPos p); bool Solid(BlockPos p); object GetChunkAtBlockPos(BlockPos p); }
    public interface IWorldAccessor { IBlockAccessor BlockAccessor { get; } Config Config { get; } long ElapsedMilliseconds { get; } Block[] Blocks { get; } }
}
namespace Vintagestory.API.Config
{
    public static class GlobalConstants { public const int ChunkSize = 32; }
}
namespace Vintagestory.GameContent
{
    /// <summary>Vanilla's liquid behaviour, as far as H7 calls it: the block of the same liquid and flow at a level.</summary>
    public class BlockBehaviorFiniteSpreadingLiquid
    {
        public int GetLiquidBlockId(Vintagestory.API.Common.IWorldAccessor world, BlockPos pos, Vintagestory.API.Common.Block b, int level) =>
            level < 1 ? 0 : WaterSim.F.Make(WaterSim.F.Kind(b.Id), WaterSim.F.Flow(b.Id), level);
    }
}
namespace Vintagestory.API.Common
{
    public sealed class Config
    {
        public bool Vessels = true, VesselsWithQuery;
        public bool? Flooding;
        public bool HasAttribute(string key) => key == "hydrologyFlooding" && Flooding.HasValue;
        public void SetBool(string key, bool value) { if (key == "hydrologyFlooding") Flooding = value; }
        public bool GetAsBool(string key, bool fallback) => key switch
        {
            "hydrologyFlooding" => Flooding ?? fallback,
            "hydrologyFillVessels" => Vessels,
            "hydrologyFillVesselsWithRiverQuery" => VesselsWithQuery,
            _ => fallback
        };
    }
}
namespace Hydrology
{
    public static class EarthRiverQuery
    {
        public static bool Active;
        public static System.Func<BlockPos, bool> IsRiver = _ => false;
        /// <summary>Earth's span: the column's generated water surface (null: no span, any height is river, as before
        /// Earth 2.0.0-rc.2).</summary>
        public static System.Func<BlockPos, int> Surface;
        public static bool River(BlockPos p) => Active && p.dimension == 0 && IsRiver(p) && (Surface == null || p.Y <= Surface(p) + 2);
        public static bool RiverColumn(BlockPos p) => Active && p.dimension == 0 && IsRiver(p);
        public static double Discharge = 1200;
        public static bool At(BlockPos p, out bool rapids, out double slope, out double discharge)
        {
            rapids = false; slope = 0; discharge = Discharge;
            return River(p);
        }
        public static bool RiverWater(BlockPos p) => Active && p.dimension == 0 && IsRiver(p) && (Surface == null || p.Y <= Surface(p));
        // HY-0019: what H4 reads
        public static int Version = 3;
        public static System.Func<BlockPos, bool> IsRapids = _ => false;
        public static bool Rapids(BlockPos p, bool source = false) => River(p) && IsRapids(p);
        public static int LabelledFlow(BlockPos p) => -1;
        public static bool NotReady(BlockPos p) => false;
        // Fable's pass (R2): the driver asks whether the query was unbound at run time
        public static bool UnboundPending;
        public static bool TakeUnbound() { bool u = UnboundPending; UnboundPending = false; return u; }
    }
    public static class HydrologyStats
    {
        public static long Filled, Refilled, FilledVessels, VesselsRefused, VesselsInRiverRefused, FilledPockets, Unheld, Unsupplied, FedChannel, DamsWoken, DrainSourcesRefused,
            Released, ReleasedCells, ReleaseAbsorbed, ReleaseKept, DamLayers, DamSources,
            SourcesDrained, Relaxed, RapidSourcesReverted, RapidSourcesByQuery, RapidSources, MadeRapid, MadeOrdinary, LabelsKeptWaiting,
            Labelled, FlowLabelsKept, EdgeWakes;
    }
    public static class Liquids
    {
        public static readonly Block WaterSource = new() { Id = F.Make(F.Water, F.Still, 7) };
        public static readonly Block SaltSource = new() { Id = F.Make(F.Salt, F.Still, 7) };
        public static readonly Block RapidSource = new() { Id = F.Make(F.Rapid, F.Still, 7) };
        public const int SourceLevel = 7;
        public static bool IsSource(Block b) => F.IsSource(b.Id);
        // HY-0019: what H4 and H7 read
        public static bool IsRapid(Block b) => F.IsRapid(b.Id);
        public static bool IsFresh(Block b) => F.IsFresh(b.Id);
        public static int Counterpart(Block b, bool rapid) => b.Id == 0 || F.Kind(b.Id) == F.Salt ? b.Id : F.WithKind(b.Id, rapid ? F.Rapid : F.Water);
        public static int FallingOf(int id) => id == 0 ? 0 : F.WithFlow(id, F.D);
        public static int FlowLabel(Block b) => F.IsFresh(b.Id) && F.IsSource(b.Id) && F.Flow(b.Id) >= F.N && F.Flow(b.Id) <= F.NW ? F.Flow(b.Id) - 1 : -1;
        public static int SourceWithFlow(Block kind, int flow) => flow < 0 || flow > 7 ? kind.Id : F.Make(F.Kind(kind.Id), flow + 1, 7);
        public static bool IsStill(Block b) => b.Id != 0 && F.Flow(b.Id) == F.Still;
        public static bool IsFalling(Block b) => b.Id != 0 && F.Flow(b.Id) == F.D;
        static readonly Block solidCell = new() { Barrier = 1f }, openCell = new() { Barrier = 0f };
        public static Block Solid(IBlockAccessor ba, BlockPos p) => ba.Solid(p) ? solidCell : openCell;
        public static BlockPos Above(BlockPos p) => p.UpCopy();
        public static BlockPos Side(BlockPos p) => p.Copy();
        public static bool Handles(Block b) => b.Id != 0;
        public static bool Same(Block a, Block b) => F.Same(a.Id, b.Id);
        // Fable's pass (R3, R5)
        public static bool IsSalt(Block b) => b.Id != 0 && F.Kind(b.Id) == F.Salt;
        public static Block SourceOf(Block b) => IsSalt(b) ? SaltSource : WaterSource;
        public static bool CanTake(IBlockAccessor ba, BlockPos p, Block liquid)
        {
            if (!Open(ba, p)) return false;
            var f = Fluid(ba, p);
            return f.Id == 0 || (Same(f, liquid) && !IsSource(f));
        }
        public static Block Fluid(IBlockAccessor ba, BlockPos p) => ba.Fluid(p);
        public static bool GroundBelow(IBlockAccessor ba, BlockPos p) => ba.Solid(p.DownCopy());
        public static bool Open(IBlockAccessor ba, BlockPos p) => !ba.Solid(p);
        public static bool Loaded(IBlockAccessor ba, BlockPos p) => !(ba is Issue20Tests.GameWorld g && g.Unloaded != null && g.Unloaded(p));
        public static bool Passes(IBlockAccessor ba, BlockPos p, BlockPos n, BlockFacing face) => !ba.Solid(p) && !ba.Solid(n);
        public static bool Passes(IBlockAccessor ba, Block pSolid, BlockPos p, BlockPos n, BlockFacing face) => pSolid.Barrier < 1f && !ba.Solid(n);
        public static BlockPos Below(BlockPos p) => p.DownCopy();
        public static BlockPos Side2(BlockPos p) => p.Copy();
        public static void SetAndWake(IWorldAccessor world, BlockPos p, int id, bool neighbours = true)
        {
            var sim = ((Issue20Tests.GameWorld)world).Sim;
            sim.SetDirect(p.X, p.Y, p.Z, id);
            sim.Wake(p.X, p.Y, p.Z);
            if (neighbours) sim.QueueNeighbourUpdate(p.X, p.Y, p.Z);
        }
    }
}
namespace Issue20Tests
{
    public sealed class GameWorld : IWorldAccessor, IBlockAccessor
    {
        public Sim Sim;
        public IBlockAccessor BlockAccessor => this;
        public Config Config { get; } = new();
        /// <summary>Cells the rules see as unloaded chunks (null: all loaded).</summary>
        public System.Func<BlockPos, bool> Unloaded;
        public long ElapsedMilliseconds => Sim.Now;
        // one Block per id, as the game hands out its registered blocks (HY-0010's measurement counts only the rules' own garbage)
        static readonly System.Collections.Generic.Dictionary<int, Block> blocks = new();
        public Block Fluid(BlockPos p)
        {
            int id = Sim.Fl(p.X, p.Y, p.Z);
            if (!blocks.TryGetValue(id, out var b)) blocks[id] = b = new Block { Id = id };
            return b;
        }
        public bool Solid(BlockPos p) => Sim.Solid(p.X, p.Y, p.Z);
        public object GetChunkAtBlockPos(BlockPos p) => Unloaded != null && Unloaded(p) ? null : this;
        // every fluid code the sim can make (kind << 8 | flow << 4 | level), as world.Blocks indexes the game's blocks
        static Block[] all;
        public Block[] Blocks => all ??= System.Linq.Enumerable.Range(0, 1024).Select(i => new Block { Id = i }).ToArray();
        public GameWorld(Sim sim, int dimension = 0)
        {
            Sim = sim;
            Hydrology.Rules.H2Filling.ResetWorld();
            sim.FillOverride = (x, y, z, id) => Hydrology.Rules.H2Filling.TryFill(this, new BlockPos(x, y, z, dimension), new Block { Id = id });
        }
    }
}
