using System;
using System.Collections.Generic;

namespace GameSvr
{
    public sealed class TSafeZoneArea
    {
        public string MapName { get; set; } = string.Empty;
        public List<(int X, int Y)> Points { get; } = new List<(int X, int Y)>();

        public bool Contains(string mapName, int x, int y)
        {
            if (!string.Equals(MapName, mapName, StringComparison.OrdinalIgnoreCase) ||
                Points.Count != 4)
            {
                return false;
            }

            var windingNumber = 0;
            for (var i = 0; i < Points.Count; i++)
            {
                var start = Points[i];
                var end = Points[(i + 1) % Points.Count];
                var side = Cross(start, end, x, y);

                // sub_4C6800 checks all four segments with sub_4C6744 before
                // PtInRegion, so every polygon edge and vertex is included.
                if (side == 0 &&
                    x >= Math.Min(start.X, end.X) && x <= Math.Max(start.X, end.X) &&
                    y >= Math.Min(start.Y, end.Y) && y <= Math.Max(start.Y, end.Y))
                {
                    return true;
                }

                if (start.Y <= y)
                {
                    if (end.Y > y && side > 0)
                    {
                        windingNumber++;
                    }
                }
                else if (end.Y <= y && side < 0)
                {
                    windingNumber--;
                }
            }

            return windingNumber != 0;
        }

        private static long Cross((int X, int Y) start, (int X, int Y) end,
            int x, int y) =>
            ((long)end.X - start.X) * ((long)y - start.Y) -
            ((long)end.Y - start.Y) * ((long)x - start.X);
    }
}
