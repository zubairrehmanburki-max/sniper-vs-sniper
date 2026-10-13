/* The practice range: one lane, steel silhouettes at fixed distances, seeded layered wind. */
using System;
using System.Collections.Generic;
using SniperDuel.Ballistics;

namespace SniperDuel.Rules
{
    public sealed class PracticeRange
    {
        public uint Seed;
        public List<Silhouette> Targets = new List<Silhouette>();
        public LaneWind Wind = new LaneWind();
        public Atmosphere Atmosphere = Atmosphere.Icao;
        public double Latitude;
    }

    public static class Practice
    {
        /// <summary>
        /// Builds the range for a seed. The shooter sits at the origin looking north (−z); targets are
        /// spread a little left and right so each needs its own windage and a real aim change.
        /// </summary>
        public static PracticeRange CreatePracticeRange(uint seed, GameConfig cfg)
        {
            var pr = cfg.PracticeRange;
            var rng = new Rng(Rng.HashSeed(seed, 0x7067));
            var t = cfg.Target;
            var targets = new List<Silhouette>();
            for (int i = 0; i < pr.TargetsM.Length; i++)
            {
                double d = pr.TargetsM[i];
                targets.Add(new Silhouette
                {
                    Id = FormattableString.Invariant($"steel-{d}"),
                    Position = new Vec3((i % 2 == 0 ? -1 : 1) * (4 + 6 * i), 0, -d),
                    FacingYaw = Math.PI,
                    Width = t.WidthM,
                    Height = t.HeightM,
                    Head = t.HeadM,
                });
            }
            double lo = pr.Wind.BaseSpeedMps[0], hi = pr.Wind.BaseSpeedMps[1];
            double baseSpeed = lo + (hi - lo) * rng.Next();
            // Mostly a crosswind (the skill test), from either side.
            double side = rng.Next() < 0.5 ? -1 : 1;
            double baseDir = side * (Math.PI / 2) + (rng.Next() * 2 - 1) * (pr.Wind.DirectionJitterDeg * Math.PI) / 180;
            var zones = new List<Vec3>();
            for (int i = 0; i <= pr.WindZonesM.Length; i++)
            {
                double speed = baseSpeed * (1 + (rng.Next() * 2 - 1) * pr.Wind.ZoneSpeedJitter);
                double dir = baseDir + (rng.Next() * 2 - 1) * 0.25;
                zones.Add(WindBlowingToward(speed, dir));
            }
            return new PracticeRange
            {
                Seed = seed,
                Targets = targets,
                Wind = new LaneWind
                {
                    Start = new Vec3(0, 0, 0),
                    Direction = new Vec3(0, 0, -1),
                    Edges = pr.WindZonesM,
                    Zones = zones,
                    Gust = new GustModel { Amplitude = pr.Wind.GustAmplitude, Period = pr.Wind.GustPeriodSeconds, Seed = Rng.HashSeed(seed, 0x67) },
                },
                Atmosphere = RifleSetup.AtmosphereFor(pr.Atmosphere),
                Latitude = pr.Latitude,
            };
        }

        /// <summary>
        /// World wind vector for air moving toward compass-like direction `dir` measured from north (−z)
        /// toward east (+x). E.g. dir = π/2 blows toward +x (a crosswind from the shooter's left).
        /// </summary>
        public static Vec3 WindBlowingToward(double speed, double dir) => new Vec3(speed * Math.Sin(dir), 0, -speed * Math.Cos(dir));
    }
}
