using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The platforms of the variable-distance posts on the water maps: dredger barges, grain silos,
    /// stilt hides, pump houses, radio masts, wellhead platforms and standby vessels. Each is built in
    /// its own frame with local +z toward the enemy. Its walkable top is exactly `TopY` over ±HalfX
    /// by ±HalfS, the platform the rules know; nothing stands proud of it in front of the shooter,
    /// and whatever tall sits on it is returned as a block the player walks round.
    /// </summary>
    public static class PostPlatforms
    {
        public static List<Bounds> Build(DuelMap map, Transform parent)
        {
            var blocks = new List<Bounds>();
            if (map.Look.Platforms == null) return blocks;
            var ground = map.Terrain ?? map.Ground;
            float water = (float)(map.Water ?? 0);
            foreach (var p in map.Look.Platforms)
            {
                var root = new GameObject($"Post {p.Style} s={p.S:0}").transform;
                root.SetParent(parent, false);
                root.localPosition = new Vector3((float)p.X, 0, (float)p.S);
                root.localRotation = Quaternion.Euler(0, p.Facing > 0 ? 0 : 180, 0);
                float top = (float)p.TopY, hx = (float)p.HalfX, hz = (float)p.HalfS;
                float bed = (float)ground(p.X, -p.S);
                var local = new List<Bounds>();
                switch (p.Style)
                {
                    case PlatformStyle.Barge: Barge(root, top, hx, hz, water, local); break;
                    case PlatformStyle.Silo: Silo(root, top, hz, bed); break;
                    case PlatformStyle.Hide: Hide(root, top, hx, hz, bed); break;
                    case PlatformStyle.PumpHouse: PumpHouse(root, top, hx, hz, bed, water); break;
                    case PlatformStyle.Mast: Mast(root, top, hx, hz, bed); break;
                    case PlatformStyle.Jacket: Jacket(root, top, hx, hz, water, local); break;
                    case PlatformStyle.Ship:
                        // The vessel is built from its own origin; its bridge roof is the platform.
                        var ship = new GameObject("Hull frame").transform;
                        ship.SetParent(root, false);
                        ship.localPosition = new Vector3(0, water, -Offshore.BridgeZ);
                        Offshore.SupplyVessel(Vector3.zero, 0, ship);
                        local.Add(new Bounds(new Vector3(0, top + 4.3f, -0.5f), new Vector3(1.2f, 8.6f, 1.2f)));
                        break;
                }
                foreach (var b in local) blocks.Add(new Bounds(root.TransformPoint(b.center), b.size));
            }
            return blocks;
        }

        static Material Steel => Library.Surface("rusty_painted_metal", 2, new Color(0.62f, 0.64f, 0.6f));
        static Material Timber => Library.Surface("weathered_planks", 2.5f, new Color(0.62f, 0.56f, 0.48f));
        static Material Concrete => Library.Weathered("concrete_wall_008", 4, new Color(0.7f, 0.69f, 0.65f), 0.7f);
        static Material Dark => Library.Plain(new Color(0.04f, 0.045f, 0.05f), 0.4f);

        /// <summary>A guard rail of tubes along the back and both sides of a deck (the front stays open).</summary>
        static void Rails(MeshBuilder mb, float top, float hx, float hz, float height = 1.05f)
        {
            var c = new[] { new Vector3(-hx, 0, hz), new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz) };
            for (int i = 0; i < 3; i++)
            {
                var a = c[i] + Vector3.up * top;
                var b = c[i + 1] + Vector3.up * top;
                mb.Tube(a + Vector3.up * height, b + Vector3.up * height, 0.03f, 6);
                mb.Tube(a + Vector3.up * height * 0.5f, b + Vector3.up * height * 0.5f, 0.022f, 6);
                float len = Vector3.Distance(a, b);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 1.8f));
                for (int k = 0; k <= n; k++)
                {
                    var foot = Vector3.Lerp(a, b, (float)k / n);
                    mb.Tube(foot, foot + Vector3.up * height, 0.025f, 6);
                }
            }
        }

        // ---- Twin Bridges ---------------------------------------------------------------------------

        /// <summary>
        /// A moored dredger: a flat steel hull with a black boot-top, a chequer-plate deck, bollards
        /// and fairleads on the edges, the wheelhouse and the dredge crane at the far starboard end
        /// (local +x) and a coil of discharge pipe trailing off the stern.
        /// </summary>
        static void Barge(Transform root, float top, float hx, float hz, float water, List<Bounds> blocks)
        {
            var hull = new MeshBuilder();
            var boot = new MeshBuilder();
            var deck = new MeshBuilder();
            var house = new MeshBuilder();
            var glass = new MeshBuilder();
            var steel = new MeshBuilder();
            var yellow = new MeshBuilder();
            float L = hx + 0.4f, B = hz + 0.4f, keel = water - 2.2f;
            // Raked ends: the hull narrows a little to a swim end at bow and stern.
            boot.Box(new Vector3(0, (keel + water + 0.4f) / 2, 0), new Vector3(2 * L - 3, water + 0.4f - keel, 2 * B));
            foreach (float e in new[] { -1f, 1f })
                boot.Face(new Vector3(e * (L - 1.5f), keel, -B), new Vector3(e * (L - 1.5f), keel, B), new Vector3(e * L, water + 0.4f, B), new Vector3(e * L, water + 0.4f, -B), new Vector3(e, -0.6f, 0));
            hull.Box(new Vector3(0, (water + 0.4f + top - 0.12f) / 2, 0), new Vector3(2 * L, top - 0.12f - water - 0.4f, 2 * B));
            deck.Box(new Vector3(0, top - 0.06f, 0), new Vector3(2 * hx + 0.6f, 0.12f, 2 * hz + 0.6f));
            // Fender rubbing strake round the hull, bollards at the corners (off the walkable deck).
            foreach (float e in new[] { -1f, 1f })
            {
                steel.Box(new Vector3(0, top - 0.7f, e * (B + 0.12f)), new Vector3(2 * L, 0.3f, 0.24f));
                steel.Box(new Vector3(e * (L + 0.12f), top - 0.7f, 0), new Vector3(0.24f, 0.3f, 2 * B));
                foreach (float f in new[] { -1f, 1f })
                    steel.Cylinder(new Vector3(e * (L - 0.6f), top, f * (B - 0.5f)), 0.18f, 0.45f, 10);
            }
            // Wheelhouse and crane at the far starboard end, behind the walk area.
            float hxEnd = hx - 2.6f;
            house.Box(new Vector3(hxEnd, top + 1.6f, -hz + 2.4f), new Vector3(4.2f, 3.2f, 4.0f));
            house.Box(new Vector3(hxEnd, top + 3.3f, -hz + 2.4f), new Vector3(4.6f, 0.18f, 4.4f));
            glass.Box(new Vector3(hxEnd, top + 2.3f, -hz + 4.42f), new Vector3(3.6f, 0.9f, 0.06f));
            glass.Box(new Vector3(hxEnd - 2.12f, top + 2.3f, -hz + 2.4f), new Vector3(0.06f, 0.9f, 3.2f));
            blocks.Add(new Bounds(new Vector3(hxEnd, top + 1.6f, -hz + 2.4f), new Vector3(4.2f, 3.2f, 4.0f)));
            // Dredge crane: a slewing pedestal and a lattice boom raised out over the stern quarter.
            var foot = new Vector3(hxEnd, top, hz - 2.2f);
            yellow.Cylinder(foot, 0.9f, 1.8f, 12);
            yellow.Box(foot + new Vector3(0, 2.4f, 0), new Vector3(2.4f, 1.4f, 2.8f));
            blocks.Add(new Bounds(foot + Vector3.up * 1.6f, new Vector3(2.8f, 3.2f, 3.2f)));
            Steelwork.Lattice(yellow, foot + new Vector3(0, 2.6f, -0.6f), foot + new Vector3(4, 13, -12), 0.9f, 0.5f, 0.07f, 0.035f, 7, Vector3.up);
            steel.Tube(foot + new Vector3(4, 13, -12), foot + new Vector3(4, 4, -12), 0.03f, 4);
            yellow.Box(foot + new Vector3(4, 3.6f, -12), new Vector3(1.4f, 0.9f, 1.2f));
            // Floating discharge pipe trailing astern on pontoons.
            for (int i = 0; i < 9; i++)
            {
                float z = -hz - 2 - i * 5.5f;
                float x = -hx * 0.4f + Mathf.Sin(i * 0.6f) * 3;
                steel.Tube(new Vector3(x, water + 0.25f, z), new Vector3(x + Mathf.Sin(i * 0.6f + 0.6f) * 0.8f, water + 0.25f, z - 5.2f), 0.35f, 10);
                yellow.Cylinder(new Vector3(x, water - 0.3f, z - 2.6f), 0.7f, 0.7f, 10);
            }
            boot.Object("Barge boot top", Library.Plain(new Color(0.06f, 0.06f, 0.06f), 0.3f), root);
            hull.Object("Barge hull", Library.Surface("rusty_painted_metal", 3, new Color(0.62f, 0.2f, 0.12f)), root);
            deck.Object("Barge deck", Library.Surface("metal_plate", 2, new Color(0.5f, 0.5f, 0.47f)), root);
            house.Object("Wheelhouse", Library.Plain(new Color(0.88f, 0.88f, 0.84f), 0.35f), root);
            glass.Object("Wheelhouse glass", Library.Plain(new Color(0.05f, 0.08f, 0.1f), 0.92f, 0.3f), root, shadows: false);
            steel.Object("Barge steel", Steel, root);
            yellow.Object("Dredge crane", Library.Surface("metal_plate", 3, new Color(0.9f, 0.7f, 0.18f), 0.6f), root);
        }

        /// <summary>
        /// Three concrete grain bins in a row running back from the river, the front one's flat roof
        /// the post, a gallery along the tops of the other two, a caged ladder and a conveyor truss
        /// slanting down to the loading shed behind.
        /// </summary>
        static void Silo(Transform root, float top, float hz, float bed)
        {
            var cast = new MeshBuilder();
            var roof = new MeshBuilder();
            var steel = new MeshBuilder();
            var clad = new MeshBuilder();
            const float R = 5.4f;
            float baseY = bed - 1.5f;
            for (int i = 0; i < 3; i++)
            {
                float z = -i * 2 * R * 0.98f;
                cast.Cylinder(new Vector3(0, baseY, z), R, top - 0.3f - baseY, 28);
                roof.Cylinder(new Vector3(0, top - 0.3f, z), R + 0.15f, 0.3f, 28);
                // Shallow vertical pilasters every 45 degrees catch the light down the shaft.
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4 + 0.2f;
                    var at = new Vector3(Mathf.Cos(a) * (R + 0.05f), baseY, z + Mathf.Sin(a) * (R + 0.05f));
                    cast.Box(at + Vector3.up * (top - 0.3f - baseY) / 2, new Vector3(0.35f, top - 0.3f - baseY, 0.35f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0));
                }
                // Discharge hopper at the foot.
                steel.Frustum(new Vector3(0, baseY + 1.5f, z), new Vector2(1.2f, 1.2f), new Vector3(0, baseY + 4.5f, z), new Vector2(3.2f, 3.2f));
            }
            // A low parapet round the back half of the front roof, open toward the enemy.
            for (int k = 0; k < 14; k++)
            {
                float a0 = Mathf.PI + k * Mathf.PI / 14, a1 = a0 + Mathf.PI / 14;
                var p0 = new Vector3(Mathf.Cos(a0) * (R - 0.1f), top, Mathf.Sin(a0) * (R - 0.1f));
                var p1 = new Vector3(Mathf.Cos(a1) * (R - 0.1f), top, Mathf.Sin(a1) * (R - 0.1f));
                steel.Tube(p0 + Vector3.up * 1.05f, p1 + Vector3.up * 1.05f, 0.03f, 6);
                steel.Tube(p0, p0 + Vector3.up * 1.05f, 0.025f, 6);
            }
            // The gallery: a clad box along the tops of the rear bins, a door onto the front roof.
            float gz0 = -R - 0.3f, gz1 = -4 * R * 0.98f - R * 0.6f;
            clad.Box(new Vector3(0, top + 1.6f, (gz0 + gz1) / 2), new Vector3(4.2f, 3.2f, gz0 - gz1));
            clad.Box(new Vector3(0, top + 3.3f, (gz0 + gz1) / 2), new Vector3(4.6f, 0.2f, gz0 - gz1 + 0.4f));
            steel.Box(new Vector3(0, top + 1.1f, gz0 + 0.02f), new Vector3(1.0f, 2.1f, 0.06f));
            // Conveyor truss from the gallery down to a shed behind the row.
            var from = new Vector3(0, top + 1.2f, gz1);
            var to = new Vector3(0, bed + 6, gz1 - 46);
            Steelwork.Lattice(steel, from, to, 2.2f, 2.2f, 0.08f, 0.04f, 12, Vector3.up);
            clad.Box(new Vector3(0, bed + 3.5f, gz1 - 50), new Vector3(14, 7.5f, 12));
            clad.Box(new Vector3(0, bed + 7.4f, gz1 - 50), new Vector3(14.6f, 0.3f, 12.6f));
            // Caged ladder up the side of the front bin.
            for (float y = baseY + 2; y < top; y += 0.35f)
                steel.Box(new Vector3(R + 0.35f, y, 0), new Vector3(0.05f, 0.04f, 0.5f));
            foreach (float e in new[] { -0.25f, 0.25f })
                steel.Box(new Vector3(R + 0.35f, (baseY + 2 + top) / 2, e), new Vector3(0.05f, top - baseY - 2, 0.05f));
            for (float y = baseY + 4; y < top; y += 1.6f)
                steel.Tube(new Vector3(R + 1.0f, y, -0.5f), new Vector3(R + 1.0f, y, 0.5f), 0.025f, 6);
            cast.Object("Silo bins", Library.Weathered("concrete_wall_008", 5, new Color(0.78f, 0.76f, 0.71f), 0.8f), root);
            roof.Object("Silo roofs", Concrete, root);
            steel.Object("Silo steel", Steel, root);
            clad.Object("Silo gallery", Library.Surface("corrugated_iron_02", 2, new Color(0.74f, 0.72f, 0.66f)), root);
        }

        // ---- Watchtowers -----------------------------------------------------------------------------

        /// <summary>A timber shooting hide on four braced stilts in the shallows: a plank deck, rails on three sides, a ladder at the back.</summary>
        static void Hide(Transform root, float top, float hx, float hz, float bed)
        {
            var wood = new MeshBuilder();
            float foot = bed - 0.6f;
            foreach (float ex in new[] { -1f, 1f })
                foreach (float ez in new[] { -1f, 1f })
                    wood.Box(new Vector3(ex * (hx - 0.15f), (foot + top - 0.2f) / 2, ez * (hz - 0.15f)), new Vector3(0.22f, top - 0.2f - foot, 0.22f));
            // Cross bracing on each face, and a ring of bearers under the deck.
            foreach (float e in new[] { -1f, 1f })
            {
                wood.Beam(new Vector3(-hx + 0.15f, foot + 0.8f, e * (hz - 0.15f)), new Vector3(hx - 0.15f, top - 0.6f, e * (hz - 0.15f)), 0.12f, 0.08f);
                wood.Beam(new Vector3(e * (hx - 0.15f), foot + 0.8f, -hz + 0.15f), new Vector3(e * (hx - 0.15f), top - 0.6f, hz - 0.15f), 0.08f, 0.12f);
                wood.Box(new Vector3(0, top - 0.3f, e * (hz - 0.15f)), new Vector3(2 * hx, 0.2f, 0.14f));
                wood.Box(new Vector3(e * (hx - 0.15f), top - 0.3f, 0), new Vector3(0.14f, 0.2f, 2 * hz));
            }
            // Deck planks with gaps.
            for (float x = -hx + 0.11f; x < hx; x += 0.24f)
                wood.Box(new Vector3(x, top - 0.035f, 0), new Vector3(0.21f, 0.07f, 2 * hz));
            // Rails: back and sides, with a camouflage net hung on the back one.
            var rail = new MeshBuilder();
            Rails(rail, top, hx - 0.1f, hz - 0.1f, 0.95f);
            var net = new MeshBuilder();
            net.Box(new Vector3(0, top + 0.5f, -hz + 0.05f), new Vector3(2 * hx - 0.3f, 0.9f, 0.03f));
            // Ladder down the back into the water.
            foreach (float e in new[] { -0.25f, 0.25f })
                wood.Beam(new Vector3(e, bed, -hz - 1.1f), new Vector3(e, top, -hz - 0.05f), 0.07f, 0.07f);
            for (float t = 0.08f; t < 1; t += 0.08f)
                wood.Box(Vector3.Lerp(new Vector3(0, bed, -hz - 1.1f), new Vector3(0, top, -hz - 0.05f), t), new Vector3(0.55f, 0.05f, 0.08f));
            wood.Object("Hide timber", Timber, root);
            rail.Object("Hide rails", Timber, root);
            net.Object("Hide net", Library.Plain(new Color(0.24f, 0.27f, 0.16f), 0.1f), root);
        }

        /// <summary>A derelict pump house standing in the marsh: weathered brick-red walls, dark broken windows, a flat slab roof with a low back parapet, an intake pipe into the water and a steel ladder up the back.</summary>
        static void PumpHouse(Transform root, float top, float hx, float hz, float bed, float water)
        {
            var walls = new MeshBuilder();
            var slab = new MeshBuilder();
            var dark = new MeshBuilder();
            var steel = new MeshBuilder();
            float foot = bed - 0.5f;
            walls.Box(new Vector3(0, (foot + top - 0.35f) / 2, 0), new Vector3(2 * hx, top - 0.35f - foot, 2 * hz));
            slab.Box(new Vector3(0, top - 0.175f, 0), new Vector3(2 * hx + 0.4f, 0.35f, 2 * hz + 0.4f));
            // Low parapet on the back edge only.
            slab.Box(new Vector3(0, top + 0.25f, -hz - 0.05f), new Vector3(2 * hx + 0.4f, 0.5f, 0.3f));
            // Water stain band, windows (some boarded, all dark) and a door on the back.
            walls.Box(new Vector3(0, water + 0.4f, 0), new Vector3(2 * hx + 0.04f, 1.2f, 2 * hz + 0.04f));
            foreach (float e in new[] { -1f, 1f })
            {
                for (float x = -hx + 1.6f; x < hx - 1; x += 2.6f)
                    dark.Box(new Vector3(x, water + 3.6f, e * (hz + 0.02f)), new Vector3(1.2f, 1.8f, 0.05f));
                dark.Box(new Vector3(e * (hx + 0.02f), water + 3.6f, 0), new Vector3(0.05f, 1.8f, 1.4f));
            }
            // Intake: a big rusted pipe from the side wall down into the water, on a concrete saddle.
            steel.Tube(new Vector3(hx, water + 1.2f, 1), new Vector3(hx + 6, water - 0.4f, 2.2f), 0.45f, 12);
            slab.Box(new Vector3(hx + 3, water - 0.2f, 1.6f), new Vector3(1.2f, 1.4f, 1.6f));
            // Ladder up the back wall to the roof.
            foreach (float e in new[] { -0.25f, 0.25f })
                steel.Box(new Vector3(e + hx * 0.5f, (water + top + 0.9f) / 2, -hz - 0.25f), new Vector3(0.05f, top + 0.9f - water, 0.05f));
            for (float y = water + 0.3f; y < top; y += 0.3f)
                steel.Box(new Vector3(hx * 0.5f, y, -hz - 0.25f), new Vector3(0.5f, 0.035f, 0.035f));
            walls.Object("Pump house walls", Library.Weathered("concrete_wall_008", 3, new Color(0.62f, 0.44f, 0.36f), 0.9f), root);
            slab.Object("Pump house roof", Concrete, root);
            dark.Object("Pump house windows", Dark, root, shadows: false);
            steel.Object("Pump house steel", Library.Surface("rusty_metal_02", 2), root);
        }

        /// <summary>A lattice radio mast standing in the marsh behind the tower, with a railed working platform on its front face and antennas and a red light above.</summary>
        static void Mast(Transform root, float top, float hx, float hz, float bed)
        {
            var steel = new MeshBuilder();
            var grate = new MeshBuilder();
            var red = new MeshBuilder();
            float w = 2.4f, mz = -hz - w / 2;
            var foot = new Vector3(0, bed - 0.5f, mz);
            var crown = new Vector3(0, top + 18, mz);
            steel.Box(foot + Vector3.up * 0.5f, new Vector3(4, 1.4f, 4));
            Steelwork.Lattice(steel, foot + Vector3.up * 1.2f, crown, w + 0.6f, w * 0.6f, 0.09f, 0.04f, 22, Vector3.forward);
            // The platform: grating on two brackets, rails on three sides.
            grate.Box(new Vector3(0, top - 0.05f, 0), new Vector3(2 * hx, 0.1f, 2 * hz));
            foreach (float e in new[] { -1f, 1f })
                steel.Beam(new Vector3(e * (hx - 0.1f), top - 2.2f, mz + w * 0.4f), new Vector3(e * (hx - 0.1f), top - 0.1f, hz - 0.1f), 0.08f, 0.08f);
            Rails(steel, top, hx, hz);
            // Antennas and dishes up top, the aviation light on the crown.
            steel.Tube(crown, crown + Vector3.up * 6, 0.06f, 8);
            steel.Cylinder(crown + new Vector3(0.9f, -3, 0.9f), 0.5f, 0.15f, 14);
            steel.Box(crown + new Vector3(-0.8f, -5, 0.4f), new Vector3(0.12f, 2.8f, 0.3f));
            red.Box(crown + Vector3.up * 6.1f, new Vector3(0.3f, 0.3f, 0.3f));
            steel.Object("Mast", Library.Plain(new Color(0.72f, 0.73f, 0.72f), 0.4f, 0.5f), root);
            grate.Object("Mast platform", Library.Surface("metal_plate", 1.5f, new Color(0.45f, 0.46f, 0.44f)), root);
            red.Object("Mast light", Library.Glow(new Color(0.4f, 0.03f, 0.02f), new Color(4f, 0.3f, 0.2f)), root, shadows: false);
        }

        // ---- Oil Rigs --------------------------------------------------------------------------------

        /// <summary>
        /// An unmanned wellhead platform: four yellow legs battered out to the sea bed with K bracing
        /// and growth through the splash zone, a cellar deck of Christmas trees under the top deck, a
        /// boat landing on the front face, a nav-aid mast and a solar rack on the back corners.
        /// </summary>
        static void Jacket(Transform root, float top, float hx, float hz, float water, List<Bounds> blocks)
        {
            var paint = new MeshBuilder();
            var growth = new MeshBuilder();
            var deck = new MeshBuilder();
            var steel = new MeshBuilder();
            var red = new MeshBuilder();
            float L = hx - 1.2f, low = water - 14;
            Vector3 Leg(int sx, int sz, float y) => new Vector3(sx * (L + (top - 2 - y) * 0.08f), y, sz * (L + (top - 2 - y) * 0.08f));
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    paint.Tube(Leg(sx, sz, water + 1.5f), Leg(sx, sz, top - 0.4f), 0.55f, 12);
                    growth.Tube(Leg(sx, sz, low), Leg(sx, sz, water + 1.5f), 0.62f, 12);
                }
            // Horizontal frames and K braces between the legs.
            foreach (float y in new[] { water - 9, water + 2.5f, top - 6 })
                foreach (var (a, b) in new[] { ((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1)) })
                {
                    var mb = y < water + 1.5f ? growth : paint;
                    mb.Tube(Leg(a.Item1, a.Item2, y), Leg(b.Item1, b.Item2, y), 0.22f, 8);
                    var mid = (Leg(a.Item1, a.Item2, y) + Leg(b.Item1, b.Item2, y)) / 2;
                    if (y < top - 7)
                    {
                        mb.Tube(Leg(a.Item1, a.Item2, y + 0.1f), mid + Vector3.up * 8.5f, 0.18f, 8);
                        mb.Tube(Leg(b.Item1, b.Item2, y + 0.1f), mid + Vector3.up * 8.5f, 0.18f, 8);
                    }
                }
            // Top deck: plate on a grid of beams, exactly the rules' platform.
            deck.Box(new Vector3(0, top - 0.06f, 0), new Vector3(2 * hx, 0.12f, 2 * hz));
            for (float x = -hx + 0.5f; x < hx; x += 2.2f)
                steel.Box(new Vector3(x, top - 0.5f, 0), new Vector3(0.3f, 0.7f, 2 * hz));
            // Cellar deck under it with the wellheads.
            float cellar = top - 5;
            deck.Box(new Vector3(0, cellar - 0.06f, 0), new Vector3(2 * hx - 1, 0.12f, 2 * hz - 1));
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3(-2.4f + i * 1.6f, cellar, -1.2f + (i % 2) * 2.2f);
                red.Cylinder(c, 0.28f, 1.6f, 10);
                red.Box(c + Vector3.up * 1.1f, new Vector3(0.8f, 0.35f, 0.35f));
                steel.Tube(c + Vector3.up * 0.4f, new Vector3(c.x, water - 2, c.z + 0.1f), 0.18f, 8);
            }
            // Rails round three sides of the top deck.
            Rails(steel, top, hx - 0.05f, hz - 0.05f);
            // Boat landing on the front face at the waterline.
            steel.Box(new Vector3(0, water + 1.4f, hz + 1.2f), new Vector3(5, 0.15f, 1.6f));
            foreach (float e in new[] { -2.4f, 2.4f })
                paint.Tube(new Vector3(e, water - 3, hz + 1.9f), new Vector3(e, water + 2.6f, hz + 1.9f), 0.2f, 8);
            // Nav-aid mast with the light on the back corner.
            var navFoot = new Vector3(hx - 0.35f, top, -hz + 0.35f);
            steel.Tube(navFoot, navFoot + Vector3.up * 5, 0.09f, 8);
            red.Box(navFoot + Vector3.up * 5.1f, new Vector3(0.35f, 0.35f, 0.35f));
            blocks.Add(new Bounds(navFoot + Vector3.up * 2.5f, new Vector3(0.6f, 5, 0.6f)));
            paint.Object("Jacket legs", Library.Surface("metal_plate", 3, new Color(0.88f, 0.70f, 0.20f), 0.6f), root);
            growth.Object("Jacket growth", Library.Plain(new Color(0.11f, 0.13f, 0.07f), 0.15f), root);
            deck.Object("Jacket decks", Library.Surface("rusty_metal_02", 3, new Color(0.72f, 0.70f, 0.66f)), root);
            steel.Object("Jacket steel", Library.Surface("rusty_painted_metal", 2, new Color(0.8f, 0.82f, 0.82f)), root);
            red.Object("Wellheads", Library.Plain(new Color(0.72f, 0.12f, 0.08f), 0.4f), root);
        }
    }
}
