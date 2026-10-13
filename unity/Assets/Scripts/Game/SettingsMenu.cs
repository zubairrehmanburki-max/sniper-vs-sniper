using System;
using System.Collections.Generic;
using SniperDuel.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game
{
    /// <summary>
    /// Drives the Settings screen: the tab rail, one panel per tab, the right-hand info column and
    /// every control in the panels (segmented buttons, toggles, sliders, pickers), each reading and
    /// writing Settings. Controls are found by naming convention in Settings.html:
    /// segments "id-0".."id-n", toggles "id" + "id-knob", sliders "id-dec"/"id-inc"/"id-track"/
    /// "id-fill"/"id-knob"/"id-value", pickers "id-prev"/"id-next" around a bound "id".
    /// </summary>
    public sealed class SettingsMenu
    {
        static readonly string[] Tabs = { "graphics", "controls", "sensitivity", "hud", "audio", "account" };

        static readonly Color Amber = Rgb(0xF2, 0xB3, 0x3D);
        static readonly Color AmberTint = new Color(Amber.r, Amber.g, Amber.b, 0.18f);
        static readonly Color TabTint = new Color(Amber.r, Amber.g, Amber.b, 0.14f);
        static readonly Color Muted = Rgb(0xA7, 0xAD, 0x9F);
        static readonly Color Paper = Rgb(0xED, 0xEF, 0xE8);
        static readonly Color Ink = Rgb(0x14, 0x12, 0x0C);
        static readonly Color Line = new Color(Paper.r, Paper.g, Paper.b, 0.2f);
        static readonly Color TrackOff = new Color(Paper.r, Paper.g, Paper.b, 0.22f);

        const string Credits = "Skies, ground and building textures: Poly Haven (CC0). Soldier: Mixamo, via the three.js examples. Every sound is synthesised in the game.";

        readonly UiScreen s;
        readonly List<Action> renders = new List<Action>();
        int tab;
        bool resetArmed;
        string? note;

        public SettingsMenu(UiScreen screen, Action back)
        {
            s = screen;
            s.On("back", back);
            s.On("reset", () =>
            {
                Settings.ResetTab(tab);
                Changed();
                note = "Defaults restored for this tab.";
                RenderInfo();
            });
            for (int i = 0; i < Tabs.Length; i++)
            {
                int k = i;
                s.On("tab-" + Tabs[i], () => { tab = k; Changed(); });
            }

            // Graphics.
            Segments("gfx-quality", 4, () => Settings.Quality, v => Settings.Quality = v, filled: true);
            Segments("gfx-fps", 3, () => Settings.FrameRate, v => Settings.FrameRate = v, filled: false);
            Slider("gfx-scale", 0.5f, 1f, 0.05f, () => Settings.RenderScale, v => Settings.RenderScale = v, Percent);
            Picker("gfx-shadows", Settings.ShadowNames, () => Settings.Shadows, v => Settings.Shadows = v, wrap: false);
            Toggle("gfx-reflections", () => Settings.WaterReflections, v => Settings.WaterReflections = v);
            Toggle("gfx-aa", () => Settings.AntiAliasing, v => Settings.AntiAliasing = v);
            Toggle("gfx-saver", () => Settings.BatterySaver, v => Settings.BatterySaver = v);

            // Controls.
            Segments("ctl-fire", 2, () => Settings.FireHold ? 1 : 0, v => Settings.FireHold = v == 1, filled: true);
            Toggle("ctl-unscope", () => Settings.UnscopeAfterShot, v => Settings.UnscopeAfterShot = v);
            Toggle("ctl-invertScope", () => Settings.InvertScopeDrag, v => Settings.InvertScopeDrag = v);
            Toggle("ctl-invertY", () => Settings.InvertLookY, v => Settings.InvertLookY = v);
            Toggle("ctl-haptics", () => Settings.HapticsOn, v =>
            {
                Settings.HapticsOn = v;
                Haptics.Click();
            });

            // Sensitivity.
            Slider("sens-look", 0.2f, 2f, 0.1f, () => Settings.LookSensitivity, v => Settings.LookSensitivity = v, Times);
            Slider("sens-scope", 0.2f, 2f, 0.1f, () => Settings.ScopeSensitivity, v => Settings.ScopeSensitivity = v, Times);
            Slider("sens-vertical", 0.5f, 1.5f, 0.05f, () => Settings.VerticalSensitivity, v => Settings.VerticalSensitivity = v, Percent);
            Segments("sens-scaling", 2, () => Settings.ScopeScaling, v => Settings.ScopeScaling = v, filled: true);

            // HUD.
            Toggle("hud-wind", () => Settings.WindArrows, v => Settings.WindArrows = v);
            Toggle("hud-card", () => Settings.RangeCard, v => Settings.RangeCard = v);
            Toggle("hud-marker", () => Settings.RangeMarker, v => Settings.RangeMarker = v);
            Toggle("hud-crosshair", () => Settings.Crosshair, v => Settings.Crosshair = v);
            Slider("hud-opacity", 0.4f, 1f, 0.1f, () => Settings.HudOpacity, v => Settings.HudOpacity = v, Percent);
            Picker("hud-scale", Array.ConvertAll(Settings.HudScales, p => p + "%"), () => Settings.HudScale, v => Settings.HudScale = v, wrap: false);

            // Audio.
            Toggle("aud-on", () => Settings.SoundOn, v => Settings.SoundOn = v);
            Slider("aud-master", 0f, 1f, 0.1f, () => Settings.MasterVolume, v => Settings.MasterVolume = v, Percent);
            Slider("aud-effects", 0f, 1f, 0.1f, () => Settings.EffectsVolume, v => Settings.EffectsVolume = v, Percent);
            Slider("aud-ambience", 0f, 1f, 0.1f, () => Settings.AmbienceVolume, v => Settings.AmbienceVolume = v, Percent);
            Toggle("aud-clicks", () => Settings.MenuClicks, v => Settings.MenuClicks = v);

            // Account.
            Picker("acct-callsign", Settings.Callsigns, () => Settings.CallsignIndex, v => Settings.CallsignIndex = v, wrap: true);
            s.Text("acct-version", "v" + Application.version);
            s.Text("acct-profile", "GUEST · THIS DEVICE");
            s.On("acct-credits", () => { Changed(); note = Credits; RenderInfo(); });
            s.On("acct-reset", () =>
            {
                if (!resetArmed)
                {
                    Changed();
                    resetArmed = true;
                    s.Text("acct-reset", "TAP TO CONFIRM");
                    note = "Tap again to clear everything saved on this device: callsign and all settings.";
                    RenderInfo();
                    return;
                }
                Settings.ResetAll();
                Changed();
                note = "Everything saved on this device was cleared.";
                RenderInfo();
            });

            Render();
        }

        /// <summary>Called when the screen opens: back to the first tab, nothing pending.</summary>
        public void Refresh()
        {
            tab = 0;
            Changed();
        }

        // ---- controls ------------------------------------------------------

        void Segments(string id, int count, Func<int> get, Action<int> set, bool filled)
        {
            for (int i = 0; i < count; i++)
            {
                int k = i;
                s.On($"{id}-{i}", () => { set(k); Changed(); });
            }
            renders.Add(() =>
            {
                int v = get();
                for (int i = 0; i < count; i++)
                {
                    if (!s.Has($"{id}-{i}")) continue;
                    var e = s[$"{id}-{i}"];
                    if (i != v) Paint(e, Color.clear, Line, Muted);
                    else if (filled) Paint(e, Amber, Amber, Ink);
                    else Paint(e, AmberTint, Amber, Amber);
                }
            });
        }

        void Toggle(string id, Func<bool> get, Action<bool> set)
        {
            s.On(id, () => { set(!get()); Changed(); });
            renders.Add(() =>
            {
                if (!s.Has(id) || !s.Has(id + "-knob")) return;
                bool on = get();
                s[id].style.backgroundColor = on ? Amber : TrackOff;
                var knob = s[id + "-knob"];
                knob.style.backgroundColor = on ? Ink : Paper;
                if (on) { knob.style.left = StyleKeyword.Auto; knob.style.right = 2; }
                else { knob.style.right = StyleKeyword.Auto; knob.style.left = 2; }
            });
        }

        void Slider(string id, float min, float max, float step, Func<float> get, Action<float> set, Func<float, string> format)
        {
            void To(float v)
            {
                v = Mathf.Clamp(min + Mathf.Round((v - min) / step) * step, min, max);
                v = (float)Math.Round(v, 3);
                if (Mathf.Approximately(v, get())) return;
                set(v);
                Changed();
            }
            s.On(id + "-dec", () => To(get() - step));
            s.On(id + "-inc", () => To(get() + step));
            if (s.Has(id + "-track"))
            {
                // Tap or drag anywhere on the track.
                var track = s[id + "-track"];
                track.pickingMode = PickingMode.Position;
                track.AddToClassList(Layout.ActionClass);
                foreach (var c in track.Query<VisualElement>().ToList())
                    if (c != track) c.pickingMode = PickingMode.Ignore;
                bool dragging = false;
                void From(Vector3 world)
                {
                    float f = Mathf.Clamp01(track.WorldToLocal(world).x / Mathf.Max(1, track.layout.width));
                    To(min + f * (max - min));
                }
                track.RegisterCallback<PointerDownEvent>(e => { dragging = true; track.CapturePointer(e.pointerId); From(e.position); });
                track.RegisterCallback<PointerMoveEvent>(e => { if (dragging) From(e.position); });
                track.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (!dragging) return;
                    dragging = false;
                    track.ReleasePointer(e.pointerId);
                    Sound.UiClick();
                });
            }
            renders.Add(() =>
            {
                float v = get();
                float f = Mathf.Clamp01((v - min) / (max - min));
                if (s.Has(id + "-fill")) s[id + "-fill"].style.width = Length.Percent(f * 100);
                if (s.Has(id + "-knob")) s[id + "-knob"].style.left = Length.Percent(f * 100);
                s.Text(id + "-value", format(v));
            });
        }

        void Picker(string id, string[] names, Func<int> get, Action<int> set, bool wrap)
        {
            void Step(int d)
            {
                int n = names.Length, v = get() + d;
                v = wrap ? (v % n + n) % n : Mathf.Clamp(v, 0, n - 1);
                if (v == get()) return;
                set(v);
                Changed();
            }
            s.On(id + "-prev", () => Step(-1));
            s.On(id + "-next", () => Step(1));
            renders.Add(() => s.Text(id, names[Mathf.Clamp(get(), 0, names.Length - 1)]));
        }

        static void Paint(VisualElement e, Color background, Color border, Color text)
        {
            e.style.backgroundColor = background;
            e.style.borderTopColor = border; e.style.borderRightColor = border;
            e.style.borderBottomColor = border; e.style.borderLeftColor = border;
            e.style.color = text;
            foreach (var l in e.Query<Label>().ToList()) l.style.color = text;
        }

        // ---- render --------------------------------------------------------

        /// <summary>Any control touched: drop pending confirmations and notes, redraw.</summary>
        void Changed()
        {
            if (resetArmed)
            {
                resetArmed = false;
                s.Text("acct-reset", "RESET");
            }
            note = null;
            Render();
        }

        void Render()
        {
            for (int i = 0; i < Tabs.Length; i++)
            {
                bool on = i == tab;
                s.Show("panel-" + Tabs[i], on);
                if (!s.Has("tab-" + Tabs[i])) continue;
                var e = s["tab-" + Tabs[i]];
                e.style.backgroundColor = on ? TabTint : Color.clear;
                e.style.borderLeftWidth = on ? 3 : 0;
                e.style.borderLeftColor = Amber;
                e.style.paddingLeft = on ? 12 : 15;
                foreach (var l in e.Query<Label>().ToList()) l.style.color = on ? Amber : Muted;
            }
            foreach (var r in renders) r();
            RenderInfo();
        }

        void RenderInfo()
        {
            s.Show("preview", tab == 0);
            s.Text("preview-label", "PREVIEW · " + Settings.QualityNames[Settings.Quality]);
            (string, string)[] rows;
            string help;
            switch (tab)
            {
                case 0:
                    rows = new[] { ("Device", SystemInfo.deviceModel), ("Recommended", Recommended()), ("Est. battery", $"~{BatteryHours():0.#} h") };
                    help = "Water reflections render the enemy bridge in the water. Turning them off costs you a visual cue, not just quality.";
                    break;
                case 1:
                    rows = new[] { ("Fire", Settings.FireHold ? "Hold" : "Tap"), ("Scope drag", Settings.InvertScopeDrag ? "Inverted" : "Normal"), ("Haptics", Settings.HapticsOn ? "On" : "Off") };
                    help = "Hold mode fires only after a short press, so a stray tap can't waste your shot or give away your position.";
                    break;
                case 2:
                    rows = new[] { ("Look", Times(Settings.LookSensitivity)), ("Scope", Times(Settings.ScopeSensitivity)), ("Scaling", Settings.ScopeScaling == 0 ? "By zoom" : "Soft") };
                    help = "By zoom keeps a drag covering the same part of the view at every magnification. Soft moves faster at high zoom.";
                    break;
                case 3:
                    int shown = (Settings.WindArrows ? 1 : 0) + (Settings.RangeCard ? 1 : 0) + (Settings.RangeMarker ? 1 : 0) + (Settings.Crosshair ? 1 : 0);
                    rows = new[] { ("Elements", $"{shown} / 4 shown"), ("Opacity", Percent(Settings.HudOpacity)), ("Scale", Settings.HudScales[Settings.HudScale] + "%") };
                    help = "Without wind arrows you read the wind from the flags and the water instead, as a real spotter would.";
                    break;
                case 4:
                    rows = new[] { ("Output", Settings.SoundOn ? "On" : "Muted"), ("Master", Percent(Settings.MasterVolume)), ("Effects", Percent(Settings.EffectsVolume)) };
                    help = "The crack of a near miss arrives before the rifle's report. Keep effects up to hear where the enemy is shooting from.";
                    break;
                default:
                    rows = new[] { ("Callsign", Settings.Callsign), ("Status", "Guest · offline"), ("Version", "v" + Application.version) };
                    help = "Your callsign and settings are saved on this device until online accounts arrive.";
                    break;
            }
            for (int i = 0; i < 3; i++)
            {
                s.Text("info-k" + i, rows[i].Item1);
                s.Text("info-v" + i, rows[i].Item2);
            }
            s.Text("help", note ?? help);
        }

        static string Recommended()
        {
            int mem = SystemInfo.systemMemorySize;
            string q = mem >= 6000 ? "High" : mem >= 4000 ? "Medium" : "Low";
            int fps = Screen.currentResolution.refreshRateRatio.value >= 100 && mem >= 6000 ? 120 : 60;
            return $"{q} · {fps} fps";
        }

        /// <summary>A rough guide, relative to about three hours at High, 60 fps, full resolution.</summary>
        static float BatteryHours()
        {
            float scale = Settings.RenderScale;
            float load = (0.6f + 0.2f * Settings.Quality) * Mathf.Pow(Settings.FrameRates[Settings.FrameRate] / 60f, 0.7f) * (0.5f + 0.5f * scale * scale);
            return Mathf.Clamp(3.2f / load, 1, 8);
        }

        static string Percent(float v) => $"{Mathf.RoundToInt(v * 100)}%";
        static string Times(float v) => $"{v:0.0}×";

        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);
    }
}
