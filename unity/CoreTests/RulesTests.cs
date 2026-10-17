using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SniperDuel.Ballistics;
using SniperDuel.Rules;
using static SniperDuel.Tests.RulesFixture;
using Env = SniperDuel.Ballistics.Environment;

namespace SniperDuel.Tests
{
    public class RulesConfigTests
    {
        [Test]
        public void UnityCopyOfGameJsonIsByteIdentical()
        {
            var source = File.ReadAllBytes(Path.Combine(Repo.Root, "config", "game.json"));
            var copy = File.ReadAllBytes(Path.Combine(Repo.Root, "unity", "Assets", "Resources", "Config", "game.json"));
            Assert.That(copy, Is.EqualTo(source), "unity/Assets/Resources/Config/game.json must be a copy of config/game.json");
        }

        [Test]
        public void ParsesEverySection()
        {
            var c = Cfg;
            Assert.That(c.Ballistics.Integrator, Is.EqualTo(Integrator.Rk4));
            Assert.That(c.Ballistics.DtSeconds, Is.EqualTo(0.0005));
            Assert.That(c.Ballistics.Effects.SpinDrift, Is.True);
            Assert.That(c.Ballistics.Effects.Coriolis, Is.False);
            Assert.That(c.Ballistics.Transonic.SigmaRadPerSqrtS, Is.EqualTo(0.004));
            Assert.That(c.Rifles.Select(r => r.Id), Is.EqualTo(new[] { "bolt308", "bolt65cm", "mag338", "am50" }));
            var r65 = c.RifleById("bolt65cm");
            Assert.That(r65.DragModel, Is.EqualTo(DragModel.G7));
            Assert.That(r65.Class, Is.EqualTo("precision"));
            Assert.That(r65.SweetSpotM, Is.EqualTo(new[] { 400.0, 900.0 }));
            Assert.That(r65.ColdBoreShiftMrad.Windage, Is.EqualTo(-0.05));
            Assert.That(c.Scope.ClickMrad, Is.EqualTo(0.1));
            Assert.That(c.Sway.AmplitudeMrad[Stance.Crouch], Is.EqualTo(1.1));
            Assert.That(c.Sway.HoldBreath.MaxStrainFactor, Is.EqualTo(3));
            Assert.That(c.Sway.EyeHeightM[Stance.Standing], Is.EqualTo(1.6));
            Assert.That(c.Target.HeadM, Is.EqualTo(0.25));
            Assert.That(c.PracticeRange.TargetsM, Is.EqualTo(new[] { 300.0, 600, 900 }));
            Assert.That(c.PracticeRange.Atmosphere.AltitudeM, Is.EqualTo(300));
            Assert.That(c.PracticeRange.Atmosphere.PressureHPa, Is.Null);
            Assert.That(c.PracticeRange.FiringPointHeightM, Is.EqualTo(1.2));
            Assert.That(c.SoftCover.MinEnergyJ, Is.EqualTo(2500));
            Assert.That(c.Rules.KillsToWin, Is.EqualTo(2));
            Assert.That(c.Rules.ChestKillEnergyJ, Is.EqualTo(1500));
            Assert.That(c.Rules.RoundWindDrift, Is.EqualTo(0.25));
            Assert.That(c.Player.Silhouette[Stance.Prone].WidthM, Is.EqualTo(0.55));
            Assert.That(c.Player.Wound.MoveSpeedFactor, Is.EqualTo(0.6));
            Assert.That(c.Bot.ThinkSeconds, Is.EqualTo(new[] { 8.0, 25 }));
            Assert.That(c.Bot[BotDifficulty.Easy].WindMode, Is.EqualTo(WindMode.HalfStation));
            Assert.That(c.Bot[BotDifficulty.Medium].WindMode, Is.EqualTo(WindMode.Station));
            Assert.That(c.Bot[BotDifficulty.Hard].WindMode, Is.EqualTo(WindMode.Layered));
            Assert.That(c.Bot[BotDifficulty.Hard].SpotChance[Stance.Prone], Is.EqualTo(0.65));
            Assert.That(c.Bot[BotDifficulty.Easy].Positions, Is.EqualTo(3));
            Assert.That(c.Bot[BotDifficulty.Medium].RelocateAfterFiring, Is.True);
        }

        [Test]
        public void UnknownRifleThrows()
        {
            Assert.Throws<ArgumentException>(() => Cfg.RifleById("musket"));
            Assert.Throws<ArgumentException>(() => Maps.ById("moon"));
        }
    }

    /// <summary>Ported from packages/shared/test/shared.test.ts.</summary>
    public class RulesWorldTests
    {
        static readonly Vec3 Eye = new Vec3(0, 0.3, 0);

        [Test]
        public void RoundTripsPointsForAnyYaw()
        {
            foreach (double yaw in new[] { 0, 0.3, -1.2, Math.PI })
            {
                var p = new Vec3(412, -3.1, 7.5);
                var w = World.ShotToWorld(p, new Vec3(5, 1, -20), yaw);
                var back = World.WorldToShot(w, new Vec3(5, 1, -20), yaw);
                Assert.That(back.X, Is.EqualTo(p.X).Within(1e-9));
                Assert.That(back.Y, Is.EqualTo(p.Y).Within(1e-9));
                Assert.That(back.Z, Is.EqualTo(p.Z).Within(1e-9));
            }
        }

        [Test]
        public void YawZeroLooksNorthWithXOnTheRight()
        {
            var w = World.ShotToWorld(new Vec3(100, 0, 2), Vec3.Zero, 0);
            Assert.That(w.X, Is.EqualTo(2));
            Assert.That(w.Y, Is.EqualTo(0));
            Assert.That(w.Z, Is.EqualTo(-100));
        }

        [Test]
        public void CrosswindPushesRightLookingNorthAndLeftLookingSouth()
        {
            Assert.That(World.VectorToShot(new Vec3(5, 0, 0), 0).Z, Is.EqualTo(5).Within(1e-9));
            Assert.That(World.VectorToShot(new Vec3(5, 0, 0), Math.PI).Z, Is.EqualTo(-5).Within(1e-9));
        }

        static LaneWind Lane() => new LaneWind
        {
            Start = Vec3.Zero,
            Direction = new Vec3(0, 0, -1),
            Edges = new double[] { 300, 600 },
            Zones = new List<Vec3> { new Vec3(1, 0, 0), new Vec3(2, 0, 0), new Vec3(3, 0, 0) },
        };

        [Test]
        public void LaneWindSamplesTheZoneAPointIsIn()
        {
            Assert.That(World.LaneWindAt(Lane(), new Vec3(0, 0, -100)).X, Is.EqualTo(1));
            Assert.That(World.LaneWindAt(Lane(), new Vec3(0, 0, -450)).X, Is.EqualTo(2));
            Assert.That(World.LaneWindAt(Lane(), new Vec3(0, 0, -800)).X, Is.EqualTo(3));
        }

        [Test]
        public void ShotWindFieldMapsZonesToDownrangeDistances()
        {
            var f = World.ShotWindField(Lane(), Vec3.Zero, 0);
            Assert.That(f.Zones.Select(z => z.UntilRange), Is.EqualTo(new[] { 300, 600, double.PositiveInfinity }));
            Assert.That(f.Zones.Select(z => Math.Round(z.Vector.Z)), Is.EqualTo(new[] { 1.0, 2, 3 }));
            Assert.That(f.Gust, Is.Null);
        }

        [Test]
        public void ShotWindFieldReversesZonesFromTheFarEnd()
        {
            var f = World.ShotWindField(Lane(), new Vec3(0, 0, -900), Math.PI);
            Assert.That(f.Zones.Select(z => Math.Round(z.UntilRange)), Is.EqualTo(new[] { 300, 600, double.PositiveInfinity }));
            // Looking south, wind toward +x pushes the bullet left (negative z in the shot frame).
            Assert.That(f.Zones.Select(z => Math.Round(z.Vector.Z)), Is.EqualTo(new[] { -3.0, -2, -1 }));
        }

        static Silhouette Target(double d) => new Silhouette
        {
            Id = $"t{d}",
            Position = new Vec3(0, 0, -d),
            FacingYaw = Math.PI,
            Width = Cfg.Target.WidthM,
            Height = Cfg.Target.HeightM,
            Head = Cfg.Target.HeadM,
        };

        static (Silhouette t, Trajectory traj) FireAt(double d, double upMrad = 0, double rightMrad = 0)
        {
            var env = new Env { Atmosphere = new Atmosphere(0, 1013.25, 15, 0.5) };
            var t = Target(d);
            double chestY = t.Height - t.Head - 0.175;
            double dy = chestY - Eye.Y;
            double pitch = Math.Atan2(dy, d);
            double slant = JsMath.Hypot(d, dy);
            var shot = RifleSetup.RifleShot(Cfg.RifleById("bolt308"), Cfg);
            shot.LosElevation = pitch;
            var sol = Solutions.SolveFiringSolution(shot, env, slant);
            shot.DialElevation = sol.Elevation + upMrad / 1000;
            shot.DialWindage = sol.Windage + rightMrad / 1000;
            return (t, Solver.Simulate(shot, env, new SolveOptions { MaxRange = d + 50 }));
        }

        [Test]
        public void CorrectSolutionHitsTheChestAt300600900()
        {
            foreach (double d in new[] { 300, 600, 900 })
            {
                var (t, traj) = FireAt(d);
                var impact = Hits.ResolveImpact(traj, Eye, 0, new[] { t });
                Assert.That(impact.Kind, Is.EqualTo(ImpactKind.Target), $"{d} m");
                Assert.That(impact.Zone, Is.EqualTo(HitZone.Chest), $"{d} m");
                Assert.That(Math.Abs(impact.Local!.Value.U), Is.LessThan(0.01));
                Assert.That(Math.Abs(impact.Local!.Value.V), Is.LessThan(0.01));
            }
        }

        [Test]
        public void DiallingUpAt600MovesTheImpactIntoTheHead()
        {
            var (t, traj) = FireAt(600, 0.45);
            Assert.That(Hits.ResolveImpact(traj, Eye, 0, new[] { t }).Zone, Is.EqualTo(HitZone.Head));
        }

        [Test]
        public void BigLowMissHitsTheGroundShort()
        {
            var (t, traj) = FireAt(600, -3);
            var impact = Hits.ResolveImpact(traj, Eye, 0, new[] { t });
            Assert.That(impact.Kind, Is.EqualTo(ImpactKind.Ground));
            Assert.That(-impact.Point.Z, Is.LessThan(600));
        }

        [Test]
        public void SpotterCallReportsTheMissInMrad()
        {
            var (t, traj) = FireAt(600, 0, 1.5);
            var call = Hits.MissCall(traj, Eye, 0, t)!.Value;
            Assert.That(call.RightMrad, Is.EqualTo(1.5).Within(0.05));
            Assert.That(Math.Abs(call.UpMrad), Is.LessThan(0.1));
            Assert.That(Hits.ResolveImpact(traj, Eye, 0, new[] { t }).Kind, Is.Not.EqualTo(ImpactKind.Target));
        }

        [Test]
        public void ImpactEnergyMatchesTheTrajectory()
        {
            var (t, traj) = FireAt(600);
            var impact = Hits.ResolveImpact(traj, Eye, 0, new[] { t });
            Assert.That(impact.Energy, Is.EqualTo(traj.AtRange(600)!.Value.Energy).Within(5));
        }

        [Test]
        public void RangefinderReadsThePlateOrTheGround()
        {
            var t = Target(600);
            double onPlate = Hits.Rangefind(Eye, new Aim(0, Math.Atan2(0.5 - 0.3, 600)), new[] { t })!.Value;
            Assert.That(onPlate, Is.EqualTo(600).Within(0.5));
            double onGround = Hits.Rangefind(Eye, new Aim(0.2, -0.3 / 250), new[] { t })!.Value;
            Assert.That(onGround, Is.InRange(240, 260));
        }

        [Test]
        public void SoftCoverStopsWeakBulletsAndPassesStrongOnes()
        {
            var (t, traj) = FireAt(300);
            var plate = new Box { Id = "ac", Min = new Vec3(-1, 0, -151), Max = new Vec3(1, 2, -150), Material = CoverMaterial.Soft };
            var weak = Hits.ResolveImpact(traj, Eye, 0, new[] { t }, null, new[] { plate }, new Penetration { MinEnergyJ = 1e6, EnergyKeep = 0.7, MaxLayers = 1 });
            Assert.That(weak.Kind, Is.EqualTo(ImpactKind.Cover));
            Assert.That(weak.CoverId, Is.EqualTo("ac"));
            var strong = Hits.ResolveImpact(traj, Eye, 0, new[] { t }, null, new[] { plate }, new Penetration { MinEnergyJ = 100, EnergyKeep = 0.7, MaxLayers = 1 });
            var clean = Hits.ResolveImpact(traj, Eye, 0, new[] { t });
            Assert.That(strong.Kind, Is.EqualTo(ImpactKind.Target));
            Assert.That(strong.Penetrated, Is.EqualTo(new[] { "ac" }));
            Assert.That(strong.Energy, Is.EqualTo(clean.Energy * 0.7).Within(1e-6));
            Assert.That(strong.Speed, Is.EqualTo(clean.Speed * Math.Sqrt(0.7)).Within(1e-9));
            plate.Material = CoverMaterial.Hard;
            Assert.That(Hits.ResolveImpact(traj, Eye, 0, new[] { t }, null, new[] { plate }, Penetration.From(Cfg.SoftCover)).Kind, Is.EqualTo(ImpactKind.Cover));
        }

        [Test]
        public void PracticeRangeIsDeterministicPerSeed()
        {
            var a = Practice.CreatePracticeRange(5, Cfg);
            var b = Practice.CreatePracticeRange(5, Cfg);
            Assert.That(a.Wind.Zones, Is.EqualTo(b.Wind.Zones));
            Assert.That(a.Wind.Gust!.Seed, Is.EqualTo(b.Wind.Gust!.Seed));
            Assert.That(Practice.CreatePracticeRange(6, Cfg).Wind.Zones, Is.Not.EqualTo(a.Wind.Zones));
        }

        [Test]
        public void PracticeSteelStandsAtTheConfiguredDistancesFacingTheShooter()
        {
            var r = Practice.CreatePracticeRange(1, Cfg);
            Assert.That(r.Targets.Select(t => -t.Position.Z), Is.EqualTo(Cfg.PracticeRange.TargetsM));
            foreach (var t in r.Targets) Assert.That(World.Forward(t.FacingYaw).Z, Is.EqualTo(1).Within(1e-9));
            Assert.That(r.Targets[0].Id, Is.EqualTo("steel-300"));
        }

        [Test]
        public void PracticeWindStaysPlayable()
        {
            for (uint s = 0; s < 50; s++)
                foreach (var z in Practice.CreatePracticeRange(s, Cfg).Wind.Zones)
                    Assert.That(JsMath.Hypot(z.X, z.Z), Is.LessThan(10));
        }

        static double Amp(Stance stance, bool bipod = false)
        {
            double max = 0;
            for (double t = 10; t < 15; t += 0.05)
            {
                var o = Sway.Offset(new SwayInput { Stance = stance, Bipod = bipod, Breath = Sway.InitialBreath(), Seed = 1 }, t, Cfg);
                max = Math.Max(max, JsMath.Hypot(o.Right, o.Up));
            }
            return max;
        }

        [Test]
        public void ProneIsSteadierAndABipodHalvesIt()
        {
            Assert.That(Amp(Stance.Prone), Is.LessThan(Amp(Stance.Standing) / 3));
            Assert.That(Amp(Stance.Prone, true), Is.EqualTo(Amp(Stance.Prone) / 2).Within(1e-5));
        }

        [Test]
        public void HoldingBreathSteadiesThenStrains()
        {
            var b = Sway.HoldBreath(Sway.InitialBreath(), 0, Cfg);
            Assert.That(Sway.BreathFactor(b, 2, Cfg), Is.EqualTo(Cfg.Sway.HoldBreath.SteadyFactor).Within(1e-9));
            Assert.That(Sway.BreathFactor(b, 9, Cfg), Is.GreaterThan(1.5));
            var released = Sway.ReleaseBreath(b, 9, Cfg);
            Assert.That(Sway.BreathFactor(released, 9.01, Cfg), Is.GreaterThan(1.5));
            Assert.That(Sway.BreathFactor(released, 30, Cfg), Is.LessThan(1.02));
        }

        [Test]
        public void JsMathMatchesJavaScript()
        {
            Assert.That(JsMath.Round(2.5), Is.EqualTo(3));
            Assert.That(JsMath.Round(-2.5), Is.EqualTo(-2));
            Assert.That(JsMath.Round(0.49999999999999994), Is.EqualTo(0));
            Assert.That(JsMath.Round(1.4999), Is.EqualTo(1));
            Assert.That(JsMath.Hypot(3, 4), Is.EqualTo(5));
            Assert.That(JsMath.Hypot(0, 0, 0), Is.EqualTo(0));
            Assert.That(JsMath.Hypot(double.NaN, double.PositiveInfinity), Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void EloMovesProvisionalPlayersFaster()
        {
            Assert.That(Elo.ExpectedScore(1200, 1200), Is.EqualTo(0.5));
            var r = Elo.RateMatch(Elo.NewRating, Elo.NewRating);
            Assert.That(r.Winner.Value, Is.EqualTo(1220));
            Assert.That(r.Loser.Value, Is.EqualTo(1180));
            Assert.That(r.Winner.Games, Is.EqualTo(1));
            var settled = Elo.RateMatch(new Rating(1200, 10), new Rating(1200, 10));
            Assert.That(settled.Winner.Value, Is.EqualTo(1212));
        }
    }

    /// <summary>Ported from packages/shared/test/match.test.ts.</summary>
    public class RulesMatchTests
    {
        static RulesConfig R => Cfg.Rules;
        static DuelMap Map => Maps.ById("training-lane");

        static MatchState NewMatch(uint seed = 1) =>
            Match.CreateMatch(seed, "training-lane", new PerSide<PlayerSetup>(new PlayerSetup("Alex", "bolt308"), new PlayerSetup("Bo", "mag338")), 0, Cfg);

        static StepResult Step(MatchState s, MatchInput input)
        {
            var r = Match.ApplyInput(s, input, Cfg);
            if (r.Error != null) throw new InvalidOperationException(r.Error);
            return r;
        }

        /// <summary>A perfect shot from the actor at the frozen target (calculator solution, no sway).</summary>
        static MatchInput PerfectFire(MatchState s, bool head = false, double? at = null)
        {
            double time = at ?? s.PhaseStart + 5;
            var side = s.Actor!.Value;
            var me = s.Players[side];
            var enemy = s.Players[Match.Other(side)];
            var eye = Combat.EyeOf(me.Position, me.Stance, Cfg);
            var sil = Combat.SilhouetteFor("t", enemy.Position, enemy.Stance, Combat.FacingYaw(enemy.Side), Cfg);
            var aim = head ? Combat.EyeOf(enemy.Position, enemy.Stance, Cfg) : Combat.ChestOf(sil);
            double dx = aim.X - eye.X;
            double dz = aim.Z - eye.Z;
            double horiz = JsMath.Hypot(dx, dz);
            double yaw = Math.Atan2(dx, -dz);
            double pitch = Math.Atan2(aim.Y - eye.Y, horiz);
            var c = Match.ActiveConditions(s);
            var rifle = Cfg.RifleById(me.RifleId);
            var shot = RifleSetup.RifleShot(rifle, Cfg);
            shot.LosElevation = pitch;
            var sol = Solutions.SolveFiringSolution(shot, new Env { Atmosphere = c.Atmosphere, Wind = World.ShotWindField(c.Wind, eye, yaw) }, JsMath.Hypot(horiz, aim.Y - eye.Y), new SolveOptions { FireTime = time });
            var cold = me.ColdBore ? rifle.ColdBoreShiftMrad : new ColdBoreShift();
            return MatchInput.FireShot(side, new FireInput { Yaw = yaw, Pitch = pitch, DialElevationMrad = sol.Elevation * 1000 - cold.Elevation, DialWindageMrad = sol.Windage * 1000 - cold.Windage }, time);
        }

        /// <summary>Put both players in the open, standing, facing each other with clear sight lines.</summary>
        static MatchState OpenField(MatchState s)
        {
            s = Step(s, MatchInput.PlaceAt(Side.A, new Vec3(10, 0, -75), Stance.Standing, 1)).State;
            s = Step(s, MatchInput.PlaceAt(Side.B, new Vec3(-10, 0, -575), Stance.Standing, 1)).State;
            s = Step(s, MatchInput.ReadyUp(Side.A, 2)).State;
            return Step(s, MatchInput.ReadyUp(Side.B, 2)).State;
        }

        static string Pa(MatchState s) => $"{Js(s.Phase)}:{s.Actor}";

        [Test]
        public void BothTrainingZonesSeeEachOtherOverTheValley()
        {
            var a = Combat.EyeOf(new Vec3(10, Map.Ground(10, -75), -75), Stance.Prone, Cfg);
            var b = Combat.EyeOf(new Vec3(-10, Map.Ground(-10, -575), -575), Stance.Prone, Cfg);
            Assert.That(Visibility.LineOfSight(a, b, Map.Ground, Map.Cover), Is.True);
        }

        [Test]
        public void EveryRockBlocksTheLineStraightThroughIt()
        {
            foreach (var box in Map.Cover)
            {
                double cx = (box.Min.X + box.Max.X) / 2;
                double cz = (box.Min.Z + box.Max.Z) / 2;
                double y = (box.Min.Y + box.Max.Y) / 2;
                Assert.That(Visibility.LineOfSight(new Vec3(cx, y, cz + 5), new Vec3(cx, y, cz - 5), Map.Ground, Map.Cover), Is.False, box.Id);
            }
        }

        [Test]
        public void RunsSetupThenAMovesBFiresBMovesAFires()
        {
            var s = NewMatch();
            Assert.That(s.Phase, Is.EqualTo(PhaseKind.Setup));
            s = OpenField(s);
            var seen = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                seen.Add(Pa(s));
                s = Step(s, MatchInput.ReadyUp(s.Actor!.Value, s.PhaseStart + 1)).State;
            }
            Assert.That(seen, Is.EqualTo(new[] { "move:A", "shot:B", "move:B", "shot:A", "move:A" }));
        }

        [Test]
        public void PhasesTimeOutOnTheClock()
        {
            var s = OpenField(NewMatch());
            double start = s.PhaseStart;
            s = Step(s, MatchInput.Tick(start + R.MoveSeconds + 0.01)).State;
            Assert.That(Pa(s), Is.EqualTo("shot:B"));
            s = Step(s, MatchInput.Tick(s.PhaseStart + R.ShotSeconds * 3 + 0.5)).State;
            Assert.That(Pa(s), Is.EqualTo("move:A"));
            Assert.That(s.Pair, Is.EqualTo(2));
        }

        [Test]
        public void RejectsOutOfTurnActions()
        {
            var s = OpenField(NewMatch());
            Assert.That(Match.ApplyInput(s, MatchInput.FireShot(Side.A, new FireInput(), s.PhaseStart + 1), Cfg).Error, Is.Not.Null);
            var r = Match.ApplyInput(s, MatchInput.PlaceAt(Side.B, new Vec3(0, 0, -600), Stance.Prone, s.PhaseStart + 1), Cfg);
            Assert.That(r.Error, Is.Not.Null);
            Assert.That(r.State, Is.SameAs(s));
        }

        [Test]
        public void LimitsMovementToTheBudgetAndKeepsPlayersInTheirZone()
        {
            var s = OpenField(NewMatch());
            var from = s.Players.A.Position;
            s = Step(s, MatchInput.PlaceAt(Side.A, new Vec3(-500, 0, 300), Stance.Prone, s.PhaseStart + 1)).State;
            var p = s.Players.A.Position;
            Assert.That(JsMath.Hypot(p.X - from.X, p.Z - from.Z), Is.LessThanOrEqualTo(Match.MoveBudget(s, Side.A, Cfg) + 0.5));
            Assert.That(p.X, Is.GreaterThanOrEqualTo(Map.Zones.A.XMin));
            Assert.That(-p.Z, Is.GreaterThanOrEqualTo(Map.Zones.A.SMin));
            Assert.That(-p.Z, Is.LessThanOrEqualTo(Map.Zones.A.SMax));
        }

        [Test]
        public void PerfectHeadShotKillsEndsTheRoundAndAlternatesTheFirstMover()
        {
            var s = OpenField(NewMatch(3));
            s = Step(s, MatchInput.ReadyUp(Side.A, s.PhaseStart + 1)).State; // A holds position
            var r = Step(s, PerfectFire(s, head: true));
            Assert.That(r.Outcome!.Verdict, Is.EqualTo(Verdict.Kill));
            Assert.That(r.State.Phase, Is.EqualTo(PhaseKind.RoundOver));
            Assert.That(r.State.Players.B.Kills, Is.EqualTo(1));
            Assert.That(s.Players.B.Kills, Is.EqualTo(0), "the previous state is not mutated");
            var next = Step(r.State, MatchInput.NextRound(r.State.PhaseStart + 3)).State;
            Assert.That(next.Phase, Is.EqualTo(PhaseKind.Setup));
            Assert.That(next.Round, Is.EqualTo(2));
            Assert.That(next.FirstMover, Is.EqualTo(Side.B));
        }

        [Test]
        public void LongChestHitWoundsAndASecondWoundPutsTheTargetDown()
        {
            var s = Match.CreateMatch(9, "training-lane", new PerSide<PlayerSetup>(new PlayerSetup("A", "bolt308"), new PlayerSetup("B", "bolt308")), 0, Cfg);
            // 620 m apart: beyond the .308's chest-kill energy.
            s = Step(s, MatchInput.PlaceAt(Side.A, new Vec3(10, 0, -20), Stance.Standing, 1)).State;
            s = Step(s, MatchInput.PlaceAt(Side.B, new Vec3(-10, 0, -640), Stance.Standing, 1)).State;
            s = Step(s, MatchInput.ReadyUp(Side.A, 2)).State;
            s = Step(s, MatchInput.ReadyUp(Side.B, 2)).State;
            s = Step(s, MatchInput.ReadyUp(Side.A, s.PhaseStart + 1)).State;
            var r = Step(s, PerfectFire(s));
            Assert.That(r.Outcome!.Impact.Energy, Is.LessThan(R.ChestKillEnergyJ));
            Assert.That(r.Outcome.Verdict, Is.EqualTo(Verdict.Wound));
            Assert.That(r.State.Players.A.Wounds, Is.EqualTo(1));
            s = r.State; // B moves, A shoots back and misses on purpose
            s = Step(s, MatchInput.ReadyUp(Side.B, s.PhaseStart + 1)).State;
            s = Step(s, MatchInput.ReadyUp(Side.A, s.PhaseStart + 1)).State;
            s = Step(s, MatchInput.ReadyUp(Side.A, s.PhaseStart + 1)).State;
            Assert.That(Pa(s), Is.EqualTo("shot:B"));
            r = Step(s, PerfectFire(s));
            Assert.That(r.Outcome!.Verdict, Is.EqualTo(Verdict.Kill));
        }

        [Test]
        public void FiringRevealsTheShooterUntilTheyMove()
        {
            var s = OpenField(NewMatch(4));
            s = Step(s, MatchInput.ReadyUp(Side.A, s.PhaseStart + 1)).State;
            var bad = PerfectFire(s);
            bad.Fire!.DialElevationMrad = 30;
            s = Step(s, bad).State;
            Assert.That(s.Players.B.Revealed, Is.Not.Null);
            s = Step(s, MatchInput.PlaceAt(Side.B, new Vec3(30, 0, -600), Stance.Prone, s.PhaseStart + 2)).State;
            Assert.That(s.Players.B.Revealed, Is.Null);
        }

        [Test]
        public void SameInputsAndSeedReplayToTheIdenticalMatch()
        {
            StepResult Run()
            {
                var s = OpenField(NewMatch(77));
                s = Step(s, MatchInput.ReadyUp(Side.A, s.PhaseStart + 1)).State;
                return Step(s, PerfectFire(s, false, s.PhaseStart + 7.3));
            }
            var a = Run();
            var b = Run();
            JsonParity.Expect(OutcomeSummary(a.Outcome), OutcomeSummary(b.Outcome), "outcome");
            JsonParity.Expect(StateSummary(a.State), StateSummary(b.State), "state");
            Assert.That(a.Outcome!.Impact.Energy, Is.EqualTo(b.Outcome!.Impact.Energy));
        }

        [Test]
        public void SuddenDeathCalmsTheWindAndShrinksTheZoneTowardTheFront()
        {
            var s = OpenField(NewMatch(5));
            var z1 = s.Conditions.Wind.Zones[1];
            double fullWind = JsMath.Hypot(z1.X, z1.Z);
            s = Step(s, MatchInput.PlaceAt(Side.A, new Vec3(0, 0, -2), Stance.Prone, s.PhaseStart + 1)).State;
            int guard = 0;
            while (s.Pair < R.SuddenDeathAfterPairs + 2 && guard++ < 200)
                s = Step(s, MatchInput.ReadyUp(s.Actor!.Value, s.PhaseStart + 0.5)).State;
            Assert.That(s.WindScale, Is.LessThan(1));
            Assert.That(Match.IsSuddenDeath(s, Cfg), Is.True);
            Assert.That(Match.MinStance(s, Cfg), Is.EqualTo(Stance.Crouch));
            Assert.That(s.Players.A.Stance, Is.Not.EqualTo(Stance.Prone));
            // A sat at the very back of the zone; the shrink pulled them forward.
            while (s.Actor != Side.A || s.Phase != PhaseKind.Move) s = Step(s, MatchInput.ReadyUp(s.Actor!.Value, s.PhaseStart + 0.5)).State;
            Assert.That(-s.Players.A.Position.Z, Is.GreaterThan(20));
            Assert.That(fullWind, Is.GreaterThan(0));
        }

        [Test]
        public void ShiftWalksAndChangesStanceWithinTheDeckAtAnyTime()
        {
            var s = Match.CreateMatch(3, "twin-bridges", new PerSide<PlayerSetup>(new PlayerSetup("A", "bolt308"), new PlayerSetup("B", "bolt308")), 0, Cfg);
            var map = Maps.ById("twin-bridges");
            s = Step(s, MatchInput.PlaceAt(Side.A, map.Spots.A[0], Stance.Prone, 1)).State;
            s = Step(s, MatchInput.ReadyUp(Side.A, 1)).State;
            s = Step(s, MatchInput.ReadyUp(Side.B, 1)).State;
            Assert.That(s.Phase, Is.EqualTo(PhaseKind.Shot));
            var actor = s.Actor;
            // A few metres along the deck, standing: allowed whoever's shot it is, and the phase goes on.
            s = Step(s, MatchInput.ShiftTo(Side.A, new Vec3(3, 0, -1), Stance.Standing, 2)).State;
            Assert.That(s.Players.A.Position.X, Is.EqualTo(3));
            Assert.That(s.Players.A.Position.Y, Is.EqualTo(24));
            Assert.That(s.Players.A.Stance, Is.EqualTo(Stance.Standing));
            Assert.That(s.Phase, Is.EqualTo(PhaseKind.Shot));
            Assert.That(s.Actor, Is.EqualTo(actor));
            Assert.That(s.Acted, Is.False);
            // Off the deck: held at the barriers and the guardrail.
            s = Step(s, MatchInput.ShiftTo(Side.A, new Vec3(40, 0, -30), Stance.Crouch, 3)).State;
            Assert.That(s.Players.A.Position.X, Is.EqualTo(5));
            Assert.That(-s.Players.A.Position.Z, Is.EqualTo(4.6).Within(1e-9));
            Assert.That(s.Players.A.Stance, Is.EqualTo(Stance.Crouch));
        }

        [Test]
        public void TurnLockMovesOnlyInYourOwnTurnAndHoldsYouAfterFiring()
        {
            var s = Match.CreateMatch(3, "twin-bridges", new PerSide<PlayerSetup>(new PlayerSetup("A", "bolt308"), new PlayerSetup("B", "bolt308")), 0, Cfg, turnLock: true);
            var map = Maps.ById("twin-bridges");
            s = Step(s, MatchInput.PlaceAt(Side.A, map.Spots.A[0], Stance.Prone, 1)).State;
            s = Step(s, MatchInput.ReadyUp(Side.A, 1)).State;
            s = Step(s, MatchInput.ReadyUp(Side.B, 1)).State;
            Assert.That(s.Phase, Is.EqualTo(PhaseKind.Shot));
            var first = s.Actor!.Value;
            var second = Match.Other(first);
            var wide = new FireInput { Yaw = 1.2, Pitch = 0.2 };
            Vec3 Spot(Side side, double dx) => new Vec3(map.Spots[side][0].X + dx, 0, map.Spots[side][0].Z);

            // Not your turn: no walking, no standing up.
            Assert.That(Match.ApplyInput(s, MatchInput.ShiftTo(second, Spot(second, 2), Stance.Standing, 2), Cfg).Error, Is.Not.Null);
            // Your turn: any stance, whatever the enemy is doing.
            s = Step(s, MatchInput.ShiftTo(first, Spot(first, 2), Stance.Crouch, 2)).State;
            Assert.That(s.Players[first].Stance, Is.EqualTo(Stance.Crouch));
            s = Step(s, MatchInput.FireShot(first, wide, 3)).State;
            Assert.That(s.Actor, Is.EqualTo(second));
            Assert.That(s.Players[first].Locked, Is.True);
            var held = s.Players[first].Position;

            // The shooter stays put through the enemy's turn; the enemy moves freely.
            Assert.That(Match.ApplyInput(s, MatchInput.ShiftTo(first, Spot(first, -2), Stance.Prone, 4), Cfg).Error, Is.Not.Null);
            s = Step(s, MatchInput.ShiftTo(second, Spot(second, 1), Stance.Standing, 4)).State;
            Assert.That(s.Players[second].Stance, Is.EqualTo(Stance.Standing));
            Assert.That(s.Players[first].Stance, Is.EqualTo(Stance.Crouch));

            // Holding fire doesn't free them: still held in their own next turn.
            s = Step(s, MatchInput.ReadyUp(second, 5)).State;
            Assert.That(s.Actor, Is.EqualTo(first));
            Assert.That(Match.CanMove(s, first), Is.False);
            s = Step(s, MatchInput.FireShot(first, wide, 6)).State;
            Assert.That(s.Players[first].Position, Is.EqualTo(held));

            // The enemy fires: the first shooter is free again in their turn, the enemy is now held.
            s = Step(s, MatchInput.FireShot(second, wide, 7)).State;
            Assert.That(s.Actor, Is.EqualTo(first));
            Assert.That(s.Players[first].Locked, Is.False);
            Assert.That(s.Players[second].Locked, Is.True);
            s = Step(s, MatchInput.ShiftTo(first, Spot(first, -2), Stance.Prone, 8)).State;
            Assert.That(s.Players[first].Stance, Is.EqualTo(Stance.Prone));
        }

        [Test]
        public void TurnLockNeverForcesAStance()
        {
            var s = Match.CreateMatch(5, "training-lane", new PerSide<PlayerSetup>(new PlayerSetup("A", "bolt308"), new PlayerSetup("B", "mag338")), 0, Cfg, turnLock: true);
            s = OpenField(s);
            s = Step(s, MatchInput.ShiftTo(Side.A, s.Players.A.Position, Stance.Prone, 2)).State;
            int guard = 0;
            while (s.Pair < R.SuddenDeathAfterPairs + R.SuddenDeathStandingAfterPairs + 2 && guard++ < 400)
                s = Step(s, MatchInput.ReadyUp(s.Actor!.Value, s.PhaseStart + 0.5)).State;
            Assert.That(Match.IsSuddenDeath(s, Cfg), Is.True);
            Assert.That(Match.MinStance(s, Cfg), Is.EqualTo(Stance.Prone));
            Assert.That(s.Players.A.Stance, Is.EqualTo(Stance.Prone));
        }

        [Test]
        public void TwoIdlePhasesForfeitWhenEnabled()
        {
            var s = Match.CreateMatch(1, "training-lane", new PerSide<PlayerSetup>(new PlayerSetup("A", "bolt308"), new PlayerSetup("B", "bolt308")), 0, Cfg, forfeitOnIdle: true);
            s = OpenField(s);
            s = Step(s, MatchInput.Tick(2 + R.MoveSeconds + R.ShotSeconds + R.MoveSeconds + R.ShotSeconds + 1)).State;
            Assert.That(s.Phase, Is.EqualTo(PhaseKind.MatchOver));
            Assert.That(s.Forfeit, Is.True);
        }

        [Test]
        public void FullBotVsBotMatchPlaysEndToEnd()
        {
            var (s, _) = RulesParityTests.PlayBots(2024, BotDifficulty.Medium, BotDifficulty.Medium, "training-lane");
            Assert.That(s.Phase, Is.EqualTo(PhaseKind.MatchOver));
            Assert.That(s.Winner, Is.Not.Null);
            Assert.That(s.Players[s.Winner!.Value].Kills, Is.EqualTo(R.KillsToWin));
            Assert.That(s.Shots.Count, Is.GreaterThan(0));
        }

        [Test]
        public void BotsFinishAMatchOnEveryMap()
        {
            foreach (var mapId in Maps.Ids)
            {
                var (s, _) = RulesParityTests.PlayBots(77, BotDifficulty.Medium, BotDifficulty.Medium, mapId);
                Assert.That(s.Phase, Is.EqualTo(PhaseKind.MatchOver), mapId);
                Assert.That(s.Forfeit, Is.False, mapId);
            }
        }

        [Test]
        public void BotsFinishATurnLockedMatchOnEveryMapWithoutMovingWhileHeld()
        {
            foreach (var mapId in Maps.Ids)
            {
                var (s, steps) = RulesParityTests.PlayBots(77, BotDifficulty.Medium, BotDifficulty.Medium, mapId, turnLock: true);
                Assert.That(s.Phase, Is.EqualTo(PhaseKind.MatchOver), mapId);
                Assert.That(s.Forfeit, Is.False, mapId);
                foreach (var step in steps)
                    Assert.That((string?)step["error"], Is.Null, $"{mapId}: {step["input"]}");
            }
        }

        [Test]
        public void HardBeatsEasyMoreOftenThanNot()
        {
            int hardWins = 0;
            for (uint i = 0; i < 6; i++)
                if (RulesParityTests.PlayBots(100 + i, BotDifficulty.Easy, BotDifficulty.Hard, "training-lane").state.Winner == Side.B) hardWins++;
            Assert.That(hardWins, Is.GreaterThanOrEqualTo(4));
        }
    }
}
