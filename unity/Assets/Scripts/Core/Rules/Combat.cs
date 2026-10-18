/* Turning a fire input into a verdict: the authoritative resolution shared by hotseat, bot and server. */
using System;
using System.Linq;
using SniperDuel.Ballistics;
using Environment = SniperDuel.Ballistics.Environment;

namespace SniperDuel.Rules
{
    /// <summary>What a client sends when it fires. Never the result: the referee computes that.</summary>
    public sealed class FireInput
    {
        /// <summary>True muzzle axis (sway included), rad.</summary>
        public double Yaw;
        public double Pitch;
        public double DialElevationMrad;
        public double DialWindageMrad;
        public double Cant;

        public FireInput Clone() => (FireInput)MemberwiseClone();
    }

    public sealed class Conditions
    {
        public Atmosphere Atmosphere = Atmosphere.Icao;
        public LaneWind Wind = new LaneWind();
        public double Latitude;
    }

    public enum Verdict
    {
        Kill,
        Wound,
        Miss,
    }

    public sealed class ShotOutcome
    {
        public Verdict Verdict;
        public HitZone? Zone;
        public Impact Impact = null!;
        /// <summary>Bullet passed within the near-miss radius while supersonic: the target hears the snap.</summary>
        public bool NearMiss;
        /// <summary>Where the bullet passed the target plane relative to its chest, mrad from the shooter (miss diagnostics).</summary>
        public MissCallResult? Call;
        public Trajectory Trajectory = null!;
        public Vec3 Eye;
        public double Yaw;
        public uint Seed;
    }

    public sealed class ShotTarget
    {
        public string Id = "";
        public Side Side;
        public Vec3 Position;
        public Stance Stance;
        public int Wounds;
    }

    public sealed class ShotRequest
    {
        public DuelMap Map = null!;
        public Conditions Conditions = null!;
        public string RifleId = "";
        public Vec3 ShooterPosition;
        public Stance ShooterStance;
        public ShotTarget Target = null!;
        public FireInput Input = null!;
        /// <summary>Match seed and shot counter, for muzzle-velocity and transonic variation.</summary>
        public uint Seed;
        public int ShotIndex;
        /// <summary>Match clock at the trigger press (gust phase).</summary>
        public double FireTime;
        public bool ColdBore;
    }

    public static class Combat
    {
        public static Silhouette SilhouetteFor(string id, Vec3 position, Stance stance, double facingYaw, GameConfig cfg)
        {
            var s = cfg.Player.Silhouette[stance];
            return new Silhouette { Id = id, Position = position, FacingYaw = facingYaw, Width = s.WidthM, Height = s.HeightM, Head = s.HeadM };
        }

        /// <summary>Eye (scope) position for a player standing at `position` in a stance.</summary>
        public static Vec3 EyeOf(Vec3 position, Stance stance, GameConfig cfg)
        {
            var s = cfg.Player.Silhouette[stance];
            return new Vec3(position.X, position.Y + s.HeightM - s.HeadM / 2, position.Z);
        }

        /// <summary>Chest centre of a silhouette (what a careful shooter aims at).</summary>
        public static Vec3 ChestOf(Silhouette s) => new Vec3(s.Position.X, s.Position.Y + s.Height - s.Head - 0.175, s.Position.Z);

        public static double FacingYaw(Side side) => side == Side.A ? 0 : Math.PI;

        /// <summary>Flies the round for a shot: its trajectory, the shooter's eye it starts from, and the shot's seed.</summary>
        public static (Trajectory trajectory, Vec3 eye, uint seed) Fly(ShotRequest req, GameConfig cfg)
        {
            var rifle = cfg.RifleById(req.RifleId);
            var b = cfg.Ballistics;
            var eye = EyeOf(req.ShooterPosition, req.ShooterStance, cfg);
            var cold = req.ColdBore ? rifle.ColdBoreShiftMrad : new ColdBoreShift();
            var shot = RifleSetup.RifleShot(rifle, cfg);
            shot.LosElevation = req.Input.Pitch;
            shot.Azimuth = req.Input.Yaw;
            shot.Cant = req.Input.Cant;
            shot.DialElevation = (req.Input.DialElevationMrad + cold.Elevation) / 1000;
            shot.DialWindage = (req.Input.DialWindageMrad + cold.Windage) / 1000;
            var env = new Environment
            {
                Atmosphere = req.Conditions.Atmosphere,
                Wind = World.ShotWindField(req.Conditions.Wind, eye, req.Input.Yaw),
                Latitude = req.Conditions.Latitude,
            };
            uint seed = Rng.HashSeed(req.Seed, req.ShotIndex);
            var trajectory = Solver.Simulate(shot, env, new SolveOptions
            {
                MaxRange = req.Map.LaneLength + 300,
                Dt = b.DtSeconds,
                Integrator = b.Integrator,
                Seed = seed,
                FireTime = req.FireTime,
                Effects = b.Effects.ToEffects(),
                Transonic = new TransonicConfig { MachThreshold = b.Transonic.MachThreshold, Sigma = b.Transonic.SigmaRadPerSqrtS },
                MaxTime = 5,
            });
            return (trajectory, eye, seed);
        }

        public static ShotOutcome ResolveShot(ShotRequest req, GameConfig cfg)
        {
            var (trajectory, eye, seed) = Fly(req, cfg);
            var target = SilhouetteFor(req.Target.Id, req.Target.Position, req.Target.Stance, FacingYaw(req.Target.Side), cfg);
            var impact = Hits.ResolveImpact(trajectory, eye, req.Input.Yaw, new[] { target }, req.Map.Ground, req.Map.Cover, Penetration.From(cfg.SoftCover));
            var call = Hits.MissCall(trajectory, eye, req.Input.Yaw, target);

            var verdict = Verdict.Miss;
            HitZone? zone = null;
            if (impact.Kind == ImpactKind.Target)
            {
                zone = impact.Zone!.Value;
                bool lethal = zone == HitZone.Head || (zone == HitZone.Chest && impact.Energy >= cfg.Rules.ChestKillEnergyJ);
                verdict = lethal || req.Target.Wounds + 1 >= cfg.Rules.WoundsToDown ? Verdict.Kill : Verdict.Wound;
            }

            bool nearMiss = false;
            var chest = ChestOf(target);
            double d = JsMath.Hypot(chest.X - eye.X, chest.Y - eye.Y, chest.Z - eye.Z);
            bool reachedTarget = JsMath.Hypot(impact.Point.X - eye.X, impact.Point.Z - eye.Z) >= JsMath.Hypot(chest.X - eye.X, chest.Z - eye.Z) - 0.5;
            if (verdict == Verdict.Miss && call.HasValue && reachedTarget)
            {
                double offsetM = (JsMath.Hypot(call.Value.UpMrad, call.Value.RightMrad) / 1000) * d;
                double passingMach = 0;
                foreach (var p in trajectory.Points)
                {
                    if (p.X >= d * 0.98)
                    {
                        passingMach = p.Mach;
                        break;
                    }
                }
                nearMiss = offsetM <= cfg.Rules.NearMissRadiusM && passingMach > 1;
            }
            return new ShotOutcome { Verdict = verdict, Zone = zone, Impact = impact, NearMiss = nearMiss, Call = call, Trajectory = trajectory, Eye = eye, Yaw = req.Input.Yaw, Seed = seed };
        }
    }
}
