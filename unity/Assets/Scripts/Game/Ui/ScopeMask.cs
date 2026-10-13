using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// Darkens everything outside the scope's eyepiece: a wide dark ring out to the screen edges, a
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
                // Outside: a near-black haze from the tube out past the screen corners, drawn as one
                // very wide ring (robust where an odd-even fill with an arc hole is not).
                float reach = Mathf.Max(r.width, r.height) * 1.5f;
                p.strokeColor = new Color(0.012f, 0.016f, 0.012f, 0.92f);
                p.lineWidth = reach;
                p.BeginPath();
                p.Arc(centre, radius + 8 + reach / 2, 0, 360);
                p.Stroke();
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
