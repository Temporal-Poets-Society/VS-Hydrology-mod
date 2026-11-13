// Minimal API facts for compiling the actual H2Filling.cs without game DLLs. The surrounding engine is watersim's
// calibrated port, not the engine itself. No Harmony, chunk packing, Earth binding or H4 behavior is emulated here.
using WaterSim;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.MathTools
{
    public sealed class BlockFacing
    {
        public int X, Y, Z;
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
    }
}
namespace Vintagestory.API.Common
{
    public sealed class Block
    {
        public int Id;
        public int LiquidLevel => F.Level(Id);
        public string LiquidCode => Id == 0 ? null : F.Kind(Id) == F.Salt ? "saltwater" : "water";
        public void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neib) => ((Issue20Tests.GameWorld)world).Sim.Wake(pos.X, pos.Y, pos.Z);
    }
    public interface IBlockAccessor { Block Fluid(BlockPos p); bool Solid(BlockPos p); }
    public interface IWorldAccessor { IBlockAccessor BlockAccessor { get; } Config Config { get; } long ElapsedMilliseconds { get; } }
    public sealed class Config
    {
        public bool Vessels = true, VesselsWithQuery;
        public bool? Flooding;
        public bool HasAttribute(string key) => key == "hydrologyFlooding" && Flooding.HasValue;
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
        public static bool River(BlockPos p) => Active && p.dimension == 0 && IsRiver(p);
    }
    public static class HydrologyStats
    {
        public static long Filled, Refilled, FilledVessels, VesselsRefused, VesselsInRiverRefused, FilledPockets, Unheld, Unsupplied,
            Released, ReleasedCells, ReleaseAbsorbed, ReleaseKept;
    }
    public static class Liquids
    {
        public static readonly Block WaterSource = new() { Id = F.Make(F.Water, F.Still, 7) };
        public static readonly Block SaltSource = new() { Id = F.Make(F.Salt, F.Still, 7) };
        public static bool IsSource(Block b) => F.IsSource(b.Id);
        public static bool Handles(Block b) => b.Id != 0;
        public static bool Same(Block a, Block b) => F.Same(a.Id, b.Id);
        public static Block Fluid(IBlockAccessor ba, BlockPos p) => ba.Fluid(p);
        public static bool GroundBelow(IBlockAccessor ba, BlockPos p) => ba.Solid(p.DownCopy());
        public static bool Open(IBlockAccessor ba, BlockPos p) => !ba.Solid(p);
        public static bool Loaded(IBlockAccessor ba, BlockPos p) => true;
        public static bool Passes(IBlockAccessor ba, BlockPos p, BlockPos n, BlockFacing face) => !ba.Solid(p) && !ba.Solid(n);
        public static BlockPos Below(BlockPos p) => p.DownCopy();
        public static BlockPos Side2(BlockPos p) => p.Copy();
        public static void SetAndWake(IWorldAccessor world, BlockPos p, int id)
        {
            var sim = ((Issue20Tests.GameWorld)world).Sim;
            sim.SetDirect(p.X, p.Y, p.Z, id);
            sim.Wake(p.X, p.Y, p.Z);
            sim.QueueNeighbourUpdate(p.X, p.Y, p.Z);
        }
    }
}
namespace Hydrology.Rules
{
    public static class H4Rapids { public static void WakeEdgesOnNewSource(IWorldAccessor world, BlockPos pos) { } }
}
namespace Issue20Tests
{
    public sealed class GameWorld : IWorldAccessor, IBlockAccessor
    {
        public Sim Sim;
        public IBlockAccessor BlockAccessor => this;
        public Config Config { get; } = new();
        public long ElapsedMilliseconds => Sim.Now;
        public Block Fluid(BlockPos p) => new() { Id = Sim.Fl(p.X, p.Y, p.Z) };
        public bool Solid(BlockPos p) => Sim.Solid(p.X, p.Y, p.Z);
        public GameWorld(Sim sim, int dimension = 0)
        {
            Sim = sim;
            Hydrology.Rules.H2Filling.ResetWorld();
            sim.FillOverride = (x, y, z, id) => Hydrology.Rules.H2Filling.TryFill(this, new BlockPos(x, y, z, dimension), new Block { Id = id });
        }
    }
}
