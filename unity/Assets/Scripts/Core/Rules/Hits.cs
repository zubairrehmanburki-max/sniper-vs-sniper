using System;
using System.Collections.Generic;
using SniperDuel.Ballistics;

namespace SniperDuel.Rules
{
    public enum HitZone
    {
        Head,
        Chest,
        Body,
    }

    /// <summary>Ground height at a world (x, z).</summary>
    public delegate double Ground(double x, double z);

    /// <summary>A human-size silhouette (steel plate on the range, the frozen player in a duel).</summary>
    public sealed class Silhouette
    {
        public string Id = "";
        /// <summary>Centre of the base, on the ground, world frame.</summary>
        public Vec3 Position;
        /// <summary>Yaw the silhouette faces (its front looks along Forward(FacingYaw)).</summary>
        public double FacingYaw;
        public double Width;
        public double Height;
        /// <summary>Head height; the head is 40% of the body width.</summary>
        public double Head;
    }

    public enum ImpactKind
    {
        Target,
        Ground,
        Cover,
        None,
    }

    public struct PlateOffset
    {
        public double U;
        public double V;
    }

    public sealed class Impact
    {
        public ImpactKind Kind;
        public string? TargetId;
        public string? CoverId;
        public HitZone? Zone;
        /// <summary>World point of impact (last integrated point for None).</summary>
        public Vec3 Point;
        /// <summary>Time of flight, s.</summary>
        public double T;
        public double Speed;
        public double Energy;
        /// <summary>For target hits: offset from the plate centre in the plate plane, m (right, up).</summary>
        public PlateOffset? Local;
        /// <summary>Soft cover the bullet went through on the way, in order.</summary>
        public List<string>? Penetrated;

        public Impact Clone()
        {
            var c = (Impact)MemberwiseClone();
            if (Penetrated != null) c.Penetrated = new List<string>(Penetrated);
            return c;
        }
    }

    /// <summary>How bullets get through soft cover (GAME_PLAN damage model).</summary>
    public sealed class Penetration
    {
        /// <summary>Energy the bullet needs at the cover to go through, J.</summary>
        public double MinEnergyJ;
        /// <summary>Fraction of energy left after each layer.</summary>
        public double EnergyKeep;
        /// <summary>Most soft layers one bullet can pass.</summary>
        public int MaxLayers;

        public static Penetration From(SoftCoverConfig c) => new Penetration { MinEnergyJ = c.MinEnergyJ, EnergyKeep = c.EnergyKeep, MaxLayers = c.MaxLayers };
    }

    public struct MissCallResult
    {
        public double UpMrad;
        public double RightMrad;
    }

    public static class Hits
    {
        public static readonly Ground FlatGround = (x, z) => 0;

        const double ChestDepth = 0.35; // m of torso below the head that counts as chest

        /// <summary>Which zone of a silhouette a local (u right, h up from base) point falls in, if any.</summary>
        public static HitZone? SilhouetteZone(Silhouette s, double u, double h)
        {
            if (h < 0 || h > s.Height) return null;
            double headBase = s.Height - s.Head;
            if (h >= headBase) return Math.Abs(u) <= 0.2 * s.Width ? HitZone.Head : (HitZone?)null;
            if (Math.Abs(u) > s.Width / 2) return null;
            return h >= headBase - ChestDepth ? HitZone.Chest : HitZone.Body;
        }

        static Vec3 LerpPoint(Vec3 a, Vec3 b, double f) => new Vec3(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Z + (b.Z - a.Z) * f);

        /// <summary>
        /// Walks the trajectory and returns the first thing the bullet meets: a silhouette or the ground.
        /// This is the authoritative hit test (server) and the preview/killcam hit test (client).
        /// </summary>
        public static Impact ResolveImpact(
            Trajectory traj,
            Vec3 eye,
            double yaw,
            IReadOnlyList<Silhouette> targets,
            Ground? ground = null,
            IReadOnlyList<Box>? obstacles = null,
            Penetration? penetration = null)
        {
            ground ??= FlatGround;
            obstacles ??= Array.Empty<Box>();
            var pts = traj.Points;
            // After going through soft cover the bullet keeps its path (the small deflection is ignored) but
            // carries less energy; speed scales with the square root.
            var passed = new HashSet<string>();
            var penetrated = new List<string>();
            double keep = 1;
            Impact Scaled(Impact impact)
            {
                if (penetrated.Count == 0) return impact;
                var c = impact.Clone();
                c.Energy = impact.Energy * keep;
                c.Speed = impact.Speed * Math.Sqrt(keep);
                c.Penetrated = new List<string>(penetrated);
                return c;
            }
            var frames = new (Silhouette s, Vec3 n, Vec3 r)[targets.Count];
            for (int k = 0; k < targets.Count; k++) frames[k] = (targets[k], World.Forward(targets[k].FacingYaw), World.RightOf(targets[k].FacingYaw));
            var prev = World.ShotToWorld(pts[0], eye, yaw);
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                var cur = World.ShotToWorld(b, eye, yaw);
                double bestF = 0;
                Impact? best = null;

                foreach (var (s, n, r) in frames)
                {
                    double da = (prev.X - s.Position.X) * n.X + (prev.Z - s.Position.Z) * n.Z;
                    double db = (cur.X - s.Position.X) * n.X + (cur.Z - s.Position.Z) * n.Z;
                    if (da > 0 && db <= 0)
                    {
                        double f = da / (da - db);
                        var p = LerpPoint(prev, cur, f);
                        double u = (p.X - s.Position.X) * r.X + (p.Z - s.Position.Z) * r.Z;
                        double h = p.Y - s.Position.Y;
                        var zone = SilhouetteZone(s, -u, h); // facing the shooter, the plate's right is the shooter's left
                        if (zone.HasValue && (best == null || f < bestF))
                        {
                            bestF = f;
                            best = new Impact
                            {
                                Kind = ImpactKind.Target,
                                TargetId = s.Id,
                                Zone = zone,
                                Point = p,
                                T = a.T + (b.T - a.T) * f,
                                Speed = a.Speed + (b.Speed - a.Speed) * f,
                                Energy = a.Energy + (b.Energy - a.Energy) * f,
                                Local = new PlateOffset { U = -u, V = h - (s.Height - s.Head - ChestDepth / 2) },
                            };
                        }
                    }
                }

                double ga = prev.Y - ground(prev.X, prev.Z);
                double gb = cur.Y - ground(cur.X, cur.Z);
                if (ga > 0 && gb <= 0)
                {
                    double f = ga / (ga - gb);
                    if (best == null || f < bestF)
                    {
                        bestF = f;
                        best = new Impact
                        {
                            Kind = ImpactKind.Ground,
                            Point = LerpPoint(prev, cur, f),
                            T = a.T + (b.T - a.T) * f,
                            Speed = a.Speed + (b.Speed - a.Speed) * f,
                            Energy = a.Energy + (b.Energy - a.Energy) * f,
                        };
                    }
                }
                Box? bestBox = null;
                foreach (var box in obstacles)
                {
                    if (passed.Contains(box.Id)) continue;
                    double? fb = Geometry.SegmentBoxEntry(prev, cur, box);
                    if (fb.HasValue && (best == null || fb.Value < bestF))
                    {
                        double f = fb.Value;
                        bestBox = box;
                        bestF = f;
                        best = new Impact
                        {
                            Kind = ImpactKind.Cover,
                            CoverId = box.Id,
                            Point = LerpPoint(prev, cur, f),
                            T = a.T + (b.T - a.T) * f,
                            Speed = a.Speed + (b.Speed - a.Speed) * f,
                            Energy = a.Energy + (b.Energy - a.Energy) * f,
                        };
                    }
                }

                if (best != null && best.Kind == ImpactKind.Cover && bestBox != null && bestBox.Material == CoverMaterial.Soft && penetration != null)
                {
                    double energy = best.Energy * keep;
                    if (energy >= penetration.MinEnergyJ && penetrated.Count < penetration.MaxLayers)
                    {
                        passed.Add(bestBox.Id);
                        penetrated.Add(bestBox.Id);
                        keep *= penetration.EnergyKeep;
                        i--; // look at this segment again, beyond the layer it went through
                        continue;
                    }
                }
                if (best != null) return Scaled(best);
                prev = cur;
            }
            var last = pts[pts.Count - 1];
            return Scaled(new Impact { Kind = ImpactKind.None, Point = prev, T = last.T, Speed = last.Speed, Energy = last.Energy });
        }

        /// <summary>
        /// Where the bullet passed a silhouette's plane relative to its chest centre, as the shooter sees it,
        /// in mrad (positive = high / right). The spotter's call after a shot. Null if it never got there.
        /// </summary>
        public static MissCallResult? MissCall(Trajectory traj, Vec3 eye, double yaw, Silhouette s)
        {
            var n = World.Forward(s.FacingYaw);
            var r = World.RightOf(s.FacingYaw);
            var pts = traj.Points;
            var prev = World.ShotToWorld(pts[0], eye, yaw);
            for (int i = 1; i < pts.Count; i++)
            {
                var cur = World.ShotToWorld(pts[i], eye, yaw);
                double da = (prev.X - s.Position.X) * n.X + (prev.Z - s.Position.Z) * n.Z;
                double db = (cur.X - s.Position.X) * n.X + (cur.Z - s.Position.Z) * n.Z;
                if (da > 0 && db <= 0)
                {
                    var p = LerpPoint(prev, cur, da / (da - db));
                    double centreY = s.Position.Y + s.Height - s.Head - ChestDepth / 2;
                    double dist = JsMath.Hypot(s.Position.X - eye.X, centreY - eye.Y, s.Position.Z - eye.Z);
                    double u = -((p.X - s.Position.X) * r.X + (p.Z - s.Position.Z) * r.Z);
                    return new MissCallResult { UpMrad = ((p.Y - centreY) / dist) * 1000, RightMrad = (u / dist) * 1000 };
                }
                prev = cur;
            }
            return null;
        }

        /// <summary>Laser rangefinder: distance along the aim ray to the first silhouette or the ground.</summary>
        public static double? Rangefind(
            Vec3 eye,
            Aim aim,
            IReadOnlyList<Silhouette> targets,
            Ground? ground = null,
            double maxRange = 3000,
            IReadOnlyList<Box>? obstacles = null)
        {
            ground ??= FlatGround;
            obstacles ??= Array.Empty<Box>();
            var f = World.Forward(aim.Yaw);
            double cp = Math.Cos(aim.Pitch);
            var dir = new Vec3(f.X * cp, Math.Sin(aim.Pitch), f.Z * cp);
            double? best = null;
            foreach (var s in targets)
            {
                var n = World.Forward(s.FacingYaw);
                double denom = dir.X * n.X + dir.Z * n.Z;
                if (denom >= 0) continue;
                double d = ((s.Position.X - eye.X) * n.X + (s.Position.Z - eye.Z) * n.Z) / denom;
                if (d <= 0) continue;
                var p = new Vec3(eye.X + dir.X * d, eye.Y + dir.Y * d, eye.Z + dir.Z * d);
                var r = World.RightOf(s.FacingYaw);
                double u = -((p.X - s.Position.X) * r.X + (p.Z - s.Position.Z) * r.Z);
                if (SilhouetteZone(s, u, p.Y - s.Position.Y).HasValue && (best == null || d < best.Value)) best = d;
            }
            foreach (var box in obstacles)
            {
                var end = new Vec3(eye.X + dir.X * maxRange, eye.Y + dir.Y * maxRange, eye.Z + dir.Z * maxRange);
                double? fb = Geometry.SegmentBoxEntry(eye, end, box);
                if (fb.HasValue && fb.Value > 0 && (best == null || fb.Value * maxRange < best.Value)) best = fb.Value * maxRange;
            }
            // March the ground.
            const double step = 0.5;
            for (double d = step; d < (best ?? maxRange); d += step)
            {
                double x = eye.X + dir.X * d;
                double z = eye.Z + dir.Z * d;
                if (eye.Y + dir.Y * d <= ground(x, z)) return d;
            }
            return best;
        }
    }
}
