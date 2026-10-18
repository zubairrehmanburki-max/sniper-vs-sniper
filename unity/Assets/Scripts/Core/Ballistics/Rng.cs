using System;

namespace SniperDuel.Ballistics
{
    /// <summary>
    /// Deterministic PRNG utilities, bit-identical to packages/ballistics/src/rng.ts so a seed gives the
    /// same gusts and dispersion on every platform. Never use System.Random or UnityEngine.Random in rules.
    /// </summary>
    public sealed class Rng
    {
        uint _a;
        double? _spare;

        public Rng(uint seed)
        {
            _a = seed;
        }

        /// <summary>Mixes integers into one 32-bit seed (murmur3 finalizer).</summary>
        public static uint HashSeed(params long[] parts)
        {
            unchecked
            {
                uint h = 0x9e3779b9;
                foreach (long p in parts)
                {
                    uint k = (uint)p * 0xcc9e2d51;
                    k = (k << 15) | (k >> 17);
                    k *= 0x1b873593;
                    h ^= k;
                    h = (h << 13) | (h >> 19);
                    h = h * 5 + 0xe6546b64;
                }
                h ^= h >> 16;
                h *= 0x85ebca6b;
                h ^= h >> 13;
                h *= 0xc2b2ae35;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>mulberry32. Uniform float in [0, 1).</summary>
        public double Next()
        {
            unchecked
            {
                _a += 0x6d2b79f5;
                uint t = (_a ^ (_a >> 15)) * (1 | _a);
                t = (t + (t ^ (t >> 7)) * (61 | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        /// <summary>Standard normal draw (Box–Muller, spare cached).</summary>
        public double Normal()
        {
            if (_spare.HasValue)
            {
                double s = _spare.Value;
                _spare = null;
                return s;
            }
            double u = 0;
            while (u == 0) u = Next();
            double v = Next();
            double r = Math.Sqrt(-2 * Math.Log(u));
            _spare = r * Math.Sin(2 * Math.PI * v);
            return r * Math.Cos(2 * Math.PI * v);
        }

        /// <summary>Hash-based value in [-1, 1] for integer lattice point i.</summary>
        public static double LatticeValue(uint seed, long i) => HashSeed(seed, i) / 4294967295.0 * 2 - 1;

        /// <summary>Smooth 1-D value noise in [-1, 1].</summary>
        public static double ValueNoise1D(uint seed, double t)
        {
            double i = Math.Floor(t);
            double f = t - i;
            double u = f * f * f * (f * (f * 6 - 15) + 10);
            double a = LatticeValue(seed, (long)i);
            double b = LatticeValue(seed, (long)i + 1);
            return a + (b - a) * u;
        }
    }
}
