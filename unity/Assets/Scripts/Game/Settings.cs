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
        public static readonly int[] HudScales = { 85, 90, 95, 100 };

        /// <summary>The keys behind each settings tab, for RESET TAB.</summary>
        public static readonly string[][] TabKeys =
        {
            new[] { "gfx.quality", "gfx.fps", "gfx.scale", "gfx.shadows", "gfx.reflections", "gfx.aa", "gfx.saver" },
            new[] { "ctl.fireHold", "ctl.unscope", "ctl.invertScope", "ctl.invertY", "ctl.haptics" },
            new[] { "sens.look", "sens.scope", "sens.vertical", "sens.scaling" },
            new[] { "hud.wind", "hud.card", "hud.marker", "hud.crosshair", "hud.opacity", "hud.scale" },
            new[] { "aud.on", "aud.master", "aud.effects", "aud.ambience", "aud.clicks" },
            new[] { "acct.name" },
        };

        // ---- graphics ------------------------------------------------------

        /// <summary>Index into QualityNames. The default suits the phone (<see cref="Tier"/>).</summary>
        public static int Quality { get => GetInt("gfx.quality", Tier, 0, 3); set => Set("gfx.quality", value); }
        /// <summary>Index into FrameRates.</summary>
        public static int FrameRate { get => GetInt("gfx.fps", 1, 0, 2); set => Set("gfx.fps", value); }
        /// <summary>
        /// URP render scale, 0.5–1. The world is drawn at this share of the screen's pixels and
        /// scaled up; the HUD and menus stay sharp. Phones' screens are dense enough that 0.75–0.9
        /// costs little detail and roughly halves the pixel work.
        /// </summary>
        public static float RenderScale { get => GetFloat("gfx.scale", Tier switch { 0 => 0.75f, 1 => 0.8f, _ => 0.9f }, 0.5f, 1f); set => Set("gfx.scale", value); }
        /// <summary>Index into ShadowNames.</summary>
        public static int Shadows { get => GetInt("gfx.shadows", Tier switch { 0 => 1, 1 => 2, _ => 3 }, 0, 3); set => Set("gfx.shadows", value); }
        public static bool WaterReflections { get => GetBool("gfx.reflections", true); set => Set("gfx.reflections", value); }
        public static bool AntiAliasing { get => GetBool("gfx.aa", true); set => Set("gfx.aa", value); }
        /// <summary>Caps menus at 30 fps (on unless turned off: the menus are a slow drift over the map).</summary>
        public static bool BatterySaver { get => GetBool("gfx.saver", true); set => Set("gfx.saver", value); }

        /// <summary>
        /// The phone's class, for the defaults of a fresh install: 0 for phones with 3 GB or less
        /// (iPhone XR, XS), 1 for 4 GB (iPhone 11, 12, 13), 2 for 6 GB and more (the Pros, 14 and
        /// later). Desktop and the CI capture count as 2. Index into QualityNames.
        /// </summary>
        public static int Tier
        {
            get
            {
                if (tier >= 0) return tier;
                int mb = SystemInfo.systemMemorySize;
                tier = !Application.isMobilePlatform ? 2 : mb < 3500 ? 0 : mb < 5500 ? 1 : 2;
                return tier;
            }
        }
        static int tier = -1;

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

        public const int CallsignMin = 3, CallsignMax = 14;

        /// <summary>The name you play under, typed in Settings › Account; "YOU" until you set one.</summary>
        public static string Callsign
        {
            get { var n = PlayerPrefs.GetString("acct.name", ""); return n.Length >= CallsignMin ? n : "YOU"; }
            set { PlayerPrefs.SetString("acct.name", value); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// A typed callsign as it will show: upper case, letters, digits, spaces, - and _ only, spaces
        /// squeezed. Null if what's left is shorter than CallsignMin.
        /// </summary>
        public static string? CleanCallsign(string typed)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in typed.Trim().ToUpperInvariant())
            {
                bool ok = (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == ' ';
                if (!ok || (ch == ' ' && (sb.Length == 0 || sb[sb.Length - 1] == ' '))) continue;
                if (sb.Length < CallsignMax) sb.Append(ch);
            }
            string n = sb.ToString().Trim();
            return n.Length >= CallsignMin ? n : null;
        }

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

            // One anti-aliasing pass, never two: multisampling on HIGH and ULTRA (edges of geometry,
            // cheap on the phone's tile memory), the SMAA post pass on LOW and MEDIUM instead.
            bool aa = AntiAliasing;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = RenderScale;
                urp.msaaSampleCount = !aa || q < 2 ? 1 : q == 2 ? 2 : 4;
                int s = Shadows;
                urp.shadowDistance = s switch { 0 => 0f, 1 => 80f, 2 => 150f, _ => q == 3 ? 300f : 220f };
                urp.shadowCascadeCount = s switch { 0 => 1, 1 => 1, 2 => 2, _ => q == 3 ? 4 : 2 };
                shadowReach = -1;
                // Ambient occlusion draws the scene a second time for its normals: HIGH and up only.
                Feature(urp, "SSAO", q >= 2);
            }
            else
            {
                QualitySettings.shadows = Shadows == 0 ? UnityEngine.ShadowQuality.Disable : UnityEngine.ShadowQuality.All;
                QualitySettings.shadowDistance = Shadows switch { 0 => 0f, 1 => 80f, 2 => 150f, _ => 220f };
            }
            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.antialiasing = aa && q < 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                    data.antialiasingQuality = AntialiasingQuality.Medium;
                }
            }
            World.PostFx.Quality(q);
            ApplyFrameRate();

            AudioListener.volume = MasterVolume;
            Sound.Enabled = SoundOn;
            Sound.Volume = EffectsVolume;
            Sound.AmbienceVolume = AmbienceVolume;
            Sound.Clicks = MenuClicks;
            Haptics.Enabled = HapticsOn;
        }

        /// <summary>Turns a renderer feature of the pipeline (by name, as the build made it) on or off.</summary>
        static void Feature(UniversalRenderPipelineAsset urp, string name, bool on)
        {
            var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(urp) as ScriptableRendererData[];
            if (list == null) return;
            foreach (var data in list)
            {
                if (data == null) continue;
                foreach (var f in data.rendererFeatures)
                    if (f != null && f.name == name && f.isActive != on) f.SetActive(on);
            }
        }

        static float shadowReach = -1;

        /// <summary>
        /// Through the scope the picture is the far bridge, well past the normal shadow distance:
        /// stretch the shadows out to `metres` while scoped (0 puts the setting back), so the target's
        /// deck, girder and railings keep their shadows. The scope's narrow view keeps them sharp.
        /// </summary>
        public static void ScopeShadows(float metres)
        {
            if (Mathf.Abs(metres - shadowReach) < 1) return;
            shadowReach = metres;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) || Shadows == 0) return;
            float normal = Shadows switch { 1 => 80f, 2 => 150f, _ => Quality == 3 ? 300f : 220f };
            urp.shadowDistance = Mathf.Max(normal, metres);
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
