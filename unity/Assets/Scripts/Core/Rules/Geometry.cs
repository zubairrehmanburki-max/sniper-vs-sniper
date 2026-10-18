using System;
using SniperDuel.Ballistics;

namespace SniperDuel.Rules
{
    public enum CoverMaterial
    {
        Hard,
        Soft,
    }

    public enum CoverLook
    {
        Rock,
        Concrete,
        Metal,
        Wood,
    }

    /// <summary>
    /// Axis-aligned box in world space. Hard cover (rock, concrete) stops every bullet; soft cover
    /// (sheet metal, wood) lets a hard enough hit through. Both block sight.
    /// </summary>
    public sealed class Box
    {
        public string Id = "";
        public Vec3 Min;
        public Vec3 Max;
        public CoverMaterial Material;
        /// <summary>How it is drawn; defaults to a boulder.</summary>
        public CoverLook? Look;
    }

    public static class Geometry
    {
        static double Axis(Vec3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        /// <summary>
        /// Fraction t ∈ [0, 1] along the segment a→b where it first enters the box, or null.
        /// Slab method.
        /// </summary>
        public static double? SegmentBoxEntry(Vec3 a, Vec3 b, Box box)
        {
            double t0 = 0;
            double t1 = 1;
            for (int axis = 0; axis < 3; axis++)
            {
                double aa = Axis(a, axis);
                double d = Axis(b, axis) - aa;
                double lo = Axis(box.Min, axis);
                double hi = Axis(box.Max, axis);
                if (Math.Abs(d) < 1e-12)
                {
                    if (aa < lo || aa > hi) return null;
                    continue;
                }
                double ta = (lo - aa) / d;
                double tb = (hi - aa) / d;
                if (ta > tb)
                {
                    double tmp = ta;
                    ta = tb;
                    tb = tmp;
                }
                t0 = Math.Max(t0, ta);
                t1 = Math.Min(t1, tb);
                if (t0 > t1) return null;
            }
            return t0;
        }

        public static bool PointInBox(Vec3 p, Box box, double margin = 0) =>
            p.X >= box.Min.X - margin &&
            p.X <= box.Max.X + margin &&
            p.Y >= box.Min.Y - margin &&
            p.Y <= box.Max.Y + margin &&
            p.Z >= box.Min.Z - margin &&
            p.Z <= box.Max.Z + margin;

        public static double Distance(Vec3 a, Vec3 b) => JsMath.Hypot(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static double HorizontalDistance(Vec3 a, Vec3 b) => JsMath.Hypot(a.X - b.X, a.Z - b.Z);
    }
}
