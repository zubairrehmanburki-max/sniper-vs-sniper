using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>
    /// Plays the game's sound. Every clip is synthesised by Synth on a worker thread, the shared
    /// ones at start-up and each map's own (its rifle reports with its echoes, its wind and its
    /// ambience) when the map loads, then turned into AudioClips here. Only the current map's set is
    /// kept: about 3 MB shared plus 5 MB per map of PCM.
    ///
    /// The mix: a pool of one-shot voices, scheduled with PlayDelayed so far-off sounds arrive at
    /// the speed of sound; a few far voices behind low-pass filters, muffled by distance; two
    /// looping beds (the wind, whose level and pitch follow the lane wind, and the map's life); and
    /// the kill cam's whoosh and drone. The world ducks under the kill cam. Nothing here throws when
    /// the machine has no audio device (the CI capture): it just stays silent.
    /// </summary>
    public sealed class Sound : MonoBehaviour
    {
        /// <summary>What a bullet hit, for the impact sound.</summary>
        public enum Surface { None, Flesh, Dirt, Water, Concrete, Metal, Wood, Glass, Tin, Clay, Pop }

        const float SpeedOfSound = 343;

        static Sound? instance;
        public static bool Enabled = true;
        /// <summary>Effects level (Settings), on top of the listener's master volume.</summary>
        public static float Volume = 1;
        /// <summary>Level of the wind and ambience beds, on top of the master volume.</summary>
        public static float AmbienceVolume = 1;
        /// <summary>Menu taps click.</summary>
        public static bool Clicks = true;
        /// <summary>A world view is on screen; in the menus the beds play quietly behind.</summary>
        public static bool AmbienceOn;
        /// <summary>Lane wind speed, m/s: sets the wind bed's level and pitch.</summary>
        public static float Wind = 4;

        sealed class Voice
        {
            public AudioSource Src = null!;
            public AudioLowPassFilter? Filter;
            /// <summary>Level before the effects volume and the kill-cam duck.</summary>
            public float Level;
            public bool Duck;
            public float BusyUntil;
        }

        readonly List<Voice> voices = new List<Voice>();
        readonly List<Voice> far = new List<Voice>();
        AudioSource windBed = null!, lifeBed = null!, whoosh = null!, drone = null!;

        readonly ConcurrentQueue<(string group, Synth.Clip clip)> baked = new ConcurrentQueue<(string, Synth.Clip)>();
        readonly Dictionary<string, AudioClip> shared = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioClip> local = new Dictionary<string, AudioClip>();
        string mapId = "";
        bool warned;

        float windShown = 4, duck = 1, lastBedCheck, lastZoom = -1;
        bool killcam, killSlow, killHit, killPaused;
        float heartSeen = -10, heartStrain, nextBeat;

        public static void Init()
        {
            if (instance != null) return;
            var go = new GameObject("Sound");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Sound>();
            try { instance.Build(); }
            catch (Exception e) { instance.Warn(e); }
            Bake("", Synth.Common);
        }

        void Build()
        {
            for (int i = 0; i < 12; i++) voices.Add(new Voice { Src = Source(gameObject) });
            // A low-pass filter acts on every source of its object, so each far voice has its own.
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Far " + i);
                go.transform.SetParent(transform, false);
                far.Add(new Voice { Src = Source(go), Filter = go.AddComponent<AudioLowPassFilter>() });
            }
            windBed = Source(gameObject, loop: true);
            lifeBed = Source(gameObject, loop: true);
            whoosh = Source(gameObject, loop: true);
            drone = Source(gameObject, loop: true);
        }

        static AudioSource Source(GameObject go, bool loop = false)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0;
            s.loop = loop;
            s.volume = 0;
            return s;
        }

        void Warn(Exception e)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[Sound] " + e.Message);
        }

        // ---- clips -------------------------------------------------------------------------

        /// <summary>Synthesises a set of clips off the main thread; group "" is shared, else a map id.</summary>
        static void Bake(string group, Func<List<Synth.Clip>> make)
        {
            var s = instance;
            if (s == null) return;
            Task.Run(() =>
            {
                try
                {
                    foreach (var c in make()) s.baked.Enqueue((group, c));
                }
                catch (Exception e) { Debug.LogWarning("[Sound] synthesis failed: " + e); }
            });
        }

        /// <summary>Loads the given map's report, echoes and ambience, dropping the last map's.</summary>
        public static void SetMap(string id)
        {
            var s = instance;
            if (s == null || id == s.mapId) return;
            s.mapId = id;
            foreach (var c in s.local.Values) s.Release(c);
            s.local.Clear();
            Bake(id, () => Synth.Map(id));
        }

        /// <summary>Turns finished PCM into clips (main thread only).</summary>
        void Receive()
        {
            while (baked.TryDequeue(out var b))
            {
                bool isShared = b.group.Length == 0;
                if (!isShared && b.group != mapId) continue;
                AudioClip? clip = null;
                try
                {
                    clip = AudioClip.Create(b.clip.Name, b.clip.Data.Length, 1, b.clip.Rate, false);
                    if (clip != null) clip.SetData(b.clip.Data, 0);
                }
                catch (Exception e) { Warn(e); }
                if (clip == null) continue;
                var into = isShared ? shared : local;
                if (into.TryGetValue(b.clip.Name, out var old)) Release(old);
                into[b.clip.Name] = clip;
                if (!isShared && b.clip.Name == "Wind") StartBed(windBed, clip);
                if (!isShared && b.clip.Name == "Life") StartBed(lifeBed, clip);
                if (!isShared && local.Count == 4) LogMemory();
            }
        }

        void LogMemory()
        {
            long bytes = 0;
            foreach (var c in shared.Values) bytes += (long)c.samples * c.channels * 4;
            foreach (var c in local.Values) bytes += (long)c.samples * c.channels * 4;
            Debug.Log($"[Sound] {mapId}: {shared.Count + local.Count} clips, {bytes / 1048576f:0.0} MB of PCM");
        }

        void StartBed(AudioSource bed, AudioClip clip)
        {
            try
            {
                bed.Stop();
                bed.clip = clip;
                bed.volume = 0;
                // Start somewhere along the loop so two visits don't sound identical.
                bed.timeSamples = UnityEngine.Random.Range(0, Math.Max(1, clip.samples - 1));
                bed.Play();
            }
            catch (Exception e) { Warn(e); }
        }

        void Release(AudioClip clip)
        {
            foreach (var v in voices) Unload(v, clip);
            foreach (var v in far) Unload(v, clip);
            foreach (var bed in new[] { windBed, lifeBed })
                if (bed != null && bed.clip == clip) { bed.Stop(); bed.clip = null; }
            Destroy(clip);
        }

        static void Unload(Voice v, AudioClip clip)
        {
            if (v.Src.clip != clip) return;
            v.Src.Stop();
            v.Src.clip = null;
            v.BusyUntil = 0;
        }

        AudioClip? Clip(string name) =>
            shared.TryGetValue(name, out var c) ? c : local.TryGetValue(name, out c) ? c : null;

        // ---- playing -----------------------------------------------------------------------

        /// <summary>
        /// Plays a clip on the voice that frees up soonest. `far` voices go through a low-pass at
        /// `cutoffHz`; `duck` lets the kill cam push it down.
        /// </summary>
        static void Play(string name, float level, float delay = 0, float pitch = 1, bool far = false, float cutoffHz = 22000, bool duck = true)
        {
            var s = instance;
            if (s == null || !Enabled) return;
            var clip = s.Clip(name);
            if (clip == null) return;
            var pool = far ? s.far : s.voices;
            var v = pool[0];
            foreach (var c in pool)
                if (c.BusyUntil < v.BusyUntil) v = c;
            v.Level = level;
            v.Duck = duck;
            v.BusyUntil = Time.unscaledTime + delay + clip.length / Mathf.Max(0.1f, pitch);
            try
            {
                v.Src.Stop();
                v.Src.clip = clip;
                v.Src.pitch = pitch;
                v.Src.volume = s.LevelOf(v);
                if (v.Filter != null) v.Filter.cutoffFrequency = cutoffHz;
                if (delay > 0) v.Src.PlayDelayed(delay);
                else v.Src.Play();
            }
            catch (Exception e) { s.Warn(e); }
        }

        float LevelOf(Voice v) => Enabled ? v.Level * Volume * (v.Duck ? duck : 1) : 0;

        static float Jitter(float amount) => 1 + UnityEngine.Random.Range(-amount, amount);

        /// <summary>Your own shot, then the bolt worked once the rifle settles.</summary>
        public static void FireMine()
        {
            Play("ShotNear", 1, 0, Jitter(0.02f));
            Play("Bolt", 0.5f, 0.85f, Jitter(0.02f));
        }

        /// <summary>
        /// The enemy fires from `shooterM` away. A round passing close arrives before its sound:
        /// first the crack (and the hit, or the strike beside you), then the report rolling in at
        /// the speed of sound, muffled by the distance.
        /// </summary>
        public static void FireEnemy(float shooterM, float flightS, bool nearMiss, bool hitMe, float impactM, Surface surface, uint seed)
        {
            if (nearMiss || hitMe) Play("Crack", hitMe ? 0.8f : 1, flightS, Jitter(0.04f), duck: false);
            if (hitMe) Play("Flesh", 1, flightS + 0.01f, 0.75f, duck: false);
            else if (impactM < 30 && surface != Surface.None)
                Impact(surface, Mathf.Clamp(8 / Mathf.Max(impactM, 1), 0.15f, 0.9f), flightS + impactM / SpeedOfSound, seed, false, 22000);
            float d = Mathf.Max(shooterM, 1);
            float cutoff = Mathf.Lerp(9000, 1500, Mathf.InverseLerp(100, 1500, d));
            Play("ShotFar", Mathf.Clamp(400 / d, 0.35f, 1), d / SpeedOfSound, Jitter(0.03f), far: true, cutoffHz: cutoff);
        }

        /// <summary>Your bullet landing `distanceM` away, heard once the sound has travelled back.</summary>
        public static void Landed(float distanceM, float flightS, Surface surface, uint seed)
        {
            if (surface == Surface.None) return;
            float d = Mathf.Max(distanceM, 1);
            float cutoff = Mathf.Lerp(7000, 1800, Mathf.InverseLerp(100, 1200, d));
            // A hit's thwack is what you listen for downrange; keep it audible.
            float level = Mathf.Clamp(150 / d, surface == Surface.Flesh ? 0.3f : 0.12f, 0.7f);
            Impact(surface, level, flightS + d / SpeedOfSound, seed, true, cutoff);
        }

        static void Impact(Surface surface, float level, float delay, uint seed, bool far, float cutoff)
        {
            string name = surface switch
            {
                Surface.Flesh => "Flesh",
                Surface.Water => "Water",
                Surface.Concrete => "Concrete",
                Surface.Metal => "Metal",
                Surface.Wood => "Wood",
                Surface.Glass => "Glass",
                Surface.Tin => "Tin",
                Surface.Clay => "Clay",
                Surface.Pop => "Pop",
                _ => "Dirt",
            };
            Play(name, level, delay, Jitter(0.06f), far, cutoff);
            // Hard surfaces sometimes send the round whining off; the match seed keeps it fair to replay.
            float chance = surface switch { Surface.Metal => 0.7f, Surface.Tin => 0.3f, Surface.Concrete => 0.45f, Surface.Dirt => 0.12f, _ => 0 };
            float roll = ((seed * 2654435761u) >> 8) / 16777216f;
            if (roll < chance) Play("Ricochet", level * 0.8f, delay + 0.015f, 0.85f + 0.3f * roll / Mathf.Max(chance, 0.01f), far, cutoff);
        }

        /// <summary>What a shot struck, for its impact sound.</summary>
        public static Surface SurfaceOf(ShotOutcome o, DuelMap map)
        {
            var i = o.Impact;
            if (i.Kind == ImpactKind.Target || o.Verdict != Verdict.Miss) return Surface.Flesh;
            if (i.Kind == ImpactKind.None) return Surface.None;
            if (map.Water.HasValue && i.Point.Y <= map.Water.Value + 0.3) return Surface.Water;
            if (i.Kind == ImpactKind.Cover && i.CoverId != null)
            {
                var box = map.Cover.Find(b => b.Id == i.CoverId);
                switch (box?.Look ?? CoverLook.Rock)
                {
                    case CoverLook.Metal: return Surface.Metal;
                    case CoverLook.Wood: return Surface.Wood;
                    default: return Surface.Concrete;
                }
            }
            // Ground: roofs on the harbor, steel decks on the rigs, earth elsewhere.
            return map.Id switch
            {
                "harbor-rooftops" => Surface.Concrete,
                "oil-rigs" => Surface.Metal,
                _ => Surface.Dirt,
            };
        }

        // ---- handling ----------------------------------------------------------------------

        /// <summary>One click of an elevation or windage turret; a dull knock at the end stop.</summary>
        public static void Turret(bool elevation, bool moved) =>
            Play("Turret", moved ? 0.55f : 0.3f, 0, (elevation ? 0.92f : 1.06f) * (moved ? 1 : 0.7f) * Jitter(0.02f), duck: false);

        /// <summary>A detent of the magnification ring (rate-limited for drags across the zoom bar).</summary>
        public static void Zoom()
        {
            var s = instance;
            if (s == null || Time.unscaledTime - s.lastZoom < 0.035f) return;
            s.lastZoom = Time.unscaledTime;
            Play("Zoom", 0.4f, 0, Jitter(0.05f), duck: false);
        }

        /// <summary>The rifle coming up to the eye, or down from it.</summary>
        public static void Raise(bool up) => Play("Raise", up ? 0.4f : 0.3f, 0, up ? Jitter(0.03f) : 1.12f, duck: false);

        /// <summary>Breath drawn and held, or let go (a gasp if it was held too long).</summary>
        public static void Breath(bool hold, float strain)
        {
            if (hold) Play("Inhale", 0.45f, 0, Jitter(0.03f), duck: false);
            else Play("Exhale", 0.3f + 0.5f * strain, 0, 0.96f + 0.12f * strain, duck: false);
        }

        /// <summary>
        /// Heartbeat while the breath is held or recovering, faster and louder with the strain
        /// (0 steady, 1 at its worst). Call every frame it should beat; it stops when calls stop.
        /// </summary>
        public static void Heart(float strain)
        {
            var s = instance;
            if (s == null) return;
            if (Time.unscaledTime - s.heartSeen > 0.3f) s.nextBeat = Time.unscaledTime + 0.45f;
            s.heartSeen = Time.unscaledTime;
            s.heartStrain = Mathf.Clamp01(strain);
        }

        // ---- kill cam ----------------------------------------------------------------------

        /// <summary>The bullet cam starts: the world ducks and the round's whoosh comes up.</summary>
        public static void KillCamBegin()
        {
            var s = instance;
            if (s == null) return;
            s.killcam = true;
            s.killSlow = false;
            s.killHit = false;
            s.killPaused = false;
            s.Loop(s.whoosh, "KillWhoosh", 0.9f);
            s.Loop(s.drone, "KillDrone", 1);
        }

        void Loop(AudioSource src, string name, float pitch)
        {
            var clip = Clip(name);
            if (clip == null || !Enabled) return;
            try
            {
                src.clip = clip;
                src.pitch = pitch;
                src.volume = 0;
                src.timeSamples = UnityEngine.Random.Range(0, Math.Max(1, clip.samples - 1));
                src.Play();
            }
            catch (Exception e) { Warn(e); }
        }

        /// <summary>The last stretch in deep slow motion (the X-ray): the whoosh drops, the drone swells.</summary>
        public static void KillCamSlow(bool slow)
        {
            if (instance != null) instance.killSlow = slow;
        }

        public static void KillCamPaused(bool paused)
        {
            var s = instance;
            if (s == null || s.killPaused == paused || !s.killcam) return;
            s.killPaused = paused;
            try
            {
                foreach (var src in new[] { s.whoosh, s.drone })
                    if (paused) src.Pause();
                    else src.UnPause();
            }
            catch (Exception e) { s.Warn(e); }
        }

        /// <summary>The round arrives: on a hit the thud and crunch of the X-ray, slowed; else the strike.</summary>
        public static void KillCamImpact(bool hit)
        {
            var s = instance;
            if (s == null) return;
            s.killHit = true;
            if (hit)
            {
                Play("KillImpact", 1, 0, 0.9f, duck: false);
                Play("Flesh", 0.6f, 0, 0.7f, duck: false);
            }
            else Play("Dirt", 0.8f, 0, 0.75f, duck: false);
        }

        public static void KillCamEnd()
        {
            if (instance != null) instance.killcam = false;
        }

        // ---- menus -------------------------------------------------------------------------

        /// <summary>Taps whose action makes its own sound (the rifle and scope controls).</summary>
        static readonly HashSet<string> OwnSound = new HashSet<string> { "scope", "exit" };
        static readonly HashSet<string> Confirms = new HashSet<string> { "deploy", "find", "quickplay", "rematch", "equip", "save", "continue", "bot" };
        static readonly HashSet<string> Backs = new HashSet<string> { "back", "cancel" };

        /// <summary>The sound for a tap on a UI action: a rising chime to commit, a falling one to go back, else a click.</summary>
        public static void UiTap(string action)
        {
            if (!Clicks || OwnSound.Contains(action)) return;
            Play(Confirms.Contains(action) ? "UiConfirm" : Backs.Contains(action) ? "UiBack" : "UiClick", 0.3f, duck: false);
        }

        public static void UiClick()
        {
            if (Clicks) Play("UiClick", 0.3f, duck: false);
        }

        // ---- frame -------------------------------------------------------------------------

        void Update()
        {
            Receive();
            if (drone == null) return;
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            duck = Mathf.MoveTowards(duck, killcam ? 0.3f : 1, dt * 2);
            foreach (var v in voices) v.Src.volume = LevelOf(v);
            foreach (var v in far) v.Src.volume = LevelOf(v);

            // Beds: quieter behind the menus, ducked under the kill cam; the wind follows the lane.
            float amb = Enabled ? AmbienceVolume * (AmbienceOn ? 1 : 0.4f) * Mathf.Lerp(0.25f, 1, (duck - 0.3f) / 0.7f) : 0;
            windShown = Mathf.Lerp(windShown, Wind, 1 - Mathf.Exp(-dt * 0.8f));
            Fade(windBed, amb * 0.6f * Mathf.Clamp01(0.15f + windShown / 9), dt);
            windBed.pitch = Mathf.Clamp(0.86f + windShown * 0.025f, 0.85f, 1.2f);
            Fade(lifeBed, amb * 0.8f, dt);
            // An interruption or an audio route change (headphones out) stops every source; restart the loops.
            if (now - lastBedCheck > 1)
            {
                lastBedCheck = now;
                Restart(windBed);
                Restart(lifeBed);
            }

            // Kill cam: the whoosh rushes until impact, slower and darker in the slow-motion close;
            // the drone comes in under the X-ray and lingers through the hold.
            float fx = Enabled ? Volume : 0;
            if (!killPaused)
            {
                Fade(whoosh, killcam && !killHit ? fx * (killSlow ? 0.8f : 0.55f) : 0, dt * (killHit ? 6 : 1.5f));
                whoosh.pitch = Mathf.MoveTowards(whoosh.pitch, killSlow ? 0.5f : 0.9f, dt * 1.5f);
                Fade(drone, killcam && (killSlow || killHit) ? fx * 0.6f : 0, dt * (killcam ? 1 : 0.6f));
                if (!killcam) { StopSilent(whoosh); StopSilent(drone); }
            }

            // Heart: beats while someone keeps calling Heart().
            if (now - heartSeen < 0.3f && now >= nextBeat)
            {
                Play("Heart", 0.25f + 0.55f * heartStrain, 0, 1 + 0.06f * heartStrain, duck: false);
                nextBeat = now + 60 / (68 + 52 * heartStrain);
            }
        }

        void Restart(AudioSource bed)
        {
            if (bed.clip == null || bed.isPlaying) return;
            try { bed.Play(); }
            catch (Exception e) { Warn(e); }
        }

        static void StopSilent(AudioSource src)
        {
            if (src.isPlaying && src.volume <= 0.001f) src.Stop();
        }

        static void Fade(AudioSource src, float target, float dt) =>
            src.volume = Mathf.MoveTowards(src.volume, target, dt * 0.5f);
    }
}
