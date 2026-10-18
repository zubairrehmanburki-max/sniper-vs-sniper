using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SniperDuel.Ballistics;
using SniperDuel.Rules;

namespace SniperDuel.Tests
{
    /// <summary>Shared fixtures for the rules tests: the real game config and JSON summaries matching rules.gen.ts.</summary>
    static class RulesFixture
    {
        static GameConfig? _cfg;

        public static GameConfig Cfg => _cfg ??= GameConfig.Parse(File.ReadAllText(Path.Combine(Repo.Root, "config", "game.json")));

        public static readonly Side[] Sides = { Side.A, Side.B };
        public static readonly Stance[] Stances = { Stance.Prone, Stance.Crouch, Stance.Standing };

        /// <summary>TS string literal for an enum value: camelCase, except sides which stay "A"/"B".</summary>
        public static string Js(Enum e)
        {
            string s = e.ToString();
            return e is Side ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        public static JToken Num(double x) => double.IsInfinity(x) || double.IsNaN(x) ? JValue.CreateNull() : new JValue(x);
        public static JToken Str(Enum? e) => e == null ? JValue.CreateNull() : new JValue(Js(e));
        public static JToken S(string? s) => s == null ? JValue.CreateNull() : new JValue(s);
        public static JToken V(Vec3 p) => new JArray(Num(p.X), Num(p.Y), Num(p.Z));
        public static JToken V(Vec3? p) => p.HasValue ? V(p.Value) : JValue.CreateNull();
        public static JArray Arr(params JToken[] items) => new JArray(items);

        public static Vec3 ToVec(JToken t) => new Vec3((double)t[0]!, (double)t[1]!, (double)t[2]!);

        public static JObject ConditionsSummary(Conditions c) => new JObject
        {
            ["atmosphere"] = Arr(Num(c.Atmosphere.AltitudeM), Num(c.Atmosphere.PressureHPa), Num(c.Atmosphere.TemperatureC), Num(c.Atmosphere.Humidity)),
            ["zones"] = new JArray(c.Wind.Zones.Select(z => V(z))),
            ["edges"] = new JArray(c.Wind.Edges.Select(Num)),
            ["gust"] = c.Wind.Gust == null ? JValue.CreateNull() : Arr(Num(c.Wind.Gust.Amplitude), Num(c.Wind.Gust.Period), new JValue((long)c.Wind.Gust.Seed)),
            ["latitude"] = Num(c.Latitude),
        };

        public static JObject StateSummary(MatchState s) => new JObject
        {
            ["round"] = s.Round,
            ["pair"] = s.Pair,
            ["phase"] = Js(s.Phase),
            ["actor"] = Str(s.Actor),
            ["moveFrom"] = V(s.MoveFrom),
            ["phaseStart"] = Num(s.PhaseStart),
            ["phaseEnd"] = Num(s.PhaseEnd),
            ["acted"] = s.Acted,
            ["firstMover"] = Js(s.FirstMover),
            ["step"] = s.Step,
            ["windScale"] = Num(s.WindScale),
            ["shotCount"] = s.ShotCount,
            ["roundWinner"] = Str(s.RoundWinner),
            ["winner"] = Str(s.Winner),
            ["forfeit"] = s.Forfeit,
            ["players"] = new JArray(Sides.Select(side =>
            {
                var p = s.Players[side];
                return Arr(V(p.Position), Js(p.Stance), p.Kills, p.Wounds, V(p.Revealed), p.ColdBore, V(p.HeardSnapFrom), p.Ready, p.IdlePhases);
            })),
        };

        public static JToken OutcomeSummary(ShotOutcome? o)
        {
            if (o == null) return JValue.CreateNull();
            var i = o.Impact;
            return new JObject
            {
                ["verdict"] = Js(o.Verdict),
                ["zone"] = Str(o.Zone),
                ["kind"] = Js(i.Kind),
                ["targetId"] = S(i.TargetId),
                ["coverId"] = S(i.CoverId),
                ["point"] = V(i.Point),
                ["t"] = Num(i.T),
                ["speed"] = Num(i.Speed),
                ["energy"] = Num(i.Energy),
                ["local"] = i.Local.HasValue ? Arr(Num(i.Local.Value.U), Num(i.Local.Value.V)) : JValue.CreateNull(),
                ["penetrated"] = i.Penetrated == null ? JValue.CreateNull() : new JArray(i.Penetrated),
                ["nearMiss"] = o.NearMiss,
                ["call"] = o.Call.HasValue ? Arr(Num(o.Call.Value.UpMrad), Num(o.Call.Value.RightMrad)) : JValue.CreateNull(),
                ["eye"] = V(o.Eye),
                ["seed"] = (long)o.Seed,
            };
        }

        public static JObject InputSummary(MatchInput i)
        {
            bool hasSide = i.Type != MatchInputType.Tick && i.Type != MatchInputType.NextRound;
            return new JObject
            {
                ["type"] = Js(i.Type),
                ["time"] = Num(i.Time),
                ["side"] = hasSide ? Str(i.Side) : JValue.CreateNull(),
                ["position"] = i.Type == MatchInputType.Place ? V(i.Position) : JValue.CreateNull(),
                ["stance"] = i.Type == MatchInputType.Place ? Str(i.Stance) : JValue.CreateNull(),
                ["fire"] = i.Fire != null ? Arr(Num(i.Fire.Yaw), Num(i.Fire.Pitch), Num(i.Fire.DialElevationMrad), Num(i.Fire.DialWindageMrad), Num(i.Fire.Cant)) : JValue.CreateNull(),
            };
        }

        static T ParseEnum<T>(string s) where T : struct, Enum => (T)Enum.Parse(typeof(T), s, true);

        /// <summary>Rebuilds a MatchInput from its golden summary.</summary>
        public static MatchInput InputFrom(JToken t)
        {
            var type = ParseEnum<MatchInputType>((string)t["type"]!);
            var input = new MatchInput { Type = type, Time = (double)t["time"]! };
            if (t["side"]!.Type != JTokenType.Null) input.Side = ParseEnum<Side>((string)t["side"]!);
            if (t["position"]!.Type != JTokenType.Null) input.Position = ToVec(t["position"]!);
            if (t["stance"]!.Type != JTokenType.Null) input.Stance = ParseEnum<Stance>((string)t["stance"]!);
            if (t["fire"]!.Type != JTokenType.Null)
            {
                var f = t["fire"]!;
                input.Fire = new FireInput { Yaw = (double)f[0]!, Pitch = (double)f[1]!, DialElevationMrad = (double)f[2]!, DialWindageMrad = (double)f[3]!, Cant = (double)f[4]! };
            }
            return input;
        }
    }

    /// <summary>
    /// Structural JSON comparison: same shape, same strings and booleans, integers exact, floating
    /// point within a relative tolerance (Math.sin and friends may differ in the last bit between V8
    /// and .NET; trajectories integrate thousands of steps).
    /// </summary>
    static class JsonParity
    {
        public const double Tolerance = 1e-9;
        public static double WorstRelative;

        static bool IsNumber(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;

        public static void Expect(JToken expected, JToken actual, string path)
        {
            if (IsNumber(expected) && IsNumber(actual))
            {
                if (expected.Type == JTokenType.Integer && actual.Type == JTokenType.Integer)
                {
                    Assert.That((long)actual, Is.EqualTo((long)expected), path);
                    return;
                }
                double e = (double)expected, a = (double)actual;
                double rel = Math.Abs(a - e) / Math.Max(1, Math.Abs(e));
                if (rel > WorstRelative) WorstRelative = rel;
                Assert.That(rel, Is.LessThanOrEqualTo(Tolerance), $"{path}: expected {e:R}, got {a:R}");
                return;
            }
            Assert.That(actual.Type == JTokenType.Null, Is.EqualTo(expected.Type == JTokenType.Null), $"{path}: expected {expected}, got {actual}");
            switch (expected.Type)
            {
                case JTokenType.Null:
                    return;
                case JTokenType.Array:
                {
                    var ea = (JArray)expected;
                    Assert.That(actual.Type, Is.EqualTo(JTokenType.Array), path);
                    var aa = (JArray)actual;
                    Assert.That(aa.Count, Is.EqualTo(ea.Count), $"{path}: length");
                    for (int i = 0; i < ea.Count; i++) Expect(ea[i], aa[i], $"{path}[{i}]");
                    return;
                }
                case JTokenType.Object:
                {
                    var eo = (JObject)expected;
                    Assert.That(actual.Type, Is.EqualTo(JTokenType.Object), path);
                    var ao = (JObject)actual;
                    Assert.That(ao.Properties().Select(p => p.Name).OrderBy(n => n), Is.EqualTo(eo.Properties().Select(p => p.Name).OrderBy(n => n)), $"{path}: keys");
                    foreach (var p in eo.Properties()) Expect(p.Value, ao[p.Name]!, $"{path}.{p.Name}");
                    return;
                }
                default:
                    Assert.That(actual.ToString(), Is.EqualTo(expected.ToString()), path);
                    return;
            }
        }
    }
}
