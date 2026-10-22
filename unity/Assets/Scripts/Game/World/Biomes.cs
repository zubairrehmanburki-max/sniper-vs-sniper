using System;
using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Per-map set dressing for the land maps (desert, harbor, alpine, range), plus the shared pieces
    /// they are built from: the cover boxes drawn as boulders, log piles, AC units or concrete;
    /// procedural boulders and firs; models sized to a height; and a ring of far hills that hides the
    /// terrain's edge. Static geometry is merged into one mesh per material.
    /// </summary>
    public static class Biomes
    {
        /// <summary>Maps that get their own look here (and skip the generic trees in Dressing).</summary>
        public static bool Handles(string id) => id is "dry-valley" or "harbor-rooftops" or "alpine-ridge" or "training-lane";

        public static void Build(DuelMap map, Transform parent, List<Flag> flags)
        {
            if (!Handles(map.Id)) return;
            var root = new GameObject("Biome " + map.Id).transform;
            root.SetParent(parent, false);
            switch (map.Id)
            {
                case "dry-valley": DryValley.Build(map, root); break;
                case "harbor-rooftops": Harbor.Build(map, root); break;
                case "alpine-ridge": AlpineRidge.Build(map, root); break;
                case "training-lane": TrainingRange.Build(map, root, flags); break;
            }
        }

        // ---- the lane ----------------------------------------------------------------------------

        /// <summary>
        /// The corridor every sight line between a spot on one side and a spot on the other stays in:
        /// anything taller than a pebble is kept out of it, so the scene never hides what the hit model
        /// says is open.
        /// </summary>
        public readonly struct Lane
        {
            public readonly float HalfWidth, SMin, SMax;

            public Lane(float halfWidth, float sMin, float sMax)
            {
                HalfWidth = halfWidth;
                SMin = sMin;
                SMax = sMax;
            }

            public bool Contains(float x, float s, float margin = 0) =>
                Mathf.Abs(x) < HalfWidth + margin && s > SMin - margin && s < SMax + margin;
        }

        public static Lane LaneOf(DuelMap map, float margin = 12)
        {
            float half = 0, sMin = float.MaxValue, sMax = float.MinValue;
            foreach (var side in new[] { Side.A, Side.B })
            {
                foreach (var p in map.Spots[side])
                {
                    half = Mathf.Max(half, Mathf.Abs((float)p.X));
                    sMin = Mathf.Min(sMin, (float)-p.Z);
                    sMax = Mathf.Max(sMax, (float)-p.Z);
                }
            }
            return new Lane(half + margin, sMin - 6, sMax + 6);
        }

        /// <summary>Distance from (x, s) to the nearest firing spot, either side.</summary>
        public static float NearestSpot(DuelMap map, float x, float s)
        {
            float best = float.MaxValue;
            foreach (var side in new[] { Side.A, Side.B })
                foreach (var p in Maps.FiringSpots(map, side))
                    best = Mathf.Min(best, new Vector2(x - (float)p.X, s + (float)p.Z).magnitude);
            return best;
        }

        /// <summary>
        /// Is (x, s) within `margin` of a post platform or of the straight line between any two posts
        /// that can be rolled against each other? Scenery keeps off them so the guaranteed line of
        /// sight is not hidden behind a tree the rules don't know about.
        /// </summary>
        public static bool InSightLine(DuelMap map, float x, float s, float margin)
        {
            if (map.Posts == null) return false;
            var p = new Vector2(x, s);
            foreach (var pair in Maps.Pairs(map))
            {
                var a = map.Posts.A[pair.A].Spot;
                var b = map.Posts.B[pair.B].Spot;
                var u = new Vector2((float)a.X, (float)-a.Z);
                var v = new Vector2((float)b.X, (float)-b.Z);
                var d = v - u;
                float t = Mathf.Clamp01(Vector2.Dot(p - u, d) / Mathf.Max(1e-3f, d.sqrMagnitude));
                if ((u + d * t - p).sqrMagnitude < margin * margin) return true;
            }
            return false;
        }

        /// <summary>A rules box in Unity coordinates.</summary>
        public static Bounds ToUnity(Box b)
        {
            var p = Library.ToUnity(b.Min);
            var q = Library.ToUnity(b.Max);
            var bounds = new Bounds();
            bounds.SetMinMax(Vector3.Min(p, q), Vector3.Max(p, q));
            return bounds;
        }

        // ---- cover -------------------------------------------------------------------------------

        /// <summary>Materials for drawing the rules' cover boxes.</summary>
        public sealed class CoverKit
        {
            public Material Rock = null!;
            public Material? Snow;
            /// <summary>Boulders whose top is above this height get a cap of snow.</summary>
            public float SnowAbove = float.MaxValue;
            public CoverLook Default = CoverLook.Rock;
            /// <summary>Scanned boulders to draw rock cover with (the first downloaded ones); none: procedural boulders.</summary>
            public string[] Scans = System.Array.Empty<string>();
            /// <summary>Colour wash over the scans (sun-bleached sandstone, cold granite).</summary>
            public Color? ScanTint;
        }

        /// <summary>
        /// Draws every cover box so what the player sees is what stops a bullet: rock is a boulder that
        /// fills its box, wood a stack of logs, metal an AC unit, concrete a cast block. Boxes listed in
        /// `skip` are drawn by the map itself.
        /// </summary>
        public static void Cover(DuelMap map, Transform root, CoverKit kit, Func<Box, bool>? skip = null)
        {
            var rock = new MeshBuilder();
            var snow = new MeshBuilder();
            var bark = new MeshBuilder();
            var metal = new MeshBuilder();
            var grille = new MeshBuilder();
            var concrete = new MeshBuilder();
            var ground = map.Terrain ?? map.Ground;
            int seed = 1;
            var scans = Wilds.Present(kit.Scans);
            var tinted = new Dictionary<Material, Material>();
            foreach (var box in map.Cover)
            {
                if (skip != null && skip(box)) continue;
                var b = ToUnity(box);
                seed++;
                // On a slope the box's floor is the ground at its centre; reach down to its lowest corner.
                float low = b.min.y;
                foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
                    low = Mathf.Min(low, (float)ground(b.center.x + dx * b.extents.x, -(b.center.z + dz * b.extents.z)) - 0.15f);
                switch (box.Look ?? kit.Default)
                {
                    case CoverLook.Rock when scans.Length > 0:
                        // A scanned boulder stretched over the box, with a cap of snow up high.
                        Wilds.FitRock(scans[seed % scans.Length], b, low, seed, root, kit.ScanTint, tinted);
                        if (kit.Snow != null && b.max.y > kit.SnowAbove)
                            Boulder(snow, new Vector3(b.center.x, b.max.y - 0.02f, b.center.z), new Vector3(b.size.x * 0.75f, 0.16f, b.size.z * 0.7f), seed + 50, 0.7f, 0.1f, 0);
                        break;
                    case CoverLook.Rock:
                        // A boxy boulder a little larger than its box, so no corner of the box is bare air.
                        var size = Vector3.Scale(new Vector3(b.size.x, b.max.y - low, b.size.z), new Vector3(1.14f, 1.03f, 1.18f));
                        Boulder(rock, new Vector3(b.center.x, low, b.center.z), size, seed, 0.55f, 0.12f, 0);
                        if (kit.Snow != null && b.max.y > kit.SnowAbove)
                            Boulder(snow, new Vector3(b.center.x, b.max.y - 0.14f, b.center.z), new Vector3(size.x * 0.8f, 0.24f, size.z * 0.8f), seed + 50, 0.7f, 0.1f, 0);
                        break;
                    case CoverLook.Wood:
                        LogPile(bark, b, new System.Random(seed));
                        break;
                    case CoverLook.Metal:
                        AcUnit(metal, grille, b);
                        break;
                    default:
                        concrete.Box(new Vector3(b.center.x, (low + b.max.y) / 2, b.center.z), new Vector3(b.size.x, b.max.y - low, b.size.z));
                        break;
                }
            }
            if (rock.VertexCount > 0) rock.Object("Cover rock", kit.Rock, root);
            if (snow.VertexCount > 0 && kit.Snow != null) snow.Object("Cover snow", kit.Snow, root);
            if (bark.VertexCount > 0) bark.Object("Cover logs", Bark, root);
            if (metal.VertexCount > 0) metal.Object("Cover metal", Library.Plain(new Color(0.66f, 0.68f, 0.67f), 0.35f, 0.3f), root);
            if (grille.VertexCount > 0) grille.Object("Cover grilles", Library.Plain(new Color(0.06f, 0.065f, 0.07f), 0.3f, 0.6f), root, shadows: false);
            if (concrete.VertexCount > 0) concrete.Object("Cover concrete", Library.Surface("concrete_wall_008", 4, new Color(0.82f, 0.8f, 0.76f)), root);
        }

        public static Material Bark => Library.Surface("weathered_planks", 1.6f, new Color(0.6f, 0.47f, 0.36f));

        /// <summary>Logs stacked in a pyramid along the box's long side, filling it to its top.</summary>
        public static void LogPile(MeshBuilder bark, Bounds b, System.Random rng)
        {
            bool alongX = b.size.x >= b.size.z;
            float len = (alongX ? b.size.x : b.size.z) + 0.3f;
            float depth = alongX ? b.size.z : b.size.x;
            const int Bottom = 4;
            float r = depth / (2 * Bottom);
            int layers = Mathf.Max(1, Mathf.Min(Bottom, Mathf.RoundToInt((b.size.y - 2 * r) / (1.73f * r)) + 1));
            for (int k = 0; k < layers; k++)
            {
                for (int j = 0; j < Bottom - k; j++)
                {
                    float across = -depth / 2 + r + (j + k * 0.5f) * 2 * r;
                    float y = b.min.y + 0.05f + r + k * 1.73f * r;
                    float a = -len / 2 + (float)rng.NextDouble() * 0.25f, e = len / 2 - (float)rng.NextDouble() * 0.25f;
                    float rr = r * (0.88f + (float)rng.NextDouble() * 0.12f);
                    var from = alongX ? new Vector3(b.center.x + a, y, b.center.z + across) : new Vector3(b.center.x + across, y, b.center.z + a);
                    var to = alongX ? new Vector3(b.center.x + e, y, b.center.z + across) : new Vector3(b.center.x + across, y, b.center.z + e);
                    bark.Tube(from, to, rr, 9);
                }
            }
        }

        /// <summary>
        /// A rooftop condenser filling the box exactly: a painted sheet-metal top and plinth joined by
        /// corner posts, a dark coil behind horizontal fins on both long faces and one end, a service
        /// panel on the other end with its refrigerant lines, and two fan guards (rim, inner ring, cross
        /// bars, hub) over dark fan wells on top. Only the guards stand a few centimetres proud.
        /// </summary>
        public static void AcUnit(MeshBuilder metal, MeshBuilder grille, Bounds b)
        {
            bool alongX = b.size.x >= b.size.z;
            float len = alongX ? b.size.x : b.size.z, wid = alongX ? b.size.z : b.size.x, h = b.size.y;
            // Local frame: a along the long side, c across it, y up from the box's centre.
            Vector3 P(float a, float y, float c) => b.center + (alongX ? new Vector3(a, y, c) : new Vector3(c, y, a));
            Vector3 S(float a, float y, float c) => alongX ? new Vector3(a, y, c) : new Vector3(c, y, a);
            const float Top = 0.12f, Plinth = 0.14f, Post = 0.07f, Inset = 0.04f;
            float coilH = h - Top - Plinth, coilY = -h / 2 + Plinth + coilH / 2;

            // Cabinet: top, plinth, posts, and the dark coil inside them.
            metal.Box(P(0, h / 2 - Top / 2, 0), S(len, Top, wid));
            grille.Box(P(0, -h / 2 + Plinth / 2, 0), S(len - 0.02f, Plinth, wid - 0.02f));
            foreach (float ea in new[] { -1f, 1f })
                foreach (float ec in new[] { -1f, 1f })
                    metal.Box(P(ea * (len - Post) / 2, coilY, ec * (wid - Post) / 2), S(Post, coilH, Post));
            grille.Box(P(0, coilY, 0), S(len - 2 * Inset, coilH, wid - 2 * Inset));

            // Fins across the coil faces, set just inside the box so the outline stays the box.
            float fins = Mathf.Max(4, Mathf.Floor(coilH / 0.085f));
            for (int i = 0; i < fins; i++)
            {
                float y = coilY - coilH / 2 + (i + 0.5f) * coilH / fins;
                foreach (float ec in new[] { -1f, 1f })
                    metal.Box(P(0, y, ec * (wid / 2 - 0.012f)), S(len - 2 * Post, 0.022f, 0.02f));
                metal.Box(P(len / 2 - 0.012f, y, 0), S(0.02f, 0.022f, wid - 2 * Post));
            }
            // A mid post splits each long coil into two bays.
            foreach (float ec in new[] { -1f, 1f })
                metal.Box(P(0, coilY, ec * (wid / 2 - 0.02f)), S(0.06f, coilH, 0.04f));
            // Service end: a solid panel and two insulated lines down to the roof and away along it.
            metal.Box(P(-len / 2 + 0.02f, coilY, 0), S(0.04f, coilH, wid - 2 * Post));
            for (int i = -1; i <= 1; i += 2)
            {
                var a = P(-len / 2 - 0.04f, coilY - coilH * 0.15f, i * 0.12f);
                var foot = new Vector3(a.x, b.min.y + 0.08f, a.z);
                grille.Tube(a, foot, 0.028f, 6, false);
                grille.Tube(foot, foot + (alongX ? Vector3.left : Vector3.back) * 0.7f, 0.028f, 6, false);
            }

            // Fans on top: a dark well under a guard of rim, ring, cross bars and hub.
            float fan = Mathf.Min(len / 2, wid) * 0.4f;
            for (int i = -1; i <= 1; i += 2)
            {
                var c = P(i * len * 0.24f, h / 2, 0);
                grille.Cylinder(c, fan, 0.025f, 16);
                metal.Annulus(c + Vector3.up * 0.045f, fan * 0.93f, fan * 1.04f, 16);
                metal.Annulus(c + Vector3.up * 0.045f, fan * 0.5f, fan * 0.56f, 12);
                metal.Box(c + Vector3.up * 0.04f, new Vector3(fan * 2, 0.012f, 0.025f));
                metal.Box(c + Vector3.up * 0.04f, new Vector3(0.025f, 0.012f, fan * 2));
                metal.Cylinder(c, fan * 0.16f, 0.05f, 10);
            }
        }

        // ---- procedural shapes ---------------------------------------------------------------------

        static readonly Vector3[]?[] icoV = new Vector3[]?[3];
        static readonly int[]?[] icoT = new int[]?[3];

        /// <summary>A unit icosphere, outward winding: 1 subdivision is 42 vertices, 2 is 162.</summary>
        static void Ico(int subdivisions, out Vector3[] verts, out int[] tris)
        {
            if (icoV[subdivisions] == null || icoT[subdivisions] == null)
            {
                float p = (1 + Mathf.Sqrt(5)) / 2;
                var v = new List<Vector3>
                {
                    new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0),
                    new Vector3(0, -1, p), new Vector3(0, 1, p), new Vector3(0, -1, -p), new Vector3(0, 1, -p),
                    new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1),
                };
                for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
                var f = new List<int>
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
                };
                for (int level = 0; level < subdivisions; level++)
                {
                    var mid = new Dictionary<long, int>();
                    int Mid(int a, int b)
                    {
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        if (mid.TryGetValue(key, out int m)) return m;
                        v.Add(((v[a] + v[b]) / 2).normalized);
                        return mid[key] = v.Count - 1;
                    }
                    var next = new List<int>();
                    for (int i = 0; i < f.Count; i += 3)
                    {
                        int a = f[i], b = f[i + 1], c = f[i + 2];
                        int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                        next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                    }
                    f = next;
                }
                // Unity's front face is where Cross(b − a, c − a) points: make that outward.
                for (int i = 0; i < f.Count; i += 3)
                {
                    Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0) (f[i + 1], f[i + 2]) = (f[i + 2], f[i + 1]);
                }
                icoV[subdivisions] = v.ToArray();
                icoT[subdivisions] = f.ToArray();
            }
            verts = icoV[subdivisions]!;
            tris = icoT[subdivisions]!;
        }

        static float Noise3(Vector3 p) =>
            (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z) + Mathf.PerlinNoise(p.z + 57.1f, p.x + 11.3f)) / 3;

        /// <summary>Smooth vertex normals from the faces around each vertex.</summary>
        public static Vector3[] Normals(IReadOnlyList<Vector3> v, IReadOnlyList<int> t)
        {
            var n = new Vector3[v.Count];
            for (int i = 0; i < t.Count; i += 3)
            {
                var fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                n[t[i]] += fn;
                n[t[i + 1]] += fn;
                n[t[i + 2]] += fn;
            }
            for (int i = 0; i < n.Length; i++) n[i] = n[i].normalized;
            return n;
        }

        /// <summary>
        /// A boulder standing on `baseCenter`, `size` across: a rounded box (boxy 1 = sphere, lower =
        /// squarer) roughened by noise, flat underneath, turned `yawDeg`.
        /// </summary>
        public static void Boulder(MeshBuilder mb, Vector3 baseCenter, Vector3 size, int seed, float boxy = 0.6f, float rough = 0.18f, float yawDeg = 0)
        {
            // Small stones don't need the finer sphere.
            Ico(size.magnitude < 1.6f ? 1 : 2, out var unit, out var tris);
            var verts = new Vector3[unit.Length];
            var rot = Quaternion.Euler(0, yawDeg, 0);
            var centre = baseCenter + Vector3.up * (size.y * 0.5f);
            var o = new Vector3(seed * 12.9898f % 100, seed * 78.233f % 100, seed * 37.719f % 100);
            static float Pow(float a, float e) => Mathf.Sign(a) * Mathf.Pow(Mathf.Abs(a), e);
            for (int i = 0; i < unit.Length; i++)
            {
                var d = unit[i];
                var q = new Vector3(Pow(d.x, boxy), Pow(d.y, boxy), Pow(d.z, boxy));
                float r = 1 + rough * (Noise3(d * 1.6f + o) - 0.35f) * 2 + rough * 0.5f * (Noise3(d * 4.1f + o) - 0.5f);
                var local = Vector3.Scale(q * r, size * 0.5f);
                local.y = Mathf.Max(local.y, -size.y * 0.46f);
                verts[i] = centre + rot * local;
            }
            mb.Smooth(verts, Normals(verts, tris), tris);
        }

        /// <summary>A low-poly fir for far slopes: a short trunk and four drooping tiers.</summary>
        public static void Fir(MeshBuilder needles, MeshBuilder trunk, Vector3 basePos, float height)
        {
            trunk.Tube(basePos + Vector3.down * 0.5f, basePos + Vector3.up * height * 0.3f, height * 0.02f, 5, false);
            for (int i = 0; i < 4; i++)
            {
                float y0 = height * (0.14f + i * 0.19f);
                float radius = height * 0.24f * (1 - i * 0.2f);
                needles.Cone(basePos + Vector3.up * y0, radius, height * (0.36f - i * 0.03f), 7, false);
            }
        }

        /// <summary>A hanging wire from a to b, `sag` metres low in the middle.</summary>
        public static void Wire(MeshBuilder mb, Vector3 a, Vector3 b, float sag, float thickness, int segments = 8)
        {
            var prev = a;
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                var p = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4 * t * (1 - t));
                mb.Beam(prev, p, thickness, thickness);
                prev = p;
            }
        }

        // ---- models --------------------------------------------------------------------------------

        /// <summary>
        /// A model scaled to `height` (but no wider than `maxSpan`), centred on `pos` across and standing
        /// `sink` metres into the ground.
        /// </summary>
        public static GameObject PlaceSized(string id, Vector3 pos, float yawDeg, float height, Transform parent, float sink = 0.2f, float maxSpan = float.MaxValue)
        {
            var go = Library.Place(id, pos, yawDeg, 1, parent);
            var b = Library.BoundsOf(go);
            if (b.size.y < 0.01f) return go;
            go.transform.localScale *= Mathf.Min(height / b.size.y, maxSpan / Mathf.Max(b.size.x, b.size.z, 0.01f));
            b = Library.BoundsOf(go);
            go.transform.position += new Vector3(pos.x - b.center.x, pos.y - sink - b.min.y, pos.z - b.center.z);
            return go;
        }

        /// <summary>Recolours a placed model with shared tinted copies of its materials (one copy per source material).</summary>
        public static void Tint(GameObject go, Color tint, Dictionary<Material, Material> cache)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (!cache.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + " tinted" };
                        // glTFast materials call it baseColorFactor; URP Lit, _BaseColor.
                        foreach (var prop in new[] { "baseColorFactor", "_BaseColor", "_Color" })
                            if (copy.HasProperty(prop)) copy.SetColor(prop, copy.GetColor(prop) * tint);
                        cache[m] = copy;
                    }
                    mats[i] = copy;
                }
                r.sharedMaterials = mats;
            }
        }

        // ---- horizon -------------------------------------------------------------------------------

        /// <summary>How the far hills look: height range, mesa-like flat tops, and materials.</summary>
        public sealed class Hills
        {
            public float MinHeight = 80, MaxHeight = 200;
            public bool Mesa;
            public Material Rock = null!;
            public Material? Snow;
            public float SnowAbove = float.MaxValue;
            public int Seed = 1;
            /// <summary>The map ground (x, s); the ring rises from its height at the inner edge so steep terrain never overtops it.</summary>
            public Func<float, float, float>? Ground;
        }

        /// <summary>
        /// A ring of hills or mountains around the 4 km terrain: it starts below the ground inside the
        /// terrain square, so the terrain's cut edge is always backed by slopes, and rises to a ragged
        /// skyline 2.5–3.5 km out that the haze softens.
        /// </summary>
        public static void Horizon(Transform root, float centreS, Hills range)
        {
            const int Around = 224, Out = 16;
            const float Inner = 1850, OuterR = 4200, BaseY = -25;
            var rng = new System.Random(range.Seed);
            float o1 = (float)rng.NextDouble() * 100, o2 = (float)rng.NextDouble() * 100;
            var v = new Vector3[Around * Out];
            for (int a = 0; a < Around; a++)
            {
                float th = a * 2 * Mathf.PI / Around;
                float cx = Mathf.Cos(th), sz = Mathf.Sin(th);
                float foot = BaseY + (range.Ground != null ? Mathf.Max(0, range.Ground(cx * Inner, centreS + sz * Inner)) : 0);
                // Peak height varies around the ring: broad massifs and sharper summits.
                float massif = Mathf.PerlinNoise(cx * 1.4f + o1, sz * 1.4f + o2);
                float peaks = 1 - Mathf.Abs(2 * Mathf.PerlinNoise(cx * 5f + o2, sz * 5f + o1) - 1);
                float h = Mathf.Lerp(range.MinHeight, range.MaxHeight, Mathf.Clamp01(massif * 0.7f + peaks * 0.5f - 0.1f));
                // The crest wanders in and out so the skyline isn't a circle.
                float crest = 0.38f + 0.16f * Mathf.PerlinNoise(cx * 2.3f + o1 + 7, sz * 2.3f + o2);
                for (int k = 0; k < Out; k++)
                {
                    float t = (float)k / (Out - 1);
                    float r = Mathf.Lerp(Inner, OuterR, t);
                    float profile = t < crest ? Mathf.Sin(t / crest * Mathf.PI / 2) : Mathf.Cos((t - crest) / (1 - crest) * Mathf.PI / 2);
                    profile = Mathf.Pow(Mathf.Clamp01(profile), 1.3f);
                    float detail = 0.75f + 0.5f * Mathf.PerlinNoise(cx * 11 + t * 3 + o1, sz * 11 + t * 2 + o2);
                    float u = profile * detail;
                    float y = range.Mesa
                        ? h * Mathf.SmoothStep(0, 1, Mathf.Clamp01((u - 0.3f) / 0.18f)) * (0.88f + 0.12f * u) + h * 0.12f * u
                        : h * u;
                    v[a * Out + k] = new Vector3(cx * r, foot + (k == 0 ? 0 : y), centreS + sz * r);
                }
            }
            var rockT = new List<int>();
            var snowT = new List<int>();
            for (int a = 0; a < Around; a++)
            {
                int a2 = (a + 1) % Around;
                for (int k = 0; k < Out - 1; k++)
                {
                    int i00 = a * Out + k, i01 = a * Out + k + 1, i10 = a2 * Out + k, i11 = a2 * Out + k + 1;
                    foreach (var (p, q, w) in new[] { (i00, i01, i11), (i00, i11, i10) })
                    {
                        // Faces point up and in (toward the lane), so wind them that way.
                        var nm = Vector3.Cross(v[q] - v[p], v[w] - v[p]);
                        bool flip = nm.y < 0;
                        float midY = (v[p].y + v[q].y + v[w].y) / 3;
                        var list = range.Snow != null && midY > range.SnowAbove && Mathf.Abs(nm.normalized.y) > 0.45f ? snowT : rockT;
                        list.Add(p);
                        list.Add(flip ? w : q);
                        list.Add(flip ? q : w);
                    }
                }
            }
            var all = new List<int>(rockT);
            all.AddRange(snowT);
            var normals = Normals(v, all);
            var rockMb = new MeshBuilder();
            rockMb.Smooth(v, normals, rockT);
            rockMb.Object("Far hills", range.Rock, root, shadows: false);
            if (snowT.Count > 0 && range.Snow != null)
            {
                var snowMb = new MeshBuilder();
                snowMb.Smooth(v, normals, snowT);
                snowMb.Object("Far snow", range.Snow, root, shadows: false);
            }
        }
    }
}
