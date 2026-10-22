using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        /// Big meshes the rays cross (the lane's stones, plant fields, ground strips: hundreds of
        /// thousands of triangles whose bounds span the whole lane) get a search structure the first
        /// time a ray meets them: their triangles sorted into compact runs of neighbours, each run
        /// boxed, so a ray tests a few runs instead of every triangle. Built off the main thread;
        /// until it is ready the mesh is tested triangle by triangle as before. Same answers.
        /// </summary>
        const int BigMesh = 3000, Run = 48;
        static readonly Dictionary<Mesh, Accel?> accels = new Dictionary<Mesh, Accel?>();
        static readonly ConcurrentQueue<(Mesh mesh, Accel accel)> built = new ConcurrentQueue<(Mesh, Accel)>();

        sealed class Accel
        {
            public Vector3[] V = null!;
            /// <summary>Triangle corners, three per triangle, in run order.</summary>
            public int[] T = null!;
            /// <summary>Each run's box: min and max corner.</summary>
            public Vector3[] Min = null!, Max = null!;
        }

        /// <summary>Drops the search structures (the map is being unloaded).</summary>
        public static void Forget()
        {
            accels.Clear();
            while (built.TryDequeue(out _)) { }
            filters = null;
            listed = null;
        }

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
            while (built.TryDequeue(out var b)) if (accels.ContainsKey(b.mesh)) accels[b.mesh] = b.accel;
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
            best = max;
            bool hit = false;
            if (Accelerated(mesh) is Accel acc) return Search(acc, o, d, max, out best);
            mesh.GetVertices(verts);
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

        /// <summary>The mesh's search structure when it is ready; starts building one for a big mesh.</summary>
        static Accel? Accelerated(Mesh mesh)
        {
            if (accels.TryGetValue(mesh, out var a)) return a;
            accels[mesh] = null;
            int count = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                if (mesh.GetTopology(sub) == MeshTopology.Triangles) count += (int)mesh.GetIndexCount(sub) / 3;
            if (count < BigMesh) return null;
            // Copied here (Unity's mesh API is main-thread only), sorted and boxed in the background.
            var v = mesh.vertices;
            var all = new List<int>(count * 3);
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                mesh.GetTriangles(tris, sub);
                all.AddRange(tris);
            }
            var t = all.ToArray();
            Task.Run(() =>
            {
                try { built.Enqueue((mesh, Build(v, t))); }
                catch (System.Exception e) { Debug.LogWarning("[Pick] " + e.Message); }
            });
            return null;
        }

        /// <summary>Sorts triangles along a Morton curve of their centres, so each run of `Run` is a tight cluster, and boxes the runs.</summary>
        static Accel Build(Vector3[] v, int[] t)
        {
            int n = t.Length / 3;
            var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var hi = -lo;
            for (int i = 0; i < v.Length; i++) { lo = Vector3.Min(lo, v[i]); hi = Vector3.Max(hi, v[i]); }
            var size = hi - lo;
            var keys = new ulong[n];
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                var c = (v[t[3 * i]] + v[t[3 * i + 1]] + v[t[3 * i + 2]]) / 3;
                uint qx = Quant(c.x, lo.x, size.x), qy = Quant(c.y, lo.y, size.y), qz = Quant(c.z, lo.z, size.z);
                keys[i] = Spread(qx) | Spread(qy) << 1 | Spread(qz) << 2;
                order[i] = i;
            }
            System.Array.Sort(keys, order);
            var sorted = new int[t.Length];
            for (int i = 0; i < n; i++)
            {
                int k = order[i];
                sorted[3 * i] = t[3 * k];
                sorted[3 * i + 1] = t[3 * k + 1];
                sorted[3 * i + 2] = t[3 * k + 2];
            }
            int runs = (n + Run - 1) / Run;
            var min = new Vector3[runs];
            var max = new Vector3[runs];
            for (int r = 0; r < runs; r++)
            {
                var a = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var b = -a;
                int end = Mathf.Min(n, (r + 1) * Run) * 3;
                for (int i = r * Run * 3; i < end; i++) { a = Vector3.Min(a, v[sorted[i]]); b = Vector3.Max(b, v[sorted[i]]); }
                min[r] = a;
                max[r] = b;
            }
            return new Accel { V = v, T = sorted, Min = min, Max = max };
        }

        static uint Quant(float x, float lo, float size) => size > 1e-6f ? (uint)Mathf.Clamp((x - lo) / size * 1023, 0, 1023) : 0;

        /// <summary>A 10-bit number's bits spread three apart, for interleaving.</summary>
        static ulong Spread(uint x)
        {
            ulong v = x & 0x3ff;
            v = (v | v << 16) & 0x30000ff;
            v = (v | v << 8) & 0x300f00f;
            v = (v | v << 4) & 0x30c30c3;
            v = (v | v << 2) & 0x9249249;
            return v;
        }

        /// <summary>The nearest triangle along the ray (mesh space) in the runs whose boxes it crosses.</summary>
        static bool Search(Accel a, Vector3 o, Vector3 d, float max, out float best)
        {
            best = max;
            bool hit = false;
            // An axis the ray runs square to gets a vanishing slope rather than a division by zero.
            static float Inv(float c) => 1 / (Mathf.Abs(c) > 1e-12f ? c : 1e-12f);
            var inv = new Vector3(Inv(d.x), Inv(d.y), Inv(d.z));
            int runs = a.Min.Length, n = a.T.Length;
            var v = a.V;
            var t = a.T;
            for (int r = 0; r < runs; r++)
            {
                if (!Slab(a.Min[r], a.Max[r], o, inv, best)) continue;
                int end = Mathf.Min(n, (r + 1) * Run * 3);
                for (int i = r * Run * 3; i < end; i += 3)
                {
                    var p0 = v[t[i]];
                    var e1 = v[t[i + 1]] - p0;
                    var e2 = v[t[i + 2]] - p0;
                    var p = Vector3.Cross(d, e2);
                    float det = Vector3.Dot(e1, p);
                    if (det > -1e-9f && det < 1e-9f) continue;
                    float id = 1 / det;
                    var s = o - p0;
                    float u = Vector3.Dot(s, p) * id;
                    if (u < 0 || u > 1) continue;
                    var q = Vector3.Cross(s, e1);
                    float w = Vector3.Dot(d, q) * id;
                    if (w < 0 || u + w > 1) continue;
                    float tt = Vector3.Dot(e2, q) * id;
                    if (tt > 0.5f && tt < best)
                    {
                        best = tt;
                        hit = true;
                    }
                }
            }
            return hit;
        }

        /// <summary>True when the ray (origin, 1/direction) meets the box before `far`.</summary>
        static bool Slab(Vector3 min, Vector3 max, Vector3 o, Vector3 inv, float far)
        {
            float t1 = (min.x - o.x) * inv.x, t2 = (max.x - o.x) * inv.x;
            float tmin = Mathf.Min(t1, t2), tmax = Mathf.Max(t1, t2);
            t1 = (min.y - o.y) * inv.y; t2 = (max.y - o.y) * inv.y;
            tmin = Mathf.Max(tmin, Mathf.Min(t1, t2)); tmax = Mathf.Min(tmax, Mathf.Max(t1, t2));
            t1 = (min.z - o.z) * inv.z; t2 = (max.z - o.z) * inv.z;
            tmin = Mathf.Max(tmin, Mathf.Min(t1, t2)); tmax = Mathf.Min(tmax, Mathf.Max(t1, t2));
            return tmax >= Mathf.Max(tmin, 0) && tmin <= far;
        }
    }
}
