using System.Collections.Generic;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The living ground shared by the land maps: plants of many kinds planted as cut-out pictures
    /// (Impostor), hundreds for the cost of a few meshes, and photo-scanned boulders fitted to a box
    /// (the rules' cover) or set down as loose rocks. A kind the asset set lacks is skipped, so a
    /// partial download still builds.
    /// </summary>
    public static class Wilds
    {
        /// <summary>
        /// A bed of plants of several kinds: one picture and one merged mesh per kind. Each kind
        /// stands at its scanned size unless given a height (scale 1 = that height).
        /// </summary>
        public sealed class Flora
        {
            readonly Dictionary<string, Impostor.Field?> fields = new Dictionary<string, Impostor.Field?>();
            readonly Dictionary<string, (float? height, Color? tint)> looks = new Dictionary<string, (float?, Color?)>();
            public int Count { get; private set; }

            /// <summary>Sets a kind's height at scale 1 (null: as scanned) and a colour wash over its picture.</summary>
            public Flora Kind(string id, float? height = null, Color? tint = null)
            {
                looks[id] = (height, tint);
                return this;
            }

            Impostor.Field? Field(string id)
            {
                if (fields.TryGetValue(id, out var f)) return f;
                var card = Impostor.Of(id);
                looks.TryGetValue(id, out var look);
                f = card == null ? null : new Impostor.Field(card, look.height ?? card.Height, look.tint);
                fields[id] = f;
                return f;
            }

            /// <summary>True when the kind's model was downloaded.</summary>
            public bool Has(string id) => Field(id) != null;

            /// <summary>The kinds in `ids` that exist.</summary>
            public string[] Available(params string[] ids) => System.Array.FindAll(ids, Has);

            /// <summary>Plants one with its foot at `foot`; false when the kind is missing.</summary>
            public bool Plant(string id, Vector3 foot, float yawDeg, float scale, bool mirror = false)
            {
                var f = Field(id);
                if (f == null) return false;
                f.Add(foot, yawDeg, scale, mirror);
                Count++;
                return true;
            }

            /// <summary>Merges each kind into its mesh under `parent`.</summary>
            public void Build(Transform parent, string name)
            {
                var root = new GameObject(name).transform;
                root.SetParent(parent, false);
                foreach (var kv in fields) kv.Value?.Build(kv.Key, root);
                Debug.Log($"[Wilds] {name}: {Count} plants of {fields.Count} kinds");
            }
        }

        /// <summary>
        /// Clumps of plants: `clumps` centres drawn by `centre` (null to reject), each with `per`
        /// plants of one kind spread `radius` m round it, kept off ground `keep` refuses.
        /// </summary>
        public static void Clumps(Flora flora, System.Random rng, string[] kinds, int clumps, int per, float radius,
            System.Func<Vector2?> centre, System.Func<float, float, bool> keep, System.Func<float, float, float> Y,
            float minScale = 0.7f, float maxScale = 1.3f, float sink = 0.05f)
        {
            if (kinds.Length == 0) return;
            float R() => (float)rng.NextDouble();
            for (int c = 0, tries = 0; c < clumps && tries < clumps * 20; tries++)
            {
                var at = centre();
                if (at == null) continue;
                c++;
                string kind = kinds[rng.Next(kinds.Length)];
                int n = Mathf.Max(1, (int)(per * (0.5f + R())));
                for (int i = 0; i < n; i++)
                {
                    // Denser in the middle of the clump.
                    float a = R() * 2 * Mathf.PI, d = radius * R() * R();
                    float x = at.Value.x + Mathf.Cos(a) * d, s = at.Value.y + Mathf.Sin(a) * d;
                    if (!keep(x, s)) continue;
                    flora.Plant(kind, new Vector3(x, Y(x, s) - sink, s), R() * 360, Mathf.Lerp(minScale, maxScale, R()), rng.Next(2) == 0);
                }
            }
        }

        // ---- scanned rock ----------------------------------------------------------------------------

        /// <summary>The first `ids` that were downloaded.</summary>
        public static string[] Present(params string[] ids) => System.Array.FindAll(ids, id => Library.Model(id) != null);

        /// <summary>
        /// A scanned boulder filling a cover box: turned a quarter at a time (so its bounds stay tight),
        /// whichever way round best matches the box, then stretched on each axis to the box grown by
        /// `grow` across and `growUp` in height, its foot `sink` below `floor`. The scans are lumpy
        /// blocks, so a little over the box covers the box's corners with rock.
        /// </summary>
        public static Transform? FitRock(string id, Bounds box, float floor, int seed, Transform parent, Color? tint, Dictionary<Material, Material> tinted,
            float grow = 1.16f, float growUp = 1.06f, float sink = 0.12f)
        {
            if (Library.Model(id) == null) return null;
            var holder = new GameObject(id + " (cover)").transform;
            holder.SetParent(parent, false);
            var m = Library.Prop(id, holder);
            if (tint is Color c) Biomes.Tint(m, c, tinted);
            m.transform.localPosition = Vector3.zero;
            var raw = Library.BoundsOf(m);
            // Long side along the box's long side; which end faces the lane is the seed's choice.
            bool boxAlongX = box.size.x >= box.size.z, rockAlongX = raw.size.x >= raw.size.z;
            float yaw = (boxAlongX == rockAlongX ? 0 : 90) + (seed % 2) * 180;
            m.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var b = Library.BoundsOf(m);
            float height = box.max.y - floor + sink;
            var want = new Vector3(box.size.x * grow, height * growUp, box.size.z * grow);
            holder.localScale = new Vector3(want.x / Mathf.Max(0.05f, b.size.x), want.y / Mathf.Max(0.05f, b.size.y), want.z / Mathf.Max(0.05f, b.size.z));
            b = Library.BoundsOf(m);
            holder.position += new Vector3(box.center.x - b.center.x, floor - sink - b.min.y, box.center.z - b.center.z);
            return holder;
        }

        /// <summary>A scanned rock `size` m across (its largest side), its foot `sink` of its height into the ground.</summary>
        public static Transform? Rock(string id, Vector3 foot, float yawDeg, float size, Transform parent, Color? tint, Dictionary<Material, Material> tinted, float sink = 0.2f)
        {
            if (Library.Model(id) == null) return null;
            var m = Library.Prop(id);
            if (tint is Color c) Biomes.Tint(m, c, tinted);
            var holder = Library.Fit(m, size, false, parent);
            holder.localRotation = Quaternion.Euler(0, yawDeg, 0);
            float h = Library.BoundsOf(m).size.y;
            holder.localPosition = foot - Vector3.up * h * sink;
            return holder;
        }

        /// <summary>Triangles under an object (for the per-map budget log).</summary>
        public static long Triangles(GameObject go)
        {
            long n = 0;
            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null && f.GetComponent<Renderer>() is Renderer r && r.enabled)
                    for (int i = 0; i < f.sharedMesh.subMeshCount; i++) n += f.sharedMesh.GetIndexCount(i) / 3;
            foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (s.sharedMesh != null)
                    for (int i = 0; i < s.sharedMesh.subMeshCount; i++) n += s.sharedMesh.GetIndexCount(i) / 3;
            return n;
        }
    }
}
