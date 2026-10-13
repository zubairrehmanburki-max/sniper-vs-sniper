using System.Collections.Generic;

namespace SniperDuel.Ballistics
{
    /// <summary>
    /// A band of the lane with one wind. Shot frame: x = downrange (positive is a tailwind), y = up,
    /// z = to the shooter's right (positive blows the bullet right).
    /// </summary>
    public sealed class WindZone
    {
        /// <summary>Downrange distance (m) where this zone ends. The last zone should be +∞.</summary>
        public double UntilRange;
        public Vec3 Vector;

        public WindZone(double untilRange, Vec3 vector)
        {
            UntilRange = untilRange;
            Vector = vector;
        }
    }

    public sealed class GustModel
    {
        /// <summary>Peak fractional speed change, e.g. 0.3 for ±30%.</summary>
        public double Amplitude;
        /// <summary>Seconds between noise lattice points (gust period).</summary>
        public double Period;
        public uint Seed;
    }

    /// <summary>Layered wind along the lane: shooter, mid-range and target zones, with gusts over time.</summary>
    public sealed class WindField
    {
        public List<WindZone> Zones = new List<WindZone>();
        public GustModel? Gust;

        /// <summary>
        /// Wind vector from a speed and the direction it blows FROM, degrees relative to the shot:
        /// 0 = from behind, 90 = from the left (pushes right), 180 = headwind, 270 = from the right.
        /// </summary>
        public static Vec3 From(double speed, double fromDeg, double vertical = 0)
        {
            double a = fromDeg * Units.Deg;
            return new Vec3(speed * System.Math.Cos(a), vertical, speed * System.Math.Sin(a));
        }

        /// <summary>Uniform wind over the whole lane.</summary>
        public static WindField Uniform(double speed, double fromDeg)
        {
            var f = new WindField();
            f.Zones.Add(new WindZone(double.PositiveInfinity, From(speed, fromDeg)));
            return f;
        }

        /// <summary>Gust multiplier at an absolute match time (1 = base wind).</summary>
        public static double GustFactor(GustModel? gust, double time)
        {
            if (gust == null || gust.Amplitude == 0) return 1;
            return 1 + gust.Amplitude * Rng.ValueNoise1D(gust.Seed, time / gust.Period);
        }

        /// <summary>Wind at a downrange distance and absolute match time.</summary>
        public Vec3 At(double range, double time)
        {
            for (int i = 0; i < Zones.Count; i++)
            {
                var zone = Zones[i];
                if (range < zone.UntilRange || i == Zones.Count - 1) return zone.Vector * GustFactor(Gust, time);
            }
            return Vec3.Zero;
        }
    }
}
