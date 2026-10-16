using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The downloaded set pieces placed round each map, never in the lane between the firing spots:
    /// a stone-and-steel viaduct downstream of Twin Bridges and old jetties on its banks; more
    /// watchtowers (steel and timber) standing in the marsh; two more production platforms in the
    /// oil field; rusted containers on the harbor quays; an abandoned position, trenches, cliffs, a
    /// mesa and scanned rocks in the dry valley; spruce and rocks on the alpine flanks; rocks
    /// along the range. A piece the asset set lacks is simply left out.
    /// </summary>
    public static class Scenery
    {
        public static void Build(DuelMap map, Transform parent)
        {
            var root = new GameObject("Scenery").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(map.Id.GetHashCode() ^ 0x5ce4e);
            var terrain = map.Terrain ?? map.Ground;
            float Y(float x, float s) => (float)terrain(x, -s);
            float lane = (float)map.LaneLength;
            float water = (float)(map.Water ?? -1000);
            switch (map.Id)
            {
                case "twin-bridges":
                    // A high viaduct across the river 600 m beyond the enemy's bridge, in the scope's haze.
                    Put("bridge_high", root, new Vector3(0, water - 4, lane + 600), 0, 620);
                    foreach (var (x, s, yaw) in new[] { (-268f, 180f, 90f), (268f, 470f, -90f), (-268f, 520f, 90f) })
                        Put("pier_old", root, new Vector3(x, water - 0.6f, s), yaw, 14);
                    break;
                case "watchtowers":
                    // More towers along the marsh, well off the line between the two.
                    foreach (var (id, x, s, h) in new[] { ("tower_steel", -95f, 130f, 14f), ("tower_steel", 120f, 340f, 14f), ("tower_lattice", 80f, -45f, 13f), ("tower_lattice", -85f, 505f, 13f) })
                        Put(id, root, new Vector3(x, Mathf.Max(Y(x, s), water - 0.8f), s), (float)rng.NextDouble() * 360, h, byHeight: true);
                    foreach (var (x, s) in new[] { (-60f, -70f), (64f, 520f) })
                        Put("pier_old", root, new Vector3(x, water - 0.6f, s), 0, 14);
                    break;
                case "oil-rigs":
                    // Waterline at the models' own y = 0.
                    Native("rig_benkos", root, new Vector3(-720, water, lane * 0.55f), 35, 1.1f);
                    Native("rig_deepwater", root, new Vector3(760, water, lane + 380), -20, 2.6f, "ocean", "water", "fish");
                    break;
                case "harbor-rooftops":
                    // Rusted boxes left on the quays, outside the sight corridor.
                    foreach (var (x, s, yaw, up) in new[] { (-70f, 148f, 0f, 0), (-70f, 148f, 2f, 1), (-58f, 146f, 90f, 0), (75f, 293f, 0f, 0), (88f, 296f, -4f, 0), (88f, 296f, 0f, 1) })
                        Put("container_rusted", root, new Vector3(x, Y(x, s) + up * 2.6f, s), yaw, 12.2f);
                    break;
                case "dry-valley":
                    Put("battlefield_pack", root, new Vector3(-150, Y(-150, lane * 0.5f) - 0.3f, lane * 0.5f), 20, 60);
                    Put("trench_set", root, new Vector3(140, Y(140, lane * 0.3f) - 1.2f, lane * 0.3f), -35, 22);
                    foreach (var (x, s, yaw) in new[] { (250f, 620f, 180f), (-260f, 760f, 10f) })
                        Put("cliffs_set", root, new Vector3(x, Y(x, s) - 6, s), yaw, 240, false, Sandstone, "cliff_04", "cliff_lp");
                    Put("desert_cliff", root, new Vector3(-500, Y(-500, lane + 900) - 30, lane + 900), 30, 700);
                    Rocks(root, rng, Y, lane, 34, 1.5f, 7, 30, 260, Sandstone, "rock_kit");
                    Sandbags(root, Y, new[] { (-150f, lane * 0.5f + 26, 20f), (-140f, lane * 0.5f + 31, 30f) });
                    break;
                case "alpine-ridge":
                    for (int i = 0; i < 46; i++)
                    {
                        float side = i % 2 == 0 ? -1 : 1;
                        float x = side * (40 + (float)rng.NextDouble() * 230);
                        float s = (float)rng.NextDouble() * lane;
                        Put("spruce", root, new Vector3(x, Y(x, s) - 0.2f, s), (float)rng.NextDouble() * 360, 14 + (float)rng.NextDouble() * 12, byHeight: true);
                    }
                    Rocks(root, rng, Y, lane, 24, 1.5f, 6, 32, 240);
                    break;
                case "training-lane":
                    Rocks(root, rng, Y, lane, 14, 1, 3.5f, 60, 160);
                    break;
            }
        }

        /// <summary>
        /// A model sized so its largest horizontal side (or its height) is `size` m, its footprint's
        /// middle at `at` and its base at at.y, turned `yaw`. Parts whose names contain a `drop`
        /// word are left out (water planes and the like).
        /// </summary>
        static Transform? Put(string id, Transform root, Vector3 at, float yaw, float size, bool byHeight = false, Color? tint = null, params string[] drop)
        {
            if (Library.Model(id) == null) return null;
            var m = Library.Prop(id);
            Drop(m, drop);
            if (tint is Color c) Biomes.Tint(m, c, Tinted);
            var holder = Library.Fit(m, size, byHeight, root);
            holder.localPosition = at;
            holder.localRotation = Quaternion.Euler(0, yaw, 0);
            return holder;
        }

        /// <summary>A model at its own scale times `scale`, its origin at `at` (for the rigs, the waterline).</summary>
        static void Native(string id, Transform root, Vector3 at, float yaw, float scale, params string[] drop)
        {
            if (Library.Model(id) == null) return;
            var m = Library.Prop(id, root);
            Drop(m, drop);
            m.transform.localPosition = at;
            m.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            m.transform.localScale *= scale;
        }

        /// <summary>Sun-bleached sandstone: the scanned rock and cliff textures are grey and mossy.</summary>
        static readonly Color Sandstone = new Color(1.55f, 1.25f, 0.95f);
        static readonly Dictionary<Material, Material> Tinted = new Dictionary<Material, Material>();

        static void Drop(GameObject m, string[] words)
        {
            if (words.Length == 0) return;
            foreach (var r in m.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name.ToLowerInvariant();
                foreach (var w in words)
                    if (n.Contains(w)) { Object.DestroyImmediate(r); break; }
            }
        }

        /// <summary>Scanned rocks (and the rock cluster) strewn either side of the lane, half sunk.</summary>
        static void Rocks(Transform root, System.Random rng, System.Func<float, float, float> Y, float lane, int count, float min, float max, float near, float far, Color? tint = null, params string[] only)
        {
            string[] ids = only.Length > 0 ? only : new[] { "rocks_scan_1", "rocks_scan_2", "rocks_scan_3", "rock_kit" };
            for (int i = 0; i < count; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                float x = side * (near + (float)rng.NextDouble() * (far - near));
                float s = -40 + (float)rng.NextDouble() * (lane + 80);
                string id = ids[i % ids.Length];
                float size = Mathf.Lerp(min, max, (float)rng.NextDouble()) * (id == "rock_kit" ? 2.5f : 1);
                Put(id, root, new Vector3(x, Y(x, s) - size * 0.18f, s), (float)rng.NextDouble() * 360, size, false, tint);
            }
        }

        /// <summary>A sandbag wall at each (x, s, yaw): two courses of the barrier, the top one offset.</summary>
        static void Sandbags(Transform root, System.Func<float, float, float> Y, (float x, float s, float yaw)[] at)
        {
            foreach (var (x, s, yaw) in at)
            {
                float y = Y(x, s) - 0.05f;
                var turn = Quaternion.Euler(0, yaw, 0);
                for (int k = -1; k <= 1; k++)
                {
                    var p = new Vector3(x, y, s) + turn * new Vector3(k * 4.4f, 0, 0);
                    Put("sandbag_barrier", root, p, yaw, 4.6f);
                    if (k < 1) Put("sandbag_barrier", root, p + turn * new Vector3(2.2f, 0.95f, 0), yaw, 4.6f);
                }
            }
        }
    }
}
