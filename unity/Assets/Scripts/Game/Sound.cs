using System;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>
    /// Every sound in the game, synthesised at start-up so nothing needs downloading or licensing:
    /// your rifle's blast with its echo off the banks, the enemy's distant report, the supersonic
    /// crack of a near miss, the far-off thump of a hit, the bolt cycling, menu clicks, and a looped
    /// wind-and-water bed. Each is built from shaped noise, swept tones and filtered echoes.
    /// </summary>
    public sealed class Sound : MonoBehaviour
    {
        const int Rate = 44100;
        const float SpeedOfSound = 343;

        static Sound? instance;
        public static bool Enabled = true;
        public static float Volume = 1;
        /// <summary>Level of the wind-and-water bed, on top of Volume.</summary>
        public static float AmbienceVolume = 1;
        /// <summary>Menu taps click.</summary>
        public static bool Clicks = true;

        AudioClip shotNear = null!, shotFar = null!, crack = null!, thump = null!, splash = null!, bolt = null!, click = null!, ambience = null!;
        AudioSource[] voices = null!;
        AudioSource bed = null!;
        int next;

        public static void Init()
        {
            if (instance != null) return;
            var go = new GameObject("Sound");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Sound>();
            instance.Build();
        }

        void Build()
        {
            var rng = new System.Random(7);
            shotNear = Make("Shot near", 2.8f, rng, ShotNear);
            shotFar = Make("Shot far", 3.2f, rng, ShotFar);
            crack = Make("Crack", 0.25f, rng, Crack);
            thump = Make("Thump", 0.5f, rng, Thump);
            splash = Make("Splash", 0.9f, rng, Splash);
            bolt = Make("Bolt", 0.9f, rng, Bolt);
            click = Make("Click", 0.06f, rng, Click);
            ambience = Make("Ambience", 12f, rng, Ambience);

            voices = new AudioSource[8];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0;
            }
            bed = gameObject.AddComponent<AudioSource>();
            bed.clip = ambience;
            bed.loop = true;
            bed.volume = 0;
            bed.Play();
        }

        void Update()
        {
            // The wind-and-water bed fades in when a world is on screen and is ducked in menus.
            if (bed == null) return;
            float target = Enabled ? AmbienceVolume * (AmbienceOn ? 0.55f : 0.18f) : 0;
            bed.volume = Mathf.MoveTowards(bed.volume, target, Time.unscaledDeltaTime * 0.5f);
        }

        public static bool AmbienceOn;

        static void Play(Func<Sound, AudioClip> clip, float volume, float delay = 0, float pitch = 1)
        {
            if (!Enabled || instance == null) return;
            var s = instance;
            var v = s.voices[s.next];
            s.next = (s.next + 1) % s.voices.Length;
            v.Stop();
            v.clip = clip(s);
            v.volume = volume * Volume;
            v.pitch = pitch;
            if (delay > 0) v.PlayDelayed(delay);
            else v.Play();
        }

        /// <summary>Your own shot, then the bolt worked once the rifle settles.</summary>
        public static void FireMine()
        {
            Play(s => s.shotNear, 1);
            Play(s => s.bolt, 0.7f, 0.9f);
        }

        /// <summary>
        /// The enemy fires from `distanceM` away. A bullet passing close arrives before its sound:
        /// first the crack, then the report rolling in at the speed of sound.
        /// </summary>
        public static void FireEnemy(float distanceM, float flightS, bool nearMiss, bool hitMe)
        {
            if (nearMiss || hitMe) Play(s => s.crack, 0.9f, flightS);
            if (hitMe) Play(s => s.thump, 1, flightS + 0.02f, 0.8f);
            Play(s => s.shotFar, Mathf.Clamp(300 / Mathf.Max(distanceM, 1), 0.25f, 0.9f), distanceM / SpeedOfSound);
        }

        /// <summary>The faint sound of your bullet landing, heard after it travels back.</summary>
        public static void Landed(float distanceM, float flightS, bool water, bool hit)
        {
            float delay = flightS + distanceM / SpeedOfSound;
            float volume = Mathf.Clamp(120 / Mathf.Max(distanceM, 1), 0.08f, 0.5f);
            if (water) Play(s => s.splash, volume, delay);
            else Play(s => s.thump, volume * (hit ? 1.2f : 0.8f), delay, hit ? 0.9f : 1.3f);
        }

        public static void UiClick()
        {
            if (Clicks) Play(s => s.click, 0.35f);
        }

        // --- Synthesis -------------------------------------------------------------------------

        delegate void Writer(float[] d, System.Random rng);

        static AudioClip Make(string name, float seconds, System.Random rng, Writer write)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            write(d, rng);
            // Normalise to -1 dBFS and fade the last 20 ms so nothing clicks.
            float peak = 1e-6f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            float g = 0.89f / peak;
            int fade = Math.Min(n, Rate / 50);
            for (int i = 0; i < n; i++)
            {
                d[i] *= g;
                if (i >= n - fade && name != "Ambience") d[i] *= (n - i) / (float)fade;
            }
            // The ambience loop already folded its last half second into its start; drop it.
            if (name == "Ambience") { n -= Rate / 2; Array.Resize(ref d, n); }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2 - 1);
        static float Coef(float hz) => 1 - Mathf.Exp(-2 * Mathf.PI * hz / Rate);

        /// <summary>Muzzle blast: a sharp transient, a falling low boom, and a filtered roar.</summary>
        static void Blast(float[] d, System.Random rng, int start, float gain, float brightHz, float boomGain)
        {
            float lp = 0, lp2 = 0;
            float phase = 0;
            for (int i = 0; start + i < d.Length && i < Rate * 0.6f; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(rng);
                float cut = Coef(brightHz * Mathf.Exp(-t / 0.05f) + 400);
                lp += cut * (n - lp);
                lp2 += cut * (lp - lp2);
                float roar = lp2 * Mathf.Exp(-t / 0.045f) * 3.2f;
                float f = 45 + 70 * Mathf.Exp(-t / 0.05f);
                phase += 2 * Mathf.PI * f / Rate;
                float boom = Mathf.Sin(phase) * Mathf.Exp(-t / 0.11f) * boomGain;
                float spike = t < 0.0015f ? 1.5f * (1 - t / 0.0015f) : 0;
                d[start + i] += gain * (roar + boom + spike);
            }
        }

        /// <summary>Diffuse tail: low-passed noise decaying over `seconds`, swelling with the echoes.</summary>
        static void Tail(float[] d, System.Random rng, float gain, float seconds, float hz, float startS)
        {
            float lp = 0, lp2 = 0, c = Coef(hz);
            for (int i = (int)(startS * Rate); i < d.Length; i++)
            {
                float t = i / (float)Rate - startS;
                lp += c * (Noise(rng) - lp);
                lp2 += c * (lp - lp2);
                float env = (1 - Mathf.Exp(-t / 0.04f)) * Mathf.Exp(-t / seconds);
                d[i] += lp2 * env * gain * 4;
            }
        }

        static void Smear(float[] d, int from, float hz)
        {
            float lp = 0, c = Coef(hz);
            for (int i = from; i < d.Length; i++) { lp += c * (d[i] - lp); d[i] = lp; }
        }

        static void ShotNear(float[] d, System.Random rng)
        {
            Blast(d, rng, 0, 1, 7000, 0.9f);
            Tail(d, rng, 0.22f, 0.55f, 1400, 0.03f);
            // Slap-back off the far bank and the bridge piers, duller each time.
            var echo = new float[d.Length];
            Blast(echo, rng, 0, 1, 2200, 0.7f);
            Smear(echo, 0, 1800);
            foreach (var (at, g) in new[] { (0.42f, 0.22f), (0.95f, 0.12f), (1.6f, 0.06f) })
            {
                int o = (int)(at * Rate);
                for (int i = 0; i + o < d.Length; i++) d[i + o] += echo[i] * g;
            }
            for (int i = 0; i < d.Length; i++) d[i] = (float)Math.Tanh(d[i] * 1.4f);
        }

        static void ShotFar(float[] d, System.Random rng)
        {
            Blast(d, rng, 0, 0.6f, 1100, 1.1f);
            Tail(d, rng, 0.35f, 0.9f, 600, 0.02f);
            Smear(d, 0, 900);
            var copy = (float[])d.Clone();
            int o = (int)(0.7f * Rate);
            for (int i = 0; i + o < d.Length; i++) d[i + o] += copy[i] * 0.3f;
        }

        /// <summary>The supersonic N-wave of a bullet passing within a few metres.</summary>
        static void Crack(float[] d, System.Random rng)
        {
            int nw = (int)(0.0022f * Rate);
            for (int i = 0; i < nw; i++) d[i] = 1 - 2f * i / nw;
            float lp = 0, c = Coef(5000);
            for (int i = nw; i < d.Length; i++)
            {
                float t = (i - nw) / (float)Rate;
                lp += c * (Noise(rng) - lp);
                d[i] += lp * Mathf.Exp(-t / 0.03f) * 0.5f;
            }
        }

        static void Thump(float[] d, System.Random rng)
        {
            float phase = 0, lp = 0, c = Coef(900);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                phase += 2 * Mathf.PI * (70 + 60 * Mathf.Exp(-t / 0.02f)) / Rate;
                lp += c * (Noise(rng) - lp);
                d[i] = Mathf.Sin(phase) * Mathf.Exp(-t / 0.07f) + lp * Mathf.Exp(-t / 0.03f) * 1.5f;
            }
        }

        static void Splash(float[] d, System.Random rng)
        {
            float lp = 0, hp = 0, c = Coef(2500);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(rng);
                lp += c * (n - lp);
                hp += Coef(300) * (lp - hp);
                float env = Mathf.Exp(-t / 0.18f) * (1 - Mathf.Exp(-t / 0.004f));
                // Droplets falling back.
                float drops = rng.NextDouble() < 0.0008 * Mathf.Exp(-t / 0.4f) ? 0.8f : 0;
                d[i] = (lp - hp) * env * 2 + drops;
            }
            Smear(d, 0, 4000);
        }

        /// <summary>Bolt up, back, forward, down: four metallic clicks with a ring.</summary>
        static void Bolt(float[] d, System.Random rng)
        {
            foreach (var (at, g, f) in new[] { (0.0f, 0.8f, 1.0f), (0.16f, 0.6f, 0.85f), (0.44f, 0.9f, 1.1f), (0.6f, 1.0f, 0.95f) })
            {
                int o = (int)(at * Rate);
                for (int i = 0; i < Rate * 0.08f && o + i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    float ring = Mathf.Sin(2 * Mathf.PI * 2100 * f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * 3450 * f * t) * 0.35f + Mathf.Sin(2 * Mathf.PI * 5230 * f * t) * 0.2f;
                    float hit = t < 0.004f ? Noise(rng) : 0;
                    d[o + i] += g * (ring * Mathf.Exp(-t / 0.018f) + hit);
                }
            }
        }

        static void Click(float[] d, System.Random rng)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = Mathf.Sin(2 * Mathf.PI * 1900 * t) * Mathf.Exp(-t / 0.008f) + (t < 0.001f ? Noise(rng) * 0.5f : 0);
            }
        }

        /// <summary>Wind gusting over open water and small waves on the piers, looping every 12 s.</summary>
        static void Ambience(float[] d, System.Random rng)
        {
            float brown = 0, lp = 0, w1 = 0, w2 = 0;
            float total = d.Length / (float)Rate;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                brown = brown * 0.997f + Noise(rng) * 0.03f;
                lp += Coef(500) * (brown - lp);
                // Gusts on periods that divide the loop, so the end meets the start.
                float gust = 0.55f + 0.25f * Mathf.Sin(2 * Mathf.PI * t * 3 / total) + 0.2f * Mathf.Sin(2 * Mathf.PI * t * 7 / total + 1);
                float wind = lp * gust * 6;
                float n = Noise(rng);
                w1 += Coef(900) * (n - w1);
                w2 += Coef(250) * (w1 - w2);
                float lap = Mathf.Pow(Mathf.Max(0, Mathf.Sin(2 * Mathf.PI * t * 5 / total)), 3) * 0.7f + 0.3f;
                d[i] = wind + (w1 - w2) * lap * 1.2f;
            }
            // Crossfade the last half second into the first so the loop is seamless.
            int x = Rate / 2;
            for (int i = 0; i < x; i++)
            {
                float a = i / (float)x;
                d[i] = d[i] * a + d[d.Length - x + i] * (1 - a);
            }
        }
    }
}
