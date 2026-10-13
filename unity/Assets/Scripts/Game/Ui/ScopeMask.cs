using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// Darkens everything outside the scope's eyepiece: a dark annulus out past the screen edges, a
    /// black tube ring and a soft inner shadow, drawn with painter2D so the world shows through.
    /// The clear circle shifts with the shooter's eye: as the rifle wobbles, recoils or the eye sits
    /// off the axis, a dark crescent ("scope shadow") creeps in from one edge, as in a real scope.
    /// </summary>
    public static class ScopeMask
    {
        /// <summary>Eye offset from the optical axis in UI pixels, set every frame by the duel.</summary>
        public static Vector2 EyeOffset;
        static Vector2 drawn;

        public static void Attach(VisualElement e, Vector2 centre, float radius)
        {
            e.pickingMode = PickingMode.Ignore;
            // Redraw when the eye moves.
            e.schedule.Execute(() =>
            {
                if ((EyeOffset - drawn).sqrMagnitude > 0.25f) e.MarkDirtyRepaint();
            }).Every(16);
            e.generateVisualContent += ctx =>
            {
                var r = e.contentRect;
                drawn = EyeOffset;
                var clear = centre + Vector2.ClampMagnitude(EyeOffset, radius * 0.6f);
                var p = ctx.painter2D;
                // Outside: a near-black annulus from the tube out past the screen corners, as raw
                // triangles (painter2D fills and very wide strokes don't cover this reliably).
                const int Seg = 128;
                float outer = Mathf.Max(r.width, r.height) * 2;
                var md = ctx.Allocate(Seg * 2, Seg * 6);
                var tint = new Color32(3, 4, 3, 235);
                for (int i = 0; i < Seg; i++)
                {
                    float a = i * Mathf.PI * 2 / Seg;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    var pi = clear + dir * (radius + 8);
                    var po = centre + dir * outer;
                    md.SetNextVertex(new Vertex { position = new Vector3(pi.x, pi.y, Vertex.nearZ), tint = tint });
                    md.SetNextVertex(new Vertex { position = new Vector3(po.x, po.y, Vertex.nearZ), tint = tint });
                }
                for (int i = 0; i < Seg; i++)
                {
                    ushort i0 = (ushort)(i * 2), o0 = (ushort)(i * 2 + 1);
                    ushort i1 = (ushort)((i + 1) % Seg * 2), o1 = (ushort)((i + 1) % Seg * 2 + 1);
                    md.SetNextIndex(i0); md.SetNextIndex(o0); md.SetNextIndex(o1);
                    md.SetNextIndex(i0); md.SetNextIndex(o1); md.SetNextIndex(i1);
                }
                // Tube ring, then a falloff inside the glass.
                p.strokeColor = Color.black;
                p.lineWidth = 12;
                p.BeginPath();
                p.Arc(centre, radius + 4, 0, 360);
                p.Stroke();
                for (int i = 0; i < 8; i++)
                {
                    p.strokeColor = new Color(0, 0, 0, 0.55f * (1 - i / 8f));
                    p.lineWidth = 4;
                    p.BeginPath();
                    p.Arc(clear, radius + 6 - i * 4, 0, 360);
                    p.Stroke();
                }
            };
            e.MarkDirtyRepaint();
        }
    }
}
