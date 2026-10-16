using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Training Lane as a proper rifle range: gravel firing pads with rubber mats at every spot and
    /// shooting benches under a canopy behind each line, concrete barricades for cover, steel gongs and
    /// silhouettes on A-frames, earth side berms and a bare-earth backstop behind each end, a range
    /// hut flying the red flag, wooded hills beyond.
    /// </summary>
    public static class TrainingRange
    {
        /// <summary>Lane distances of the two firing lines (the middle of each side's spots) and the service road's x.</summary>
        public const float LineA = 70, LineB = 581, RoadX = 84;

        public static void Build(DuelMap map, Transform root, List<Flag> flags)
        {
            var ground = map.Terrain ?? map.Ground;
            float Y(float x, float s) => (float)ground(x, -s);
            var rng = new System.Random(650);
            var lane = Biomes.LaneOf(map);
            float length = (float)map.LaneLength;

            Biomes.Cover(map, root, new Biomes.CoverKit { Rock = Library.Rock(3), Default = CoverLook.Concrete });

            FiringLines(map, root, Y);
            Targets(root, Y);
            Berms(root, Y, length);

            // A range hut behind each line with the red flag up: the range is live.
            var hut = new MeshBuilder();
            var tin = new MeshBuilder();
            foreach (var (x, s) in new[] { (-78f, LineA - 40), (72f, LineB + 36) })
            {
                float y = Y(x, s);
                hut.Box(new Vector3(x, y + 1.2f, s), new Vector3(4.2f, 3.2f, 3.2f));
                tin.Box(new Vector3(x, y + 2.9f, s), new Vector3(5f, 0.12f, 4f), Quaternion.Euler(6, 0, 0));
                var flag = Flag.Create(new Vector3(x + 3.6f, y, s), 6, new Color(0.8f, 0.06f, 0.05f), root);
                flags.Add(flag);
            }
            hut.Object("Range huts", Library.Surface("concrete_wall_008", 4, new Color(0.86f, 0.84f, 0.78f)), root);
            tin.Object("Range hut roofs", Library.Surface("corrugated_iron_02", 2, new Color(0.7f, 0.72f, 0.74f)), root);

            // Woods on the hills either side.
            int planted = 0;
            for (int i = 0; i < 2000 && planted < 44; i++)
            {
                float x = (rng.Next(2) == 0 ? -1 : 1) * (130 + (float)rng.NextDouble() * 520);
                float s = -350 + (float)rng.NextDouble() * (length + 700);
                if (lane.Contains(x, s, 30) || Mathf.PerlinNoise(x * 0.012f, s * 0.012f) < 0.45f) continue;
                string id = rng.Next(3) == 0 ? "island_tree_01" : "fir_tree_01";
                Biomes.PlaceSized(id, new Vector3(x, Y(x, s), s), (float)rng.NextDouble() * 360, 9 + (float)rng.NextDouble() * 9, root, 0.3f);
                planted++;
            }

            Biomes.Horizon(root, length / 2, new Biomes.Hills
            {
                MinHeight = 60,
                MaxHeight = 230,
                Rock = Library.Surface("sparse_grass", 50, new Color(0.72f, 0.82f, 0.62f)),
                Seed = 65,
                Ground = Y,
            });
        }

        /// <summary>A rubber mat under each spot, benches under a canopy behind each line.</summary>
        static void FiringLines(DuelMap map, Transform root, System.Func<float, float, float> Y)
        {
            var mats = new MeshBuilder();
            var wood = new MeshBuilder();
            var steel = new MeshBuilder();
            var roof = new MeshBuilder();
            var models = new List<string>(Figure.RifleModels);
            int shown = 0;
            foreach (var side in new[] { Side.A, Side.B })
            {
                // The shooter lies behind the spot, toward his own rear.
                float back = side == Side.A ? -1 : 1;
                foreach (var p in map.Spots[side])
                {
                    var spot = Library.ToUnity(p);
                    mats.Box(new Vector3(spot.x, spot.y + 0.015f, spot.z + back * 0.7f), new Vector3(1.0f, 0.03f, 2.1f));
                }
                float line = side == Side.A ? LineA : LineB;
                float benchS = line + back * 22;
                for (int i = 0; i < 5; i++)
                {
                    float x = -36 + i * 18;
                    float y = Y(x, benchS);
                    // Table top on a steel frame, a stool behind it.
                    wood.Box(new Vector3(x, y + 0.8f, benchS), new Vector3(1.0f, 0.06f, 1.5f));
                    foreach (var (dx, dz) in new[] { (-0.4f, -0.6f), (0.4f, -0.6f), (-0.4f, 0.6f), (0.4f, 0.6f) })
                        steel.Box(new Vector3(x + dx, y + 0.38f, benchS + dz), new Vector3(0.06f, 0.8f, 0.06f));
                    wood.Box(new Vector3(x, y + 0.48f, benchS + back * 1.2f), new Vector3(0.5f, 0.05f, 0.4f));
                    // A rifle laid on each bench, muzzle down-range: every model the armoury has.
                    var gun = Figure.Rifle(root, models[shown++ % models.Count]);
                    gun.transform.rotation = Quaternion.Euler(0, side == Side.A ? 0 : 180, 0);
                    gun.transform.position = new Vector3(x + 0.1f, y + 0.83f, benchS);
                    Library.Ground(gun, y + 0.83f);
                    steel.Box(new Vector3(x, y + 0.23f, benchS + back * 1.2f), new Vector3(0.06f, 0.46f, 0.06f));
                }
                // Canopy over the benches: steel posts, a shallow tin roof.
                float cy = Y(0, benchS);
                for (float x = -45; x <= 45; x += 15)
                {
                    steel.Box(new Vector3(x, cy + 1.6f, benchS - 3.5f), new Vector3(0.14f, 3.6f, 0.14f));
                    steel.Box(new Vector3(x, cy + 1.6f, benchS + 3.5f), new Vector3(0.14f, 3.6f, 0.14f));
                }
                roof.Box(new Vector3(0, cy + 3.5f, benchS), new Vector3(92, 0.1f, 8.4f), Quaternion.Euler(-back * 4, 0, 0));
            }
            mats.Object("Firing mats", Library.Plain(new Color(0.1f, 0.13f, 0.1f), 0.15f), root, shadows: false);
            wood.Object("Benches", Library.Surface("weathered_planks", 1.5f, new Color(0.8f, 0.7f, 0.58f)), root);
            steel.Object("Range steel", Library.Plain(new Color(0.3f, 0.32f, 0.33f), 0.4f, 0.7f), root);
            roof.Object("Canopies", Library.Surface("corrugated_iron_02", 2, new Color(0.75f, 0.77f, 0.8f)), root);
        }

        /// <summary>
        /// Steel: gongs and torso silhouettes hung from A-frames along both edges of the lane, and a row
        /// in front of each backstop, all well outside the lines between the spots.
        /// </summary>
        static void Targets(Transform root, System.Func<float, float, float> Y)
        {
            var frame = new MeshBuilder();
            var white = new MeshBuilder();
            var orange = new MeshBuilder();
            var stands = new List<(float x, float s, float facing)>();
            for (int k = 1; k <= 5; k++)
            {
                stands.Add((-70, LineA + 100 * k - 30, -1));
                stands.Add((70, LineB - 100 * k + 30, 1));
            }
            foreach (float x in new[] { -88f, -72f, 72f, 88f })
            {
                stands.Add((x, 660, -1));
                stands.Add((x, -18, 1));
            }
            int n = 0;
            foreach (var (x, s, facing) in stands)
            {
                float y = Y(x, s);
                // Two legs each side splayed along the lane, a cross bar at 1.9 m.
                var l = new Vector3(x - 1.2f, y + 1.9f, s);
                var r = new Vector3(x + 1.2f, y + 1.9f, s);
                foreach (var top in new[] { l, r })
                {
                    frame.Beam(top + new Vector3(0, -2.1f, -0.6f), top, 0.07f, 0.07f);
                    frame.Beam(top + new Vector3(0, -2.1f, 0.6f), top, 0.07f, 0.07f);
                }
                frame.Beam(l, r, 0.08f, 0.08f);
                // Alternate a round gong and a torso silhouette, hung on short chains.
                bool gong = n++ % 2 == 0;
                var paint = n % 3 == 0 ? orange : white;
                for (int i = -1; i <= 1; i += 2)
                {
                    float hx = x + i * 0.55f;
                    frame.Beam(new Vector3(hx, y + 1.9f, s), new Vector3(hx, y + 1.55f, s), 0.015f, 0.015f);
                    if (gong) paint.Tube(new Vector3(hx, y + 1.25f, s - 0.01f), new Vector3(hx, y + 1.25f, s + 0.01f), 0.3f, 16);
                    else
                    {
                        paint.Box(new Vector3(hx, y + 1.18f, s), new Vector3(0.45f, 0.72f, 0.012f));
                        paint.Box(new Vector3(hx, y + 1.62f, s), new Vector3(0.16f, 0.16f, 0.012f));
                    }
                }
            }
            frame.Object("Target frames", Library.Plain(new Color(0.2f, 0.21f, 0.2f), 0.3f, 0.6f), root);
            white.Object("Steel targets", Library.Plain(new Color(0.88f, 0.88f, 0.84f), 0.35f, 0.2f), root);
            orange.Object("Steel targets orange", Library.Plain(new Color(0.95f, 0.42f, 0.08f), 0.35f, 0.2f), root);
        }

        /// <summary>
        /// Earth berms: long grassed side berms beyond the target stands, and a bare-earth backstop
        /// across each end behind the zones, built as trapezoid sections that follow the ground.
        /// </summary>
        static void Berms(Transform root, System.Func<float, float, float> Y, float length)
        {
            var grass = new MeshBuilder();
            var earth = new MeshBuilder();
            foreach (float side in new[] { -1f, 1f })
                Berm(grass, Y, true, side * 100, -60, length + 60, 14, 3, 3.5f);
            Berm(earth, Y, false, length + 32, -130, 130, 30, 7, 9);
            Berm(earth, Y, false, -46, -130, 130, 28, 6, 7);
            grass.Object("Side berms", Library.Surface("sparse_grass", 8, new Color(0.9f, 1f, 0.82f)), root);
            earth.Object("Backstops", Library.Surface("brown_mud_leaves_01", 7, new Color(0.95f, 0.85f, 0.75f)), root);
        }

        /// <summary>
        /// One berm: its crest runs along s (alongS) at x = `at`, or along x at s = `at`, from `from` to
        /// `to`; `baseW` wide at the foot, `topW` at the crest, `height` above the ground under the crest.
        /// </summary>
        static void Berm(MeshBuilder mb, System.Func<float, float, float> Y, bool alongS, float at, float from, float to, float baseW, float topW, float height)
        {
            Vector3 P(float along, float across, float y) => alongS ? new Vector3(at + across, y, along) : new Vector3(along, y, at + across);
            float G(float along, float across) => alongS ? Y(at + across, along) : Y(along, at + across);
            const float Step = 8;
            Vector3[]? prev = null;
            for (float u = from; u <= to + 0.01f; u += Step)
            {
                float crest = G(u, 0) + height;
                var sec = new[]
                {
                    P(u, -baseW / 2, G(u, -baseW / 2) - 0.6f),
                    P(u, -topW / 2, crest),
                    P(u, topW / 2, crest),
                    P(u, baseW / 2, G(u, baseW / 2) - 0.6f),
                };
                if (prev == null)
                {
                    EndCap(mb, sec, alongS ? -1 : 1);
                }
                else
                {
                    for (int k = 0; k < 3; k++)
                    {
                        // Quad between this section and the last, outward-facing.
                        Vector3 a = prev[k], b = prev[k + 1], c = sec[k + 1], d = sec[k];
                        var nm = Vector3.Cross(b - a, c - a);
                        if (nm.y < 0) { mb.Triangle(a, c, b); mb.Triangle(a, d, c); }
                        else { mb.Triangle(a, b, c); mb.Triangle(a, c, d); }
                    }
                }
                prev = sec;
            }
            if (prev != null) EndCap(mb, prev, alongS ? 1 : -1);
        }

        /// <summary>Closes a berm's end; `dir` is the direction the cap faces along the berm.</summary>
        static void EndCap(MeshBuilder mb, Vector3[] sec, float dir)
        {
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var nm = Vector3.Cross(b - a, c - a);
                var axis = Vector3.Cross(sec[3] - sec[0], Vector3.up);
                if (Vector3.Dot(nm, axis) * dir < 0) mb.Triangle(a, c, b);
                else mb.Triangle(a, b, c);
            }
            Tri(sec[0], sec[1], sec[2]);
            Tri(sec[0], sec[2], sec[3]);
        }
    }
}
