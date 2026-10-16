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
            readonly Dictionary<string, List<Impostor.Field>> fields = new Dictionary<string, List<Impostor.Field>>();
            readonly Dictionary<string, (float? height, Color? tint)> looks = new Dictionary<string, (float?, Color?)>();
            readonly System.Random pick = new System.Random(7);
            public int Count { get; private set; }

            /// <summary>
            /// Sets a kind's height at scale 1 (null: as scanned; given, it is the tallest variant's
            /// height and the others keep their proportion to it) and a colour wash over its picture.
            /// </summary>
            public Flora Kind(string id, float? height = null, Color? tint = null)
            {
                looks[id] = (height, tint);
                return this;
            }

            /// <summary>One field per variant of a kind (a model holding a row of plants gives several); empty when missing.</summary>
            List<Impostor.Field> Fields(string id)
            {
                if (fields.TryGetValue(id, out var list)) return list;
                list = new List<Impostor.Field>();
                looks.TryGetValue(id, out var look);
                int n = Impostor.Variants(id);
                var cards = new List<Impostor.Card>();
                for (int k = 0; k < n; k++)
                {
                    var card = Impostor.Of(n > 1 ? $"{id}#{k}" : id);
                    if (card != null) cards.Add(card);
                }
                float tallest = 0.01f;
                foreach (var c in cards) tallest = Mathf.Max(tallest, c.Height);
                float scale = look.height.HasValue ? look.height.Value / tallest : 1;
                foreach (var c in cards) list.Add(new Impostor.Field(c, c.Height * scale, look.tint));
                fields[id] = list;
                return list;
            }

            /// <summary>True when the kind's model was downloaded.</summary>
            public bool Has(string id) => Fields(id).Count > 0;

            /// <summary>The kinds in `ids` that exist.</summary>
            public string[] Available(params string[] ids) => System.Array.FindAll(ids, Has);

            /// <summary>Plants one (a random variant) with its foot at `foot`; false when the kind is missing.</summary>
            public bool Plant(string id, Vector3 foot, float yawDeg, float scale, bool mirror = false)
            {
                var list = Fields(id);
                if (list.Count == 0) return false;
                list[pick.Next(list.Count)].Add(foot, yawDeg, scale, mirror);
                Count++;
                return true;
            }

            /// <summary>Merges each variant of each kind into its mesh under `parent`.</summary>
            public void Build(Transform parent, string name)
            {
                var root = new GameObject(name).transform;
                root.SetParent(parent, false);
                int kinds = 0;
                foreach (var kv in fields)
                    for (int k = 0; k < kv.Value.Count; k++, kinds++) kv.Value[k].Build(kv.Key + "#" + k, root);
                Debug.Log($"[Wilds] {name}: {Count} plants, {kinds} pictures");
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

        /// <summary>
        /// A carpet of ground cover over the lane floor itself, kept to `maxHeight` (a pebble's height,
        /// so it never hides what the rules call open), clear of the ground round every spot and of
        /// the strip straight ahead of each, where a prone shooter's eye is lowest.
        /// </summary>
        public static void LaneCarpet(Flora flora, SniperDuel.Rules.DuelMap map, Biomes.Lane lane, System.Random rng, string[] kinds,
            int count, float scaleLo, float scaleHi, System.Func<float, float, float> Y, System.Func<float, float, bool>? keep = null)
        {
            if (kinds.Length == 0) return;
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = 0; i < count; i++)
            {
                float x = R(-lane.HalfWidth - 4, lane.HalfWidth + 4), s = R(lane.SMin - 30, lane.SMax + 30);
                if (Biomes.NearestSpot(map, x, s) < 6 || InFront(map, x, s) || (keep != null && !keep(x, s))) continue;
                flora.Plant(kinds[rng.Next(kinds.Length)], new Vector3(x, Y(x, s) - 0.02f, s), R(0, 360), R(scaleLo, scaleHi), rng.Next(2) == 0);
            }
        }

        /// <summary>True in the 5 m-wide, 35 m-long strip ahead of any spot, toward the other side.</summary>
        public static bool InFront(SniperDuel.Rules.DuelMap map, float x, float s)
        {
            foreach (var side in new[] { SniperDuel.Rules.Side.A, SniperDuel.Rules.Side.B })
            {
                float dir = side == SniperDuel.Rules.Side.A ? 1 : -1;
                foreach (var p in map.Spots[side])
                {
                    float ahead = (s - (float)-p.Z) * dir;
                    if (Mathf.Abs(x - (float)p.X) < 2.5f && ahead > -1 && ahead < 35) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Clumps of ground cover on the lane floor: `patches` centres anywhere LaneCarpet would plant,
        /// each a tight patch of `per` tufts within `radius` m, so the cover reads as tufts and bare
        /// ground at a prone eye rather than an even sprinkle of specks. Same limits as LaneCarpet.
        /// </summary>
        public static void LanePatches(Flora flora, SniperDuel.Rules.DuelMap map, Biomes.Lane lane, System.Random rng, string[] kinds,
            int patches, int per, float radius, float scaleLo, float scaleHi, System.Func<float, float, float> Y, System.Func<float, float, bool>? keep = null)
        {
            Clumps(flora, rng, kinds, patches, per, radius, () => LanePoint(map, lane, rng, keep), (x, s) => LaneOk(map, x, s, keep), Y, scaleLo, scaleHi, 0.02f);
        }

        /// <summary>A random point on the lane floor (and 4 m past its sides and 30 m past its ends) where low cover may lie; null when refused.</summary>
        public static Vector2? LanePoint(SniperDuel.Rules.DuelMap map, Biomes.Lane lane, System.Random rng, System.Func<float, float, bool>? keep = null)
        {
            float x = Mathf.Lerp(-lane.HalfWidth - 4, lane.HalfWidth + 4, (float)rng.NextDouble());
            float s = Mathf.Lerp(lane.SMin - 30, lane.SMax + 30, (float)rng.NextDouble());
            return LaneOk(map, x, s, keep) ? new Vector2(x, s) : (Vector2?)null;
        }

        /// <summary>A random point `rMin`..`rMax` m from a random spot, off the strip ahead of every spot; null when refused.</summary>
        public static Vector2? NearSpot(SniperDuel.Rules.DuelMap map, System.Random rng, float rMin, float rMax)
        {
            var spots = map.Spots[rng.Next(2) == 0 ? SniperDuel.Rules.Side.A : SniperDuel.Rules.Side.B];
            if (spots.Count == 0) return null;
            var p = spots[rng.Next(spots.Count)];
            float a = (float)rng.NextDouble() * 2 * Mathf.PI, d = Mathf.Lerp(rMin, rMax, (float)rng.NextDouble());
            float x = (float)p.X + Mathf.Cos(a) * d, s = (float)-p.Z + Mathf.Sin(a) * d;
            return Biomes.NearestSpot(map, x, s) >= rMin && !InFront(map, x, s) ? new Vector2(x, s) : (Vector2?)null;
        }

        /// <summary>
        /// The lane floor's loose stones: cobbles and pebbles wherever LaneCarpet would plant, and
        /// a scatter of small pebbles round the spots themselves (a shooter's own ground), all in `mat`.
        /// </summary>
        public static void LaneStones(Transform parent, SniperDuel.Rules.DuelMap map, Biomes.Lane lane, System.Random rng, Material mat, int count,
            System.Func<float, float, float> Y, System.Func<float, float, bool>? keep = null, float sizeHi = 0.42f)
        {
            Stones(parent, "Lane stones", mat, rng, count, 0.05f, sizeHi, () => LanePoint(map, lane, rng, keep), Y);
            Stones(parent, "Spot pebbles", mat, rng, count / 12, 0.04f, 0.16f, () =>
            {
                var at = NearSpot(map, rng, 2.2f, 6);
                return at != null && (keep == null || keep(at.Value.x, at.Value.y)) ? at : null;
            }, Y, 0.5f, 0.08f);
        }

        static bool LaneOk(SniperDuel.Rules.DuelMap map, float x, float s, System.Func<float, float, bool>? keep) =>
            Biomes.NearestSpot(map, x, s) >= 6 && !InFront(map, x, s) && (keep == null || keep(x, s));

        /// <summary>
        /// Loose stones, all in one mesh: `count` faceted pebbles and cobbles `sizeLo`..`sizeHi` m across
        /// (mostly small), each about `flat` as high as it is wide and half bedded in the ground, at points
        /// `where` draws (null to reject). Real lit geometry, so they cast shadows and catch the sun at a
        /// prone eye where a flat picture reads as a speck. No stone stands higher than `maxHeight`.
        /// </summary>
        public static GameObject? Stones(Transform parent, string name, Material mat, System.Random rng, int count, float sizeLo, float sizeHi,
            System.Func<Vector2?> where, System.Func<float, float, float> Y, float flat = 0.5f, float maxHeight = 0.18f)
        {
            var mb = new MeshBuilder { UvScale = 1.5f };
            float R() => (float)rng.NextDouble();
            const int Segs = 6;
            var low = new Vector3[Segs];
            var mid = new Vector3[Segs];
            int placed = 0;
            for (int i = 0, tries = 0; placed < count && tries < count * 6; tries++)
            {
                var at = where();
                if (at == null) continue;
                float size = Mathf.Lerp(sizeLo, sizeHi, R() * R() * R());
                float h = Mathf.Min(size * flat * Mathf.Lerp(0.6f, 1.2f, R()), maxHeight * 1.5f);
                float yaw = R() * Mathf.PI * 2, stretch = Mathf.Lerp(0.65f, 1f, R());
                // Bedded: the widest ring sits at ground level, a third of the stone below it.
                var c = new Vector3(at.Value.x, Y(at.Value.x, at.Value.y) - h * 0.35f, at.Value.y);
                Vector3 Ring(float a, float r, float y)
                {
                    float jr = r * Mathf.Lerp(0.78f, 1.15f, R());
                    float lx = Mathf.Cos(a) * jr, lz = Mathf.Sin(a) * jr * stretch;
                    return c + new Vector3(lx * Mathf.Cos(yaw) - lz * Mathf.Sin(yaw), y, lx * Mathf.Sin(yaw) + lz * Mathf.Cos(yaw));
                }
                for (int k = 0; k < Segs; k++)
                {
                    float a = k * 2 * Mathf.PI / Segs;
                    low[k] = Ring(a, size * 0.5f, 0);
                    mid[k] = Ring(a + Mathf.PI / Segs, size * 0.38f, h * Mathf.Lerp(0.55f, 0.75f, R()));
                }
                var top = Ring(R() * 6.28f, size * 0.12f * R(), h);
                var centre = c + Vector3.up * h * 0.4f;
                void Tri(Vector3 p, Vector3 q, Vector3 r)
                {
                    // Wind each facet outward, whatever order the rings were laid in.
                    if (Vector3.Dot(Vector3.Cross(q - p, r - p), (p + q + r) / 3 - centre) < 0) (q, r) = (r, q);
                    mb.Triangle(p, q, r);
                }
                for (int k = 0; k < Segs; k++)
                {
                    int n = (k + 1) % Segs;
                    Tri(low[k], low[n], mid[k]);
                    Tri(mid[k], low[n], mid[n]);
                    Tri(mid[k], mid[n], top);
                }
                placed++;
                i++;
            }
            if (mb.VertexCount == 0) return null;
            var go = mb.Object(name, mat, parent);
            Debug.Log($"[Wilds] {name}: {placed} stones");
            return go;
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

        /// <summary>The heaviest meshes under an object, by total triangles drawn (name × instances), for the budget log.</summary>
        public static string Heaviest(GameObject go, int top = 15)
        {
            var sum = new Dictionary<string, (long tris, int n)>();
            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                long t = 0;
                for (int i = 0; i < f.sharedMesh.subMeshCount; i++) t += f.sharedMesh.GetIndexCount(i) / 3;
                // Group by the placed model (the nearest ancestor under a group) rather than the part.
                var owner = f.transform;
                while (owner.parent != null && owner.parent.parent != null && owner.parent.parent.parent != null && owner.parent.name.IndexOf("(fitted)") < 0 && owner.parent.name.IndexOf("(cover)") < 0) owner = owner.parent;
                string key = owner.name;
                sum.TryGetValue(key, out var v);
                sum[key] = (v.tris + t, v.n + 1);
            }
            var list = new List<KeyValuePair<string, (long tris, int n)>>(sum);
            list.Sort((a, b) => b.Value.tris.CompareTo(a.Value.tris));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Mathf.Min(top, list.Count); i++) sb.Append($"\n  {list[i].Value.tris:N0} {list[i].Key} ({list[i].Value.n} meshes)");
            return sb.ToString();
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
