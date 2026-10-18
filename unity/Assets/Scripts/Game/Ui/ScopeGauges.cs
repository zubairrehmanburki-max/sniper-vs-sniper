using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// The scope view's two steadiness cues: the heart rate as a short arc inside the glass on the
    /// left (a blue mark that climbs as the pulse rises, with the beats per minute beside it), and
    /// the breath left as a blue ring round the fire button that drains while the breath is held
    /// and fills again as the shooter recovers.
    /// </summary>
    public static class ScopeGauges
    {
        /// <summary>Set every frame by the duel.</summary>
        public static float Bpm = 72, Breath = 0.7f;
        public static bool Holding = true, Cancelling;

        static readonly Color Blue = new Color(0.31f, 0.70f, 0.91f);
        /// <summary>The arc's ends on the glass (degrees, clockwise from the right): calm at the bottom, racing at the top.</summary>
        const float CalmAngle = 152, RacingAngle = 196;
        const float MinBpm = 55, MaxBpm = 140;

        public static void AttachHeart(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            if (e.parent != null) e.parent.style.overflow = Overflow.Visible;
            var bpm = Text(e, 16, "Mono");
            var unit = Text(e, 12, "Condensed");
            unit.text = "BPM";
            int shownBpm = -1;
            Vector2 shownAt = default;
            e.schedule.Execute(() =>
            {
                if (!ScopeMask.Frame(e, out var c, out float r, out _)) return;
                var at = Point(c, r, CalmAngle) + new Vector2(16, 2);
                int b = Mathf.RoundToInt(Bpm);
                if (b == shownBpm && at == shownAt) return;
                shownBpm = b;
                shownAt = at;
                bpm.text = b.ToString();
                bpm.style.left = at.x + 4;
                bpm.style.top = at.y - 4;
                unit.style.left = at.x + 4;
                unit.style.top = at.y + 14;
                e.MarkDirtyRepaint();
            }).Every(50);
            e.generateVisualContent += ctx =>
            {
                if (!ScopeMask.Frame(e, out var c, out float r, out _)) return;
                var p = ctx.painter2D;
                float rad = ArcRadius(r);
                p.lineCap = LineCap.Round;
                // Dark groove with a faint light edge so it reads on sky and on shadow.
                p.strokeColor = new Color(0, 0, 0, 0.45f);
                p.lineWidth = 9;
                p.BeginPath();
                p.Arc(c, rad, CalmAngle, RacingAngle);
                p.Stroke();
                p.strokeColor = new Color(1, 1, 1, 0.16f);
                p.lineWidth = 5;
                p.BeginPath();
                p.Arc(c, rad, CalmAngle, RacingAngle);
                p.Stroke();
                float f = Mathf.Clamp01((Bpm - MinBpm) / (MaxBpm - MinBpm));
                float mark = Mathf.Lerp(CalmAngle, RacingAngle, f);
                p.strokeColor = Blue;
                p.lineWidth = 6;
                p.BeginPath();
                p.Arc(c, rad, Mathf.Max(CalmAngle, mark - 7), Mathf.Min(RacingAngle, mark + 2));
                p.Stroke();
                // A pulse trace beside the reading.
                var o = Point(c, r, CalmAngle) + new Vector2(0, 8);
                p.strokeColor = new Color(0.93f, 0.94f, 0.91f, 0.95f);
                p.lineWidth = 1.6f;
                p.lineJoin = LineJoin.Round;
                p.BeginPath();
                p.MoveTo(o);
                p.LineTo(o + new Vector2(4, 0));
                p.LineTo(o + new Vector2(6.5f, -5));
                p.LineTo(o + new Vector2(9.5f, 5));
                p.LineTo(o + new Vector2(12, -2));
                p.LineTo(o + new Vector2(16, -2));
                p.Stroke();
            };
        }

        static float ArcRadius(float r) => r * 0.9f;

        static Vector2 Point(Vector2 c, float r, float deg) =>
            c + new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * ArcRadius(r);

        static Label Text(VisualElement parent, float size, string family)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.style.position = Position.Absolute;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
            l.style.fontSize = size;
            l.style.color = new Color(0.93f, 0.94f, 0.91f);
            l.style.textShadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 3, color = new Color(0, 0, 0, 0.8f) };
            var font = Fonts.Get(family, family == "Condensed" ? 700 : 400, false);
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
            parent.Add(l);
            return l;
        }

        /// <summary>The breath ring round the fire button: drawn inside the element's own box.</summary>
        public static void AttachBreathRing(VisualElement e)
        {
            // Each duel wires the same (long-lived) button; draw its ring once.
            const string Mark = "breath-ring";
            if (e.ClassListContains(Mark)) return;
            e.AddToClassList(Mark);
            float shown = -1;
            bool shownHold = false, shownCancel = false;
            e.schedule.Execute(() =>
            {
                if (Mathf.Abs(Breath - shown) < 0.004f && Holding == shownHold && Cancelling == shownCancel) return;
                shown = Breath;
                shownHold = Holding;
                shownCancel = Cancelling;
                e.MarkDirtyRepaint();
            }).Every(16);
            e.generateVisualContent += ctx =>
            {
                var box = e.contentRect;
                if (box.width < 1) return;
                var c = box.center;
                float rad = Mathf.Min(box.width, box.height) / 2 - 4;
                var p = ctx.painter2D;
                p.lineCap = LineCap.Butt;
                p.strokeColor = new Color(0, 0, 0, 0.5f);
                p.lineWidth = 7;
                p.BeginPath();
                p.Arc(c, rad, 0, 360);
                p.Stroke();
                float f = Mathf.Clamp01(Breath);
                if (f <= 0.002f) return;
                var col = Cancelling ? new Color(0.65f, 0.67f, 0.64f) : Blue;
                if (!Holding && !Cancelling) col.a = 0.6f;
                p.strokeColor = col;
                p.lineWidth = 5;
                p.lineCap = LineCap.Round;
                p.BeginPath();
                // From the top, clockwise; it drains back toward the top.
                p.Arc(c, rad, -90, -90 + 360 * f);
                p.Stroke();
            };
        }
    }
}
