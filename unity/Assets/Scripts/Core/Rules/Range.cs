/* The practice range's targets: what a real range sets out, as flat hit shapes the round is tested against. */
using System;
using System.Collections.Generic;
using SniperDuel.Ballistics;

namespace SniperDuel.Rules
{
    /// <summary>What a target is made of: how it sounds when struck and what it does.</summary>
    public enum MarkMaterial
    {
        Steel,
        Paper,
        Glass,
        Tin,
        Clay,
        Rubber,
        Water,
        Fruit,
    }

    /// <summary>What a hit does to the target.</summary>
    public enum MarkReaction
    {
        /// <summary>Hangs on chains and swings back, still there to shoot again.</summary>
        Swing,
        /// <summary>Spins round its axle and settles upright.</summary>
        Spin,
        /// <summary>A dueling-tree paddle: swings over to the other side of the post.</summary>
        Flip,
        /// <summary>Knocked over off its stand (poppers, steel animals): gone till the field is reset.</summary>
        Fall,
        /// <summary>Shatters or bursts (bottles, clays, balloons, jugs, fruit): gone.</summary>
        Break,
        /// <summary>Thrown off its perch (cans): gone.</summary>
        Knock,
        /// <summary>Paper: a hole, and a score.</summary>
        Hole,
    }

    public enum PartShape
    {
        Rect,
        Disc,
        Poly,
    }

    /// <summary>
    /// One flat piece of a target, in its face plane: (u right, v up as the shooter sees it,
    /// metres from the mark's origin). Round things (bottles, cans, balloons) are drawn so their
    /// outline seen from the firing line is exactly this shape.
    /// </summary>
    public sealed class MarkPart
    {
        public PartShape Shape;
        public double U, V;
        /// <summary>Rect width and height; a disc's diameter is W.</summary>
        public double W, H;
        /// <summary>Poly outline as u0, v0, u1, v1 ... about (U, V).</summary>
        public double[]? Outline;
        /// <summary>A scored bullseye: ten rings over the disc, the X inside the ten.</summary>
        public bool Rings;

        public static MarkPart Rect(double u, double v, double w, double h) => new MarkPart { Shape = PartShape.Rect, U = u, V = v, W = w, H = h };
        public static MarkPart Disc(double u, double v, double d, bool rings = false) => new MarkPart { Shape = PartShape.Disc, U = u, V = v, W = d, H = d, Rings = rings };

        public static MarkPart Poly(double u, double v, double[] outline)
        {
            double lo = double.MaxValue, hi = double.MinValue, bot = double.MaxValue, top = double.MinValue;
            for (int i = 0; i < outline.Length; i += 2)
            {
                lo = Math.Min(lo, outline[i]);
                hi = Math.Max(hi, outline[i]);
                bot = Math.Min(bot, outline[i + 1]);
                top = Math.Max(top, outline[i + 1]);
            }
            return new MarkPart { Shape = PartShape.Poly, U = u, V = v, W = hi - lo, H = top - bot, Outline = outline };
        }

        /// <summary>True when the local point (du, dv) from the part's centre is on it.</summary>
        public bool Contains(double du, double dv)
        {
            switch (Shape)
            {
                case PartShape.Rect: return Math.Abs(du) <= W / 2 && Math.Abs(dv) <= H / 2;
                case PartShape.Disc: return du * du + dv * dv <= W * W / 4;
                default:
                    var o = Outline!;
                    bool inside = false;
                    for (int i = 0, j = o.Length - 2; i < o.Length; j = i, i += 2)
                    {
                        double xi = o[i], yi = o[i + 1], xj = o[j], yj = o[j + 1];
                        if ((yi > dv) != (yj > dv) && du < (xj - xi) * (dv - yi) / (yj - yi) + xi) inside = !inside;
                    }
                    return inside;
            }
        }

        /// <summary>Lowest and highest v and u the part covers (its box in the face plane).</summary>
        public (double u0, double u1, double v0, double v1) Bounds()
        {
            if (Shape != PartShape.Poly) return (U - W / 2, U + W / 2, V - H / 2, V + H / 2);
            var o = Outline!;
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            for (int i = 0; i < o.Length; i += 2)
            {
                u0 = Math.Min(u0, o[i]);
                u1 = Math.Max(u1, o[i]);
                v0 = Math.Min(v0, o[i + 1]);
                v1 = Math.Max(v1, o[i + 1]);
            }
            return (U + u0, U + u1, V + v0, V + v1);
        }
    }

    /// <summary>One target on the range: a steel plate, a paper, a bottle, a can.</summary>
    public sealed class Mark
    {
        public string Id = "";
        /// <summary>What it is ("gong", "bottle", "ram" ...), for how it is drawn.</summary>
        public string Kind = "";
        /// <summary>The stand it sits on (index into the field's stations).</summary>
        public int Station;
        public MarkMaterial Material;
        public MarkReaction Reaction;
        /// <summary>Origin of the parts, rules frame: the point (u 0, v 0) of the face plane.</summary>
        public Vec3 Position;
        /// <summary>The face looks along Forward(FacingYaw), back at the firing line.</summary>
        public double FacingYaw;
        public List<MarkPart> Parts = new List<MarkPart>();
        /// <summary>A mover: runs back and forth along its face, Rail metres either side of the origin.</summary>
        public double Rail, Speed, Phase;
        /// <summary>Height (v) of what it turns about when hit: the chains' top, the axle, the hinge at its foot.</summary>
        public double PivotV;
        /// <summary>A dueling-tree paddle swung over to the far side of its post.</summary>
        public bool Mirrored;
        /// <summary>Struck at least once this field.</summary>
        public bool Hit;
        /// <summary>Knocked down, broken or thrown off: nothing left to shoot.</summary>
        public bool Gone;

        /// <summary>Where along its rail a mover is at range clock `t` (0 for everything else).</summary>
        public double Offset(double t)
        {
            if (Rail <= 0 || Speed <= 0) return 0;
            double run = 2 * Rail;
            double along = (t * Speed + Phase) % (2 * run);
            if (along < 0) along += 2 * run;
            return -Rail + (run - Math.Abs(along - run));
        }

        /// <summary>The part under local (u, v) at clock t, or -1.</summary>
        public int PartAt(double u, double v, double t)
        {
            u -= Offset(t);
            if (Mirrored) u = -u;
            for (int k = 0; k < Parts.Count; k++)
                if (Parts[k].Contains(u - Parts[k].U, v - Parts[k].V)) return k;
            return -1;
        }
    }

    /// <summary>A target stand (a frame, a table, a row of posts) and where it stands.</summary>
    public sealed class Station
    {
        public string Kind = "";
        /// <summary>On the ground under the stand's middle, rules frame.</summary>
        public Vec3 Position;
        public double FacingYaw;
        public double Ground;
    }

    /// <summary>A laid-out range: the targets, their stands, and the solid parts of the stands.</summary>
    public sealed class RangeField
    {
        public List<Mark> Marks = new List<Mark>();
        public List<Station> Stations = new List<Station>();
        /// <summary>Solid parts of the stands (tables, drums, posts, berms): a round stops on them.</summary>
        public List<Box> Props = new List<Box>();

        public int Count => Marks.Count;

        public int HitCount
        {
            get
            {
                int n = 0;
                foreach (var m in Marks) if (m.Hit) n++;
                return n;
            }
        }

        public Mark? Find(string id) => Marks.Find(m => m.Id == id);
    }

    /// <summary>Where a round met a target.</summary>
    public sealed class MarkHit
    {
        public Mark Mark = null!;
        public int Part;
        /// <summary>Strike point in the face plane, from the mark's origin as it stood at that moment.</summary>
        public double U, V;
        /// <summary>Paper bullseye score: 10 down to 1 (11 for the X), 0 off the rings.</summary>
        public int Score;
    }

    public static class RangeHits
    {
        /// <summary>Firing-line-facing yaw for a target at `at` seen from `eye`.</summary>
        public static double Facing(Vec3 at, Vec3 eye) => Math.Atan2(eye.X - at.X, -(eye.Z - at.Z));

        static Vec3 Lerp(Vec3 a, Vec3 b, double f) => new Vec3(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Z + (b.Z - a.Z) * f);

        /// <summary>The world point of local (u, v) on a mark at clock t.</summary>
        public static Vec3 PointOn(Mark m, double u, double v, double t = 0)
        {
            var r = World.RightOf(m.FacingYaw);
            double along = u + m.Offset(t);
            return new Vec3(m.Position.X - along * r.X, m.Position.Y + v, m.Position.Z - along * r.Z);
        }

        /// <summary>Local (u, v) of a world point in a mark's face plane (u shooter's right).</summary>
        public static (double u, double v) Local(Mark m, Vec3 p)
        {
            var r = World.RightOf(m.FacingYaw);
            double u = -((p.X - m.Position.X) * r.X + (p.Z - m.Position.Z) * r.Z);
            return (u, p.Y - m.Position.Y);
        }

        /// <summary>
        /// A practice shot down the range: the round flies as in a duel (same rifle, dials, wind and
        /// air) and strikes the first target, stand, piece of cover or patch of ground on its path.
        /// `clock` is the range clock at the trigger press (movers run on it). Nobody is hurt, so the
        /// verdict is always a miss; a target strike is an impact of kind Target naming the mark.
        /// </summary>
        public static (ShotOutcome outcome, MarkHit? hit) Shoot(ShotRequest req, RangeField field, double clock, GameConfig cfg)
        {
            var (trajectory, eye, seed) = Combat.Fly(req, cfg);
            var (impact, hit) = Resolve(trajectory, eye, req.Input.Yaw, field, req.Map, clock, cfg);
            return (new ShotOutcome { Verdict = Verdict.Miss, Zone = impact.Zone, Impact = impact, Trajectory = trajectory, Eye = eye, Yaw = req.Input.Yaw, Seed = seed }, hit);
        }

        public static (Impact impact, MarkHit? hit) Resolve(Trajectory traj, Vec3 eye, double yaw, RangeField field, DuelMap map, double clock, GameConfig cfg)
        {
            var obstacles = new List<Box>(map.Cover);
            obstacles.AddRange(field.Props);
            var env = Hits.ResolveImpact(traj, eye, yaw, Array.Empty<Silhouette>(), map.Ground, obstacles, Penetration.From(cfg.SoftCover));
            var pts = traj.Points;
            var prev = World.ShotToWorld(pts[0], eye, yaw);
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                if (a.T > env.T) break;
                var cur = World.ShotToWorld(b, eye, yaw);
                double bestF = 2;
                MarkHit? best = null;
                Vec3 bestP = default;
                foreach (var m in field.Marks)
                {
                    if (m.Gone) continue;
                    var n = World.Forward(m.FacingYaw);
                    double da = (prev.X - m.Position.X) * n.X + (prev.Z - m.Position.Z) * n.Z;
                    double db = (cur.X - m.Position.X) * n.X + (cur.Z - m.Position.Z) * n.Z;
                    if (!(da > 0 && db <= 0)) continue;
                    double f = da / (da - db);
                    if (f >= bestF) continue;
                    var p = Lerp(prev, cur, f);
                    double t = a.T + (b.T - a.T) * f;
                    var (u, v) = Local(m, p);
                    int part = m.PartAt(u, v, clock + t);
                    if (part < 0) continue;
                    bestF = f;
                    bestP = p;
                    best = new MarkHit { Mark = m, Part = part, U = u - m.Offset(clock + t), V = v };
                }
                if (best != null)
                {
                    double t = a.T + (b.T - a.T) * bestF;
                    if (t > env.T) break;
                    best.Score = Score(best);
                    var impact = new Impact
                    {
                        Kind = ImpactKind.Target,
                        TargetId = best.Mark.Id,
                        Point = bestP,
                        T = t,
                        Speed = a.Speed + (b.Speed - a.Speed) * bestF,
                        Energy = a.Energy + (b.Energy - a.Energy) * bestF,
                        Local = new PlateOffset { U = best.U, V = best.V },
                    };
                    return (impact, best);
                }
                prev = cur;
            }
            return (env, null);
        }

        /// <summary>Bullseye rings: ten bands over the disc's radius, the X the inner half of the ten.</summary>
        static int Score(MarkHit h)
        {
            var p = h.Mark.Parts[h.Part];
            if (!p.Rings) return 0;
            double u = h.Mark.Mirrored ? -h.U : h.U;
            double r = Math.Sqrt((u - p.U) * (u - p.U) + (h.V - p.V) * (h.V - p.V));
            double band = p.W / 2 / 10;
            if (r <= band / 2) return 11;
            return Math.Max(0, 10 - (int)Math.Floor(r / band));
        }

        /// <summary>Laser range to the first standing target or stand along the aim, or null.</summary>
        public static double? Rangefind(Vec3 eye, Aim aim, RangeField field, double clock, double maxRange = 3000)
        {
            var f = World.Forward(aim.Yaw);
            double cp = Math.Cos(aim.Pitch);
            var dir = new Vec3(f.X * cp, Math.Sin(aim.Pitch), f.Z * cp);
            double? best = null;
            foreach (var m in field.Marks)
            {
                if (m.Gone) continue;
                var n = World.Forward(m.FacingYaw);
                double denom = dir.X * n.X + dir.Z * n.Z;
                if (denom >= 0) continue;
                double d = ((m.Position.X - eye.X) * n.X + (m.Position.Z - eye.Z) * n.Z) / denom;
                if (d <= 0 || d > maxRange) continue;
                var p = new Vec3(eye.X + dir.X * d, eye.Y + dir.Y * d, eye.Z + dir.Z * d);
                var (u, v) = Local(m, p);
                if (m.PartAt(u, v, clock) >= 0 && (best == null || d < best.Value)) best = d;
            }
            var end = new Vec3(eye.X + dir.X * maxRange, eye.Y + dir.Y * maxRange, eye.Z + dir.Z * maxRange);
            foreach (var box in field.Props)
            {
                double? fb = Geometry.SegmentBoxEntry(eye, end, box);
                if (fb.HasValue && fb.Value > 0 && (best == null || fb.Value * maxRange < best.Value)) best = fb.Value * maxRange;
            }
            return best;
        }

        /// <summary>Range to the standing target nearest the line of sight (for the side focus).</summary>
        public static double? RangeNear(Vec3 eye, double yaw, double pitch, RangeField field)
        {
            double best = double.MaxValue;
            double? range = null;
            foreach (var m in field.Marks)
            {
                if (m.Gone) continue;
                double dx = m.Position.X - eye.X, dz = m.Position.Z - eye.Z, dy = m.Position.Y - eye.Y;
                double flat = Math.Sqrt(dx * dx + dz * dz);
                double by = Math.Atan2(dx, -dz) - yaw, bp = Math.Atan2(dy, flat) - pitch;
                double off = by * by + bp * bp;
                if (off < best)
                {
                    best = off;
                    range = flat;
                }
            }
            return range;
        }
    }
}
