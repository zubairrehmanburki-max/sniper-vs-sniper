using System;
using System.Collections.Generic;
using System.Linq;

namespace SniperDuel.Ballistics
{
    public struct FiringSolution
    {
        /// <summary>Elevation to dial or hold, rad (positive = up).</summary>
        public double Elevation;
        /// <summary>Windage to dial or hold, rad (positive = right).</summary>
        public double Windage;
        public double TimeOfFlight;
        public double ImpactVelocity;
        public double ImpactEnergy;
    }

    public struct DopeRow
    {
        public double Range;
        public double Time;
        public double Velocity;
        public double Mach;
        public double Energy;
        /// <summary>Bullet height relative to the line of sight, m (negative = below).</summary>
        public double Height;
        /// <summary>Lateral offset, m (positive = right).</summary>
        public double Lateral;
        /// <summary>Elevation correction to dial, mrad (positive = dial up).</summary>
        public double ElevationMrad;
        /// <summary>Windage correction to dial, mrad (positive = dial right).</summary>
        public double WindageMrad;
    }

    public static class Solutions
    {
        /// <summary>
        /// Bore elevation (rad) that puts the bullet on the line of sight at zeroRange, in still air.
        /// Zeroing ignores dial, cant, incline and random effects, like zeroing on a flat range.
        /// </summary>
        public static double SolveZero(Shot shot, Environment env, double zeroRange, SolveOptions? options = null)
        {
            var level = shot.Clone();
            level.ZeroElevation = 0;
            level.ZeroWindage = 0;
            level.DialElevation = 0;
            level.DialWindage = 0;
            level.Cant = 0;
            level.LosElevation = 0;
            var calm = new Environment { Atmosphere = env.Atmosphere };
            var opts = (options ?? new SolveOptions()).Clone();
            opts.Effects = Effects.None;
            opts.Seed = null;
            opts.MaxRange = zeroRange;
            double elev = 0;
            for (int i = 0; i < 30; i++)
            {
                level.ZeroElevation = elev;
                var p = Solver.Simulate(level, calm, opts).AtRange(zeroRange);
                if (!p.HasValue) throw new InvalidOperationException($"Cannot zero at {zeroRange} m: bullet does not reach it");
                elev -= p.Value.Y / zeroRange;
                if (Math.Abs(p.Value.Y) < 1e-7) break;
            }
            return elev;
        }

        /// <summary>
        /// The dial/hold that puts the bullet on the point of aim at a slant distance along the shot's
        /// line of sight (incline, cant and wind included). The bot asks for this before perturbing it.
        /// </summary>
        public static FiringSolution SolveFiringSolution(Shot shot, Environment env, double slantRange, SolveOptions? options = null)
        {
            double range = slantRange * Math.Cos(shot.LosElevation);
            var opts = (options ?? new SolveOptions()).Clone();
            opts.MaxRange = range;
            var s = shot.Clone();
            double elevation = 0, windage = 0;
            for (int i = 0; i < 30; i++)
            {
                s.DialElevation = elevation;
                s.DialWindage = windage;
                var p = Solver.Simulate(s, env, opts).AtRange(range);
                if (!p.HasValue) throw new InvalidOperationException($"Target at {slantRange:0} m is out of reach");
                var rel = LosOffset.Of(p.Value, shot.LosElevation);
                if (Math.Abs(rel.Height) < 1e-5 && Math.Abs(rel.Lateral) < 1e-5)
                {
                    return new FiringSolution
                    {
                        Elevation = elevation,
                        Windage = windage,
                        TimeOfFlight = p.Value.T,
                        ImpactVelocity = p.Value.Speed,
                        ImpactEnergy = p.Value.Energy,
                    };
                }
                // With cant the dial axes are rotated; map the miss back into the reticle frame.
                double up = rel.Height / rel.Along;
                double right = rel.Lateral / rel.Along;
                elevation -= up * Math.Cos(shot.Cant) + right * Math.Sin(shot.Cant);
                windage -= right * Math.Cos(shot.Cant) - up * Math.Sin(shot.Cant);
            }
            throw new InvalidOperationException("Firing solution did not converge");
        }

        /// <summary>A dope card: corrections at each range for one shot setup (dial/hold should be zero).</summary>
        public static List<DopeRow> DopeTable(Shot shot, Environment env, IReadOnlyList<double> ranges, SolveOptions? options = null)
        {
            var opts = (options ?? new SolveOptions()).Clone();
            opts.MaxRange = ranges.Max();
            var traj = Solver.Simulate(shot, env, opts);
            var rows = new List<DopeRow>();
            foreach (double range in ranges)
            {
                var p = traj.AtRange(range) ?? throw new InvalidOperationException($"Bullet does not reach {range} m");
                var rel = LosOffset.Of(p, traj.LosElevation);
                rows.Add(new DopeRow
                {
                    Range = range,
                    Time = p.T,
                    Velocity = p.Speed,
                    Mach = p.Mach,
                    Energy = p.Energy,
                    Height = rel.Height,
                    Lateral = rel.Lateral,
                    ElevationMrad = Units.RadToMrad(-rel.Height / rel.Along),
                    WindageMrad = Units.RadToMrad(-rel.Lateral / rel.Along),
                });
            }
            return rows;
        }
    }
}
