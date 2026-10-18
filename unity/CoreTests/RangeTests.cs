using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SniperDuel.Ballistics;
using SniperDuel.Rules;
using static SniperDuel.Tests.RulesFixture;

namespace SniperDuel.Tests
{
    public class RangeTests
    {
        static readonly DuelMap Lane = Maps.ById("training-lane");

        /// <summary>Fields are laid for the shooter lying down (the lowest eye), so they show in every stance.</summary>
        [Test]
        public void EveryStandIsSetOutOnEverySeed()
        {
            foreach (var spot in Lane.Spots.A)
                for (int seed = 0; seed < 10; seed++)
                {
                    var f = RangeLayout.Lay(Lane, Combat.EyeOf(spot, Stance.Prone, Cfg), seed);
                    Assert.That(f.Stations.Count, Is.GreaterThanOrEqualTo(21), $"seed {seed} at {spot.X}: {string.Join(", ", f.Stations.Select(s => s.Kind))}");
                    Assert.That(f.Count, Is.GreaterThanOrEqualTo(60));
                }
        }

        [Test]
        public void LayoutIsDeterministicPerSeed()
        {
            var eye = Combat.EyeOf(Lane.Spots.A[2], Stance.Prone, Cfg);
            var a = RangeLayout.Lay(Lane, eye, 7);
            var b = RangeLayout.Lay(Lane, eye, 7);
            Assert.That(a.Marks.Select(m => (m.Id, m.Position.X, m.Position.Z)), Is.EqualTo(b.Marks.Select(m => (m.Id, m.Position.X, m.Position.Z))));
            Assert.That(RangeLayout.Lay(Lane, eye, 8).Marks.Select(m => m.Position.X), Is.Not.EqualTo(a.Marks.Select(m => m.Position.X)));
        }

        [Test]
        public void PartsCoverWhatTheyShould()
        {
            var bull = MarkPart.Disc(0, 0, 0.5, rings: true);
            Assert.That(bull.Contains(0.24, 0), Is.True);
            Assert.That(bull.Contains(0.2, 0.2), Is.False);
            var ipsc = MarkPart.Poly(0, 0, RangeLayout.Ipsc);
            Assert.That(ipsc.Contains(0, 0.3), Is.True);
            Assert.That(ipsc.Contains(0, 0.72), Is.True);
            Assert.That(ipsc.Contains(0.2, 0.68), Is.False, "beside the head");
            Assert.That(ipsc.Contains(0.22, 0.02), Is.False, "cut corner");
            var beer = MarkPart.Poly(0, 0, RangeLayout.Turned(RangeLayout.Bottles[0]));
            Assert.That(beer.Contains(0.03, 0.05), Is.True);
            Assert.That(beer.Contains(0.03, 0.2), Is.False, "beside the neck");
            Assert.That(beer.Contains(0.0, 0.22), Is.True);
            foreach (var o in new[] { RangeLayout.Chicken, RangeLayout.Pig, RangeLayout.Turkey, RangeLayout.Ram, RangeLayout.PrairieDog, RangeLayout.Popper() })
            {
                var p = MarkPart.Poly(0, 0, o);
                Assert.That(p.Contains(0, p.H * 0.6) || p.Contains(0.05, p.H * 0.5) || p.Contains(-0.05, p.H * 0.5), Is.True);
                Assert.That(p.Contains(0, -0.01), Is.False);
            }
        }

        [Test]
        public void MoverRunsBackAndForthAlongItsRail()
        {
            var m = new Mark { Rail = 6, Speed = 1.5 };
            Assert.That(m.Offset(0), Is.EqualTo(-6).Within(1e-9));
            Assert.That(m.Offset(4), Is.EqualTo(0).Within(1e-9));
            Assert.That(m.Offset(8), Is.EqualTo(6).Within(1e-9));
            Assert.That(m.Offset(12), Is.EqualTo(0).Within(1e-9));
            Assert.That(m.Offset(16), Is.EqualTo(-6).Within(1e-9));
        }

        /// <summary>
        /// The whole point: aim with real ballistics (wind, drop, spin drift) at the middle of every
        /// part of every target in several fields, from every spot and stance, and the round hits it.
        /// </summary>
        [Test]
        public void EveryTargetCanBeHit()
        {
            var state = Match.CreateMatch(11, Lane.Id, new PerSide<PlayerSetup>(new PlayerSetup("A", "bolt65cm"), new PlayerSetup("B", "bolt65cm")), 0, Cfg);
            var conditions = Match.ActiveConditions(state);
            var failures = new List<string>();
            int shots = 0;
            foreach (var (spot, stance) in new[] { (Lane.Spots.A[2], Stance.Prone), (Lane.Spots.A[0], Stance.Standing), (Lane.Spots.A[4], Stance.Crouch), (Lane.Spots.A[1], Stance.Prone) })
                for (int seed = 0; seed < 3; seed++)
                {
                    var eye = Combat.EyeOf(spot, stance, Cfg);
                    var field = RangeLayout.Lay(Lane, Combat.EyeOf(spot, Stance.Prone, Cfg), seed);
                    foreach (var m in field.Marks)
                        for (int k = 0; k < m.Parts.Count; k++)
                        {
                            var part = m.Parts[k];
                            // Thin arms are drawn but too slim to aim at on purpose; the paddles on them are what counts.
                            if (part.Shape == PartShape.Rect && Math.Min(part.W, part.H) < 0.04) continue;
                            double clock = 3.7 * shots;
                            var hit = AimAndFire(field, m, part, spot, stance, eye, conditions, clock, shots++);
                            if (hit == null || hit.Mark != m)
                                failures.Add($"{m.Id} {m.Kind} part {k} at {-m.Position.Z + eye.Z:0} m x {m.Position.X:0.0} ({stance}, seed {seed}): {(hit == null ? "missed" : "hit " + hit.Mark.Id)}");
                        }
                }
            Assert.That(shots, Is.GreaterThan(500));
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        static MarkHit? AimAndFire(RangeField field, Mark m, MarkPart part, Vec3 spot, Stance stance, Vec3 eye, Conditions conditions, double clock, int index)
        {
            // Lead a mover by where it will be when the round gets there.
            double tof = 0;
            double yaw = 0, pitch = 0;
            Vec3 Want(double t)
            {
                double u = part.U, v = part.V;
                if (part.Shape == PartShape.Poly)
                {
                    var (u0, u1, v0, v1) = part.Bounds();
                    u = (u0 + u1) / 2;
                    v = v0 + 0.45 * (v1 - v0);
                    if (!part.Contains(u - part.U, v - part.V)) u = part.U;
                }
                if (m.Mirrored) u = -u;
                return RangeHits.PointOn(m, u, v, clock + t);
            }
            var target = Want(0);
            yaw = Math.Atan2(target.X - eye.X, -(target.Z - eye.Z));
            pitch = Math.Atan2(target.Y - eye.Y, Math.Sqrt(Sq(target.X - eye.X) + Sq(target.Z - eye.Z)));
            MarkHit? hit = null;
            for (int it = 0; it < 8; it++)
            {
                target = Want(tof);
                var req = new ShotRequest
                {
                    Map = Lane,
                    Conditions = conditions,
                    RifleId = "bolt65cm",
                    ShooterPosition = spot,
                    ShooterStance = stance,
                    Input = new FireInput { Yaw = yaw, Pitch = pitch },
                    Seed = 3,
                    ShotIndex = index,
                    FireTime = clock,
                };
                var (traj, e, _) = Combat.Fly(req, Cfg);
                var (impact, h) = RangeHits.Resolve(traj, e, yaw, field, Lane, clock, Cfg);
                hit = h;
                // Where the round crossed the target's plane, against where we wanted it.
                var n = World.Forward(m.FacingYaw);
                var prev = World.ShotToWorld(traj.Points[0], e, yaw);
                Vec3? cross = null;
                for (int i = 1; i < traj.Points.Count; i++)
                {
                    var cur = World.ShotToWorld(traj.Points[i], e, yaw);
                    double da = (prev.X - m.Position.X) * n.X + (prev.Z - m.Position.Z) * n.Z;
                    double db = (cur.X - m.Position.X) * n.X + (cur.Z - m.Position.Z) * n.Z;
                    if (da > 0 && db <= 0)
                    {
                        double f = da / (da - db);
                        cross = new Vec3(prev.X + (cur.X - prev.X) * f, prev.Y + (cur.Y - prev.Y) * f, prev.Z + (cur.Z - prev.Z) * f);
                        tof = traj.Points[i - 1].T + (traj.Points[i].T - traj.Points[i - 1].T) * f;
                        break;
                    }
                    prev = cur;
                }
                if (cross == null) return hit;
                double dist = Math.Sqrt(Sq(target.X - e.X) + Sq(target.Z - e.Z));
                var (cu, cv) = RangeHits.Local(m, cross.Value);
                var (tu, tv) = RangeHits.Local(m, target);
                if (Math.Abs(cu - tu) < 0.002 && Math.Abs(cv - tv) < 0.002 && hit != null) break;
                yaw -= (cu - tu) / dist;
                pitch -= (cv - tv) / dist;
            }
            return hit;
        }

        static double Sq(double x) => x * x;
    }
}
