using System;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace SniperDuel.Game
{
    /// <summary>
    /// A crash trail: the last step the game reached (with the memory in use then) and the last
    /// error, kept in a small file as it goes. A crash leaves the file behind, and the next launch
    /// shows it on the title screen so the player can report where it stopped.
    /// </summary>
    public static class Trail
    {
        static string FilePath => Path.Combine(Application.persistentDataPath, "trail.txt");
        static string step = "", error = "";

        /// <summary>What the last run left behind, when it stopped somewhere other than the menus.</summary>
        public static string? Previous { get; private set; }

        public static void Begin()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var text = File.ReadAllText(FilePath).Trim();
                    if (text.Length > 0 && !text.StartsWith("menu")) Previous = text;
                }
            }
            catch (Exception) { }
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
                if (message.StartsWith("ALSA") || message.StartsWith("FMOD")) return;
                string where = stack.Split('\n')[0].Trim();
                error = $"{message} @ {where}";
                if (error.Length > 240) error = error.Substring(0, 240);
                Write();
            };
            Mark("menu");
        }

        /// <summary>Back in the menus: nothing to report from here.</summary>
        public static void Menu()
        {
            error = "";
            Mark("menu");
        }

        /// <summary>Records the step the game is at (cheap enough for a few marks a second, not every frame).</summary>
        public static void Mark(string what)
        {
            step = $"{what} · {Profiler.GetTotalReservedMemoryLong() / (1024 * 1024)} MB";
            Write();
        }

        static void Write()
        {
            try { File.WriteAllText(FilePath, error.Length > 0 ? $"{step}\nlast error: {error}" : step); }
            catch (Exception) { }
        }
    }
}
