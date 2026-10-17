using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// The scope's eyepiece over the world, as in a real rifle scope held to the eye: the glass
    /// with a soft dark fall-off at its edge, a thick machined metal tube round it,
    /// and the world beyond the tube dimmed nearly to black. Drawn as raw vertex-coloured rings so
    /// the gradients are smooth. The clear circle (the exit pupil) shifts with the shooter's eye:
    /// as the rifle wobbles, recoils or the eye sits off the axis, a dark crescent ("scope shadow")
    /// creeps in from one edge while the tube itself stays put, as in a real scope. The eyepiece
    /// size, the tube, the edge and the glare follow the fitted rifle's scope (ScopeStyle).
    /// </summary>
    public static class ScopeMask
    {
        /// <summary>Eye offset from the optical axis in UI pixels, set every frame by the duel.</summary>
        public static Vector2 EyeOffset;
        /// <summary>How far the eye has settled into the eyepiece: 0 just arriving (wide, dark), 1 settled.</summary>
        public static float Open = 1;
        static Vector2 drawn;
        static float drawnOpen = -1;
        static Vector2 drawnSize;
        static ScopeStyle? drawnStyle;

        /// <summary>
        /// Where the eyepiece sits in an element's frame: on the camera axis (the middle of the
        /// whole panel, whatever the safe area or aspect), with a radius from the panel height.
        /// </summary>
        public static bool Frame(VisualElement e, out Vector2 centre, out float radius, out float height)
        {
            centre = default;
            radius = height = 0;
            var root = e.panel?.visualTree;
            if (root == null) return false;
            var wb = root.worldBound;
            if (wb.height < 1 || float.IsNaN(wb.height)) return false;
            centre = e.WorldToLocal(wb.center);
            height = wb.height;
            radius = Mathf.Min(wb.height * 0.485f, wb.width * 0.31f) * ScopeStyle.Current.Pupil;
            return true;
        }

        public static void Attach(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            // The darkness runs out past the safe area to the real screen edges.
            if (e.parent != null) e.parent.style.overflow = Overflow.Visible;
            // Redraw when the eye moves or the scope opens.
            e.schedule.Execute(() =>
            {
                var size = e.panel?.visualTree.worldBound.size ?? Vector2.zero;
                if ((EyeOffset - drawn).sqrMagnitude > 0.25f || Mathf.Abs(Open - drawnOpen) > 0.002f || size != drawnSize || drawnStyle != ScopeStyle.Current) e.MarkDirtyRepaint();
            }).Every(16);
            e.generateVisualContent += ctx =>
            {
                if (!Frame(e, out var centre, out float radius, out _)) return;
                drawn = EyeOffset;
                drawnOpen = Open;
                drawnSize = e.panel?.visualTree.worldBound.size ?? Vector2.zero;
                var style = drawnStyle = ScopeStyle.Current;
                float tube = style.Tube, fall = style.Falloff;
                float open = Mathf.SmoothStep(0, 1, Mathf.Clamp01(Open));
                // Arriving at the eyepiece the picture starts wide and tunnels in to the tube.
                float r = radius * Mathf.Lerp(1.9f, 1, open);
                var pupil = centre + Vector2.ClampMagnitude(EyeOffset, r * 0.45f);
                float outer = Mathf.Max(drawnSize.x, drawnSize.y, 1000) * 2;
                byte a = (byte)Mathf.RoundToInt(Mathf.Lerp(150, 255, open));
                // The tube's metal in the scope's own finish.
                Color32 M(byte cr, byte cg, byte cb) => new Color32(
                    (byte)Mathf.Clamp(cr * style.Metal.r, 0, 255), (byte)Mathf.Clamp(cg * style.Metal.g, 0, 255), (byte)Mathf.Clamp(cb * style.Metal.b, 0, 255), a);

                // Eye-box shadow: soft falloff to black round the pupil, out under the tube.
                var black = new Color32(1, 1, 1, 0);
                float mid = Mathf.Lerp(fall, 0.985f, 0.6f);
                Ring(ctx, pupil, r * fall, black, pupil, r * mid, With(black, 55));
                Ring(ctx, pupil, r * mid, With(black, 55), pupil, r * 0.985f, With(black, 185));
                Ring(ctx, pupil, r * 0.985f, With(black, 185), pupil, r * 1.04f, With(black, a));
                Ring(ctx, pupil, r * 1.04f, With(black, a), centre, r * tube + 2, With(black, a));

                // The tube: dark machined metal with a shadowed inner lip, a lit bevel and a dark
                // outer edge, catching a little more light from above.
                Ring(ctx, centre, r, M(4, 4, 4), centre, r + 5, M(14, 15, 15), 0.1f);
                Ring(ctx, centre, r + 5, M(14, 15, 15), centre, r * 1.1f, M(40, 42, 44), 0.45f);
                Ring(ctx, centre, r * 1.1f, M(40, 42, 44), centre, r * 1.13f, M(60, 62, 65), 0.6f);
                Ring(ctx, centre, r * 1.13f, M(60, 62, 65), centre, r * 1.16f, M(24, 25, 26), 0.5f);
                Ring(ctx, centre, r * 1.16f, M(24, 25, 26), centre, r * (tube - 0.03f), M(33, 34, 36), 0.4f);
                Ring(ctx, centre, r * (tube - 0.03f), M(33, 34, 36), centre, r * tube, M(6, 6, 6), 0.3f);
                // Beyond the tube the world is still there, dimmed nearly to black.
                byte dim = (byte)Mathf.RoundToInt(Mathf.Lerp(150, 232, open));
                Ring(ctx, centre, r * tube, With(black, a), centre, r * tube + 40, With(black, dim));
                Ring(ctx, centre, r * tube + 40, With(black, dim), centre, outer, With(black, dim));

                var p = ctx.painter2D;
                // A faint lit edge where the glass meets the tube.
                p.strokeColor = new Color(0.62f, 0.64f, 0.62f, 0.18f * open);
                p.lineWidth = 1.2f;
                p.BeginPath();
                p.Arc(centre, r - 0.6f, 0, 360);
                p.Stroke();
                // A sliver of glare on the glass, top left.
                p.strokeColor = new Color(1, 1, 1, 0.05f * style.Glare * open);
                p.lineWidth = 5;
                p.BeginPath();
                p.Arc(centre, r * 0.9f, 200, 245);
                p.Stroke();
                p.strokeColor = new Color(1, 1, 1, 0.03f * style.Glare * open);
                p.lineWidth = 2;
                p.BeginPath();
                p.Arc(centre, r * 0.84f, 210, 232);
                p.Stroke();
            };
            e.MarkDirtyRepaint();
        }

        static Color32 With(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);

        static Color32 Lit(Color32 c, float k) =>
            k == 1 ? c : new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), c.a);

        /// <summary>
        /// A band between two circles (each with its own centre) with the colour graded from the inner
        /// to the outer edge: vignettes, the tube and the off-axis shadow crescent. `light` brightens
        /// the top of the band and darkens the bottom by up to that fraction.
        /// </summary>
        static void Ring(MeshGenerationContext ctx, Vector2 c0, float r0, Color32 col0, Vector2 c1, float r1, Color32 col1, float light = 0)
        {
            const int Seg = 96;
            var md = ctx.Allocate(Seg * 2, Seg * 6);
            for (int i = 0; i < Seg; i++)
            {
                float a = i * Mathf.PI * 2 / Seg;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var pi = c0 + dir * r0;
                var po = c1 + dir * r1;
                // Screen y runs down: dir.y < 0 is the top of the ring.
                float k = 1 - light * dir.y;
                md.SetNextVertex(new Vertex { position = new Vector3(pi.x, pi.y, Vertex.nearZ), tint = Lit(col0, k) });
                md.SetNextVertex(new Vertex { position = new Vector3(po.x, po.y, Vertex.nearZ), tint = Lit(col1, k) });
            }
            for (int i = 0; i < Seg; i++)
            {
                ushort i0 = (ushort)(i * 2), o0 = (ushort)(i * 2 + 1);
                ushort i1 = (ushort)((i + 1) % Seg * 2), o1 = (ushort)((i + 1) % Seg * 2 + 1);
                md.SetNextIndex(i0); md.SetNextIndex(o0); md.SetNextIndex(o1);
                md.SetNextIndex(i0); md.SetNextIndex(o1); md.SetNextIndex(i1);
            }
        }
    }
}
