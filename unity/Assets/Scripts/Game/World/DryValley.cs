using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Dry Valley: a gravel desert floor between low ridges. Sandstone outcrops and boulder fields on
    /// the ridges, dry scrub, a dirt track up the west side of the valley with a line of telegraph
    /// poles, two ruined adobe huts and a gutted concrete post, mesas on the horizon. The lane between
    /// the two benches stays open apart from the rules' own boulders.
    /// </summary>
    public static class DryValley
    {
        /// <summary>The track's centre line (world x) at lane distance s; Land paints it, the poles follow it.</summary>
        public static float TrackX(float s) => -94 + 18 * Mathf.Sin(s * 0.006f + 0.8f) + 7 * Mathf.Sin(s * 0.017f);

        static readonly Color Warm = new Color(1.0f, 0.84f, 0.68f);

        public static void Build(DuelMap map, Transform root)
        {
            var ground = map.Terrain ?? map.Ground;
            float Y(float x, float s) => (float)ground(x, -s);
            var rng = new System.Random(870);
            var lane = Biomes.LaneOf(map);
            float length = (float)map.LaneLength;

            var rock = Library.Rock(3, new Color(1f, 0.86f, 0.72f));
            Biomes.Cover(map, root, new Biomes.CoverKit
            {
                Rock = rock,
                Scans = new[] { "namaqualand_boulder_03", "namaqualand_boulder_05", "namaqualand_boulder_02", "namaqualand_boulder_04" },
                ScanTint = Bleached,
            });

            Outcrops(map, root, rng, lane, Y);
            Scrub(root, rng, lane, length, Y);
            Veld(map, root, rng, lane, length, Y);
            Poles(root, rng, Y);

            // Ruins: an adobe farmstead by the track, a smaller one across the valley, and a gutted
            // concrete checkpoint on the east side facing the track.
            var adobe = new MeshBuilder();
            var concrete = new MeshBuilder();
            var wood = new MeshBuilder();
            var tin = new MeshBuilder();
            var rebar = new MeshBuilder();
            Ruin(adobe, wood, tin, rebar, Y, TrackX(300) - 17, 300, 12, 7.5f, 5.5f, 2.9f, rng, false);
            Ruin(adobe, wood, tin, rebar, Y, 128, 905, -30, 5.5f, 4.5f, 2.6f, rng, false);
            Ruin(concrete, wood, tin, rebar, Y, 116, 560, 95, 6.5f, 5f, 3.1f, rng, true);
            adobe.Object("Adobe ruins", Library.Surface("concrete_wall_008", 3, new Color(0.86f, 0.66f, 0.48f)), root);
            concrete.Object("Concrete ruin", Library.Surface("concrete_wall_008", 4, new Color(0.78f, 0.75f, 0.7f)), root);
            wood.Object("Ruin timbers", Library.Surface("weathered_planks", 1.6f, new Color(0.62f, 0.52f, 0.42f)), root);
            tin.Object("Ruin tin", Library.Surface("corrugated_iron_02", 2, new Color(0.9f, 0.72f, 0.58f)), root);
            rebar.Object("Rebar", Library.Surface("rusty_metal_02", 1), root, shadows: false);

            Biomes.Horizon(root, length / 2, new Biomes.Hills
            {
                // Low enough that the 12x scope shows sky over the far mesas behind B.
                MinHeight = 50,
                MaxHeight = 150,
                Mesa = true,
                // Darker and warmer than the floor, so the mesas stand out of the haze instead of fading white.
                Rock = Library.Rock(60, new Color(0.86f, 0.64f, 0.48f)),
                Seed = 34,
                Ground = Y,
            });
        }

        /// <summary>
        /// Rock on the ridges: big scanned faces and coast rocks (sun-bleached to sandstone), each with
        /// a skirt of procedural boulders, plus loose stones across the flats; only pebbles in the lane.
        /// </summary>
        static void Outcrops(DuelMap map, Transform root, System.Random rng, Biomes.Lane lane, System.Func<float, float, float> Y)
        {
            var tinted = new Dictionary<Material, Material>();
            // The coast rock scans run to 700k triangles apiece: the Namaqualand cliff and piles instead.
            string[] ids = Wilds.Present("rock_face_01", "namaqualand_cliff_01", "namaqualand_boulders_01");
            var boulders = new MeshBuilder();
            var pebbles = new MeshBuilder();
            int placed = 0;
            for (int i = 0; i < 400 && placed < 30; i++)
            {
                // Mostly on the ridges, a few nearer the lane edge where the ground starts to rise.
                bool near = i % 4 == 0;
                float ax = near ? 78 + (float)rng.NextDouble() * 50 : 120 + (float)rng.NextDouble() * 650;
                float x = (rng.Next(2) == 0 ? -1 : 1) * ax;
                float s = -600 + (float)rng.NextDouble() * 2100;
                if (ids.Length == 0 || lane.Contains(x, s, 18) || Mathf.Abs(x - TrackX(s)) < 14) continue;
                float h = near ? 3 + (float)rng.NextDouble() * 3 : 5 + (float)rng.NextDouble() * 13;
                var go = Biomes.PlaceSized(ids[rng.Next(ids.Length)], new Vector3(x, Y(x, s), s), (float)rng.NextDouble() * 360, h, root, h * 0.2f, h * 3);
                Biomes.Tint(go, Warm, tinted);
                placed++;
                // A skirt of fallen blocks downhill of the outcrop.
                int n = 4 + rng.Next(6);
                for (int k = 0; k < n; k++)
                {
                    float bx = x + ((float)rng.NextDouble() - 0.5f) * h * 2.4f, bs = s + ((float)rng.NextDouble() - 0.5f) * h * 2.4f;
                    if (lane.Contains(bx, bs, 4)) continue;
                    float size = 0.5f + (float)rng.NextDouble() * h * 0.22f;
                    Boulder(boulders, rng, bx, bs, size, Y);
                }
            }
            // Loose boulders over the valley floor outside the lane.
            for (int i = 0; i < 160; i++)
            {
                float x = ((float)rng.NextDouble() * 2 - 1) * 520;
                float s = -300 + (float)rng.NextDouble() * 1500;
                if (lane.Contains(x, s, 6) || Mathf.Abs(x - TrackX(s)) < 6) continue;
                Boulder(boulders, rng, x, s, 0.3f + (float)rng.NextDouble() * (float)rng.NextDouble() * 2.2f, Y);
            }
            // Pebbles everywhere, the lane included: too low to hide a prone man, enough to break up the gravel.
            for (int i = 0; i < 260; i++)
            {
                float x = ((float)rng.NextDouble() * 2 - 1) * 140;
                float s = -40 + (float)rng.NextDouble() * (lane.SMax + 80);
                if (Biomes.NearestSpot(map, x, s) < 3) continue;
                float size = 0.12f + (float)rng.NextDouble() * 0.2f;
                Biomes.Boulder(pebbles, new Vector3(x, Y(x, s) - size * 0.35f, s), new Vector3(size * 1.3f, size, size * 1.1f), rng.Next(1000), 0.8f, 0.25f, (float)rng.NextDouble() * 360);
            }
            var stone = Library.Rock(3, new Color(1f, 0.86f, 0.72f));
            boulders.Object("Boulders", stone, root);
            pebbles.Object("Pebbles", stone, root, shadows: false);
        }

        /// <summary>The Namaqualand granite scans, warmed a touch toward the valley's sandstone.</summary>
        static readonly Color Bleached = new Color(1.06f, 0.95f, 0.84f);

        /// <summary>
        /// The desert's living ground from the Namaqualand scans: rooibos and thorn bushes in clumps
        /// along the wash and the foot of the ridges, sun-dried bunch grass, low succulents, quiver
        /// trees and taaibos standing alone on the slopes, granite boulders and boulder piles on the
        /// ridges and the lane's edges, and dead branches lying about. Only succulents a hand high
        /// grow in the lane, never right in front of a spot.
        /// </summary>
        static void Veld(DuelMap map, Transform root, System.Random rng, Biomes.Lane lane, float length, System.Func<float, float, float> Y)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var straw = new Color(1.02f, 0.86f, 0.58f);
            var flora = new Wilds.Flora()
                .Kind("grass_medium_02", 0.6f, straw).Kind("grass_medium_01", 0.65f, straw)
                .Kind("wild_rooibos_bush", 1.1f).Kind("didelta_spinosa", 1.7f).Kind("searsia_lucida", 3.4f)
                .Kind("quiver_tree_01", 5.5f, new Color(1.04f, 0.98f, 0.9f)).Kind("quiver_tree_02", 4.2f, new Color(1.04f, 0.98f, 0.9f))
                .Kind("cheiridopsis_succulent", 0.22f);
            var bushes = flora.Available("wild_rooibos_bush", "didelta_spinosa", "searsia_lucida");
            var grass = flora.Available("grass_medium_02", "grass_medium_01");
            var trees = flora.Available("quiver_tree_01", "quiver_tree_02", "searsia_lucida");
            bool Off(float x, float s, float margin) => !lane.Contains(x, s, margin) && Mathf.Abs(x - TrackX(s)) > 4 && Biomes.NearestSpot(map, x, s) > 12 + margin;

            // Bushes in clumps: thickest in the wash, along the track and where the ridges begin.
            Wilds.Clumps(flora, rng, bushes, 300, 8, 10, () =>
            {
                float x = R(-650, 650), s = R(-500, length + 600);
                float q = (s - 430) / 70;
                bool wet = Mathf.Exp(-q * q) > 0.3f, foot = Mathf.Abs(Mathf.Abs(x) - 110) < 50;
                if (!wet && !foot && rng.NextDouble() > 0.35) return null;
                return Off(x, s, 6) ? new Vector2(x, s) : (Vector2?)null;
            }, (x, s) => Off(x, s, 3), Y, 0.6f, 1.25f, 0.08f);
            // Bunch grass everywhere off the lane, and a margin of it up to the lane's edges.
            Wilds.Clumps(flora, rng, grass, 700, 12, 7, () =>
            {
                float x = R(-560, 560), s = R(-400, length + 500);
                return Off(x, s, 2) ? new Vector2(x, s) : (Vector2?)null;
            }, (x, s) => Off(x, s, 0), Y, 0.6f, 1.3f, 0.03f);
            // Lone trees on the slopes, none in the valley floor's sight lines.
            for (int i = 0, n = 0; trees.Length > 0 && i < 600 && n < 46; i++)
            {
                float x = (rng.Next(2) == 0 ? -1 : 1) * R(90, 700), s = R(-450, length + 500);
                if (!Off(x, s, 20)) continue;
                n++;
                flora.Plant(trees[rng.Next(trees.Length)], new Vector3(x, Y(x, s) - 0.1f, s), R(0, 360), R(0.75f, 1.3f), rng.Next(2) == 0);
            }
            // Succulents and sun-dried tufts a hand high over the lane floor, clear of the spots.
            Wilds.LaneCarpet(flora, map, lane, rng, new[] { "cheiridopsis_succulent" }, 2600, 0.6f, 0.9f, Y);
            Wilds.LaneCarpet(flora, map, lane, rng, grass, 3200, 0.22f, 0.32f, Y, (x, s) => Mathf.Abs(x - TrackX(s)) > 3);
            flora.Build(root, "Veld");

            // Granite: single boulders along the lane's edges and over the flats, piles and cliffs on the ridges.
            var tinted = new Dictionary<Material, Material>();
            var single = Wilds.Present("namaqualand_boulder_02", "namaqualand_boulder_03", "namaqualand_boulder_04", "namaqualand_boulder_05");
            for (int i = 0, n = 0; single.Length > 0 && i < 800 && n < 26; i++)
            {
                bool edge = n % 3 == 0;
                float x = (rng.Next(2) == 0 ? -1 : 1) * (edge ? R(lane.HalfWidth + 6, lane.HalfWidth + 40) : R(lane.HalfWidth + 20, 420));
                float s = R(-300, length + 300);
                if (!Off(x, s, 6)) continue;
                n++;
                Wilds.Rock(single[n % single.Length], new Vector3(x, Y(x, s), s), R(0, 360), edge ? R(1.4f, 3.2f) : R(2, 6), root, Bleached, tinted, 0.22f);
            }
            var piles = Wilds.Present("namaqualand_boulders_01", "namaqualand_cliff_01");
            for (int i = 0, n = 0; piles.Length > 0 && i < 600 && n < 8; i++)
            {
                float x = (rng.Next(2) == 0 ? -1 : 1) * R(130, 650), s = R(-500, length + 600);
                if (!Off(x, s, 30)) continue;
                n++;
                string id = piles[n % piles.Length];
                Wilds.Rock(id, new Vector3(x, Y(x, s), s), R(0, 360), id.Contains("cliff") ? R(18, 34) : R(8, 16), root, Bleached, tinted, 0.25f);
            }
            var deadwood = Wilds.Present("dry_branches_medium_01");
            for (int i = 0, n = 0; deadwood.Length > 0 && i < 300 && n < 8; i++)
            {
                float x = (rng.Next(2) == 0 ? -1 : 1) * R(lane.HalfWidth + 4, 260), s = R(-200, length + 200);
                if (!Off(x, s, 4)) continue;
                n++;
                Wilds.Rock(deadwood[0], new Vector3(x, Y(x, s), s), R(0, 360), R(1.5f, 3), root, new Color(0.95f, 0.88f, 0.8f), tinted, 0.05f);
            }
        }

        static void Boulder(MeshBuilder mb, System.Random rng, float x, float s, float size, System.Func<float, float, float> Y)
        {
            var dims = new Vector3(size * (1 + (float)rng.NextDouble() * 0.6f), size * (0.55f + (float)rng.NextDouble() * 0.4f), size * (1 + (float)rng.NextDouble() * 0.5f));
            Biomes.Boulder(mb, new Vector3(x, Y(x, s) - dims.y * 0.25f, s), dims, rng.Next(10000), 0.62f, 0.2f, (float)rng.NextDouble() * 360);
        }

        /// <summary>Dry scrub (the scanned shrub, sun-dried in colour) and a few dead thorn trees, off the lane.</summary>
        static void Scrub(Transform root, System.Random rng, Biomes.Lane lane, float length, System.Func<float, float, float> Y)
        {
            var dried = new Dictionary<Material, Material>();
            int planted = 0;
            for (int i = 0; i < 600 && planted < 42; i++)
            {
                float x = ((float)rng.NextDouble() * 2 - 1) * 420;
                float s = -250 + (float)rng.NextDouble() * (length + 500);
                if (lane.Contains(x, s, 6) || Mathf.Abs(x - TrackX(s)) < 5) continue;
                // Scrub grows where water collects: thicker along the wash.
                float q = (s - 430) / 60;
                if (rng.NextDouble() > 0.35 + Mathf.Exp(-q * q)) continue;
                var go = Biomes.PlaceSized("shrub_02", new Vector3(x, Y(x, s), s), (float)rng.NextDouble() * 360, 0.6f + (float)rng.NextDouble() * 0.9f, root, 0.1f);
                Biomes.Tint(go, new Color(0.92f, 0.76f, 0.5f), dried);
                planted++;
            }
            var bark = new MeshBuilder();
            for (int i = 0; i < 9; i++)
            {
                float s = -150 + i * 150 + (float)rng.Next(-40, 40);
                float x = TrackX(s) + (rng.Next(2) == 0 ? -1 : 1) * (12 + (float)rng.NextDouble() * 40);
                if (lane.Contains(x, s, 8)) continue;
                DeadTree(bark, new Vector3(x, Y(x, s), s), 3.5f + (float)rng.NextDouble() * 2.5f, rng);
            }
            bark.Object("Dead trees", Library.Surface("weathered_planks", 1.2f, new Color(0.55f, 0.5f, 0.45f)), root);
        }

        /// <summary>A bare thorn tree: a leaning trunk forking into upturned branches.</summary>
        static void DeadTree(MeshBuilder mb, Vector3 foot, float height, System.Random rng)
        {
            var lean = new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 0.6f;
            var fork = foot + (Vector3.up + lean) * height * 0.45f;
            mb.Tube(foot + Vector3.down * 0.3f, fork, height * 0.045f, 6);
            int branches = 3 + rng.Next(2);
            for (int b = 0; b < branches; b++)
            {
                float a = (b + (float)rng.NextDouble() * 0.5f) * Mathf.PI * 2 / branches;
                var dir = new Vector3(Mathf.Cos(a), 0.9f + (float)rng.NextDouble() * 0.6f, Mathf.Sin(a)).normalized;
                var tip = fork + dir * height * (0.45f + (float)rng.NextDouble() * 0.2f);
                mb.Tube(fork, tip, height * 0.025f, 5);
                for (int t = 0; t < 2; t++)
                {
                    var twig = (dir + new Vector3((float)rng.NextDouble() - 0.5f, 0.3f, (float)rng.NextDouble() - 0.5f)).normalized;
                    var from = Vector3.Lerp(fork, tip, 0.5f + t * 0.3f);
                    mb.Tube(from, from + twig * height * 0.2f, height * 0.012f, 4, false);
                }
            }
        }

        /// <summary>
        /// Telegraph poles along the outside of the track every 55 m: weathered timber, a cross-arm
        /// with three insulators, and wires sagging between them. Some lean.
        /// </summary>
        static void Poles(Transform root, System.Random rng, System.Func<float, float, float> Y)
        {
            var timber = new MeshBuilder();
            var insulators = new MeshBuilder();
            var wires = new MeshBuilder();
            Vector3[]? prev = null;
            for (float s = -900; s <= 1800; s += 55)
            {
                float x = TrackX(s) - 7;
                var foot = new Vector3(x, Y(x, s), s);
                var lean = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.5f, 0, ((float)rng.NextDouble() - 0.5f) * 0.3f);
                var top = foot + Vector3.up * 8.6f + lean;
                timber.Tube(foot + Vector3.down * 0.8f, top, 0.13f, 7);
                var arm = top + Vector3.down * 0.45f;
                timber.Beam(arm + Vector3.left * 1.15f, arm + Vector3.right * 1.15f, 0.1f, 0.12f);
                // Brace from the pole to the arm.
                timber.Beam(arm + Vector3.down * 0.6f, arm + Vector3.left * 0.6f, 0.05f, 0.05f);
                var ends = new[] { arm + new Vector3(-1.0f, 0.06f, 0), arm + new Vector3(1.0f, 0.06f, 0), top + new Vector3(0, 0.05f, 0) };
                var attach = new Vector3[3];
                for (int k = 0; k < 3; k++)
                {
                    insulators.Cylinder(ends[k], 0.05f, 0.16f, 6);
                    attach[k] = ends[k] + Vector3.up * 0.15f;
                }
                if (prev != null)
                    for (int k = 0; k < 3; k++) Biomes.Wire(wires, prev[k], attach[k], 0.9f, 0.025f);
                prev = attach;
            }
            timber.Object("Telegraph poles", Library.Surface("weathered_planks", 2, new Color(0.5f, 0.42f, 0.34f)), root);
            insulators.Object("Insulators", Library.Plain(new Color(0.42f, 0.5f, 0.45f), 0.8f), root, shadows: false);
            wires.Object("Wires", Library.Plain(new Color(0.07f, 0.07f, 0.07f), 0.4f, 0.6f), root, shadows: false);
        }

        /// <summary>
        /// A roofless ruin w × d, walls h high, turned `yaw`: walls of 0.8 m columns broken to ragged
        /// heights, a doorway and window holes, a few roof beams still across and one fallen in, rubble
        /// at the foot of the broken stretches, a sheet of roofing tin leaning on a wall. A concrete
        /// ruin also keeps half its roof slab, the other half collapsed inside, and bristles with rebar.
        /// </summary>
        static void Ruin(MeshBuilder walls, MeshBuilder wood, MeshBuilder tin, MeshBuilder rebar, System.Func<float, float, float> Y,
            float cx, float cs, float yaw, float w, float d, float h, System.Random rng, bool concrete)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            float floor = float.MaxValue;
            foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1), (0, 0) })
            {
                var c = new Vector3(cx, 0, cs) + rot * new Vector3(dx * w / 2, 0, dz * d / 2);
                floor = Mathf.Min(floor, Y(c.x, c.z));
            }
            Vector3 P(float lx, float ly, float lz) => new Vector3(cx, floor, cs) + rot * new Vector3(lx, ly, lz);
            const float T = 0.45f, Col = 0.8f, Sunk = 0.4f;

            // Walls: (start, end, along-x?, offset, has door, has window, damaged share).
            var sides = new (bool alongX, float offset, float len, bool door, bool window)[]
            {
                (true, -d / 2 + T / 2, w, true, false),
                (true, d / 2 - T / 2, w, false, true),
                (false, -w / 2 + T / 2, d - 2 * T, false, true),
                (false, w / 2 - T / 2, d - 2 * T, false, false),
            };
            var rubble = new List<Vector3>();
            foreach (var side in sides)
            {
                int cols = Mathf.Max(2, Mathf.RoundToInt(side.len / Col));
                float cw = side.len / cols;
                // Each wall loses its top along a random stretch: heights wander down and back up.
                float broken = rng.Next(3) == 0 ? 0 : 0.35f + (float)rng.NextDouble() * 0.45f;
                int centre = rng.Next(cols);
                for (int i = 0; i < cols; i++)
                {
                    float along = -side.len / 2 + (i + 0.5f) * cw;
                    float dist = Mathf.Abs(i - centre) / (float)cols;
                    float top = h * (1 - broken * Mathf.Clamp01(1 - dist * 2.2f)) * (0.94f + (float)rng.NextDouble() * 0.06f);
                    bool door = side.door && i == cols / 2;
                    bool window = side.window && (i == cols / 3 || i == cols - 1 - cols / 3);
                    var lc = side.alongX ? new Vector3(along, 0, side.offset) : new Vector3(side.offset, 0, along);
                    var size = side.alongX ? new Vector3(cw + 0.01f, 0, T) : new Vector3(T, 0, cw + 0.01f);
                    void Piece(float y0, float y1)
                    {
                        if (y1 - y0 < 0.05f) return;
                        walls.Box(P(lc.x, (y0 + y1) / 2, lc.z), new Vector3(size.x, y1 - y0, size.z), rot);
                    }
                    if (door) Piece(2.1f, Mathf.Max(2.1f, top));
                    else if (window && top > 1.9f) { Piece(-Sunk, 0.95f); Piece(1.85f, top); }
                    else Piece(-Sunk, top);
                    if (top < h * 0.8f && rng.NextDouble() < 0.7)
                        rubble.Add(P(lc.x + ((float)rng.NextDouble() - 0.5f) * 1.6f, 0, lc.z + ((float)rng.NextDouble() - 0.5f) * 1.6f));
                }
            }
            foreach (var r in rubble)
            {
                float size = 0.25f + (float)rng.NextDouble() * 0.45f;
                Biomes.Boulder(walls, new Vector3(r.x, Y(r.x, r.z) - size * 0.3f, r.z), new Vector3(size * 1.4f, size * 0.7f, size), rng.Next(1000), 0.45f, 0.15f, (float)rng.NextDouble() * 360);
            }

            // Roof timbers across the short span; most have gone.
            for (float z = -d / 2 + 0.6f; z < d / 2 - 0.4f; z += 0.75f)
            {
                if (rng.NextDouble() < 0.55) continue;
                wood.Box(P(0, h - 0.12f, z), new Vector3(w + 0.3f, 0.16f, 0.14f), rot);
            }
            wood.Beam(P(-w / 2 + 0.4f, h - 0.2f, -0.3f), P(w / 4, 0.1f, 0.6f), 0.16f, 0.14f);
            // A door lintel in the front wall.
            wood.Box(P(0, 2.15f, -d / 2 + T / 2), new Vector3(1.4f, 0.14f, T + 0.06f), rot);
            // Roofing tin leaning on the outside of the back wall.
            var lean = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-18, 0, 0);
            tin.Box(P(w * 0.2f, 0.95f, d / 2 + 0.38f), new Vector3(1.8f, 2.1f, 0.03f), lean);

            if (!concrete) return;
            // Half the slab still on, the rest fallen in at an angle, rebar sticking out of the break.
            walls.Box(P(-w / 4, h + 0.12f, 0), new Vector3(w / 2 + 0.2f, 0.24f, d + 0.2f), rot);
            walls.Box(P(w / 4 + 0.1f, h * 0.55f, 0), new Vector3(w / 2, 0.22f, d - 1f), rot * Quaternion.Euler(0, 0, -32));
            for (int i = 0; i < 9; i++)
            {
                float z = -d / 2 + 0.4f + i * (d - 0.8f) / 8;
                var from = P(0.02f, h + 0.12f, z);
                rebar.Beam(from, from + rot * new Vector3(0.5f + (float)rng.NextDouble() * 0.5f, ((float)rng.NextDouble() - 0.6f) * 0.6f, 0), 0.025f, 0.025f);
            }
        }
    }
}
