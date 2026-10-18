using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SniperDuel.Ballistics;
using Env = SniperDuel.Ballistics.Environment;

namespace SniperDuel.Tests
{
    static class Repo
    {
        /// <summary>Repository root, found by walking up from the test binary.</summary>
        public static string Root
        {
            get
            {
                string? env = System.Environment.GetEnvironmentVariable("SVS_REPO_ROOT");
                if (!string.IsNullOrEmpty(env)) return env!;
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pnpm-workspace.yaml"))) dir = dir.Parent;
                return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
            }
        }

        public static JObject Json(params string[] path) => JObject.Parse(File.ReadAllText(Path.Combine(new[] { Root }.Concat(path).ToArray())));
    }

    /// <summary>
    /// Exit gate carried over from packages/ballistics/test/validation.test.ts: dope tables within 1% of
    /// py-ballisticcalc 3.1.0 to 1,000 m (1,200 m for the .338).
    /// </summary>
    public class ValidationTests
    {
        static readonly JObject Reference = Repo.Json("packages", "ballistics", "reference", "reference-tables.json");

        static Projectile ProjectileFor(string load)
        {
            var l = Reference["loads"]![load]!;
            return new Projectile
            {
                DragModel = (string)l["table"]! == "G1" ? DragModel.G1 : DragModel.G7,
                Bc = (double)l["bc"]!,
                MassKg = (double)l["grains"]! * Units.Grain,
                DiameterM = (double)l["diameter_in"]! * Units.Inch,
                LengthM = (double)l["length_in"]! * Units.Inch,
            };
        }

        static (Shot shot, Env env) SetupFor(JToken s)
        {
            string load = (string)s["load"]!;
            var l = Reference["loads"]![load]!;
            var c = s["coriolis"]!;
            bool spin = (bool)s["spinDrift"]!;
            var a = Reference["atmospheres"]![(string)s["atmosphere"]!]!;
            var shot = new Shot
            {
                Projectile = ProjectileFor(load),
                MuzzleVelocity = (double)l["mv"]!,
                SightHeight = (double)Reference["sightHeightMm"]! / 1000,
                Twist = spin ? (double)l["twist_in"]! * Units.Inch : 0,
                Azimuth = c.Type == JTokenType.Null ? (double?)null : (double)c["azimuth"]! * Math.PI / 180,
            };
            var env = new Env
            {
                Atmosphere = new Atmosphere((double)a["altitudeM"]!, (double)a["pressureHPa"]!, (double)a["temperatureC"]!, (double)a["humidity"]!),
                Wind = WindField.Uniform((double)Reference["crosswindMps"]!, 90),
                Latitude = c.Type == JTokenType.Null ? (double?)null : (double)c["latitude"]!,
            };
            return (shot, env);
        }

        static Trajectory Fly(JToken s, double zeroElevation)
        {
            var (shot, env) = SetupFor(s);
            shot.ZeroElevation = zeroElevation;
            var rows = (JArray)s["rows"]!;
            return Solver.Simulate(shot, env, new SolveOptions
            {
                MaxRange = (double)rows.Last!["range"]!,
                Effects = new Effects { SpinDrift = (bool)s["spinDrift"]!, Coriolis = s["coriolis"]!.Type != JTokenType.Null },
            });
        }

        static void ExpectClose(double actual, double expected, string label)
        {
            double tol = Math.Max(0.01 * Math.Abs(expected), 0.005);
            Assert.That(Math.Abs(actual - expected), Is.LessThanOrEqualTo(tol), $"{label}: got {actual:0.0000}, reference {expected:0.0000}");
        }

        [Test]
        public void DopeTablesWithinOnePercent()
        {
            int checkedRows = 0;
            foreach (var s in Reference["scenarios"]!)
            {
                var traj = Fly(s, (double)s["zeroElevationMrad"]! / 1000);
                foreach (var row in s["rows"]!)
                {
                    double range = (double)row["range"]!;
                    var p = traj.AtRange(range);
                    Assert.That(p.HasValue, $"{s["name"]} {range} m reached");
                    var rel = LosOffset.Of(p!.Value, 0);
                    string at = $"{s["name"]} {range} m";
                    ExpectClose(rel.Height, (double)row["height"]!, at + " height");
                    ExpectClose(rel.Lateral, (double)row["windage"]!, at + " windage");
                    Assert.That(Math.Abs(p.Value.Speed - (double)row["velocity"]!) / (double)row["velocity"]!, Is.LessThan(0.005), at + " velocity");
                    Assert.That(Math.Abs(p.Value.T - (double)row["time"]!) / (double)row["time"]!, Is.LessThan(0.005), at + " time");
                    checkedRows++;
                }
            }
            Assert.That(checkedRows, Is.GreaterThan(50));
        }

        [Test]
        public void ZeroingMatchesReference()
        {
            foreach (var s in Reference["scenarios"]!.Where(x => x["coriolis"]!.Type == JTokenType.Null))
            {
                var (shot, env) = SetupFor(s);
                double zero = Solutions.SolveZero(shot, env, (double)Reference["zeroRangeM"]!);
                double want = (double)s["zeroElevationMrad"]!;
                Assert.That(Math.Abs(zero * 1000 - want) / want, Is.LessThan(0.001), (string)s["name"]!);
            }
        }
    }

    /// <summary>The C# port must agree with the TypeScript solver (unity/CoreTests/Golden/ballistics.gen.ts).</summary>
    public class ParityTests
    {
        static readonly JObject Golden = Repo.Json("unity", "CoreTests", "Golden", "ballistics.json");

        [Test]
        public void RngIsBitIdentical()
        {
            var rng = new Rng(Rng.HashSeed(1234, 7));
            foreach (var v in Golden["rngSeq"]!) Assert.That(rng.Next(), Is.EqualTo((double)v));
            foreach (var v in Golden["normals"]!) Assert.That(rng.Normal(), Is.EqualTo((double)v).Within(1e-12));
            var hashes = new[] { Rng.HashSeed(0), Rng.HashSeed(1, 2, 3), Rng.HashSeed(4294967295, -5), Rng.HashSeed(0x6d76, 99) };
            Assert.That(hashes.Select(h => (long)h), Is.EqualTo(Golden["hashes"]!.Select(v => (long)v)));
            var ts = new[] { 0, 0.37, 1.5, -2.25, 17.9 };
            var noise = Golden["noise"]!.Select(v => (double)v).ToArray();
            for (int i = 0; i < ts.Length; i++) Assert.That(Rng.ValueNoise1D(42, ts[i]), Is.EqualTo(noise[i]).Within(1e-15));
            var gt = new[] { 0, 3.3, 10, 41.7 };
            var gusts = Golden["gusts"]!.Select(v => (double)v).ToArray();
            var gust = new GustModel { Amplitude = 0.3, Period = 4, Seed = 77 };
            for (int i = 0; i < gt.Length; i++) Assert.That(WindField.GustFactor(gust, gt[i]), Is.EqualTo(gusts[i]).Within(1e-15));
        }

        [Test]
        public void SeededTrajectoryMatchesTypeScript()
        {
            var projectile = new Projectile { DragModel = DragModel.G7, Bc = 0.322, MassKg = 250 * Units.Grain, DiameterM = 0.338 * Units.Inch, LengthM = 1.555 * Units.Inch };
            var shot = new Shot { Projectile = projectile, MuzzleVelocity = 900, MuzzleVelocitySd = 4, SightHeight = 0.045, Twist = 10 * Units.Inch };
            var wind = new WindField { Gust = new GustModel { Amplitude = 0.25, Period = 5, Seed = 99 } };
            wind.Zones.Add(new WindZone(300, new Vec3(1, 0, 3)));
            wind.Zones.Add(new WindZone(700, new Vec3(-0.5, 0.2, 5.5)));
            wind.Zones.Add(new WindZone(double.PositiveInfinity, new Vec3(0, 0, 2)));
            var env = new Env { Atmosphere = new Atmosphere(300, 980, 22, 0.6), Wind = wind };

            double zero = Solutions.SolveZero(shot, env, 100);
            Assert.That(zero, Is.EqualTo((double)Golden["zero"]!).Within(1e-12));
            shot.ZeroElevation = zero;
            shot.LosElevation = 0.03;
            shot.Cant = 0.02;
            shot.DialElevation = 0.0042;
            shot.DialWindage = 0.0011;
            var traj = Solver.Simulate(shot, env, new SolveOptions
            {
                MaxRange = 1300,
                Seed = 31337,
                FireTime = 12.5,
                Effects = new Effects { SpinDrift = true, AerodynamicJump = true, TransonicDispersion = true },
            });
            Assert.That(traj.MuzzleVelocity, Is.EqualTo((double)Golden["muzzleVelocity"]!).Within(1e-9));
            Assert.That(traj.AerodynamicJump, Is.EqualTo((double)Golden["aerodynamicJump"]!).Within(1e-15));
            Assert.That(traj.Stability, Is.EqualTo((double)Golden["stability"]!).Within(1e-12));
            foreach (var g in Golden["samples"]!)
            {
                var p = traj.AtRange((double)g["range"]!)!.Value;
                Assert.That(p.T, Is.EqualTo((double)g["t"]!).Within(1e-9), "t");
                Assert.That(p.Y, Is.EqualTo((double)g["y"]!).Within(1e-7), "y");
                Assert.That(p.Z, Is.EqualTo((double)g["z"]!).Within(1e-7), "z");
                Assert.That(p.Speed, Is.EqualTo((double)g["speed"]!).Within(1e-6), "speed");
            }

            shot.DialElevation = 0;
            shot.DialWindage = 0;
            var sol = Solutions.SolveFiringSolution(shot, env, 640);
            Assert.That(sol.Elevation, Is.EqualTo((double)Golden["solution"]!["elevation"]!).Within(1e-9));
            Assert.That(sol.Windage, Is.EqualTo((double)Golden["solution"]!["windage"]!).Within(1e-9));
        }
    }

    public class PhysicsTests
    {
        [Test]
        public void IcaoSeaLevel()
        {
            Assert.That(Air.Density(15, 1013.25, 0), Is.EqualTo(1.225).Within(0.005));
            Assert.That(Air.SpeedOfSound(15), Is.EqualTo(340.29).Within(0.05));
        }

        [Test]
        public void NegligibleDragDropsHalfGTSquared()
        {
            var shot = new Shot
            {
                Projectile = new Projectile { DragModel = DragModel.G7, Bc = 1e9, MassKg = 0.01, DiameterM = 0.008, LengthM = 0.03 },
                MuzzleVelocity = 792.5,
            };
            var p = Solver.Simulate(shot, new Env(), new SolveOptions { MaxRange = 2000 }).AtTime(1)!.Value;
            Assert.That(p.Y, Is.EqualTo(-0.5 * Units.Gravity).Within(1e-5));
            Assert.That(p.X, Is.EqualTo(792.5).Within(1e-3));
        }
    }
}
