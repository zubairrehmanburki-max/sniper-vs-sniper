using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// The mockups' colours and the few looks the menus switch between at runtime: a selected or
    /// idle card, an underlined header tab, a toggle track.
    /// </summary>
    public static class Skin
    {
        public static readonly Color Amber = Rgb(0xF2, 0xB3, 0x3D);
        public static readonly Color AmberTint = new Color(Amber.r, Amber.g, Amber.b, 0.14f);
        public static readonly Color Muted = Rgb(0xA7, 0xAD, 0x9F);
        public static readonly Color Paper = Rgb(0xED, 0xEF, 0xE8);
        public static readonly Color Ink = Rgb(0x14, 0x12, 0x0C);
        public static readonly Color Red = Rgb(0xE5, 0x48, 0x3B);
        public static readonly Color Blue = Rgb(0x9F, 0xC4, 0xFF);
        public static readonly Color Locked = Rgb(0x8A, 0x8F, 0x84);
        public static readonly Color Line = new Color(Paper.r, Paper.g, Paper.b, 0.16f);
        public static readonly Color Panel = new Color(10 / 255f, 12 / 255f, 11 / 255f, 0.6f);
        static readonly Color TrackOff = new Color(Paper.r, Paper.g, Paper.b, 0.2f);

        public static void Border(VisualElement e, Color c, float? width = null)
        {
            e.style.borderTopColor = c; e.style.borderRightColor = c;
            e.style.borderBottomColor = c; e.style.borderLeftColor = c;
            if (width is float w)
            {
                e.style.borderTopWidth = w; e.style.borderRightWidth = w;
                e.style.borderBottomWidth = w; e.style.borderLeftWidth = w;
            }
        }

        /// <summary>A pickable card: amber outline and tint when selected, a faint line otherwise.</summary>
        public static void Card(VisualElement e, bool on, Color? idle = null)
        {
            Border(e, on ? Amber : Line);
            e.style.backgroundColor = on ? AmberTint : idle ?? Panel;
        }

        /// <summary>A small segmented button: filled amber when on.</summary>
        public static void Segment(VisualElement e, bool on)
        {
            Border(e, on ? Amber : new Color(Paper.r, Paper.g, Paper.b, 0.2f));
            e.style.backgroundColor = on ? Amber : Color.clear;
            Text(e, on ? Ink : Muted);
        }

        /// <summary>A header tab: amber underline and paper text when on.</summary>
        public static void Tab(VisualElement e, bool on)
        {
            e.style.borderBottomWidth = 2;
            e.style.borderBottomColor = on ? Amber : Color.clear;
            Text(e, on ? Paper : Muted);
        }

        /// <summary>A switch drawn as a track with a knob ("id" and "id-knob").</summary>
        public static void Toggle(UiScreen s, string id, bool on)
        {
            if (!s.Has(id) || !s.Has(id + "-knob")) return;
            s[id].style.backgroundColor = on ? Amber : TrackOff;
            var knob = s[id + "-knob"];
            knob.style.backgroundColor = on ? Ink : Paper;
            if (on) { knob.style.left = StyleKeyword.Auto; knob.style.right = 2; }
            else { knob.style.right = StyleKeyword.Auto; knob.style.left = 2; }
        }

        /// <summary>Colours every text run inside an element.</summary>
        public static void Text(VisualElement e, Color c)
        {
            e.style.color = c;
            foreach (var l in e.Query<Label>().ToList()) l.style.color = c;
        }

        public static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);
    }
}
