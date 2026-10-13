using System.Collections.Generic;
using UnityEngine;

namespace SniperDuel.Game.Ui
{
    /// <summary>The mockups' typefaces (Barlow, Barlow Condensed, Share Tech Mono), from the downloaded set.</summary>
    public static class Fonts
    {
        static readonly Dictionary<string, Font?> Cache = new Dictionary<string, Font?>();

        public static Font? Get(string family, int weight, bool italic)
        {
            string file;
            if (family == "Mono") file = "ShareTechMono-Regular";
            else if (family == "Condensed")
                file = italic ? "BarlowCondensed-ExtraBoldItalic"
                    : weight >= 800 ? "BarlowCondensed-ExtraBold"
                    : weight >= 700 ? "BarlowCondensed-Bold"
                    : weight >= 600 ? "BarlowCondensed-SemiBold"
                    : "BarlowCondensed-Medium";
            else file = weight >= 600 ? "Barlow-SemiBold" : weight >= 500 ? "Barlow-Medium" : "Barlow-Regular";
            if (Cache.TryGetValue(file, out var f)) return f;
            f = Resources.Load<Font>("TP/Fonts/" + file);
            if (f == null) Debug.LogWarning($"[Fonts] missing {file}");
            Cache[file] = f;
            return f;
        }
    }
}
