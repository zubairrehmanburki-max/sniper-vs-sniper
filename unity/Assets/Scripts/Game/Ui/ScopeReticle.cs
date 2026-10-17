using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// The scope's reticle, modelled on a real first-focal-plane mil hash reticle (as on long-range
    /// tactical scopes): a thin black crosshair the full width of the glass, heavy tapered posts
    /// outside the 10 mil mark, 1 mil hashes with half-mil marks between them and 0.2 mil
    /// subtensions near the centre, numbered every 2 mil. It is drawn to the picture's true
    /// angular scale (read from the camera's field of view), so a mil on the glass is a mil on the
    /// target at every magnification; finer marks appear only when the magnification gives them
    /// room. Black ink with a faint light edge so it reads on dark targets too. Each rifle's scope
    /// (ScopeStyle) has its own pattern drawn to the same mil scale: this mil hash, a mil-dot, a
    /// Christmas-tree holdover grid, or a heavy duplex with a lit red centre.
    /// </summary>
    public static class ScopeReticle
    {
        static readonly Color Halo = new Color(0.86f, 0.87f, 0.82f, 0.26f);
        static readonly Color Lit = new Color(1f, 0.12f, 0.06f, 0.95f);

        static ReticleKind Kind => ScopeStyle.Current.Reticle;

        /// <summary>Where the fine reticle gives way to the heavy posts, mil.</summary>
        static float Extent => Kind switch { ReticleKind.MilDot => 5, ReticleKind.Duplex => 4, _ => 10 };

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
                string key = FormattableString.Invariant($"{m:0.00}|{c.x:0.0},{c.y:0.0}|{r:0.0}|{Kind}");
                if (key == shown) return;
                shown = key;
                Relabel(e, labels, c, r, m);
                e.MarkDirtyRepaint();
            }).Every(16);
            e.generateVisualContent += ctx =>
            {
                if (!ScopeMask.Frame(e, out var c, out float r, out float h)) return;
                var p = ctx.painter2D;
                float m = PxPerMrad(h);
                // A faint light edge first, then the ink over it.
                Draw(p, c, r, m, Halo, 1.7f);
                Draw(p, c, r, m, ScopeStyle.Current.Ink, 0);
                if (Kind == ReticleKind.Duplex)
                {
                    // The lit centre: a small red dot glowing in the glass.
                    p.fillColor = new Color(Lit.r, Lit.g, Lit.b, 0.25f);
                    p.BeginPath();
                    p.Arc(c, 4.2f, 0, 360);
                    p.Fill();
                    p.fillColor = Lit;
                    p.BeginPath();
                    p.Arc(c, 2.1f, 0, 360);
                    p.Fill();
                }
            };
        }

        /// <summary>UI pixels per milliradian at the centre of the picture.</summary>
        static float PxPerMrad(float panelHeight)
        {
            var cam = Camera.main;
            float fov = cam != null ? cam.fieldOfView : 2.5f;
            return panelHeight * 0.5f / Mathf.Tan(Mathf.Clamp(fov, 0.1f, 170) * 0.5f * Mathf.Deg2Rad) / 1000f;
        }

        /// <summary>Where the posts begin, px from the centre: 10 mil, or inside the glass when it shows less.</summary>
        static float PostStart(float r, float m) => Mathf.Min(Extent * m, r * 0.8f);

        /// <summary>Whole mils of fine reticle before the posts.</summary>
        static int Reach(float r, float m) => Mathf.Max(1, Mathf.FloorToInt(PostStart(r, m) / m - 0.25f));

        static float Major(float m) => Mathf.Clamp(0.3f * m, 3.5f, 7);

        /// <summary>A dot a quarter of a mil across, kept readable at every zoom.</summary>
        static float DotRadius(float m) => Mathf.Clamp(0.125f * m, 1.3f, 3.2f);

        static void Dot(Painter2D p, Vector2 at, float radius)
        {
            p.BeginPath();
            p.Arc(at, radius, 0, 360);
            p.Fill();
        }

        /// <summary>Mils between numbers: every 2 where they fit, wider apart at low power.</summary>
        static int NumberEvery(float m)
        {
            foreach (int k in new[] { 2, 4, 5, 10 })
                if (k * m >= 19) return k;
            return 20;
        }

        static void Draw(Painter2D p, Vector2 c, float r, float m, Color col, float grow)
        {
            float post = PostStart(r, m);
            int reach = Reach(r, m);
            float major = Major(m);
            p.lineCap = LineCap.Butt;

            // Heavy posts from the 10 mil mark out past the edge of the glass, tapering toward the centre.
            p.fillColor = col;
            foreach (var dir in new[] { Vector2.left, Vector2.right, Vector2.down, Vector2.up })
            {
                float start = post - grow, end = r * 1.08f;
                if (end <= start) continue;
                var n = new Vector2(-dir.y, dir.x);
                // The duplex's posts are much heavier; the tree has no post below, where its grid runs.
                if (Kind == ReticleKind.Tree && dir == Vector2.up) continue;
                float heavy = Kind == ReticleKind.Duplex ? 2.2f : 1;
                float w0 = 0.7f * heavy + grow, w1 = 2.9f * heavy + grow;
                p.BeginPath();
                p.MoveTo(c + dir * start + n * w0);
                p.LineTo(c + dir * (start + 16) + n * w1);
                p.LineTo(c + dir * end + n * w1);
                p.LineTo(c + dir * end - n * w1);
                p.LineTo(c + dir * (start + 16) - n * w1);
                p.LineTo(c + dir * start - n * w0);
                p.ClosePath();
                p.Fill();
            }

            // Thin crosshair the full width of the glass.
            p.strokeColor = col;
            p.lineWidth = 1.1f + grow;
            p.BeginPath();
            Seg(p, c + new Vector2(-r * 1.08f, 0), c + new Vector2(r * 1.08f, 0));
            Seg(p, c + new Vector2(0, -r * 1.08f), c + new Vector2(0, r * 1.08f));
            if (Kind == ReticleKind.MilDot)
            {
                p.Stroke();
                // A dot at every mil on all four arms.
                p.fillColor = col;
                float d = DotRadius(m) + grow;
                for (int i = 1; i <= reach; i++)
                {
                    float o = i * m;
                    Dot(p, c + new Vector2(o, 0), d);
                    Dot(p, c + new Vector2(-o, 0), d);
                    Dot(p, c + new Vector2(0, o), d);
                    Dot(p, c + new Vector2(0, -o), d);
                }
                return;
            }
            if (Kind == ReticleKind.Duplex)
            {
                // Short whole-mil hashes on the fine cross, nothing finer.
                for (int i = 1; i <= reach; i++)
                {
                    float o = i * m, len = major * 0.7f;
                    Seg(p, c + new Vector2(o, -len), c + new Vector2(o, len));
                    Seg(p, c + new Vector2(-o, -len), c + new Vector2(-o, len));
                    Seg(p, c + new Vector2(-len, -o), c + new Vector2(len, -o));
                    Seg(p, c + new Vector2(-len, o), c + new Vector2(len, o));
                }
                p.Stroke();
                return;
            }
            if (Kind == ReticleKind.Tree)
            {
                p.Stroke();
                DrawTree(p, c, r, m, col, grow);
                p.lineWidth = 1.1f + grow;
                p.BeginPath();
            }
            // Whole-mil hashes on all four arms, longer at 5.
            for (int i = 1; i <= reach; i++)
            {
                float len = i % 5 == 0 ? major * 1.45f : major;
                float o = i * m;
                Seg(p, c + new Vector2(o, -len), c + new Vector2(o, len));
                Seg(p, c + new Vector2(-o, -len), c + new Vector2(-o, len));
                Seg(p, c + new Vector2(-len, -o), c + new Vector2(len, -o));
                Seg(p, c + new Vector2(-len, o), c + new Vector2(len, o));
            }
            p.Stroke();

            // Half-mil marks between them when they have room.
            if (m * 0.5f >= 4.5f)
            {
                p.lineWidth = 0.95f + grow;
                p.BeginPath();
                float len = major * 0.55f;
                for (float i = 0.5f; i < reach; i += 1)
                {
                    float o = i * m;
                    Seg(p, c + new Vector2(o, -len), c + new Vector2(o, len));
                    Seg(p, c + new Vector2(-o, -len), c + new Vector2(-o, len));
                    Seg(p, c + new Vector2(-len, -o), c + new Vector2(len, -o));
                    Seg(p, c + new Vector2(-len, o), c + new Vector2(len, o));
                }
                p.Stroke();
            }
            // 0.2 mil subtensions over the first two mils: above the line on the horizontal, to the right of the stem.
            if (m * 0.2f >= 3.5f)
            {
                p.lineWidth = 0.8f + grow;
                p.BeginPath();
                float len = major * 0.4f;
                for (int i = 1; i < 10; i++)
                {
                    if (i == 5) continue;
                    float o = i * 0.2f * m;
                    Seg(p, c + new Vector2(o, -len), c + new Vector2(o, 0));
                    Seg(p, c + new Vector2(-o, -len), c + new Vector2(-o, 0));
                    Seg(p, c + new Vector2(0, o), c + new Vector2(len, o));
                    Seg(p, c + new Vector2(0, -o), c + new Vector2(len, -o));
                }
                p.Stroke();
            }
        }

        /// <summary>Mils of tree grid below the centre that fit in the glass.</summary>
        static int TreeRows(float r, float m) => Mathf.Min(10, Mathf.FloorToInt(r * 0.9f / m));

        /// <summary>
        /// The tree's holdover grid: a dot every mil across each whole-mil row below the centre,
        /// one more each side per row, as far as the glass allows. It needs room between the
        /// dots, so at low power only the stem's hashes show.
        /// </summary>
        static void DrawTree(Painter2D p, Vector2 c, float r, float m, Color col, float grow)
        {
            int rows = TreeRows(r, m);
            float major = Major(m);
            // The stem's hashes run the whole way down, past the 10 mil post line.
            p.BeginPath();
            for (int i = Reach(r, m) + 1; i <= rows; i++)
                Seg(p, c + new Vector2(-major, i * m), c + new Vector2(major, i * m));
            p.Stroke();
            if (m < 6) return;
            p.fillColor = col;
            float d = Mathf.Clamp(0.08f * m, 1.1f, 2.2f) + grow;
            for (int i = 2; i <= rows; i++)
            {
                float y = i * m;
                int across = Mathf.Min(i + 1, 10);
                for (int j = 1; j <= across; j++)
                {
                    float x = j * m;
                    if (x * x + y * y > r * r * 0.86f) break;
                    Dot(p, c + new Vector2(x, y), d);
                    Dot(p, c + new Vector2(-x, y), d);
                }
            }
        }

        static void Relabel(VisualElement e, List<Label> labels, Vector2 c, float r, float m)
        {
            var want = new List<(string text, Vector2 at, TextAnchor align)>();
            int reach = Reach(r, m);
            float major = Major(m);
            int every = NumberEvery(m);
            // The mil-dot and the duplex carry no numbers.
            if (Kind == ReticleKind.MilDot || Kind == ReticleKind.Duplex) reach = 0;
            for (int i = every; i <= reach; i += every)
            {
                string t = i.ToString();
                // Above the hash on the horizontal; beside the hash on the stem, up and down.
                want.Add((t, c + new Vector2(i * m, -major - 9), TextAnchor.MiddleCenter));
                want.Add((t, c + new Vector2(-i * m, -major - 9), TextAnchor.MiddleCenter));
                // Below the centre the tree's grid fills the space beside the stem.
                if (Kind != ReticleKind.Tree) want.Add((t, c + new Vector2(major + 4, i * m), TextAnchor.MiddleLeft));
                want.Add((t, c + new Vector2(major + 4, -i * m), TextAnchor.MiddleLeft));
            }

            var font = Fonts.Get("Mono", 400, false);
            while (labels.Count < want.Count)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.style.position = Position.Absolute;
                l.style.width = 30;
                l.style.height = 14;
                l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
                l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
                l.style.fontSize = 12;
                l.style.color = ScopeStyle.Current.Ink;
                l.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 3, color = new Color(Halo.r, Halo.g, Halo.b, 0.7f) };
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
                var (text, at, align) = want[i];
                l.style.display = DisplayStyle.Flex;
                l.text = text;
                l.style.unityTextAlign = align;
                l.style.top = at.y - 7;
                l.style.left = align == TextAnchor.MiddleLeft ? at.x : at.x - 15;
            }
        }

        static void Seg(Painter2D p, Vector2 a, Vector2 b)
        {
            p.MoveTo(a);
            p.LineTo(b);
        }
    }
}
