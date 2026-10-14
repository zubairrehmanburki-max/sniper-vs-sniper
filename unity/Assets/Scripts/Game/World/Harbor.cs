using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Harbor Rooftops: two blocks facing each other across a dock channel. A's roof is 12 m up, B's
    /// 16 m (the rules' ground function); each building's roof slab tops out at exactly that height
    /// and carries the rules' parapets, stair housings and AC units plus water tanks, vents and masts
    /// behind the shooters. Between them: the quays with container stacks, ship-to-shore cranes and
    /// moored ships, the rules' warehouse, crane tower, office and shed. Around them a dense harbor
    /// district of window-facade blocks, thinning to warehouses toward the hills on the horizon.
    /// </summary>
    public static class Harbor
    {
        const float FloorM = 3.4f;
        /// <summary>Quay walls (lane distance) either side of the channel; the rules' channel floor drops between 150 and 290.</summary>
        const float WallA = 158, WallB = 282, WaterY = -1.0f;
        /// <summary>
        /// Footprints of the two firing buildings: the rules' raised ground plus the terrain's ramp
        /// down to the next 4 m height sample (A's front ramp runs out at s = 61), so no terrain shows.
        /// </summary>
        const float BuildingHalfX = 48.5f, A0 = -15.5f, A1 = 61.5f, B0 = 376.5f, B1 = 457.5f;

        sealed class Kit
        {
            public readonly MeshBuilder[] Facades = new MeshBuilder[6];
            public readonly Material[] FacadeMats = new Material[6];
            public readonly MeshBuilder Concrete = new MeshBuilder(), RoofDeck = new MeshBuilder(), Roofs = new MeshBuilder(), Plant = new MeshBuilder();
            public readonly MeshBuilder Iron = new MeshBuilder(), Rust = new MeshBuilder(), Dark = new MeshBuilder(), Steel = new MeshBuilder();
            public readonly MeshBuilder[] Boxes = new MeshBuilder[5];
            public readonly MeshBuilder CraneA = new MeshBuilder(), CraneB = new MeshBuilder(), Beacons = new MeshBuilder(), Lamps = new MeshBuilder();
        }

        public static void Build(DuelMap map, Transform root)
        {
            var ground = map.Terrain ?? map.Ground;
            var rng = new System.Random(450);
            // Roof heights straight from the rules' ground, so the slab is where the shooter stands.
            float roofA = (float)ground(0, -20), roofB = (float)ground(0, -420);

            var k = new Kit();
            // Harbor-town facades: limestone, ochre, brick red, harbor blue-grey, pale render, dark glass.
            var tones = new[]
            {
                (new Color(0.62f, 0.58f, 0.5f), false), (new Color(0.6f, 0.46f, 0.32f), false), (new Color(0.46f, 0.26f, 0.2f), false),
                (new Color(0.42f, 0.46f, 0.5f), false), (new Color(0.68f, 0.66f, 0.62f), false), (new Color(0.17f, 0.2f, 0.23f), true),
            };
            for (int i = 0; i < 6; i++)
            {
                k.Facades[i] = new MeshBuilder();
                k.FacadeMats[i] = Skyline.Facade(rng, tones[i].Item1, 0.025f, tones[i].Item2);
            }
            for (int i = 0; i < 5; i++) k.Boxes[i] = new MeshBuilder();

            FiringBuilding(map, k, roofA, A0, A1, Side.A);
            FiringBuilding(map, k, roofB, B0, B1, Side.B);
            LaneBuildings(map, k);
            Quays(root, k, rng);
            Cranes(k);
            Containers(k, rng);
            District(k, rng);

            for (int i = 0; i < 6; i++)
                if (k.Facades[i].VertexCount > 0) k.Facades[i].Object("Facades " + i, k.FacadeMats[i], root);
            k.Concrete.Object("Concrete", Library.Surface("concrete_wall_008", 4, new Color(0.74f, 0.73f, 0.7f)), root);
            k.RoofDeck.Object("Roof decks", Library.Surface("concrete_wall_008", 3, new Color(0.6f, 0.6f, 0.58f)), root);
            k.Roofs.Object("Roofs", Library.Plain(new Color(0.33f, 0.33f, 0.32f), 0.2f), root);
            k.Plant.Object("Rooftop plant", Library.Surface("metal_plate", 2, new Color(0.7f, 0.71f, 0.7f), 0.5f), root);
            k.Iron.Object("Corrugated iron", Library.Surface("corrugated_iron_02", 3, new Color(0.62f, 0.66f, 0.7f)), root);
            k.Rust.Object("Rusty steel", Library.Surface("rusty_metal_02", 2), root);
            k.Dark.Object("Openings", Library.Plain(new Color(0.05f, 0.055f, 0.06f), 0.6f), root, shadows: false);
            k.Steel.Object("Steel", Library.Plain(new Color(0.25f, 0.26f, 0.27f), 0.4f, 0.7f), root);
            var boxColours = new[]
            {
                new Color(0.62f, 0.16f, 0.1f), new Color(0.12f, 0.3f, 0.55f), new Color(0.2f, 0.42f, 0.24f), new Color(0.8f, 0.45f, 0.12f), new Color(0.6f, 0.6f, 0.58f),
            };
            for (int i = 0; i < 5; i++)
                if (k.Boxes[i].VertexCount > 0) k.Boxes[i].Object("Containers " + i, Library.Surface("corrugated_iron_02", 2.5f, boxColours[i]), root);
            k.CraneA.Object("Cranes red", Library.Surface("rusty_painted_metal", 3, new Color(0.78f, 0.22f, 0.14f)), root);
            k.CraneB.Object("Cranes blue", Library.Surface("rusty_painted_metal", 3, new Color(0.3f, 0.45f, 0.7f)), root);
            k.Beacons.Object("Beacons", Library.Glow(new Color(0.3f, 0.02f, 0.02f), new Color(5f, 0.3f, 0.2f)), root, shadows: false);
            k.Lamps.Object("Quay lamps", Library.Glow(new Color(0.9f, 0.85f, 0.7f), new Color(1.6f, 1.3f, 0.9f)), root, shadows: false);

            Ships(root);
            Biomes.Horizon(root, (float)map.LaneLength / 2, new Biomes.Hills
            {
                MinHeight = 60,
                MaxHeight = 190,
                Rock = Library.Surface("sparse_grass", 60, new Color(0.62f, 0.68f, 0.62f)),
                Seed = 44,
            });
        }

        // ---- the two firing buildings --------------------------------------------------------------

        /// <summary>
        /// A firing building: facade floors, a concrete cornice, the roof slab with its top at exactly
        /// `roof`, the rules' furniture on it, and plant (tanks, vents, a mast, ducts) behind the zone.
        /// </summary>
        static void FiringBuilding(DuelMap map, Kit k, float roof, float s0, float s1, Side side)
        {
            float sc = (s0 + s1) / 2, depth = s1 - s0, width = 2 * BuildingHalfX;
            // Whole floors of windows, then a cornice band up to the slab.
            float facadeTop = Mathf.Floor((roof - 1.4f) / FloorM) * FloorM;
            k.Facades[side == Side.A ? 0 : 4].Block(new Vector3(0, -0.5f, sc), new Vector3(width, facadeTop + 0.5f, depth));
            k.Concrete.Box(new Vector3(0, (facadeTop + roof - 0.3f) / 2, sc), new Vector3(width + 0.3f, roof - 0.3f - facadeTop, depth + 0.3f));
            k.RoofDeck.Box(new Vector3(0, roof - 0.15f, sc), new Vector3(width, 0.3f, depth));
            // A plinth course at street level, and a loading dock along the front.
            k.Concrete.Box(new Vector3(0, 0.3f, sc), new Vector3(width + 0.2f, 1.2f, depth + 0.2f));
            float front = side == Side.A ? s1 : s0, ahead = side == Side.A ? 1 : -1;
            k.Concrete.Box(new Vector3(0, 0.35f, front + ahead * 2.5f), new Vector3(width, 1.3f, 5));

            // Edge parapets on the sides and the back; the front keeps only the rules' parapet.
            float back = side == Side.A ? s0 + 0.2f : s1 - 0.2f;
            k.Concrete.Box(new Vector3(0, roof + 0.5f, back), new Vector3(width, 1.0f, 0.4f));
            foreach (float e in new[] { -1f, 1f })
                k.Concrete.Box(new Vector3(e * (BuildingHalfX - 0.2f), roof + 0.5f, sc), new Vector3(0.4f, 1.0f, depth));

            // The rules' boxes on this roof.
            foreach (var box in map.Cover)
            {
                if (!box.Id.StartsWith(side == Side.A ? "A-" : "B-")) continue;
                var b = Biomes.ToUnity(box);
                if (box.Look == CoverLook.Metal) Biomes.AcUnit(k.Plant, k.Dark, b);
                else if (box.Id.Contains("stairs"))
                {
                    k.Concrete.Box(b.center, b.size);
                    k.Concrete.Box(new Vector3(b.center.x, b.max.y - 0.06f, b.center.z), new Vector3(b.size.x + 0.3f, 0.12f, b.size.z + 0.3f));
                    // Door on the face toward the building's back.
                    float dz = side == Side.A ? b.min.z - 0.02f : b.max.z + 0.02f;
                    k.Dark.Box(new Vector3(b.center.x, b.min.y + 1.05f, dz), new Vector3(1.0f, 2.1f, 0.04f));
                }
                else
                {
                    // Parapet segment with a coping stone.
                    k.Concrete.Box(b.center, b.size);
                    k.Concrete.Box(new Vector3(b.center.x, b.max.y - 0.04f, b.center.z), new Vector3(b.size.x, 0.08f, b.size.z + 0.1f));
                }
            }

            // Plant behind the zone: never between the spots.
            float r = side == Side.A ? 1 : -1;
            float rear = side == Side.A ? s0 : s1;
            WaterTank(k, new Vector3(-24, roof, rear + r * 7.5f));
            WaterTank(k, new Vector3(16, roof, rear + r * 9.5f));
            Mast(k, new Vector3(side == Side.A ? 45.5f : -45.5f, roof, rear + r * 26), 10);
            foreach (var (x, ds) in new[] { (-40f, 13f), (-8f, 4f), (32f, 11f), (2f, 15f) })
                k.Rust.Tube(new Vector3(x, roof, rear + r * ds), new Vector3(x, roof + 1.3f, rear + r * ds), 0.16f, 8);
            // A duct run along the side strip outside the zone.
            float dx = side == Side.A ? 45.6f : -45.6f;
            k.Plant.Box(new Vector3(dx, roof + 0.4f, rear + r * 32), new Vector3(0.6f, 0.6f, 48));
            for (float s = 10; s < 56; s += 6) k.Steel.Box(new Vector3(dx, roof + 0.05f, rear + r * s), new Vector3(0.9f, 0.1f, 0.1f));
            // Roof hatches.
            k.Plant.Box(new Vector3(-2, roof + 0.25f, rear + r * 5), new Vector3(1.2f, 0.5f, 1.2f));
            k.Plant.Box(new Vector3(30, roof + 0.25f, rear + r * 3), new Vector3(1.2f, 0.5f, 1.2f));
        }

        /// <summary>A rooftop water tank: a riveted steel drum on a four-leg stand, conical lid.</summary>
        static void WaterTank(Kit k, Vector3 foot)
        {
            const float R = 1.7f, Stand = 2.2f, H = 3.2f;
            foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
                k.Steel.Box(foot + new Vector3(x * R * 0.6f, Stand / 2, z * R * 0.6f), new Vector3(0.14f, Stand, 0.14f));
            k.Steel.Box(foot + new Vector3(0, Stand - 0.1f, 0), new Vector3(R * 1.5f, 0.2f, R * 1.5f));
            k.Rust.Cylinder(foot + Vector3.up * Stand, R, H, 16);
            k.Rust.Cone(foot + Vector3.up * (Stand + H), R + 0.08f, 0.7f, 16, false);
        }

        /// <summary>A guyed radio mast with a red light on top.</summary>
        static void Mast(Kit k, Vector3 foot, float height)
        {
            k.Steel.Tube(foot, foot + Vector3.up * height, 0.07f, 6);
            for (float y = 2.5f; y < height; y += 2.5f) k.Steel.Beam(foot + new Vector3(-0.6f, y, 0), foot + new Vector3(0.6f, y, 0), 0.04f, 0.04f);
            foreach (var d in new[] { new Vector3(2.5f, 0, 2.5f), new Vector3(-2.5f, 0, 2.5f), new Vector3(0, 0, -3.4f) })
                k.Steel.Beam(foot + Vector3.up * height * 0.75f, foot + d, 0.015f, 0.015f);
            k.Beacons.Box(foot + Vector3.up * (height + 0.15f), new Vector3(0.25f, 0.3f, 0.25f));
        }

        // ---- the rules' buildings between the roofs ------------------------------------------------

        /// <summary>The warehouse, crane tower, office and shed: each fills its rules box exactly.</summary>
        static void LaneBuildings(DuelMap map, Kit k)
        {
            foreach (var box in map.Cover)
            {
                var b = Biomes.ToUnity(box);
                switch (box.Id)
                {
                    case "warehouse":
                    case "shed":
                        // Concrete plinth, corrugated walls and roof, big doors and a band of clerestory glazing.
                        k.Concrete.Box(new Vector3(b.center.x, (b.min.y + 1.1f) / 2, b.center.z), new Vector3(b.size.x, 1.1f - b.min.y, b.size.z));
                        k.Iron.Box(new Vector3(b.center.x, (1.1f + b.max.y) / 2, b.center.z), new Vector3(b.size.x - 0.04f, b.max.y - 1.1f, b.size.z - 0.04f));
                        foreach (float e in new[] { -1f, 1f })
                        {
                            float z = b.center.z + e * (b.extents.z + 0.01f);
                            k.Dark.Box(new Vector3(b.center.x - b.size.x * 0.2f, 2.6f, z), new Vector3(Mathf.Min(6, b.size.x * 0.3f), 5.2f, 0.03f));
                            k.Dark.Box(new Vector3(b.center.x, b.max.y - 1.4f, z), new Vector3(b.size.x * 0.8f, 0.8f, 0.03f));
                        }
                        break;
                    case "office":
                        float top = Mathf.Floor((b.max.y - 1.0f) / FloorM) * FloorM;
                        k.Facades[5].Block(new Vector3(b.center.x, b.min.y, b.center.z), new Vector3(b.size.x, top - b.min.y, b.size.z));
                        k.Concrete.Box(new Vector3(b.center.x, (top + b.max.y) / 2, b.center.z), new Vector3(b.size.x, b.max.y - top, b.size.z));
                        Mast(k, new Vector3(b.center.x - 6, b.max.y, b.center.z + 4), 6);
                        break;
                    case "crane-tower":
                        CraneTower(k, b);
                        break;
                }
            }
        }

        /// <summary>
        /// The crane tower: a concrete shaft filling its box with the operator's glazing near the top,
        /// and a tower crane's lattice jib above it, out over the quay away from the lane.
        /// </summary>
        static void CraneTower(Kit k, Bounds b)
        {
            k.Concrete.Box(b.center, b.size);
            foreach (float e in new[] { -1f, 1f })
            {
                k.Dark.Box(new Vector3(b.center.x, b.max.y - 2.2f, b.center.z + e * (b.extents.z + 0.01f)), new Vector3(b.size.x * 0.8f, 1.6f, 0.03f));
                k.Dark.Box(new Vector3(b.center.x + e * (b.extents.x + 0.01f), b.max.y - 2.2f, b.center.z), new Vector3(0.03f, 1.6f, b.size.z * 0.8f));
            }
            float y0 = b.max.y + 0.5f, cx = b.center.x, cz = b.center.z;
            float jibEnd = cx + 62, counterEnd = cx - 20;
            // Slewing ring and tower head.
            k.CraneA.Box(new Vector3(cx, b.max.y + 0.25f, cz), new Vector3(4, 0.5f, 4));
            foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
                k.CraneA.Beam(new Vector3(cx + x * 1.4f, y0, cz + z * 1.4f), new Vector3(cx, y0 + 8, cz), 0.2f, 0.2f);
            // Triangular lattice jib: two bottom chords, one top chord, diagonals every 3 m.
            Vector3 L(float x) => new Vector3(x, y0, cz - 0.9f);
            Vector3 R(float x) => new Vector3(x, y0, cz + 0.9f);
            Vector3 T(float x) => new Vector3(x, y0 + 1.8f, cz);
            k.CraneA.Beam(L(counterEnd), L(jibEnd), 0.18f, 0.18f);
            k.CraneA.Beam(R(counterEnd), R(jibEnd), 0.18f, 0.18f);
            k.CraneA.Beam(T(cx), T(jibEnd), 0.16f, 0.16f);
            for (float x = cx; x < jibEnd - 1; x += 3)
            {
                k.CraneA.Beam(L(x), T(x + 1.5f), 0.08f, 0.08f);
                k.CraneA.Beam(R(x), T(x + 1.5f), 0.08f, 0.08f);
                k.CraneA.Beam(T(x + 1.5f), L(x + 3), 0.08f, 0.08f);
                k.CraneA.Beam(T(x + 1.5f), R(x + 3), 0.08f, 0.08f);
                k.CraneA.Beam(L(x), R(x), 0.08f, 0.08f);
            }
            // Counter-jib walkway and its concrete counterweights.
            for (float x = counterEnd; x < cx; x += 2) k.CraneA.Beam(L(x), R(x), 0.1f, 0.1f);
            k.Concrete.Box(new Vector3(counterEnd + 3, y0 - 0.9f, cz), new Vector3(4, 2.2f, 2.4f));
            // Pendants from the head to the jib and counter-jib.
            var head = new Vector3(cx, y0 + 8, cz);
            k.Steel.Beam(head, T(cx + 40), 0.05f, 0.05f);
            k.Steel.Beam(head, new Vector3(counterEnd + 1, y0, cz), 0.05f, 0.05f);
            k.Beacons.Box(head + Vector3.up * 0.2f, new Vector3(0.3f, 0.3f, 0.3f));
            k.Beacons.Box(T(jibEnd) + Vector3.up * 0.2f, new Vector3(0.3f, 0.3f, 0.3f));
            // Trolley and hook out over the quay.
            float hx = cx + 34;
            k.Steel.Box(new Vector3(hx, y0 - 0.3f, cz), new Vector3(2.2f, 0.6f, 2.2f));
            k.Steel.Beam(new Vector3(hx, y0 - 0.6f, cz), new Vector3(hx, 12, cz), 0.04f, 0.04f);
            k.CraneA.Box(new Vector3(hx, 11.5f, cz), new Vector3(0.8f, 1.0f, 0.6f));
        }

        // ---- quays, water, cranes, containers, ships -------------------------------------------------

        /// <summary>
        /// Concrete quays along both sides of the channel (covering the terrain's ramp into it) with
        /// timber fenders, bollards and lamp posts; the channel's water; a timber dolphin under the
        /// mid-channel wind flag.
        /// </summary>
        static void Quays(Transform root, Kit k, System.Random rng)
        {
            const float Reach = 2040;
            k.Concrete.Box(new Vector3(0, -2, (120 + WallA) / 2), new Vector3(2 * Reach, 4.1f, WallA - 120));
            k.Concrete.Box(new Vector3(0, -2, (WallB + 320) / 2), new Vector3(2 * Reach, 4.1f, 320 - WallB));
            var timber = new MeshBuilder();
            for (float x = -900; x <= 900; x += 15)
            {
                foreach (var (wall, dir) in new[] { (WallA, 1f), (WallB, -1f) })
                {
                    timber.Box(new Vector3(x, -1.5f, wall + dir * 0.18f), new Vector3(0.35f, 3.2f, 0.35f));
                    k.Steel.Cylinder(new Vector3(x + 7.5f, 0.05f, wall - dir * 0.8f), 0.22f, 0.55f, 10);
                    // Lamps along the quay, off the lane.
                    if (Mathf.Abs(x) > 60 && Mathf.RoundToInt(x / 15) % 3 == 0)
                    {
                        var foot = new Vector3(x, 0.05f, wall - dir * 6);
                        k.Steel.Tube(foot, foot + Vector3.up * 9, 0.09f, 6);
                        k.Steel.Beam(foot + Vector3.up * 9, foot + new Vector3(0, 9, dir * 1.4f), 0.08f, 0.08f);
                        k.Lamps.Box(foot + new Vector3(0, 8.85f, dir * 1.4f), new Vector3(0.35f, 0.12f, 0.6f));
                    }
                }
            }
            // Mooring dolphin: a cluster of piles round the flag in mid-channel.
            foreach (var (dx, dz) in new[] { (0f, 0f), (0.7f, 0.4f), (-0.6f, 0.5f), (0.1f, -0.7f) })
                timber.Tube(new Vector3(55 + dx, -4, 230 + dz), new Vector3(55 + dx, 1.0f, 230 + dz), 0.3f, 8);
            timber.Object("Fenders and piles", Library.Surface("weathered_planks", 1.5f, new Color(0.45f, 0.38f, 0.32f)), root);

            // The channel: the same wave-normal water as the open-water maps, between the quay walls.
            var mb = new MeshBuilder { UvScale = 1 };
            mb.Plane(new Vector3(0, WaterY, (WallA + WallB) / 2), 2 * Reach + 8, WallB - WallA + 12);
            var mat = new Material(Library.Template("Water"));
            mat.SetTextureScale("_BaseMap", Vector2.one / 24f);
            mat.SetTextureScale("_DetailAlbedoMap", Vector2.one / 9f);
            var water = mb.Object("Channel water", mat, root, shadows: false);
            water.GetComponent<MeshRenderer>().receiveShadows = true;
            water.AddComponent<Waves>().Material = mat;

            // Channel markers, off the lane.
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * (130 + i * 60);
                var buoy = Library.Place(i % 2 == 0 ? "lateral_sea_marker" : "ocean_buoy", new Vector3(x, WaterY, 200 + i * 18), (float)rng.NextDouble() * 360, 1.2f, root);
                Library.Ground(buoy, WaterY - 0.6f);
                buoy.AddComponent<Bob>().Phase = i * 1.7f;
            }
        }

        static readonly float[] CraneXA = { -112, 186 }, CraneXB = { 124, -214 };

        /// <summary>Two ship-to-shore container cranes on each quay, boom out over the water, clear of the lane.</summary>
        static void Cranes(Kit k)
        {
            foreach (float x in CraneXA) StsCrane(k, k.CraneA, x, WallA, 1);
            foreach (float x in CraneXB) StsCrane(k, k.CraneB, x, WallB, -1);
        }

        /// <summary>
        /// A ship-to-shore crane at x on the quay whose wall is at `wall`, water toward `dir` (±s): a
        /// portal on four legs, a twin-girder boom from the backreach out over the water, an A-frame with
        /// stays, the machinery house, trolley and spreader.
        /// </summary>
        static void StsCrane(Kit k, MeshBuilder paint, float x, float wall, float dir)
        {
            float sWater = wall - dir * 3, sLand = wall - dir * 33;
            const float Portal = 36, Boom = 40, Apex = 62;
            foreach (float e in new[] { -9f, 9f })
            {
                foreach (float s in new[] { sWater, sLand })
                {
                    paint.Box(new Vector3(x + e, Portal / 2, s), new Vector3(1.4f, Portal, 1.4f));
                    k.Steel.Box(new Vector3(x + e, 0.6f, s), new Vector3(2.0f, 1.2f, 3.4f));
                }
                paint.Beam(new Vector3(x + e, Portal, sWater), new Vector3(x + e, Portal, sLand), 1.2f, 1.6f);
                paint.Beam(new Vector3(x + e, 14, sWater), new Vector3(x + e, 14, sLand), 0.8f, 0.8f);
                // Bracing in the portal's side frame.
                paint.Beam(new Vector3(x + e, 14, sWater), new Vector3(x + e, Portal, sLand), 0.5f, 0.5f);
            }
            foreach (float s in new[] { sWater, sLand })
                paint.Beam(new Vector3(x - 9, Portal, s), new Vector3(x + 9, Portal, s), 1.4f, 1.8f);
            // Boom: two box girders and cross ties.
            float back = wall - dir * 58, tip = wall + dir * 62;
            foreach (float e in new[] { -2.4f, 2.4f })
                paint.Beam(new Vector3(x + e, Boom, back), new Vector3(x + e, Boom, tip), 1.1f, 2.4f);
            for (float t = 0; t <= 1.001f; t += 1f / 15)
            {
                float s = Mathf.Lerp(back, tip, t);
                paint.Beam(new Vector3(x - 2.4f, Boom + 1.0f, s), new Vector3(x + 2.4f, Boom + 1.0f, s), 0.3f, 0.3f);
            }
            // A-frame and stays.
            var apex = new Vector3(x, Apex, sWater);
            foreach (float e in new[] { -8f, 8f })
            {
                paint.Beam(new Vector3(x + e, Portal + 1, sWater), apex, 0.9f, 0.9f);
                paint.Beam(new Vector3(x + e, Portal + 1, sLand), apex, 0.7f, 0.7f);
            }
            foreach (float e in new[] { -2.4f, 2.4f })
            {
                k.Steel.Beam(apex, new Vector3(x + e, Boom + 1.2f, tip - dir * 2), 0.12f, 0.12f);
                k.Steel.Beam(apex, new Vector3(x + e, Boom + 1.2f, wall + dir * 25), 0.12f, 0.12f);
                k.Steel.Beam(apex, new Vector3(x + e, Boom + 1.2f, back + dir * 2), 0.12f, 0.12f);
            }
            k.Beacons.Box(apex + Vector3.up * 0.4f, new Vector3(0.5f, 0.5f, 0.5f));
            // Machinery house on the backreach, trolley with cab and spreader over the water.
            k.Plant.Box(new Vector3(x, Boom + 4.5f, wall - dir * 44), new Vector3(9, 6, 14));
            float ts = wall + dir * 22;
            paint.Box(new Vector3(x, Boom - 1.6f, ts), new Vector3(6, 1.2f, 5));
            k.Plant.Box(new Vector3(x + 2.8f, Boom - 3.6f, ts), new Vector3(2.4f, 2.6f, 2.6f));
            k.Dark.Box(new Vector3(x + 2.8f, Boom - 3.4f, ts + dir * 1.32f), new Vector3(2.0f, 1.4f, 0.03f));
            foreach (float e in new[] { -1.5f, 1.5f })
                k.Steel.Beam(new Vector3(x + e, Boom - 2.2f, ts), new Vector3(x + e, 19, ts), 0.05f, 0.05f);
            paint.Box(new Vector3(x, 18.7f, ts), new Vector3(12.4f, 0.6f, 2.6f));
        }

        /// <summary>Container stacks on both quay aprons in blocks between the cranes, 1–4 high, five liveries.</summary>
        static void Containers(Kit k, System.Random rng)
        {
            const float L = 12.2f, W = 2.44f, H = 2.6f;
            foreach (var (s0, s1, cranes) in new[] { (124f, 150f, CraneXA), (290f, 316f, CraneXB) })
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    for (float x0 = 66; x0 < 360; x0 += L + 0.4f)
                    {
                        float x = side * (x0 + L / 2);
                        bool clear = true;
                        foreach (float c in cranes) clear &= Mathf.Abs(x - c) > L / 2 + 12;
                        // Lanes for the straddle carriers every few slots.
                        if (!clear || Mathf.RoundToInt(x0 / (L + 0.4f)) % 5 == 4) continue;
                        for (float s = s0; s + W <= s1; s += W + 0.3f)
                        {
                            if (rng.NextDouble() < 0.3) continue;
                            int tiers = 1 + rng.Next(4);
                            for (int t = 0; t < tiers; t++)
                            {
                                var c = new Vector3(x + ((float)rng.NextDouble() - 0.5f) * 0.3f, 0.05f + H * (t + 0.5f), s + W / 2);
                                k.Boxes[rng.Next(5)].Box(c, new Vector3(L, H - 0.02f, W));
                            }
                        }
                    }
                }
            }
        }

        /// <summary>A sailing ship moored alongside each quay and a second on A's, hulls a metre off the wall.</summary>
        static void Ships(Transform root)
        {
            Moor(root, "dutch_ship_large_01", -300, WallA, 1, 2.2f);
            Moor(root, "dutch_ship_medium", 330, WallA, 1, 1.4f);
            Moor(root, "dutch_ship_medium", 280, WallB, -1, 1.4f);
        }

        static void Moor(Transform root, string id, float x, float wall, float dir, float draft)
        {
            var ship = Library.Place(id, new Vector3(x, WaterY, wall + dir * 15), 0, 1, root);
            var b = Library.BoundsOf(ship);
            if (b.size.y < 0.01f) return;
            // Lie alongside: the long axis along the quay (x).
            if (b.size.z > b.size.x)
            {
                ship.transform.rotation = Quaternion.Euler(0, 90, 0);
                b = Library.BoundsOf(ship);
            }
            float edge = dir > 0 ? b.min.z : b.max.z;
            ship.transform.position += new Vector3(x - b.center.x, 0, wall + dir * 1.2f - edge);
            Library.Ground(ship, WaterY - draft);
        }

        // ---- the district ----------------------------------------------------------------------------

        /// <summary>
        /// Streets of blocks on both sides of the lane and behind each firing building: low waterfront
        /// rows, taller behind, the odd tower; then warehouses and sheds out toward the terrain's edge.
        /// Nothing in the lane between the firing buildings.
        /// </summary>
        static void District(Kit k, System.Random rng)
        {
            // A side: from far behind A up to the apron. B side: from the apron to far behind B.
            Rows(k, rng, -430, 114, WallA, a => a < A0 - 9);
            Rows(k, rng, 326, 1010, WallB, a => a > B1 + 9);

            // Out to the edge: big low sheds and warehouses on a loose grid.
            for (float x = -1800; x <= 1800; x += 110)
            {
                for (float s = -1500; s <= 1950; s += 110)
                {
                    if (Mathf.Abs(x) < 780 && s > -450 && s < 1030) continue;
                    if (s > 100 && s < 340) continue;
                    if (rng.NextDouble() < 0.45) continue;
                    float w = 30 + (float)rng.NextDouble() * 55, d = 25 + (float)rng.NextDouble() * 45;
                    float px = x + ((float)rng.NextDouble() - 0.5f) * 30, ps = s + ((float)rng.NextDouble() - 0.5f) * 30;
                    if (ps + d / 2 > 140 && ps - d / 2 < 300) continue;
                    int floors = 2 + rng.Next(rng.NextDouble() < 0.2 ? 8 : 3);
                    if (rng.NextDouble() < 0.5) Shed(k, px, ps, w, d, floors * FloorM);
                    else Block(k, rng, px, ps, w, d, floors);
                }
            }
        }

        /// <summary>
        /// Rows of blocks between sFrom and sTo, separated by streets, terraced along x with cross
        /// streets. Rows nearest the water are lower. `central(s)` says whether a row may also fill the
        /// middle (|x| &lt; 56), which is only true behind the firing buildings.
        /// </summary>
        static void Rows(Kit k, System.Random rng, float sFrom, float sTo, float waterS, System.Func<float, bool> central)
        {
            for (float s0 = sFrom; s0 < sTo;)
            {
                float d = 18 + (float)rng.NextDouble() * 14;
                if (s0 + d > sTo) d = sTo - s0;
                if (d < 12) break;
                float sc = s0 + d / 2;
                bool waterfront = Mathf.Abs(sc - waterS) < 60;
                bool middle = central(s0) && central(s0 + d);
                float xStart = middle ? -760 : 56;
                foreach (float side in middle ? new[] { 1f } : new[] { -1f, 1f })
                {
                    for (float x0 = xStart; x0 < 760;)
                    {
                        float w = 14 + (float)rng.NextDouble() * 16;
                        float xc = side * (x0 + w / 2);
                        float far = Mathf.Abs(xc);
                        if (!(far > 450 && rng.NextDouble() < 0.3))
                        {
                            int floors = waterfront ? 3 + rng.Next(3) : 4 + rng.Next(far > 400 ? 4 : 7);
                            if (!waterfront && far > 140 && rng.NextDouble() < 0.06) floors = 12 + rng.Next(10);
                            Block(k, rng, xc, sc, w, d, floors);
                        }
                        x0 += w + (rng.NextDouble() < 0.22 ? 10 : 0.02f);
                    }
                }
                s0 += d + 10 + (float)rng.NextDouble() * 4;
            }
        }

        /// <summary>A city block: window facade, cornice, roof with plant and the odd water tank.</summary>
        static void Block(Kit k, System.Random rng, float x, float s, float w, float d, int floors)
        {
            float h = floors * FloorM;
            k.Facades[rng.Next(k.Facades.Length)].Block(new Vector3(x, -0.5f, s), new Vector3(w, h + 0.5f, d));
            k.Roofs.Box(new Vector3(x, h + 0.45f, s), new Vector3(w + 0.3f, 0.9f, d + 0.3f));
            int plant = rng.Next(3);
            for (int i = 0; i < plant; i++)
            {
                float px = x + ((float)rng.NextDouble() - 0.5f) * w * 0.6f, pz = s + ((float)rng.NextDouble() - 0.5f) * d * 0.6f;
                k.Plant.Box(new Vector3(px, h + 1.6f, pz), new Vector3(2 + (float)rng.NextDouble() * 3, 1.6f, 2 + (float)rng.NextDouble() * 3));
            }
            if (rng.NextDouble() < 0.12) WaterTank(k, new Vector3(x + w * 0.2f, h + 0.9f, s - d * 0.2f));
        }

        /// <summary>A port shed: corrugated walls on a concrete plinth, a shallow roof.</summary>
        static void Shed(Kit k, float x, float s, float w, float d, float h)
        {
            k.Concrete.Box(new Vector3(x, 0.25f, s), new Vector3(w, 1.5f, d));
            k.Iron.Box(new Vector3(x, 1 + (h - 1) / 2, s), new Vector3(w - 0.1f, h - 1, d - 0.1f));
            k.Roofs.Box(new Vector3(x, h + 0.2f, s), new Vector3(w + 0.6f, 0.4f, d + 0.6f));
        }
    }
}
