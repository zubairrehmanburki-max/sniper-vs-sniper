using System;
using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// A skeleton and organs built procedurally inside a posed figure for the X-ray kill cam: tapered
    /// long bones with knobbed ends, a segmented spine, curved ribs meeting a sternum, scapulae,
    /// pelvis, skull, and lungs, heart, liver, stomach and kidneys. Everything is placed from the
    /// figure's own joints, so it follows whatever pose he holds. Each part keeps its primitives so
    /// it can be hit-tested against the bullet's path and rebuilt in two broken halves.
    /// </summary>
    public sealed class Anatomy
    {
        public enum Kind { Bone, Organ }

        /// <summary>A tube along a curve: radius by u, optionally flattened across `Wide`, with lobes round its section.</summary>
        public sealed class TubeSpec
        {
            public Shapes.Curve Curve = null!;
            public Func<float, float> Radius = _ => 0.01f;
            public Vector3? Wide;
            public float Flat = 1;
            public Func<float, float, float>? Lobes;
            public bool RoundA = true, RoundB = true;
            public int Sides = 10;
            public float MaxRadius;
        }

        public sealed class BlobSpec
        {
            public Vector3 C;
            public Quaternion Rot;
            public Vector3 Radii;
            public Func<Vector3, Vector3>? Deform;
            public int Rings = 10, Sides = 14;
        }

        /// <summary>One bone or organ.</summary>
        public sealed class Part
        {
            public string Name = "";
            public Kind Kind;
            public Material Material = null!;
            public GameObject Go = null!;
            public readonly List<TubeSpec> Tubes = new List<TubeSpec>();
            public readonly List<BlobSpec> Blobs = new List<BlobSpec>();
            /// <summary>Long bones and ribs snap in two; others chip and crack.</summary>
            public bool Splits;
            /// <summary>Organs and the skull (the brain) bleed when hit.</summary>
            public bool Bleeds;
            public bool Hittable = true;
            /// <summary>The part a shot to this zone is steered through when its path misses every such part.</summary>
            public HitZone? Target;
            public Color Blood = new Color(0.55f, 0.02f, 0.02f);
            /// <summary>The real skeleton's triangles for this bone (RealBones), drawn in place of the shapes.</summary>
            public Mesh? Real;
            public Material[]? RealMats;

            public Vector3 Centre
            {
                get
                {
                    var c = Vector3.zero;
                    int n = 0;
                    foreach (var t in Tubes) { c += t.Curve.At(0.5f); n++; }
                    foreach (var b in Blobs) { c += b.C; n++; }
                    return n > 0 ? c / n : Vector3.zero;
                }
            }

            public float Size
            {
                get
                {
                    float r = 0;
                    foreach (var b in Blobs) r = Mathf.Max(r, Mathf.Min(b.Radii.x, Mathf.Min(b.Radii.y, b.Radii.z)));
                    foreach (var t in Tubes) r = Mathf.Max(r, t.MaxRadius);
                    return r;
                }
            }

            /// <summary>How far along the line from `o` (unit `d`) the bullet first touches this part; +inf if never.</summary>
            public float Hit(Vector3 o, Vector3 d)
            {
                float best = float.PositiveInfinity;
                foreach (var t in Tubes)
                {
                    var prev = t.Curve.At(0);
                    for (int i = 1; i <= 16; i++)
                    {
                        var p = t.Curve.At(i / 16f);
                        if (Capsule(o, d, prev, p, t.MaxRadius, out float s)) best = Mathf.Min(best, s);
                        prev = p;
                    }
                }
                foreach (var b in Blobs)
                    if (Ellipsoid(o, d, b, out float s)) best = Mathf.Min(best, s);
                return best;
            }

            /// <summary>Where the line o + s·d (unit d) first comes within r of the segment a–b.</summary>
            static bool Capsule(Vector3 o, Vector3 d, Vector3 a, Vector3 b, float r, out float s)
            {
                var e = b - a;
                var w0 = o - a;
                float ee = Vector3.Dot(e, e), de = Vector3.Dot(d, e), dw = Vector3.Dot(d, w0), ew = Vector3.Dot(e, w0);
                float denom = ee - de * de;
                float t = denom > 1e-9f ? Mathf.Clamp01((ew - dw * de) / denom) : 0;
                s = t * de - dw;
                float dist = (w0 + s * d - t * e).magnitude;
                if (dist > r) return false;
                s -= Mathf.Sqrt(r * r - dist * dist);
                return true;
            }

            static bool Ellipsoid(Vector3 o, Vector3 d, BlobSpec b, out float s)
            {
                s = 0;
                var inv = Quaternion.Inverse(b.Rot);
                var lo = inv * (o - b.C);
                var ld = inv * d;
                lo = new Vector3(lo.x / b.Radii.x, lo.y / b.Radii.y, lo.z / b.Radii.z);
                ld = new Vector3(ld.x / b.Radii.x, ld.y / b.Radii.y, ld.z / b.Radii.z);
                float qa = Vector3.Dot(ld, ld), qb = 2 * Vector3.Dot(lo, ld), qc = Vector3.Dot(lo, lo) - 1;
                float disc = qb * qb - 4 * qa * qc;
                if (disc < 0 || qa < 1e-12f) return false;
                s = (-qb - Mathf.Sqrt(disc)) / (2 * qa);
                return true;
            }

            /// <summary>
            /// The part's mesh: whole (half 0), or the half on the near (1) or far (2) side of the
            /// plane through `hit` with normal `plane`, its tubes snapped at the hit with jagged ends.
            /// </summary>
            public GameObject? Build(Transform parent, int half, Vector3 hit, Vector3 plane, float gap)
            {
                if (Real != null) return BuildReal(parent, half, hit, plane, gap);
                var mb = new MeshBuilder();
                int seed = 17;
                foreach (char ch in Name) seed = seed * 31 + ch;
                foreach (var t in Tubes)
                {
                    seed++;
                    if (half == 0) { TubeMesh(mb, t); continue; }
                    float u = t.Curve.Nearest(hit);
                    bool through = (t.Curve.At(u) - hit).magnitude < t.MaxRadius * 3 && u > 0.05f && u < 0.95f;
                    if (through)
                    {
                        float g = gap / Mathf.Max(1e-3f, t.Curve.Length);
                        // The half toward the curve's start lies against the tangent.
                        bool startNear = Vector3.Dot(t.Curve.At(0) - hit, plane) < 0;
                        bool wantStart = (half == 1) == startNear;
                        if (wantStart && u - g > 0.03f) TubeMesh(mb, t, 0, u - g, false, true, seed);
                        if (!wantStart && u + g < 0.97f) TubeMesh(mb, t, u + g, 1, true, false, seed);
                    }
                    else if ((Vector3.Dot(t.Curve.At(0.5f) - hit, plane) < 0) == (half == 1)) TubeMesh(mb, t);
                }
                foreach (var b in Blobs)
                    if (half == 0 || (Vector3.Dot(b.C - hit, plane) < 0) == (half == 1))
                        Shapes.Ellipsoid(mb, b.C, b.Rot, b.Radii, b.Deform, b.Rings, b.Sides);
                if (mb.VertexCount == 0) return null;
                var go = mb.Object(half == 0 ? Name : Name + (half == 1 ? " A" : " B"), Material, parent, shadows: false);
                go.GetComponent<MeshRenderer>().receiveShadows = false;
                return go;
            }
            /// <summary>
            /// The real mesh whole, or one side of the break: along the struck tube's length where a
            /// triangle lies on it, else by the side of the plane; triangles within `gap` are dropped.
            /// </summary>
            GameObject? BuildReal(Transform parent, int half, Vector3 hit, Vector3 plane, float gap)
            {
                var mesh = Real!;
                if (half != 0)
                {
                    TubeSpec? tube = null;
                    float bd = float.MaxValue;
                    foreach (var t in Tubes)
                    {
                        float d = (t.Curve.At(t.Curve.Nearest(hit)) - hit).sqrMagnitude;
                        if (d < bd) { bd = d; tube = t; }
                    }
                    float uh = tube != null ? tube.Curve.Nearest(hit) : 0;
                    float g = tube != null ? gap / Mathf.Max(1e-3f, tube.Curve.Length) : 0;
                    bool startNear = tube != null && Vector3.Dot(tube.Curve.At(0) - hit, plane) < 0;
                    var v = new List<Vector3>();
                    mesh.GetVertices(v);
                    var cut = new Mesh { name = Name + (half == 1 ? " A" : " B"), indexFormat = mesh.indexFormat };
                    cut.SetVertices(v);
                    var n = new List<Vector3>();
                    mesh.GetNormals(n);
                    cut.SetNormals(n);
                    var uv = new List<Vector2>();
                    mesh.GetUVs(0, uv);
                    cut.SetUVs(0, uv);
                    cut.subMeshCount = mesh.subMeshCount;
                    int kept = 0;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        var idx = mesh.GetTriangles(sub);
                        var keep = new List<int>(idx.Length / 2);
                        for (int i = 0; i + 2 < idx.Length; i += 3)
                        {
                            var c = (v[idx[i]] + v[idx[i + 1]] + v[idx[i + 2]]) / 3;
                            bool first;
                            float ut = tube != null ? tube.Curve.Nearest(c) : 0;
                            if (tube != null && (tube.Curve.At(ut) - c).magnitude < tube.MaxRadius * 3)
                            {
                                if (Mathf.Abs(ut - uh) < g) continue;
                                first = (ut < uh) == startNear;
                            }
                            else
                            {
                                float sd = Vector3.Dot(c - hit, plane);
                                if (Mathf.Abs(sd) < gap) continue;
                                first = sd < 0;
                            }
                            if (first != (half == 1)) continue;
                            keep.Add(idx[i]);
                            keep.Add(idx[i + 1]);
                            keep.Add(idx[i + 2]);
                        }
                        cut.SetTriangles(keep, sub);
                        kept += keep.Count;
                    }
                    if (kept == 0)
                    {
                        UnityEngine.Object.Destroy(cut);
                        return null;
                    }
                    cut.RecalculateBounds();
                    mesh = cut;
                }
                var go = new GameObject(half == 0 ? Name : Name + (half == 1 ? " A" : " B"));
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = RealMats ?? new[] { Material };
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                return go;
            }
        }

        public readonly List<Part> Parts = new List<Part>();
        public readonly Transform Root;
        public readonly Material Bone, Dark;
        /// <summary>Scale of the figure against a 1.75 m man.</summary>
        public float K = 1;

        readonly Figure fig;
        readonly Material cartilage;
        Vector3 right;
        float w;
        Shapes.Curve spine = null!;
        float uL5, uT12, uC7, uC1;

        Anatomy(Figure f, Transform parent)
        {
            fig = f;
            Root = new GameObject("Anatomy").transform;
            Root.SetParent(parent, false);
            Bone = Glow(new Color(0.8f, 0.78f, 0.72f), new Color(0.15f, 0.155f, 0.17f), 0.35f);
            cartilage = Glow(new Color(0.6f, 0.7f, 0.8f), new Color(0.16f, 0.22f, 0.3f), 0.5f);
            Dark = Library.Plain(new Color(0.035f, 0.02f, 0.025f), 0.2f);
        }

        static Material Glow(Color c, Color e, float smooth)
        {
            var m = Library.Glow(c, e);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0);
            return m;
        }

        public static Anatomy Build(Figure f, Transform parent)
        {
            var a = new Anatomy(f, parent);
            if (!a.Has("Hips", out _) || !a.Has("Head", out _) || !a.Has("LeftUpLeg", out _) || !a.Has("RightUpLeg", out _)) return a;
            a.Frame();
            a.Spine();
            a.Ribs();
            a.Shoulders();
            foreach (var s in new[] { -1f, 1f })
            {
                a.Arm(s);
                a.Leg(s);
            }
            a.Pelvis();
            a.Skull();
            a.Organs();
            foreach (var p in a.Parts)
            {
                var go = p.Build(a.Root, 0, Vector3.zero, Vector3.zero, 0);
                if (go != null) p.Go = go;
            }
            a.Parts.RemoveAll(p => p.Go == null);
            RealBones.Dress(a, f);
            RealOrgans.Dress(a, f);
            return a;
        }

        // ---- frame ---------------------------------------------------------------------------

        bool Has(string n, out Vector3 p)
        {
            p = default;
            if (!fig.Bones.TryGetValue(n, out var t)) return false;
            p = t.position;
            return true;
        }

        Vector3 J(string n, Vector3 fallback) => Has(n, out var p) ? p : fallback;

        void Frame()
        {
            Has("Hips", out var hips);
            Has("LeftUpLeg", out var lul);
            Has("RightUpLeg", out var rul);
            right = (rul - lul).normalized;
            if (Has("LeftArm", out var la) && Has("RightArm", out var ra))
            {
                right = (right + (ra - la).normalized).normalized;
                w = Mathf.Clamp(Vector3.Distance(la, ra) * 0.5f, 0.12f, 0.26f);
            }
            if (Has("LeftLeg", out var ll)) K = Mathf.Clamp(Vector3.Distance(lul, ll) / 0.43f, 0.75f, 1.35f);
            if (w <= 0) w = 0.17f * K;

            var names = new List<string>();
            var ctrl = new List<Vector3>();
            foreach (var n in new[] { "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head" })
            {
                if (!Has(n, out var p)) continue;
                names.Add(n);
                ctrl.Add(p);
            }
            spine = new Shapes.Curve(ctrl);
            float L = Mathf.Max(0.1f, spine.Length);
            float U(string n, float fb) => names.Contains(n) ? spine.UOf(names.IndexOf(n)) : fb;
            uL5 = 0.03f * K / L;
            uT12 = U("Spine1", 0.42f);
            uC7 = U("Neck", 0.82f) - 0.02f * K / L;
            uC1 = 1 - 0.012f * K / L;
        }

        /// <summary>Spine direction, body right and front at u along the spine.</summary>
        (Vector3 up, Vector3 r, Vector3 fr) At(float u)
        {
            var t = spine.Tangent(u);
            var r = Vector3.ProjectOnPlane(right, t).normalized;
            return (t, r, Vector3.Cross(r, t));
        }

        float BackOffset(float u)
        {
            if (u < uT12) return Mathf.Lerp(0.028f, 0.042f, Mathf.InverseLerp(uL5, uT12, u)) * K;
            if (u < uC7) return 0.046f * K;
            return Mathf.Lerp(0.04f, 0.012f, Mathf.InverseLerp(uC7, uC1, u)) * K;
        }

        /// <summary>Centre of the vertebral column at u: behind the joints, toward the back.</summary>
        Vector3 Column(float u) => spine.At(u) - At(u).fr * BackOffset(u);

        float UT(int n) => Mathf.Lerp(uC7, uT12, (n - 0.5f) / 12);
        float UL(int n) => Mathf.Lerp(uT12, uL5, (n - 0.5f) / 5);
        float UC(int n) => Mathf.Lerp(uC1, uC7, (n - 0.5f) / 7);

        static float Bump(float u, float c, float width) => Mathf.Exp(-(u - c) * (u - c) / (width * width));

        Part NewPart(string name, Kind kind, Material mat, bool splits = false)
        {
            var p = new Part { Name = name, Kind = kind, Material = mat, Splits = splits, Bleeds = kind == Kind.Organ };
            Parts.Add(p);
            return p;
        }

        TubeSpec Tube(Part p, IReadOnlyList<Vector3> pts, Func<float, float> radius, Vector3? wide = null, float flat = 1, Func<float, float, float>? lobes = null, int sides = 10)
        {
            var t = new TubeSpec { Curve = new Shapes.Curve(pts), Radius = radius, Wide = wide, Flat = flat, Lobes = lobes, Sides = sides };
            float m = 0;
            for (int i = 0; i <= 20; i++) m = Mathf.Max(m, radius(i / 20f));
            t.MaxRadius = m * Mathf.Max(1, flat) * (lobes != null ? 1.3f : 1.1f);
            p.Tubes.Add(t);
            return t;
        }

        void Blob(Part p, Vector3 c, Quaternion rot, Vector3 radii, Func<Vector3, Vector3>? deform = null, int rings = 10, int sides = 14) =>
            p.Blobs.Add(new BlobSpec { C = c, Rot = rot, Radii = radii, Deform = deform, Rings = rings, Sides = sides });

        void Ball(Part p, Vector3 c, float r) => Blob(p, c, Quaternion.identity, Vector3.one * r, null, 8, 12);

        /// <summary>Rotation whose local y is `up` and local z as near `front` as it can be.</summary>
        static Quaternion Orient(Vector3 up, Vector3 front)
        {
            var (z, _) = Shapes.Across(up.normalized, front);
            return Quaternion.LookRotation(z, up);
        }

        /// <summary>Builds a tube's mesh, from u0 to u1, with rounded, flat or jagged (broken) ends.</summary>
        public static void TubeMesh(MeshBuilder mb, TubeSpec s, float u0 = 0, float u1 = 1, bool jagA = false, bool jagB = false, int seed = 1)
        {
            float L = s.Curve.Length;
            if (L < 1e-4f || u1 - u0 < 1e-4f) return;
            bool ra = s.RoundA && u0 <= 0 && !jagA, rb = s.RoundB && u1 >= 1 && !jagB;
            int R = Mathf.Clamp(8 + (int)((u1 - u0) * L * 90), 10, 40), S = s.Sides;
            float capA = Mathf.Max(1e-4f, s.Radius(0) * 1.1f / L), capB = Mathf.Max(1e-4f, s.Radius(1) * 1.1f / L);
            var rings = new Vector3[R][];
            var rng = new System.Random(seed);
            var prev = Vector3.zero;
            Vector3 c0 = default, t0 = default, c1 = default, t1 = default;
            float r0 = 0, r1 = 0;
            for (int k = 0; k < R; k++)
            {
                float x = k / (float)(R - 1);
                float wgt = ra && rb ? 0.5f - 0.5f * Mathf.Cos(Mathf.PI * x) : ra ? 1 - Mathf.Cos(Mathf.PI * 0.5f * x) : rb ? Mathf.Sin(Mathf.PI * 0.5f * x) : x;
                float u = Mathf.Lerp(u0, u1, wgt);
                var c = s.Curve.At(u);
                var t = s.Curve.Tangent(u);
                Vector3 n, b;
                if (s.Wide.HasValue || k == 0) (n, b) = Shapes.Across(t, s.Wide ?? Vector3.up);
                else (n, b) = Shapes.Across(t, prev);
                prev = n;
                float r = s.Radius(u);
                if (ra) r *= Round(u / capA);
                if (rb) r *= Round((1 - u) / capB);
                bool jag = (jagA && k == 0) || (jagB && k == R - 1);
                rings[k] = new Vector3[S];
                for (int j = 0; j < S; j++)
                {
                    float ph = 2 * Mathf.PI * j / S;
                    float m = s.Lobes?.Invoke(u, ph) ?? 1;
                    var off = (n * Mathf.Cos(ph) * s.Flat + b * Mathf.Sin(ph)) * r * m;
                    if (jag) off = off * (0.8f + 0.4f * (float)rng.NextDouble()) + t * ((float)rng.NextDouble() - 0.5f) * r * 2.2f;
                    rings[k][j] = c + off;
                }
                if (k == 0) { c0 = c; t0 = t; r0 = r; }
                if (k == R - 1) { c1 = c; t1 = t; r1 = r; }
            }
            // Broken ends are hollow: the cap sinks into the bone.
            Vector3? a = ra ? (Vector3?)null : c0 + t0 * (jagA ? r0 * 0.6f : 0);
            Vector3? e = rb ? (Vector3?)null : c1 - t1 * (jagB ? r1 * 0.6f : 0);
            Shapes.Loft(mb, rings, a, e);
        }

        static float Round(float f)
        {
            f = Mathf.Clamp01(f);
            return Mathf.Sqrt(1 - (1 - f) * (1 - f));
        }

        // ---- skeleton ------------------------------------------------------------------------

        void Spine()
        {
            for (int i = 0; i < 24; i++)
            {
                // C1 at the skull down to L5 above the sacrum.
                string name;
                float u, r, h;
                if (i < 7) { name = $"C{i + 1}"; u = UC(i + 1); r = 0.0105f + i * 0.0006f; }
                else if (i < 19) { int n = i - 6; name = $"T{n}"; u = UT(n); r = 0.0135f + n * 0.0006f; }
                else { int n = i - 18; name = $"L{n}"; u = UL(n); r = 0.021f + n * 0.0009f; }
                r *= K;
                // Height: most of the spacing to the next, the rest the disc between.
                float du = i < 7 ? (uC1 - uC7) / 7 : i < 19 ? (uC7 - uT12) / 12 : (uT12 - uL5) / 5;
                h = Mathf.Max(du * spine.Length * 0.62f, 0.008f * K);
                var (up, rt, fr) = At(u);
                var c = Column(u);
                var p = NewPart("Vertebra " + name, Kind.Bone, Bone);
                // The body: a squat drum with flattened end plates.
                Blob(p, c, Orient(up, fr), new Vector3(r * 1.12f, h * 0.5f, r), s => new Vector3(s.x, Mathf.Sign(s.y) * Mathf.Pow(Mathf.Abs(s.y), 0.55f), s.z), 8, 12);
                // Spinous process back and down; transverse processes to the sides.
                float lumbar = i >= 19 ? 1 : 0;
                float reach = (i < 7 ? 0.018f : i < 19 ? 0.034f : 0.03f) * K;
                var arch = c - fr * r * 1.2f;
                Tube(p, new[] { arch, arch - fr * reach - up * reach * (0.6f - lumbar * 0.5f) }, u2 => Mathf.Lerp(0.0048f, 0.0032f, u2) * K * (1 + lumbar * 0.4f), up, 1.4f, null, 7);
                foreach (var sd in new[] { -1f, 1f })
                {
                    float span = (i < 7 ? 0.016f : i < 19 ? 0.024f : 0.03f) * K;
                    Tube(p, new[] { arch + fr * r * 0.2f, arch - fr * r * 0.25f + rt * sd * (r + span) }, u2 => 0.0036f * K * (1 - 0.3f * u2), null, 1, null, 7);
                }
                // The neural arch ring around the cord.
                Tube(p, new[] { c - fr * r * 0.7f + rt * r * 0.55f, arch, c - fr * r * 0.7f - rt * r * 0.55f }, _ => 0.0032f * K, null, 1, null, 7);
            }
        }

        static readonly float[] RibA = { 0.42f, 0.55f, 0.64f, 0.71f, 0.76f, 0.79f, 0.81f, 0.81f, 0.79f, 0.75f, 0.69f, 0.6f };
        static readonly float[] RibB = { 0.55f, 0.7f, 0.8f, 0.88f, 0.94f, 0.98f, 1, 1, 0.98f, 0.94f, 0.9f, 0.85f };
        static readonly float[] RibDrop = { 0.03f, 0.055f, 0.075f, 0.09f, 0.1f, 0.108f, 0.112f, 0.112f, 0.105f, 0.095f, 0.06f, 0.04f };

        /// <summary>A point on rib n (1–12) on side s at angle phi from the spine (0) round to the front (π).</summary>
        Vector3 RibPoint(int n, float s, float phi)
        {
            float u = UT(n);
            var (up, rt, fr) = At(u);
            float a = w * RibA[n - 1], b = 0.105f * K * RibB[n - 1], drop = K * RibDrop[n - 1];
            float lateral = 0.016f * K + a * Mathf.Sin(phi);
            // The rib runs out and a little back from the spine (its angle) before sweeping forward.
            float depth = b * (1 - Mathf.Cos(phi)) - 0.03f * K * Mathf.Sin(phi) * (1 - phi / Mathf.PI);
            return Column(u) + rt * s * lateral + fr * depth - up * drop * (1 - Mathf.Cos(phi)) * 0.5f;
        }

        /// <summary>Where rib n's cartilage meets the sternum (the front midline at its level).</summary>
        Vector3 Sternal(int n) => RibPoint(n, 1, Mathf.PI) - At(UT(n)).r * (0.016f * K);

        float RibEnd(int n) => n <= 7 ? 0.88f * Mathf.PI : n <= 10 ? 0.8f * Mathf.PI : n == 11 ? 0.56f * Mathf.PI : 0.45f * Mathf.PI;

        void Ribs()
        {
            var cart = NewPart("Cartilage", Kind.Bone, cartilage);
            cart.Hittable = false;
            for (int n = 1; n <= 12; n++)
            {
                var up = At(UT(n)).up;
                foreach (var s in new[] { -1f, 1f })
                {
                    var p = NewPart($"Rib {(s < 0 ? "L" : "R")}{n}", Kind.Bone, Bone, splits: true);
                    var pts = new List<Vector3>();
                    float end = RibEnd(n);
                    for (int i = 0; i <= 10; i++) pts.Add(RibPoint(n, s, Mathf.Lerp(0.02f * Mathf.PI, end, i / 10f)));
                    float r = (n <= 2 ? 0.0058f : 0.0052f) * K;
                    Tube(p, pts, u => r * (1 - 0.2f * u), up, 1.7f, null, 8);
                    // Costal cartilage to the sternum: straight in for the true ribs, curving up for 8–10.
                    var tip = pts[pts.Count - 1];
                    var rt = At(UT(n)).r;
                    if (n <= 7) Tube(cart, new[] { tip, Vector3.Lerp(tip, Sternal(n), 0.5f) - up * 0.004f * K, Sternal(n) + rt * s * 0.006f * K }, _ => 0.0042f * K, up, 1.3f, null, 7);
                    else if (n <= 10)
                    {
                        var to = Sternal(7) + rt * s * 0.012f * K;
                        Tube(cart, new[] { tip, Vector3.Lerp(tip, to, 0.5f) - up * 0.01f * K, to }, _ => 0.0038f * K, up, 1.3f, null, 7);
                    }
                }
            }
            // The sternum: manubrium, body and xiphoid down the front.
            var st = NewPart("Sternum", Kind.Bone, Bone, splits: true);
            var top = At(UT(1));
            var low = At(UT(7));
            Tube(st, new[] { Sternal(1) + top.up * 0.03f * K, Sternal(1), Sternal(3), Sternal(5), Sternal(7), Sternal(7) - low.up * 0.045f * K },
                u => Mathf.Lerp(1.35f, 0.55f, u) * 0.0048f * K, top.r, 3.2f, null, 10);
        }

        void Shoulders()
        {
            var t1 = At(UT(1));
            var mTop = Sternal(1) + t1.up * 0.025f * K;
            foreach (var s in new[] { -1f, 1f })
            {
                string side = s < 0 ? "Left" : "Right", tag = s < 0 ? "L" : "R";
                var arm = J(side + "Arm", Column(UT(2)) + t1.r * s * w);
                var acromion = arm + t1.up * 0.028f * K - t1.fr * 0.008f * K;
                // Clavicle: a gentle S from the top of the sternum out to the point of the shoulder.
                var cl = NewPart("Clavicle " + tag, Kind.Bone, Bone, splits: true);
                cl.Target = HitZone.Chest;
                var a = mTop + t1.r * s * 0.016f * K;
                Tube(cl, new[] { a, Vector3.Lerp(a, acromion, 0.4f) + t1.fr * 0.016f * K + t1.up * 0.006f * K, Vector3.Lerp(a, acromion, 0.8f) - t1.fr * 0.006f * K, acromion },
                    u => 0.0062f * K * (1 + 0.3f * Bump(u, 0, 0.12f)), null, 1, null, 9);

                // Scapula: a curved triangular plate on the back over ribs 2–7.
                var sc = NewPart("Scapula " + tag, Kind.Bone, Bone);
                var f4 = At(UT(4));
                var c = Column(UT(4)) - f4.fr * 0.03f * K + f4.r * s * 0.095f * K - f4.up * 0.012f * K;
                var lateral = (f4.r * s * 0.92f + f4.fr * 0.38f).normalized;
                var rot = Orient(f4.up, Vector3.Cross(lateral, f4.up) * s);
                float sg = Mathf.Sign(Vector3.Dot(rot * Vector3.right, lateral));
                Blob(sc, c, rot, new Vector3(0.058f, 0.078f, 0.0075f) * K, v =>
                {
                    float lx = v.x * sg, y01 = (v.y + 1) * 0.5f;
                    // Wide along the top, narrowing to the inferior angle, which sits toward the spine.
                    float wdt = 0.3f + 0.7f * Mathf.Sqrt(y01);
                    lx = lx * wdt - (1 - y01) * 0.35f;
                    return new Vector3(lx * sg, v.y, v.z * (0.55f + 0.45f * (1 - Mathf.Abs(v.x))) + v.x * v.x * 1.2f);
                }, 12, 16);
                // The spine of the scapula across its back, up to the acromion; the glenoid socket by the arm.
                var spineStart = c + f4.up * 0.04f * K - lateral * 0.05f * K - f4.fr * 0.004f * K;
                Tube(sc, new[] { spineStart, Vector3.Lerp(spineStart, acromion, 0.5f) - f4.fr * 0.012f * K, acromion }, u => Mathf.Lerp(0.0035f, 0.006f, u) * K, null, 1, null, 8);
                Ball(sc, arm - lateral * 0.018f * K, 0.013f * K);
            }
        }

        void Arm(float s)
        {
            string side = s < 0 ? "Left" : "Right", tag = s < 0 ? "L" : "R";
            if (!Has(side + "Arm", out var arm) || !Has(side + "ForeArm", out var elbow) || !Has(side + "Hand", out var wrist)) return;
            var hu = NewPart("Humerus " + tag, Kind.Bone, Bone, splits: true);
            Tube(hu, new[] { arm, elbow }, u => 0.0105f * K * (1 + 0.85f * Bump(u, 0, 0.1f) + 0.5f * Bump(u, 1, 0.09f)), right, 1,
                (u, ph) => 1 + 0.38f * Bump(u, 1, 0.1f) * Mathf.Cos(2 * ph), 12);
            Ball(hu, arm, 0.02f * K);

            var dir = (wrist - elbow).normalized;
            var o = Vector3.ProjectOnPlane(right, dir).normalized * 0.009f * K;
            var fa = NewPart("Forearm " + tag, Kind.Bone, Bone, splits: true);
            Tube(fa, new[] { elbow - dir * 0.018f * K + o, wrist + o * 0.85f }, u => 0.0068f * K * (1 + 0.75f * Bump(u, 0, 0.1f)) * Mathf.Lerp(1, 0.62f, u), null, 1, null, 9);
            Tube(fa, new[] { elbow - o * 0.4f, wrist - o }, u => 0.0058f * K * Mathf.Lerp(0.85f, 1.25f, u) * (1 + 0.5f * Bump(u, 1, 0.08f)), null, 1, null, 9);

            // Hand: carpals, then metacarpals and phalanges along the finger joints.
            var hand = NewPart("Hand " + tag, Kind.Bone, Bone);
            var handDir = Has(side + "HandMiddle1", out var mid1) ? (mid1 - wrist).normalized : dir;
            Blob(hand, wrist + handDir * 0.016f * K, Orient(handDir, Vector3.ProjectOnPlane(right, handDir)), new Vector3(0.019f, 0.014f, 0.01f) * K, null, 8, 12);
            bool fingers = false;
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
            {
                var joints = new List<Vector3>();
                for (int i = 1; i <= 4; i++)
                    if (Has($"{side}Hand{finger}{i}", out var jp)) joints.Add(jp);
                if (joints.Count < 2) continue;
                fingers = true;
                float r = (finger == "Thumb" ? 0.0048f : 0.0038f) * K;
                Tube(hand, new[] { Vector3.Lerp(wrist, joints[0], 0.25f), joints[0] }, u => r * (1 + 0.3f * Bump(u, 1, 0.15f)), null, 1, null, 7);
                Tube(hand, joints, u => r * Mathf.Lerp(0.95f, 0.7f, u), null, 1, null, 7);
            }
            if (!fingers)
                Blob(hand, wrist + handDir * 0.06f * K, Orient(handDir, Vector3.ProjectOnPlane(right, handDir)), new Vector3(0.03f, 0.05f, 0.008f) * K, null, 8, 12);
        }

        void Leg(float s)
        {
            string side = s < 0 ? "Left" : "Right", tag = s < 0 ? "L" : "R";
            if (!Has(side + "UpLeg", out var hip) || !Has(side + "Leg", out var knee) || !Has(side + "Foot", out var ankle)) return;
            var f0 = At(0);
            var dir = (knee - hip).normalized;
            var lat = Vector3.ProjectOnPlane(f0.r * s, dir).normalized;
            // Femur: head in the socket, neck out to the greater trochanter, shaft to the condyles.
            var fe = NewPart("Femur " + tag, Kind.Bone, Bone, splits: true);
            var troch = hip + lat * 0.03f * K + dir * 0.04f * K;
            Tube(fe, new[] { troch, Vector3.Lerp(troch, knee, 0.5f) - lat * 0.006f * K, knee }, u => 0.0135f * K * (1 + 0.45f * Bump(u, 0, 0.08f) + 0.75f * Bump(u, 1, 0.09f)), lat, 1,
                (u, ph) => 1 + 0.42f * Bump(u, 1, 0.1f) * Mathf.Cos(2 * ph), 12);
            Tube(fe, new[] { hip, troch }, u => 0.0115f * K, null, 1, null, 9);
            Ball(fe, hip, 0.023f * K);
            Ball(fe, troch + lat * 0.008f * K - dir * 0.006f * K, 0.016f * K);

            // Shin: tibia with its plateau and malleolus, the thin fibula beside it, the kneecap in front.
            var shin = NewPart("Shin " + tag, Kind.Bone, Bone, splits: true);
            var sd = (ankle - knee).normalized;
            var sl = Vector3.ProjectOnPlane(f0.r * s, sd).normalized;
            var front = Vector3.ProjectOnPlane(f0.fr, sd).normalized;
            Tube(shin, new[] { knee + sd * 0.012f * K, ankle }, u => 0.0125f * K * (1 + 0.9f * Bump(u, 0, 0.08f) + 0.35f * Bump(u, 1, 0.07f)), sl, 1,
                (u, ph) => 1 + 0.3f * Bump(u, 0, 0.1f) * Mathf.Cos(2 * ph), 12);
            var fo = sl * 0.02f * K - front * 0.008f * K;
            Tube(shin, new[] { knee + fo + sd * 0.03f * K, ankle + fo * 1.05f + sd * 0.006f * K }, u => 0.0052f * K * (1 + 0.5f * Bump(u, 1, 0.08f) + 0.3f * Bump(u, 0, 0.08f)), null, 1, null, 8);
            Blob(shin, knee + front * 0.034f * K - dir * 0.006f * K, Orient(dir, front), new Vector3(0.02f, 0.022f, 0.009f) * K, null, 8, 12);

            // Foot: heel, metatarsals fanning to the toes.
            var foot = NewPart("Foot " + tag, Kind.Bone, Bone);
            var toe = J(side + "ToeBase", ankle + front * 0.12f * K);
            var fd = (toe - ankle).normalized;
            var fl = Vector3.Cross(fd, sd).normalized;
            var toeEnd = J(side + "Toe_End", toe + fd * 0.045f * K);
            var heel = ankle - fd * 0.035f * K + sd * 0.03f * K;
            Blob(foot, Vector3.Lerp(ankle, heel, 0.6f), Orient(fd, sd), new Vector3(0.02f, 0.035f, 0.022f) * K, null, 8, 12);
            Ball(foot, ankle + sd * 0.008f * K, 0.016f * K);
            for (int i = 0; i < 5; i++)
            {
                float off = (i - 2) * 0.011f * K;
                var from = Vector3.Lerp(ankle, toe, 0.35f) + fl * off * 0.8f;
                var to = toe + fl * off * 1.15f;
                Tube(foot, new[] { from, to }, u => (i == 0 || i == 4 ? 0.0042f : 0.0046f) * K * (1 + 0.35f * Bump(u, 1, 0.12f)), null, 1, null, 7);
                Tube(foot, new[] { to + fd * 0.004f * K, Vector3.Lerp(to, toeEnd + fl * off * 1.2f, 0.85f) }, u => 0.0036f * K, null, 1, null, 7);
            }
        }

        void Pelvis()
        {
            Has("Hips", out var hips);
            Has("LeftUpLeg", out var lul);
            Has("RightUpLeg", out var rul);
            var f = At(0);
            var p = NewPart("Pelvis", Kind.Bone, Bone);
            var mid = (lul + rul) * 0.5f;
            var sym = mid + f.fr * 0.055f * K - f.up * 0.04f * K;
            foreach (var s in new[] { -1f, 1f })
            {
                var hip = s < 0 ? lul : rul;
                // Ilium: a cupped wing flaring forward and out above the hip socket.
                var ic = hips + f.r * s * 0.075f * K + f.up * 0.045f * K;
                var h = (f.fr * 0.75f + f.r * s * 0.66f).normalized;
                var rot = Orient(f.up, Vector3.Cross(h, f.up) * s);
                float sg = Mathf.Sign(Vector3.Dot(rot * Vector3.right, h));
                float ha = Mathf.Clamp(Vector3.Dot(hip - ic, h) / (0.075f * K), -0.6f, 0.6f);
                Blob(p, ic, rot, new Vector3(0.075f, 0.06f, 0.008f) * K, v =>
                {
                    float x = v.x * sg, y01 = (v.y + 1) * 0.5f;
                    float wdt = 0.35f + 0.65f * Mathf.Sqrt(y01);
                    x = x * wdt + (1 - wdt) * ha;
                    return new Vector3(x * sg, v.y, v.z + x * x * 2.6f - 1.3f);
                }, 12, 16);
                // Iliac crest along the top edge.
                var crest = new List<Vector3>();
                for (int i = 0; i <= 8; i++)
                {
                    float x = Mathf.Lerp(-0.92f, 0.92f, i / 8f);
                    float y = Mathf.Sqrt(1 - x * x) * 0.96f;
                    crest.Add(ic + h * x * 0.075f * K + f.up * y * 0.06f * K + rot * Vector3.forward * (x * x * 2.6f - 1.3f) * 0.008f * K);
                }
                Tube(p, crest, u => 0.0055f * K, null, 1, null, 8);
                // Hip socket, pubic and ischial rami round the obturator gap to the symphysis.
                Blob(p, hip + f.r * s * 0.008f * K, Orient(f.r * s, f.up), new Vector3(0.027f, 0.012f, 0.027f) * K, null, 8, 12);
                var tuber = hip - f.up * 0.065f * K - f.fr * 0.02f * K - f.r * s * 0.008f * K;
                Tube(p, new[] { hip + f.fr * 0.015f * K - f.r * s * 0.01f * K, Vector3.Lerp(hip, sym, 0.55f) + f.up * 0.008f * K, sym + f.r * s * 0.012f * K }, u => 0.0075f * K, null, 1, null, 8);
                Tube(p, new[] { hip - f.up * 0.01f * K, tuber, Vector3.Lerp(tuber, sym, 0.55f) - f.up * 0.012f * K, sym + f.r * s * 0.01f * K }, u => 0.0085f * K * (1 + 0.4f * Bump(u, 0.33f, 0.12f)), null, 1, null, 8);
                // Sacroiliac join from the wing to the sacrum.
                Tube(p, new[] { ic - h * 0.06f * K - f.up * 0.01f * K, hips - f.fr * 0.05f * K + f.r * s * 0.025f * K }, u => 0.009f * K, null, 1, null, 8);
            }
            // Sacrum and coccyx, curving down and back between the wings.
            var top = Column(uL5) - f.up * 0.008f * K;
            Tube(p, new[] { top, hips - f.fr * 0.058f * K - f.up * 0.035f * K, hips - f.fr * 0.06f * K - f.up * 0.085f * K, hips - f.fr * 0.045f * K - f.up * 0.11f * K },
                u => Mathf.Lerp(0.013f, 0.003f, u) * K, f.r, 3.2f, null, 10);
        }

        void Skull()
        {
            Has("Head", out var head);
            var crown = J("HeadTop_End", head + At(1).up * 0.19f * K);
            var up = (crown - head).normalized;
            float hh = Mathf.Clamp(Vector3.Distance(head, crown), 0.14f * K, 0.24f * K);
            var side = Vector3.ProjectOnPlane(right, up).normalized;
            var front = Vector3.Cross(side, up);
            var rot = Quaternion.LookRotation(front, up);
            Vector3 P(float x, float y, float z) => head + side * x * hh + up * y * hh + front * z * hh;

            var sk = NewPart("Skull", Kind.Bone, Bone);
            sk.Bleeds = true;
            sk.Target = HitZone.Head;
            sk.Blood = new Color(0.6f, 0.04f, 0.05f);
            // Cranium: an egg, longer front to back, a little narrower at the brow.
            Blob(sk, P(0, 0.56f, -0.04f), rot, new Vector3(0.4f, 0.47f, 0.53f) * hh, v => new Vector3(v.x * (1 - 0.12f * Mathf.Max(0, v.z)), v.y, v.z), 14, 18);
            // Face: brow ridge, cheekbones, upper jaw.
            Blob(sk, P(0, 0.33f, 0.36f), rot, new Vector3(0.27f, 0.22f, 0.2f) * hh, null, 10, 14);
            Tube(sk, new[] { P(-0.27f, 0.58f, 0.38f), P(0, 0.62f, 0.5f), P(0.27f, 0.58f, 0.38f) }, _ => 0.04f * hh, null, 1, null, 8);
            foreach (var x in new[] { -1f, 1f })
            {
                Ball(sk, P(x * 0.27f, 0.38f, 0.35f), 0.08f * hh);
                Tube(sk, new[] { P(x * 0.3f, 0.38f, 0.33f), P(x * 0.37f, 0.36f, 0.12f), P(x * 0.36f, 0.33f, -0.02f) }, _ => 0.03f * hh, up, 1.6f, null, 7);
            }
            // Mandible: a U from each jaw joint down to the angle and round to the chin.
            Tube(sk, new[] { P(-0.33f, 0.26f, -0.02f), P(-0.31f, -0.02f, 0.02f), P(-0.2f, -0.08f, 0.3f), P(0, -0.11f, 0.44f), P(0.2f, -0.08f, 0.3f), P(0.31f, -0.02f, 0.02f), P(0.33f, 0.26f, -0.02f) },
                u => 0.045f * hh * (1 + 0.25f * Bump(u, 0.5f, 0.1f)), up, 1.5f, null, 9);
            // Teeth: upper and lower rows.
            for (int row = 0; row < 2; row++)
            {
                for (int i = 0; i < 12; i++)
                {
                    float al = Mathf.Lerp(-1.25f, 1.25f, i / 11f);
                    float y = row == 0 ? 0.13f : 0.06f;
                    var c = P(Mathf.Sin(al) * 0.21f, y, 0.2f + Mathf.Cos(al) * 0.22f);
                    Blob(sk, c, rot, new Vector3(0.028f, 0.04f, 0.026f) * hh, null, 6, 8);
                }
            }
            // Eye sockets and nose: dark hollows set into the face.
            var holes = NewPart("Sockets", Kind.Bone, Dark);
            holes.Hittable = false;
            foreach (var x in new[] { -1f, 1f })
                Blob(holes, P(x * 0.165f, 0.48f, 0.46f), rot, new Vector3(0.11f, 0.1f, 0.07f) * hh, null, 8, 12);
            Blob(holes, P(0, 0.33f, 0.555f), rot, new Vector3(0.045f, 0.075f, 0.03f) * hh, null, 8, 10);
        }

        // ---- organs --------------------------------------------------------------------------

        /// <summary>The middle of the rib hoop at rib n's level.</summary>
        Vector3 Chest(int n)
        {
            var f = At(UT(n));
            return Column(UT(n)) + f.fr * 0.105f * K * RibB[n - 1] - f.up * K * RibDrop[n - 1] * 0.5f;
        }

        void Organs()
        {
            var f6 = At(UT(6));
            float H = Vector3.Distance(Column(UT(1)), Column(UT(12)));

            foreach (var s in new[] { -1f, 1f })
            {
                var lung = NewPart("Lung " + (s < 0 ? "L" : "R"), Kind.Organ, Glow(new Color(0.86f, 0.42f, 0.47f), new Color(0.36f, 0.08f, 0.1f), 0.6f));
                lung.Target = HitZone.Chest;
                lung.Blood = new Color(0.75f, 0.12f, 0.14f);
                float a = w * RibA[5];
                bool left = s < 0;
                Blob(lung, Chest(5) + f6.r * s * 0.5f * a - f6.up * 0.01f * K, Orient(f6.up, f6.fr), new Vector3(0.45f * a, 0.4f * H, 0.085f * K), v =>
                {
                    float lx = v.x * s;
                    // Narrow apex, flat inner face (notched for the heart on the left), concave base on the diaphragm.
                    float taper = 1 - 0.5f * Mathf.Pow(Mathf.Max(0, v.y), 1.6f);
                    if (lx < 0) lx *= left && v.z > -0.2f && v.y < 0.3f ? 0.35f : 0.6f;
                    float y = v.y;
                    if (y < -0.4f) y += 0.35f * (1 - lx * lx - v.z * v.z) * Mathf.InverseLerp(-0.4f, -1, y);
                    return new Vector3(lx * s * taper, y, v.z * taper);
                }, 14, 18);
            }

            var heart = NewPart("Heart", Kind.Organ, Glow(new Color(0.78f, 0.07f, 0.07f), new Color(0.75f, 0.03f, 0.03f), 0.65f));
            heart.Target = HitZone.Chest;
            heart.Blood = new Color(0.7f, 0.02f, 0.02f);
            var f7 = At(UT(7));
            var hc = Chest(7) - f7.r * 0.022f * K + f7.fr * 0.025f * K;
            var apex = (-f7.r * 0.55f - f7.up * 0.65f + f7.fr * 0.45f).normalized;
            var hrot = Orient(-apex, f7.fr);
            Blob(heart, hc, hrot, new Vector3(0.046f, 0.06f, 0.043f) * K, v =>
            {
                float t = v.y < 0 ? 1 + v.y * 0.55f : 1;
                return new Vector3(v.x * t, v.y, v.z * t);
            }, 14, 18);
            // The aorta arching up and back over to run down beside the spine.
            var hTop = hc - apex * 0.05f * K;
            var f4 = At(UT(4));
            Tube(heart, new[] { hTop, hTop + f7.up * 0.045f * K, Column(UT(4)) + f4.fr * 0.04f * K - f4.r * 0.012f * K + f4.up * 0.012f * K, Column(UT(8)) + f7.fr * 0.032f * K - f7.r * 0.02f * K, Column(UT(12)) + f7.fr * 0.032f * K - f7.r * 0.014f * K },
                u => 0.0105f * K * Mathf.Lerp(1.15f, 0.8f, u), null, 1, null, 10);

            var f11 = At(UT(11));
            var liver = NewPart("Liver", Kind.Organ, Glow(new Color(0.48f, 0.1f, 0.08f), new Color(0.3f, 0.04f, 0.03f), 0.6f));
            liver.Target = HitZone.Body;
            liver.Blood = new Color(0.45f, 0.03f, 0.03f);
            Blob(liver, Chest(11) + f11.r * 0.045f * K + f11.fr * 0.015f * K, Orient(f11.up, f11.fr), new Vector3(0.1f, 0.055f, 0.075f) * K, v =>
            {
                // A wedge: thick under the right ribs, thinning across to the left.
                float t = 0.5f + 0.5f * (v.x + 1) * 0.5f;
                return new Vector3(v.x, (v.y < 0 ? v.y * 0.7f : v.y) * t, v.z * t);
            }, 12, 18);

            var stomach = NewPart("Stomach", Kind.Organ, Glow(new Color(0.88f, 0.5f, 0.44f), new Color(0.32f, 0.1f, 0.08f), 0.65f));
            stomach.Target = HitZone.Body;
            stomach.Blood = new Color(0.6f, 0.08f, 0.06f);
            var f10 = At(UT(10));
            Tube(stomach, new[]
            {
                Chest(9) - f10.r * 0.045f * K + f10.up * 0.015f * K,
                Chest(10) - f10.r * 0.065f * K + f10.fr * 0.025f * K,
                Chest(12) - f10.r * 0.035f * K + f10.fr * 0.05f * K,
                Chest(12) + f10.r * 0.025f * K + f10.fr * 0.05f * K + f10.up * 0.018f * K,
            }, u => Mathf.Lerp(0.036f, 0.013f, u) * K * (1 + 0.15f * Bump(u, 0.35f, 0.2f)), null, 1, null, 14);

            foreach (var s in new[] { -1f, 1f })
            {
                var kid = NewPart("Kidney " + (s < 0 ? "L" : "R"), Kind.Organ, Glow(new Color(0.58f, 0.13f, 0.13f), new Color(0.32f, 0.04f, 0.04f), 0.6f));
                kid.Target = HitZone.Body;
                kid.Blood = new Color(0.55f, 0.03f, 0.03f);
                float u = UL(1);
                var f = At(u);
                var axis = (f.up - f.r * s * 0.25f).normalized;
                Blob(kid, Column(u) + f.r * s * 0.058f * K + f.fr * 0.012f * K + f.up * (s < 0 ? 0.012f : 0) * K, Orient(axis, f.fr), new Vector3(0.028f, 0.055f, 0.022f) * K, v =>
                {
                    // A bean: the inner edge dished in at the middle.
                    float lx = v.x * s;
                    if (lx < 0) lx *= 1 - 0.6f * (1 - v.y * v.y);
                    return new Vector3(lx * s, v.y, v.z);
                }, 12, 16);
            }
        }
    }
}
