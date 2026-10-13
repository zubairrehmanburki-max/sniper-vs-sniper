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
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Config = GameConfig.Parse(Resources.Load<TextAsset>("Config/game").text);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.045f);
            rig = CameraRig.Create(cam);
            PostFx.Setup(cam, transform);

            ui = UiHost.Create();
            ui.Created += s =>
            {
                if (s.Name == "Scope" && s.Has("mask")) ScopeMask.Attach(s["mask"], new Vector2(466, 215), 200);
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
            rig.Eye = Library.ToUnity(Combat.EyeOf(a, Stance.Prone, Config));
            var enemy = arena.PlaceEnemy(b, Stance.Prone, Side.B);
            // Default view: straight at the enemy's head.
            var to = Library.ToUnity(Combat.EyeOf(b, Stance.Prone, Config)) - rig.Eye;
            rig.Yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            rig.Pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            foreach (var f in arena.Flags) f.Wind = new Vector3(4.5f, 0, 1.2f);
        }

        void Frame(string screen)
        {
            bool world = Array.IndexOf(InWorld, screen) >= 0;
            rig.Current = world ? CameraRig.Mode.Eye : CameraRig.Mode.Menu;
            rig.Zoom = screen == "Scope" ? 12 : 1;
        }

        IEnumerator CaptureAll(string dir)
        {
            Directory.CreateDirectory(dir);
            int n = 0;
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
            foreach (var (label, screen) in new[] { ("World-Menu", "Title"), ("World-Eye", "Hud"), ("World-Scope", "Scope") })
            {
                Frame(screen);
                for (int f = 0; f < 6; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{++n:00}-{label}.png"));
                yield return null;
                yield return new WaitForEndOfFrame();
            }
            for (int f = 0; f < 10; f++) yield return null;
            Application.Quit(0);
        }
    }
}
