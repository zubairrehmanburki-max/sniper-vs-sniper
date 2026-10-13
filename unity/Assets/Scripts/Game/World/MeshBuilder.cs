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
