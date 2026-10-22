using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SniperDuel.Ballistics;
using SniperDuel.Rules;
using static SniperDuel.Tests.RulesFixture;

namespace SniperDuel.Tests
{
    /// <summary>Variable-distance posts: the round roll, line of sight for every pair, walking, climbing and relocating.</summary>
    public class PostsTests
    {
        static IEnumerable<DuelMap> PostMaps => Maps.Ids.Select(Maps.ById).Where(m => m.Posts != null);

        static MatchState NewMatch(string map, uint seed, int avoid = -1) =>
            Match.CreateMatch(seed, map, new PerSide<PlayerSetup>(new PlayerSetup("A", "bolt308"), new PlayerSetup("B", "bolt308", BotDifficulty.Medium)), 0, Cfg, turnLock: true, variedPosts: true, avoidOpening: avoid);

        /// <summary>Can someone at `from` (in that stance) see any of the body of someone standing at `to`?</summary>
        static bool Sees(DuelMap map, Vec3 from, Stance stance, Vec3 to, Side target, Stance targetStance)
        {
            var eye = Combat.EyeOf(from, stance, Cfg);
            var body = Combat.SilhouetteFor("t", to, targetStance, Combat.FacingYaw(target), Cfg);
            return Visibility.LineOfSight(eye, Combat.ChestOf(body), map.Ground, map.Cover) || Visibility.LineOfSight(eye, Combat.EyeOf(to, targetStance, Cfg), map.Ground, map.Cover);
        }

        [Test]
        public void SomeMapsHavePosts() => Assert.That(PostMaps.Count(), Is.GreaterThanOrEqualTo(2));

        [Test]
        public void EveryPairSeesEachOther()
        {
            // Guaranteed line of sight: from each start spot, kneeling, you see the enemy kneeling at theirs.
            var bad = new List<string>();
            foreach (var map in PostMaps)
                foreach (var pair in Maps.Pairs(map))
                {
                    var a = map.Posts!.A[pair.A];
                    var b = map.Posts!.B[pair.B];
                    if (!Sees(map, a.Spot, Stance.Crouch, b.Spot, Side.B, Stance.Crouch)) bad.Add($"{map.Id}: {a.Id} can't see {b.Id}");
                    if (!Sees(map, b.Spot, Stance.Crouch, a.Spot, Side.A, Stance.Crouch)) bad.Add($"{map.Id}: {b.Id} can't see {a.Id}");
                }
            Assert.That(bad, Is.Empty, string.Join("\n", bad));
        }

        [Test]
        public void PostsStandOnSomething()
        {
            foreach (var map in PostMaps)
                foreach (var side in Sides)
                    foreach (var p in map.Posts![side])
                    {
                        Assert.That(p.Spot.Y, Is.EqualTo(map.Ground(p.Spot.X, p.Spot.Z)).Within(1e-6), $"{map.Id} {p.Id} floats or sinks");
                        var w = p.Walk;
                        Assert.That(p.Spot.X >= w.XMin - 1e-6 && p.Spot.X <= w.XMax + 1e-6 && -p.Spot.Z >= w.SMin - 1e-6 && -p.Spot.Z <= w.SMax + 1e-6, $"{map.Id} {p.Id} starts outside its walk area");
                        // A roof or platform's walk area is one level: no stepping off into the street.
                        // (Hillside posts on the cover maps simply follow the slope.)
                        if (map.Terrain != null && w.XMax - w.XMin <= 30)
                        foreach (var (x, s) in new[] { (w.XMin, w.SMin), (w.XMin, w.SMax), (w.XMax, w.SMin), (w.XMax, w.SMax) })
                            Assert.That(Math.Abs(map.Ground(x, -s) - p.Spot.Y), Is.LessThan(4), $"{map.Id} {p.Id} walk corner ({x}, {s})");
                        foreach (var link in p.Links) Assert.That(Maps.PostIndex(map, side, link), Is.GreaterThanOrEqualTo(0), $"{map.Id} {p.Id} links to missing {link}");
                    }
        }

        [Test]
        public void RoundsNeverRepeatABandAndSeedsReplay()
        {
            foreach (var map in PostMaps)
            {
                var bands = Maps.Pairs(map).Select(p => p.Band).Distinct().Count();
                var seen = new HashSet<RangeBand>();
                for (uint seed = 1; seed < 400; seed++)
                {
                    int prev = -1;
                    for (int round = 1; round <= 5; round++)
                    {
                        int pick = Match.RollPostPair(map, seed, round, prev);
                        Assert.That(pick, Is.EqualTo(Match.RollPostPair(map, seed, round, prev)));
                        var band = Maps.Pairs(map)[pick].Band;
                        seen.Add(band);
                        if (prev >= 0 && bands > 1) Assert.That(band, Is.Not.EqualTo(Maps.Pairs(map)[prev].Band), $"{map.Id} seed {seed} round {round}");
                        prev = pick;
                    }
                }
                Assert.That(seen.Count, Is.EqualTo(bands), $"{map.Id}: every band comes up");
            }
        }

        [Test]
        public void FirstRoundAvoidsLastOpening()
        {
            foreach (var map in PostMaps)
                for (uint seed = 1; seed < 100; seed++)
                {
                    var first = NewMatch(map.Id, seed).PostPair;
                    var again = NewMatch(map.Id, seed, first).PostPair;
                    if (Maps.Pairs(map).Count(p => p.Band == Maps.Pairs(map)[first].Band) > 1) Assert.That(again, Is.Not.EqualTo(first));
                }
        }

        [Test]
        public void PlayersStartAtTheRolledPosts()
        {
            foreach (var map in PostMaps)
            {
                var s = NewMatch(map.Id, 7);
                var pair = Maps.Pairs(map)[s.PostPair];
                Assert.That(s.Players.A.Position, Is.EqualTo(map.Posts!.A[pair.A].Spot));
                Assert.That(s.Players.B.Position, Is.EqualTo(map.Posts!.B[pair.B].Spot));
            }
        }

        [Test]
        public void ClassicMatchesIgnorePosts()
        {
            var s = Match.CreateMatch(7, "harbor-rooftops", new PerSide<PlayerSetup>(new PlayerSetup("A", "bolt308"), new PlayerSetup("B", "bolt308")), 0, Cfg, turnLock: true);
            Assert.That(s.PostPair, Is.EqualTo(-1));
            Assert.That(s.Players.A.Position, Is.EqualTo(Maps.ById("harbor-rooftops").Spots.A[0]));
        }

        /// <summary>Runs the match to the first turn of `side` (setup done, both ready).</summary>
        static MatchState ToTurnOf(MatchState s, Side side)
        {
            s = Match.ApplyInput(s, MatchInput.ReadyUp(Side.A, 1), Cfg).State;
            s = Match.ApplyInput(s, MatchInput.ReadyUp(Side.B, 1), Cfg).State;
            for (int i = 0; i < 6 && !(s.Actor == side && s.Phase == PhaseKind.Shot); i++)
                s = Match.ApplyInput(s, MatchInput.ReadyUp(s.Actor!.Value, s.PhaseStart + 1), Cfg).State;
            Assert.That(s.Actor, Is.EqualTo(side));
            return s;
        }

        [Test]
        public void RelocatingEndsYourTurnAtTheNewPost()
        {
            var map = Maps.ById("harbor-rooftops");
            var s = ToTurnOf(NewMatch(map.Id, 11), Side.A);
            var from = map.Posts!.A[s.Players.A.Post];
            var to = Maps.PostIndex(map, Side.A, from.Links[0]);
            // Not the enemy's to move, and not somewhere unlinked.
            Assert.That(Match.ApplyInput(s, MatchInput.RelocateTo(Side.B, 0, Stance.Prone, s.PhaseStart + 1), Cfg).Error, Is.Not.Null);
            var unlinked = Enumerable.Range(0, map.Posts.A.Count).FirstOrDefault(i => i != s.Players.A.Post && Array.IndexOf(from.Links, map.Posts.A[i].Id) < 0 && map.Posts.A[i].Group != from.Group, -1);
            if (unlinked >= 0) Assert.That(Match.ApplyInput(s, MatchInput.RelocateTo(Side.A, unlinked, Stance.Prone, s.PhaseStart + 1), Cfg).Error, Is.Not.Null);
            var r = Match.ApplyInput(s, MatchInput.RelocateTo(Side.A, to, Stance.Crouch, s.PhaseStart + 1), Cfg);
            Assert.That(r.Error, Is.Null);
            Assert.That(r.State.Players.A.Post, Is.EqualTo(to));
            Assert.That(r.State.Players.A.Position, Is.EqualTo(map.Posts.A[to].Spot));
            Assert.That(r.State.Players.A.Stance, Is.EqualTo(Stance.Crouch));
            Assert.That(r.State.Players.A.Relocations, Is.EqualTo(1));
            Assert.That(r.State.Phase == PhaseKind.Shot && r.State.Actor == Side.A, Is.False, "the trip ends A's shot turn");
            // Walking now stays on the new roof.
            var walk = map.Posts.A[to].Walk;
            var b = ToTurnOf(r.State, Side.A);
            var w = Match.ApplyInput(b, MatchInput.ShiftTo(Side.A, new Vec3(walk.XMax + 50, 0, -(walk.SMax + 50)), Stance.Prone, b.PhaseStart + 1), Cfg);
            Assert.That(w.Error, Is.Null);
            Assert.That(w.State.Players.A.Position.X, Is.EqualTo(walk.XMax).Within(1e-9));
            Assert.That(w.State.Players.A.Position.Y, Is.EqualTo(map.Posts.A[to].Spot.Y).Within(1e-6));
        }

        [Test]
        public void RelocationsRunOut()
        {
            var map = Maps.ById("harbor-rooftops");
            var s = NewMatch(map.Id, 11);
            for (int trip = 0; trip <= Cfg.Rules.RelocationsPerRound; trip++)
            {
                s = ToTurnOf(s, Side.A);
                var from = map.Posts!.A[s.Players.A.Post];
                var r = Match.ApplyInput(s, MatchInput.RelocateTo(Side.A, Maps.PostIndex(map, Side.A, from.Links[0]), Stance.Prone, s.PhaseStart + 1), Cfg);
                if (trip < Cfg.Rules.RelocationsPerRound) Assert.That(r.Error, Is.Null, $"trip {trip}");
                else Assert.That(r.Error, Is.Not.Null);
                s = r.Error == null ? r.State : s;
            }
        }

        [Test]
        public void HeldAfterFiringMeansNoRelocating()
        {
            var map = Maps.ById("harbor-rooftops");
            var s = ToTurnOf(NewMatch(map.Id, 11), Side.A);
            s.Players.A.Locked = true;
            var from = map.Posts!.A[s.Players.A.Post];
            Assert.That(Match.ApplyInput(s, MatchInput.RelocateTo(Side.A, Maps.PostIndex(map, Side.A, from.Links[0]), Stance.Prone, s.PhaseStart + 1), Cfg).Error, Is.Not.Null);
        }

        [Test]
        public void BotMovesPostWhenItKeepsMissing()
        {
            var map = Maps.ById("harbor-rooftops");
            int moved = 0;
            for (uint seed = 1; seed < 60; seed++)
            {
                var s = ToTurnOf(NewMatch(map.Id, seed), Side.B);
                foreach (var miss in new[] { 1, 2 })
                    s.Shots.Add(new ShotRecord { Round = s.Round, Shooter = Side.B, Verdict = Verdict.Miss, Input = new FireInput() });
                var plan = Bot.Plan(s, Side.B, Cfg);
                if (plan.Count == 1 && plan[0].Type == MatchInputType.Relocate)
                {
                    moved++;
                    Assert.That(Match.ApplyInput(s, plan[0], Cfg).Error, Is.Null);
                }
            }
            Assert.That(moved, Is.InRange(5, 55), "medium bot relocates some of the time");
        }

        [Test]
        public void PrintRanges()
        {
            foreach (var map in PostMaps)
                foreach (var p in Maps.Pairs(map))
                    TestContext.Progress.WriteLine($"{map.Id}: {map.Posts!.A[p.A].Id} - {map.Posts.B[p.B].Id}: {p.Distance:F0} m {p.Band}");
        }
    }
}
