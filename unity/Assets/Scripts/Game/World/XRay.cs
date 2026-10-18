using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The X-ray kill cam. The target's body turns to a translucent blue shell, glowing at its rim,
    /// with his skeleton and organs (Anatomy) inside it, built from his own posed bones. The round's
    /// path through him is traced against the parts: bones it meets snap or crack and throw splinters,
    /// organs rupture in a spray of blood. Everything after the round enters is a function of how far
    /// it has gone (Evaluate), so the bullet cam can freeze, resume or jump to any moment.
    /// </summary>
    public sealed class XRay : MonoBehaviour
    {
        /// <summary>Metres the round travels per second of the impact sequence.</summary>
        public const float Pace = 0.5f;

        sealed class Ghost
        {
            public MeshFilter Filter = null!;
            public Mesh Original = null!, Copy = null!;
            public Vector3[] Pos = null!, Nor = null!;
            public Color[] Col = null!;
        }

        /// <summary>One part the round passes through, and what it leaves behind.</summary>
        sealed class Strike
        {
            public Anatomy.Part Part = null!;
            public float S;
            public Vector3 Point;
            public bool Fired;
            public GameObject? A, B;
            public Vector3 PivotA, PivotB, AxisA, AxisB;
            public Transform? Hole, Mist;
            public Material? MistMat;
            public float Angle;
        }

        /// <summary>A splinter of bone or a drop of blood: a little rigid mesh thrown from a strike.</summary>
        sealed class Bit
        {
            public float Born;
            public Vector3 P0, V, Axis;
            public float Spin, Size;
            public Vector3[] Verts = null!, Norms = null!;
        }

        readonly List<(Renderer r, Material[] mats, ShadowCastingMode shadows)> saved = new List<(Renderer, Material[], ShadowCastingMode)>();
        readonly List<Ghost> ghosts = new List<Ghost>();
        readonly List<Strike> strikes = new List<Strike>();
        readonly List<Bit> chips = new List<Bit>(), drops = new List<Bit>();
        Figure target = null!;
        Anatomy anatomy = null!;
        Material ghostMat = null!;
        Mesh? chipMesh, dropMesh;
        Transform? channel, flare;
        GameObject? backdrop;
        static Mesh? sphere;

        public bool Shown { get; private set; }
        /// <summary>Where the round goes in, and its heading.</summary>
        public Vector3 Entry { get; private set; }
        public Vector3 Dir { get; private set; } = Vector3.forward;
        /// <summary>How far past the entry the round meets its first part, and where it leaves the last.</summary>
        public float FirstHit { get; private set; }
        public float Exit { get; private set; } = 0.3f;

        /// <summary>Builds the anatomy inside the figure, hidden until Show.</summary>
        public static XRay Build(Figure target)
        {
            // Built in world coordinates, so the object stays at the origin rather than under the figure.
            var go = new GameObject("XRay");
            var x = go.AddComponent<XRay>();
            x.target = target;
            x.anatomy = Anatomy.Build(target, go.transform);
            x.anatomy.Root.gameObject.SetActive(false);
            x.ghostMat = Tinted("Additive", new Color(0.24f, 0.5f, 0.95f, 1));
            return x;
        }

        /// <summary>Turns the body to glass and lights up what's inside.</summary>
        public void Show()
        {
            if (Shown) return;
            Shown = true;
            // The rifle too, so it can't hide the wound; meshes that can't be read glow flat and dim.
            Material? flat = null;
            foreach (var r in target.GetComponentsInChildren<Renderer>())
            {
                saved.Add((r, r.sharedMaterials, r.shadowCastingMode));
                // The shell would shadow everything inside it.
                r.shadowCastingMode = ShadowCastingMode.Off;
                var mf = r.GetComponent<MeshFilter>();
                var g = mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable ? Rim(mf) : null;
                if (g == null && flat == null) flat = Tinted("Additive", new Color(0.05f, 0.1f, 0.18f, 1));
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = g != null ? ghostMat : flat!;
                r.sharedMaterials = mats;
            }
            anatomy.Root.gameObject.SetActive(true);
            PostFx.Xray(1);
            LateUpdate();
        }

        /// <summary>A copy of the mesh whose vertex alpha carries the rim glow, brightest where the surface turns away.</summary>
        Ghost? Rim(MeshFilter mf)
        {
            var copy = Instantiate(mf.sharedMesh);
            copy.name = mf.sharedMesh.name + " (x-ray)";
            var g = new Ghost { Filter = mf, Original = mf.sharedMesh, Copy = copy, Pos = copy.vertices, Nor = copy.normals };
            if (g.Nor.Length != g.Pos.Length)
            {
                Destroy(copy);
                return null;
            }
            var m = mf.transform.localToWorldMatrix;
            for (int i = 0; i < g.Pos.Length; i++)
            {
                g.Pos[i] = m.MultiplyPoint3x4(g.Pos[i]);
                g.Nor[i] = m.MultiplyVector(g.Nor[i]).normalized;
            }
            g.Col = new Color[g.Pos.Length];
            mf.sharedMesh = copy;
            ghosts.Add(g);
            return g;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || ghosts.Count == 0) return;
            var eye = cam.transform.position;
            foreach (var g in ghosts)
            {
                for (int i = 0; i < g.Pos.Length; i++)
                {
                    var v = eye - g.Pos[i];
                    float d = Mathf.Abs(Vector3.Dot(g.Nor[i], v)) / Mathf.Max(1e-5f, v.magnitude);
                    float rim = 1 - d;
                    g.Col[i] = new Color(1, 1, 1, 0.035f + 0.85f * rim * rim * rim);
                }
                g.Copy.colors = g.Col;
            }
        }

        /// <summary>
        /// Lines the round up on the body: the path of a shot landing at `point` heading `dir`.
        /// When it would miss every part of the zone the rules say it hit (the hit boxes and the
        /// model don't agree exactly), it is moved across onto that zone's organ. Returns the entry.
        /// </summary>
        public Vector3 Aim(Vector3 point, Vector3 dir, HitZone? zone)
        {
            Dir = dir.normalized;
            var o = point - Dir * 1.5f;
            bool any = false, inZone = false;
            foreach (var p in anatomy.Parts)
            {
                if (!p.Hittable || float.IsInfinity(p.Hit(o, Dir))) continue;
                any = true;
                if (zone != null && p.Target == zone) inZone = true;
            }
            if (zone != null ? !inZone : !any)
            {
                Anatomy.Part? best = null;
                float bd = float.MaxValue;
                foreach (var p in anatomy.Parts)
                {
                    if (p.Target == null || (zone != null && p.Target != zone)) continue;
                    float d = Vector3.ProjectOnPlane(p.Centre - o, Dir).magnitude;
                    if (d < bd) { bd = d; best = p; }
                }
                if (best != null) o += Vector3.ProjectOnPlane(best.Centre - o, Dir) * 0.9f;
            }

            var found = new List<(Anatomy.Part p, float s)>();
            foreach (var p in anatomy.Parts)
            {
                if (!p.Hittable) continue;
                float s = p.Hit(o, Dir);
                if (!float.IsInfinity(s)) found.Add((p, s));
            }
            found.Sort((a, b) => a.s.CompareTo(b.s));
            float s0 = Vector3.Dot(point - o, Dir);
            if (found.Count > 0) s0 = Mathf.Clamp(s0, found[0].s - 0.12f, found[0].s - 0.03f);
            Entry = o + Dir * s0;
            foreach (var (p, s) in found)
                if (s >= s0 - 0.02f && s <= s0 + 0.6f)
                    strikes.Add(new Strike { Part = p, S = Mathf.Max(0, s - s0), Point = o + Dir * Mathf.Max(s, s0) });
            FirstHit = strikes.Count > 0 ? strikes[0].S : 0;
            Exit = (strikes.Count > 0 ? strikes[strikes.Count - 1].S : 0) + 0.1f;
            Scatter();
            return Entry;
        }

        /// <summary>Plans each strike's splinters and blood: where they start, how they fly.</summary>
        void Scatter()
        {
            var rng = new System.Random(1234);
            float R() => (float)rng.NextDouble();
            Vector3 Unit() => new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1).normalized;
            float k = anatomy.K;
            int per = Mathf.Max(4, 14 - strikes.Count);
            foreach (var st in strikes)
            {
                if (st.Part.Kind == Anatomy.Kind.Bone)
                {
                    for (int i = 0; i < per; i++)
                    {
                        float size = Mathf.Lerp(0.004f, 0.012f, R() * R()) * k;
                        var (v, n) = Chip(size, rng);
                        chips.Add(new Bit
                        {
                            Born = st.S, P0 = st.Point + Unit() * 0.006f * k, V = Dir * Mathf.Lerp(0.15f, 0.5f, R()) + Unit() * Mathf.Lerp(0.12f, 0.38f, R()),
                            Axis = Unit(), Spin = Mathf.Lerp(3, 12, R()), Size = size, Verts = v, Norms = n,
                        });
                    }
                }
                if (st.Part.Bleeds)
                {
                    int count = Mathf.Max(30, 90 - strikes.Count * 6);
                    for (int i = 0; i < count; i++)
                    {
                        // Mostly onward with the round, some blown back out of the entry.
                        var away = R() < 0.25f ? -Dir * Mathf.Lerp(0.05f, 0.25f, R()) : Dir * Mathf.Lerp(0.1f, 0.45f, R());
                        drops.Add(new Bit
                        {
                            Born = st.S, P0 = st.Point + Unit() * 0.008f * k, V = away + Unit() * Mathf.Lerp(0.08f, 0.42f, R()),
                            Axis = Unit(), Size = Mathf.Lerp(0.0025f, 0.0075f, R() * R()) * k,
                        });
                    }
                }
            }
            if (chips.Count > 0) chipMesh = DynamicMesh("Splinters", anatomy.Bone);
            if (drops.Count > 0) dropMesh = DynamicMesh("Blood", Glowing(new Color(0.42f, 0.015f, 0.02f), new Color(0.45f, 0.01f, 0.01f)));
        }

        /// <summary>A bone splinter: a jagged bipyramid, flat shaded, long along z.</summary>
        static (Vector3[] v, Vector3[] n) Chip(float size, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            var basePts = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2 / 3 + R() * 0.8f;
                basePts[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), (R() - 0.5f) * 0.4f) * size * Mathf.Lerp(0.35f, 0.6f, R());
            }
            var top = new Vector3((R() - 0.5f) * 0.3f, (R() - 0.5f) * 0.3f, 1) * size * Mathf.Lerp(0.8f, 1.4f, R());
            var bot = new Vector3((R() - 0.5f) * 0.3f, (R() - 0.5f) * 0.3f, -1) * size * Mathf.Lerp(0.4f, 0.8f, R());
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            for (int i = 0; i < 3; i++)
            {
                var a = basePts[i];
                var b = basePts[(i + 1) % 3];
                foreach (var apex in new[] { top, bot })
                {
                    var fn = Vector3.Cross(b - a, apex - a).normalized;
                    var mid = (a + b + apex) / 3;
                    if (Vector3.Dot(fn, mid) < 0) { v.Add(a); v.Add(apex); v.Add(b); fn = -fn; }
                    else { v.Add(a); v.Add(b); v.Add(apex); }
                    n.Add(fn); n.Add(fn); n.Add(fn);
                }
            }
            return (v.ToArray(), n.ToArray());
        }

        Mesh DynamicMesh(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mesh;
        }

        /// <summary>Everything as it stands when the round is `s` metres past the entry (negative: still on its way).</summary>
        public void Evaluate(float s)
        {
            float k = anatomy.K;
            foreach (var st in strikes)
            {
                float dt = (s - st.S) / Pace;
                if (dt < 0) continue;
                if (!st.Fired) Fire(st);
                if (st.A != null || st.B != null)
                {
                    // The broken halves swing apart about their far ends, pushed on by the round.
                    float ang = st.Angle * (1 - Mathf.Exp(-dt * 3.2f));
                    Swing(st.A, st.PivotA, st.AxisA, ang);
                    Swing(st.B, st.PivotB, st.AxisB, ang);
                }
                if (st.Part.Kind == Anatomy.Kind.Organ && st.Part.Go != null)
                {
                    // The organ lurches with the shock wave, then settles a little swollen.
                    float sc = 1 + 0.09f * Mathf.Sin(Mathf.Clamp01(dt / 0.35f) * Mathf.PI) * Mathf.Exp(-dt) + 0.02f * Mathf.Clamp01(dt * 4);
                    var c = st.Part.Centre;
                    st.Part.Go.transform.localScale = Vector3.one * sc;
                    st.Part.Go.transform.position = c * (1 - sc);
                }
                if (st.Mist != null && st.MistMat != null)
                {
                    float r = (st.Part.Bleeds ? 0.015f + 0.06f * (1 - Mathf.Exp(-dt * 2.6f)) : 0.008f + 0.025f * (1 - Mathf.Exp(-dt * 3))) * k;
                    st.Mist.localScale = Vector3.one * r;
                    st.Mist.position = st.Point + Dir * 0.06f * dt;
                    var col = st.MistMat.GetColor("_BaseColor");
                    col.a = (st.Part.Bleeds ? 0.4f : 0.15f) * Mathf.Exp(-dt * 1.5f);
                    st.MistMat.SetColor("_BaseColor", col);
                }
            }
            Fly(chipMesh, chips, s, false);
            Fly(dropMesh, drops, s, true);

            // The wound channel glowing behind the round, and a flare where it goes in.
            if (s >= 0)
            {
                if (channel == null)
                {
                    var mb = new MeshBuilder();
                    mb.Tube(Vector3.zero, Vector3.forward, 1, 1, 10, true);
                    channel = mb.Object("Channel", Tinted("Additive", new Color(1.4f, 0.32f, 0.12f, 0.9f)), transform, shadows: false).transform;
                    flare = Sphere("Flare", Tinted("Additive", new Color(0.7f, 0.3f, 0.12f, 1)));
                }
                float len = Mathf.Clamp(s, 0.001f, Exit + 0.05f);
                channel.position = Entry;
                channel.rotation = Quaternion.LookRotation(Dir);
                float rad = 0.0035f * k * (1 + 0.6f * Mathf.Clamp01(s / 0.2f));
                channel.localScale = new Vector3(rad, rad, len);
                float ft = s / Pace;
                flare!.position = Entry;
                flare.localScale = Vector3.one * (ft < 0.5f ? Mathf.Lerp(0.006f, 0.016f, ft / 0.5f) * (1 - ft / 0.5f) + 0.0001f : 0.0001f);
            }
        }

        static void Swing(GameObject? go, Vector3 pivot, Vector3 axis, float angle)
        {
            if (go == null) return;
            var q = Quaternion.AngleAxis(angle, axis);
            go.transform.rotation = q;
            go.transform.position = pivot - q * pivot;
        }

        void Fire(Strike st)
        {
            st.Fired = true;
            var p = st.Part;
            float k = anatomy.K;
            if (p.Splits && p.Tubes.Count > 0 && p.Go != null)
            {
                // Snap across the tube nearest the hit.
                var tube = p.Tubes[0];
                float bestD = float.MaxValue;
                foreach (var t in p.Tubes)
                {
                    float d = (t.Curve.At(t.Curve.Nearest(st.Point)) - st.Point).sqrMagnitude;
                    if (d < bestD) { bestD = d; tube = t; }
                }
                float u = tube.Curve.Nearest(st.Point);
                var plane = tube.Curve.Tangent(u);
                if (u > 0.06f && u < 0.94f)
                {
                    var hit = tube.Curve.At(u);
                    st.A = p.Build(anatomy.Root, 1, hit, plane, 0.006f * k);
                    st.B = p.Build(anatomy.Root, 2, hit, plane, 0.006f * k);
                    bool startNear = Vector3.Dot(tube.Curve.At(0) - hit, plane) < 0;
                    st.PivotA = startNear ? tube.Curve.At(0) : tube.Curve.At(1);
                    st.PivotB = startNear ? tube.Curve.At(1) : tube.Curve.At(0);
                    st.AxisA = Axis(hit - st.PivotA);
                    st.AxisB = Axis(hit - st.PivotB);
                    st.Angle = Mathf.Lerp(9, 16, Mathf.Abs(Mathf.Sin(st.S * 37)));
                    if (st.A != null || st.B != null) p.Go.SetActive(false);
                }
            }
            if (st.A == null && st.B == null)
            {
                // Cracked rather than snapped: a dark hole where it went through.
                st.Hole = Sphere("Hole", anatomy.Dark);
                st.Hole.position = st.Point + Dir * 0.004f * k;
                st.Hole.localScale = Vector3.one * (p.Kind == Anatomy.Kind.Organ ? 0.011f : 0.008f) * k;
            }
            var mist = p.Bleeds ? new Color(p.Blood.r, p.Blood.g, p.Blood.b, 0.5f) : new Color(0.7f, 0.66f, 0.6f, 0.15f);
            st.MistMat = Tinted("Fade", mist);
            st.Mist = Sphere("Mist", st.MistMat);
            st.Mist.position = st.Point;
            st.Mist.localScale = Vector3.one * 0.0001f;
        }

        Vector3 Axis(Vector3 arm)
        {
            var a = Vector3.Cross(arm, Dir);
            return a.sqrMagnitude > 1e-10f ? a.normalized : Vector3.up;
        }

        /// <summary>Moves every splinter or drop to where it is at `s`; ones not yet thrown collapse to nothing.</summary>
        void Fly(Mesh? mesh, List<Bit> bits, float s, bool liquid)
        {
            if (mesh == null) return;
            int per = liquid ? Octa.Length : bits.Count > 0 ? bits[0].Verts.Length : 0;
            var v = new Vector3[bits.Count * per];
            var n = new Vector3[v.Length];
            var g = Vector3.down * 0.7f;
            const float Drag = 1.3f;
            for (int b = 0; b < bits.Count; b++)
            {
                var bit = bits[b];
                float dt = (s - bit.Born) / Pace;
                int o = b * per;
                if (dt < 0)
                {
                    for (int i = 0; i < per; i++) { v[o + i] = bit.P0; n[o + i] = Vector3.up; }
                    continue;
                }
                var pos = bit.P0 + bit.V * ((1 - Mathf.Exp(-Drag * dt)) / Drag) + 0.5f * g * dt * dt;
                if (liquid)
                {
                    // A drop stretched along its flight, shrinking as it thins out.
                    var vel = bit.V * Mathf.Exp(-Drag * dt) + g * dt;
                    var q = Quaternion.LookRotation(vel.sqrMagnitude > 1e-8f ? vel : Vector3.forward);
                    float size = bit.Size * Mathf.Clamp01(1.6f - dt * 0.6f);
                    var scale = new Vector3(size, size, size * (1.6f + vel.magnitude * 4));
                    for (int i = 0; i < per; i++)
                    {
                        v[o + i] = pos + q * Vector3.Scale(Octa[i], scale);
                        n[o + i] = q * Octa[i];
                    }
                }
                else
                {
                    var q = Quaternion.AngleAxis(bit.Spin * dt * Mathf.Rad2Deg, bit.Axis) * Quaternion.LookRotation(bit.V);
                    for (int i = 0; i < per; i++)
                    {
                        v[o + i] = pos + q * bit.Verts[i];
                        n[o + i] = q * bit.Norms[i];
                    }
                }
            }
            bool first = mesh.vertexCount != v.Length;
            mesh.vertices = v;
            mesh.normals = n;
            if (first)
            {
                var tris = new int[liquid ? bits.Count * OctaTris.Length : v.Length];
                for (int b = 0; b < (liquid ? bits.Count : 0); b++)
                    for (int i = 0; i < OctaTris.Length; i++) tris[b * OctaTris.Length + i] = b * per + OctaTris[i];
                if (!liquid) for (int i = 0; i < tris.Length; i++) tris[i] = i;
                mesh.triangles = tris;
            }
            mesh.bounds = new Bounds(Entry, Vector3.one * 6);
        }

        static readonly Vector3[] Octa = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly int[] OctaTris = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };

        Transform Sphere(string name, Material mat)
        {
            if (sphere == null)
            {
                var mb = new MeshBuilder();
                Shapes.Ellipsoid(mb, Vector3.zero, Quaternion.identity, Vector3.one, null, 12, 16);
                sphere = mb.Build("Unit sphere");
            }
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = sphere;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        /// <summary>
        /// Dims the world around the body for the close-up: nested dark shells round the entry,
        /// so the ground and sky fall away softly while the body and bones inside stay bright.
        /// </summary>
        public void Backdrop(bool on)
        {
            if (on && backdrop == null)
            {
                backdrop = new GameObject("Backdrop");
                backdrop.transform.SetParent(transform, false);
                foreach (var r in new[] { 1.8f, 2.6f, 3.8f })
                {
                    var m = Tinted("Fade", new Color(0.01f, 0.015f, 0.03f, 0.32f));
                    m.SetFloat("_Cull", 0);
                    m.renderQueue = (int)RenderQueue.Transparent - 5;
                    var shell = Sphere("Shell", m);
                    shell.SetParent(backdrop.transform, false);
                    shell.position = Entry;
                    shell.localScale = Vector3.one * r;
                }
            }
            if (backdrop != null) backdrop.SetActive(on);
        }

        static Material Tinted(string template, Color c)
        {
            var m = new Material(Library.Template(template));
            m.SetColor("_BaseColor", c);
            return m;
        }

        static Material Glowing(Color c, Color e)
        {
            var m = Library.Glow(c, e);
            m.SetFloat("_Smoothness", 0.75f);
            m.SetFloat("_Metallic", 0);
            return m;
        }

        /// <summary>Puts the body's own materials and meshes back and removes the X-ray parts.</summary>
        public void End()
        {
            foreach (var (r, mats, shadows) in saved)
            {
                if (r == null) continue;
                r.sharedMaterials = mats;
                r.shadowCastingMode = shadows;
            }
            saved.Clear();
            foreach (var g in ghosts)
            {
                if (g.Filter != null) g.Filter.sharedMesh = g.Original;
                Destroy(g.Copy);
            }
            ghosts.Clear();
            PostFx.Xray(0);
            PostFx.Flash(0);
            Destroy(gameObject);
        }
    }
}
