using System.Collections.Generic;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// What a ray meets in the drawn world: the first triangle of the map's meshes along it. The
    /// rules only know the ground, the platforms and the targets, but the eye sees the far bridges,
    /// the skyline and the rest of the scenery, and the crosshair has to land on what it sees.
    /// Skinned figures are not meshes here; the rules' silhouettes stand in for them.
    /// </summary>
    public static class Pick
    {
        static readonly List<Vector3> verts = new List<Vector3>();
        static readonly List<int> tris = new List<int>();
        static readonly List<(float at, MeshFilter mesh)> crossed = new List<(float, MeshFilter)>();
        static MeshFilter[]? filters;
        static Transform? listed;
        static float listedAt = -10;
        /// <summary>What the last ray met, for the capture log.</summary>
        public static string LastHit = "";

        /// <summary>
        /// Metres along the ray to the first drawn surface under the root, or null for open sky.
        /// The skipped part (the player's own body) never counts, nor anything nearer than half a metre.
        /// </summary>
        public static float? Ray(Transform root, Vector3 origin, Vector3 dir, float max, Transform? skip = null)
        {
            dir.Normalize();
            // The list is refreshed now and then for what comes and goes (practice plates, effects).
            if (filters == null || listed != root || Time.unscaledTime - listedAt > 2)
            {
                filters = root.GetComponentsInChildren<MeshFilter>();
                listed = root;
                listedAt = Time.unscaledTime;
            }
            var ray = new Ray(origin, dir);
            crossed.Clear();
            foreach (var mf in filters)
            {
                if (mf == null || !mf.gameObject.activeInHierarchy) continue;
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                if (!mf.TryGetComponent<MeshRenderer>(out var r) || !r.enabled) continue;
                if (skip != null && mf.transform.IsChildOf(skip)) continue;
                var b = r.bounds;
                if (b.Contains(origin)) crossed.Add((0, mf));
                else if (b.IntersectRay(ray, out float at) && at < max) crossed.Add((at, mf));
            }
            crossed.Sort((a, b) => a.at.CompareTo(b.at));
            float best = max;
            bool found = false;
            LastHit = "sky";
            foreach (var (at, mf) in crossed)
            {
                if (at >= best) break;
                if (Mesh(mf, origin, dir, best, out float t))
                {
                    best = t;
                    found = true;
                    LastHit = $"{mf.name} ({mf.GetComponent<Renderer>().sharedMaterial?.name}) at {t:0.0} m";
                }
            }
            return found ? best : (float?)null;
        }

        /// <summary>The nearest triangle of one mesh along the ray, in world metres (an affine map keeps the ray's parameter).</summary>
        static bool Mesh(MeshFilter mf, Vector3 origin, Vector3 dir, float max, out float best)
        {
            var toLocal = mf.transform.worldToLocalMatrix;
            var o = toLocal.MultiplyPoint3x4(origin);
            var d = toLocal.MultiplyVector(dir);
            var mesh = mf.sharedMesh;
            mesh.GetVertices(verts);
            best = max;
            bool hit = false;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                mesh.GetTriangles(tris, sub);
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    // Möller–Trumbore, both faces.
                    var a = verts[tris[i]];
                    var e1 = verts[tris[i + 1]] - a;
                    var e2 = verts[tris[i + 2]] - a;
                    var p = Vector3.Cross(d, e2);
                    float det = Vector3.Dot(e1, p);
                    if (det > -1e-9f && det < 1e-9f) continue;
                    float inv = 1 / det;
                    var s = o - a;
                    float u = Vector3.Dot(s, p) * inv;
                    if (u < 0 || u > 1) continue;
                    var q = Vector3.Cross(s, e1);
                    float v = Vector3.Dot(d, q) * inv;
                    if (v < 0 || u + v > 1) continue;
                    float t = Vector3.Dot(e2, q) * inv;
                    if (t > 0.5f && t < best)
                    {
                        best = t;
                        hit = true;
                    }
                }
            }
            return hit;
        }
    }
}
