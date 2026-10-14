using System;
using System.Collections;
using System.IO;
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
            "Title", "Lobby", "Play", "Loadout", "Operators", "Matchmaking", "Versus", "Hud", "Scope",
            "Killcam", "Results", "Settings", "Gunsmith", "HudLayout", "Career", "Pass",
        };

        public static string? Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
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
                if (s.Name != "Scope") return;
                if (s.Has("reticle")) ScopeReticle.Attach(s["reticle"]);
                if (s.Has("mask")) ScopeMask.Attach(s["mask"]);
            };
            try { BuildArena("twin-bridges"); }
            catch (Exception e) { Debug.LogException(e); }
        }

        IEnumerator Start()
        {
            string? capture = Arg("-capture-all");
            if (capture != null)
            {
                yield return CaptureAll(capture);
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
            ScopeMask.Open = 1;
            // The scope shadow a little off centre, as with the eye a touch low and left.
            ScopeMask.EyeOffset = scope ? new Vector2(-9, 6) : Vector2.zero;
            if (scope)
            {
                var s = ui.Get("Scope");
                s.Text("zoom", screen == "Scope25" ? $"{Config.Scope.MaxZoom:0}" : "12");
                s.Text("mag", screen == "Scope25" ? $"{Config.Scope.MaxZoom:0.0}×" : "12.0×");
            }
        }

        IEnumerator CaptureAll(string dir)
        {
            Directory.CreateDirectory(dir);
            int n = 0;
            if (Config.Rifles.Count > 0 && arena != null)
                ScopeReticle.Holds = Duel.Holdovers(Config, Config.Rifles[0].Id, RifleSetup.AtmosphereFor(0, 15, 0.5));
            foreach (var name in AllScreens)
            {
                try { Frame(name); ui.Show(name); }
                catch (Exception e) { Debug.LogException(e); continue; }
                for (int f = 0; f < 6; f++) yield return null;
                string path = Path.Combine(dir, $"{++n:00}-{name}.png");
                ScreenCapture.CaptureScreenshot(path);
                yield return null;
                yield return new WaitForEndOfFrame();
                Debug.Log($"[Capture] {path}");
            }
            // Plain world views for review: the menu backdrop, the 1× eye and a 12× look at the enemy.
            ui.HideAll();
            XRay? xray = null;
            foreach (var (label, screen) in new[] { ("World-Menu", "Title"), ("World-Eye", "Hud"), ("World-Scope", "Scope"), ("World-Enemy", ""), ("World-XRay", "") })
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
                    if (label == "World-XRay")
                    {
                        // The kill cam's X-ray on a chest hit, seen from beside the bullet's path.
                        var shotDir = (e - rig.Eye).normalized;
                        Vector3 chest = b.center;
                        if (arena.Enemy.Bones.TryGetValue("Spine2", out var s2)) chest = s2.position;
                        xray = XRay.Begin(arena.Enemy, chest, shotDir);
                        rig.Cam.transform.position = chest - shotDir * 1.1f + Vector3.up * 0.5f + Vector3.Cross(Vector3.up, shotDir) * 0.6f;
                        rig.Cam.transform.LookAt(chest);
                        rig.Cam.fieldOfView = 50;
                    }
                }
                for (int f = 0; f < 6; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{++n:00}-{label}.png"));
                yield return null;
                yield return new WaitForEndOfFrame();
            }
            // First person: the rifle shouldered, then nearly at the eye as it comes up to the
            // scope, and the scope wound out to full magnification.
            if (xray != null) xray.End();
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
            for (int f = 0; f < 10; f++) yield return null;
            Application.Quit(0);
        }
    }
}
