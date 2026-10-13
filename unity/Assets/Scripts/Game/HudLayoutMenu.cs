using System;
using SniperDuel.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game
{
    /// <summary>
    /// Drives the custom HUD editor: three layouts, tap an element to select it, size and opacity
    /// sliders, mirror, the hold-to-fire switch (the same option as in Settings), reset and save.
    /// Edits are pending until SAVE; closing discards them. Saved layouts live in PlayerPrefs and
    /// the active one is applied to the duel HUD (fire button, wind panel, range card).
    /// </summary>
    public sealed class HudLayoutMenu
    {
        static readonly string[] Ids = { "card", "compass", "score", "wind", "ammo", "fire", "scope", "breath", "stance" };
        static readonly string[] Names = { "RANGE CARD", "COMPASS", "SCORE + TIMER", "WIND", "AMMO + TOOLS", "FIRE BUTTON", "SCOPE BUTTON", "BREATH", "STANCE" };
        const int Layouts = 3, Fire = 5;
        const int SizeMin = 60, SizeMax = 150, SizeStep = 10, OpacityMin = 20, OpacityMax = 100, OpacityStep = 5;

        readonly UiScreen s;
        readonly (StyleLength left, StyleLength right)[] origin = new (StyleLength, StyleLength)[Ids.Length];
        readonly Color[] borders = new Color[Ids.Length];
        readonly int[] size = new int[Ids.Length], opacity = new int[Ids.Length];
        bool mirror, dirty;
        int layout, selected = Fire;
        string status = "";

        public HudLayoutMenu(UiScreen screen, Action back)
        {
            s = screen;
            s.On("back", back);
            for (int i = 0; i < Ids.Length; i++)
            {
                int k = i;
                s.On("el-" + Ids[i], () => { selected = k; status = ""; Render(); });
                if (!s.Has("el-" + Ids[i])) continue;
                var e = s["el-" + Ids[i]];
                origin[i] = (e.style.left, e.style.right);
                borders[i] = e.style.borderTopColor.value;
            }
            for (int i = 0; i < Layouts; i++)
            {
                int k = i;
                s.On("layout" + i, () => { Load(k); status = dirty ? "Unsaved changes discarded." : ""; dirty = false; Render(); });
            }
            Slider("size", SizeMin, SizeMax, SizeStep, () => size[selected], v => size[selected] = v);
            Slider("opacity", OpacityMin, OpacityMax, OpacityStep, () => opacity[selected], v => opacity[selected] = v);
            s.On("release", () => { Settings.FireHold = !Settings.FireHold; status = Settings.FireHold ? "Fire needs a short hold." : "Fire on tap."; Render(); });
            s.On("mirror", () => { mirror = !mirror; Edited(); });
            s.On("reset", () =>
            {
                for (int i = 0; i < Ids.Length; i++) { size[i] = 100; opacity[i] = 100; }
                mirror = false;
                Edited();
                status = "Defaults restored. SAVE to keep them.";
                Render();
            });
            s.On("save", () =>
            {
                Store(layout);
                PlayerPrefs.SetInt("hudl.active", layout);
                PlayerPrefs.Save();
                dirty = false;
                status = $"Saved. Layout {layout + 1} is used in duels.";
                Render();
            });
        }

        /// <summary>Called when the editor opens: the active layout, as saved.</summary>
        public void Refresh()
        {
            Load(Active);
            dirty = false;
            status = "";
            Render();
        }

        static int Active => Mathf.Clamp(PlayerPrefs.GetInt("hudl.active", 0), 0, Layouts - 1);
        static string Key(int l, int el, string what) => $"hudl{l}.{Ids[el]}.{what}";

        void Load(int l)
        {
            layout = l;
            for (int i = 0; i < Ids.Length; i++)
            {
                size[i] = Mathf.Clamp(PlayerPrefs.GetInt(Key(l, i, "size"), 100), SizeMin, SizeMax);
                opacity[i] = Mathf.Clamp(PlayerPrefs.GetInt(Key(l, i, "opacity"), 100), OpacityMin, OpacityMax);
            }
            mirror = PlayerPrefs.GetInt($"hudl{l}.mirror", 0) != 0;
        }

        void Store(int l)
        {
            for (int i = 0; i < Ids.Length; i++)
            {
                PlayerPrefs.SetInt(Key(l, i, "size"), size[i]);
                PlayerPrefs.SetInt(Key(l, i, "opacity"), opacity[i]);
            }
            PlayerPrefs.SetInt($"hudl{l}.mirror", mirror ? 1 : 0);
        }

        void Edited()
        {
            dirty = true;
            status = "Unsaved changes.";
            Render();
        }

        void Slider(string id, int min, int max, int step, Func<int> get, Action<int> set)
        {
            void To(float v)
            {
                int n = Mathf.Clamp(min + Mathf.RoundToInt((v - min) / step) * step, min, max);
                if (n == get()) return;
                set(n);
                Edited();
            }
            if (!s.Has(id + "-track")) return;
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

        void Render()
        {
            for (int i = 0; i < Layouts; i++)
            {
                if (!s.Has("layout" + i)) continue;
                var e = s["layout" + i];
                bool on = i == layout;
                Skin.Border(e, on ? Skin.Amber : new Color(Skin.Paper.r, Skin.Paper.g, Skin.Paper.b, 0.2f));
                e.style.backgroundColor = on ? new Color(Skin.Amber.r, Skin.Amber.g, Skin.Amber.b, 0.18f) : Color.clear;
                Skin.Text(e, on ? Skin.Amber : Skin.Muted);
            }
            for (int i = 0; i < Ids.Length; i++)
            {
                if (!s.Has("el-" + Ids[i])) continue;
                var e = s["el-" + Ids[i]];
                bool on = i == selected;
                Skin.Border(e, on ? Skin.Amber : borders[i], on ? 2 : i == Fire ? 2 : 1);
                e.style.scale = new Scale(Vector2.one * size[i] / 100f);
                e.style.opacity = opacity[i] / 100f;
                var (l, r) = origin[i];
                e.style.left = mirror ? r : l;
                e.style.right = mirror ? l : r;
            }
            s.Text("selected", Names[selected]);
            s.Text("status", status.Length > 0 ? status : "Tap an element to edit it.");
            Value("size", size[selected], SizeMin, SizeMax);
            Value("opacity", opacity[selected], OpacityMin, OpacityMax);
            Skin.Toggle(s, "release", Settings.FireHold);
            s.Text("mirror", mirror ? "UNMIRROR" : "MIRROR");
            s.Text("save", dirty ? "SAVE" : "SAVED");
        }

        void Value(string id, int v, int min, int max)
        {
            float f = (v - min) / (float)(max - min);
            s.Fill(id + "-fill", f);
            if (s.Has(id + "-knob")) s[id + "-knob"].style.left = Length.Percent(f * 100);
            s.Text(id + "-value", v + "%");
        }

        /// <summary>Applies the active saved layout to the duel HUD where it has matching elements.</summary>
        public static void ApplyTo(UiScreen hud)
        {
            int l = Active;
            void Set(VisualElement? e, int el)
            {
                if (e == null) return;
                float sz = Mathf.Clamp(PlayerPrefs.GetInt(Key(l, el, "size"), 100), SizeMin, SizeMax) / 100f;
                e.style.scale = new Scale(new Vector2(sz, sz));
                e.style.opacity = Mathf.Clamp(PlayerPrefs.GetInt(Key(l, el, "opacity"), 100), OpacityMin, OpacityMax) / 100f;
            }
            if (hud.Has("fireBtn")) Set(hud["fireBtn"], Fire);
            if (hud.Has("windArrow0")) Set(hud["windArrow0"].parent?.parent?.parent, 3);
            if (hud.Has("cardTarget")) Set(hud["cardTarget"].parent?.parent, 0);
        }
    }
}
