using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Alpine Ridge: a meadow shoulder (A) across a forested gully to a snowfield below the summit
    /// (B). Fir forest on the lower flanks (scanned firs near the lane, low-poly firs beyond), scree
    /// and boulders, crags on the far flanks, a stone-and-timber hut under the snow line near B and
    /// a hay barn behind A, and a ring of snow peaks on the horizon.
    /// </summary>
    public static class AlpineRidge
    {
        /// <summary>Mean snow line (m); Land's ground mix uses it too, so the snow paint and the snow on rocks agree.</summary>
        public const float SnowLine = 62;

        public static void Build(DuelMap map, Transform root)
        {
            var ground = map.Terrain ?? map.Ground;
            float Y(float x, float s) => (float)ground(x, -s);
            var rng = new System.Random(2400);
            var lane = Biomes.LaneOf(map);
            float length = (float)map.LaneLength;
            var snow = Snow(4);
            var rock = Library.Surface("rocky_terrain_02", 3, new Color(0.84f, 0.86f, 0.9f));

            Biomes.Cover(map, root, new Biomes.CoverKit { Rock = rock, Snow = snow, SnowAbove = SnowLine });

            // Huts: one under the snow line on B's flank, a hay barn on the meadow behind A.
            var huts = new HutKit();
            var hutA = new Vector3(-132, 0, 965);
            var hutB = new Vector3(118, 0, -36);
            Hut(huts, Y, hutA.x, hutA.z, 90, 9, 7, true, true);
            Hut(huts, Y, hutB.x, hutB.z, -80, 11, 7.5f, false, false);
            huts.Build(root, snow);
            // A woodpile against the hut's gable end.
            var bark = new MeshBuilder();
            var pileC = new Vector3(hutA.x - 2, 0, hutA.z + 6.2f);
            float pileY = Y(pileC.x, pileC.z);
            Biomes.LogPile(bark, new Bounds(new Vector3(pileC.x, pileY + 0.55f, pileC.z), new Vector3(3.4f, 1.1f, 1.2f)), rng);
            bark.Object("Woodpile", Biomes.Bark, root);

            Forest(map, root, rng, lane, length, Y, new[] { hutA, hutB });
            Rocks(root, rng, lane, length, Y, rock, snow);

            Biomes.Horizon(root, length / 2, new Biomes.Hills
            {
                MinHeight = 420,
                MaxHeight = 1300,
                Rock = Library.Surface("rocky_terrain_02", 80, new Color(0.72f, 0.75f, 0.82f)),
                Snow = Snow(60),
                SnowAbove = 260,
                Seed = 46,
                Ground = Y,
            });
        }

        /// <summary>The scanned snow, or plain white if it wasn't downloaded.</summary>
        static Material Snow(float tile) =>
            Library.Load<Texture2D>("TP/Textures/snow_02/snow_02_diff_2k") != null
                ? Library.Surface("snow_02", tile)
                : Library.Plain(new Color(0.9f, 0.92f, 0.96f), 0.35f);

        static float Slope(System.Func<float, float, float> Y, float x, float s)
        {
            float dx = Y(x + 2, s) - Y(x - 2, s), ds = Y(x, s + 2) - Y(x, s - 2);
            return Mathf.Atan(new Vector2(dx, ds).magnitude / 4) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Firs in clumps below the tree line on slopes a tree can hold. The nearest ones to either
        /// firing position are the scanned model; the rest are low-poly cones merged into one mesh.
        /// </summary>
        static void Forest(DuelMap map, Transform root, System.Random rng, Biomes.Lane lane, float length, System.Func<float, float, float> Y, Vector3[] huts)
        {
            var needles = new MeshBuilder();
            var trunks = new MeshBuilder();
            var a = new Vector2(0, (float)map.Zones.A.Front);
            var b = new Vector2(0, (float)map.Zones.B.Front);
            int models = 0, cones = 0;
            for (int i = 0; i < 30000 && cones < 800; i++)
            {
                float x = (rng.Next(2) == 0 ? -1 : 1) * (70 + (float)rng.NextDouble() * 800);
                float s = -700 + (float)rng.NextDouble() * (length + 1400);
                if (lane.Contains(x, s, 14)) continue;
                // Woods grow in clumps with meadow between.
                if (Mathf.PerlinNoise(x * 0.009f + 3.1f, s * 0.009f + 7.7f) < 0.58f) continue;
                float y = Y(x, s);
                if (y > SnowLine + 12 + (float)rng.NextDouble() * 10 || Slope(Y, x, s) > 32) continue;
                bool clear = true;
                foreach (var h in huts) clear &= new Vector2(x - h.x, s - h.z).magnitude > 16;
                if (!clear) continue;
                float height = 12 + (float)rng.NextDouble() * 14;
                var p = new Vector2(x, s);
                float near = Mathf.Min((p - a).magnitude, (p - b).magnitude);
                if (near < 380 && models < 48)
                {
                    Biomes.PlaceSized("fir_tree_01", new Vector3(x, y, s), (float)rng.NextDouble() * 360, height, root, 0.3f);
                    models++;
                }
                else if (near > 300)
                {
                    Biomes.Fir(needles, trunks, new Vector3(x, y, s), height);
                    cones++;
                }
            }
            needles.Object("Far firs", Library.Plain(new Color(0.06f, 0.11f, 0.07f), 0.12f), root, shadows: false);
            trunks.Object("Far fir trunks", Library.Plain(new Color(0.16f, 0.12f, 0.09f), 0.1f), root, shadows: false);
        }

        /// <summary>Scanned rock faces on the steeper ground, crags far out on the flanks, and a scatter of scree boulders (snow-capped up high).</summary>
        static void Rocks(Transform root, System.Random rng, Biomes.Lane lane, float length, System.Func<float, float, float> Y, Material rock, Material snow)
        {
            var cool = new Dictionary<Material, Material>();
            string[] ids = { "rock_face_01", "coast_rocks_01", "coast_rocks_03" };
            int placed = 0;
            for (int i = 0; i < 600 && placed < 24; i++)
            {
                float x = (rng.Next(2) == 0 ? -1 : 1) * (85 + (float)rng.NextDouble() * 600);
                float s = -500 + (float)rng.NextDouble() * (length + 1000);
                if (lane.Contains(x, s, 20) || Slope(Y, x, s) < 14) continue;
                float h = 4 + (float)rng.NextDouble() * 10;
                var go = Biomes.PlaceSized(ids[rng.Next(ids.Length)], new Vector3(x, Y(x, s), s), (float)rng.NextDouble() * 360, h, root, h * 0.25f, h * 3);
                Biomes.Tint(go, new Color(0.85f, 0.87f, 0.92f), cool);
                placed++;
            }
            for (int i = 0; i < 6; i++)
            {
                // Crags: the scanned mountainside piece, scaled up, out on the far flanks.
                float x = (i % 2 == 0 ? -1 : 1) * (650 + (float)rng.NextDouble() * 650);
                float s = -600 + i * (length + 1200) / 6 + (float)rng.Next(-80, 80);
                float h = 60 + (float)rng.NextDouble() * 70;
                var go = Biomes.PlaceSized("mountainside", new Vector3(x, Y(x, s), s), (float)rng.NextDouble() * 360, h, root, h * 0.3f, h * 3);
                Biomes.Tint(go, new Color(0.85f, 0.87f, 0.92f), cool);
            }

            var boulders = new MeshBuilder();
            var caps = new MeshBuilder();
            for (int i = 0; i < 170; i++)
            {
                float x = ((float)rng.NextDouble() * 2 - 1) * 600;
                float s = -300 + (float)rng.NextDouble() * (length + 600);
                if (lane.Contains(x, s, 6)) continue;
                float size = 0.4f + (float)rng.NextDouble() * (float)rng.NextDouble() * 3f;
                float y = Y(x, s);
                var dims = new Vector3(size * (1 + (float)rng.NextDouble() * 0.5f), size * (0.6f + (float)rng.NextDouble() * 0.4f), size * (1 + (float)rng.NextDouble() * 0.4f));
                float yaw = (float)rng.NextDouble() * 360;
                Biomes.Boulder(boulders, new Vector3(x, y - dims.y * 0.3f, s), dims, rng.Next(10000), 0.62f, 0.2f, yaw);
                if (y > SnowLine + 8)
                    Biomes.Boulder(caps, new Vector3(x, y + dims.y * 0.55f, s), new Vector3(dims.x * 0.75f, dims.y * 0.22f, dims.z * 0.75f), rng.Next(10000), 0.7f, 0.1f, yaw);
            }
            boulders.Object("Scree boulders", rock, root);
            if (caps.VertexCount > 0) caps.Object("Boulder snow", snow, root, shadows: false);
        }

        /// <summary>Builders for the huts, one per material.</summary>
        sealed class HutKit
        {
            public readonly MeshBuilder Stone = new MeshBuilder(), Timber = new MeshBuilder(), Roof = new MeshBuilder(), Snow = new MeshBuilder(), Dark = new MeshBuilder();

            public void Build(Transform root, Material snow)
            {
                Stone.Object("Hut stone", Library.Surface("aerial_rocks_02", 2.5f, new Color(0.78f, 0.78f, 0.76f)), root);
                Timber.Object("Hut timber", Library.Surface("weathered_planks", 2, new Color(0.58f, 0.42f, 0.3f)), root);
                Roof.Object("Hut roofs", Library.Surface("corrugated_iron_02", 2, new Color(0.52f, 0.44f, 0.4f)), root);
                if (Snow.VertexCount > 0) Snow.Object("Hut snow", snow, root);
                Dark.Object("Hut windows", Library.Plain(new Color(0.05f, 0.05f, 0.055f), 0.7f), root, shadows: false);
            }
        }

        /// <summary>
        /// An alpine hut w × d (ridge along w), turned `yaw`: a stone plinth that takes up the slope,
        /// larch walls, a pitched tin roof with deep eaves (snow on it if `snowy`), shuttered windows,
        /// a door, a chimney. A barn is timber down to the plinth and has no chimney.
        /// </summary>
        static void Hut(HutKit k, System.Func<float, float, float> Y, float cx, float cs, float yaw, float w, float d, bool snowy, bool house)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            {
                var c = new Vector3(cx, 0, cs) + rot * new Vector3(dx * w / 2, 0, dz * d / 2);
                float y = Y(c.x, c.z);
                lo = Mathf.Min(lo, y);
                hi = Mathf.Max(hi, y);
            }
            float floor = hi + 0.5f;
            Vector3 P(float lx, float ly, float lz) => new Vector3(cx, floor, cs) + rot * new Vector3(lx, ly, lz);
            float plinthBottom = lo - 0.6f - floor;
            k.Stone.Box(P(0, plinthBottom / 2, 0), new Vector3(w + 0.3f, -plinthBottom, d + 0.3f), rot);

            const float Wall = 2.8f;
            float rise = d * 0.42f;
            (house ? k.Stone : k.Timber).Box(P(0, 0.6f, 0), new Vector3(w, 1.2f, d), rot);
            k.Timber.Box(P(0, 1.2f + (Wall - 1.2f) / 2, 0), new Vector3(w - 0.06f, Wall - 1.2f, d - 0.06f), rot);
            // Gable ends, boarded.
            foreach (float e in new[] { -1f, 1f })
            {
                var a = P(e * (w / 2 - 0.03f), Wall, -d / 2);
                var b = P(e * (w / 2 - 0.03f), Wall + rise, 0);
                var c = P(e * (w / 2 - 0.03f), Wall, d / 2);
                if (e > 0) k.Timber.Triangle(a, b, c);
                else k.Timber.Triangle(a, c, b);
            }
            // Roof: two slabs from the ridge out past the eaves; snow lies on top of them.
            float angle = Mathf.Atan2(rise, d / 2) * Mathf.Rad2Deg;
            float slant = Mathf.Sqrt(d * d / 4 + rise * rise);
            const float Eave = 0.9f, Thick = 0.2f;
            foreach (float e in new[] { -1f, 1f })
            {
                var tilt = rot * Quaternion.Euler(e * angle, 0, 0);
                var normal = tilt * Vector3.up;
                var down = tilt * new Vector3(0, 0, e);
                var ridge = P(0, Wall + rise, 0);
                var centre = ridge + down * ((slant + Eave) / 2) + normal * (Thick / 2 + 0.05f);
                k.Roof.Box(centre, new Vector3(w + 1.4f, Thick, slant + Eave), tilt);
                if (snowy) k.Snow.Box(centre + normal * 0.2f - down * 0.1f, new Vector3(w + 1.2f, 0.22f, slant + Eave - 0.3f), tilt);
            }
            // Windows with shutters on the long sides, a door on one gable end.
            foreach (float side in new[] { -1f, 1f })
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    float wx = i * w * 0.24f;
                    k.Dark.Box(P(wx, 1.75f, side * (d / 2 + 0.02f)), new Vector3(0.9f, 1.0f, 0.04f), rot);
                    k.Timber.Box(P(wx - 0.75f, 1.75f, side * (d / 2 + 0.04f)), new Vector3(0.48f, 1.1f, 0.05f), rot);
                    k.Timber.Box(P(wx + 0.75f, 1.75f, side * (d / 2 + 0.04f)), new Vector3(0.48f, 1.1f, 0.05f), rot);
                }
            }
            k.Dark.Box(P(w / 2 + 0.03f, house ? 1.05f : 1.4f, 0), new Vector3(0.05f, house ? 2.1f : 2.8f, house ? 1.0f : 2.6f), rot);
            if (house)
            {
                k.Stone.Box(P(-w * 0.28f, Wall + rise * 0.7f, d * 0.18f), new Vector3(0.7f, rise * 1.2f + 1.2f, 0.7f), rot);
                // A bench and a balcony rail under the eaves.
                k.Timber.Box(P(w * 0.1f, 0.45f, -d / 2 - 0.35f), new Vector3(2.2f, 0.08f, 0.4f), rot);
            }
        }
    }
}
