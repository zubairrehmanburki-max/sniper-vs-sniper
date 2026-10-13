using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SniperDuel.Game
{
    /// <summary>
    /// The player's options, saved in PlayerPrefs. Each property reads its stored value (or the
    /// default) and writing one saves it and re-applies everything that lives outside the game code:
    /// quality level, frame rate, render scale, shadows, audio and haptics.
    /// </summary>
    public static class Settings
    {
        public static readonly string[] QualityNames = { "LOW", "MEDIUM", "HIGH", "ULTRA" };
        public static readonly int[] FrameRates = { 30, 60, 120 };
        public static readonly string[] ShadowNames = { "OFF", "LOW", "MEDIUM", "HIGH" };
        public static readonly string[] Callsigns = { "YOU", "HAWK", "SPECTRE", "NOMAD", "RAVEN", "ORCA", "LYNX", "TALON" };
        public static readonly int[] HudScales = { 85, 90, 95, 100 };

        /// <summary>The keys behind each settings tab, for RESET TAB.</summary>
        public static readonly string[][] TabKeys =
        {
            new[] { "gfx.quality", "gfx.fps", "gfx.scale", "gfx.shadows", "gfx.reflections", "gfx.aa", "gfx.saver" },
            new[] { "ctl.fireHold", "ctl.unscope", "ctl.invertScope", "ctl.invertY", "ctl.haptics" },
            new[] { "sens.look", "sens.scope", "sens.vertical", "sens.scaling" },
            new[] { "hud.wind", "hud.card", "hud.marker", "hud.crosshair", "hud.opacity", "hud.scale" },
            new[] { "aud.on", "aud.master", "aud.effects", "aud.ambience", "aud.clicks" },
            new[] { "acct.callsign" },
        };

        // ---- graphics ------------------------------------------------------

        /// <summary>Index into QualityNames.</summary>
        public static int Quality { get => GetInt("gfx.quality", 2, 0, 3); set => Set("gfx.quality", value); }
        /// <summary>Index into FrameRates.</summary>
        public static int FrameRate { get => GetInt("gfx.fps", 1, 0, 2); set => Set("gfx.fps", value); }
        /// <summary>URP render scale, 0.5–1.</summary>
        public static float RenderScale { get => GetFloat("gfx.scale", 1f, 0.5f, 1f); set => Set("gfx.scale", value); }
        /// <summary>Index into ShadowNames.</summary>
        public static int Shadows { get => GetInt("gfx.shadows", 3, 0, 3); set => Set("gfx.shadows", value); }
        public static bool WaterReflections { get => GetBool("gfx.reflections", true); set => Set("gfx.reflections", value); }
        public static bool AntiAliasing { get => GetBool("gfx.aa", true); set => Set("gfx.aa", value); }
        /// <summary>Caps menus at 30 fps.</summary>
        public static bool BatterySaver { get => GetBool("gfx.saver", false); set => Set("gfx.saver", value); }

        // ---- controls ------------------------------------------------------

        /// <summary>Fire needs a short press-and-hold rather than a tap.</summary>
        public static bool FireHold { get => GetBool("ctl.fireHold", false); set => Set("ctl.fireHold", value); }
        public static bool UnscopeAfterShot { get => GetBool("ctl.unscope", false); set => Set("ctl.unscope", value); }
        public static bool InvertScopeDrag { get => GetBool("ctl.invertScope", false); set => Set("ctl.invertScope", value); }
        public static bool InvertLookY { get => GetBool("ctl.invertY", false); set => Set("ctl.invertY", value); }
        public static bool HapticsOn { get => GetBool("ctl.haptics", true); set => Set("ctl.haptics", value); }

        // ---- sensitivity ---------------------------------------------------

        public static float LookSensitivity { get => GetFloat("sens.look", 1f, 0.2f, 2f); set => Set("sens.look", value); }
        public static float ScopeSensitivity { get => GetFloat("sens.scope", 1f, 0.2f, 2f); set => Set("sens.scope", value); }
        /// <summary>Vertical drag relative to horizontal, 0.5–1.5.</summary>
        public static float VerticalSensitivity { get => GetFloat("sens.vertical", 1f, 0.5f, 1.5f); set => Set("sens.vertical", value); }
        /// <summary>0: scope drag scales with zoom; 1: soft, only partly (faster at high zoom).</summary>
        public static int ScopeScaling { get => GetInt("sens.scaling", 0, 0, 1); set => Set("sens.scaling", value); }

        // ---- hud -----------------------------------------------------------

        public static bool WindArrows { get => GetBool("hud.wind", true); set => Set("hud.wind", value); }
        public static bool RangeCard { get => GetBool("hud.card", true); set => Set("hud.card", value); }
        public static bool RangeMarker { get => GetBool("hud.marker", true); set => Set("hud.marker", value); }
        public static bool Crosshair { get => GetBool("hud.crosshair", true); set => Set("hud.crosshair", value); }
        public static float HudOpacity { get => GetFloat("hud.opacity", 1f, 0.4f, 1f); set => Set("hud.opacity", value); }
        /// <summary>Index into HudScales.</summary>
        public static int HudScale { get => GetInt("hud.scale", 3, 0, 3); set => Set("hud.scale", value); }

        // ---- audio ---------------------------------------------------------

        public static bool SoundOn { get => GetBool("aud.on", true); set => Set("aud.on", value); }
        public static float MasterVolume { get => GetFloat("aud.master", 1f, 0f, 1f); set => Set("aud.master", value); }
        public static float EffectsVolume { get => GetFloat("aud.effects", 1f, 0f, 1f); set => Set("aud.effects", value); }
        public static float AmbienceVolume { get => GetFloat("aud.ambience", 1f, 0f, 1f); set => Set("aud.ambience", value); }
        public static bool MenuClicks { get => GetBool("aud.clicks", true); set => Set("aud.clicks", value); }

        // ---- account -------------------------------------------------------

        /// <summary>Index into Callsigns.</summary>
        public static int CallsignIndex { get => GetInt("acct.callsign", 0, 0, Callsigns.Length - 1); set => Set("acct.callsign", value); }
        public static string Callsign => Callsigns[CallsignIndex];

        // ---- apply ---------------------------------------------------------

        static bool inMenus = true;
        static int appliedQualityLevel = -1;

        /// <summary>Menus run at 30 fps with the battery saver on; the duel always gets the chosen rate.</summary>
        public static void SetInMenus(bool menus)
        {
            if (inMenus == menus) return;
            inMenus = menus;
            ApplyFrameRate();
        }

        /// <summary>Restores the defaults of one tab (index into TabKeys).</summary>
        public static void ResetTab(int tab)
        {
            foreach (var k in TabKeys[tab]) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
            Apply();
        }

        /// <summary>Clears everything saved on this device.</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            appliedQualityLevel = -1;
            Apply();
        }

        /// <summary>Pushes every setting into the engine. Cheap; called at start-up and on each change.</summary>
        public static void Apply()
        {
            int q = Quality;
            var names = QualitySettings.names;
            if (names.Length > 1)
            {
                // Spread the four presets over however many levels the project has.
                int level = Mathf.RoundToInt(q * (names.Length - 1) / 3f);
                if (level != appliedQualityLevel)
                {
                    QualitySettings.SetQualityLevel(level, true);
                    appliedQualityLevel = level;
                }
            }
            else
            {
                QualitySettings.lodBias = q switch { 0 => 0.6f, 1 => 1f, 2 => 1.5f, _ => 2f };
                QualitySettings.anisotropicFiltering = q >= 2 ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
            }
            // A quality level carries its own vsync and probe settings; ours win.
            QualitySettings.vSyncCount = 0;
            QualitySettings.realtimeReflectionProbes = WaterReflections;

            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = RenderScale;
                urp.msaaSampleCount = q switch { 0 => 1, 1 => 2, _ => 4 };
                int s = Shadows;
                urp.shadowDistance = s switch { 0 => 0f, 1 => 80f, 2 => 150f, _ => q == 3 ? 300f : 220f };
                urp.shadowCascadeCount = s switch { 0 => 1, 1 => 1, 2 => 2, _ => 4 };
            }
            else
            {
                QualitySettings.shadows = Shadows == 0 ? ShadowQuality.Disable : ShadowQuality.All;
                QualitySettings.shadowDistance = Shadows switch { 0 => 0f, 1 => 80f, 2 => 150f, _ => 220f };
            }
            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.antialiasing = AntiAliasing ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            }
            ApplyFrameRate();

            AudioListener.volume = MasterVolume;
            Sound.Enabled = SoundOn;
            Sound.Volume = EffectsVolume;
            Sound.AmbienceVolume = AmbienceVolume;
            Sound.Clicks = MenuClicks;
            Haptics.Enabled = HapticsOn;
        }

        static void ApplyFrameRate()
        {
            int fps = FrameRates[FrameRate];
            Application.targetFrameRate = inMenus && BatterySaver ? Mathf.Min(30, fps) : fps;
        }

        // ---- storage -------------------------------------------------------

        static int GetInt(string key, int def, int min, int max) => Mathf.Clamp(PlayerPrefs.GetInt(key, def), min, max);
        static float GetFloat(string key, float def, float min, float max) => Mathf.Clamp(PlayerPrefs.GetFloat(key, def), min, max);
        static bool GetBool(string key, bool def) => PlayerPrefs.GetInt(key, def ? 1 : 0) != 0;

        static void Set(string key, int v) { PlayerPrefs.SetInt(key, v); Changed(); }
        static void Set(string key, float v) { PlayerPrefs.SetFloat(key, v); Changed(); }
        static void Set(string key, bool v) { PlayerPrefs.SetInt(key, v ? 1 : 0); Changed(); }

        static void Changed()
        {
            PlayerPrefs.Save();
            Apply();
        }
    }
}
