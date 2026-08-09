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
            if (!string.Equals(MapName, mapName, StringComparison.OrdinalIgnoreCase) || Points.Count < 3)
            {
                return false;
            }

            var inside = false;
            for (int i = 0, j = Points.Count - 1; i < Points.Count; j = i++)
            {
                var pi = Points[i];
                var pj = Points[j];
                if (((pi.Y > y) != (pj.Y > y)) &&
                    x < (double)(pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y) + pi.X)
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
