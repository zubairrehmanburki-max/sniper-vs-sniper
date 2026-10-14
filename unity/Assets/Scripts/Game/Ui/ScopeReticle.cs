using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// The scope's reticle: a first-focal-plane MRAD tree drawn to the picture's true angular scale
    /// (read from the camera's field of view), so a mil on the glass is a mil on the target at every
    /// magnification. Fine crosshair with 1 mil hashes, half-mil and 0.2 mil subtensions when the
    /// magnification gives them room, numbered every 2 mil, heavy posts outside 10 mil, a
    /// windage-dot tree below the centre for holds, and range holdover marks set beside the tree
    /// from the rifle's dope relative to what is dialled. The illuminated centre dot is red.
    /// </summary>
    public static class ScopeReticle
    {
        static IReadOnlyList<(double range, double mrad)> holds = Array.Empty<(double, double)>();
        static int holdsVersion;

        /// <summary>Elevation hold for each range, mrad up from the rifle's zero: (range m, mrad).</summary>
        public static IReadOnlyList<(double range, double mrad)> Holds
        {
            get => holds;
            set { holds = value; holdsVersion++; }
        }

        /// <summary>Elevation dialled on the turret, mrad (the holds sit relative to it).</summary>
        public static double DialMrad;

        static readonly Color Ink = new Color(0.035f, 0.035f, 0.03f, 0.94f);
        static readonly Color Lit = new Color(0.95f, 0.22f, 0.16f);
        const float Extent = 10;

        public static void Attach(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            if (e.parent != null) e.parent.style.overflow = Overflow.Visible;
            var labels = new List<Label>();
            string shown = "";
            // The labels can't be placed while drawing, so they follow the zoom here.
            e.schedule.Execute(() =>
            {
                if (!ScopeMask.Frame(e, out var c, out float r, out float h)) return;
                float m = PxPerMrad(h);
                string key = FormattableString.Invariant($"{m:0.00}|{c.x:0.0},{c.y:0.0}|{r:0.0}|{DialMrad:0.00}|{holdsVersion}");
                if (key == shown) return;
                shown = key;
                Relabel(e, labels, c, r, m);
                e.MarkDirtyRepaint();
            }).Every(16);
            e.generateVisualContent += ctx =>
            {
                if (!ScopeMask.Frame(e, out var c, out float r, out float h)) return;
                Draw(ctx.painter2D, c, r, PxPerMrad(h));
            };
        }

        /// <summary>UI pixels per milliradian at the centre of the picture.</summary>
        static float PxPerMrad(float panelHeight)
        {
            var cam = Camera.main;
            float fov = cam != null ? cam.fieldOfView : 2.5f;
            return panelHeight * 0.5f / Mathf.Tan(Mathf.Clamp(fov, 0.1f, 170) * 0.5f * Mathf.Deg2Rad) / 1000f;
        }

        /// <summary>Inner reticle reach in mil: 10, or less when the glass shows less than that.</summary>
        static float Reach(float r, float m) => Mathf.Max(1, Mathf.Min(Extent, Mathf.Floor(r * 0.8f / m)));

        /// <summary>Windage dots each side on the tree row `d` mil down.</summary>
        static int TreeHalf(float d) => Mathf.Min(Mathf.FloorToInt(d * 0.5f) + 1, 5);

        /// <summary>Tree spacing in mil: 1, or 2 when the magnification is too low for every dot.</summary>
        static int TreeStep(float m) => m >= 7 ? 1 : 2;

        static void Draw(Painter2D p, Vector2 c, float r, float m)
        {
            float reach = Reach(r, m);
            float inner = reach * m;
            float major = Mathf.Clamp(0.34f * m, 3.5f, 8);
            float gap = Mathf.Max(0.2f * m, 2.5f);
            p.lineCap = LineCap.Butt;

            // Heavy posts outside the fine reticle, tapering to a point toward the centre.
            p.fillColor = Ink;
            foreach (var dir in new[] { Vector2.left, Vector2.right, Vector2.down, Vector2.up })
            {
                // Bottom post clears the tree.
                float start = inner + (dir == Vector2.up ? 1.2f * m : 0.6f * m) + 2;
                float end = r * 1.08f;
                if (end <= start) continue;
                var n = new Vector2(-dir.y, dir.x);
                p.BeginPath();
                p.MoveTo(c + dir * start + n * 0.6f);
                p.LineTo(c + dir * (start + 14) + n * 2.6f);
                p.LineTo(c + dir * end + n * 2.8f);
                p.LineTo(c + dir * end - n * 2.8f);
                p.LineTo(c + dir * (start + 14) - n * 2.6f);
                p.LineTo(c + dir * start - n * 0.6f);
                p.ClosePath();
                p.Fill();
            }

            // Fine crosshair with an open centre.
            p.strokeColor = Ink;
            p.lineWidth = 1.15f;
            p.BeginPath();
            Seg(p, c + new Vector2(-inner, 0), c + new Vector2(-gap, 0));
            Seg(p, c + new Vector2(gap, 0), c + new Vector2(inner, 0));
            Seg(p, c + new Vector2(0, -inner), c + new Vector2(0, -gap));
            Seg(p, c + new Vector2(0, gap), c + new Vector2(0, inner));
            // Whole-mil hashes, longer at 5 and 10.
            for (int i = 1; i <= reach; i++)
            {
                float len = i % 5 == 0 ? major * 1.5f : major;
                float o = i * m;
                Seg(p, c + new Vector2(o, -len), c + new Vector2(o, len));
                Seg(p, c + new Vector2(-o, -len), c + new Vector2(-o, len));
                Seg(p, c + new Vector2(-len, -o), c + new Vector2(len, -o));
                Seg(p, c + new Vector2(-len, o), c + new Vector2(len, o));
            }
            p.Stroke();

            // Half-mil hashes when they have room, then 0.2 mil subtensions near the centre.
            if (m * 0.5f >= 5)
            {
                p.lineWidth = 1;
                p.BeginPath();
                float len = major * 0.55f;
                for (float i = 0.5f; i < reach; i += 1)
                {
                    float o = i * m;
                    Seg(p, c + new Vector2(o, -len), c + new Vector2(o, len));
                    Seg(p, c + new Vector2(-o, -len), c + new Vector2(-o, len));
                    Seg(p, c + new Vector2(-len, -o), c + new Vector2(len, -o));
                }
                p.Stroke();
            }
            if (m * 0.2f >= 4)
            {
                p.lineWidth = 0.8f;
                p.BeginPath();
                float len = major * 0.32f;
                for (int i = 1; i < 10; i++)
                {
                    if (i == 5) continue;
                    float o = i * 0.2f * m;
                    Seg(p, c + new Vector2(o, -len), c + new Vector2(o, 0));
                    Seg(p, c + new Vector2(-o, -len), c + new Vector2(-o, 0));
                    Seg(p, c + new Vector2(0, -o), c + new Vector2(len, -o));
                }
                p.Stroke();
            }

            // Windage tree: rows of dots each mil (or two) down, widening as the drop grows.
            int step = TreeStep(m);
            float dot = Mathf.Clamp(m * 0.09f, 0.9f, 1.5f);
            p.fillColor = Ink;
            p.BeginPath();
            for (int d = step; d < reach; d += step)
                for (int k = step; k <= TreeHalf(d); k += step)
                {
                    Dot(p, c + new Vector2(k * m, d * m), dot);
                    Dot(p, c + new Vector2(-k * m, d * m), dot);
                }
            p.Fill();

            // Range holdovers: a bar left of the tree at each range's hold below what's dialled.
            p.lineWidth = 1.6f;
            p.BeginPath();
            foreach (var (_, y, x) in HoldMarks(r, m))
                Seg(p, c + new Vector2(x, y), c + new Vector2(x + 9, y));
            p.Stroke();

            // Illuminated centre.
            p.fillColor = Lit;
            p.BeginPath();
            Dot(p, c, 1.7f);
            p.Fill();
        }

        /// <summary>Holdover marks to show: (label, y below centre px, left end x px), spaced out when crowded.</summary>
        static List<(string text, float y, float x)> HoldMarks(float r, float m)
        {
            var marks = new List<(string, float, float)>();
            float reach = Reach(r, m);
            float last = float.NegativeInfinity;
            foreach (var (range, mrad) in holds)
            {
                float d = (float)(mrad - DialMrad);
                if (d < 0.6f || d > reach - 0.3f) continue;
                float y = d * m;
                if (y - last < 11) continue;
                last = y;
                float half = Mathf.Max(1, TreeHalf(Mathf.Floor(d / TreeStep(m)) * TreeStep(m)));
                marks.Add((range.ToString("0"), y, -(half + 0.7f) * m - 9));
            }
            return marks;
        }

        static void Relabel(VisualElement e, List<Label> labels, Vector2 c, float r, float m)
        {
            var want = new List<(string text, Vector2 at, TextAnchor align, Color color)>();
            float reach = Reach(r, m);
            float major = Mathf.Clamp(0.34f * m, 3.5f, 8);
            int every = 2 * m >= 14 ? 2 : 5;
            for (int i = every; i <= reach; i += every)
            {
                string t = i.ToString();
                // Along the horizontal, above the hash; up the top stem, to its right.
                want.Add((t, c + new Vector2(i * m, -major - 6), TextAnchor.MiddleCenter, Ink));
                want.Add((t, c + new Vector2(-i * m, -major - 6), TextAnchor.MiddleCenter, Ink));
                want.Add((t, c + new Vector2(major + 6, -i * m), TextAnchor.MiddleLeft, Ink));
            }
            foreach (var (text, y, x) in HoldMarks(r, m))
                want.Add((text, c + new Vector2(x - 3, y), TextAnchor.MiddleRight, Ink));

            var font = Fonts.Get("Mono", 400, false);
            while (labels.Count < want.Count)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.style.position = Position.Absolute;
                l.style.width = 40;
                l.style.height = 12;
                l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
                l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
                l.style.fontSize = 9;
                if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
                e.Add(l);
                labels.Add(l);
            }
            for (int i = 0; i < labels.Count; i++)
            {
                var l = labels[i];
                if (i >= want.Count)
                {
                    l.style.display = DisplayStyle.None;
                    continue;
                }
                var (text, at, align, color) = want[i];
                l.style.display = DisplayStyle.Flex;
                l.text = text;
                l.style.color = color;
                l.style.unityTextAlign = align;
                l.style.top = at.y - 6;
                l.style.left = align == TextAnchor.MiddleRight ? at.x - 40 : align == TextAnchor.MiddleLeft ? at.x : at.x - 20;
            }
        }

        static void Seg(Painter2D p, Vector2 a, Vector2 b)
        {
            p.MoveTo(a);
            p.LineTo(b);
        }

        static void Dot(Painter2D p, Vector2 at, float radius)
        {
            p.MoveTo(at + new Vector2(radius, 0));
            p.Arc(at, radius, 0, 360);
            p.ClosePath();
        }
    }
}
