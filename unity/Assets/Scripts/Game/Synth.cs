using System;
using System.Collections.Generic;

namespace SniperDuel.Game
{
    /// <summary>
    /// Every sound in the game, synthesised as mono PCM from noise, tones and filters, so nothing
    /// needs downloading or licensing. Plain C# with no engine calls: Sound runs it on a worker
    /// thread and turns the results into AudioClips. Common() builds the clips every map shares
    /// (bolt, scope, breath, impacts, kill cam, menu); Map(id) builds one map's rifle reports, with
    /// that map's echoes, and its two ambience loops.
    /// </summary>
    public static class Synth
    {
        /// <summary>Sample rate of one-shots: cracks and metal need the top octave.</summary>
        public const int Rate = 44100;
        /// <summary>Sample rate of the long loops: wind, water and creatures sit below 11 kHz, and this halves their memory.</summary>
        public const int BedRate = 22050;

        public sealed class Clip
        {
            public readonly string Name;
            public readonly float[] Data;
            public readonly int Rate;

            public Clip(string name, float[] data, int rate)
            {
                Name = name;
                Data = data;
                Rate = rate;
            }
        }

        // ---- the clip sets -----------------------------------------------------------------

        public static List<Clip> Common()
        {
            return new List<Clip>
            {
                new Clip("Crack", NearCrack(11), Rate),
                new Clip("Bolt", Bolt(12), Rate),
                new Clip("Turret", Turret(13), Rate),
                new Clip("Zoom", ZoomClick(14), Rate),
                new Clip("Raise", Raise(15), Rate),
                new Clip("Inhale", Inhale(16), Rate),
                new Clip("Exhale", Exhale(17), Rate),
                new Clip("Heart", Heart(18), Rate),
                new Clip("Flesh", Flesh(19), Rate),
                new Clip("Dirt", Dirt(20), Rate),
                new Clip("Concrete", Concrete(21), Rate),
                new Clip("Metal", Metal(22), Rate),
                new Clip("Wood", Wood(23), Rate),
                new Clip("Water", Splash(24), Rate),
                new Clip("Ricochet", Ricochet(25), Rate),
                new Clip("Glass", Glass(32), Rate),
                new Clip("Tin", Tin(33), Rate),
                new Clip("Clay", Clay(34), Rate),
                new Clip("Pop", Pop(35), Rate),
                new Clip("KillImpact", KillImpact(26), Rate),
                new Clip("KillWhoosh", KillWhoosh(27), BedRate),
                new Clip("KillDrone", KillDrone(28), BedRate),
                new Clip("UiClick", UiClick(29), Rate),
                new Clip("UiConfirm", UiChime(30, 1318.5f, 1975.5f), Rate),
                new Clip("UiBack", UiChime(31, 1174.7f, 880f), Rate),
            };
        }

        public static List<Clip> Map(string id)
        {
            int seed = 17;
            foreach (char ch in id) seed = seed * 31 + ch;
            var a = AcousticsFor(id);
            return new List<Clip>
            {
                new Clip("ShotNear", ShotNear(a, seed), Rate),
                new Clip("ShotFar", ShotFar(a, seed + 1), Rate),
                new Clip("Wind", WindBed(WindFor(id), seed + 2), BedRate),
                new Clip("Life", Life(id, seed + 3), BedRate),
            };
        }

        // ---- building blocks ---------------------------------------------------------------

        /// <summary>xorshift32: fast, and the same sound on every device.</summary>
        sealed class Rng
        {
            uint s;
            public Rng(int seed) { s = (uint)seed * 2654435761u | 1; }
            public float Next()
            {
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
                return s * (1f / 4294967296f);
            }
            public float Noise() => Next() * 2 - 1;
            public float Range(float a, float b) => a + (b - a) * Next();
        }

        static float Coef(float hz, int rate) => 1 - MathF.Exp(-2 * MathF.PI * hz / rate);

        /// <summary>Sine clamped at zero, for envelopes raised to a power (a hair below zero would give NaN).</summary>
        static float Sin0(float x) => MathF.Max(0, MathF.Sin(x));

        struct Pole
        {
            public float Y;
            public float Lp(float x, float c) => Y += c * (x - Y);
        }

        /// <summary>Zero-delay state-variable filter; Band is normalised to unity gain at the centre.</summary>
        struct Svf
        {
            float g, k, a1, a2, a3, ic1, ic2;

            public void Set(float hz, float q, int rate)
            {
                g = MathF.Tan(MathF.PI * MathF.Min(hz, rate * 0.45f) / rate);
                k = 1 / q;
                a1 = 1 / (1 + g * (g + k));
                a2 = g * a1;
                a3 = g * a2;
            }

            public float Run(float x, out float low, out float high)
            {
                float v3 = x - ic2;
                float v1 = a1 * ic1 + a2 * v3;
                float v2 = ic2 + a2 * ic1 + a3 * v3;
                ic1 = 2 * v1 - ic1;
                ic2 = 2 * v2 - ic2;
                low = v2;
                high = x - k * v1 - v2;
                return v1;
            }

            public float Band(float x) => k * Run(x, out _, out _);
            public float Low(float x) { Run(x, out var l, out _); return l; }
            public float High(float x) { Run(x, out _, out var h); return h; }
        }

        static float[] Make(float seconds, int rate) => new float[Math.Max(1, (int)(seconds * rate))];

        /// <summary>Adds `src` into `d` at sample `at`; with `wrap` it folds round the end of a loop.</summary>
        static void Mix(float[] d, float[] src, int at, float gain, bool wrap = false)
        {
            for (int i = 0; i < src.Length; i++)
            {
                int j = at + i;
                if (wrap) j %= d.Length;
                else if (j >= d.Length) break;
                if (j >= 0) d[j] += src[i] * gain;
            }
        }

        static float[] Lowpass(float[] src, float hz, int rate)
        {
            var o = new float[src.Length];
            Pole a = default, b = default;
            float c = Coef(hz, rate);
            for (int i = 0; i < src.Length; i++) o[i] = b.Lp(a.Lp(src[i], c), c);
            return o;
        }

        static void LowpassInPlace(float[] d, float hz, int rate)
        {
            Pole a = default, b = default;
            float c = Coef(hz, rate);
            for (int i = 0; i < d.Length; i++) d[i] = b.Lp(a.Lp(d[i], c), c);
        }

        static float Peak(float[] d)
        {
            float m = 1e-9f;
            foreach (var v in d) m = MathF.Max(m, MathF.Abs(v));
            return m;
        }

        static float[] Normalise(float[] d, float to = 1)
        {
            float g = to / Peak(d);
            for (int i = 0; i < d.Length; i++) d[i] *= g;
            return d;
        }

        /// <summary>Soft saturation, as a mic and a phone speaker hear a rifle: peaks rounded, body brought up.</summary>
        static void Drive(float[] d, float drive)
        {
            Normalise(d);
            float k = 1 / MathF.Tanh(drive);
            for (int i = 0; i < d.Length; i++) d[i] = MathF.Tanh(d[i] * drive) * k;
        }

        /// <summary>Normalise to -1 dBFS and fade the last few ms so nothing clicks.</summary>
        static float[] Finish(float[] d, int rate, float fadeS = 0.02f)
        {
            Normalise(d, 0.89f);
            int fade = Math.Min(d.Length, (int)(fadeS * rate));
            for (int i = 0; i < fade; i++) d[d.Length - 1 - i] *= i / (float)fade;
            return d;
        }

        /// <summary>
        /// Folds the last `x` samples over the start with an equal-power crossfade and drops them, so
        /// the clip loops without a seam. Events added afterwards should wrap (Mix with wrap).
        /// </summary>
        static float[] Loop(float[] d, int x)
        {
            int n = d.Length - x;
            var o = new float[n];
            Array.Copy(d, o, n);
            for (int i = 0; i < x; i++)
            {
                float a = i / (float)x * MathF.PI / 2;
                o[i] = d[i] * MathF.Sin(a) + d[n + i] * MathF.Cos(a);
            }
            return o;
        }

        /// <summary>Struck metal: inharmonic partials, each ringing down on its own, plus the strike.</summary>
        static void Ring(float[] d, int rate, float at, float gain, Rng rng, float strikeMs, params (float hz, float decay, float amp)[] partials)
        {
            int o = (int)(at * rate);
            float longest = 0;
            foreach (var p in partials) longest = MathF.Max(longest, p.decay);
            int n = (int)(longest * 7 * rate);
            var phase = new float[partials.Length];
            for (int k = 0; k < phase.Length; k++) phase[k] = rng.Next() * 6.283f;
            float strike = strikeMs / 1000;
            for (int i = 0; i < n && o + i < d.Length; i++)
            {
                float t = i / (float)rate;
                float s = 0;
                for (int k = 0; k < partials.Length; k++)
                {
                    var p = partials[k];
                    s += p.amp * MathF.Sin(2 * MathF.PI * p.hz * t + phase[k]) * MathF.Exp(-t / p.decay);
                }
                if (t < strike) s += rng.Noise() * (1 - t / strike);
                d[o + i] += s * gain;
            }
        }

        /// <summary>A pitched thud: a sine falling from f0 to f1, the body of a blow.</summary>
        static void Thump(float[] d, int rate, float at, float gain, float f0, float f1, float sweep, float decay)
        {
            int o = (int)(at * rate);
            int n = (int)(decay * 7 * rate);
            double ph = 0;
            for (int i = 0; i < n && o + i < d.Length; i++)
            {
                float t = i / (float)rate;
                ph += 2 * Math.PI * (f1 + (f0 - f1) * MathF.Exp(-t / sweep)) / rate;
                d[o + i] += gain * (float)Math.Sin(ph) * (1 - MathF.Exp(-t / 0.0008f)) * MathF.Exp(-t / decay);
            }
        }

        /// <summary>
        /// Band-passed noise with an attack and an exponential decay. Scaled by its bandwidth so a
        /// narrow band peaks about as high as a wide one (gain 1 ≈ a unit thump).
        /// </summary>
        static void Burst(float[] d, int rate, float at, float gain, Rng rng, float hz, float q, float attack, float decay)
        {
            int o = (int)(at * rate);
            int n = (int)((attack + decay * 7) * rate);
            var f = new Svf();
            f.Set(hz, q, rate);
            gain *= 0.35f / (0.577f * MathF.Sqrt(MathF.Min(1, hz / q / (rate / 2f))));
            for (int i = 0; i < n && o + i < d.Length; i++)
            {
                float t = i / (float)rate;
                d[o + i] += gain * f.Band(rng.Noise()) * (1 - MathF.Exp(-t / attack)) * MathF.Exp(-t / decay);
            }
        }

        /// <summary>
        /// Debris and crackle: many tiny pings at random pitches, thick at first and thinning out,
        /// like grit pattering down or bone giving way.
        /// </summary>
        static void Grains(float[] d, int rate, float at, float seconds, float gain, Rng rng, float perSecond, float hzLo, float hzHi, float thinning, float grainMs)
        {
            int count = (int)(perSecond * seconds);
            float tau = grainMs / 1000;
            int len = (int)(tau * 5 * rate);
            for (int g = 0; g < count; g++)
            {
                float when = seconds * MathF.Pow(rng.Next(), 1.6f);
                float amp = gain * MathF.Exp(-when / thinning) * rng.Range(0.2f, 1);
                float hz = rng.Range(hzLo, hzHi);
                float ph = rng.Next() * 6.283f;
                int o = (int)((at + when) * rate);
                for (int i = 0; i < len && o + i < d.Length; i++)
                {
                    float t = i / (float)rate;
                    d[o + i] += amp * MathF.Sin(2 * MathF.PI * hz * t + ph) * MathF.Exp(-t / tau);
                }
            }
        }

        /// <summary>A pressure N-wave: the double bang of a supersonic shock, `ms` long.</summary>
        static void NWave(float[] d, int rate, float at, float ms, float gain)
        {
            int o = (int)(at * rate);
            int n = Math.Max(3, (int)(ms / 1000 * rate));
            for (int i = 0; i < n && o + i < d.Length; i++) d[o + i] += gain * (1 - 2f * i / (n - 1));
        }

        /// <summary>Friction of steel on steel: grainy band noise, as a bolt slides in its raceway.</summary>
        static void Scrape(float[] d, int rate, float at, float dur, float gain, Rng rng, float hz)
        {
            int o = (int)(at * rate);
            int n = (int)(dur * rate);
            Svf a = default, b = default;
            a.Set(hz, 1.6f, rate);
            b.Set(hz * 2.13f, 2.4f, rate);
            Pole grit = default;
            float cg = Coef(180, rate);
            for (int i = 0; i < n && o + i < d.Length; i++)
            {
                float u = i / (float)n;
                // Stick-slip: the noise comes in grains rather than a smooth hiss.
                float g = grit.Lp(rng.Next() < 0.06f ? 1 : 0, cg) * 12;
                float x = rng.Noise() * (0.25f + g);
                d[o + i] += gain * (a.Band(x) + 0.6f * b.Band(x)) * MathF.Pow(Sin0(MathF.PI * u), 0.6f);
            }
        }

        // ---- rifle reports -----------------------------------------------------------------

        /// <summary>How a map answers a shot: its distinct echoes and the rolling tail behind them.</summary>
        public sealed class Acoustics
        {
            /// <summary>Reflections: arrival (s), level, dullness (low-pass Hz) and how smeared (s).</summary>
            public (float at, float gain, float hz, float spread)[] Echoes = Array.Empty<(float, float, float, float)>();
            public float TailSeconds = 0.8f, TailHz = 1000, TailGain = 0.15f;
            /// <summary>How much the tail rumbles and rolls (0 smooth, 1 thunder).</summary>
            public float TailRoll = 0.2f;
            /// <summary>Steel nearby ringing with the blast (the rig's decks and legs).</summary>
            public float Steel;
            public float Length = 3;
        }

        public static Acoustics AcousticsFor(string mapId)
        {
            switch (mapId)
            {
                case "twin-bridges":
                    // Embankment walls and the city blocks give fast slap-backs; the far bank and the
                    // bridges answer later, duller.
                    return new Acoustics
                    {
                        Echoes = new[] { (0.09f, 0.30f, 2800f, 0.02f), (0.23f, 0.24f, 2200f, 0.04f), (0.42f, 0.22f, 1800f, 0.06f), (0.8f, 0.13f, 1300f, 0.1f), (1.35f, 0.07f, 900f, 0.15f) },
                        TailSeconds = 0.9f, TailHz = 1300, TailGain = 0.2f, TailRoll = 0.3f, Length = 3.4f,
                    };
                case "watchtowers":
                    // Flat reed beds swallow the blast; only the tree line far off sends anything back.
                    return new Acoustics
                    {
                        Echoes = new[] { (0.62f, 0.09f, 900f, 0.2f), (1.4f, 0.05f, 600f, 0.3f) },
                        TailSeconds = 0.7f, TailHz = 700, TailGain = 0.12f, TailRoll = 0.1f, Length = 2.8f,
                    };
                case "oil-rigs":
                    // Steel all round: a ringing comb of close reflections, then open sea and one far rig.
                    return new Acoustics
                    {
                        Echoes = new[] { (0.018f, 0.25f, 4000f, 0.004f), (0.035f, 0.2f, 3500f, 0.006f), (0.061f, 0.15f, 3000f, 0.01f), (0.95f, 0.12f, 1200f, 0.08f) },
                        TailSeconds = 0.8f, TailHz = 800, TailGain = 0.14f, TailRoll = 0.15f, Steel = 0.18f, Length = 3f,
                    };
                case "dry-valley":
                    // Canyon walls: strong, distinct echoes bouncing back and forth down the valley.
                    return new Acoustics
                    {
                        Echoes = new[] { (0.35f, 0.36f, 2000f, 0.05f), (0.9f, 0.26f, 1500f, 0.1f), (1.6f, 0.17f, 1100f, 0.15f), (2.4f, 0.1f, 800f, 0.2f) },
                        TailSeconds = 1.4f, TailHz = 900, TailGain = 0.18f, TailRoll = 0.5f, Length = 4.2f,
                    };
                case "harbor-rooftops":
                    // Rooftops and parapets close by, the warehouses across the basin a second later.
                    return new Acoustics
                    {
                        Echoes = new[] { (0.06f, 0.28f, 3000f, 0.015f), (0.15f, 0.24f, 2500f, 0.03f), (0.33f, 0.18f, 2000f, 0.05f), (1.2f, 0.13f, 1100f, 0.12f) },
                        TailSeconds = 1f, TailHz = 1200, TailGain = 0.2f, TailRoll = 0.3f, Length = 3.4f,
                    };
                case "alpine-ridge":
                    // Mountains: the report rolls from face to face for seconds, each answer duller.
                    return new Acoustics
                    {
                        Echoes = new[] { (0.8f, 0.3f, 1600f, 0.12f), (1.9f, 0.26f, 1200f, 0.2f), (3.1f, 0.16f, 800f, 0.3f), (4.2f, 0.1f, 600f, 0.35f) },
                        TailSeconds = 2.2f, TailHz = 700, TailGain = 0.25f, TailRoll = 0.7f, Length = 5.6f,
                    };
                default:
                    // Training range: earth berms either side and the backstop downrange.
                    return new Acoustics
                    {
                        Echoes = new[] { (0.05f, 0.3f, 2500f, 0.01f), (0.12f, 0.18f, 2000f, 0.03f), (0.65f, 0.1f, 1200f, 0.08f) },
                        TailSeconds = 0.6f, TailHz = 1000, TailGain = 0.12f, TailRoll = 0.2f, Length = 2.6f,
                    };
            }
        }

        /// <summary>Muzzle blast: a noisy pressure roar whose brightness falls fast, over a low swept boom.</summary>
        static float[] Blast(Rng rng, float seconds, float brightHz, float body, float attackMs)
        {
            var d = Make(seconds, Rate);
            Pole a = default, b = default;
            var chest = new Svf();
            chest.Set(420, 0.6f, Rate);
            double ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float att = 1 - MathF.Exp(-t / (attackMs / 1000));
                // The chesty middle of the blast, where most of its loudness lives.
                float mid = chest.Band(rng.Noise()) * MathF.Exp(-t / 0.06f) * 5;
                // The roar keeps some bite (a floor of ~brightHz/12) so it carries on a small speaker.
                float c = Coef(brightHz * MathF.Exp(-t / 0.05f) + brightHz / 12 + 150, Rate);
                float roar = b.Lp(a.Lp(rng.Noise(), c), c) * MathF.Exp(-t / 0.08f) * 4.5f;
                ph += 2 * Math.PI * (45 + 110 * MathF.Exp(-t / 0.035f)) / Rate;
                float p = (float)ph;
                float boom = (MathF.Sin(p) + 0.35f * MathF.Sin(2 * p + 0.5f)) * MathF.Exp(-t / 0.15f) * body;
                d[i] = att * (roar + mid + boom);
            }
            return Normalise(d);
        }

        /// <summary>The map's echoes of `src` and its diffuse rolling tail, added from `t0` s.</summary>
        static void Surroundings(float[] d, float[] src, Acoustics a, Rng rng, float t0, float echoScale, float tailScale)
        {
            foreach (var e in a.Echoes)
            {
                // A reflection is never a clean copy: a cluster of duller arrivals spread over the
                // face it came off.
                var dull = Lowpass(src, e.hz, Rate);
                int taps = Math.Clamp(4 + (int)(e.spread * 40), 4, 10);
                for (int k = 0; k < taps; k++)
                {
                    float off = k == 0 ? 0 : e.spread * MathF.Pow(rng.Next(), 0.7f);
                    float g = e.gain * echoScale * (k == 0 ? 0.7f : 0.6f * rng.Range(0.3f, 1) / MathF.Sqrt(taps - 1));
                    Mix(d, dull, (int)((t0 + e.at + off) * Rate), g);
                }
            }

            // Tail: low noise swelling in as the reflections pile up, rumbling if the land rolls it.
            var tail = new float[d.Length];
            Pole b1 = default, b2 = default, l1 = default, l2 = default, roll = default;
            float cb = Coef(a.TailHz, Rate), cl = Coef(170, Rate), cr = Coef(3, Rate);
            float rollScale = 0.6f / (0.577f * MathF.Sqrt(cr / 2));
            int start = (int)(t0 * Rate);
            for (int i = start; i < d.Length; i++)
            {
                float t = (i - start) / (float)Rate;
                float band = b2.Lp(b1.Lp(rng.Noise(), cb), cb);
                float low = l2.Lp(l1.Lp(rng.Noise(), cl), cl) * 1.5f;
                float mod = MathF.Max(0, 1 + a.TailRoll * roll.Lp(rng.Noise(), cr) * rollScale);
                tail[i] = (band + low) * mod * (1 - MathF.Exp(-t / 0.06f)) * MathF.Exp(-t / a.TailSeconds);
            }
            Mix(d, Normalise(tail), 0, a.TailGain * tailScale);

            if (a.Steel > 0)
                Ring(d, Rate, t0 + 0.004f, a.Steel, rng, 0, (310, 0.35f, 1), (742, 0.25f, 0.7f), (1287, 0.18f, 0.5f), (2210, 0.1f, 0.3f));
        }

        /// <summary>Your own rifle: firing-pin tick, supersonic crack, muzzle blast and boom, then the map answering.</summary>
        static float[] ShotNear(Acoustics a, int seed)
        {
            var rng = new Rng(seed);
            var d = Make(a.Length, Rate);
            // Sear and firing pin: a small metallic tick a few ms ahead of the blast.
            Ring(d, Rate, 0, 0.18f, rng, 0.4f, (3100, 0.006f, 1), (4900, 0.004f, 0.6f), (7300, 0.003f, 0.4f));
            const float t0 = 0.004f;
            var blast = Blast(rng, 0.9f, 7500, 0.8f, 0.4f);
            Mix(d, blast, (int)(t0 * Rate), 1);
            // The shock leaving the muzzle: an N-wave, a spit of high noise and the gas crackling out.
            NWave(d, Rate, t0, 0.45f, 1.3f);
            Burst(d, Rate, t0, 0.8f, rng, 6500, 0.6f, 0.0002f, 0.008f);
            Burst(d, Rate, t0, 0.7f, rng, 2200, 0.7f, 0.0003f, 0.025f);
            // Action and stock ringing against the shoulder, and the kick through the body.
            Ring(d, Rate, t0 + 0.002f, 0.1f, rng, 0, (1650, 0.03f, 1), (2730, 0.022f, 0.7f), (4410, 0.015f, 0.4f));
            Thump(d, Rate, t0, 0.35f, 180, 60, 0.01f, 0.06f);
            Surroundings(d, blast, a, rng, t0, 1, 1);
            Drive(d, 2.2f);
            return Finish(d, Rate, 0.3f);
        }

        /// <summary>
        /// The enemy's rifle from hundreds of metres: the crack is gone (the bullet isn't passing),
        /// the blast is dull and slow to rise, and the land's echoes are nearly as loud as the shot.
        /// Sound adds a distance low-pass on top.
        /// </summary>
        static float[] ShotFar(Acoustics a, int seed)
        {
            var rng = new Rng(seed);
            var d = Make(a.Length + 0.8f, Rate);
            var blast = Lowpass(Blast(rng, 1f, 1400, 1.5f, 2.5f), 1600, Rate);
            Normalise(blast);
            Mix(d, blast, 0, 0.8f);
            Surroundings(d, blast, a, rng, 0, 1.6f, 2);
            LowpassInPlace(d, 2600, Rate);
            Drive(d, 1.6f);
            return Finish(d, Rate, 0.3f);
        }

        /// <summary>A round passing within a metre or two: a sharp snap, the whip of air behind it, a slap off the ground.</summary>
        static float[] NearCrack(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.45f, Rate);
            NWave(d, Rate, 0.002f, 0.9f, 1);
            Burst(d, Rate, 0.002f, 0.6f, rng, 5000, 0.7f, 0.0001f, 0.004f);
            var f = new Svf();
            int o = (int)(0.003f * Rate);
            for (int i = 0; i < 0.25f * Rate; i++)
            {
                float t = i / (float)Rate;
                if (i % 16 == 0) f.Set(900 + 2600 * MathF.Exp(-t / 0.03f), 1.5f, Rate);
                d[o + i] += 0.35f * f.Band(rng.Noise()) * (1 - MathF.Exp(-t / 0.002f)) * MathF.Exp(-t / 0.04f);
            }
            var head = new float[(int)(0.02f * Rate)];
            Array.Copy(d, head, head.Length);
            head = Lowpass(head, 3000, Rate);
            Mix(d, head, (int)(0.045f * Rate), 0.3f);
            Mix(d, head, (int)(0.12f * Rate), 0.15f);
            return Finish(d, Rate);
        }

        // ---- rifle handling ----------------------------------------------------------------

        /// <summary>Bolt up, back (brass flicks out), forward (picks up a round), down; the case lands nearby.</summary>
        static float[] Bolt(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(1.3f, Rate);
            // Up: the handle lifts and the lugs unlock.
            Ring(d, Rate, 0, 0.8f, rng, 0.6f, (2100, 0.015f, 1), (3400, 0.012f, 0.6f), (5200, 0.008f, 0.4f));
            Scrape(d, Rate, 0.005f, 0.06f, 0.25f, rng, 2600);
            // Back: a long slide, the extractor yanking the case, the bolt hitting its stop.
            Scrape(d, Rate, 0.1f, 0.14f, 0.5f, rng, 2400);
            Ring(d, Rate, 0.24f, 0.9f, rng, 0.8f, (1700, 0.02f, 1), (2900, 0.015f, 0.7f), (4400, 0.01f, 0.5f));
            // The brass case spinning out of the port.
            Ring(d, Rate, 0.27f, 0.3f, rng, 0.2f, (4800, 0.03f, 1), (7600, 0.02f, 0.6f), (9900, 0.015f, 0.4f));
            // Forward: slides home, stripping the next round from the magazine.
            Scrape(d, Rate, 0.42f, 0.12f, 0.55f, rng, 2200);
            Ring(d, Rate, 0.47f, 0.35f, rng, 0.4f, (2500, 0.01f, 1), (3900, 0.008f, 0.5f));
            Ring(d, Rate, 0.56f, 0.75f, rng, 0.5f, (1900, 0.012f, 1), (3100, 0.01f, 0.6f), (4700, 0.007f, 0.4f));
            // Down: the handle locks with a solid clack through the stock.
            Ring(d, Rate, 0.66f, 1f, rng, 0.8f, (1500, 0.018f, 1), (2600, 0.014f, 0.7f), (3900, 0.01f, 0.5f));
            Thump(d, Rate, 0.66f, 0.5f, 260, 180, 0.005f, 0.02f);
            // The case bouncing on the ground beside you.
            Ring(d, Rate, 0.95f, 0.14f, rng, 0.1f, (5300, 0.03f, 1), (8100, 0.02f, 0.6f));
            Ring(d, Rate, 1.07f, 0.07f, rng, 0.1f, (5400, 0.025f, 1), (8300, 0.015f, 0.6f));
            return Finish(d, Rate);
        }

        /// <summary>One detent of a target turret: a crisp tick, a small ring, and the spring settling.</summary>
        static float[] Turret(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.06f, Rate);
            Ring(d, Rate, 0, 1, rng, 0.15f, (4200, 0.003f, 1), (6900, 0.002f, 0.5f));
            Thump(d, Rate, 0, 0.4f, 1300, 1000, 0.002f, 0.005f);
            Ring(d, Rate, 0.007f, 0.3f, rng, 0.1f, (3600, 0.002f, 1));
            return Finish(d, Rate, 0.005f);
        }

        /// <summary>The magnification ring: a softer, duller ratchet than the turrets.</summary>
        static float[] ZoomClick(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.06f, Rate);
            Ring(d, Rate, 0, 1, rng, 0.4f, (2600, 0.005f, 1), (4100, 0.003f, 0.4f));
            Ring(d, Rate, 0.01f, 0.45f, rng, 0.3f, (2400, 0.004f, 1));
            LowpassInPlace(d, 6000, Rate);
            return Finish(d, Rate, 0.005f);
        }

        /// <summary>The rifle coming up to the eye: sleeve and sling rustling, the cheek finding the stock.</summary>
        static float[] Raise(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.45f, Rate);
            var f = new Svf();
            f.Set(1800, 0.8f, Rate);
            Pole grain = default;
            float cg = Coef(40, Rate);
            for (int i = 0; i < 0.36f * Rate; i++)
            {
                float u = i / (0.36f * Rate);
                float g = grain.Lp(MathF.Abs(rng.Noise()), cg);
                d[i] += f.Band(rng.Noise()) * g * g * 8 * MathF.Pow(Sin0(MathF.PI * u), 1.5f);
            }
            Thump(d, Rate, 0.34f, 0.25f, 700, 450, 0.003f, 0.01f);
            Ring(d, Rate, 0.345f, 0.08f, rng, 0.2f, (3300, 0.004f, 1));
            return Finish(d, Rate);
        }

        // ---- breath and heart --------------------------------------------------------------

        /// <summary>A slow breath in through the nose and mouth, caught and held at the top.</summary>
        static float[] Inhale(int seed)
        {
            var rng = new Rng(seed);
            const float dur = 0.85f;
            var d = Make(dur + 0.05f, Rate);
            Svf f = default, h = default;
            h.Set(3500, 0.7f, Rate);
            for (int i = 0; i < dur * Rate; i++)
            {
                float u = i / (dur * Rate);
                if (i % 32 == 0) f.Set(1100 + 900 * u, 0.9f, Rate);
                float n = rng.Noise();
                float env = MathF.Pow(Sin0(MathF.PI * MathF.Pow(u, 0.75f)), 1.2f);
                d[i] = (f.Band(n) * 0.8f + h.High(n) * 0.2f) * env;
            }
            // The glottis closing to hold it.
            Thump(d, Rate, dur - 0.02f, 0.12f, 220, 130, 0.002f, 0.008f);
            LowpassInPlace(d, 5000, Rate);
            return Finish(d, Rate);
        }

        /// <summary>The held breath let go: a quick rush out, falling away.</summary>
        static float[] Exhale(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(1.3f, Rate);
            Svf f = default, l = default;
            l.Set(320, 0.6f, Rate);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                if (i % 32 == 0) f.Set(950 - 300 * t / 1.3f, 0.8f, Rate);
                float n = rng.Noise();
                d[i] = (f.Band(n) + 0.4f * l.Band(n)) * (1 - MathF.Exp(-t / 0.04f)) * MathF.Exp(-t / 0.42f);
            }
            LowpassInPlace(d, 4000, Rate);
            return Finish(d, Rate, 0.1f);
        }

        /// <summary>One heartbeat, lub-dub, driven so its harmonics carry on a phone speaker.</summary>
        static float[] Heart(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.6f, Rate);
            Thump(d, Rate, 0, 1, 110, 62, 0.01f, 0.055f);
            Burst(d, Rate, 0, 1.2f, rng, 190, 1, 0.003f, 0.035f);
            Thump(d, Rate, 0.24f, 0.75f, 130, 75, 0.008f, 0.04f);
            Burst(d, Rate, 0.24f, 0.9f, rng, 230, 1, 0.002f, 0.025f);
            Drive(d, 3f);
            LowpassInPlace(d, 900, Rate);
            return Finish(d, Rate);
        }

        // ---- impacts -----------------------------------------------------------------------

        static float[] Flesh(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.6f, Rate);
            // A wet, heavy thwack: the body's resonance around 200 Hz, meat in the low mids, a slap on top.
            Thump(d, Rate, 0, 0.5f, 260, 110, 0.01f, 0.06f);
            Burst(d, Rate, 0, 1.6f, rng, 650, 0.8f, 0.0005f, 0.035f);
            Burst(d, Rate, 0, 1.2f, rng, 1800, 1.2f, 0.0001f, 0.007f);
            Drive(d, 1.8f);
            return Finish(d, Rate);
        }

        static float[] Dirt(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.8f, Rate);
            Thump(d, Rate, 0, 0.35f, 160, 80, 0.008f, 0.04f);
            Burst(d, Rate, 0, 1.4f, rng, 450, 0.6f, 0.0003f, 0.05f);
            // Soil and grit thrown up, pattering back down.
            Grains(d, Rate, 0.01f, 0.5f, 0.35f, rng, 140, 1500, 4500, 0.15f, 1.2f);
            Burst(d, Rate, 0, 0.2f, rng, 3000, 0.5f, 0.005f, 0.12f);
            return Finish(d, Rate);
        }

        static float[] Concrete(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.8f, Rate);
            NWave(d, Rate, 0, 0.2f, 1);
            Burst(d, Rate, 0, 0.8f, rng, 3200, 1.5f, 0.00005f, 0.004f);
            Ring(d, Rate, 0, 0.2f, rng, 0, (1900, 0.012f, 1), (3700, 0.008f, 0.5f));
            Thump(d, Rate, 0, 0.3f, 300, 120, 0.004f, 0.02f);
            // Chips flying off and dust.
            Grains(d, Rate, 0.005f, 0.4f, 0.4f, rng, 220, 2500, 6500, 0.08f, 0.8f);
            Burst(d, Rate, 0.005f, 0.15f, rng, 1500, 0.5f, 0.01f, 0.15f);
            return Finish(d, Rate);
        }

        /// <summary>Steel plate: the ping a sniper listens for.</summary>
        static float[] Metal(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(1.3f, Rate);
            NWave(d, Rate, 0, 0.2f, 0.6f);
            Ring(d, Rate, 0, 0.8f, rng, 0.3f, (870, 0.35f, 1), (2140, 0.22f, 0.7f), (3390, 0.15f, 0.55f), (5120, 0.08f, 0.4f), (6830, 0.05f, 0.3f));
            Thump(d, Rate, 0, 0.4f, 600, 250, 0.004f, 0.02f);
            return Finish(d, Rate, 0.1f);
        }

        static float[] Wood(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.6f, Rate);
            Thump(d, Rate, 0, 0.3f, 260, 150, 0.005f, 0.03f);
            Burst(d, Rate, 0, 2.2f, rng, 520, 2.5f, 0.0002f, 0.035f);
            Burst(d, Rate, 0, 1.6f, rng, 1250, 3f, 0.0001f, 0.02f);
            Grains(d, Rate, 0.004f, 0.15f, 0.6f, rng, 160, 2000, 5000, 0.05f, 0.7f);
            return Finish(d, Rate);
        }

        /// <summary>A bottle bursting: a sharp crack, then the shards' bright tinkle raining down.</summary>
        static float[] Glass(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(1.1f, Rate);
            Burst(d, Rate, 0, 1.2f, rng, 4200, 1.5f, 0.00005f, 0.006f);
            Ring(d, Rate, 0, 0.25f, rng, 0.3f, (3150, 0.03f, 1), (5230, 0.02f, 0.7f), (7480, 0.015f, 0.5f));
            Grains(d, Rate, 0.01f, 0.9f, 0.6f, rng, 260, 3500, 9000, 0.25f, 3f);
            return Finish(d, Rate, 0.1f);
        }

        /// <summary>A tin can: a thin, short clank and the can rattling away.</summary>
        static float[] Tin(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.7f, Rate);
            Ring(d, Rate, 0, 0.6f, rng, 0.4f, (1480, 0.05f, 1), (2950, 0.035f, 0.6f), (4400, 0.02f, 0.4f));
            Burst(d, Rate, 0, 0.6f, rng, 2600, 1.2f, 0.0001f, 0.01f);
            Ring(d, Rate, 0.22f, 0.2f, rng, 0.3f, (1520, 0.03f, 1), (3050, 0.02f, 0.5f));
            Ring(d, Rate, 0.38f, 0.1f, rng, 0.3f, (1500, 0.025f, 1), (3000, 0.02f, 0.5f));
            return Finish(d, Rate);
        }

        /// <summary>A clay pigeon breaking: a dry crack and grit.</summary>
        static float[] Clay(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.6f, Rate);
            Burst(d, Rate, 0, 1.4f, rng, 2200, 1.1f, 0.00005f, 0.008f);
            Thump(d, Rate, 0, 0.2f, 500, 220, 0.003f, 0.012f);
            Grains(d, Rate, 0.004f, 0.35f, 0.45f, rng, 240, 2000, 6000, 0.08f, 0.9f);
            return Finish(d, Rate);
        }

        /// <summary>A balloon popping: one hard, bright snap.</summary>
        static float[] Pop(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.35f, Rate);
            NWave(d, Rate, 0, 0.6f, 1);
            Burst(d, Rate, 0, 1.0f, rng, 1800, 0.7f, 0.00005f, 0.012f);
            return Finish(d, Rate);
        }

        /// <summary>A round into water: a slap, the cavity's rising bloop, spray, the column falling back, drops.</summary>
        static float[] Splash(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(1.3f, Rate);
            Burst(d, Rate, 0, 0.7f, rng, 1100, 0.8f, 0.0001f, 0.012f);
            double ph = 0;
            for (int i = 0; i < 0.2f * Rate; i++)
            {
                float t = i / (float)Rate;
                ph += 2 * Math.PI * (1300 - 920 * MathF.Exp(-t / 0.045f)) / Rate;
                d[i + (int)(0.004f * Rate)] += 0.6f * (float)Math.Sin(ph) * (1 - MathF.Exp(-t / 0.001f)) * MathF.Exp(-t / 0.035f);
            }
            var spray = new Svf();
            spray.Set(3200, 0.6f, Rate);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] += 0.5f * spray.Band(rng.Noise()) * (1 - MathF.Exp(-t / 0.025f)) * MathF.Exp(-t / 0.22f);
            }
            Burst(d, Rate, 0.12f, 0.35f, rng, 400, 0.6f, 0.06f, 0.2f);
            for (int k = 0; k < 18; k++)
            {
                float at = rng.Range(0.15f, 0.9f);
                float g = rng.Range(0.08f, 0.2f) * MathF.Exp(-(at - 0.15f) / 0.5f);
                double p = 0;
                float f0 = rng.Range(1400, 1900);
                int o = (int)(at * Rate);
                for (int i = 0; i < 0.03f * Rate && o + i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    p += 2 * Math.PI * (f0 * (1 + 0.8f * (1 - MathF.Exp(-t / 0.006f)))) / Rate;
                    d[o + i] += g * (float)Math.Sin(p) * MathF.Exp(-t / 0.005f);
                }
            }
            return Finish(d, Rate);
        }

        /// <summary>A ricochet tumbling away: a falling whine, warbling as the bent bullet spins.</summary>
        static float[] Ricochet(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(1.2f, Rate);
            const float f0 = 3600, T = 0.9f;
            double ph = 0;
            var f = new Svf();
            for (int i = 0; i < T * Rate; i++)
            {
                float t = i / (float)Rate;
                float hz = f0 * (1 - 0.5f * t / T) * (1 + 0.012f * MathF.Sin(2 * MathF.PI * 9 * t));
                ph += 2 * Math.PI * hz / Rate;
                if (i % 16 == 0) f.Set(hz, 6, Rate);
                float tumble = 1 - 0.55f * (0.5f + 0.5f * MathF.Sin(2 * MathF.PI * 47 * t));
                float env = (1 - MathF.Exp(-t / 0.015f)) * MathF.Exp(-t / 0.32f);
                d[i] += ((float)Math.Sin(ph) * 0.7f + f.Band(rng.Noise()) * 0.6f) * tumble * env;
            }
            return Finish(d, Rate, 0.2f);
        }

        // ---- kill cam ----------------------------------------------------------------------

        /// <summary>The X-ray moment: a deep thud through the body, the wet hit, bones giving way, a low rumble after.</summary>
        static float[] KillImpact(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(1.8f, Rate);
            Thump(d, Rate, 0, 0.6f, 190, 60, 0.03f, 0.22f);
            Burst(d, Rate, 0, 2f, rng, 550, 0.7f, 0.0005f, 0.05f);
            Burst(d, Rate, 0, 1f, rng, 2000, 1, 0.0001f, 0.008f);
            // Crunch: dense crackle over a fifth of a second with a few sharper snaps in it.
            Grains(d, Rate, 0.012f, 0.22f, 1.4f, rng, 900, 900, 4200, 0.07f, 1.1f);
            for (int k = 0; k < 4; k++)
            {
                float at = rng.Range(0.01f, 0.09f);
                NWave(d, Rate, at, 0.25f, 0.8f);
                Burst(d, Rate, at, 0.8f, rng, 2600, 2, 0.0001f, 0.004f);
            }
            Burst(d, Rate, 0, 1f, rng, 220, 0.7f, 0.02f, 0.4f);
            Drive(d, 2.2f);
            return Finish(d, Rate, 0.2f);
        }

        /// <summary>The bullet heard from behind in slow motion: a rushing whoosh that flutters with the spin. Loops.</summary>
        static float[] KillWhoosh(int seed)
        {
            var rng = new Rng(seed);
            const float L = 3;
            int x = (int)(0.3f * BedRate);
            var d = new float[(int)(L * BedRate) + x];
            Svf a = default, b = default;
            // Pitched up here: Sound plays it slowed to about half speed.
            a.Set(700, 0.9f, BedRate);
            b.Set(2200, 1.5f, BedRate);
            Pole low = default;
            float cl = Coef(160, BedRate);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)BedRate;
                float n = rng.Noise();
                // 12 Hz spin flutter: a whole number of cycles in the loop.
                float spin = 1 + 0.4f * MathF.Sin(2 * MathF.PI * 12 * t);
                d[i] = (a.Band(n) * 0.8f + b.Band(n) * 0.35f) * spin + low.Lp(rng.Noise(), cl) * 1.5f;
            }
            return Finish(Loop(d, x), BedRate, 0);
        }

        /// <summary>A low, uneasy drone under the slow-motion close: detuned tones that beat slowly. Loops.</summary>
        static float[] KillDrone(int seed)
        {
            var rng = new Rng(seed);
            const float L = 6;
            int x = (int)(0.5f * BedRate);
            var d = new float[(int)(L * BedRate) + x];
            // Every frequency fits a whole number of cycles in the loop, so the seam is clean.
            // A and E around 82 and 110 Hz, one pair beating once every three seconds.
            var tones = new[] { (492 / L, 1f), (660 / L, 0.8f), (662 / L, 0.8f), (990 / L, 0.4f) };
            var air = new Svf();
            air.Set(2000, 3, BedRate);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)BedRate;
                float bright = 0.5f + 0.5f * MathF.Sin(2 * MathF.PI * t / L);
                float s = 0;
                foreach (var (hz, amp) in tones)
                    for (int h = 1; h <= 6; h++)
                        s += amp * MathF.Sin(2 * MathF.PI * hz * h * t) / MathF.Pow(h, 0.9f + (1 - bright));
                s += air.Band(rng.Noise()) * 0.15f * (0.5f + 0.5f * MathF.Sin(2 * MathF.PI * 2 * t / L));
                d[i] = s;
            }
            return Finish(Loop(d, x), BedRate, 0);
        }

        // ---- menu --------------------------------------------------------------------------

        static float[] UiClick(int seed)
        {
            var rng = new Rng(seed);
            var d = Make(0.05f, Rate);
            Ring(d, Rate, 0, 0.6f, rng, 0.2f, (3200, 0.004f, 1), (5400, 0.0025f, 0.4f));
            Thump(d, Rate, 0, 0.4f, 900, 700, 0.002f, 0.004f);
            return Finish(d, Rate, 0.005f);
        }

        /// <summary>Two soft notes: rising to confirm, falling to go back.</summary>
        static float[] UiChime(int seed, float first, float second)
        {
            var rng = new Rng(seed);
            var d = Make(0.4f, Rate);
            foreach (var (at, hz, g) in new[] { (0f, first, 0.8f), (0.07f, second, 1f) })
            {
                int o = (int)(at * Rate);
                for (int i = 0; o + i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    float env = (1 - MathF.Exp(-t / 0.003f)) * MathF.Exp(-t / 0.08f);
                    d[o + i] += g * env * (MathF.Sin(2 * MathF.PI * hz * t) + 0.15f * MathF.Sin(4 * MathF.PI * hz * t));
                }
            }
            Ring(d, Rate, 0, 0.3f, rng, 0.2f, (3200, 0.003f, 1));
            return Finish(d, Rate, 0.05f);
        }

        // ---- ambience: wind ----------------------------------------------------------------

        public sealed class WindLook
        {
            /// <summary>Where the body of the wind sits (low-pass Hz).</summary>
            public float LowHz = 400;
            /// <summary>Whistle in wires, rigging or rock (level, centre Hz).</summary>
            public float Whistle, WhistleHz = 900;
            /// <summary>Rustle of reeds and grass, and the hiss of blown sand.</summary>
            public float Rustle, Hiss;
            /// <summary>How hard it gusts (0 steady, 1 wild).</summary>
            public float Gust = 0.5f;
        }

        public static WindLook WindFor(string mapId) => mapId switch
        {
            "twin-bridges" => new WindLook { LowHz = 380, Whistle = 0.05f, WhistleHz = 1100, Gust = 0.45f },
            "watchtowers" => new WindLook { LowHz = 320, Rustle = 0.55f, Gust = 0.5f },
            "oil-rigs" => new WindLook { LowHz = 450, Whistle = 0.3f, WhistleHz = 650, Gust = 0.55f },
            "dry-valley" => new WindLook { LowHz = 600, Hiss = 0.35f, Gust = 0.85f },
            "harbor-rooftops" => new WindLook { LowHz = 400, Whistle = 0.12f, WhistleHz = 1250, Gust = 0.5f },
            "alpine-ridge" => new WindLook { LowHz = 550, Whistle = 0.35f, WhistleHz = 1300, Hiss = 0.1f, Gust = 0.9f },
            _ => new WindLook { LowHz = 350, Rustle = 0.15f, Gust = 0.35f },
        };

        /// <summary>A 16 s loop of the map's wind. Sound sets its level and pitch from the lane wind.</summary>
        static float[] WindBed(WindLook w, int seed)
        {
            var rng = new Rng(seed);
            const float L = 16;
            int r = BedRate, x = r;
            var d = new float[(int)(L * r) + x];
            Pole rumble = default, drift = default, flutter = default, flt = default;
            Svf body = default, whistle = default, whistle2 = default, rustle = default, hiss = default;
            rustle.Set(4000, 0.7f, r);
            hiss.Set(6000, 0.8f, r);
            float cd = Coef(0.4f, r), cf = Coef(9, r), cft = Coef(1500, r), cl = Coef(140, r);
            float driftScale = 1 / (0.577f * MathF.Sqrt(cd / 2));
            float flutterScale = 1 / (0.577f * MathF.Sqrt(cf / 2));
            float lowScale = 0.5f / (0.577f * MathF.Sqrt(cl / 2));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)r;
                // Gusts: slow swells (whole cycles per loop) and a wandering random part.
                float gust = 0.6f + w.Gust * (0.22f * MathF.Sin(2 * MathF.PI * 2 * t / L) + 0.14f * MathF.Sin(2 * MathF.PI * 5 * t / L + 1)
                    + 0.18f * drift.Lp(rng.Noise(), cd) * driftScale);
                gust = MathF.Max(0.15f, gust);
                // The rush of air past the ears: a broad band that brightens as it blows harder.
                if (i % 16 == 0) body.Set(w.LowHz * (0.5f + 0.8f * gust), 0.5f, r);
                float s = (body.Band(rng.Noise()) + 0.4f * rumble.Lp(rng.Noise(), cl) * lowScale) * gust;
                if (w.Whistle > 0)
                {
                    if (i % 16 == 0)
                    {
                        whistle.Set(w.WhistleHz * (0.85f + 0.3f * gust), 14, r);
                        whistle2.Set(w.WhistleHz * 1.47f * (0.9f + 0.2f * gust), 18, r);
                    }
                    float n = rng.Noise();
                    s += w.Whistle * 2.5f * (whistle.Band(n) + 0.5f * whistle2.Band(n)) * gust * gust * gust;
                }
                if (w.Rustle > 0)
                {
                    // Reeds and grass: bright noise in fluttering grains.
                    float f = MathF.Max(0, flutter.Lp(rng.Noise(), cf) * flutterScale);
                    s += w.Rustle * rustle.Band(flt.Lp(rng.Noise(), cft) * 3) * f * gust;
                }
                if (w.Hiss > 0) s += w.Hiss * hiss.Band(rng.Noise()) * MathF.Pow(gust, 2.5f);
                d[i] = s;
            }
            d = Loop(d, x);
            Highpass(d, 60, r);
            return Finish(d, r, 0);
        }

        // ---- ambience: life ----------------------------------------------------------------

        /// <summary>
        /// A 24 s loop of what lives and moves on the map, apart from the wind. Each layer and event
        /// is normalised and placed at a set peak level, so the maps sit at like loudness: busy ones
        /// (city, sea) fuller, sparse ones (desert, mountains) mostly quiet with the odd call.
        /// </summary>
        static float[] Life(string id, int seed)
        {
            var rng = new Rng(seed);
            const float L = 24;
            int r = BedRate, x = r;
            var d = new float[(int)(L * r) + x];
            switch (id)
            {
                case "twin-bridges":
                    Layer(d, 0.3f, t => Traffic(t, rng, 5));
                    Layer(d, 0.1f, t => Wash(t, rng));
                    d = Loop(d, x);
                    Scatter(d, rng, 26, 0.08f, 0.2f, () => Lap(rng));
                    Scatter(d, rng, 4, 0.12f, 0.3f, () => Gull(rng));
                    Scatter(d, rng, 1, 0.06f, 0.06f, () => CarHorn(rng));
                    break;
                case "watchtowers":
                    Layer(d, 0.05f, t => Wash(t, rng));
                    d = Loop(d, x);
                    Layer(d, 0.16f, t => Crickets(t, rng, 6));
                    Scatter(d, rng, 12, 0.1f, 0.3f, () => Frog(rng));
                    Scatter(d, rng, 8, 0.04f, 0.1f, () => Lap(rng));
                    break;
                case "oil-rigs":
                    Layer(d, 0.4f, t => Waves(t, rng));
                    d = Loop(d, x);
                    Scatter(d, rng, 6, 0.12f, 0.25f, () => Slap(rng));
                    Scatter(d, rng, 4, 0.1f, 0.22f, () => Creak(rng));
                    Scatter(d, rng, 3, 0.05f, 0.1f, () => Clank(rng));
                    Scatter(d, rng, 2, 0.05f, 0.08f, () => Gull(rng));
                    break;
                case "dry-valley":
                    d = Loop(d, x);
                    Scatter(d, rng, 7, 0.04f, 0.1f, () => Stridulate(rng));
                    Scatter(d, rng, 1, 0.12f, 0.12f, () => Cry(rng, 2900, 2000, 0.9f));
                    break;
                case "harbor-rooftops":
                    Layer(d, 0.1f, t => Traffic(t, rng, 2));
                    Layer(d, 0.08f, t => Wash(t, rng));
                    d = Loop(d, x);
                    Scatter(d, rng, 20, 0.06f, 0.15f, () => Lap(rng));
                    Scatter(d, rng, 8, 0.12f, 0.3f, () => Gull(rng));
                    Scatter(d, rng, 1, 0.3f, 0.3f, () => ShipHorn(rng));
                    Scatter(d, rng, 1, 0.12f, 0.12f, () => Crane(rng));
                    Scatter(d, rng, 2, 0.04f, 0.08f, () => Clank(rng));
                    break;
                case "alpine-ridge":
                    d = Loop(d, x);
                    Scatter(d, rng, 3, 0.07f, 0.15f, () => Cry(rng, 2600, 1700, 0.28f));
                    Scatter(d, rng, 3, 0.03f, 0.06f, () => Song(rng, 5));
                    break;
                default:
                    d = Loop(d, x);
                    Scatter(d, rng, 6, 0.08f, 0.2f, () => Song(rng, 12));
                    Scatter(d, rng, 1, 0.1f, 0.1f, () => RangeShot(rng));
                    break;
            }
            Highpass(d, 60, r);
            // Only ever turned down, so the levels above hold across maps.
            float peak = Peak(d);
            if (peak > 0.89f) Normalise(d, 0.89f);
            return d;
        }

        /// <summary>
        /// Two-pole high-pass on a loop: keeps rumble a phone can't play from eating headroom. The
        /// filter first runs over the loop's last half second so it starts settled, and the seam stays clean.
        /// </summary>
        static void Highpass(float[] d, float hz, int rate)
        {
            Pole a = default, b = default;
            float c = Coef(hz, rate);
            for (int i = Math.Max(0, d.Length - rate / 2); i < d.Length; i++) b.Lp(d[i] - a.Lp(d[i], c), c);
            for (int i = 0; i < d.Length; i++)
            {
                float h = d[i] - a.Lp(d[i], c);
                d[i] = h - b.Lp(h, c);
            }
        }

        /// <summary>Builds a continuous layer in its own buffer and adds it at `gain` peak.</summary>
        static void Layer(float[] d, float gain, Action<float[]> fill)
        {
            var t = new float[d.Length];
            fill(t);
            Mix(d, Normalise(t), 0, gain);
        }

        /// <summary>Drops `count` copies of an event at random places round the loop, each at a random peak level.</summary>
        static void Scatter(float[] d, Rng rng, int count, float minGain, float maxGain, Func<float[]> make)
        {
            for (int k = 0; k < count; k++)
                Mix(d, make(), (int)(rng.Next() * d.Length), rng.Range(minGain, maxGain), wrap: true);
        }

        /// <summary>Low water movement: a soft band of noise rising and falling.</summary>
        static void Wash(float[] d, Rng rng)
        {
            var f = new Svf();
            f.Set(380, 0.6f, BedRate);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)BedRate;
                d[i] += f.Band(rng.Noise()) * (0.7f + 0.3f * MathF.Sin(2 * MathF.PI * t / 4.7f));
            }
        }

        /// <summary>A city across the water: a low roar of traffic with cars swelling past now and then.</summary>
        static void Traffic(float[] d, Rng rng, int passes)
        {
            Svf roar = default, hum = default;
            roar.Set(170, 0.7f, BedRate);
            hum.Set(320, 1, BedRate);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)BedRate;
                d[i] += roar.Band(rng.Noise()) + hum.Band(rng.Noise()) * 0.4f * (0.8f + 0.2f * MathF.Sin(t * 0.7f));
            }
            for (int k = 0; k < passes; k++)
            {
                float dur = rng.Range(4, 6);
                var car = Make(dur, BedRate);
                Svf tone = default, tyre = default;
                tyre.Set(1200, 0.7f, BedRate);
                for (int i = 0; i < car.Length; i++)
                {
                    float u = i / (float)car.Length;
                    // Doppler: a little higher coming, lower going.
                    if (i % 32 == 0) tone.Set(420 - 90 * u, 1.2f, BedRate);
                    float n = rng.Noise();
                    car[i] = (tone.Band(n) + 0.3f * tyre.Band(n)) * MathF.Pow(Sin0(MathF.PI * u), 2);
                }
                Mix(d, car, (int)(rng.Next() * d.Length), rng.Range(1, 2), wrap: true);
            }
        }

        /// <summary>One small wave lapping at a pier or bank, sometimes with a little plip.</summary>
        static float[] Lap(Rng rng)
        {
            var lap = Make(0.5f, BedRate);
            Burst(lap, BedRate, 0, 1, rng, rng.Range(500, 1100), 1.2f, 0.03f, 0.09f);
            if (rng.Next() < 0.4f)
            {
                double p = 0;
                int o = (int)(rng.Range(0.05f, 0.2f) * BedRate);
                float f0 = rng.Range(650, 900);
                for (int i = 0; i < 0.05f * BedRate && o + i < lap.Length; i++)
                {
                    float t = i / (float)BedRate;
                    p += 2 * Math.PI * f0 * (1 + 0.8f * t / 0.025f) / BedRate;
                    lap[o + i] += 0.25f * (float)Math.Sin(p) * MathF.Exp(-t / 0.012f);
                }
            }
            return Normalise(lap);
        }

        /// <summary>Open sea: long swells and the wash as they break round the rig legs (8 s period, whole cycles per loop).</summary>
        static void Waves(float[] d, Rng rng)
        {
            Svf body = default, wash = default;
            body.Set(260, 0.5f, BedRate);
            wash.Set(1600, 0.5f, BedRate);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)BedRate;
                float swell = 0.55f + 0.3f * MathF.Sin(2 * MathF.PI * 3 * t / 24) + 0.15f * MathF.Sin(2 * MathF.PI * 5 * t / 24 + 2);
                float brk = MathF.Pow(MathF.Max(0, MathF.Sin(2 * MathF.PI * 3 * t / 24 - 1.2f)), 3);
                d[i] += body.Band(rng.Noise()) * swell + wash.Band(rng.Noise()) * 0.6f * (0.15f + brk);
            }
        }

        static float[] Slap(Rng rng)
        {
            var e = Make(0.8f, BedRate);
            Burst(e, BedRate, 0, 1, rng, 300, 0.8f, 0.005f, 0.12f);
            Burst(e, BedRate, 0, 0.5f, rng, 1200, 0.6f, 0.002f, 0.05f);
            return Normalise(e);
        }

        /// <summary>Steel under load: a slow stick-slip groan through the structure's resonances.</summary>
        static float[] Creak(Rng rng)
        {
            float dur = rng.Range(1, 2.2f);
            var e = Make(dur + 0.3f, BedRate);
            Svf r1 = default, r2 = default, r3 = default;
            r1.Set(rng.Range(350, 480), 8, BedRate);
            r2.Set(rng.Range(1000, 1300), 10, BedRate);
            r3.Set(rng.Range(2100, 2500), 12, BedRate);
            float f0 = rng.Range(40, 60), f1 = rng.Range(90, 130);
            float next = 0;
            for (int i = 0; i < e.Length; i++)
            {
                float t = i / (float)BedRate;
                float u = MathF.Min(1, t / dur);
                float x = 0;
                if (t >= next && t < dur)
                {
                    x = 1;
                    float hz = f0 + (f1 - f0) * MathF.Sin(MathF.PI * u * 0.5f);
                    next = t + 1 / hz * rng.Range(0.88f, 1.12f);
                }
                float env = MathF.Pow(Sin0(MathF.PI * u), 0.7f);
                e[i] = (r1.Band(x) + 0.7f * r2.Band(x) + 0.4f * r3.Band(x)) * env;
            }
            return Normalise(e);
        }

        static float[] Clank(Rng rng)
        {
            var e = Make(1.6f, BedRate);
            float k = rng.Range(0.85f, 1.15f);
            Ring(e, BedRate, 0, 1, rng, 0.6f, (480 * k, 0.6f, 1), (1210 * k, 0.4f, 0.6f), (1990 * k, 0.25f, 0.4f), (2870 * k, 0.15f, 0.25f));
            LowpassInPlace(e, 2200, BedRate);
            return Normalise(e);
        }

        /// <summary>A herring gull: two to five raucous "kyow" notes, heard from a distance.</summary>
        static float[] Gull(Rng rng)
        {
            var e = Make(1.8f, BedRate);
            int notes = 2 + (int)(rng.Next() * 4);
            float at = 0;
            Pole rasp = default;
            float cr = Coef(1500, BedRate);
            for (int k = 0; k < notes && at < 1.5f; k++)
            {
                float dur = rng.Range(0.14f, 0.26f);
                float peak = rng.Range(1700, 2100), end = peak * rng.Range(0.55f, 0.68f), start = peak * 0.8f;
                double ph = 0;
                int o = (int)(at * BedRate);
                int n = (int)(dur * BedRate);
                for (int i = 0; i < n && o + i < e.Length; i++)
                {
                    float u = i / (float)n;
                    float t = i / (float)BedRate;
                    float f = u < 0.2f ? start + (peak - start) * u / 0.2f : peak + (end - peak) * (u - 0.2f) / 0.8f;
                    ph += 2 * Math.PI * f * (1 + 0.015f * MathF.Sin(2 * MathF.PI * 28 * t)) / BedRate;
                    float p = (float)ph;
                    float s = MathF.Sin(p) + 0.6f * MathF.Sin(2 * p) + 0.35f * MathF.Sin(3 * p) + 0.2f * MathF.Sin(4 * p);
                    float raspy = 1 + 0.6f * rasp.Lp(rng.Noise(), cr) * 6;
                    e[o + i] += s * raspy * MathF.Pow(Sin0(MathF.PI * u), 0.6f);
                }
                at += dur + rng.Range(0.06f, 0.14f);
            }
            LowpassInPlace(e, 3500, BedRate);
            return Normalise(e);
        }

        static float[] CarHorn(Rng rng)
        {
            var e = Make(0.8f, BedRate);
            foreach (float at in new[] { 0f, 0.3f })
            {
                int o = (int)(at * BedRate);
                for (int i = 0; i < 0.18f * BedRate; i++)
                {
                    float t = i / (float)BedRate;
                    float env = MathF.Min(1, t / 0.01f) * MathF.Min(1, (0.18f - t) / 0.02f);
                    float s = 0;
                    foreach (float hz in new[] { 415f, 523f })
                        s += MathF.Sin(2 * MathF.PI * hz * t) + 0.3f * MathF.Sin(6 * MathF.PI * hz * t) + 0.15f * MathF.Sin(10 * MathF.PI * hz * t);
                    e[o + i] += s * env;
                }
            }
            LowpassInPlace(e, 1500, BedRate);
            return Normalise(e);
        }

        /// <summary>A ship's horn across the harbor: a long low chord, and its echo off the warehouses.</summary>
        static float[] ShipHorn(Rng rng)
        {
            var e = Make(6, BedRate);
            for (int i = 0; i < 4.2f * BedRate; i++)
            {
                float t = i / (float)BedRate;
                float env = MathF.Min(1, t / 0.4f) * MathF.Min(1, MathF.Max(0, (4.2f - t) / 1.2f));
                float vib = 1 + 0.003f * MathF.Sin(2 * MathF.PI * 4 * t);
                float s = 0;
                foreach (float hz in new[] { 92f, 138f })
                    for (int h = 1; h <= 12; h++)
                        s += MathF.Sin(2 * MathF.PI * hz * vib * h * t) / h;
                e[i] = s * env;
            }
            LowpassInPlace(e, 900, BedRate);
            var echo = Lowpass(e, 500, BedRate);
            Mix(e, echo, (int)(1.1f * BedRate), 0.3f);
            return Normalise(e);
        }

        /// <summary>A dockside crane: motor whine rising and falling, chain rattling, a clank as the load lands.</summary>
        static float[] Crane(Rng rng)
        {
            var e = Make(5, BedRate);
            double ph = 0;
            for (int i = 0; i < 3.6f * BedRate; i++)
            {
                float t = i / (float)BedRate;
                float u = t / 3.6f;
                float hz = 160 + 40 * MathF.Sin(MathF.PI * u);
                ph += 2 * Math.PI * hz / BedRate;
                float p = (float)ph;
                float env = MathF.Pow(Sin0(MathF.PI * u), 0.5f);
                e[i] += (MathF.Sin(p) + 0.5f * MathF.Sin(2 * p) + 0.3f * MathF.Sin(3 * p)) * env * 0.5f;
            }
            Grains(e, BedRate, 0.3f, 3, 0.25f, rng, 40, 2000, 3200, 3, 3);
            var clank = Clank(rng);
            Mix(e, clank, (int)(3.7f * BedRate), 0.8f);
            LowpassInPlace(e, 3000, BedRate);
            return Normalise(e);
        }

        static float[] Frog(Rng rng)
        {
            var e = Make(1.8f, BedRate);
            int croaks = 1 + (int)(rng.Next() * 3);
            float f1 = rng.Range(280, 420), f2 = rng.Range(950, 1300), rateHz = rng.Range(16, 28);
            for (int c = 0; c < croaks; c++)
            {
                float dur = rng.Range(0.25f, 0.5f);
                int o = (int)(c * 0.6f * BedRate);
                int pulses = (int)(dur * rateHz);
                for (int p = 0; p < pulses; p++)
                {
                    float amp = MathF.Sin(MathF.PI * (p + 0.5f) / pulses);
                    int po = o + (int)(p / rateHz * BedRate);
                    for (int i = 0; i < 0.04f * BedRate && po + i < e.Length; i++)
                    {
                        float t = i / (float)BedRate;
                        e[po + i] += amp * MathF.Exp(-t / 0.008f) * (MathF.Sin(2 * MathF.PI * f1 * t) + 0.5f * MathF.Sin(2 * MathF.PI * f2 * t));
                    }
                }
            }
            return Normalise(e);
        }

        /// <summary>A field of crickets: each a pure tone chirping in groups of three or four pulses, on its own clock.</summary>
        static void Crickets(float[] d, Rng rng, int count)
        {
            float L = d.Length / (float)BedRate;
            for (int c = 0; c < count; c++)
            {
                float hz = rng.Range(4000, 5200), period = rng.Range(0.45f, 0.8f), amp = rng.Range(0.3f, 1);
                int pulses = rng.Next() < 0.5f ? 3 : 4;
                const float len = 0.011f, gap = 0.008f;
                for (float t = rng.Next() * period; t < L; t += period * rng.Range(0.95f, 1.05f))
                {
                    if (rng.Next() < 0.1f) continue;
                    for (int p = 0; p < pulses; p++)
                    {
                        int o = (int)((t + p * (len + gap)) * BedRate);
                        int n = (int)(len * BedRate);
                        for (int i = 0; i < n; i++)
                        {
                            float s = MathF.Sin(MathF.PI * i / n);
                            d[(o + i) % d.Length] += amp * MathF.Sin(2 * MathF.PI * hz * i / BedRate) * s * s;
                        }
                    }
                }
            }
        }

        /// <summary>A grasshopper's dry rasp in the desert scrub.</summary>
        static float[] Stridulate(Rng rng)
        {
            var e = Make(1.2f, BedRate);
            int bursts = 2 + (int)(rng.Next() * 3);
            for (int b = 0; b < bursts; b++)
                Grains(e, BedRate, b * 0.28f, 0.18f, 1, rng, 220, 3500, 6000, 1, 0.6f);
            return Normalise(e);
        }

        /// <summary>A bird of prey or a mountain chough: one rising-then-falling harsh cry.</summary>
        static float[] Cry(Rng rng, float f0, float f1, float dur)
        {
            var e = Make(dur + 0.1f, BedRate);
            double ph = 0;
            Pole rasp = default;
            float cr = Coef(900, BedRate);
            int n = (int)(dur * BedRate);
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                float f = u < 0.15f ? f0 * (0.85f + u) : f0 + (f1 - f0) * (u - 0.15f) / 0.85f;
                ph += 2 * Math.PI * f / BedRate;
                float p = (float)ph;
                float s = MathF.Sin(p) + 0.4f * MathF.Sin(2 * p) + 0.15f * MathF.Sin(3 * p);
                e[i] = s * (1 + 3 * rasp.Lp(rng.Noise(), cr)) * MathF.Pow(Sin0(MathF.PI * u), 0.5f);
            }
            LowpassInPlace(e, 4500, BedRate);
            return Normalise(e);
        }

        /// <summary>A songbird: a run of whistles, trills and sweeps.</summary>
        static float[] Song(Rng rng, int maxNotes)
        {
            var e = Make(2.2f, BedRate);
            float baseHz = rng.Range(2600, 3800);
            int notes = Math.Max(3, (int)(maxNotes * rng.Range(0.55f, 1)));
            float at = 0;
            for (int k = 0; k < notes && at < 2; k++)
            {
                float dur = rng.Range(0.04f, 0.13f);
                int kind = (int)(rng.Next() * 3);
                float f = baseHz * rng.Range(0.8f, 1.3f), glide = rng.Range(-0.1f, 0.1f);
                double ph = 0;
                int o = (int)(at * BedRate), n = (int)(dur * BedRate);
                for (int i = 0; i < n && o + i < e.Length; i++)
                {
                    float u = i / (float)n, t = i / (float)BedRate;
                    float hz = kind == 0 ? f * (1 + glide * u)
                        : kind == 1 ? f + 400 * MathF.Sin(2 * MathF.PI * 45 * t)
                        : f * (1.3f - 0.55f * u);
                    ph += 2 * Math.PI * hz / BedRate;
                    e[o + i] += ((float)Math.Sin(ph) + 0.08f * (float)Math.Sin(2 * ph)) * MathF.Pow(Sin0(MathF.PI * u), 0.8f);
                }
                at += dur + rng.Range(0.02f, 0.07f);
            }
            return Normalise(e);
        }

        /// <summary>Someone on a far lane: a dull distant report and, a second later, the faint ping of steel.</summary>
        static float[] RangeShot(Rng rng)
        {
            var e = Make(2.5f, BedRate);
            Thump(e, BedRate, 0, 1, 140, 50, 0.02f, 0.12f);
            Burst(e, BedRate, 0, 0.8f, rng, 500, 0.6f, 0.004f, 0.08f);
            Burst(e, BedRate, 0.05f, 0.3f, rng, 400, 0.6f, 0.05f, 0.4f);
            Ring(e, BedRate, 1.05f, 0.25f, rng, 0.2f, (1100, 0.3f, 1), (2650, 0.2f, 0.6f), (4400, 0.12f, 0.3f));
            LowpassInPlace(e, 1800, BedRate);
            return Normalise(e);
        }
    }
}
