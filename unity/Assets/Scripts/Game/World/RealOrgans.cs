using System.Collections.Generic;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Real organs for the X-ray (CC-BY Sketchfab models): each is fitted into the room its
    /// procedural organ takes in the body, turned to the body's own up, right and front, and drawn in
    /// its place. The lungs model is split down the middle for the two lungs. The procedural shapes
    /// stay for the bullet's hit tests; an organ whose model is missing keeps its procedural look.
    /// </summary>
    static class RealOrgans
    {
        /// <summary>Procedural part, model, and which half of the model (-1 its -x half, 1 its +x half, 0 whole).</summary>
        static readonly (string part, string model, int half)[] Fits =
        {
            ("Heart", "organ_heart", 0),
            ("Liver", "organ_liver", 0),
            ("Lung L", "organ_lungs", -1),
            ("Lung R", "organ_lungs", 1),
            ("Kidney L", "organ_kidney", 0),
            ("Kidney R", "organ_kidney", 0),
        };

        /// <summary>A model's triangles in its own space, gathered once.</summary>
        sealed class Source
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<(Material? mat, int[] tris)> Subs = new List<(Material?, int[])>();
        }

        static readonly Dictionary<string, Source?> sources = new Dictionary<string, Source?>();

        static Source? Load(string id)
        {
            if (sources.TryGetValue(id, out var done)) return done;
            Source? src = null;
            var prefab = Library.Model(id);
            if (prefab != null)
            {
                var inst = Object.Instantiate(prefab);
                try
                {
                    foreach (var anim in inst.GetComponentsInChildren<Animation>(true)) anim.enabled = false;
                    foreach (var anim in inst.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
                    Figure.Freeze(inst);
                    // Sketchfab wraps its exports in a node that turns the author's y-up model on
                    // its back; the organ is measured in that node's own space, the author's frame.
                    var frame = inst.transform;
                    foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith("Sketchfab_model")) { frame = t; break; }
                    var world = frame.worldToLocalMatrix;
                    src = new Source();
                    foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mesh = mf.sharedMesh;
                        if (mesh == null || !mesh.isReadable) continue;
                        var m = world * mf.transform.localToWorldMatrix;
                        var nm = m.inverse.transpose;
                        int b = src.V.Count;
                        var v = mesh.vertices;
                        var n = mesh.normals;
                        var uv = mesh.uv;
                        for (int i = 0; i < v.Length; i++)
                        {
                            src.V.Add(m.MultiplyPoint3x4(v[i]));
                            src.N.Add(n.Length == v.Length ? nm.MultiplyVector(n[i]).normalized : Vector3.up);
                            src.U.Add(uv.Length == v.Length ? uv[i] : Vector2.zero);
                        }
                        var mats = mf.GetComponent<Renderer>()?.sharedMaterials ?? new Material[0];
                        for (int s = 0; s < mesh.subMeshCount; s++)
                        {
                            var t = mesh.GetTriangles(s);
                            for (int i = 0; i < t.Length; i++) t[i] += b;
                            src.Subs.Add((s < mats.Length ? mats[s] : null, t));
                        }
                    }
                    if (src.V.Count == 0) src = null;
                }
                finally
                {
                    Object.DestroyImmediate(inst);
                }
            }
            sources[id] = src;
            return src;
        }

        public static void Dress(Anatomy a, Figure fig)
        {
            if (!fig.Bones.TryGetValue("Hips", out var hips) || !fig.Bones.TryGetValue("Neck", out var neck)
                || !fig.Bones.TryGetValue("LeftUpLeg", out var lul) || !fig.Bones.TryGetValue("RightUpLeg", out var rul)) return;
            // The body's frame: up the trunk, right across the hips, front.
            var up = (neck.position - hips.position).normalized;
            var right = Vector3.ProjectOnPlane(rul.position - lul.position, up).normalized;
            var front = Vector3.Cross(right, up);
            int fitted = 0;
            foreach (var (name, model, half) in Fits)
            {
                var p = a.Parts.Find(q => q.Name == name && q.Kind == Anatomy.Kind.Organ);
                if (p == null) continue;
                var src = Load(model);
                if (src == null) continue;
                if (Fit(a, p, src, half, right, up, front)) fitted++;
            }
            Debug.Log($"[RealOrgans] {fitted} organs fitted");
        }

        static bool Fit(Anatomy a, Anatomy.Part p, Source src, int half, Vector3 right, Vector3 up, Vector3 front)
        {
            // The model's side: the triangles on one side of its middle (for one lung), or all.
            var all = new Bounds(src.V[0], Vector3.zero);
            foreach (var x in src.V) all.Encapsulate(x);
            bool Keep(int[] t, int i) =>
                half == 0 || (((src.V[t[i]] + src.V[t[i + 1]] + src.V[t[i + 2]]) / 3).x < all.center.x) == (half < 0);
            Bounds? mb = null;
            foreach (var (_, t) in src.Subs)
                for (int i = 0; i + 2 < t.Length; i += 3)
                    if (Keep(t, i))
                        for (int k = 0; k < 3; k++)
                        {
                            var x = src.V[t[i + k]];
                            if (mb == null) mb = new Bounds(x, Vector3.zero);
                            else { var b = mb.Value; b.Encapsulate(x); mb = b; }
                        }
            if (mb == null) return false;

            // The procedural organ's room in the body's frame.
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            void Take(Vector3 w)
            {
                var q = new Vector3(Vector3.Dot(w, right), Vector3.Dot(w, up), Vector3.Dot(w, front));
                lo = Vector3.Min(lo, q);
                hi = Vector3.Max(hi, q);
            }
            foreach (var b in p.Blobs)
                for (int i = -1; i <= 1; i++)
                    for (int j = -1; j <= 1; j++)
                        for (int k = -1; k <= 1; k++)
                            if (i != 0 || j != 0 || k != 0)
                                Take(b.C + b.Rot * Vector3.Scale(new Vector3(i, j, k).normalized, b.Radii));
            foreach (var t in p.Tubes)
                for (int i = 0; i <= 8; i++)
                {
                    var c = t.Curve.At(i / 8f);
                    Take(c + right * t.MaxRadius); Take(c - right * t.MaxRadius);
                    Take(c + up * t.MaxRadius); Take(c - up * t.MaxRadius);
                    Take(c + front * t.MaxRadius); Take(c - front * t.MaxRadius);
                }
            if (lo.x > hi.x) return false;
            var room = hi - lo;
            var size = mb.Value.size;
            // One scale for all three sides (the model keeps its shape): the mean of the three fits.
            float s = Mathf.Pow(room.x / Mathf.Max(1e-4f, size.x) * room.y / Mathf.Max(1e-4f, size.y) * room.z / Mathf.Max(1e-4f, size.z), 1f / 3);
            var mid = (lo + hi) / 2;
            var centre = right * mid.x + up * mid.y + front * mid.z;
            var c0 = mb.Value.center;
            Vector3 Place(Vector3 x)
            {
                x = (x - c0) * s;
                return centre + right * x.x + up * x.y + front * x.z;
            }

            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var remap = new Dictionary<int, int>();
            var subs = new List<List<int>>();
            var mats = new List<Material>();
            foreach (var (m, t) in src.Subs)
            {
                var list = new List<int>();
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    if (!Keep(t, i)) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int o = t[i + k];
                        if (!remap.TryGetValue(o, out int ni))
                        {
                            ni = v.Count;
                            v.Add(Place(src.V[o]));
                            var sn = src.N[o];
                            n.Add((right * sn.x + up * sn.y + front * sn.z).normalized);
                            uv.Add(src.U[o]);
                            remap[o] = ni;
                        }
                        list.Add(ni);
                    }
                }
                if (list.Count == 0) continue;
                subs.Add(list);
                mats.Add(Mat(p.Material, m));
            }
            if (subs.Count == 0) return false;
            var mesh = new Mesh { name = p.Name + " (real)" };
            if (v.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            p.Real = mesh;
            p.RealMats = mats.ToArray();
            var go = p.Build(a.Root, 0, Vector3.zero, Vector3.zero, 0);
            if (go == null) return false;
            if (p.Go != null) Object.Destroy(p.Go);
            p.Go = go;
            return true;
        }

        /// <summary>The organ's X-ray glow over the model's own texture.</summary>
        static Material Mat(Material glow, Material? src)
        {
            var m = new Material(glow) { name = glow.name + " (real)" };
            var pbr = Library.Pbr(src);
            var tex = pbr.GetTexture("_BaseMap");
            if (tex != null && tex != Texture2D.whiteTexture)
            {
                m.SetTexture("_BaseMap", tex);
                // The texture carries the colour; the glow's own colour only warms it a little.
                m.SetColor("_BaseColor", Color.Lerp(Color.white, glow.GetColor("_BaseColor"), 0.25f));
                var nor = pbr.GetTexture("_BumpMap");
                if (nor != null)
                {
                    m.SetTexture("_BumpMap", nor);
                    m.EnableKeyword("_NORMALMAP");
                }
            }
            return m;
        }
    }
}
