using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// Darkens everything outside the scope's eyepiece: a full-screen fill with a round hole, a
    /// black tube ring and a soft inner shadow, drawn with painter2D so the world shows through.
    /// </summary>
    public static class ScopeMask
    {
        public static void Attach(VisualElement e, Vector2 centre, float radius)
        {
            e.pickingMode = PickingMode.Ignore;
            e.generateVisualContent += ctx =>
            {
                var r = e.contentRect;
                var p = ctx.painter2D;
                // Outside: near-black haze over the screen edge, hole cut with the odd-even rule.
                p.fillColor = new Color(0.012f, 0.016f, 0.012f, 0.9f);
                p.BeginPath();
                p.MoveTo(new Vector2(r.xMin - 50, r.yMin - 50));
                p.LineTo(new Vector2(r.xMax + 50, r.yMin - 50));
                p.LineTo(new Vector2(r.xMax + 50, r.yMax + 50));
                p.LineTo(new Vector2(r.xMin - 50, r.yMax + 50));
                p.ClosePath();
                p.MoveTo(centre + new Vector2(radius + 10, 0));
                p.Arc(centre, radius + 10, 0, 360);
                p.ClosePath();
                p.Fill(FillRule.OddEven);
                // Tube ring, then a falloff inside the glass.
                p.strokeColor = Color.black;
                p.lineWidth = 12;
                p.BeginPath();
                p.Arc(centre, radius + 4, 0, 360);
                p.Stroke();
                for (int i = 0; i < 6; i++)
                {
                    p.strokeColor = new Color(0, 0, 0, 0.5f * (1 - i / 6f));
                    p.lineWidth = 4;
                    p.BeginPath();
                    p.Arc(centre, radius - 2 - i * 4, 0, 360);
                    p.Stroke();
                }
            };
            e.MarkDirtyRepaint();
        }
    }
}
