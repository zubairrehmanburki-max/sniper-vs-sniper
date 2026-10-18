using System;
using System.Collections.Generic;
using SniperDuel.Ballistics;

namespace SniperDuel.Rules
{
    public static class Visibility
    {
        /// <summary>True when nothing (hard cover or terrain) blocks the straight line between two points.</summary>
        public static bool LineOfSight(Vec3 from, Vec3 to, Ground ground, IReadOnlyList<Box> cover, double step = 2)
        {
            foreach (var box in cover)
            {
                double? f = Geometry.SegmentBoxEntry(from, to, box);
                if (f.HasValue && f.Value > 1e-6 && f.Value < 1 - 1e-6) return false;
            }
            double len = JsMath.Hypot(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
            int n = (int)Math.Max(2, Math.Ceiling(len / step));
            for (int i = 1; i < n; i++)
            {
                double f = (double)i / n;
                double x = from.X + (to.X - from.X) * f;
                double y = from.Y + (to.Y - from.Y) * f;
                double z = from.Z + (to.Z - from.Z) * f;
                if (y < ground(x, z)) return false;
            }
            return true;
        }
    }
}
