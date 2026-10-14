using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Accumulates boxes, cylinders and quads into one mesh. UVs are in metres (scaled by uvScale),
    /// so tiling PBR textures keep a real-world size whatever the shape.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();
        public float UvScale = 0.25f;

        public int VertexCount => v.Count;

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float uLen, float vLen, Vector2 uvOrigin)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int k = 0; k < 4; k++) n.Add(normal);
            uv.Add(uvOrigin * UvScale);
            uv.Add((uvOrigin + new Vector2(uLen, 0)) * UvScale);
            uv.Add((uvOrigin + new Vector2(uLen, vLen)) * UvScale);
            uv.Add((uvOrigin + new Vector2(0, vLen)) * UvScale);
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }

        /// <summary>Axis-aligned box (optionally rotated about its centre).</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion? rotation = null)
        {
            var r = rotation ?? Quaternion.identity;
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + r * new Vector3(x * h.x, y * h.y, z * h.z);
            var o = new Vector2(center.x + center.z, center.y);
            // +Y top, −Y bottom, ±X, ±Z. Vertices wind clockwise seen from outside (Unity front faces).
            Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), r * Vector3.up, size.x, size.z, new Vector2(center.x, center.z));
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), r * Vector3.down, size.x, size.z, new Vector2(center.x, center.z));
            Quad(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), r * Vector3.right, size.z, size.y, o);
            Quad(P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), r * Vector3.left, size.z, size.y, o);
            Quad(P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), r * Vector3.forward, size.x, size.y, o);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), r * Vector3.back, size.x, size.y, o);
        }

        /// <summary>A box stretched between two points (rails, cables, beams).</summary>
        public void Beam(Vector3 from, Vector3 to, float width, float height)
        {
            var d = to - from;
            if (d.sqrMagnitude < 1e-6f) return;
            var rot = Quaternion.LookRotation(d.normalized, Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
            Box((from + to) * 0.5f, new Vector3(width, height, d.magnitude), rot);
        }

        /// <summary>Vertical cylinder (open ends are capped).</summary>
        public void Cylinder(Vector3 baseCenter, float radius, float height, int sides = 12)
        {
            float circ = 2 * Mathf.PI * radius;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * 2 * Mathf.PI / sides, a1 = (s + 1) * 2 * Mathf.PI / sides;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
                var nm = new Vector3(Mathf.Cos((a0 + a1) / 2), 0, Mathf.Sin((a0 + a1) / 2));
                var up = new Vector3(0, height, 0);
                Quad(baseCenter + p0, baseCenter + p1, baseCenter + p1 + up, baseCenter + p0 + up, nm, circ / sides, height, new Vector2(circ * s / sides, baseCenter.y));
                // caps
                int i = v.Count;
                v.Add(baseCenter + up); v.Add(baseCenter + p0 + up); v.Add(baseCenter + p1 + up);
                n.Add(Vector3.up); n.Add(Vector3.up); n.Add(Vector3.up);
                uv.Add(Vector2.zero); uv.Add(new Vector2(p0.x, p0.z) * UvScale); uv.Add(new Vector2(p1.x, p1.z) * UvScale);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
            }
        }

        /// <summary>Flat quad facing up (+Y), centred, for water and decals.</summary>
        public void Plane(Vector3 center, float sizeX, float sizeZ)
        {
            var h = new Vector3(sizeX / 2, 0, sizeZ / 2);
            Quad(center + new Vector3(-h.x, 0, -h.z), center + new Vector3(h.x, 0, -h.z), center + new Vector3(h.x, 0, h.z), center + new Vector3(-h.x, 0, h.z),
                Vector3.up, sizeX, sizeZ, new Vector2(center.x - h.x, center.z - h.z));
        }

        /// <summary>Adds a quad with its own per-vertex normals; the winding is fixed up to face the normals.</summary>
        void QuadN(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector2 uva, Vector2 uvb, Vector2 uvc, Vector2 uvd)
        {
            var mean = na + nb + nc + nd;
            if (Vector3.Dot(Vector3.Cross(c - a, b - a), mean) < 0)
            {
                (b, d) = (d, b);
                (nb, nd) = (nd, nb);
                (uvb, uvd) = (uvd, uvb);
            }
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(na); n.Add(nb); n.Add(nc); n.Add(nd);
            uv.Add(uva * UvScale); uv.Add(uvb * UvScale); uv.Add(uvc * UvScale); uv.Add(uvd * UvScale);
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }

        /// <summary>Adds one triangle; the winding is fixed up to face the normals.</summary>
        void Tri(Vector3 p, Vector3 q, Vector3 r, Vector3 np, Vector3 nq, Vector3 nr, Vector2 uvp, Vector2 uvq, Vector2 uvr)
        {
            if (Vector3.Dot(Vector3.Cross(q - p, r - p), np + nq + nr) < 0)
            {
                (q, r) = (r, q);
                (nq, nr) = (nr, nq);
                (uvq, uvr) = (uvr, uvq);
            }
            int i = v.Count;
            v.Add(p); v.Add(q); v.Add(r);
            n.Add(np); n.Add(nq); n.Add(nr);
            uv.Add(uvp * UvScale); uv.Add(uvq * UvScale); uv.Add(uvr * UvScale);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        /// <summary>A flat four-sided face in any orientation, facing `outward` (flat shaded).</summary>
        public void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            var nm = Vector3.Cross(c - a, b - a);
            if (nm.sqrMagnitude < 1e-10f) nm = Vector3.Cross(d - a, c - a);
            nm = nm.normalized;
            if (Vector3.Dot(nm, outward) < 0) nm = -nm;
            float uLen = Vector3.Distance(a, b), vLen = Vector3.Distance(b, c);
            var o = new Vector2(a.x + a.z, a.y);
            QuadN(a, b, c, d, nm, nm, nm, nm, o, o + new Vector2(uLen, 0), o + new Vector2(uLen, vLen), o + new Vector2(0, vLen));
        }

        /// <summary>
        /// A box whose top and bottom are axis-aligned rectangles of different sizes and centres: a
        /// tapered column, a battered leg, a plinth with sloping sides.
        /// </summary>
        public void Frustum(Vector3 bottom, Vector2 bottomSize, Vector3 top, Vector2 topSize)
        {
            Vector3 P(int sx, int sy, int sz)
            {
                var c = sy > 0 ? top : bottom;
                var h = (sy > 0 ? topSize : bottomSize) * 0.5f;
                return c + new Vector3(sx * h.x, 0, sz * h.y);
            }
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 o)
            {
                var nm = Vector3.Cross(c - a, b - a).normalized;
                float uLen = Vector3.Distance(a, b), vLen = Vector3.Distance(b, c);
                QuadN(a, b, c, d, nm, nm, nm, nm, o, o + new Vector2(uLen, 0), o + new Vector2(uLen, vLen), o + new Vector2(0, vLen));
            }
            var side = new Vector2(bottom.x + bottom.z, bottom.y);
            Face(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), new Vector2(top.x, top.z));
            Face(P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), new Vector2(bottom.x, bottom.z));
            Face(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), side);
            Face(P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), side);
            Face(P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), side);
            Face(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), side);
        }

        /// <summary>
        /// A round tube between two points in any direction, tapering from r0 to r1, smooth shaded:
        /// pipes, tubular jacket members, tree trunks and branches.
        /// </summary>
        public void Tube(Vector3 from, Vector3 to, float r0, float r1, int sides = 10, bool caps = false)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            var axis = d / len;
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.99f ? Vector3.up : Vector3.forward).normalized;
            var w = Vector3.Cross(axis, u);
            float circ = Mathf.PI * (r0 + r1);
            // The surface slopes when the tube tapers, so the normals lean towards the narrow end.
            float lean = (r0 - r1) / len;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * 2 * Mathf.PI / sides, a1 = (s + 1) * 2 * Mathf.PI / sides;
                var e0 = Mathf.Cos(a0) * u + Mathf.Sin(a0) * w;
                var e1 = Mathf.Cos(a1) * u + Mathf.Sin(a1) * w;
                var n0 = (e0 + axis * lean).normalized;
                var n1 = (e1 + axis * lean).normalized;
                float u0 = circ * s / sides, u1 = circ * (s + 1) / sides;
                QuadN(from + e0 * r0, from + e1 * r0, to + e1 * r1, to + e0 * r1, n0, n1, n1, n0,
                    new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, len), new Vector2(u0, len));
                if (caps)
                {
                    Tri(to, to + e0 * r1, to + e1 * r1, axis, axis, axis, Vector2.zero, Vector2.zero, Vector2.zero);
                    Tri(from, from + e1 * r0, from + e0 * r0, -axis, -axis, -axis, Vector2.zero, Vector2.zero, Vector2.zero);
                }
            }
        }

        /// <summary>
        /// One grass or reed blade: a thin triangle from a root of the given width to a point. Drawn with a
        /// double-sided material; the normal leans up so both sides catch the sky alike.
        /// </summary>
        public void Blade(Vector3 root, Vector3 tip, float width, Vector3 facing)
        {
            var side = Vector3.Cross(tip - root, facing).normalized * (width / 2);
            var nm = (facing.normalized + Vector3.up * 1.5f).normalized;
            float h = (tip - root).magnitude;
            Tri(root - side, root + side, tip, nm, nm, nm, new Vector2(0, 0), new Vector2(width, 0), new Vector2(width / 2, h));
        }

        /// <summary>
        /// A low irregular dome on a horizontal base: a mud bank, a tussock. The outline wobbles by up
        /// to `wobble` of the radius, from `rng`, so no two are the same.
        /// </summary>
        public void Mound(Vector3 center, float rx, float rz, float height, System.Random rng, float wobble = 0.25f, int rings = 4, int segs = 14)
        {
            var k = new float[segs];
            for (int s = 0; s < segs; s++) k[s] = 1 + ((float)rng.NextDouble() * 2 - 1) * wobble;
            Vector3 Pt(int ring, int seg, out Vector3 normal)
            {
                float f = (float)ring / rings;
                float a = (seg % segs) * 2 * Mathf.PI / segs;
                float scale = Mathf.Lerp(1, k[seg % segs], f);
                float x = Mathf.Cos(a) * rx * f * scale, z = Mathf.Sin(a) * rz * f * scale;
                // y = h (1 − f²)²: flat-topped, feathering out to nothing at the rim.
                float y = height * (1 - f * f) * (1 - f * f);
                float g = -4 * height * (1 - f * f);
                normal = new Vector3(-g * x / (rx * rx), 1, -g * z / (rz * rz)).normalized;
                return center + new Vector3(x, y, z);
            }
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segs; s++)
                {
                    var a = Pt(r, s, out var na);
                    var b = Pt(r, s + 1, out var nb);
                    var c = Pt(r + 1, s + 1, out var nc);
                    var d = Pt(r + 1, s, out var nd);
                    Vector2 U(Vector3 p) => new Vector2(p.x, p.z);
                    if (r == 0) Tri(a, c, d, na, nc, nd, U(a), U(c), U(d));
                    else QuadN(a, b, c, d, na, nb, nc, nd, U(a), U(b), U(c), U(d));
                }
            }
        }

        /// <summary>A flat ring facing up (+Y), for painted circles.</summary>
        public void Annulus(Vector3 center, float rInner, float rOuter, int segs = 48)
        {
            for (int s = 0; s < segs; s++)
            {
                float a0 = s * 2 * Mathf.PI / segs, a1 = (s + 1) * 2 * Mathf.PI / segs;
                var e0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var e1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector2 U(Vector3 p) => new Vector2(p.x, p.z);
                var a = center + e0 * rInner; var b = center + e1 * rInner; var c = center + e1 * rOuter; var d = center + e0 * rOuter;
                QuadN(a, b, c, d, Vector3.up, Vector3.up, Vector3.up, Vector3.up, U(a), U(b), U(c), U(d));
            }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        public GameObject Object(string name, Material material, Transform? parent = null, bool shadows = true)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Build(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }
}
