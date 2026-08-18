namespace GameSvr
{
    public partial class Envirnoment
    {
        private readonly List<NativeMapAreaRegion> _nativeMapAreaRegions = new();

        internal int NativeMapAreaRegionCount => _nativeMapAreaRegions.Count;

        internal void ClearNativeMapAreaRegions()
        {
            _nativeMapAreaRegions.Clear();
        }

        internal void PrependNativeMapAreaRegion(string description,
            int x, int y, int radius)
        {
            // maparea.txt is converted to a head-linked list by the native
            // loader, so a later section on the line wins an overlap.
            _nativeMapAreaRegions.Insert(0,
                new NativeMapAreaRegion(description, x, y, radius));
        }

        internal string ResolveNativeMapDescription(int x, int y)
        {
            var fallback = sMapDesc ?? string.Empty;
            // sub_77B5CC returns [Envir+0x48] immediately when either axis is 0.
            if (x == 0 || y == 0)
                return fallback;

            for (var index = 0; index < _nativeMapAreaRegions.Count; index++)
            {
                var region = _nativeMapAreaRegions[index];
                if (region.Contains(x, y))
                    return region.Description;
            }
            return fallback;
        }
    }

    internal readonly struct NativeMapAreaRegion
    {
        internal NativeMapAreaRegion(string description, int x, int y,
            int radius)
        {
            Description = description ?? string.Empty;
            X = x;
            Y = y;
            Radius = radius;
        }

        internal string Description { get; }
        internal int X { get; }
        internal int Y { get; }
        internal int Radius { get; }

        internal bool Contains(int x, int y)
        {
            // sub_77C2D8 uses a strict Manhattan-radius comparison.
            var distance = Math.Abs((long)x - X) + Math.Abs((long)y - Y);
            return distance < Radius;
        }
    }
}
