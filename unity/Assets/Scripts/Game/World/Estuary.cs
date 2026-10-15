using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Twin Bridges' river front, between the water and the city (Skyline): rip-rap boulders along
    /// the waterline, a paved riverside walk with railings and lamps at the top of each bank, a line
    /// of trees behind it and a riverside road with parked cars, approach roads running inland from
    /// each bridge, and wooded hills beyond the city. Nothing goes near the firing spots: anything
    /// behind a shooter is what the other side sees him against.
    /// Plants are cut-out pictures (Impostor) so hundreds cost little.
    /// </summary>
    public static class Estuary
    {
        /// <summary>Distances from the river's centre line (the terrain is symmetric in x).</summary>
        const float WalkIn = 288, WalkOut = 300, TreeLine = 305, RoadIn = 311, RoadOut = 322, PavementOut = 326;

        public static void Build(DuelMap map, Transform parent)
        {
            var root = new GameObject("Estuary").transform;
            root.SetParent(parent, false);
            var ground = map.Terrain ?? map.Ground;
            float Y(float x, float s) => (float)ground(x, -s);
            var rng = new System.Random(4401);
            float R() => (float)rng.NextDouble();
            float lane = (float)map.LaneLength;
            float water = (float)(map.Water ?? 0);
            var bridges = new List<Structure>();
            if (map.Look.Structures != null)
                foreach (var st in map.Look.Structures)
                    if (st.Kind == StructureKind.Bridge) bridges.Add(st);
            float FromBridge(float s)
            {
                float d = float.MaxValue;
                foreach (var b in bridges) d = Mathf.Min(d, Mathf.Abs(s - (float)b.S));
                return d;
            }

            const float S0 = -1300, S1 = 2300, Step = 4;
            var walk = new MeshBuilder();
            var road = new MeshBuilder();
            var kerb = new MeshBuilder();
            var marks = new MeshBuilder();
            var steel = new MeshBuilder();
            var rocks = new MeshBuilder();

            // A slab on the ground from |x| = a to b over one step of s, its top `lift` proud, with
            // the river-side edge as a kerb face.
            void Slab(MeshBuilder mb, float side, float a, float b, float s, float ds, float lift, bool edge)
            {
                Vector3 P(float ax, float ss) => new Vector3(side * ax, Y(side * ax, ss) + lift, ss);
                var p00 = P(a, s); var p01 = P(a, s + ds); var p10 = P(b, s); var p11 = P(b, s + ds);
                if (side > 0) mb.Face(p00, p01, p11, p10, Vector3.up);
                else mb.Face(p10, p11, p01, p00, Vector3.up);
                if (edge)
                {
                    var d0 = p00 - Vector3.up * (lift + 0.4f);
                    var d1 = p01 - Vector3.up * (lift + 0.4f);
                    kerb.Face(d0, p00, p01, d1, new Vector3(-side, 0, 0));
                }
            }

            foreach (float side in new[] { -1f, 1f })
            {
                for (float s = S0; s < S1; s += Step)
                {
                    float near = FromBridge(s + Step / 2);
                    // The riverside walk: pale pavers, kerbed on the river side; the bridges take its place.
                    if (near > 9) Slab(walk, side, WalkIn, WalkOut, s, Step, 0.22f, true);
                    // The riverside road and its far pavement; it runs under nothing, so it stops at each bridge approach.
                    if (near > 9)
                    {
                        Slab(road, side, RoadIn, RoadOut, s, Step, 0.14f, false);
                        Slab(walk, side, RoadOut, PavementOut, s, Step, 0.26f, true);
                        // Centre line, dashed.
                        if (Mathf.Repeat(s, 12) < 6)
                        {
                            float xm = side * (RoadIn + RoadOut) / 2;
                            marks.Box(new Vector3(xm, Y(xm, s + Step / 2) + 0.16f, s + Step / 2), new Vector3(0.14f, 0.02f, Step));
                        }
                    }

                    // Railing along the walk's river edge: posts every 2 m, a top rail and a mid rail.
                    if (near > 10)
                    {
                        float x = side * (WalkIn + 0.3f);
                        var a = new Vector3(x, Y(x, s) + 0.22f, s);
                        var b = new Vector3(x, Y(x, s + Step) + 0.22f, s + Step);
                        steel.Beam(a + Vector3.up * 1.05f, b + Vector3.up * 1.05f, 0.07f, 0.07f);
                        steel.Beam(a + Vector3.up * 0.55f, b + Vector3.up * 0.55f, 0.04f, 0.04f);
                        steel.Box(a + Vector3.up * 0.53f, new Vector3(0.06f, 1.06f, 0.06f));
                        steel.Box(a + new Vector3(0, 0.53f, Step / 2), new Vector3(0.06f, 1.06f, 0.06f));
                    }

                    // Lamps along the walk every 28 m.
                    if (near > 14 && Mathf.Repeat(s, 28) < Step)
                    {
                        float x = side * (WalkIn + 1.2f);
                        var foot = new Vector3(x, Y(x, s) + 0.22f, s);
                        steel.Tube(foot, foot + Vector3.up * 5.6f, 0.09f, 0.06f, 8, true);
                        steel.Tube(foot + Vector3.up * 5.5f, foot + new Vector3(-side * 0.9f, 5.9f, 0), 0.05f, 0.04f, 6, true);
                        steel.Box(foot + new Vector3(-side * 1.05f, 5.85f, 0), new Vector3(0.55f, 0.16f, 0.3f));
                        steel.Cylinder(foot, 0.16f, 0.5f, 8);
                    }

                    // Rip-rap: boulders piled along the waterline.
                    if (s > -700 && s < lane + 900)
                    {
                        float xw = 205;
                        while (xw < 260 && Y(side * xw, s) < water + 0.2f) xw += 0.5f;
                        for (int k = 0; k < 2; k++)
                        {
                            float r = 0.55f + R() * 0.8f;
                            float bx = side * (xw - 1.5f + R() * 4.5f);
                            float bs = s + R() * Step;
                            var c = new Vector3(bx, Mathf.Max(Y(bx, bs), water - 1.2f) + r * 0.25f, bs);
                            float sq = 0.6f + R() * 0.3f, f1 = R() * 6, f2 = R() * 6;
                            Shapes.Ellipsoid(rocks, c, Quaternion.Euler(R() * 30, R() * 360, R() * 30), new Vector3(r * 1.2f, r * sq, r),
                                p => p * (0.86f + 0.14f * Mathf.Sin(p.x * 3.1f + f1) * Mathf.Cos(p.z * 2.7f + f2)), 5, 7);
                        }
                    }
                }
            }
            walk.Object("Riverside walk", Library.Surface(Pick("concrete_pavers_02", "concrete_wall_008"), 3, new Color(0.86f, 0.84f, 0.8f)), root);
            road.Object("Riverside road", Library.Surface("asphalt_02", 7, new Color(0.9f, 0.9f, 0.9f)), root);
            kerb.Object("Kerbs", Library.Surface("concrete_wall_008", 2, new Color(0.8f, 0.8f, 0.78f)), root);
            marks.Object("Road markings", Library.Plain(new Color(0.85f, 0.83f, 0.75f), 0.3f), root, shadows: false);
            steel.Object("Railings and lamps", Library.Plain(new Color(0.16f, 0.2f, 0.19f), 0.45f, 0.6f), root);
            rocks.Object("Rip-rap", Library.Rock(3, new Color(0.62f, 0.6f, 0.57f)), root);

            // Each bridge's road runs on inland up the hill on an embankment, kerbed both sides.
            var approach = new MeshBuilder();
            var verge = new MeshBuilder();
            foreach (var b in bridges)
            {
                float hw = (float)b.HalfWidth, bs = (float)b.S;
                float reach = (float)b.HalfSpan;
                while (reach < 900 && Y(reach, bs) < b.DeckY - 0.3) reach += 5;
                foreach (float side in new[] { -1f, 1f })
                {
                    for (float x = reach - 1; x < 760; x += Step)
                    {
                        float x0 = side * x, x1 = side * (x + Step);
                        float y0 = Mathf.Max(Y(x0, bs - hw), Y(x0, bs + hw)) + 0.18f, y1 = Mathf.Max(Y(x1, bs - hw), Y(x1, bs + hw)) + 0.18f;
                        var a = new Vector3(x0, y0, bs - hw + 1.8f); var c = new Vector3(x1, y1, bs + hw - 1.8f);
                        approach.Face(a, new Vector3(x0, y0, c.z), c, new Vector3(x1, y1, a.z), Vector3.up);
                        foreach (float e in new[] { -1f, 1f })
                        {
                            float z0 = bs + e * (hw - 1.8f), z1 = bs + e * hw;
                            verge.Face(new Vector3(x0, y0 + 0.1f, z0), new Vector3(x0, y0 + 0.1f, z1), new Vector3(x1, y1 + 0.1f, z1), new Vector3(x1, y1 + 0.1f, z0), Vector3.up);
                            verge.Face(new Vector3(x0, y0 - 1.5f, z1), new Vector3(x0, y0 + 0.1f, z1), new Vector3(x1, y1 + 0.1f, z1), new Vector3(x1, y1 - 1.5f, z1), new Vector3(0, 0, e));
                        }
                        if (Mathf.Repeat(x, 12) < Step)
                            marks.Box(new Vector3(side * (x + 3), (y0 + y1) / 2 + 0.03f, bs), new Vector3(6, 0.02f, 0.15f));
                    }
                }
            }
            approach.Object("Approach roads", Library.Surface("asphalt_02", 7), root);
            verge.Object("Approach kerbs", Library.Surface("concrete_wall_008", 3, new Color(0.75f, 0.75f, 0.73f)), root);
            marks.Object("Approach markings", Library.Plain(new Color(0.85f, 0.83f, 0.75f), 0.3f), root, shadows: false);

            Trees(map, root, rng, Y, FromBridge, lane, water);
            Cars(bridges, root, rng, Y);
        }

        /// <summary>The first id whose texture was downloaded.</summary>
        static string Pick(params string[] ids)
        {
            foreach (var id in ids)
                if (Library.Load<Texture2D>($"TP/Textures/{id}/{id}_diff_2k") != null) return id;
            return ids[ids.Length - 1];
        }

        delegate float Height(float x, float s);

        /// <summary>
        /// A line of street trees behind each riverside walk, a looser second row in the park strip,
        /// shrubs down the grassy banks and woods on the hills beyond the city.
        /// </summary>
        static void Trees(DuelMap map, Transform root, System.Random rng, Height Y, System.Func<float, float> fromBridge, float lane, float water)
        {
            float R() => (float)rng.NextDouble();
            var kinds = new List<Impostor.Field>();
            foreach (var id in new[] { "island_tree_02", "island_tree_03", "island_tree_01", "tree_small_02" })
            {
                var card = Impostor.Of(id);
                if (card != null) kinds.Add(new Impostor.Field(card));
            }
            var shrubCard = Impostor.Of("shrub_01") ?? Impostor.Of("shrub_02");
            var shrubs = shrubCard != null ? new Impostor.Field(shrubCard) : null;
            if (kinds.Count == 0)
            {
                Debug.LogWarning("[Estuary] no tree pictures; the banks stay bare");
                return;
            }
            Impostor.Field Any() => kinds[rng.Next(kinds.Count)];
            void Plant(Impostor.Field f, float x, float s, float scale)
            {
                f.Add(new Vector3(x, Y(x, s) - 0.2f, s), R() * 360, scale, rng.Next(2) == 0);
            }

            foreach (float side in new[] { -1f, 1f })
            {
                // Street trees: evenly spaced, alike in size, as a city plants them.
                var street = Any();
                for (float s = -1300; s < 2300; s += 13)
                    if (fromBridge(s) > 16) Plant(street, side * (TreeLine + R() * 0.6f), s + R() * 1.5f, 0.75f + R() * 0.15f);
                // The park strip between the road and the first blocks.
                for (float s = -1300; s < 2300; s += 9)
                {
                    if (fromBridge(s) < 30 || R() < 0.45f) continue;
                    Plant(Any(), side * (PavementOut + 4 + R() * 26), s + R() * 8, 0.7f + R() * 0.5f);
                }
                // Shrubs and the odd tree down the grassy bank, thinning toward the water.
                for (float s = -900; s < 1700; s += 6)
                {
                    if (fromBridge(s) < 14) continue;
                    float x = side * (238 + R() * 46);
                    if (Y(x, s) < water + 3) continue;
                    if (shrubs != null && R() < 0.55f) Plant(shrubs, x, s + R() * 6, 0.9f + R() * 0.9f);
                    else if (R() < 0.12f) Plant(Any(), x, s + R() * 6, 0.6f + R() * 0.4f);
                }
                // Woods on the hills beyond the city, in clumps.
                for (int c = 0; c < 70; c++)
                {
                    float cx = side * (1120 + R() * 900), cs = -1600 + R() * (lane + 3400);
                    int n = 8 + rng.Next(16);
                    for (int i = 0; i < n; i++)
                    {
                        float x = cx + (R() - 0.5f) * 140, s = cs + (R() - 0.5f) * 140;
                        if (Mathf.Abs(x) < 1100) continue;
                        Plant(Any(), x, s, 0.9f + R() * 0.7f);
                    }
                }
            }
            int planted = 0;
            for (int i = 0; i < kinds.Count; i++)
            {
                planted += kinds[i].Count;
                kinds[i].Build("Trees " + i, root);
            }
            shrubs?.Build("Shrubs", root);
            Debug.Log($"[Estuary] {planted} trees, {shrubs?.Count ?? 0} shrubs");
        }

        /// <summary>
        /// Cars under covers, parked along the riverside roads near the bridges and abandoned on the
        /// decks when the army closed them; mid-span stays clear.
        /// </summary>
        static void Cars(List<Structure> bridges, Transform root, System.Random rng, Height Y)
        {
            if (Library.Model("covered_car") == null) return;
            float R() => (float)rng.NextDouble();
            // The model's long axis, so a heading (degrees from +z) lines the car up whichever way it was modelled.
            var probe = Library.Place("covered_car", Vector3.zero, 0, 1, root);
            var pb = Library.BoundsOf(probe);
            float modelYaw = pb.size.x > pb.size.z ? 90 : 0;
            Object.Destroy(probe);
            void Car(Vector3 at, float heading, float ground)
            {
                var go = Library.Place("covered_car", at, heading - modelYaw, 1, root);
                Library.Ground(go, ground - 0.02f);
            }
            foreach (var b in bridges)
            {
                float bs = (float)b.S, deck = (float)b.DeckY, hw = (float)b.HalfWidth;
                // Two on the deck, one each way, stopped at an angle in the lanes.
                foreach (float dir in new[] { -1f, 1f })
                {
                    float x = dir * (70 + R() * 60);
                    float z = bs + (dir > 0 ? -1 : 1) * (2 + R() * 1.5f);
                    Car(new Vector3(x, deck, z), 90 + dir * (8 + R() * 20), deck + 0.04f);
                }
                // A few parked along the riverside road either side of the approach.
                foreach (float side in new[] { -1f, 1f })
                    foreach (float along in new[] { -1f, 1f })
                    {
                        int n = 1 + rng.Next(3);
                        for (int i = 0; i < n; i++)
                        {
                            float s = bs + along * (22 + i * 7 + R() * 2);
                            float x = side * (RoadOut - 1.3f);
                            Car(new Vector3(x, Y(x, s), s), along > 0 ? 0 : 180, Y(x, s) + 0.14f);
                        }
                    }
            }
        }
    }
}
