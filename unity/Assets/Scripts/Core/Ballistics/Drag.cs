using System;

namespace SniperDuel.Ballistics
{
    public static class Drag
    {
        /// <summary>Standard-projectile drag coefficient at a Mach number, linearly interpolated.</summary>
        public static double Coefficient(DragModel model, double mach)
        {
            double[] m = model == DragModel.G1 ? DragTables.G1Mach : DragTables.G7Mach;
            double[] cd = model == DragModel.G1 ? DragTables.G1Cd : DragTables.G7Cd;
            int n = m.Length;
            if (mach <= m[0]) return cd[0];
            if (mach >= m[n - 1]) return cd[n - 1];
            int lo = 0;
            int hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (m[mid] <= mach) lo = mid;
                else hi = mid;
            }
            double f = (mach - m[lo]) / (m[hi] - m[lo]);
            return cd[lo] + f * (cd[hi] - cd[lo]);
        }

        /// <summary>
        /// Drag factor k such that the drag deceleration vector is a = -k·|v_r|·v_r.
        /// k = ρ·Cd_std·π / (8·BC) with BC in kg/m². Mass and diameter cancel out.
        /// </summary>
        public static double Factor(DragModel model, double bcLbIn2, double mach, double density) =>
            density * Coefficient(model, mach) * Math.PI / (8 * bcLbIn2 * Units.BcLbIn2);
    }

    public static class Stability
    {
        /// <summary>Miller gyroscopic stability factor Sg, with velocity and atmosphere corrections. twist is metres per turn.</summary>
        public static double Miller(Projectile projectile, double twist, double muzzleVelocity, Atmosphere atmo)
        {
            if (twist == 0) return 0;
            double d = projectile.DiameterM / Units.Inch;
            double twistCal = Math.Abs(twist) / Units.Inch / d;
            double lengthCal = projectile.LengthM / Units.Inch / d;
            double grains = projectile.MassKg / Units.Grain;
            double sg = 30 * grains / (twistCal * twistCal * d * d * d * lengthCal * (1 + lengthCal * lengthCal));
            double fv = Math.Cbrt(muzzleVelocity / Units.Fps / 2800);
            double tempF = atmo.TemperatureC * 9 / 5 + 32;
            double pressureInHg = atmo.PressureHPa / 33.8639;
            double ftp = (tempF + 460) / 519 * (29.92 / pressureInHg);
            return sg * fv * ftp;
        }

        /// <summary>Litz spin drift, metres to the right (left for a left-hand twist).</summary>
        public static double SpinDrift(double sg, double twist, double time)
        {
            if (sg == 0 || twist == 0 || time <= 0) return 0;
            return Math.Sign(twist) * 1.25 * (sg + 1.2) * Math.Pow(time, 1.83) * Units.Inch;
        }

        /// <summary>Litz aerodynamic-jump estimate: vertical launch deflection (rad) from crosswind at the muzzle.</summary>
        public static double AerodynamicJump(Projectile projectile, double sg, double twist, double crosswind)
        {
            if (sg == 0 || twist == 0) return 0;
            double lengthCal = projectile.LengthM / projectile.DiameterM;
            double moaPerMph = 0.01 * sg - 0.0024 * lengthCal + 0.032;
            return Math.Sign(twist) * moaPerMph * (crosswind / Units.Mph) * Units.Moa;
        }
    }
}
