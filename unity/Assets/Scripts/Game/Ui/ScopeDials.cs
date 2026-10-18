using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// The rifle scope's controls seen from behind the shooter, drawn on the HUD outside the scope:
    /// the elevation turret on top, the windage knob on the right, the side focus (parallax) knob on
    /// the left and the magnification ring at the eyepiece. Each knob is a knurled metal cylinder
    /// with its scale engraved round it; the scale turns under an amber index mark as the value
    /// changes, foreshortened toward the edges as on a real drum. Swiping on a knob turns it: the
    /// surface follows the thumb (sideways on the turret and the ring, up and down on the side
    /// knobs), and every detent it crosses is reported to <see cref="Turned"/>.
    /// </summary>
    public static class ScopeDials
    {
        public enum Dial { Elevation, Windage, Zoom, Focus }

        /// <summary>What the knobs show, set every frame by the duel: mil up / right, the magnification, the focus position (see FocusMetres).</summary>
        public static double Elevation = 3.1, Windage = 0.5, Focus = 3.6;
        public static int Zoom = 12;
        public static double MinElevation = -2, MaxElevation = 30, MaxWindage = 10;
        public static int MinZoom = 5, MaxZoom = 25;
        /// <summary>A knob was turned this many detents (positive: up, right, more zoom, farther focus).</summary>
        public static Action<Dial, int>? Turned;

        /// <summary>The focus scale runs 0 to 5: 50 m doubling each step to 800 m at 4, infinity at 5.</summary>
        public const double FocusMax = 5;
        public static double FocusMetres(double p) =>
            p <= 4 ? 50 * Math.Pow(2, Math.Max(0, p)) : p >= FocusMax - 0.001 ? double.PositiveInfinity : 800 / (FocusMax - p);
        public static string FocusText(double p)
        {
            double m = FocusMetres(p);
            return double.IsInfinity(m) || m > 5000 ? "∞" : $"{Math.Round(m / 10) * 10:0} m";
        }

        static readonly int[] ZoomMarks = { 5, 8, 10, 12, 15, 18, 20, 25 };

        static readonly Color32 Metal = new Color32(46, 48, 52, 255);
        static readonly Color Engrave = new Color(0.9f, 0.9f, 0.86f);
        static readonly Color Amber = new Color(0.95f, 0.70f, 0.24f);

        /// <summary>Host size in layout units; the knobs sit at fixed spots inside it.</summary>
        public const float Width = 330, Height = 226;
        /// <summary>
        /// The scope is drawn a little smaller than its layout so it clears the crosshair while the
        /// magnification ring stays well up from the bottom edge, where iOS keeps its home swipe.
        /// </summary>
        public const float BodyScale = 0.82f;

        public static void Attach(VisualElement host)
        {
            host.pickingMode = PickingMode.Ignore;
            host.style.width = Width;
            host.style.height = Height;
            // Shrinks toward the bottom middle, where the host is anchored.
            var body = new VisualElement { name = "dials-body", pickingMode = PickingMode.Ignore };
            body.style.position = Position.Absolute;
            body.style.left = 0;
            body.style.top = 0;
            body.style.width = Width;
            body.style.height = Height;
            body.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100), 0);
            body.style.scale = new Scale(new Vector2(BodyScale, BodyScale));
            host.Add(body);
            body.generateVisualContent += DrawBody;
            Add(body, new Knob(Dial.Focus, new Rect(28, 96, 66, 68), horizontal: false, radius: 33, degPerUnit: 34, unit: 0.1, minorStep: 0.25, majorStep: 1));
            Add(body, new Knob(Dial.Windage, new Rect(236, 96, 66, 68), horizontal: false, radius: 33, degPerUnit: 36, unit: 0.1, minorStep: 0.1, majorStep: 1));
            Add(body, new Knob(Dial.Elevation, new Rect(95, 4, 140, 90), horizontal: true, radius: 68, degPerUnit: 36, unit: 0.1, minorStep: 0.1, majorStep: 1));
            // The ring's touch area reaches past the drawn ring below and to the sides, so a thumb
            // doesn't have to land exactly on it.
            Add(body, new Knob(Dial.Zoom, new Rect(48, 166, 234, 60), horizontal: true, radius: 105, degPerUnit: 6, unit: 1, minorStep: 1, majorStep: 5));
        }

        static void Add(VisualElement host, Knob k)
        {
            host.Add(k.El);
            k.Wire();
        }

        // ---- the scope body (static) ---------------------------------------------------------

        static void DrawBody(MeshGenerationContext ctx)
        {
            // Centre tube between the side knobs, and the stems the knobs turn on.
            Band(ctx, new Vector2(96, 100), new Vector2(234, 100), new Vector2(96, 166), new Vector2(234, 166), Metal, 0.85f);
            Band(ctx, new Vector2(86, 112), new Vector2(98, 112), new Vector2(86, 148), new Vector2(98, 148), new Color32(28, 29, 31, 255), 1, vertical: true);
            Band(ctx, new Vector2(232, 112), new Vector2(244, 112), new Vector2(232, 148), new Vector2(244, 148), new Color32(28, 29, 31, 255), 1, vertical: true);
            // The eyepiece bell widening toward the shooter.
            Band(ctx, new Vector2(100, 162), new Vector2(230, 162), new Vector2(62, 178), new Vector2(268, 178), new Color32(38, 40, 44, 255), 0.9f);
            var p = ctx.painter2D;
            // The turret saddle, with its lit top edge.
            p.fillColor = new Color32(22, 23, 25, 255);
            p.BeginPath();
            RoundRect(p, new Rect(88, 88, 154, 18), 5);
            p.Fill();
            p.strokeColor = new Color(1, 1, 1, 0.14f);
            p.lineWidth = 1;
            p.BeginPath();
            p.MoveTo(new Vector2(93, 88.5f));
            p.LineTo(new Vector2(237, 88.5f));
            p.Stroke();
            // Index marks: under the turret, beside each side knob, above the ring.
            Triangle(p, new Vector2(165, 95), Vector2.up);
            Triangle(p, new Vector2(233, 130), Vector2.right);
            Triangle(p, new Vector2(97, 130), Vector2.left);
            Triangle(p, new Vector2(165, 173), Vector2.down);
        }

        static void Triangle(Painter2D p, Vector2 tip, Vector2 dir)
        {
            var n = new Vector2(-dir.y, dir.x);
            p.fillColor = Amber;
            p.BeginPath();
            p.MoveTo(tip);
            p.LineTo(tip - dir * 9 + n * 6);
            p.LineTo(tip - dir * 9 - n * 6);
            p.ClosePath();
            p.Fill();
        }

        static void RoundRect(Painter2D p, Rect r, float rad)
        {
            p.MoveTo(new Vector2(r.xMin + rad, r.yMin));
            p.LineTo(new Vector2(r.xMax - rad, r.yMin));
            p.ArcTo(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMin + rad), rad);
            p.LineTo(new Vector2(r.xMax, r.yMax - rad));
            p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.xMax - rad, r.yMax), rad);
            p.LineTo(new Vector2(r.xMin + rad, r.yMax));
            p.ArcTo(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMax - rad), rad);
            p.LineTo(new Vector2(r.xMin, r.yMin + rad));
            p.ArcTo(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + rad, r.yMin), rad);
            p.ClosePath();
        }

        static void Ellipse(Painter2D p, Vector2 c, float rx, float ry, float from = 0, float to = 360)
        {
            const int N = 40;
            for (int i = 0; i <= N; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)N) * Mathf.Deg2Rad;
                var v = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(v);
                else p.LineTo(v);
            }
            p.ClosePath();
        }

        /// <summary>
        /// A shaded metal surface curving across one direction: from the edge a0–a1 to the edge
        /// b0–b1 (or top to bottom when `vertical`), lit in the middle and dark where it turns away,
        /// with a little more light from the upper left.
        /// </summary>
        static void Band(MeshGenerationContext ctx, Vector2 a0, Vector2 b0, Vector2 a1, Vector2 b1, Color32 col, float gain, bool vertical = false)
        {
            const int N = 24;
            // Columns run from (a0, a1) to (b0, b1); for a vertical band they run from (a0, b0) to (a1, b1).
            Vector2 s0 = a0, s1 = vertical ? b0 : a1, e0 = vertical ? a1 : b0, e1 = b1;
            var md = ctx.Allocate((N + 1) * 2, N * 6);
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N;
                float lit = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Lerp(0.04f, 0.96f, t)), 0.7f);
                float k = (0.32f + 0.95f * lit + 0.18f * (1 - t)) * gain;
                var c = new Color32((byte)Mathf.Min(255, col.r * k), (byte)Mathf.Min(255, col.g * k), (byte)Mathf.Min(255, col.b * k), col.a);
                var u = Vector2.Lerp(s0, e0, t);
                var v = Vector2.Lerp(s1, e1, t);
                md.SetNextVertex(new Vertex { position = new Vector3(u.x, u.y, Vertex.nearZ), tint = c });
                md.SetNextVertex(new Vertex { position = new Vector3(v.x, v.y, Vertex.nearZ), tint = c });
            }
            for (int i = 0; i < N; i++)
            {
                ushort a = (ushort)(i * 2), b = (ushort)(i * 2 + 1), c2 = (ushort)(i * 2 + 2), d = (ushort)(i * 2 + 3);
                // Clockwise on screen either way round.
                if (vertical)
                {
                    md.SetNextIndex(a); md.SetNextIndex(b); md.SetNextIndex(c2);
                    md.SetNextIndex(b); md.SetNextIndex(d); md.SetNextIndex(c2);
                }
                else
                {
                    md.SetNextIndex(a); md.SetNextIndex(c2); md.SetNextIndex(b);
                    md.SetNextIndex(b); md.SetNextIndex(c2); md.SetNextIndex(d);
                }
            }
        }

        // ---- one knob ------------------------------------------------------------------------

        sealed class Knob
        {
            public readonly VisualElement El;
            readonly Dial kind;
            readonly bool horizontal;
            readonly float radius, degPerUnit;
            readonly double unit, minorStep, majorStep;
            readonly List<Label> labels = new List<Label>();
            string shown = "";
            int? pointer;
            Vector2 last;
            /// <summary>Thumb travel toward the next detent, in detents (under one): the drum follows it, then settles back on the detent.</summary>
            float carry;

            public Knob(Dial kind, Rect at, bool horizontal, float radius, float degPerUnit, double unit, double minorStep, double majorStep)
            {
                this.kind = kind;
                this.horizontal = horizontal;
                this.radius = radius;
                this.degPerUnit = degPerUnit;
                this.unit = unit;
                this.minorStep = minorStep;
                this.majorStep = majorStep;
                El = new VisualElement { name = "dial-" + kind.ToString().ToLowerInvariant() };
                El.style.position = Position.Absolute;
                El.style.left = at.x;
                El.style.top = at.y;
                El.style.width = at.width;
                El.style.height = at.height;
                El.style.overflow = Overflow.Hidden;
                El.pickingMode = PickingMode.Position;
                El.AddToClassList(Layout.ActionClass);
            }

            double Value => kind switch
            {
                Dial.Elevation => Elevation,
                Dial.Windage => Windage,
                Dial.Zoom => Zoom,
                _ => Focus,
            };

            double Min => kind switch
            {
                Dial.Elevation => MinElevation,
                Dial.Windage => -MaxWindage,
                Dial.Zoom => MinZoom,
                _ => 0,
            };

            double Max => kind switch
            {
                Dial.Elevation => MaxElevation,
                Dial.Windage => MaxWindage,
                Dial.Zoom => MaxZoom,
                _ => FocusMax,
            };

            /// <summary>Thumb travel per detent: the surface follows the thumb, geared down where the detents are fine.</summary>
            float PxPerClick => Mathf.Max(4, radius * degPerUnit * Mathf.Deg2Rad * (float)unit);

            /// <summary>The face the scale is engraved on, in the knob's frame.</summary>
            Rect Face => kind switch
            {
                Dial.Elevation => new Rect(2, 18, 136, 66),
                Dial.Windage => new Rect(0, 0, 56, 68),
                Dial.Focus => new Rect(10, 0, 56, 68),
                _ => new Rect(10, 8, 214, 38),
            };

            public void Wire()
            {
                El.generateVisualContent += Draw;
                El.schedule.Execute(() =>
                {
                    // Let go between detents: the drum springs back onto the one it clicked into.
                    if (pointer == null && carry != 0) carry = Mathf.Abs(carry) < 0.02f ? 0 : carry * 0.6f;
                    string key = Value.ToString("0.000") + "/" + Shown.ToString("0.000");
                    if (key == shown) return;
                    shown = key;
                    Relabel();
                    El.MarkDirtyRepaint();
                }).Every(16);
                El.RegisterCallback<PointerDownEvent>(e =>
                {
                    e.StopPropagation();
                    if (pointer != null) return;
                    pointer = e.pointerId;
                    // Local coordinates, so the thumb's travel is measured on the knob as drawn.
                    last = e.localPosition;
                    carry = 0;
                    El.CapturePointer(e.pointerId);
                });
                El.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (pointer != e.pointerId) return;
                    Vector2 pos = e.localPosition;
                    var d = pos - last;
                    last = pos;
                    // Pulling the scale toward lower numbers brings the higher ones under the index.
                    carry += -(horizontal ? d.x : d.y) / PxPerClick;
                    // At an end stop the drum doesn't turn any further.
                    if ((carry > 0 && Value >= Max - 1e-6) || (carry < 0 && Value <= Min + 1e-6)) carry = 0;
                    int n = (int)carry;
                    if (n != 0)
                    {
                        carry -= n;
                        // One detent at a time, so each one clicks and ticks on its own.
                        for (int i = 0; i < Math.Abs(n); i++) Turned?.Invoke(kind, Math.Sign(n));
                    }
                    Relabel();
                    El.MarkDirtyRepaint();
                });
                El.RegisterCallback<PointerUpEvent>(e => End(e.pointerId, true));
                El.RegisterCallback<PointerCancelEvent>(e => End(e.pointerId, true));
                El.RegisterCallback<PointerCaptureOutEvent>(e => End(e.pointerId, false));
            }

            void End(int id, bool release)
            {
                if (pointer != id) return;
                pointer = null;
                if (release && El.HasPointerCapture(id)) El.ReleasePointer(id);
            }

            /// <summary>The value under the index as drawn: the set value plus the thumb's travel toward the next detent.</summary>
            double Shown => Value + carry * unit;

            /// <summary>Where a scale value sits across the drum, and how squarely it faces us (0 at the edge, 1 head-on).</summary>
            bool Project(double v, out float offset, out float facing)
            {
                float a = (float)(v - Shown) * degPerUnit * Mathf.Deg2Rad;
                offset = radius * Mathf.Sin(a);
                facing = Mathf.Cos(a);
                return Mathf.Abs(a) < Mathf.PI * 0.5f;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var f = Face;
                var p = ctx.painter2D;
                if (kind == Dial.Elevation)
                {
                    // The drum, its rounded underside and the flat top with the cap screw.
                    Band(ctx, new Vector2(f.xMin, f.yMin), new Vector2(f.xMax, f.yMin), new Vector2(f.xMin, f.yMax), new Vector2(f.xMax, f.yMax), Metal, 1);
                    p.fillColor = new Color32(20, 21, 23, 255);
                    p.BeginPath();
                    Ellipse(p, new Vector2(f.center.x, f.yMax), f.width / 2, 6, 0, 180);
                    p.Fill();
                    p.fillColor = new Color32(66, 69, 74, 255);
                    p.BeginPath();
                    Ellipse(p, new Vector2(f.center.x, f.yMin), f.width / 2, 12);
                    p.Fill();
                    p.fillColor = new Color32(30, 31, 34, 255);
                    p.BeginPath();
                    Ellipse(p, new Vector2(f.center.x, f.yMin + 0.5f), f.width / 2 - 14, 8);
                    p.Fill();
                    p.fillColor = new Color32(12, 12, 13, 255);
                    p.BeginPath();
                    Ellipse(p, new Vector2(f.center.x, f.yMin + 1), 18, 3);
                    p.Fill();
                    Knurl(p, new Rect(f.xMin, f.yMin + 12, f.width, 11), 0.25);
                    Ticks(p, f.yMin + 25, down: true);
                }
                else if (kind == Dial.Zoom)
                {
                    Band(ctx, new Vector2(f.xMin, f.yMin), new Vector2(f.xMax, f.yMin), new Vector2(f.xMin, f.yMax), new Vector2(f.xMax, f.yMax), new Color32(40, 42, 46, 255), 1);
                    Knurl(p, new Rect(f.xMin, f.yMin + 1, f.width, 11), 0.5);
                    Ticks(p, f.yMin + 13, down: true);
                }
                else
                {
                    // Side knobs: the face curves top to bottom; the knurled end cap is on the outside.
                    bool right = kind == Dial.Windage;
                    var cap = right ? new Rect(f.xMax, 2, 10, f.height - 4) : new Rect(0, 2, 10, f.height - 4);
                    Band(ctx, new Vector2(f.xMin, f.yMin), new Vector2(f.xMax, f.yMin), new Vector2(f.xMin, f.yMax), new Vector2(f.xMax, f.yMax), Metal, 1, vertical: true);
                    Band(ctx, new Vector2(cap.xMin, cap.yMin), new Vector2(cap.xMax, cap.yMin), new Vector2(cap.xMin, cap.yMax), new Vector2(cap.xMax, cap.yMax), new Color32(30, 31, 34, 255), 1, vertical: true);
                    Knurl(p, cap, 0.25);
                    Ticks(p, right ? f.xMin + 1 : f.xMax - 1, down: right);
                }
            }

            /// <summary>Fine ridges round the drum that roll with it.</summary>
            void Knurl(Painter2D p, Rect r, double step)
            {
                double from = Math.Floor((Shown - 90 / degPerUnit) / step) * step;
                for (double v = from; v < Shown + 90 / degPerUnit; v += step)
                {
                    if (!Project(v, out float o, out float facing)) continue;
                    p.strokeColor = new Color(0, 0, 0, 0.5f * facing + 0.15f);
                    p.lineWidth = 1;
                    p.BeginPath();
                    if (horizontal) { p.MoveTo(new Vector2(r.center.x + o, r.yMin)); p.LineTo(new Vector2(r.center.x + o, r.yMax)); }
                    else { p.MoveTo(new Vector2(r.xMin, r.center.y + o)); p.LineTo(new Vector2(r.xMax, r.center.y + o)); }
                    p.Stroke();
                    p.strokeColor = new Color(1, 1, 1, 0.07f * facing);
                    p.BeginPath();
                    if (horizontal) { p.MoveTo(new Vector2(r.center.x + o + 1, r.yMin)); p.LineTo(new Vector2(r.center.x + o + 1, r.yMax)); }
                    else { p.MoveTo(new Vector2(r.xMin, r.center.y + o + 1)); p.LineTo(new Vector2(r.xMax, r.center.y + o + 1)); }
                    p.Stroke();
                }
            }

            /// <summary>
            /// The engraved scale: ticks from `edge` into the face (downward on the turret and ring,
            /// rightward on the windage knob, leftward on the focus knob).
            /// </summary>
            void Ticks(Painter2D p, float edge, bool down)
            {
                var f = Face;
                double first = Math.Ceiling(Min / minorStep - 1e-6) * minorStep;
                for (double v = first; v <= Max + 1e-6; v += minorStep)
                {
                    if (!Project(v, out float o, out float facing)) continue;
                    bool major = IsMultiple(v, majorStep);
                    bool mid = !major && IsMultiple(v, majorStep / 2);
                    float len = major ? 11 : mid ? 7.5f : 4.5f;
                    p.strokeColor = new Color(Engrave.r, Engrave.g, Engrave.b, 0.15f + 0.8f * facing);
                    p.lineWidth = major ? 1.6f : 1;
                    p.BeginPath();
                    if (horizontal)
                    {
                        p.MoveTo(new Vector2(f.center.x + o, edge));
                        p.LineTo(new Vector2(f.center.x + o, edge + len));
                    }
                    else
                    {
                        float dir = down ? 1 : -1;
                        p.MoveTo(new Vector2(edge, f.center.y + o));
                        p.LineTo(new Vector2(edge + dir * len, f.center.y + o));
                    }
                    p.Stroke();
                }
            }

            static bool IsMultiple(double v, double step) => Math.Abs(v / step - Math.Round(v / step)) < 1e-4;

            IEnumerable<(double at, string text)> Marks()
            {
                switch (kind)
                {
                    case Dial.Elevation:
                        for (int i = (int)Math.Ceiling(Min); i <= Max; i++) yield return (i, i.ToString());
                        break;
                    case Dial.Windage:
                        for (int i = (int)-MaxWindage; i <= MaxWindage; i++) yield return (i, i == 0 ? "0" : (i < 0 ? "L" : "R") + Math.Abs(i));
                        break;
                    case Dial.Zoom:
                        foreach (int z in ZoomMarks) if (z >= MinZoom && z <= MaxZoom) yield return (z, z.ToString());
                        break;
                    default:
                        for (int i = 0; i <= FocusMax; i++) yield return (i, i == FocusMax ? "∞" : (50 << i).ToString());
                        break;
                }
            }

            void Relabel()
            {
                var want = new List<(string text, float offset, float facing)>();
                foreach (var (at, text) in Marks())
                    if (Project(at, out float o, out float facing) && facing > 0.2f) want.Add((text, o, facing));
                var mono = Fonts.Get("Mono", 400, false);
                var plain = Fonts.Get("Barlow", 600, false);
                while (labels.Count < want.Count)
                {
                    var l = new Label { pickingMode = PickingMode.Ignore };
                    l.style.position = Position.Absolute;
                    l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
                    l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
                    // Drawn at BodyScale, so a size up to read as 12.
                    l.style.fontSize = 14;
                    l.style.height = 16;
                    El.Add(l);
                    labels.Add(l);
                }
                var f = Face;
                for (int i = 0; i < labels.Count; i++)
                {
                    var l = labels[i];
                    if (i >= want.Count) { l.style.display = DisplayStyle.None; continue; }
                    var (text, o, facing) = want[i];
                    l.style.display = DisplayStyle.Flex;
                    l.text = text;
                    var font = text == "∞" ? plain : mono;
                    if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
                    l.style.color = new Color(Engrave.r, Engrave.g, Engrave.b, Mathf.Clamp01(facing * 1.3f - 0.2f));
                    // Narrower toward the edges, as the drum turns away.
                    l.style.scale = new Scale(horizontal ? new Vector2(Mathf.Max(0.35f, facing), 1) : new Vector2(1, Mathf.Max(0.35f, facing)));
                    if (horizontal)
                    {
                        l.style.width = 30;
                        l.style.left = f.center.x + o - 15;
                        l.style.top = kind == Dial.Zoom ? f.yMin + 23 : f.yMin + 38;
                        l.style.unityTextAlign = TextAnchor.MiddleCenter;
                    }
                    else
                    {
                        l.style.width = 34;
                        l.style.top = f.center.y + o - 8;
                        bool right = kind == Dial.Windage;
                        l.style.left = right ? f.xMin + 15 : f.xMax - 15 - 34;
                        l.style.unityTextAlign = right ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                    }
                }
            }
        }
    }
}
