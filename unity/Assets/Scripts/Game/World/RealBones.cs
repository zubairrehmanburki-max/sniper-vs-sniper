using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Dresses the X-ray's procedural bones in a real skeleton (the CC-BY "Male Skeleton" model):
    /// its rig is turned joint by joint to the figure's pose, the posed mesh is baked into the world,
    /// and each triangle goes to the nearest procedural bone. That bone then draws the real mesh
    /// in place of its own, and snaps it in two at the hit; the procedural shapes stay behind,
    /// unseen, for the bullet's hit tests. Without the model the procedural skeleton shows as before.
    /// </summary>
    static class RealBones
    {
        public const string ModelId = "anatomy_skeleton";

        static readonly Regex Suffix = new Regex(@"_\d+$");

        /// <summary>The figure's joint for each of the skeleton's (DAZ names, first match wins).</summary>
        static readonly (string fig, string[] names)[] Joints =
        {
            ("Hips", new[] { "hip", "pelvis" }),
            ("Spine", new[] { "abdomen", "abdomenLower" }),
            ("Spine2", new[] { "chest", "chestUpper", "chestLower" }),
            ("Neck", new[] { "neck", "neckLower" }),
            ("Head", new[] { "head" }),
            ("RightShoulder", new[] { "rCollar" }),
            ("RightArm", new[] { "rShldr", "rShldrBend" }),
            ("RightForeArm", new[] { "rForeArm", "rForearmBend" }),
            ("RightHand", new[] { "rHand" }),
            ("LeftShoulder", new[] { "lCollar" }),
            ("LeftArm", new[] { "lShldr", "lShldrBend" }),
            ("LeftForeArm", new[] { "lForeArm", "lForearmBend" }),
            ("LeftHand", new[] { "lHand" }),
            ("RightUpLeg", new[] { "rThigh", "rThighBend" }),
            ("RightLeg", new[] { "rShin" }),
            ("RightFoot", new[] { "rFoot" }),
            ("RightToeBase", new[] { "rToe", "rToes" }),
            ("LeftUpLeg", new[] { "lThigh", "lThighBend" }),
            ("LeftLeg", new[] { "lShin" }),
            ("LeftFoot", new[] { "lFoot" }),
            ("LeftToeBase", new[] { "lToe", "lToes" }),
        };

        /// <summary>Chains turned in order, each joint aimed at the next.</summary>
        static readonly string[][] Chains =
        {
            new[] { "Hips", "Spine", "Spine2", "Neck", "Head" },
            new[] { "RightShoulder", "RightArm", "RightForeArm", "RightHand" },
            new[] { "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand" },
            new[] { "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase" },
            new[] { "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase" },
        };

        static readonly string[] Needed = { "Hips", "Neck", "Head", "RightArm", "LeftArm", "RightUpLeg", "LeftUpLeg", "RightLeg", "LeftLeg" };

        /// <summary>A bone part's shape as capsules and spheres, for finding the nearest one.</summary>
        sealed class Probe
        {
            public Anatomy.Part Part = null!;
            public readonly List<(Vector3 a, Vector3 b, float r)> Segs = new List<(Vector3, Vector3, float)>();
            public Vector3 C;
            public float R;

            public float Distance(Vector3 p)
            {
                float best = float.MaxValue;
                foreach (var (a, b, r) in Segs)
                {
                    var e = b - a;
                    float t = e.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, e) / e.sqrMagnitude) : 0;
                    best = Mathf.Min(best, (a + e * t - p).magnitude - r);
                }
                return best;
            }
        }

        /// <summary>Gives the figure's bone parts real meshes; false (nothing changed) when the model or its rig is missing.</summary>
        public static bool Dress(Anatomy a, Figure fig)
        {
            var prefab = Library.Model(ModelId);
            if (prefab == null) return false;
            var inst = Object.Instantiate(prefab);
            inst.name = "Real skeleton";
            try
            {
                foreach (var anim in inst.GetComponentsInChildren<Animation>(true)) anim.enabled = false;
                foreach (var anim in inst.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
                var joints = Find(inst.transform);
                foreach (var n in Needed)
                    if (!joints.ContainsKey(n) || !fig.Bones.ContainsKey(n))
                    {
                        Debug.LogWarning($"[RealBones] no {n} joint; skeleton joints: {string.Join(", ", Names(inst.transform))}");
                        return false;
                    }
                Pose(inst.transform, joints, fig);
                Bake(a, inst);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        static Dictionary<string, Transform> Find(Transform root)
        {
            var byName = new Dictionary<string, Transform>();
            // The skin's own bones first, so a mesh node sharing a joint's name can't stand in for it.
            var all = new List<Transform>();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var b in skin.bones)
                    if (b != null) all.Add(b);
            all.AddRange(root.GetComponentsInChildren<Transform>(true));
            foreach (var t in all)
            {
                string n = Suffix.Replace(t.name, "");
                if (!byName.ContainsKey(n)) byName[n] = t;
            }
            var found = new Dictionary<string, Transform>();
            foreach (var (f, names) in Joints)
                foreach (var n in names)
                    if (byName.TryGetValue(n, out var t)) { found[f] = t; break; }
            return found;
        }

        static IEnumerable<string> Names(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) yield return t.name;
        }

        /// <summary>Scales, turns and moves the skeleton onto the figure, then aims each limb bone along the figure's.</summary>
        static void Pose(Transform root, Dictionary<string, Transform> s, Figure fig)
        {
            Vector3 F(string n) => fig.Bones[n].position;
            Vector3 S(string n) => s[n].position;

            float Size(System.Func<string, Vector3> p) =>
                (p("Neck") - p("Hips")).magnitude + (p("RightLeg") - p("RightUpLeg")).magnitude + (p("LeftLeg") - p("LeftUpLeg")).magnitude;
            float now = Size(S);
            if (now > 1e-4f) root.localScale *= Size(F) / now;
            Debug.Log($"[RealBones] skeleton size {now:F3} -> {Size(F):F3}, scale {root.localScale.x:F4}");

            // The whole skeleton turned so its trunk and hips line up with the figure's.
            static Quaternion Frame(Vector3 up, Vector3 right)
            {
                var fwd = Vector3.Cross(right, up);
                return fwd.sqrMagnitude > 1e-10f ? Quaternion.LookRotation(fwd, up) : Quaternion.identity;
            }
            var rest = Frame(S("Neck") - S("Hips"), S("RightUpLeg") - S("LeftUpLeg"));
            var want = Frame(F("Neck") - F("Hips"), F("RightUpLeg") - F("LeftUpLeg"));
            root.rotation = want * Quaternion.Inverse(rest) * root.rotation;
            root.position += F("Hips") - S("Hips");

            foreach (var chain in Chains)
                for (int i = 0; i + 1 < chain.Length; i++)
                {
                    string j = chain[i], c = chain[i + 1];
                    if (j == "Hips" || !s.ContainsKey(j) || !s.ContainsKey(c) || !fig.Bones.ContainsKey(j) || !fig.Bones.ContainsKey(c)) continue;
                    var from = S(c) - S(j);
                    var to = F(c) - F(j);
                    if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) continue;
                    s[j].rotation = Quaternion.FromToRotation(from, to) * s[j].rotation;
                }
            Debug.Log($"[RealBones] posed: head off by {(S("Head") - F("Head")).magnitude:F3} m, right hand {(S("RightHand") - F("RightHand")).magnitude:F3} m, right foot {(s.ContainsKey("RightFoot") && fig.Bones.ContainsKey("RightFoot") ? (S("RightFoot") - F("RightFoot")).magnitude : -1):F3} m");
        }

        static Dictionary<string, Material> mats = new Dictionary<string, Material>();

        /// <summary>The X-ray bone glow over the model's own bone texture.</summary>
        static Material Mat(Anatomy a, Material? src)
        {
            string key = src != null ? src.name + src.GetInstanceID() : "";
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(a.Bone) { name = "Real bone " + (src != null ? src.name : "") };
            var pbr = Library.Pbr(src);
            var tex = pbr.GetTexture("_BaseMap");
            if (tex != null && tex != Texture2D.whiteTexture)
            {
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", new Color(1.05f, 1.02f, 0.96f));
                var nor = pbr.GetTexture("_BumpMap");
                if (nor != null)
                {
                    m.SetTexture("_BumpMap", nor);
                    m.EnableKeyword("_NORMALMAP");
                }
            }
            mats[key] = m;
            return m;
        }

        /// <summary>Per part, its triangles gathered so far: vertex lists and an index list per material.</summary>
        sealed class Gather
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<Material> Mats = new List<Material>();
            public readonly List<List<int>> Tris = new List<List<int>>();
            public readonly Dictionary<int, int> Remap = new Dictionary<int, int>();
            public int Source = -1;
        }

        static void Bake(Anatomy a, GameObject inst)
        {
            var probes = new List<Probe>();
            foreach (var p in a.Parts)
            {
                if (p.Kind != Anatomy.Kind.Bone || p.Name == "Sockets" || p.Name == "Cartilage") continue;
                var pr = new Probe { Part = p };
                foreach (var t in p.Tubes)
                {
                    var prev = t.Curve.At(0);
                    for (int i = 1; i <= 8; i++)
                    {
                        var q = t.Curve.At(i / 8f);
                        pr.Segs.Add((prev, q, t.Radius((i - 0.5f) / 8f) * Mathf.Max(1, t.Flat)));
                        prev = q;
                    }
                }
                foreach (var b in p.Blobs)
                    pr.Segs.Add((b.C, b.C, Mathf.Min(b.Radii.x, Mathf.Min(b.Radii.y, b.Radii.z))));
                if (pr.Segs.Count == 0) continue;
                var c = Vector3.zero;
                foreach (var (sa, sb, _) in pr.Segs) c += (sa + sb) * 0.5f;
                pr.C = c / pr.Segs.Count;
                foreach (var (sa, sb, sr) in pr.Segs) pr.R = Mathf.Max(pr.R, Mathf.Max((sa - pr.C).magnitude, (sb - pr.C).magnitude) + sr);
                probes.Add(pr);
            }
            if (probes.Count == 0) return;

            Probe Nearest(Vector3 x)
            {
                Probe best = probes[0];
                float bd = float.MaxValue;
                foreach (var pr in probes)
                {
                    // Can't beat the best so far from outside its bounding ball.
                    if ((x - pr.C).magnitude - pr.R > bd) continue;
                    float d = pr.Distance(x);
                    if (d < bd) { bd = d; best = pr; }
                }
                return best;
            }

            var body = Vector3.zero;
            foreach (var pr in probes) body += pr.C;
            body /= probes.Count;

            var gathered = new Dictionary<Anatomy.Part, Gather>();
            int source = 0, tris = 0;
            var held = new List<GameObject>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                Mesh? mesh;
                Matrix4x4 m;
                if (r is SkinnedMeshRenderer skin)
                {
                    if (skin.sharedMesh == null) continue;
                    mesh = new Mesh();
                    skin.BakeMesh(mesh, false);
                    // Unity versions differ on whether the bake carries the renderer's scale:
                    // take whichever placement lands the mesh round the body.
                    var b = mesh.bounds;
                    var full = skin.transform.localToWorldMatrix;
                    var unscaled = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                    m = (full.MultiplyPoint3x4(b.center) - body).sqrMagnitude <= (unscaled.MultiplyPoint3x4(b.center) - body).sqrMagnitude ? full : unscaled;
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (!mf.sharedMesh.isReadable)
                    {
                        // Can't be split: kept whole as it stands (teeth, small rigid pieces).
                        held.Add(r.gameObject);
                        continue;
                    }
                    mesh = mf.sharedMesh;
                    m = r.transform.localToWorldMatrix;
                }
                source++;
                var v = mesh.vertices;
                var nrm = mesh.normals;
                var uv = mesh.uv;
                for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                var nm = m.inverse.transpose;
                if (nrm.Length == v.Length)
                    for (int i = 0; i < nrm.Length; i++) nrm[i] = nm.MultiplyVector(nrm[i]).normalized;
                var srcMats = r.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var mat = Mat(a, sub < srcMats.Length ? srcMats[sub] : null);
                    var idx = mesh.GetTriangles(sub);
                    for (int t = 0; t + 2 < idx.Length; t += 3)
                    {
                        var pr = Nearest((v[idx[t]] + v[idx[t + 1]] + v[idx[t + 2]]) / 3);
                        if (!gathered.TryGetValue(pr.Part, out var g)) gathered[pr.Part] = g = new Gather();
                        if (g.Source != source) { g.Source = source; g.Remap.Clear(); }
                        int mi = g.Mats.IndexOf(mat);
                        if (mi < 0) { g.Mats.Add(mat); g.Tris.Add(new List<int>()); mi = g.Mats.Count - 1; }
                        for (int k = 0; k < 3; k++)
                        {
                            int o = idx[t + k];
                            if (!g.Remap.TryGetValue(o, out int ni))
                            {
                                ni = g.V.Count;
                                g.V.Add(v[o]);
                                g.N.Add(nrm.Length == v.Length ? nrm[o] : Vector3.up);
                                g.U.Add(uv.Length == v.Length ? uv[o] : Vector2.zero);
                                g.Remap[o] = ni;
                            }
                            g.Tris[mi].Add(ni);
                        }
                        tris++;
                    }
                }
                if (r is SkinnedMeshRenderer) Object.DestroyImmediate(mesh);
            }

            // A real skeleton lined up on the body spreads over most bones; one that didn't land
            // there piles onto a few. Then the procedural bones stay as they are.
            if (gathered.Count < probes.Count / 3)
            {
                Debug.LogWarning($"[RealBones] {tris} triangles fell on only {gathered.Count} of {probes.Count} bones; keeping the procedural skeleton");
                return;
            }
            foreach (var p in a.Parts)
            {
                if (p.Kind != Anatomy.Kind.Bone) continue;
                if (gathered.TryGetValue(p, out var g))
                {
                    var mesh = new Mesh { name = p.Name + " (real)" };
                    if (g.V.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.SetVertices(g.V);
                    mesh.SetNormals(g.N);
                    mesh.SetUVs(0, g.U);
                    mesh.subMeshCount = g.Tris.Count;
                    for (int i = 0; i < g.Tris.Count; i++) mesh.SetTriangles(g.Tris[i], i);
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    p.Real = mesh;
                    p.RealMats = g.Mats.ToArray();
                    var go = p.Build(a.Root, 0, Vector3.zero, Vector3.zero, 0);
                    if (go != null)
                    {
                        if (p.Go != null) Object.Destroy(p.Go);
                        p.Go = go;
                    }
                }
                // Bones the real skeleton covers without them (and the procedural eye sockets) stay
                // only as hit shapes; they still break into procedural halves when struck.
                else if (p.Go != null) p.Go.SetActive(false);
            }
            foreach (var go in held)
            {
                go.transform.SetParent(a.Root, true);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var sm = r.sharedMaterials;
                    for (int i = 0; i < sm.Length; i++) sm[i] = Mat(a, sm[i]);
                    r.sharedMaterials = sm;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            Debug.Log($"[RealBones] {tris} triangles over {gathered.Count} bones, {held.Count} kept whole");
        }
    }
}
