using System;
using System.Collections;
using System.IO;
using SniperDuel.Game.Ui;
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

        UiHost ui = null!;

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.045f);

            ui = UiHost.Create();
        }

        IEnumerator Start()
        {
            string? capture = Arg("-capture-all");
            if (capture != null)
            {
                yield return CaptureAll(capture);
                yield break;
            }
            ui.Show("Title");
        }

        IEnumerator CaptureAll(string dir)
        {
            Directory.CreateDirectory(dir);
            int n = 0;
            foreach (var name in AllScreens)
            {
                try { ui.Show(name); }
                catch (Exception e) { Debug.LogException(e); continue; }
                for (int f = 0; f < 6; f++) yield return null;
                string path = Path.Combine(dir, $"{++n:00}-{name}.png");
                ScreenCapture.CaptureScreenshot(path);
                yield return null;
                yield return new WaitForEndOfFrame();
                Debug.Log($"[Capture] {path}");
            }
            for (int f = 0; f < 10; f++) yield return null;
            Application.Quit(0);
        }
    }
}
