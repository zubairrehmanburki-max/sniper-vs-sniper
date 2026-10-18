/* Laying out the practice range: what a real range sets out, at ranges nobody tells you, every target in plain view. */
using System;
using System.Collections.Generic;
using SniperDuel.Ballistics;

namespace SniperDuel.Rules
{
    public static class RangeLayout
    {
        /// <summary>A stand to set out, between these ranges from the shooter (m).</summary>
        sealed class Spec
        {
            public string Kind;
            public double From, To;
            public Spec(string kind, double from, double to)
            {
                Kind = kind;
                From = from;
                To = to;
            }
        }

        /// <summary>
        /// The range's stands. Roughly how a real range spreads them: plinking (bottles, cans,
        /// balloons, jugs, fruit) nearest, paper and the small reactive steel at the middle
        /// distances, the metallic-silhouette animals at their match distances (chickens about 200 m,
        /// pigs 300, turkeys 385, rams 500), hanging steel and the big gong far out. Nothing comes
        /// nearer than about 160 m: lying on the Training Lane's firing bench, the ground closer in
        /// is dead ground below the bench's edge. The narrow bands go first.
        /// </summary>
        static readonly Spec[] Specs =
        {
            new Spec("mover", 230, 400),
            new Spec("pigs", 270, 330),
            new Spec("turkeys", 355, 415),
            new Spec("rams", 465, 525),
            new Spec("chickens", 190, 230),
            new Spec("bottles", 160, 230),
            new Spec("cans", 165, 240),
            new Spec("balloons", 170, 260),
            new Spec("table", 170, 260),
            new Spec("paper", 165, 280),
            new Spec("clays", 180, 300),
            new Spec("cardboard", 190, 320),
            new Spec("spinner", 190, 320),
            new Spec("tree", 200, 330),
            new Spec("paper", 220, 360),
            new Spec("poppers", 210, 360),
            new Spec("kyl", 230, 400),
            new Spec("prairie", 300, 460),
            new Spec("gongs", 260, 470),
            new Spec("ipsc", 300, 540),
            new Spec("ipsc", 380, 560),
            new Spec("biggong", 500, 575),
        };

        /// <summary>Furthest a stand goes across the lane either side of the middle (m).</summary>
        const double HalfWidth = 56;
        /// <summary>Nearest a stand comes to the backstop (lane metres).</summary>
        const double LastLane = 648;
        /// <summary>Top of the steel rail the animals stand on, m.</summary>
        public const double RailTop = 0.75;
        /// <summary>Angular gap kept between stands as the shooter sees them (rad): nothing hides behind anything.</summary>
        const double Gap = 0.004;

        public static readonly double[] Ipsc =
        {
            -0.15, 0, 0.15, 0, 0.23, 0.12, 0.23, 0.46, 0.15, 0.58, 0.075, 0.58, 0.075, 0.70, 0.05, 0.73,
            -0.05, 0.73, -0.075, 0.70, -0.075, 0.58, -0.15, 0.58, -0.23, 0.46, -0.23, 0.12,
        };

        public static readonly double[] Chicken =
        {
            -0.05, 0, 0.05, 0, 0.05, 0.08, 0.14, 0.12, 0.20, 0.18, 0.23, 0.30, 0.20, 0.41, 0.14, 0.34, 0.06, 0.28,
            -0.06, 0.30, -0.10, 0.36, -0.12, 0.44, -0.16, 0.46, -0.20, 0.43, -0.25, 0.40, -0.19, 0.38, -0.17, 0.33,
            -0.15, 0.26, -0.14, 0.18, -0.08, 0.11, -0.05, 0.08,
        };

        public static readonly double[] Pig =
        {
            -0.28, 0, -0.22, 0, -0.20, 0.10, 0.18, 0.10, 0.20, 0, 0.26, 0, 0.26, 0.14, 0.32, 0.22, 0.34, 0.30,
            0.28, 0.36, 0.10, 0.40, -0.10, 0.42, -0.20, 0.40, -0.26, 0.37, -0.28, 0.44, -0.31, 0.35, -0.38, 0.27,
            -0.42, 0.20, -0.40, 0.15, -0.30, 0.14, -0.28, 0.10,
        };

        public static readonly double[] Turkey =
        {
            -0.04, 0, 0.02, 0, 0.03, 0.12, 0.10, 0.16, 0.18, 0.22, 0.26, 0.34, 0.30, 0.50, 0.26, 0.64, 0.16, 0.72,
            0.06, 0.66, 0.02, 0.52, -0.08, 0.48, -0.14, 0.50, -0.16, 0.60, -0.17, 0.68, -0.22, 0.70, -0.25, 0.66,
            -0.21, 0.62, -0.22, 0.54, -0.20, 0.46, -0.22, 0.34, -0.16, 0.22, -0.08, 0.15, -0.03, 0.12,
        };

        public static readonly double[] Ram =
        {
            -0.24, 0, -0.18, 0, -0.17, 0.22, 0.20, 0.22, 0.22, 0, 0.28, 0, 0.30, 0.26, 0.36, 0.36, 0.36, 0.46,
            0.30, 0.52, 0.0, 0.54, -0.18, 0.56, -0.24, 0.62, -0.26, 0.72, -0.22, 0.80, -0.30, 0.82, -0.38, 0.76,
            -0.40, 0.66, -0.35, 0.60, -0.42, 0.58, -0.48, 0.52, -0.46, 0.46, -0.38, 0.46, -0.30, 0.40, -0.26, 0.30,
            -0.24, 0.22,
        };

        public static readonly double[] PrairieDog =
        {
            -0.05, 0, 0.05, 0, 0.06, 0.08, 0.055, 0.16, 0.04, 0.20, 0.035, 0.235, 0.02, 0.26, -0.02, 0.26,
            -0.035, 0.235, -0.04, 0.20, -0.055, 0.16, -0.06, 0.08,
        };

        /// <summary>Bottle profiles: (radius, height) up the side from the base, m.</summary>
        public static readonly double[][] Bottles =
        {
            new[] { 0.032, 0, 0.032, 0.135, 0.026, 0.155, 0.016, 0.175, 0.013, 0.20, 0.0145, 0.226, 0.0145, 0.23 },
            new[] { 0.038, 0, 0.038, 0.19, 0.03, 0.22, 0.017, 0.245, 0.015, 0.30, 0.016, 0.302, 0.016, 0.306 },
            new[] { 0.034, 0, 0.034, 0.12, 0.03, 0.15, 0.018, 0.18, 0.013, 0.21, 0.014, 0.212, 0.014, 0.216 },
        };

        /// <summary>A gallon jug seen face on: (half width, height) up the side.</summary>
        public static readonly double[] Jug = { 0.075, 0, 0.075, 0.2, 0.05, 0.232, 0.02, 0.245, 0.02, 0.268 };

        /// <summary>Outline of a profile turned about its axis: right side up, left side down.</summary>
        public static double[] Turned(double[] profile)
        {
            int n = profile.Length / 2;
            var o = new double[n * 4];
            for (int i = 0; i < n; i++)
            {
                o[2 * i] = profile[2 * i];
                o[2 * i + 1] = profile[2 * i + 1];
                o[4 * n - 2 - 2 * i] = -profile[2 * i];
                o[4 * n - 1 - 2 * i] = profile[2 * i + 1];
            }
            return o;
        }

        public static double[] Ellipse(double w, double h, int n = 24)
        {
            var o = new double[n * 2];
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                o[2 * i] = w / 2 * Math.Cos(a);
                o[2 * i + 1] = h / 2 * Math.Sin(a);
            }
            return o;
        }

        /// <summary>A pepper popper: a narrow foot widening to the shoulders, a neck and a round head; 1.07 m.</summary>
        public static double[] Popper()
        {
            var o = new List<double> { -0.05, 0, 0.05, 0, 0.15, 0.72, 0.13, 0.77, 0.05, 0.82 };
            for (int i = 0; i <= 12; i++)
            {
                double a = (-60 + 300.0 * i / 12) * Math.PI / 180;
                o.Add(0.1 * Math.Cos(a));
                o.Add(0.97 + 0.1 * Math.Sin(a));
            }
            o.AddRange(new[] { -0.05, 0.82, -0.13, 0.77, -0.15, 0.72 });
            return o.ToArray();
        }

        /// <summary>
        /// Builds one stand's targets and solid parts in its own frame: u across (shooter's right),
        /// h up from the ground, d down-range from the stand's front.
        /// </summary>
        sealed class Kit
        {
            public readonly List<Mark> Marks = new List<Mark>();
            public readonly List<Box> Props = new List<Box>();
            readonly Station st;
            readonly int index;
            readonly Vec3 eye;
            readonly System.Random rng;

            public Kit(Station st, int index, Vec3 eye, System.Random rng)
            {
                this.st = st;
                this.index = index;
                this.eye = eye;
                this.rng = rng;
            }

            public Vec3 At(double u, double h, double d = 0) => new Vec3(st.Position.X + u, st.Ground + h, st.Position.Z - d);

            /// <summary>A target whose parts are measured from (u, h, d). Round things turn to face the shooter.</summary>
            public Mark Add(string kind, MarkMaterial mat, MarkReaction react, double u, double h, double d, bool round, params MarkPart[] parts)
            {
                var p = At(u, h, d);
                var m = new Mark
                {
                    Id = $"{st.Kind}-{index}-{Marks.Count}",
                    Kind = kind,
                    Station = index,
                    Material = mat,
                    Reaction = react,
                    Position = p,
                    FacingYaw = round ? RangeHits.Facing(p, eye) : Math.PI,
                };
                m.Parts.AddRange(parts);
                Marks.Add(m);
                return m;
            }

            public void Prop(string id, double u0, double u1, double h0, double h1, double d0, double d1, CoverLook look)
            {
                Props.Add(new Box
                {
                    Id = $"{st.Kind}-{index}-{id}",
                    Min = new Vec3(st.Position.X + u0, st.Ground + h0, st.Position.Z - d1),
                    Max = new Vec3(st.Position.X + u1, st.Ground + h1, st.Position.Z - d0),
                    Material = CoverMaterial.Hard,
                    Look = look,
                });
            }

            public double R(double a, double b) => a + rng.NextDouble() * (b - a);
        }

        /// <summary>
        /// Clears the range and sets out a fresh field for a shooter at `eye`: every stand somewhere
        /// in its band of ranges, across the lane, each target in plain view of the eye (over the
        /// ground and the cover, clear of every other stand). A stand that finds no such place is
        /// left out, so nothing is ever set out that can't be hit.
        /// </summary>
        public static RangeField Lay(DuelMap map, Vec3 eye, int seed)
        {
            var field = new RangeField();
            var rng = new System.Random(seed);
            var seen = new List<(double y0, double y1, double p0, double p1)>();
            var visual = map.Terrain ?? map.Ground;
            foreach (var spec in Specs)
            {
                for (int tries = 0; tries < 300; tries++)
                {
                    // Past the first tries, look a little nearer and further than the stand's own band,
                    // and in the end anywhere down the lane it can be seen.
                    double widen = tries < 80 ? 0 : 0.25 * (spec.To - spec.From) + 15;
                    double from = spec.From - widen, to = spec.To + widen;
                    if (tries >= 200)
                    {
                        from = 160;
                        to = 575;
                    }
                    double range = from + rng.NextDouble() * (to - from);
                    double lane = -eye.Z + range;
                    if (lane > LastLane) lane = LastLane - rng.NextDouble() * 10;
                    double half = Math.Min(HalfWidth, range * 0.45);
                    double x = -half + rng.NextDouble() * 2 * half;
                    double z = -lane;
                    var st = new Station
                    {
                        Kind = spec.Kind,
                        Position = new Vec3(x, 0, z),
                        FacingYaw = Math.PI,
                        Ground = Math.Max(map.Ground(x, z), visual(x, z)),
                    };
                    st.Position = new Vec3(x, st.Ground, z);
                    var kit = new Kit(st, field.Stations.Count, eye, new System.Random(rng.Next()));
                    Build(kit, spec.Kind);
                    if (!Fits(map, eye, kit, field, seen, out var span)) continue;
                    field.Stations.Add(st);
                    field.Marks.AddRange(kit.Marks);
                    field.Props.AddRange(kit.Props);
                    seen.Add(span);
                    break;
                }
            }
            return field;
        }

        /// <summary>The stand stands clear of the cover, every target shows to the eye, and nothing else is in the way.</summary>
        static bool Fits(DuelMap map, Vec3 eye, Kit kit, RangeField field, List<(double y0, double y1, double p0, double p1)> seen, out (double y0, double y1, double p0, double p1) span)
        {
            double y0 = double.MaxValue, y1 = double.MinValue, p0 = double.MaxValue, p1 = double.MinValue;
            void See(Vec3 p)
            {
                double dx = p.X - eye.X, dz = p.Z - eye.Z;
                double yaw = Math.Atan2(dx, -dz), pitch = Math.Atan2(p.Y - eye.Y, Math.Sqrt(dx * dx + dz * dz));
                y0 = Math.Min(y0, yaw);
                y1 = Math.Max(y1, yaw);
                p0 = Math.Min(p0, pitch);
                p1 = Math.Max(p1, pitch);
            }
            var blockers = new List<Box>(map.Cover);
            blockers.AddRange(field.Props);
            blockers.AddRange(kit.Props);
            span = default;
            foreach (var b in kit.Props)
            {
                foreach (var c in map.Cover)
                    if (b.Min.X < c.Max.X + 1 && b.Max.X > c.Min.X - 1 && b.Min.Z < c.Max.Z + 1 && b.Max.Z > c.Min.Z - 1) return false;
                See(b.Min);
                See(b.Max);
                See(new Vec3(b.Min.X, b.Max.Y, b.Max.Z));
                See(new Vec3(b.Max.X, b.Min.Y, b.Min.Z));
            }
            foreach (var m in kit.Marks)
            {
                var offsets = m.Rail > 0 ? new[] { -m.Rail, 0, m.Rail } : new[] { 0.0 };
                foreach (var part in m.Parts)
                {
                    var (u0, u1, v0, v1) = part.Bounds();
                    foreach (double off in offsets)
                    {
                        foreach (var (u, v) in new[] { (u0, v0), (u1, v1), (u0, v1), (u1, v0) })
                            See(RangeHits.PointOn(m, u + off - m.Offset(0), v));
                        // The middle and the top of every part must show (a fold of the ground may take the foot).
                        double um = (u0 + u1) / 2 + off - m.Offset(0);
                        foreach (double v in new[] { v0 + 0.4 * (v1 - v0), v1 - 0.15 * (v1 - v0) })
                            if (!Clear(map, eye, RangeHits.PointOn(m, um, v), blockers)) return false;
                    }
                }
            }
            // A frame or a post a little beyond the outermost target.
            y0 -= 0.004;
            y1 += 0.004;
            foreach (var s in seen)
                if (y0 < s.y1 + Gap && y1 > s.y0 - Gap && p0 < s.p1 + Gap && p1 > s.p0 - Gap) return false;
            span = (y0, y1, p0, p1);
            return true;
        }

        /// <summary>The straight line from the eye to `to` stays over the ground and out of every box.</summary>
        static bool Clear(DuelMap map, Vec3 eye, Vec3 to, List<Box> boxes)
        {
            double dist = Math.Sqrt((to.X - eye.X) * (to.X - eye.X) + (to.Z - eye.Z) * (to.Z - eye.Z));
            int n = Math.Max(40, (int)(dist / 2));
            for (int i = 1; i < n; i++)
            {
                double f = i / (double)n;
                double x = eye.X + (to.X - eye.X) * f, y = eye.Y + (to.Y - eye.Y) * f, z = eye.Z + (to.Z - eye.Z) * f;
                if (map.Ground(x, z) > y - 0.06) return false;
                if (map.Terrain != null && map.Terrain(x, z) > y - 0.06) return false;
            }
            // Stop just short of the target, so its own stand under it doesn't count.
            var near = new Vec3(eye.X + (to.X - eye.X) * 0.999, eye.Y + (to.Y - eye.Y) * 0.999, eye.Z + (to.Z - eye.Z) * 0.999);
            foreach (var b in boxes)
                if (Geometry.SegmentBoxEntry(eye, near, b).HasValue) return false;
            return true;
        }

        static void Build(Kit k, string kind)
        {
            switch (kind)
            {
                case "bottles":
                {
                    // A scaffold plank across two oil drums, five bottles stood along it.
                    k.Prop("drum-l", -1.18, -0.62, 0, 0.88, 0, 0.56, CoverLook.Metal);
                    k.Prop("drum-r", 0.62, 1.18, 0, 0.88, 0, 0.56, CoverLook.Metal);
                    k.Prop("plank", -1.25, 1.25, 0.88, 0.92, 0.14, 0.42, CoverLook.Wood);
                    for (int i = 0; i < 5; i++)
                    {
                        var profile = Bottles[(i + k.Marks.Count) % Bottles.Length];
                        k.Add("bottle", MarkMaterial.Glass, MarkReaction.Break, -0.9 + 0.45 * i + k.R(-0.05, 0.05), 0.92, 0.28, true,
                            MarkPart.Poly(0, 0, Turned(profile)));
                    }
                    break;
                }
                case "cans":
                {
                    // Tin cans on top of a row of fence posts.
                    for (int i = 0; i < 5; i++)
                    {
                        double u = -1.2 + 0.6 * i, top = 1.0 + k.R(-0.05, 0.05);
                        k.Prop($"post-{i}", u - 0.045, u + 0.045, -0.2, top, 0.0, 0.09, CoverLook.Wood);
                        k.Add("can", MarkMaterial.Tin, MarkReaction.Knock, u, top, 0.045, true, MarkPart.Rect(0, 0.0615, 0.066, 0.123));
                    }
                    break;
                }
                case "balloons":
                {
                    // Party balloons tied off to stakes, a hand's height apart.
                    for (int i = 0; i < 4; i++)
                        k.Add("balloon", MarkMaterial.Rubber, MarkReaction.Break, -0.9 + 0.6 * i, 1.25 + 0.12 * (i % 2) + k.R(0, 0.1), 0, true, MarkPart.Disc(0, 0.14, 0.28));
                    break;
                }
                case "table":
                {
                    // A trestle table: three water jugs and a watermelon.
                    k.Prop("top", -1.0, 1.0, 0.72, 0.77, 0, 0.7, CoverLook.Wood);
                    for (int i = 0; i < 3; i++)
                        k.Add("jug", MarkMaterial.Water, MarkReaction.Break, -0.8 + 0.42 * i, 0.77, 0.35, true, MarkPart.Poly(0, 0, Turned(Jug)));
                    k.Add("melon", MarkMaterial.Fruit, MarkReaction.Break, 0.62, 0.77, 0.35, true, MarkPart.Poly(0, 0.12, Ellipse(0.36, 0.24)));
                    break;
                }
                case "paper":
                {
                    // A 24-inch cardboard backer on two stakes with a bullseye stapled on.
                    k.Prop("stake-l", -0.36, -0.31, -0.3, 1.6, 0.01, 0.1, CoverLook.Wood);
                    k.Prop("stake-r", 0.31, 0.36, -0.3, 1.6, 0.01, 0.1, CoverLook.Wood);
                    k.Add("bullseye", MarkMaterial.Paper, MarkReaction.Hole, 0, 0.95, 0, false,
                        MarkPart.Disc(0, 0.305, 0.5, rings: true), MarkPart.Rect(0, 0.305, 0.61, 0.61));
                    break;
                }
                case "cardboard":
                {
                    // An IPSC cardboard on stakes, scored A, C and D.
                    k.Prop("stake-l", -0.29, -0.24, -0.3, 1.5, 0.01, 0.1, CoverLook.Wood);
                    k.Prop("stake-r", 0.24, 0.29, -0.3, 1.5, 0.01, 0.1, CoverLook.Wood);
                    k.Add("cardboard", MarkMaterial.Paper, MarkReaction.Hole, 0, 0.85, 0, false, MarkPart.Poly(0, 0, Ipsc));
                    break;
                }
                case "clays":
                {
                    // Clay pigeons stood up on edge against an earth mound.
                    k.Prop("mound", -1.6, 1.6, -0.3, 1.05, 0.06, 1.9, CoverLook.Rock);
                    for (int i = 0; i < 5; i++)
                        k.Add("clay", MarkMaterial.Clay, MarkReaction.Break, -1.1 + 0.55 * i + k.R(-0.08, 0.08), 0.5 + 0.18 * (i % 3) + k.R(0, 0.06), 0, false,
                            MarkPart.Disc(0, 0.054, 0.108));
                    break;
                }
                case "spinner":
                {
                    // Two paddles on an axle between two posts: the big one low, so it rests upright.
                    k.Prop("post-l", -0.32, -0.26, -0.2, 1.0, 0.0, 0.06, CoverLook.Metal);
                    k.Prop("post-r", 0.26, 0.32, -0.2, 1.0, 0.0, 0.06, CoverLook.Metal);
                    var m = k.Add("spinner", MarkMaterial.Steel, MarkReaction.Spin, 0, 0.75, 0.03, false,
                        MarkPart.Disc(0, 0.17, 0.10), MarkPart.Disc(0, -0.21, 0.15), MarkPart.Rect(0, -0.02, 0.025, 0.3));
                    m.PivotV = 0;
                    break;
                }
                case "tree":
                {
                    // A dueling tree: six paddles off a post; a hit swings one over to the other side.
                    k.Prop("post", -0.04, 0.04, -0.3, 1.72, 0.0, 0.08, CoverLook.Metal);
                    for (int i = 0; i < 6; i++)
                    {
                        double side = i % 2 == 0 ? -1 : 1;
                        k.Add("paddle", MarkMaterial.Steel, MarkReaction.Flip, 0, 0.62 + 0.18 * i, 0.04, false,
                            MarkPart.Disc(side * 0.30, 0, 0.15), MarkPart.Rect(side * 0.14, 0, 0.18, 0.025));
                    }
                    break;
                }
                case "poppers":
                {
                    // Three pepper poppers on their base plates; a hit lays one flat.
                    var outline = Popper();
                    for (int i = 0; i < 3; i++)
                    {
                        var m = k.Add("popper", MarkMaterial.Steel, MarkReaction.Fall, -1.2 + 1.2 * i, 0.06, 0, false, MarkPart.Poly(0, 0, outline));
                        m.PivotV = 0;
                    }
                    break;
                }
                case "kyl":
                {
                    // "Know your limits": five squares, each smaller than the last, off one bar.
                    double[] sizes = { 0.25, 0.19, 0.14, 0.10, 0.065 };
                    double u = -0.9;
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        var m = k.Add("square", MarkMaterial.Steel, MarkReaction.Swing, u, 1.25 - sizes[i], 0, false, MarkPart.Rect(0, sizes[i] / 2, sizes[i], sizes[i]));
                        m.PivotV = 1.55 - (1.25 - sizes[i]);
                        u += sizes[i] / 2 + 0.3 + (i + 1 < sizes.Length ? sizes[i + 1] / 2 : 0);
                    }
                    break;
                }
                case "gongs":
                {
                    double[] sizes = { 0.45, 0.30, 0.20 };
                    double[] us = { -0.8, 0.05, 0.75 };
                    for (int i = 0; i < 3; i++)
                    {
                        double c = 1.25 - sizes[i] / 2;
                        var m = k.Add("gong", MarkMaterial.Steel, MarkReaction.Swing, us[i], c, 0, false, MarkPart.Disc(0, 0, sizes[i]));
                        m.PivotV = 1.62 - c;
                    }
                    break;
                }
                case "biggong":
                {
                    var m = k.Add("gong", MarkMaterial.Steel, MarkReaction.Swing, 0, 1.3, 0, false, MarkPart.Disc(0, 0, 0.6));
                    m.PivotV = 0.75;
                    break;
                }
                case "ipsc":
                {
                    var m = k.Add("ipsc", MarkMaterial.Steel, MarkReaction.Swing, 0, 0.4, 0, false, MarkPart.Poly(0, 0, Ipsc));
                    m.PivotV = 1.12;
                    break;
                }
                case "mover":
                {
                    // A steel plate on a trolley running back and forth along a rail behind a low berm.
                    k.Prop("berm", -7.6, 7.6, -0.3, 0.5, -1.9, -0.7, CoverLook.Rock);
                    var m = k.Add("mover", MarkMaterial.Steel, MarkReaction.Swing, 0, 0.82, 0, false, MarkPart.Poly(0, 0, Ipsc));
                    m.Rail = 6;
                    m.Speed = k.R(1.0, 1.8);
                    m.Phase = k.R(0, 24);
                    m.PivotV = 0;
                    break;
                }
                case "chickens":
                case "pigs":
                case "turkeys":
                case "rams":
                {
                    // Metallic silhouettes on a steel stand rail: knock them off.
                    var (outline, gap) = kind switch
                    {
                        "chickens" => (Chicken, 0.75),
                        "pigs" => (Pig, 1.15),
                        "turkeys" => (Turkey, 0.95),
                        _ => (Ram, 1.25),
                    };
                    double width = gap * 3;
                    k.Prop("rail", -width / 2, width / 2, RailTop - 0.12, RailTop, 0.0, 0.3, CoverLook.Metal);
                    for (int i = 0; i < 3; i++)
                    {
                        var m = k.Add(kind.TrimEnd('s'), MarkMaterial.Steel, MarkReaction.Fall, -gap + gap * i, RailTop, 0.1, false, MarkPart.Poly(0, 0, outline));
                        m.PivotV = 0;
                    }
                    break;
                }
                case "prairie":
                {
                    for (int i = 0; i < 3; i++)
                    {
                        double u = -1.0 + 1.0 * i, top = 0.34 + k.R(0, 0.06);
                        k.Prop($"stake-{i}", u - 0.03, u + 0.03, -0.2, top, 0.02, 0.08, CoverLook.Metal);
                        var m = k.Add("prairie-dog", MarkMaterial.Steel, MarkReaction.Fall, u, top, 0, false, MarkPart.Poly(0, 0, PrairieDog));
                        m.PivotV = 0;
                    }
                    break;
                }
            }
        }
    }
}
