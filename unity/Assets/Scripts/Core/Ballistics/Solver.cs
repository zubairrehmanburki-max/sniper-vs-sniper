using System;
using System.Collections.Generic;

namespace SniperDuel.Ballistics
{
    /// <summary>
    /// Everything about the rifle and the trigger press. Angles are radians.
    /// Shot frame: the scope's eye is the origin, x points downrange (horizontal, along the line of
    /// sight's azimuth), y is up, z is to the shooter's right.
    /// </summary>
    public sealed class Shot
    {
        public Projectile Projectile = null!;
        /// <summary>Nominal muzzle velocity, m/s.</summary>
        public double MuzzleVelocity;
        /// <summary>Shot-to-shot muzzle velocity standard deviation, m/s. Applied only when a seed is given.</summary>
        public double MuzzleVelocitySd;
        /// <summary>Scope centre height over the bore, m.</summary>
        public double SightHeight;
        /// <summary>Barrel twist, metres per turn. Positive = right-hand, negative = left-hand, 0 = ignore spin.</summary>
        public double Twist;
        /// <summary>Bore elevation above the scope axis from zeroing (see Solutions.SolveZero).</summary>
        public double ZeroElevation;
        public double ZeroWindage;
        /// <summary>Line-of-sight elevation above horizontal (uphill positive).</summary>
        public double LosElevation;
        /// <summary>Compass azimuth of the line of sight (0 = north, π/2 = east). Only used for Coriolis.</summary>
        public double? Azimuth;
        /// <summary>Elevation dialled on the turret or held on the reticle (positive = up).</summary>
        public double DialElevation;
        /// <summary>Windage dialled or held (positive = right).</summary>
        public double DialWindage;
        /// <summary>Rifle cant, positive = rotated clockwise as the shooter sees it.</summary>
        public double Cant;

        public Shot Clone() => (Shot)MemberwiseClone();
    }

    public sealed class Environment
    {
        public Atmosphere Atmosphere = Atmosphere.Icao;
        public WindField? Wind;
        /// <summary>Latitude in degrees, needed for Coriolis/Eötvös.</summary>
        public double? Latitude;
    }

    public sealed class Effects
    {
        /// <summary>Litz spin drift (needs twist and bullet length).</summary>
        public bool SpinDrift = true;
        /// <summary>Coriolis and Eötvös (needs latitude and azimuth). Off by default: "full realism" toggle.</summary>
        public bool Coriolis;
        /// <summary>Vertical jump from muzzle crosswind.</summary>
        public bool AerodynamicJump;
        /// <summary>Seeded dispersion growth below the transonic threshold; needs a seed.</summary>
        public bool TransonicDispersion;

        public static Effects None => new Effects { SpinDrift = false };
        public Effects Clone() => (Effects)MemberwiseClone();
    }

    public sealed class TransonicConfig
    {
        /// <summary>Mach number below which the bullet starts to destabilise.</summary>
        public double MachThreshold = 1.2;
        /// <summary>Angular random-walk intensity of the velocity direction, rad/√s.</summary>
        public double Sigma = 0.004;
    }

    public enum Integrator
    {
        Rk4,
        Euler,
    }

    public sealed class SolveOptions
    {
        /// <summary>Stop once the bullet passes this horizontal downrange distance, m.</summary>
        public double MaxRange;
        /// <summary>Hard stop on time of flight, s.</summary>
        public double MaxTime = 8;
        /// <summary>Integration step, s.</summary>
        public double Dt = 0.0005;
        public Integrator Integrator = Integrator.Rk4;
        /// <summary>Match seed. Drives muzzle-velocity variation and transonic dispersion.</summary>
        public uint? Seed;
        /// <summary>Match clock at the trigger press, s. Sets the gust phase.</summary>
        public double FireTime;
        public Effects Effects = new Effects();
        public TransonicConfig Transonic = new TransonicConfig();
        /// <summary>Stop if the bullet drops this far below the shooter, m.</summary>
        public double MaxDrop = 2000;
        /// <summary>Stop below this speed, m/s.</summary>
        public double MinSpeed = 30;

        public SolveOptions Clone()
        {
            var c = (SolveOptions)MemberwiseClone();
            c.Effects = Effects.Clone();
            return c;
        }
    }

    public struct TrajectoryPoint
    {
        /// <summary>Time of flight, s.</summary>
        public double T;
        /// <summary>Position in the shot frame, m. Spin drift is included in Z.</summary>
        public double X, Y, Z;
        public double Vx, Vy, Vz;
        public double Speed;
        public double Mach;
        /// <summary>Kinetic energy, J.</summary>
        public double Energy;
    }

    public enum Termination
    {
        Range,
        Time,
        Drop,
        Speed,
    }

    public sealed class Trajectory
    {
        public List<TrajectoryPoint> Points = new List<TrajectoryPoint>();
        /// <summary>Muzzle velocity actually used (nominal plus seeded variation), m/s.</summary>
        public double MuzzleVelocity;
        /// <summary>Miller stability factor (0 if spin is ignored).</summary>
        public double Stability;
        /// <summary>Aerodynamic jump applied at launch, rad (positive = up).</summary>
        public double AerodynamicJump;
        public double LosElevation;
        public Termination Termination;

        /// <summary>Bullet state where it crosses a horizontal downrange distance, or null if it never got there.</summary>
        public TrajectoryPoint? AtRange(double range)
        {
            var pts = Points;
            if (pts.Count == 0 || pts[pts.Count - 1].X < range) return null;
            if (pts[0].X >= range) return pts[0];
            int lo = 0;
            int hi = pts.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (pts[mid].X < range) lo = mid;
                else hi = mid;
            }
            var a = pts[lo];
            var b = pts[hi];
            return Interpolate(a, b, (range - a.X) / (b.X - a.X));
        }

        /// <summary>Bullet state at a time of flight, or null if the flight ended earlier.</summary>
        public TrajectoryPoint? AtTime(double time)
        {
            var pts = Points;
            if (pts.Count == 0 || time > pts[pts.Count - 1].T) return null;
            if (time <= 0) return pts[0];
            int lo = 0;
            int hi = pts.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (pts[mid].T < time) lo = mid;
                else hi = mid;
            }
            var a = pts[lo];
            var b = pts[hi];
            return Interpolate(a, b, (time - a.T) / (b.T - a.T));
        }

        static double Lerp(double a, double b, double f) => a + (b - a) * f;

        static TrajectoryPoint Interpolate(TrajectoryPoint a, TrajectoryPoint b, double f) => new TrajectoryPoint
        {
            T = Lerp(a.T, b.T, f),
            X = Lerp(a.X, b.X, f),
            Y = Lerp(a.Y, b.Y, f),
            Z = Lerp(a.Z, b.Z, f),
            Vx = Lerp(a.Vx, b.Vx, f),
            Vy = Lerp(a.Vy, b.Vy, f),
            Vz = Lerp(a.Vz, b.Vz, f),
            Speed = Lerp(a.Speed, b.Speed, f),
            Mach = Lerp(a.Mach, b.Mach, f),
            Energy = Lerp(a.Energy, b.Energy, f),
        };
    }

    /// <summary>Position relative to the line of sight.</summary>
    public struct LosOffset
    {
        /// <summary>Distance along the line of sight, m.</summary>
        public double Along;
        /// <summary>Height above the line of sight, m.</summary>
        public double Height;
        /// <summary>Lateral offset, m (positive = right).</summary>
        public double Lateral;

        public static LosOffset Of(TrajectoryPoint p, double losElevation)
        {
            double ce = Math.Cos(losElevation);
            double se = Math.Sin(losElevation);
            return new LosOffset { Along = p.X * ce + p.Y * se, Height = -p.X * se + p.Y * ce, Lateral = p.Z };
        }
    }

    /// <summary>
    /// 3-DOF point-mass solver, a line-for-line port of packages/ballistics/src/solver.ts.
    /// A pure function of (shot, environment, options): no globals, no clock, no Random.
    /// </summary>
    public static class Solver
    {
        public static Trajectory Simulate(Shot shot, Environment env, SolveOptions options)
        {
            var effects = options.Effects;
            double dt = options.Dt;
            double fireTime = options.FireTime;
            var transonic = options.Transonic;
            var wind = env.Wind;
            var air = Air.Model(env.Atmosphere);
            var projectile = shot.Projectile;
            bool seeded = options.Seed.HasValue;

            // --- launch conditions ---
            double mv = shot.MuzzleVelocity;
            if (seeded && shot.MuzzleVelocitySd != 0)
                mv += shot.MuzzleVelocitySd * new Rng(Rng.HashSeed(options.Seed!.Value, 0x6d76)).Normal();

            double sg = effects.SpinDrift || effects.AerodynamicJump ? Stability.Miller(projectile, shot.Twist, mv, env.Atmosphere) : 0;

            double losEl = shot.LosElevation;
            double cant = shot.Cant;
            // Unit vectors of the scope: line of sight l, scope-up u, scope-right r (cant rotates u/r about l).
            double ce = Math.Cos(losEl), se = Math.Sin(losEl);
            double cc = Math.Cos(cant), sc = Math.Sin(cant);
            var l = new Vec3(ce, se, 0);
            var up0 = new Vec3(-se, ce, 0);
            var u = new Vec3(cc * up0.X, cc * up0.Y, sc);
            var r = new Vec3(-sc * up0.X, -sc * up0.Y, cc);

            double elev = shot.ZeroElevation + shot.DialElevation;
            double wnd = shot.ZeroWindage + shot.DialWindage;
            double te = Math.Tan(elev);
            double tw = Math.Tan(wnd);
            double dx = l.X + te * u.X + tw * r.X;
            double dy = l.Y + te * u.Y + tw * r.Y;
            double dz = l.Z + te * u.Z + tw * r.Z;

            double jump = 0;
            if (effects.AerodynamicJump && wind != null)
            {
                jump = Stability.AerodynamicJump(projectile, sg, shot.Twist, wind.At(0, fireTime).Z);
                double tj = Math.Tan(jump);
                dx += tj * up0.X;
                dy += tj * up0.Y;
            }
            double dn = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            double x = -shot.SightHeight * u.X;
            double y = -shot.SightHeight * u.Y;
            double z = -shot.SightHeight * u.Z;
            double vx = mv * dx / dn;
            double vy = mv * dy / dn;
            double vz = mv * dz / dn;

            // Earth rotation in the shot frame (x along the azimuth, y up, z right).
            double ox = 0, oy = 0, oz = 0;
            if (effects.Coriolis && env.Latitude.HasValue && shot.Azimuth.HasValue)
            {
                double lat = env.Latitude.Value * Units.Deg;
                double az = shot.Azimuth.Value;
                ox = Units.EarthRotation * Math.Cos(lat) * Math.Cos(az);
                oy = Units.EarthRotation * Math.Sin(lat);
                oz = -Units.EarthRotation * Math.Cos(lat) * Math.Sin(az);
            }
            bool useCoriolis = ox != 0 || oy != 0 || oz != 0;

            double bc = projectile.Bc;
            var model = projectile.DragModel;
            double ax = 0, ay = 0, az2 = 0;

            // Acceleration at a state; writes ax/ay/az2 and returns Mach.
            double Accel(double t, double px, double py, double vxx, double vyy, double vzz)
            {
                double wx = 0, wy = 0, wz = 0;
                if (wind != null)
                {
                    var w = wind.At(px, fireTime + t);
                    wx = w.X;
                    wy = w.Y;
                    wz = w.Z;
                }
                double rx = vxx - wx, ry = vyy - wy, rz = vzz - wz;
                double vr = Math.Sqrt(rx * rx + ry * ry + rz * rz);
                var a = air(py);
                double mach = vr / a.SpeedOfSound;
                double k = Drag.Factor(model, bc, mach, a.Density) * vr;
                ax = -k * rx;
                ay = -k * ry - Units.Gravity;
                az2 = -k * rz;
                if (useCoriolis)
                {
                    ax += -2 * (oy * vzz - oz * vyy);
                    ay += -2 * (oz * vxx - ox * vzz);
                    az2 += -2 * (ox * vyy - oy * vxx);
                }
                return mach;
            }

            var traj = new Trajectory { Stability = sg, LosElevation = losEl };
            var points = traj.Points;

            void Record(double t, double m)
            {
                double speed = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                points.Add(new TrajectoryPoint
                {
                    T = t,
                    X = x,
                    Y = y,
                    Z = z + (effects.SpinDrift ? Stability.SpinDrift(sg, shot.Twist, t) : 0),
                    Vx = vx,
                    Vy = vy,
                    Vz = vz,
                    Speed = speed,
                    Mach = m,
                    Energy = projectile.KineticEnergy(speed),
                });
            }

            Rng? dispersionRng = seeded && effects.TransonicDispersion ? new Rng(Rng.HashSeed(options.Seed!.Value, 0x7472)) : null;
            double sqrtDt = Math.Sqrt(dt);

            double time = 0;
            double mach0 = Accel(0, x, y, vx, vy, vz);
            Record(0, mach0);
            var termination = Termination.Time;

            while (true)
            {
                if (options.Integrator == Integrator.Euler)
                {
                    // Semi-implicit (symplectic) Euler.
                    Accel(time, x, y, vx, vy, vz);
                    vx += ax * dt;
                    vy += ay * dt;
                    vz += az2 * dt;
                    x += vx * dt;
                    y += vy * dt;
                    z += vz * dt;
                }
                else
                {
                    Accel(time, x, y, vx, vy, vz);
                    double k1x = vx, k1y = vy, k1z = vz;
                    double a1x = ax, a1y = ay, a1z = az2;
                    double h2 = dt / 2;
                    Accel(time + h2, x + k1x * h2, y + k1y * h2, vx + a1x * h2, vy + a1y * h2, vz + a1z * h2);
                    double k2x = vx + a1x * h2, k2y = vy + a1y * h2, k2z = vz + a1z * h2;
                    double a2x = ax, a2y = ay, a2z = az2;
                    Accel(time + h2, x + k2x * h2, y + k2y * h2, vx + a2x * h2, vy + a2y * h2, vz + a2z * h2);
                    double k3x = vx + a2x * h2, k3y = vy + a2y * h2, k3z = vz + a2z * h2;
                    double a3x = ax, a3y = ay, a3z = az2;
                    Accel(time + dt, x + k3x * dt, y + k3y * dt, vx + a3x * dt, vy + a3y * dt, vz + a3z * dt);
                    double k4x = vx + a3x * dt, k4y = vy + a3y * dt, k4z = vz + a3z * dt;
                    double a4x = ax, a4y = ay, a4z = az2;
                    double s = dt / 6;
                    x += s * (k1x + 2 * k2x + 2 * k3x + k4x);
                    y += s * (k1y + 2 * k2y + 2 * k3y + k4y);
                    z += s * (k1z + 2 * k2z + 2 * k3z + k4z);
                    vx += s * (a1x + 2 * a2x + 2 * a3x + a4x);
                    vy += s * (a1y + 2 * a2y + 2 * a3y + a4y);
                    vz += s * (a1z + 2 * a2z + 2 * a3z + a4z);
                }
                time += dt;
                double mach = Accel(time, x, y, vx, vy, vz);

                if (dispersionRng != null && mach < transonic.MachThreshold)
                {
                    // Random walk of the velocity direction in the two axes perpendicular to flight.
                    double speed = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    double h = Math.Sqrt(vx * vx + vz * vz);
                    double dPitch = transonic.Sigma * sqrtDt * dispersionRng.Normal();
                    double dYaw = transonic.Sigma * sqrtDt * dispersionRng.Normal();
                    double px = -vx * vy / (speed * h);
                    double py = h / speed;
                    double pz = -vz * vy / (speed * h);
                    double yx = -vz / h;
                    double yz = vx / h;
                    vx += speed * (dPitch * px + dYaw * yx);
                    vy += speed * dPitch * py;
                    vz += speed * (dPitch * pz + dYaw * yz);
                }

                Record(time, mach);

                if (x >= options.MaxRange)
                {
                    termination = Termination.Range;
                    break;
                }
                if (time >= options.MaxTime)
                {
                    termination = Termination.Time;
                    break;
                }
                if (y < -options.MaxDrop)
                {
                    termination = Termination.Drop;
                    break;
                }
                if (points[points.Count - 1].Speed < options.MinSpeed)
                {
                    termination = Termination.Speed;
                    break;
                }
            }

            traj.MuzzleVelocity = mv;
            traj.AerodynamicJump = jump;
            traj.Termination = termination;
            return traj;
        }
    }
}
