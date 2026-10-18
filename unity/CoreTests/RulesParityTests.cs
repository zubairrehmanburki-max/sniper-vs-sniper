using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SniperDuel.Ballistics;
using SniperDuel.Rules;
using static SniperDuel.Tests.RulesFixture;

namespace SniperDuel.Tests
{
    /// <summary>
    /// The C# rules must agree with the TypeScript referee (unity/CoreTests/Golden/rules.gen.ts): the
    /// same maps, hit verdicts, reducer transitions and seeded bot decisions.
    /// </summary>
    public class RulesParityTests
    {
        static readonly JObject Golden = Repo.Json("unity", "CoreTests", "Golden", "rules.json");
        static GameConfig C => Cfg;

        static void Expect(JToken expected, JToken actual, string path) => JsonParity.Expect(expected, actual, path);

        [Test]
        public void MapsMatchTypeScript()
        {
            var ids = Golden["maps"]!.Select(m => (string)m["id"]!).ToArray();
            Assert.That(Maps.Ids, Is.EqualTo(ids));
            foreach (var g in Golden["maps"]!)
            {
                var m = Maps.ById((string)g["id"]!);
                string at = m.Id;
                var actual = new JObject
                {
                    ["id"] = m.Id,
                    ["laneLength"] = Num(m.LaneLength),
                    ["fixed"] = m.Fixed,
                    ["zones"] = new JArray(Sides.Select(s => Arr(Num(m.Zones[s].XMin), Num(m.Zones[s].XMax), Num(m.Zones[s].SMin), Num(m.Zones[s].SMax), Num(m.Zones[s].Front)))),
                    ["cover"] = new JArray(m.Cover.Select(b => new JObject
                    {
                        ["id"] = b.Id,
                        ["min"] = V(b.Min),
                        ["max"] = V(b.Max),
                        ["material"] = Js(b.Material),
                        ["look"] = Str(b.Look),
                    })),
                    ["spots"] = new JArray(Sides.Select(s => new JArray(m.Spots[s].Select(p => V(p))))),
                };
                foreach (var p in actual.Properties()) Expect(g[p.Name]!, p.Value, $"{at}.{p.Name}");

                foreach (var gs in g["groundSamples"]!)
                    Expect(gs[2]!, Num(m.Ground((double)gs[0]!, (double)gs[1]!)), $"{at} ground({gs[0]}, {gs[1]})");

                var clamps = new JArray();
                foreach (var side in Sides)
                {
                    var z = m.Zones[side];
                    var pts = new[] { (0, z.SMin), (-500, z.Front), (7.3, (z.SMin + z.SMax) / 2), (-38, 70.4), (-16, 78), (100, 1e4), (-8, 46), (28, 408) };
                    foreach (var (x, s) in pts)
                    {
                        var p = Maps.ClampToZone(m, z, new Vec3(x, 0, -s));
                        clamps.Add(Arr(Num(x), Num(s), Num(p.X), Num(p.Y), Num(p.Z)));
                    }
                    foreach (double shrink in new[] { 0, 0.3, 1 })
                    {
                        var u = Maps.UsableZone(m, side, shrink, C.Rules.MinZoneDepthM);
                        clamps.Add(Arr(Num(shrink), Num(u.XMin), Num(u.XMax), Num(u.SMin), Num(u.SMax), Num(u.Front)));
                    }
                }
                Expect(g["clamps"]!, clamps, $"{at}.clamps");

                var los = new JArray();
                foreach (var a in m.Spots.A)
                foreach (var b in m.Spots.B)
                foreach (var st in Stances)
                    los.Add(Visibility.LineOfSight(Combat.EyeOf(a, st, C), Combat.EyeOf(b, st, C), m.Ground, m.Cover));
                Expect(g["los"]!, los, $"{at}.los");

                var conditions = new JArray(new[] { 1, 2, 3 }.Select(round => ConditionsSummary(Match.RollConditions(m, 4242, round, C))));
                Expect(g["conditions"]!, conditions, $"{at}.conditions");
            }
        }

        [Test]
        public void ShotWindFieldsMatchTypeScript()
        {
            var lane = new LaneWind
            {
                Start = new Vec3(0, 0, 0),
                Direction = new Vec3(0, 0, -1),
                Edges = new double[] { 300, 600 },
                Zones = new List<Vec3> { new Vec3(1, 0, 0.5), new Vec3(2, 0, -0.3), new Vec3(3, 0.1, 0) },
                Gust = new GustModel { Amplitude = 0.3, Period = 7, Seed = 5 },
            };
            foreach (var g in Golden["windFields"]!)
            {
                double x = (double)g["x"]!, z = (double)g["z"]!, yaw = (double)g["yaw"]!;
                var f = World.ShotWindField(lane, new Vec3(x, 1, z), yaw);
                Assert.That(f.Gust, Is.SameAs(lane.Gust));
                var zones = new JArray(f.Zones.Select(zn => Arr(Num(zn.UntilRange), Num(zn.Vector.X), Num(zn.Vector.Y), Num(zn.Vector.Z))));
                Expect(g["zones"]!, zones, $"windField({x}, {z}, {yaw})");
            }
        }

        [Test]
        public void SwayAndBreathMatchTypeScript()
        {
            var sway = new JArray();
            foreach (var stance in Stances)
            foreach (bool bipod in new[] { false, true })
            foreach (double t in new[] { 0, 0.37, 2.5, 10.1, 33.3 })
            {
                var o = Sway.Offset(new SwayInput { Stance = stance, Bipod = bipod, Breath = Sway.InitialBreath(), Seed = 1234567, Penalty = bipod ? 1 : 1.4 }, t, C);
                sway.Add(Arr((int)stance, bipod ? 1 : 0, Num(t), Num(o.Right), Num(o.Up)));
            }
            Expect(Golden["sway"]!, sway, "sway");

            var held = Sway.HoldBreath(Sway.InitialBreath(), 3, C);
            var released = Sway.ReleaseBreath(held, 12, C);
            var rehold = Sway.HoldBreath(released, 13, C);
            var maxSeed = Sway.Offset(new SwayInput { Stance = Stance.Crouch, Bipod = false, Breath = Sway.InitialBreath(), Seed = 4294967295 }, 7.7, C);
            var breath = new JObject
            {
                ["held"] = Arr(Num(held.Since), Num(held.StrainAtRelease)),
                ["released"] = Arr(Num(released.Since), Num(released.StrainAtRelease)),
                ["rehold"] = Arr(Num(rehold.Since), Num(rehold.StrainAtRelease)),
                ["factors"] = new JArray(new double[] { 0, 3.2, 3.6, 5, 8.1, 9, 12, 14, 30 }.Select(t =>
                    Arr(Num(Sway.BreathFactor(held, t, C)), Num(Sway.BreathFactor(released, t, C)), Num(Sway.BreathFactor(rehold, t, C))))),
                ["maxSeed"] = Arr(Num(maxSeed.Right), Num(maxSeed.Up)),
            };
            Expect(Golden["breath"]!, breath, "breath");
        }

        [Test]
        public void HitVerdictsMatchTypeScript()
        {
            int n = 0;
            foreach (var g in Golden["shots"]!)
            {
                var m = Maps.ById((string)g["map"]!);
                var c = Match.RollConditions(m, 99, 1, C);
                var side = (string)g["side"]! == "A" ? Side.A : Side.B;
                var targetSide = Match.Other(side);
                var shooterPos = m.Spots[side][0];
                var targetPos = m.Spots[targetSide][m.Spots[targetSide].Count - 1];
                var shooterStance = (Stance)Enum.Parse(typeof(Stance), (string)g["shooterStance"]!, true);
                var targetStance = (Stance)Enum.Parse(typeof(Stance), (string)g["targetStance"]!, true);
                var gi = g["input"]!;
                var input = new FireInput { Yaw = (double)gi[0]!, Pitch = (double)gi[1]!, DialElevationMrad = (double)gi[2]!, DialWindageMrad = (double)gi[3]!, Cant = (double)gi[4]! };
                var outcome = Combat.ResolveShot(new ShotRequest
                {
                    Map = m,
                    Conditions = c,
                    RifleId = (string)g["rifleId"]!,
                    ShooterPosition = shooterPos,
                    ShooterStance = shooterStance,
                    Target = new ShotTarget { Id = targetSide.ToString(), Side = targetSide, Position = targetPos, Stance = targetStance, Wounds = (int)g["wounds"]! },
                    Input = input,
                    Seed = 99,
                    ShotIndex = (int)g["shotIndex"]!,
                    FireTime = (double)g["fireTime"]!,
                    ColdBore = (bool)g["coldBore"]!,
                }, C);
                string at = $"{m.Id} {side} {g["name"]}";
                Expect(g["outcome"]!, OutcomeSummary(outcome), at);

                var eye = Combat.EyeOf(shooterPos, shooterStance, C);
                var sil = Combat.SilhouetteFor(targetSide.ToString(), targetPos, targetStance, Combat.FacingYaw(targetSide), C);
                // The rangefinder was pointed along the unperturbed aim; the golden input differs only in dial.
                var ranges = new JArray(new[] { 0, -0.002, -0.01, -0.05 }.Select(dp =>
                {
                    double? r = Hits.Rangefind(eye, new Aim(input.Yaw, input.Pitch + dp), new[] { sil }, m.Ground, 3000, m.Cover);
                    return r.HasValue ? Num(r.Value) : JValue.CreateNull();
                }));
                Expect(g["ranges"]!, ranges, at + " ranges");
                n++;
            }
            Assert.That(n, Is.EqualTo(112));
        }

        [Test]
        public void PracticeRangeAndEloMatchTypeScript()
        {
            foreach (var g in Golden["practice"]!)
            {
                uint seed = (uint)(long)g["seed"]!;
                var r = Practice.CreatePracticeRange(seed, C);
                var actual = new JObject
                {
                    ["seed"] = (long)r.Seed,
                    ["targets"] = new JArray(r.Targets.Select(t => new JObject
                    {
                        ["id"] = t.Id,
                        ["position"] = V(t.Position),
                        ["facingYaw"] = Num(t.FacingYaw),
                        ["width"] = Num(t.Width),
                        ["height"] = Num(t.Height),
                        ["head"] = Num(t.Head),
                    })),
                    ["zones"] = new JArray(r.Wind.Zones.Select(z => V(z))),
                    ["gust"] = Arr(Num(r.Wind.Gust!.Amplitude), Num(r.Wind.Gust.Period), (long)r.Wind.Gust.Seed),
                    ["atmosphere"] = Arr(Num(r.Atmosphere.AltitudeM), Num(r.Atmosphere.PressureHPa), Num(r.Atmosphere.TemperatureC), Num(r.Atmosphere.Humidity)),
                    ["latitude"] = Num(r.Latitude),
                };
                Expect(g, actual, $"practice({seed})");
            }
            foreach (var g in Golden["elo"]!)
            {
                var i = g["input"]!;
                var w = new Rating((double)i[0]!, (int)i[1]!);
                var l = new Rating((double)i[2]!, (int)i[3]!);
                var r = Elo.RateMatch(w, l);
                Expect(g["expected"]!, Num(Elo.ExpectedScore(w.Value, l.Value)), "elo expected");
                Assert.That(r.Winner.Value, Is.EqualTo((double)g["winner"]![0]!), "winner rating");
                Assert.That(r.Winner.Games, Is.EqualTo((int)g["winner"]![1]!));
                Assert.That(r.Loser.Value, Is.EqualTo((double)g["loser"]![0]!), "loser rating");
                Assert.That(r.Loser.Games, Is.EqualTo((int)g["loser"]![1]!));
            }
        }

        [Test]
        public void ScriptedMatchesReplayIdentically()
        {
            foreach (var script in Golden["scripts"]!)
            {
                string name = (string)script["name"]!;
                var rifles = script["rifles"]!;
                var s = Match.CreateMatch(
                    (uint)(long)script["seed"]!,
                    (string)script["mapId"]!,
                    new PerSide<PlayerSetup>(new PlayerSetup("A", (string)rifles[0]!), new PlayerSetup("B", (string)rifles[1]!)),
                    0,
                    C,
                    (bool)script["forfeitOnIdle"]!);
                var steps = (JArray)script["steps"]!;
                Expect(steps[0]["state"]!, StateSummary(s), $"{name} initial state");
                Expect(steps[0]["conditions"]!, ConditionsSummary(s.Conditions), $"{name} conditions");
                for (int k = 1; k < steps.Count; k++)
                {
                    var g = steps[k];
                    var input = InputFrom(g["input"]!);
                    var r = Match.ApplyInput(s, input, C);
                    string at = $"{name} step {k} ({input.Type})";
                    Expect(g["input"]!, InputSummary(input), at + " input");
                    Expect(g["error"]!, r.Error == null ? JValue.CreateNull() : new JValue(r.Error), at + " error");
                    Expect(g["outcome"]!, OutcomeSummary(r.Outcome), at + " outcome");
                    Expect(g["state"]!, StateSummary(r.State), at + " state");
                    if (r.Error != null) Assert.That(r.State, Is.SameAs(s), at + ": rejected input leaves the state unchanged");
                    s = r.State;
                }
            }
        }

        [Test]
        public void SeededBotTurnsMatchTypeScript()
        {
            foreach (var g in Golden["botTurns"]!)
            {
                var bot = (BotDifficulty)Enum.Parse(typeof(BotDifficulty), (string)g["bot"]!, true);
                string mapId = (string)g["mapId"]!;
                var map = Maps.ById(mapId);
                var s = Match.CreateMatch(11, mapId, new PerSide<PlayerSetup>(new PlayerSetup("A", "bolt65cm"), new PlayerSetup("B", "mag338", bot)), 0, C);
                string at = $"{bot} on {mapId}";
                Expect(g["setup"]!, new JArray(Bot.Plan(s, Side.B, C).Select(InputSummary)), at + " setup plan");
                void Go(MatchInput i)
                {
                    var r = Match.ApplyInput(s, i, C);
                    Assert.That(r.Error, Is.Null, at);
                    s = r.State;
                }
                if (!map.Fixed)
                {
                    Go(MatchInput.PlaceAt(Side.A, new Vec3(10, 0, -75), Stance.Standing, 1));
                    Go(MatchInput.PlaceAt(Side.B, new Vec3(-10, 0, -(map.Zones.B.SMin + 5)), Stance.Prone, 1));
                }
                Go(MatchInput.ReadyUp(Side.A, 2));
                Go(MatchInput.ReadyUp(Side.B, 2));
                if (s.Phase == PhaseKind.Move) Go(MatchInput.ReadyUp(Side.A, s.PhaseStart + 1));
                Expect(g["shot"]!, new JArray(Bot.Plan(s, Side.B, C).Select(InputSummary)), at + " shot plan");
            }
        }

        static string PhaseId(MatchState s) => $"{s.Round}:{s.Pair}:{s.Step}:{s.Phase}";

        /// <summary>The hotseat/bot driver from match.test.ts, recording every input it applies.</summary>
        public static (MatchState state, JArray steps) PlayBots(uint seed, BotDifficulty a, BotDifficulty b, string mapId, bool turnLock = false)
        {
            var s = Match.CreateMatch(seed, mapId, new PerSide<PlayerSetup>(new PlayerSetup("BotA", "bolt65cm", a), new PlayerSetup("BotB", "mag338", b)), 0, Cfg, turnLock: turnLock);
            var steps = new JArray();
            void Apply(MatchInput input)
            {
                var r = Match.ApplyInput(s, input, Cfg);
                steps.Add(new JObject
                {
                    ["input"] = InputSummary(input),
                    ["error"] = r.Error == null ? JValue.CreateNull() : new JValue(r.Error),
                    ["outcome"] = OutcomeSummary(r.Outcome),
                    ["state"] = StateSummary(r.State),
                });
                if (r.Error == null) s = r.State;
            }
            int phases = 0;
            while (s.Phase != PhaseKind.MatchOver && phases < 2000)
            {
                phases++;
                if (s.Phase == PhaseKind.RoundOver)
                {
                    Apply(MatchInput.NextRound(s.PhaseStart + 3));
                    continue;
                }
                var sides = s.Phase == PhaseKind.Setup ? new[] { Side.A, Side.B } : new[] { s.Actor!.Value };
                // Stable sort, like Array.prototype.sort.
                var plan = sides.SelectMany(side => Bot.Plan(s, side, Cfg)).OrderBy(x => x.Time).ToList();
                string phaseId = PhaseId(s);
                foreach (var input in plan)
                {
                    if (PhaseId(s) != phaseId) break;
                    Apply(input);
                }
                if (PhaseId(s) == phaseId) Apply(MatchInput.Tick(s.PhaseEnd + 0.01));
            }
            return (s, steps);
        }

        [Test]
        public void BotMatchesReplayIdentically()
        {
            foreach (var g in Golden["botMatches"]!)
            {
                uint seed = (uint)(long)g["seed"]!;
                var a = (BotDifficulty)Enum.Parse(typeof(BotDifficulty), (string)g["a"]!, true);
                var b = (BotDifficulty)Enum.Parse(typeof(BotDifficulty), (string)g["b"]!, true);
                string mapId = (string)g["mapId"]!;
                var (_, steps) = PlayBots(seed, a, b, mapId);
                var expected = (JArray)g["steps"]!;
                string at = $"bots {a} v {b} on {mapId} seed {seed}";
                int n = Math.Min(expected.Count, steps.Count);
                for (int k = 0; k < n; k++) Expect(expected[k], steps[k], $"{at} step {k}");
                Assert.That(steps.Count, Is.EqualTo(expected.Count), at + " step count");
            }
            Console.WriteLine($"worst relative deviation from TypeScript: {JsonParity.WorstRelative:E2}");
        }
    }
}
