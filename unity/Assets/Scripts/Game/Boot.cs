using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SniperDuel.Ballistics;
using SniperDuel.Game.Ui;
using SniperDuel.Game.World;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>
    /// The only object in the scene. Builds the game from code: camera, world, UI and flow.
    /// With -capture-all DIR it walks every screen, saves a PNG of each and quits (CI screenshots).
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        public static readonly string[] AllScreens =
        {
            "Title", "Lobby", "Play", "Loadout", "Operators", "Versus", "Hud", "Scope",
            "Killcam", "Results", "Settings", "Gunsmith", "HudLayout", "Career", "Pass",
        };

        public static string? Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>Screens that sit over the first-person view rather than the menu backdrop.</summary>
        static readonly string[] InWorld = { "Hud", "Scope", "Killcam" };

        public static GameConfig Config = null!;
        UiHost ui = null!;
        CameraRig rig = null!;
        Arena? arena;

        void Awake()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Config = GameConfig.Parse(Resources.Load<TextAsset>("Config/game").text);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            Sound.Init();
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.045f);
            rig = CameraRig.Create(cam);
            PostFx.Setup(cam, transform);
            // Frame rate, quality, render scale, shadows, audio levels and haptics from the saved settings.
            Settings.Apply();

            ui = UiHost.Create();
            ui.Created += s =>
            {
                if (s.Name == "Hud" && s.Has("dials")) ScopeDials.Attach(s["dials"]);
                if (s.Name != "Scope") return;
                if (s.Has("reticle")) ScopeReticle.Attach(s["reticle"]);
                if (s.Has("mask")) ScopeMask.Attach(s["mask"]);
                if (s.Has("heart")) ScopeGauges.AttachHeart(s["heart"]);
                if (s.Has("fireRing")) ScopeGauges.AttachBreathRing(s["fireRing"]);
            };
            try { BuildArena("twin-bridges"); }
            catch (Exception e) { Debug.LogException(e); }
        }

        IEnumerator Start()
        {
            string? capture = Arg("-capture-all");
            if (capture != null)
            {
                // The menus filled from a fresh profile, as a new install sees them.
                yield return CaptureAll(capture, Flow.Create(Config, ui, rig, arena));
                yield break;
            }
            Flow.Create(Config, ui, rig, arena);
            Frame("Title");
            ui.Show("Title");
        }

        void BuildArena(string mapId)
        {
            var map = Maps.ById(mapId);
            arena = Arena.Build(map);
            var a = map.Spots.A[0];
            var b = map.Spots.B[0];
            rig.MenuFocus = Library.ToUnity(a);
            var enemy = arena.PlaceEnemy(b, Stance.Prone, Side.B);
            rig.Body = arena.PlacePlayer(a, Stance.Prone, Side.A).gameObject;
            AimAtEnemy();
            foreach (var f in arena.Flags) f.Wind = new Vector3(4.5f, 0, 1.2f);
        }

        /// <summary>Default view: from the player's eye straight at the enemy's head.</summary>
        void AimAtEnemy()
        {
            if (arena == null) return;
            var map = arena.Map;
            rig.Eye = Library.ToUnity(Combat.EyeOf(map.Spots.A[0], Stance.Prone, Config));
            var to = Library.ToUnity(Combat.EyeOf(map.Spots.B[0], Stance.Prone, Config)) - rig.Eye;
            rig.Yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            rig.Pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Sets the camera for a screen. Besides the screens: "Aim" (first person, rifle shouldered),
        /// "Raise" (the rifle nearly at the eye, scoping in) and "Scope25" (the scope at 25x).
        /// </summary>
        void Frame(string screen)
        {
            bool world = Array.IndexOf(InWorld, screen) >= 0 || screen == "Aim" || screen == "Raise" || screen == "Scope25";
            bool scope = screen == "Scope" || screen == "Scope25";
            rig.Current = world ? CameraRig.Mode.Eye : CameraRig.Mode.Menu;
            rig.ThirdPerson = !scope && screen != "Aim" && screen != "Raise";
            rig.Raise = screen == "Aim" ? 0 : screen == "Raise" ? 0.8f : 1;
            float eye = (float)Config.Scope.EyeFovDeg;
            rig.Fov = scope ? (float)(Config.Scope.FovAt1xDeg / (screen == "Scope25" ? Config.Scope.MaxZoom : 12))
                : screen == "Raise" ? Mathf.Lerp(eye, eye * 0.8f, Mathf.SmoothStep(0, 1, 0.8f)) : eye;
            PostFx.Scope(scope ? 1 : 0);
            Settings.ScopeShadows(scope ? 1100 : 0);
            ScopeMask.Open = 1;
            // The scope shadow a little off centre, as with the eye a touch low and left.
            ScopeMask.EyeOffset = scope ? new Vector2(-9, 6) : Vector2.zero;
            // The knobs as the mockup has them: 12×, up 3.1, right 0.5, focus 600 m; the breath part
            // held and the pulse steady.
            ScopeDials.Zoom = screen == "Scope25" ? (int)Config.Scope.MaxZoom : 12;
            ScopeDials.Elevation = 3.1;
            ScopeDials.Windage = 0.5;
            ScopeDials.Focus = 3.6;
            ScopeGauges.Bpm = 72;
            ScopeGauges.Breath = 0.7f;
            ScopeGauges.Holding = true;
            if (scope)
            {
                var s = ui.Get("Scope");
                s.Text("lrf", "641 m");
                s.Show("solution", false);
            }
        }

        IEnumerator CaptureAll(string dir, Flow flow)
        {
            Directory.CreateDirectory(dir);
            int n = 0;
            foreach (var name in AllScreens)
            {
                try { Frame(name); flow.Preview(name); ui.Show(name); }
                catch (Exception e) { Debug.LogException(e); continue; }
                for (int f = 0; f < 6; f++) yield return null;
                string path = Path.Combine(dir, $"{++n:00}-{name}.png");
                ScreenCapture.CaptureScreenshot(path);
                yield return null;
                yield return new WaitForEndOfFrame();
                Debug.Log($"[Capture] {path}");
            }
            // Plain world views for review: the menu backdrop, the 1× eye, a 12× look at the enemy, then
            // the kill cam: the X-ray just after the round meets bone, and the round in flight.
            ui.HideAll();
            BulletCam? killcam = null;
            foreach (var (label, screen) in new[] { ("World-Menu", "Title"), ("World-Eye", "Hud"), ("World-Scope", "Scope"), ("World-Enemy", ""), ("World-XRay", ""), ("World-Bullet", "") })
            {
                Frame(screen);
                if (screen == "" && arena?.Enemy != null)
                {
                    // Close look at the enemy figure: posing, scale and lighting.
                    var e = arena.Enemy.transform.position;
                    rig.Current = CameraRig.Mode.Director;
                    rig.Cam.fieldOfView = 40;
                    rig.Cam.transform.position = e + new Vector3(2.6f, 1.4f, -3.2f);
                    rig.Cam.transform.LookAt(e + new Vector3(0, 0.3f, 0.3f));
                    var b = Library.BoundsOf(arena.Enemy.gameObject);
                    Debug.Log($"[Capture] enemy at {e} bounds {b.center} size {b.size}");
                    if (label != "World-Enemy")
                    {
                        // The kill cam on a chest shot from the player's eye.
                        if (killcam != null) killcam.Finish();
                        Vector3 chest = b.center;
                        if (arena.Enemy.Bones.TryGetValue("Spine2", out var s2)) chest = s2.position;
                        killcam = BulletCam.Rehearse(rig.Eye, chest, rig, arena.Root, arena.Enemy, HitZone.Chest);
                        if (label == "World-XRay") killcam.SeekImpact(0.22f);
                        else killcam.Seek(0.55f);
                    }
                }
                for (int f = 0; f < 6; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{++n:00}-{label}.png"));
                yield return null;
                yield return new WaitForEndOfFrame();
            }
            if (killcam != null) killcam.Finish();
            // First person: the rifle shouldered, then nearly at the eye as it comes up to the
            // scope, and the scope wound out to full magnification.
            foreach (var (label, screen) in new[] { ("World-Aim", "Aim"), ("World-Raise", "Raise"), ("Scope-25x", "Scope25") })
            {
                Frame(screen);
                AimAtEnemy();
                if (screen == "Scope25") ui.Show("Scope");
                else ui.HideAll();
                for (int f = 0; f < 8; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{++n:00}-{label}.png"));
                yield return null;
                yield return new WaitForEndOfFrame();
                Debug.Log($"[Capture] {label}");
            }
            ui.HideAll();
            Frame("");
            if (arena != null) yield return CaptureShots(dir, () => ++n);
            // Twin Bridges in detail for review: the whole estuary from the air, the river front from
            // the deck, along the deck to a pylon, a pylon from the water, and the soldier's head.
            if (arena != null && arena.Map.Id == "twin-bridges")
            {
                var head = arena.Enemy != null && arena.Enemy.Bones.TryGetValue("Head", out var hb) ? hb.position : new Vector3(0, 24.3f, 638);
                foreach (var (label, from, to, fov) in new[]
                {
                    ("TB-Wide", new Vector3(-520, 170, -420), new Vector3(0, 0, 420), 52f),
                    ("TB-Bank", new Vector3(-40, 33, -14), new Vector3(300, 26, 170), 50f),
                    ("TB-Deck", new Vector3(-38, 25.7f, 3.5f), new Vector3(-200, 30, -1), 58f),
                    ("TB-Pylon", new Vector3(-150, 4, -70), new Vector3(-90, 32, 0), 55f),
                    ("TB-Head", head + new Vector3(0.75f, 0.35f, -0.9f), head, 30f),
                })
                {
                    Frame("");
                    rig.Current = CameraRig.Mode.Director;
                    rig.Cam.fieldOfView = fov;
                    rig.Cam.transform.position = from;
                    rig.Cam.transform.LookAt(to);
                    for (int f = 0; f < 6; f++) yield return null;
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{++n:00}-{label}.png"));
                    yield return null;
                    yield return new WaitForEndOfFrame();
                }
            }
            // The player's soldier moving on the deck: standing, walking, crouched, crouch-walking,
            // prone and crawling, from a little ahead and to the side; then the walk from the
            // game's over-the-shoulder camera with the HUD's stick and stance buttons.
            if (arena != null)
            {
                var spot = arena.Map.Spots.A[0];
                var fig = arena.PlacePlayer(spot, Stance.Standing, Side.A, live: true);
                rig.Body = fig.gameObject;
                var at = fig.transform.position;
                foreach (var (label, stance, pace) in new[]
                {
                    ("Move-Stand", Stance.Standing, 0f), ("Move-Walk", Stance.Standing, 2.2f), ("Move-Crouch", Stance.Crouch, 0f),
                    ("Move-CrouchWalk", Stance.Crouch, 1.15f), ("Move-Prone", Stance.Prone, 0f), ("Move-Crawl", Stance.Prone, 0.45f),
                })
                {
                    Frame("");
                    fig.Drive(stance, pace, Vector3.forward);
                    rig.Current = CameraRig.Mode.Director;
                    rig.Cam.fieldOfView = 40;
                    rig.Cam.transform.position = at + new Vector3(2.6f, 1.5f, 3.2f);
                    rig.Cam.transform.LookAt(at + new Vector3(0, stance == Stance.Prone ? 0.25f : 0.8f, 0.3f));
                    yield return new WaitForSeconds(0.9f);
                    for (int f = 0; f < 4; f++) yield return null;
                    Debug.Log($"[Capture] {label}: figure bounds {Library.BoundsOf(fig.gameObject).size}");
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{++n:00}-{label}.png"));
                    yield return null;
                    yield return new WaitForEndOfFrame();
                }
                Frame("Hud");
                fig.Drive(Stance.Standing, 2.2f, Vector3.forward);
                rig.Eye = Library.ToUnity(Combat.EyeOf(spot, Stance.Standing, Config));
                rig.Pitch = 0;
                ui.Show("Hud");
                yield return new WaitForSeconds(0.9f);
                for (int f = 0; f < 4; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{++n:00}-Move-Hud.png"));
                yield return null;
                yield return new WaitForEndOfFrame();
                ui.HideAll();
            }
            // The other maps: the menu backdrop and the 1x eye on each, to judge them against Twin Bridges.
            foreach (var id in Maps.Ids)
            {
                if (id == "twin-bridges") continue;
                if (arena != null) Destroy(arena.Root.gameObject);
                try { BuildArena(id); }
                catch (Exception e) { Debug.LogException(e); continue; }
                foreach (var (label, screen) in new[] { ("Menu", "Title"), ("Eye", "Hud") })
                {
                    Frame(screen);
                    for (int f = 0; f < 8; f++) yield return null;
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{++n:00}-Map-{id}-{label}.png"));
                    yield return null;
                    yield return new WaitForEndOfFrame();
                }
            }
            // Solo practice on the range lane (the last map built): the HUD from a live session,
            // then a plate through the scope at 12x after a hit.
            if (arena != null && arena.Map.Id == "training-lane") yield return CapturePractice(dir, () => ++n);
            for (int f = 0; f < 10; f++) yield return null;
            Application.Quit(0);
        }

        /// <summary>A shot aimed straight at a point (no dials), flown with the map's first-round conditions.</summary>
        ShotOutcome Aimed(Vec3 from, Stance stance, Vec3 at)
        {
            var map = arena!.Map;
            var eye = Combat.EyeOf(from, stance, Config);
            var state = Match.CreateMatch(0x5eed, map.Id, new PerSide<PlayerSetup>(new PlayerSetup("A", Profile.Rifle(Config)), new PlayerSetup("B", Profile.Rifle(Config))), 0, Config);
            double yaw = Math.Atan2(at.X - eye.X, -(at.Z - eye.Z));
            double pitch = Math.Atan2(at.Y - eye.Y, Math.Sqrt((at.X - eye.X) * (at.X - eye.X) + (at.Z - eye.Z) * (at.Z - eye.Z)));
            return Practice.ResolvePracticeShot(new ShotRequest
            {
                Map = map,
                Conditions = Match.ActiveConditions(state),
                RifleId = Profile.Rifle(Config),
                ShooterPosition = from,
                ShooterStance = stance,
                Input = new FireInput { Yaw = yaw, Pitch = pitch },
                Seed = 0x5eed,
                ShotIndex = 1,
            }, new List<Silhouette>(), Config);
        }

        static IEnumerator Shoot(string dir, Func<int> next, string label, int frames = 4)
        {
            for (int f = 0; f < frames; f++) yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{next():00}-{label}.png"));
            yield return null;
            yield return new WaitForEndOfFrame();
            Debug.Log($"[Capture] {label}");
        }

        /// <summary>
        /// The shots for review on the first map, at a fixed 30 frames a second of game time: your
        /// round's vapour trace a third of the way out and near the far bridge, through the scope
        /// at 12x and from the 1x eye; then the enemy firing, seen through the scope just after the
        /// trigger: flash and muzzle blast.
        /// </summary>
        IEnumerator CaptureShots(string dir, Func<int> next)
        {
            var map = arena!.Map;
            Time.captureDeltaTime = 1f / 30;
            var a = map.Spots.A[0];
            var b = map.Spots.B[0];
            var chestB = Combat.EyeOf(b, Stance.Prone, Config);
            var o = Aimed(a, Stance.Prone, new Vec3(chestB.X, chestB.Y + 1.2, chestB.Z));
            foreach (var (label, screen, at) in new[] { ("Shot-Trace", "Scope", 0.35f), ("Shot-Trace-Far", "Scope", 0.8f), ("Shot-Trace-Eye", "Aim", 0.25f) })
            {
                Frame(screen);
                AimAtEnemy();
                if (screen == "Scope") ui.Show("Scope");
                else ui.HideAll();
                var flight = World.Effects.Shot(o, true, arena);
                flight.Seek((float)o.Impact.T * at);
                yield return Shoot(dir, next, label);
                Destroy(flight.gameObject);
            }
            // The enemy fires back at us.
            var eyeA = Combat.EyeOf(a, Stance.Prone, Config);
            var back = Aimed(b, Stance.Prone, eyeA);
            Frame("Scope");
            AimAtEnemy();
            ui.Show("Scope");
            var enemyShot = World.Effects.Shot(back, false, arena);
            yield return Shoot(dir, next, "Shot-Enemy", 3);
            yield return Shoot(dir, next, "Shot-Enemy-Later", 12);
            Destroy(enemyShot.gameObject);
            ui.HideAll();
            Frame("");
            Time.captureDeltaTime = 0;
        }

        IEnumerator CapturePractice(string dir, Func<int> next)
        {
            Time.captureDeltaTime = 1f / 30;
            string rifle = Profile.Rifle(Config);
            var duel = Duel.Begin(Gear.ForMatch(Config, rifle), ui, rig, arena!, "YOU", rifle, BotDifficulty.Medium, 0x5eed, solo: true);
            yield return Shoot(dir, next, "Practice-Hud", 20);
            Destroy(duel.gameObject);
            yield return null;
            // Through the scope on the third plate of a fresh field, struck in the chest a moment ago.
            var map = arena!.Map;
            var spot = map.Spots.A[map.Spots.A.Count / 2];
            var eye = Combat.EyeOf(spot, Stance.Prone, Config);
            var plates = Plates.Create(arena.Root);
            plates.Lay(map, eye, 0x5eed, Config);
            var shapes = plates.Shapes;
            Debug.Log($"[Capture] practice plates: {string.Join(", ", shapes.Select(x => $"{x.Id} at {-x.Position.Z:0} m, x {x.Position.X:0}"))}");
            if (shapes.Count > 2)
            {
                var p = shapes[2];
                plates.Strike(p.Id, new Vec3(p.Position.X + 0.06, p.Position.Y + 0.5, p.Position.Z), 3000);
                Frame("Scope");
                rig.Eye = Library.ToUnity(eye);
                var to = Library.ToUnity(new Vec3(p.Position.X, p.Position.Y + 0.6, p.Position.Z)) - rig.Eye;
                rig.Yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                rig.Pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                ui.Show("Scope");
                yield return Shoot(dir, next, "Practice-Scope", 8);
            }
            Destroy(plates.gameObject);
            ui.HideAll();
            Time.captureDeltaTime = 0;
        }
    }
}
