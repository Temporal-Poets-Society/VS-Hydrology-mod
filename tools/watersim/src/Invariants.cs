namespace WaterSim
{
    /// <summary>Water where nothing holds it (invariant a), counted on a settled world.</summary>
    public struct FloatingCount
    {
        /// <summary>Water with open air right below it (it would fall if it were updated).</summary>
        public int Hanging;
        /// <summary>Non-source water not labelled falling, on non-source water, and fed by no higher water beside it: only
        /// the water above holds it up (vanilla counts any water above as a full feed). A standing column.</summary>
        public int Standing;
        /// <summary>A source with air or non-source water below it.</summary>
        public int UnsupportedSources;
        public int Total => Hanging + Standing + UnsupportedSources;
        public override string ToString() => $"{Hanging} hanging, {Standing} standing, {UnsupportedSources} unsupported sources";
    }

    public static class Invariants
    {
        public static FloatingCount Floating(World w)
        {
            var r = new FloatingCount();
            for (int y = 1; y < w.SY; y++)
                for (int z = 0; z < w.SZ; z++)
                    for (int x = 0; x < w.SX; x++)
                    {
                        int c = w.Get(x, y, z);
                        if (c == 0 || w.IsSolid(x, y, z)) continue;
                        if (w.IsSolid(x, y - 1, z)) continue;
                        int below = w.Get(x, y - 1, z);
                        if (below == 0) { r.Hanging++; continue; }
                        if (F.IsSource(below)) continue;
                        if (F.IsSource(c)) { r.UnsupportedSources++; continue; }
                        if (F.Flow(c) == F.D) continue;
                        bool fedBeside = false;
                        for (int h = 0; h < 4 && !fedBeside; h++)
                        {
                            int n = w.Get(x + Dir.HX[h], y, z + Dir.HZ[h]);
                            fedBeside = F.Same(n, c) && F.Level(n) > F.Level(c);
                        }
                        if (!fedBeside) r.Standing++;
                    }
            return r;
        }
    }
}
