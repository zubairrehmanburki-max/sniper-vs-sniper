using System;
using System.Collections.Generic;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Smooth organic shapes for MeshBuilder: lofts through rings of points, tubes along curves with a
    /// radius profile, and deformed ellipsoids. Used for the X-ray anatomy and the bullet.
    /// </summary>
    public static class Shapes
    {
        /// <summary>A Catmull-Rom curve through control points, sampled by arc length (u from 0 to 1).</summary>
        public sealed class Curve
        {
            readonly List<Vector3> pts = new List<Vector3>();
            readonly List<float> len = new List<float>();
            readonly int per;

            public float Length => len[len.Count - 1];

            public Curve(IReadOnlyList<Vector3> ctrl, int per = 12)
            {
                this.per = per;
                if (ctrl.Count == 1) pts.Add(ctrl[0]);
                for (int i = 0; i < ctrl.Count - 1; i++)
                {
                    var p0 = i > 0 ? ctrl[i - 1] : 2 * ctrl[i] - ctrl[i + 1];
                    var p3 = i + 2 < ctrl.Count ? ctrl[i + 2] : 2 * ctrl[i + 1] - ctrl[i];
                    for (int s = 0; s < per; s++) pts.Add(CatmullRom(p0, ctrl[i], ctrl[i + 1], p3, s / (float)per));
                }
                if (ctrl.Count > 1) pts.Add(ctrl[ctrl.Count - 1]);
                len.Add(0);
                for (int i = 1; i < pts.Count; i++) len.Add(len[i - 1] + Vector3.Distance(pts[i - 1], pts[i]));
            }

            static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
            {
                float t2 = t * t, t3 = t2 * t;
                return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
            }

            public Vector3 At(float u)
            {
                if (pts.Count == 1 || Length < 1e-6f) return pts[0];
                float d = Mathf.Clamp01(u) * Length;
                int lo = 0, hi = len.Count - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (len[mid] < d) lo = mid;
                    else hi = mid;
                }
                float span = len[hi] - len[lo];
                return Vector3.Lerp(pts[lo], pts[hi], span > 1e-7f ? (d - len[lo]) / span : 0);
            }

            public Vector3 Tangent(float u)
            {
                float e = Mathf.Min(0.01f, 0.004f / Mathf.Max(1e-4f, Length));
                var d = At(Mathf.Min(1, u + e)) - At(Mathf.Max(0, u - e));
                if (d.sqrMagnitude < 1e-12f) d = pts[pts.Count - 1] - pts[0];
                return d.sqrMagnitude < 1e-12f ? Vector3.up : d.normalized;
            }

            /// <summary>Where control point `i` falls along the curve.</summary>
            public float UOf(int i) => Length < 1e-6f ? 0 : len[Mathf.Clamp(i * per, 0, len.Count - 1)] / Length;

            /// <summary>The u of the point on the curve nearest `p`.</summary>
            public float Nearest(Vector3 p)
            {
                float best = 0, bestD = float.MaxValue;
                for (int i = 0; i <= 64; i++)
                {
                    float u = i / 64f, d = (At(u) - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = u; }
                }
                // Refine between the neighbouring samples.
                for (int i = -8; i <= 8; i++)
                {
                    float u = Mathf.Clamp01(best + i / 512f), d = (At(u) - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = u; }
                }
                return best;
            }
        }

        /// <summary>
        /// A smooth surface through rings of points (each ring the same count, running round in the
        /// same sense), optionally closed at either end by a centre point. Normals are averaged from
        /// the faces; rings shrunk to a point (the poles of a blob) take the axis direction.
        /// </summary>
        public static void Loft(MeshBuilder mb, Vector3[][] rings, Vector3? capA, Vector3? capB)
        {
            int R = rings.Length, S = rings[0].Length;
            var v = new List<Vector3>(R * S + 2);
            foreach (var ring in rings) v.AddRange(ring);
            var tris = new List<int>(R * S * 6 + S * 6);
            for (int k = 0; k < R - 1; k++)
            {
                for (int j = 0; j < S; j++)
                {
                    int a = k * S + j, b = k * S + (j + 1) % S, c = (k + 1) * S + (j + 1) % S, d = (k + 1) * S + j;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(d);
                }
            }
            if (capA.HasValue)
            {
                int ca = v.Count;
                v.Add(capA.Value);
                for (int j = 0; j < S; j++) { tris.Add(ca); tris.Add((j + 1) % S); tris.Add(j); }
            }
            if (capB.HasValue)
            {
                int cb = v.Count, b0 = (R - 1) * S;
                v.Add(capB.Value);
                for (int j = 0; j < S; j++) { tris.Add(cb); tris.Add(b0 + j); tris.Add(b0 + (j + 1) % S); }
            }

            // Wind every face outward (Unity's front faces), whichever way the rings ran.
            var centroid = Vector3.zero;
            foreach (var p in v) centroid += p;
            centroid /= v.Count;
            float sign = 0;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                sign += Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3 - centroid);
            }
            if (sign < 0)
                for (int i = 0; i < tris.Count; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);

            var n = new Vector3[v.Count];
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                var fn = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                n[a] += fn; n[b] += fn; n[c] += fn;
            }
            for (int k = 0; k < R; k++)
            {
                if ((rings[k][0] - rings[k][S / 2]).sqrMagnitude > 1e-12f || R < 2) continue;
                var nb = rings[k == 0 ? 1 : k - 1];
                var mid = Vector3.zero;
                foreach (var p in nb) mid += p;
                var axis = rings[k][0] - mid / S;
                for (int j = 0; j < S; j++) n[k * S + j] = axis;
            }
            for (int i = 0; i < n.Length; i++) n[i] = n[i].sqrMagnitude > 1e-20f ? n[i].normalized : Vector3.up;
            mb.Smooth(v, n, tris);
        }

        /// <summary>
        /// An ellipsoid with the given radii (local x, y, z) turned by `rot`, its poles on local y.
        /// `deform` reshapes points of the unit sphere before scaling: tapers, notches, flattening.
        /// </summary>
        public static void Ellipsoid(MeshBuilder mb, Vector3 centre, Quaternion rot, Vector3 radii, Func<Vector3, Vector3>? deform = null, int rings = 12, int sides = 16)
        {
            var grid = new Vector3[rings][];
            for (int k = 0; k < rings; k++)
            {
                float th = Mathf.PI * k / (rings - 1);
                grid[k] = new Vector3[sides];
                for (int j = 0; j < sides; j++)
                {
                    float ph = 2 * Mathf.PI * j / sides;
                    var s = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    if (k == 0) s = Vector3.up;
                    else if (k == rings - 1) s = Vector3.down;
                    if (deform != null) s = deform(s);
                    grid[k][j] = centre + rot * Vector3.Scale(s, radii);
                }
            }
            Loft(mb, grid, null, null);
        }

        /// <summary>Two perpendicular unit vectors across `t`, the first as near `hint` as it can be.</summary>
        public static (Vector3 n, Vector3 b) Across(Vector3 t, Vector3 hint)
        {
            var n = Vector3.ProjectOnPlane(hint, t);
            if (n.sqrMagnitude < 1e-8f) n = Vector3.ProjectOnPlane(Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right, t);
            n.Normalize();
            return (n, Vector3.Cross(t, n));
        }
    }
}
