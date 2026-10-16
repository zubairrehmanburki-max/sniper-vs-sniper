using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Set dressing from the scanned model library: cliffs and rocks along the banks, trees on the
    /// hills, ships and buoys on the water, gear next to each firing spot. Placement is seeded so a
    /// map always looks the same.
    /// </summary>
    public static class Dressing
    {
        public static void Build(DuelMap map, Transform parent)
        {
            var root = new GameObject("Dressing").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(map.Id.GetHashCode() & 0x7fffffff);
            var ground = map.Terrain ?? map.Ground;
            float Y(float x, float s) => (float)ground(x, -s);
            float lane = (float)map.LaneLength;
            float water = (float)(map.Water ?? -1000);
            bool open = map.Water.HasValue;

            // Rock along the waterline wherever the bank rises out of the water.
            string[] rocks = { "coastal_cliff_01", "coastal_cliff_02", "coastal_cliff_04", "coast_rocks_01", "coast_rocks_03", "rock_face_01" };
            // Twin Bridges has its own rip-rap and river front (Estuary) instead of the scanned cliffs.
            // The marsh's shores are soft mud and reed (Marsh), not sea cliffs.
            if (open && map.Id != "twin-bridges" && map.Id != "watchtowers")
            {
                for (float s = -400; s < lane + 700; s += 85)
                {
                    foreach (float side in new[] { -1f, 1f })
                    {
                        // Walk out from the lane to where the ground meets the water.
                        float x = 0;
                        for (float probe = 120; probe < 900; probe += 4)
                        {
                            if (Y(side * probe, s) > water + 0.5f) { x = side * probe; break; }
                        }
                        if (x == 0) continue;
                        // Keep the lane itself clear: nothing between the two spots.
                        if (Mathf.Abs(x) < 150 && s > -20 && s < lane + 20) continue;
                        string id = rocks[rng.Next(rocks.Length)];
                        float scale = id.StartsWith("coastal_cliff") ? 2.2f + (float)rng.NextDouble() * 1.6f : 3f + (float)rng.NextDouble() * 3f;
                        var go = Library.Place(id, new Vector3(x + side * (float)rng.NextDouble() * 8, 0, s + (float)rng.Next(-15, 15)), (float)rng.NextDouble() * 360, scale, root);
                        Library.Ground(go, water - 1.5f);
                    }
                }
            }

            // Trees on the land beyond the banks, thinning with distance. Maps with their own biome plant their own.
            string[] trees = { "island_tree_01", "island_tree_01", "shrub_02" };
            int planted = 0, toPlant = Biomes.Handles(map.Id) || map.Id is "twin-bridges" or "watchtowers" or "oil-rigs" ? 0 : 70;
            for (int i = 0; i < 2000 && planted < toPlant; i++)
            {
                float x = ((float)rng.NextDouble() * 2 - 1) * 1400;
                float s = -700 + (float)rng.NextDouble() * (lane + 1400);
                if (Mathf.Abs(x) < 240) continue;
                float y = Y(x, s);
                if (y < water + 3) continue;
                // Not on the road embankments either side of each bridge.
                if (map.Look.Structures != null && System.Array.Exists(map.Look.Structures, st => Mathf.Abs(s - (float)st.S) < 25)) continue;
                string id = trees[rng.Next(trees.Length)];
                var go = Library.Place(id, new Vector3(x, y - 0.3f, s), (float)rng.NextDouble() * 360, 0.85f + (float)rng.NextDouble() * 0.6f, root);
                planted++;
            }

            // The marsh and the open sea get their own dressing (Marsh, Offshore) instead of the river's.
            if (open && map.Id != "watchtowers" && map.Id != "oil-rigs") Water(map, root, rng, water);
            if (map.Look.Structures != null)
                foreach (var st in map.Look.Structures)
                    if (st.Kind == StructureKind.Bridge) Roadblock(st, root, rng);
        }

        /// <summary>
        /// A bridge closed off by the military: lane-closure barriers, supply dumps, fuel cans and
        /// searchlights along the deck. Mid-span stays clear so nothing stands in the firing line.
        /// </summary>
        static void Roadblock(Structure b, Transform root, System.Random rng)
        {
            float deck = (float)b.DeckY, hw = (float)b.HalfWidth, s = (float)b.S;
            float span = (float)b.HalfSpan;
            GameObject Put(string id, float x, float z, float yaw, float scale = 1)
            {
                var go = Library.Place(id, new Vector3(x, deck, s + z), yaw, scale, root);
                Library.Ground(go, deck + 0.04f);
                return go;
            }
            foreach (float dir in new[] { -1f, 1f })
            {
                // Chicane of barriers across each carriageway, up to the median.
                float bx = dir * (26 + (float)rng.NextDouble() * 10);
                for (int i = 0; i < 3; i++) Put("concrete_road_barrier", bx + i * 0.4f * dir, -hw + 2.6f + i * 1.7f, 90 + (float)rng.Next(-6, 6));
                float bx2 = bx + dir * 9;
                for (int i = 0; i < 3; i++) Put("concrete_road_barrier", bx2 - i * 0.4f * dir, hw - 2.6f - i * 1.7f, 90 + (float)rng.Next(-6, 6));
                // A supply dump and a searchlight further along.
                float dx = dir * (55 + (float)rng.NextDouble() * 30);
                float dz = (rng.Next(2) == 0 ? -1 : 1) * (hw - 3.4f);
                Put("wooden_military_crate", dx, dz, (float)rng.Next(0, 30));
                var under = Put("wooden_military_crate", dx + 1.3f, dz, (float)rng.Next(0, 30));
                var top = Library.Place("old_military_crate", new Vector3(dx + 1.3f, deck, s + dz), (float)rng.Next(0, 90), 1, root);
                Library.Ground(top, Library.BoundsOf(under).max.y);
                for (int i = 0; i < 3; i++) Put("barrel_03", dx + 3 + i * 0.7f, dz + (float)rng.NextDouble() * 0.6f, (float)rng.Next(0, 360));
                for (int i = 0; i < 2; i++) Put("metal_jerrycan_green", dx - 1.6f - i * 0.45f, dz + 0.3f, (float)rng.Next(0, 360));
                Put("portable_searchlight", dir * (18 + (float)rng.NextDouble() * 4), -dz * 0.8f, dir > 0 ? -90 : 90);
            }
        }

        static void Water(DuelMap map, Transform root, System.Random rng, float water)
        {
            float lane = (float)map.LaneLength;
            // A sailing ship anchored upstream beyond the enemy, another downstream behind the player.
            var ship = Library.Place("dutch_ship_large_01", new Vector3(-150, water, lane + 420), 62, 1, root);
            Library.Ground(ship, water - 2.2f);
            var ship2 = Library.Place("dutch_ship_medium", new Vector3(170, water, -360), -20, 1, root);
            Library.Ground(ship2, water - 1.4f);
            // Channel markers either side of the lane.
            for (int i = 0; i < 6; i++)
            {
                float s = lane * (i + 0.5f) / 6;
                float x = (i % 2 == 0 ? -1 : 1) * (110 + (float)rng.NextDouble() * 30);
                var buoy = Library.Place(i % 3 == 0 ? "ocean_buoy" : "lateral_sea_marker", new Vector3(x, water, s), (float)rng.NextDouble() * 360, 1.2f, root);
                Library.Ground(buoy, water - 0.6f);
                buoy.AddComponent<Bob>().Phase = i * 1.3f;
            }
        }

        /// <summary>
        /// Gear at a firing spot: ammo, binoculars, a crate and a can beside the shooter, and road
        /// barriers either side where there is room (not on a tower's small roof).
        /// </summary>
        public static List<Bounds> Kit(Vector3 spot, float facingDeg, Transform parent, bool barriers = true)
        {
            var root = new GameObject("Kit").transform;
            root.SetParent(parent, false);
            root.position = spot;
            root.rotation = Quaternion.Euler(0, facingDeg, 0);
            // What was put down, so the player walks round it rather than through it.
            var placed = new List<Bounds>();
            void Put(string id, Vector3 local, float yaw, float scale = 1)
            {
                var go = Library.Place(id, local, yaw, scale, root);
                Library.Ground(go, spot.y);
                placed.Add(Library.BoundsOf(go));
            }
            Put("ammo_box", new Vector3(0.9f, 0, -0.6f), 20);
            Put("binoculars", new Vector3(-0.7f, 0, -0.3f), -40);
            Put("old_military_crate", new Vector3(2.2f, 0, -2.1f), 8);
            Put("metal_jerrycan_green", new Vector3(-2.4f, 0, -2.5f), 70);
            if (!barriers) return placed;
            Put("concrete_road_barrier", new Vector3(6, 0, -1), 90);
            Put("concrete_road_barrier", new Vector3(-6, 0, -1), 90);
            return placed;
        }
    }

    /// <summary>Slow heave and roll for floating things.</summary>
    public sealed class Bob : MonoBehaviour
    {
        public float Phase;
        Vector3 basePos;
        Quaternion baseRot;

        void Start()
        {
            basePos = transform.position;
            baseRot = transform.rotation;
        }

        void Update()
        {
            float t = Time.time + Phase;
            transform.position = basePos + new Vector3(0, Mathf.Sin(t * 0.9f) * 0.18f, 0);
            transform.rotation = baseRot * Quaternion.Euler(Mathf.Sin(t * 0.7f) * 3, 0, Mathf.Sin(t * 0.55f + 1) * 4);
        }
    }
}
