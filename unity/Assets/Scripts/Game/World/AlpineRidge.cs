using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Alpine Ridge: a meadow shoulder (A) across a forested gully to a snowfield below the summit
    /// (B). Fir forest on the lower flanks (scanned firs near the lane, low-poly firs beyond), scree
    /// and boulders, crags on the far flanks, a stone-and-timber hut under the snow line near B and
    /// a hay barn behind A, and three ranges of rock-and-snow peaks on the horizon, hazier and
    /// bluer with distance.
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
            var snow = Snow(4, new Color(0.95f, 0.96f, 0.99f), 0.7f);
            var rock = Library.Rock(3, new Color(0.84f, 0.86f, 0.9f));

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

            Peaks(root, length / 2, Y);
        }

        /// <summary>
        /// The scanned snow, or plain snow-white if it wasn't downloaded. Far snow passes a cool grey
        /// `tint` and a low `gloss` so it reads matte and recedes instead of glaring.
        /// </summary>
        static Material Snow(float tile, Color? tint = null, float gloss = 1) =>
            Library.Load<Texture2D>("TP/Textures/snow_02/snow_02_diff_2k") != null
                ? Library.Surface("snow_02", tile, tint, gloss)
                : Library.Plain((tint ?? Color.white) * new Color(0.9f, 0.92f, 0.96f), 0.35f * gloss);

        // ---- the ring of peaks -----------------------------------------------------------------------

        /// <summary>One range of the ring: its radial extent, crest heights, snow line and colours.</summary>
        sealed class Range
        {
            public float Inner, Outer, MinH, MaxH, SnowLine;
            public int Around, Out, Seed;
            public Color Rock, Snow;
        }

        /// <summary>
        /// Three ranges of peaks one behind another, each higher, hazier and bluer than the last: the
        /// nearest rises from under the terrain's edge so the edge is always backed by slopes; the
        /// farther two show over its saddles. Each is ridged noise on a polar grid (sharp crests,
        /// jagged summits, smooth valleys) with snow only where the ground is gentle enough to hold it.
        /// </summary>
        static void Peaks(Transform root, float centreS, System.Func<float, float, float> Y)
        {
            var ranges = new[]
            {
                new Range { Inner = 1850, Outer = 3500, MinH = 300, MaxH = 720, SnowLine = 210, Around = 480, Out = 26, Seed = 46,
                    Rock = new Color(0.5f, 0.52f, 0.56f), Snow = new Color(0.84f, 0.87f, 0.92f) },
                new Range { Inner = 2700, Outer = 4300, MinH = 520, MaxH = 1100, SnowLine = 240, Around = 400, Out = 20, Seed = 71,
                    Rock = new Color(0.55f, 0.59f, 0.67f), Snow = new Color(0.8f, 0.84f, 0.91f) },
                new Range { Inner = 3600, Outer = 5200, MinH = 720, MaxH = 1500, SnowLine = 270, Around = 320, Out = 16, Seed = 93,
                    Rock = new Color(0.6f, 0.65f, 0.75f), Snow = new Color(0.78f, 0.83f, 0.91f) },
            };
            foreach (var r in ranges) Ridge(root, centreS, Y, r);
        }

        /// <summary>
        /// Ridged multifractal noise in 0–1 at world (x, z): each octave's creases are sharpened and
        /// weighted by the octave below, so crests stay jagged while valleys run smooth.
        /// </summary>
        static float Ridged(float x, float z, float o)
        {
            float sum = 0, norm = 0, amp = 1, freq = 1 / 1400f, weight = 1;
            for (int i = 0; i < 5; i++)
            {
                float n = 1 - Mathf.Abs(2 * Mathf.PerlinNoise(x * freq + o, z * freq + o * 1.7f) - 1);
                n *= n * weight;
                weight = Mathf.Clamp01(n * 1.8f);
                sum += n * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.07f;
            }
            return sum / norm;
        }

        /// <summary>One range: a polar grid of ridged heights, split into a rock mesh and a snow mesh that share smooth normals.</summary>
        static void Ridge(Transform root, float centreS, System.Func<float, float, float> Y, Range range)
        {
            const float BaseY = -25;
            int around = range.Around, outN = range.Out;
            var rng = new System.Random(range.Seed);
            float o1 = (float)rng.NextDouble() * 100, o2 = (float)rng.NextDouble() * 100, o3 = (float)rng.NextDouble() * 100;
            var v = new Vector3[around * outN];
            for (int a = 0; a < around; a++)
            {
                float th = a * 2 * Mathf.PI / around;
                float cx = Mathf.Cos(th), sz = Mathf.Sin(th);
                // Every range stands on the ground at the terrain's edge in its direction.
                float foot = BaseY + Mathf.Max(0, Y(cx * 1850, centreS + sz * 1850));
                // Massifs and low passes around the ring, so the skyline is not one even height.
                float massif = Mathf.Clamp01(Mathf.PerlinNoise(cx * 1.3f + o1, sz * 1.3f + o2) * 1.4f - 0.2f);
                float h = Mathf.Lerp(range.MinH, range.MaxH, Mathf.Pow(massif, 1.2f));
                float crest = 0.28f + 0.22f * Mathf.PerlinNoise(cx * 2.1f + o2, sz * 2.1f + o1 + 5);
                for (int k = 0; k < outN; k++)
                {
                    float t = (float)k / (outN - 1);
                    float r = Mathf.Lerp(range.Inner, range.Outer, t);
                    float x = cx * r, z = centreS + sz * r;
                    float profile = t < crest ? Mathf.Sin(t / crest * Mathf.PI / 2) : Mathf.Cos((t - crest) / (1 - crest) * Mathf.PI / 2);
                    profile = Mathf.Pow(Mathf.Clamp01(profile), 1.1f);
                    // A light domain warp bends the ridges so they don't line up with the noise grid.
                    float wx = x + 260 * (Mathf.PerlinNoise(x * 0.0011f + o3, z * 0.0011f) - 0.5f);
                    float wz = z + 260 * (Mathf.PerlinNoise(x * 0.0011f, z * 0.0011f + o3) - 0.5f);
                    float shape = 0.3f + 1.25f * Ridged(wx, wz, o1);
                    v[a * outN + k] = new Vector3(x, foot + (k == 0 ? 0 : h * profile * shape), z);
                }
            }

            var rockT = new List<int>();
            var snowT = new List<int>();
            for (int a = 0; a < around; a++)
            {
                int a2 = (a + 1) % around;
                for (int k = 0; k < outN - 1; k++)
                {
                    int i00 = a * outN + k, i01 = a * outN + k + 1, i10 = a2 * outN + k, i11 = a2 * outN + k + 1;
                    foreach (var (p, q, w) in new[] { (i00, i01, i11), (i00, i11, i10) })
                    {
                        // Faces point up and in (toward the lane), so wind them that way.
                        var nm = Vector3.Cross(v[q] - v[p], v[w] - v[p]);
                        bool flip = nm.y < 0;
                        float up = Mathf.Abs(nm.normalized.y);
                        var mid = (v[p] + v[q] + v[w]) / 3;
                        // Snow lies on gentle ground above a ragged snow line; the higher, the steeper
                        // it clings, but cliffs and the sharpest crests stay bare rock.
                        float line = range.SnowLine + 140 * (Mathf.PerlinNoise(mid.x * 0.003f + o2, mid.z * 0.003f) - 0.5f);
                        float alt = Mathf.Clamp01((mid.y - line) / 380);
                        float need = Mathf.Lerp(0.9f, 0.66f, alt) + 0.12f * (Mathf.PerlinNoise(mid.x * 0.012f, mid.z * 0.012f + o3) - 0.5f);
                        var list = mid.y > line && up > need ? snowT : rockT;
                        list.Add(p);
                        list.Add(flip ? w : q);
                        list.Add(flip ? q : w);
                    }
                }
            }
            var all = new List<int>(rockT);
            all.AddRange(snowT);
            var normals = Biomes.Normals(v, all);
            var rockMb = new MeshBuilder();
            rockMb.Smooth(v, normals, rockT);
            rockMb.Object("Peaks rock " + range.Seed, Library.Rock(140, range.Rock), root, shadows: false);
            if (snowT.Count > 0)
            {
                var snowMb = new MeshBuilder();
                snowMb.Smooth(v, normals, snowT);
                snowMb.Object("Peaks snow " + range.Seed, Snow(90, range.Snow, 0.3f), root, shadows: false);
            }
        }

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
