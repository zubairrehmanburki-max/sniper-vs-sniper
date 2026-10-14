using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game.Ui
{
    /// <summary>
    /// The scope's eyepiece over the world: a dark tube from the glass out past the screen edges, a
    /// machined rim, a soft vignette inside the glass, a faint coating tint and glare, and a hint of
    /// colour fringing where the image falls off. Drawn as raw vertex-coloured rings so the gradients
    /// are smooth. The clear circle (the exit pupil) shifts with the shooter's eye: as the rifle
    /// wobbles, recoils or the eye sits off the axis, a dark crescent ("scope shadow") creeps in from
    /// one edge while the tube itself stays put, as in a real scope.
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
            radius = Mathf.Min(wb.height * 0.465f, wb.width * 0.3f);
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
                if ((EyeOffset - drawn).sqrMagnitude > 0.25f || Mathf.Abs(Open - drawnOpen) > 0.002f || size != drawnSize) e.MarkDirtyRepaint();
            }).Every(16);
            e.generateVisualContent += ctx =>
            {
                if (!Frame(e, out var centre, out float radius, out _)) return;
                drawn = EyeOffset;
                drawnOpen = Open;
                drawnSize = e.panel?.visualTree.worldBound.size ?? Vector2.zero;
                float open = Mathf.SmoothStep(0, 1, Mathf.Clamp01(Open));
                // Arriving at the eyepiece the picture starts wide and tunnels in to the tube.
                float r = radius * Mathf.Lerp(1.9f, 1, open);
                var pupil = centre + Vector2.ClampMagnitude(EyeOffset, r * 0.45f);
                float outer = Mathf.Max(drawnSize.x, drawnSize.y, 1000) * 2;
                byte fade = (byte)Mathf.RoundToInt(Mathf.Lerp(150, 255, open));

                // Glass: a cool coating tint that deepens toward the edge, and a soft vignette.
                var tint = new Color32(60, 105, 115, 0);
                Ring(ctx, centre, 0, tint, centre, r * 0.65f, With(tint, 6));
                Ring(ctx, centre, r * 0.65f, With(tint, 6), centre, r, With(tint, 24));
                // Colour fringe where the image falls off: blue inside, warm outside, following the pupil.
                var blue = new Color32(70, 130, 255, 0);
                var warm = new Color32(255, 96, 40, 0);
                var fringe = pupil + (pupil - centre) * 0.06f;
                Ring(ctx, fringe, r * 0.83f, blue, fringe, r * 0.885f, With(blue, 20));
                Ring(ctx, fringe, r * 0.885f, With(blue, 20), fringe, r * 0.92f, blue);
                Ring(ctx, fringe, r * 0.9f, warm, fringe, r * 0.945f, With(warm, 26));
                Ring(ctx, fringe, r * 0.945f, With(warm, 26), fringe, r * 0.99f, warm);
                // Eye-box shadow: soft falloff to black around the pupil, out to the edges.
                var black = new Color32(1, 2, 1, 0);
                Ring(ctx, pupil, r * 0.74f, black, pupil, r * 0.9f, With(black, 60));
                Ring(ctx, pupil, r * 0.9f, With(black, 60), pupil, r * 0.985f, With(black, 190));
                Ring(ctx, pupil, r * 0.985f, With(black, 190), pupil, r * 1.04f, With(black, fade));
                Ring(ctx, pupil, r * 1.04f, With(black, fade), centre, outer, With(black, fade));
                // The tube itself: hard dark outside the glass, wherever the eye is.
                var tube = new Color32(3, 4, 3, fade);
                Ring(ctx, centre, r, tube, centre, outer, tube);

                var p = ctx.painter2D;
                // Machined rim: a dark lip with a faint lit edge just inside it.
                p.strokeColor = new Color(0, 0, 0, open);
                p.lineWidth = 7;
                p.BeginPath();
                p.Arc(centre, r + 3, 0, 360);
                p.Stroke();
                p.strokeColor = new Color(0.62f, 0.66f, 0.6f, 0.2f * open);
                p.lineWidth = 1.2f;
                p.BeginPath();
                p.Arc(centre, r - 0.6f, 0, 360);
                p.Stroke();
                // A sliver of glare on the glass, top left.
                p.strokeColor = new Color(1, 1, 1, 0.05f * open);
                p.lineWidth = 5;
                p.BeginPath();
                p.Arc(centre, r * 0.9f, 200, 245);
                p.Stroke();
                p.strokeColor = new Color(1, 1, 1, 0.03f * open);
                p.lineWidth = 2;
                p.BeginPath();
                p.Arc(centre, r * 0.84f, 210, 232);
                p.Stroke();
            };
            e.MarkDirtyRepaint();
        }

        static Color32 With(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);

        /// <summary>
        /// A band between two circles (each with its own centre) with the colour graded from the inner
        /// to the outer edge: vignettes, fringes and the off-axis shadow crescent.
        /// </summary>
        static void Ring(MeshGenerationContext ctx, Vector2 c0, float r0, Color32 col0, Vector2 c1, float r1, Color32 col1)
        {
            const int Seg = 96;
            var md = ctx.Allocate(Seg * 2, Seg * 6);
            for (int i = 0; i < Seg; i++)
            {
                float a = i * Mathf.PI * 2 / Seg;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var pi = c0 + dir * r0;
                var po = c1 + dir * r1;
                md.SetNextVertex(new Vertex { position = new Vector3(pi.x, pi.y, Vertex.nearZ), tint = col0 });
                md.SetNextVertex(new Vertex { position = new Vector3(po.x, po.y, Vertex.nearZ), tint = col1 });
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
