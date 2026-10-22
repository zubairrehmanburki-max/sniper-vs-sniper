using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// A drowned marsh round the watchtowers: reed beds on the shallows and mud banks, dead trees and
    /// stumps standing in the water, half-sunk fence lines, floating logs, and two ruined timber piers
    /// running out from the shore behind each tower. Everything keeps out of a corridor along the lane
    /// so the line between the towers, and the splashes of shots that fall short, stay in plain view.
    /// All of it merges into a handful of meshes: one per material.
    /// </summary>
    public static class Marsh
    {
        /// <summary>Half width of the clear corridor along the lane, m.</summary>
        const float Corridor = 24;

        public static void Build(DuelMap map, Transform parent)
        {
            var root = new GameObject("Marsh").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(map.Id.GetHashCode() ^ 0x3a75);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var ground = map.Terrain ?? map.Ground;
            float Y(float x, float s) => (float)ground(x, -s);
            float lane = (float)map.LaneLength;
            float water = (float)(map.Water ?? 0);
            // The lane corridor and the tower footprints, widened by `margin`: nothing goes there.
            bool InCorridor(float x, float s, float margin) =>
                (Mathf.Abs(x) < Corridor + margin && s > -20 - margin && s < lane + 20 + margin)
                || new Vector2(x, s).magnitude < 16 + margin || new Vector2(x, s - lane).magnitude < 16 + margin
                || Biomes.InSightLine(map, x, s, 8 + margin);

            var green = new MeshBuilder();
            var straw = new MeshBuilder();
            var heads = new MeshBuilder();
            var wood = new MeshBuilder();
            var mud = new MeshBuilder();
            var wire = new MeshBuilder();
            // Prevailing wind on this map blows toward +x: reeds and dead trees lean that way.
            var downwind = new Vector3(1, 0, 0.25f).normalized;

            // ---- Reed beds and mud banks on the shallows ------------------------------------------
            int beds = 0;
            for (int attempt = 0; attempt < 9000 && beds < 170; attempt++)
            {
                float cx = R(-420, 420), cs = R(-240, lane + 240);
                float y = Y(cx, cs);
                if (y < water - 3.4f || y > water + 0.8f || InCorridor(cx, cs, 4)) continue;
                beds++;
                float rx = R(4, 13), rz = R(4, 13);
                if (rng.NextDouble() < 0.6)
                    mud.Mound(new Vector3(cx, water - 0.45f, cs), rx * 1.15f, rz * 1.15f, R(0.55f, 0.75f), rng, 0.3f, 4, 16);
                int blades = Mathf.Min(360, (int)(rx * rz * R(1.8f, 2.8f)));
                float dead = R(0.15f, 0.6f);
                for (int i = 0; i < blades; i++)
                {
                    // Uniform over the ellipse; shorter towards the edge of the bed.
                    float a = R(0, 2 * Mathf.PI), f = Mathf.Sqrt((float)rng.NextDouble());
                    float x = cx + Mathf.Cos(a) * rx * f, s = cs + Mathf.Sin(a) * rz * f;
                    if (InCorridor(x, s, 0)) continue;
                    float baseY = Mathf.Max(Y(x, s), water - 0.15f);
                    float h = R(1.3f, 2.6f) * (1 - 0.45f * f * f);
                    var rootP = new Vector3(x, baseY, s);
                    var tip = rootP + Vector3.up * h + downwind * h * R(0.05f, 0.3f) + new Vector3(R(-0.2f, 0.2f), 0, R(-0.2f, 0.2f));
                    var facing = new Vector3(Mathf.Cos(a * 3.7f), 0, Mathf.Sin(a * 3.7f));
                    bool isDead = rng.NextDouble() < dead;
                    (isDead ? straw : green).Blade(rootP, tip, R(0.035f, 0.065f), facing);
                    // Bulrush heads on a few of the live stems.
                    if (!isDead && rng.NextDouble() < 0.07)
                    {
                        var t0 = Vector3.Lerp(rootP, tip, 0.8f);
                        heads.Tube(t0, Vector3.Lerp(rootP, tip, 0.92f), 0.035f, 0.03f, 5, caps: true);
                    }
                }
            }
            // A few bare mud banks with nothing growing on them.
            for (int i = 0, n = 0; i < 2000 && n < 16; i++)
            {
                float x = R(-380, 380), s = R(-200, lane + 200);
                float y = Y(x, s);
                if (y < water - 1.2f || y > water + 0.5f || InCorridor(x, s, 2)) continue;
                n++;
                mud.Mound(new Vector3(x, water - 0.4f, s), R(5, 16), R(4, 12), R(0.5f, 0.6f), rng, 0.35f, 4, 16);
            }

            // ---- Dead trees and stumps ------------------------------------------------------------
            void DeadTree(Vector3 foot, float height, float r0)
            {
                var lean = (downwind * R(0, 0.08f) + new Vector3(R(-0.06f, 0.06f), 0, R(-0.06f, 0.06f)));
                var axis = (Vector3.up + lean).normalized;
                var top = foot + axis * height;
                // Snapped crown: the trunk ends thick and ragged.
                wood.Tube(foot, top, r0, r0 * 0.35f, 9, caps: true);
                for (int k = 0; k < 2; k++)
                    wood.Tube(top, top + (axis + new Vector3(R(-0.3f, 0.3f), 0, R(-0.3f, 0.3f))).normalized * R(0.4f, 0.9f), r0 * 0.2f, 0.01f, 5);
                int branches = rng.Next(3, 7);
                for (int b = 0; b < branches; b++)
                {
                    float at = R(0.45f, 0.9f);
                    var start = Vector3.Lerp(foot, top, at);
                    float az = R(0, 2 * Mathf.PI), elev = R(0.25f, 0.9f);
                    var dir = (new Vector3(Mathf.Cos(az), 0, Mathf.Sin(az)) + Vector3.up * elev).normalized;
                    float len = R(1.0f, 3.8f) * (1.2f - at);
                    float rb = r0 * Mathf.Lerp(1, 0.35f, at) * 0.45f;
                    var end = start + dir * len;
                    wood.Tube(start, end, rb, 0.015f, 6);
                    if (rng.NextDouble() < 0.5)
                    {
                        var mid = Vector3.Lerp(start, end, 0.6f);
                        var twig = (dir + new Vector3(R(-0.6f, 0.6f), R(0, 0.6f), R(-0.6f, 0.6f))).normalized;
                        wood.Tube(mid, mid + twig * len * 0.5f, rb * 0.4f, 0.01f, 5);
                    }
                }
            }
            for (int i = 0, n = 0; i < 3000 && n < 28; i++)
            {
                float x = R(-400, 400), s = R(-230, lane + 230);
                float y = Y(x, s);
                if (y < water - 3.2f || y > water + 2 || InCorridor(x, s, 14)) continue;
                n++;
                DeadTree(new Vector3(x, y - 0.3f, s), (water - y) + R(4, 12), R(0.2f, 0.38f));
            }
            for (int i = 0, n = 0; i < 3000 && n < 70; i++)
            {
                float x = R(-380, 380), s = R(-220, lane + 220);
                float y = Y(x, s);
                if (y < water - 3.2f || y > water + 1 || InCorridor(x, s, 2)) continue;
                n++;
                float r = R(0.18f, 0.45f);
                var foot = new Vector3(x, y - 0.2f, s);
                var top = new Vector3(x + R(-0.15f, 0.15f), Mathf.Max(y, water) + R(0.25f, 1.6f), s + R(-0.15f, 0.15f));
                wood.Tube(foot, top, r, r * R(0.75f, 0.95f), 8, caps: true);
                // Splinters where it broke.
                for (int k = rng.Next(0, 3); k > 0; k--)
                {
                    var p = top + new Vector3(R(-r, r) * 0.6f, 0, R(-r, r) * 0.6f);
                    wood.Tube(p - Vector3.up * 0.1f, p + new Vector3(R(-0.1f, 0.1f), R(0.2f, 0.6f), R(-0.1f, 0.1f)), r * 0.25f, 0.01f, 4);
                }
            }

            // ---- Half-sunk fence lines and floating logs -----------------------------------------
            for (int attempt = 0, lines = 0; attempt < 500 && lines < 12; attempt++)
            {
                float x = R(-360, 360), s = R(-200, lane + 200);
                if (InCorridor(x, s, 6)) continue;
                lines++;
                float az = R(0, Mathf.PI);
                var step = new Vector3(Mathf.Cos(az), 0, Mathf.Sin(az)) * 2.6f;
                int posts = rng.Next(8, 24);
                Vector3? prevTop = null;
                for (int i = 0; i < posts; i++)
                {
                    var p = new Vector3(x, 0, s) + step * i + new Vector3(R(-0.2f, 0.2f), 0, R(-0.2f, 0.2f));
                    if (InCorridor(p.x, p.z, 1) || rng.NextDouble() < 0.18) { prevTop = null; continue; }
                    float bed = Y(p.x, p.z);
                    var foot = new Vector3(p.x, bed - 0.4f, p.z);
                    var tilt = new Vector3(R(-0.25f, 0.25f), 0, R(-0.25f, 0.25f));
                    float above = R(0.2f, 1.3f);
                    var top = new Vector3(p.x, Mathf.Max(bed, water) + above, p.z) + tilt * (Mathf.Max(bed, water) + above - foot.y) * 0.3f;
                    wood.Beam(foot, top, R(0.1f, 0.15f), R(0.1f, 0.15f));
                    if (prevTop.HasValue && rng.NextDouble() < 0.6)
                    {
                        // Rusted wire sagging between the posts, sometimes trailing into the water.
                        var a = prevTop.Value - Vector3.up * 0.1f;
                        var b = top - Vector3.up * 0.1f;
                        var mid = (a + b) / 2;
                        mid.y = Mathf.Max(water - 0.1f, mid.y - R(0.15f, 0.6f));
                        wire.Beam(a, mid, 0.015f, 0.015f);
                        wire.Beam(mid, b, 0.015f, 0.015f);
                    }
                    prevTop = top;
                }
            }
            for (int i = 0, n = 0; i < 1000 && n < 14; i++)
            {
                float x = R(-360, 360), s = R(-200, lane + 200);
                if (Y(x, s) > water - 0.3f || InCorridor(x, s, 4)) continue;
                n++;
                float az = R(0, Mathf.PI), len = R(3, 8), r = R(0.15f, 0.3f);
                var d = new Vector3(Mathf.Cos(az), R(-0.04f, 0.04f), Mathf.Sin(az)) * len / 2;
                var c = new Vector3(x, water - r * 0.4f, s);
                wood.Tube(c - d, c + d, r, r * 0.8f, 8, caps: true);
            }

            // ---- Ruined piers from the shore behind each tower ------------------------------------
            Pier(wood, rng, ground, water, 62, lane + 60, lane + 420);
            Pier(wood, rng, ground, water, -58, -60, -420);

            Shores(map, root, rng, Y, InCorridor, lane, water);

            green.Object("Reeds", Cloth(new Color(0.40f, 0.45f, 0.21f)), root, shadows: false);
            straw.Object("Dead reeds", Cloth(new Color(0.64f, 0.56f, 0.36f)), root, shadows: false);
            heads.Object("Bulrush heads", Library.Plain(new Color(0.22f, 0.13f, 0.07f), 0.15f), root, shadows: false);
            mud.Object("Mud banks", Library.Surface("brown_mud_leaves_01", 4, new Color(0.62f, 0.56f, 0.46f)), root, shadows: false);
            wood.Object("Dead wood", Library.Surface("weathered_planks", 3, new Color(0.64f, 0.62f, 0.57f)), root);
            wire.Object("Fence wire", Library.Plain(new Color(0.2f, 0.12f, 0.08f), 0.3f, 0.5f), root, shadows: false);
        }

        /// <summary>
        /// The land round the marsh: a fringe of shrubs, ferns and tussock grass where the mud meets
        /// the meadow, alder and willow woods in clumps up the shores (thick enough to close the view
        /// behind each tower), fallen trunks and stumps at the wood edges, and low wooded hills
        /// on the horizon.
        /// </summary>
        static void Shores(DuelMap map, Transform root, System.Random rng, System.Func<float, float, float> Y,
            System.Func<float, float, float, bool> inCorridor, float lane, float water)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // Distance out from the marsh's middle, in the terrain's own stretched measure.
            float Ring(float x, float s) => new Vector2(x * 0.75f, s - 225).magnitude;
            var flora = new Wilds.Flora()
                .Kind("island_tree_01", 13).Kind("island_tree_02", 12).Kind("island_tree_03", 12).Kind("tree_small_02", 8)
                .Kind("fern_02", 0.9f).Kind("grass_medium_01", 0.75f).Kind("grass_medium_02", 0.7f).Kind("shrub_01", 1.6f).Kind("shrub_02", 1.5f).Kind("shrub_03", 1.4f).Kind("shrub_04", 1.3f);
            var trees = flora.Available("island_tree_02", "island_tree_03", "island_tree_01", "tree_small_02");
            var shrubs = flora.Available("shrub_01", "shrub_02", "shrub_03", "shrub_04");
            var tussock = flora.Available("grass_medium_01", "grass_medium_02", "fern_02");

            // Woods: clumps on the shores beyond the waterline, a few strays on the meadow between them.
            Wilds.Clumps(flora, rng, trees, 420, 22, 50, () =>
            {
                float a = R(0, 2 * Mathf.PI), r = R(480, 1500);
                float x = Mathf.Cos(a) * r / 0.75f, s = 225 + Mathf.Sin(a) * r;
                if (Mathf.PerlinNoise(x * 0.004f + 3.3f, s * 0.004f + 8.1f) < 0.3f) return null;
                return new Vector2(x, s);
            }, (x, s) => Ring(x, s) > 470 && Y(x, s) > water + 1.2f, Y, 0.75f, 1.25f, 0.3f);
            // The wood edge on the shore: shrubs and tussocks in a ragged band where mud meets meadow.
            Wilds.Clumps(flora, rng, shrubs, 260, 9, 10, () =>
            {
                float a = R(0, 2 * Mathf.PI), r = R(430, 520);
                float x = Mathf.Cos(a) * r / 0.75f, s = 225 + Mathf.Sin(a) * r;
                return Y(x, s) > water + 0.3f && !inCorridor(x, s, 8) ? new Vector2(x, s) : (Vector2?)null;
            }, (x, s) => Y(x, s) > water + 0.2f && !inCorridor(x, s, 4), Y, 0.6f, 1.3f);
            // Tussocks and ferns along the waterline and on the mud islets out in the marsh.
            Wilds.Clumps(flora, rng, tussock, 520, 12, 7, () =>
            {
                float x = R(-560, 560), s = R(-260, lane + 260);
                float y = Y(x, s);
                return y > water - 0.1f && y < water + 1.5f && !inCorridor(x, s, 6) ? new Vector2(x, s) : (Vector2?)null;
            }, (x, s) => Y(x, s) > water - 0.15f && !inCorridor(x, s, 3), Y, 0.8f, 1.4f, 0.02f);
            flora.Build(root, "Shore plants");

            // Fallen trunks and stumps along the wood edges.
            var tinted = new Dictionary<Material, Material>();
            string[] logs = Wilds.Present("dead_tree_trunk", "dead_tree_trunk_02", "tree_stump_01");
            for (int i = 0, n = 0; logs.Length > 0 && i < 400 && n < 22; i++)
            {
                float a = R(0, 2 * Mathf.PI), r = R(445, 560);
                float x = Mathf.Cos(a) * r / 0.75f, s = 225 + Mathf.Sin(a) * r;
                float y = Y(x, s);
                if (y < water + 0.2f || inCorridor(x, s, 10)) continue;
                n++;
                string id = logs[n % logs.Length];
                Wilds.Rock(id, new Vector3(x, y, s), R(0, 360), id.StartsWith("tree_stump") ? R(0.8f, 1.4f) : R(5, 9), root, null, tinted, 0.05f);
            }

            // Low wooded hills where the farmland rolls away, so the far shore has a skyline.
            Biomes.Horizon(root, lane / 2, new Biomes.Hills
            {
                MinHeight = 40,
                MaxHeight = 120,
                Rock = Library.Surface("sparse_grass", 60, new Color(0.42f, 0.52f, 0.36f)),
                Seed = 52,
                Ground = Y,
            });
        }

        /// <summary>A double-sided foliage material (the flag cloth template) in a flat colour.</summary>
        static Material Cloth(Color c)
        {
            var mat = new Material(Library.Template("Cloth"));
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Smoothness", 0.25f);
            return mat;
        }

        /// <summary>
        /// A timber landing stage at world x on pile bents every 3 m, from the shore (found by walking
        /// from lane position `landward` towards `seaEnd` until the ground meets the water) out to
        /// `seaEnd`. It decays towards the water end: boards gone, then whole bays sagged into the
        /// water, then only stumps of piles.
        /// </summary>
        static void Pier(MeshBuilder wood, System.Random rng, Ground ground, float water, float x, float seaEnd, float landward)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float Y(float s) => (float)ground(x, -s);
            float dir = Mathf.Sign(seaEnd - landward);
            // Walk from the land side towards the sea to the first point that is under water.
            float shore = landward;
            while ((shore - seaEnd) * dir < 0 && Y(shore) > water + 0.3f) shore += dir * 2;
            if ((shore - seaEnd) * dir >= 0) return;
            float start = shore - dir * 6;
            float length = Mathf.Abs(seaEnd - start);
            float deck = water + 1.3f;
            const float Half = 1.4f, Bay = 3;
            int bays = Mathf.FloorToInt(length / Bay);
            float PileTop(float f) => f < 0.7f ? deck - 0.25f : deck - 0.25f - R(0, 1.6f) * (f - 0.6f) * 2.5f;
            for (int i = 0; i <= bays; i++)
            {
                float s = start + dir * i * Bay, f = (float)i / bays;
                float bed = Y(s);
                foreach (float k in new[] { -1f, 1f })
                {
                    if (f > 0.75f && rng.NextDouble() < 0.3) continue;
                    float t = Mathf.Max(PileTop(f), water + 0.2f);
                    var lean = f > 0.6f ? new Vector3(R(-0.4f, 0.4f), 0, R(-0.4f, 0.4f)) : Vector3.zero;
                    wood.Tube(new Vector3(x + k * Half, Mathf.Min(bed, water) - 0.6f, s), new Vector3(x + k * Half, t, s) + lean, 0.15f, 0.13f, 7, caps: true);
                }
                if (f < 0.72f) wood.Box(new Vector3(x, deck - 0.36f, s), new Vector3(2 * Half + 0.5f, 0.22f, 0.25f));
                if (i == bays) break;
                // The bay from this bent to the next.
                float s0 = s, s1 = s + dir * Bay;
                if (f > 0.82f) continue;
                bool sagged = f > 0.55f;
                float y0 = deck, y1 = sagged ? water - R(0.1f, 0.5f) : deck;
                if (sagged && rng.NextDouble() < 0.4) continue;
                float keep = Mathf.Lerp(0.95f, 0.45f, f / 0.82f);
                foreach (float k in new[] { -1f, 0f, 1f })
                    if (!sagged || rng.NextDouble() < 0.7)
                        wood.Beam(new Vector3(x + k * (Half - 0.1f), y0 - 0.17f, s0), new Vector3(x + k * (Half - 0.1f), y1 - 0.17f, s1), 0.12f, 0.2f);
                for (float d = 0.12f; d < Bay; d += 0.24f)
                {
                    if (rng.NextDouble() > keep) continue;
                    float y = Mathf.Lerp(y0, y1, d / Bay) - 0.03f;
                    wood.Box(new Vector3(x + R(-0.05f, 0.05f), y, s0 + dir * d), new Vector3(2 * Half + 0.2f, 0.05f, 0.21f), Quaternion.Euler(0, R(-3, 3), 0));
                }
            }
        }
    }
}
