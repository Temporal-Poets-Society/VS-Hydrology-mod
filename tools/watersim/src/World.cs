using System;
using System.Text;

namespace WaterSim
{
    /// <summary>
    /// A fluid block, packed in an int: kind (water, rapid water, salt water) &lt;&lt; 8 | flow &lt;&lt; 4 | height. 0 is no fluid.
    /// The flows are vanilla's variant states: still, the 8 cardinal directions in vanilla's Cardinal order, and d (falling).
    /// </summary>
    public static class F
    {
        public const int Water = 1, Rapid = 2, Salt = 3;
        public const int Still = 0, N = 1, NE = 2, E = 3, SE = 4, S = 5, SW = 6, W = 7, NW = 8, D = 9;
        public const int SourceLevel = 7;

        public static readonly string[] FlowNames = { "still", "n", "ne", "e", "se", "s", "sw", "w", "nw", "d" };
        public static readonly string[] KindNames = { "air", "water", "rapidwater", "saltwater" };
        /// <summary>The (x, z) unit vector of flows 1..8 (Cardinal.Normali; north is -z).</summary>
        public static readonly int[] FlowDX = { 0, 0, 1, 1, 1, 0, -1, -1, -1, 0 };
        public static readonly int[] FlowDZ = { 0, -1, -1, 0, 1, 1, 1, 0, -1, 0 };

        public static int Make(int kind, int flow, int level) => level < 1 ? 0 : (kind << 8) | (flow << 4) | level;
        public static int Kind(int c) => c >> 8;
        public static int Flow(int c) => (c >> 4) & 15;
        public static int Level(int c) => c & 15;
        public static bool IsLiquid(int c) => c != 0;
        public static bool IsSource(int c) => (c & 15) == SourceLevel;
        public static bool IsRapid(int c) => (c >> 8) == Rapid;
        public static bool IsFresh(int c) => c != 0 && (c >> 8) != Salt;
        /// <summary>Vanilla's LiquidCode: rapid water's is "water", so water and rapid water are the same liquid.</summary>
        public static int Code(int c) => c == 0 ? 0 : ((c >> 8) == Salt ? 2 : 1);
        public static bool Same(int a, int b) => a != 0 && b != 0 && Code(a) == Code(b);
        public static int WithKind(int c, int kind) => c == 0 ? 0 : (kind << 8) | (c & 255);
        public static int WithFlow(int c, int flow) => c == 0 ? 0 : (c & ~0xF0) | (flow << 4);

        /// <summary>
        /// BlockForFluidsLayer.FlowRate: the length of FlowNormali, which is the flow direction times the block's flowSpeed
        /// attribute (2 for rapid water). Still and falling water have no direction, so rate 1 in either kind.
        /// </summary>
        public static int FlowRate(int c)
        {
            int f = Flow(c);
            if (f >= N && f <= NW) return Kind(c) == Rapid ? 2 : 1;
            return 1;
        }

        public static int Delay(int kind) => kind == Rapid ? 50 : kind == Salt ? 125 : 75;
        public static bool MultiplySpread(int kind) => kind != Rapid;

        public static string Name(int c) => c == 0 ? "air" : KindNames[Kind(c)] + "-" + FlowNames[Flow(c)] + "-" + Level(c);
    }

    /// <summary>A box of cells: solid layer (0 air, 1 stone) and fluid layer. Outside the box is stone without fluid.</summary>
    public sealed class World
    {
        public readonly int SX, SY, SZ;
        public readonly byte[] Solid;
        public readonly int[] Fluid;
        /// <summary>Sub-level of flowing water (prototype of long runs only).</summary>
        public readonly byte[] Sub;

        public World(int sx, int sy, int sz)
        {
            SX = sx; SY = sy; SZ = sz;
            Solid = new byte[sx * sy * sz];
            Fluid = new int[sx * sy * sz];
            Sub = new byte[sx * sy * sz];
        }

        public World Clone()
        {
            var w = new World(SX, SY, SZ);
            Array.Copy(Solid, w.Solid, Solid.Length);
            Array.Copy(Fluid, w.Fluid, Fluid.Length);
            Array.Copy(Sub, w.Sub, Sub.Length);
            return w;
        }

        public void CopyFrom(World o)
        {
            Array.Copy(o.Solid, Solid, Solid.Length);
            Array.Copy(o.Fluid, Fluid, Fluid.Length);
            Array.Copy(o.Sub, Sub, Sub.Length);
        }

        public bool In(int x, int y, int z) => (uint)x < (uint)SX && (uint)y < (uint)SY && (uint)z < (uint)SZ;
        public int Idx(int x, int y, int z) => (y * SZ + z) * SX + x;
        public void Pos(int i, out int x, out int y, out int z) { x = i % SX; z = (i / SX) % SZ; y = i / (SX * SZ); }

        public bool IsSolid(int x, int y, int z) => !In(x, y, z) || Solid[Idx(x, y, z)] != 0;
        public int Get(int x, int y, int z) => In(x, y, z) ? Fluid[Idx(x, y, z)] : 0;

        public void SetSolid(int x, int y, int z, bool s) { if (In(x, y, z)) { Solid[Idx(x, y, z)] = (byte)(s ? 1 : 0); if (s) Fluid[Idx(x, y, z)] = 0; } }
        public void SetFluid(int x, int y, int z, int f) { if (In(x, y, z)) Fluid[Idx(x, y, z)] = f; }

        public void FillSolid(int x0, int y0, int z0, int x1, int y1, int z1, bool s = true)
        {
            for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) SetSolid(x, y, z, s);
        }

        public int CountDiff(World o)
        {
            int n = 0;
            for (int i = 0; i < Fluid.Length; i++) if (Fluid[i] != o.Fluid[i] || Solid[i] != o.Solid[i]) n++;
            return n;
        }

        /// <summary>Cells whose water differs in a way a player sees: presence, source or not, kind, or height.</summary>
        public int CountWaterDiff(World o, bool ignoreLabels = true)
        {
            int n = 0;
            for (int i = 0; i < Fluid.Length; i++)
            {
                int a = Fluid[i], b = o.Fluid[i];
                if (ignoreLabels) { a &= ~0xF0; b &= ~0xF0; }
                if (a != b || Solid[i] != o.Solid[i]) n++;
            }
            return n;
        }

        public int CountSources()
        {
            int n = 0;
            foreach (int c in Fluid) if (F.IsSource(c)) n++;
            return n;
        }

        public string Column(int x, int z, int y0, int y1)
        {
            var sb = new StringBuilder();
            for (int y = y1; y >= y0; y--)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(IsSolid(x, y, z) ? "##" : F.Name(Get(x, y, z)));
            }
            return sb.ToString();
        }

        /// <summary>A horizontal slice: # stone, . air, S/s source (rapid upper case R), digits flowing height, | falling.</summary>
        public string Slice(int y)
        {
            var sb = new StringBuilder();
            for (int z = 0; z < SZ; z++)
            {
                for (int x = 0; x < SX; x++) sb.Append(Glyph(x, y, z));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>A vertical slice at row z (x across, y up).</summary>
        public string Section(int z, int y0 = 0, int y1 = -1)
        {
            if (y1 < 0) y1 = SY - 1;
            var sb = new StringBuilder();
            for (int y = y1; y >= y0; y--)
            {
                sb.Append(y.ToString().PadLeft(3)).Append(' ');
                for (int x = 0; x < SX; x++) sb.Append(Glyph(x, y, z));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public char Glyph(int x, int y, int z)
        {
            if (IsSolid(x, y, z)) return '#';
            int c = Get(x, y, z);
            if (c == 0) return '.';
            bool rapid = F.IsRapid(c), salt = F.Kind(c) == F.Salt;
            if (F.IsSource(c)) return rapid ? 'R' : salt ? 'Z' : 'S';
            if (F.Flow(c) == F.D) return rapid ? '!' : '|';
            if (F.Flow(c) == F.Still) return rapid ? 'o' : 'u';
            return (char)((rapid ? 'a' - 1 : '0') + F.Level(c));
        }
    }
}
