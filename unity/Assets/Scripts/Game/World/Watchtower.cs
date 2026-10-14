using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    public static partial class Structures
    {
        /// <summary>
        /// A tower from the map's structure list. Each is built in its own frame with local +z looking
        /// at the other end of the lane, so the open side of the platform always faces the opponent.
        /// </summary>
        static void Tower(DuelMap map, Structure t, Transform parent)
        {
            var root = new GameObject($"Tower s={t.S:0}").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3((float)t.X, 0, (float)t.S);
            root.localRotation = Quaternion.Euler(0, t.S < map.LaneLength / 2 ? 0 : 180, 0);
            float water = (float)(map.Water ?? 0);
            float bed = (float)(map.Terrain ?? map.Ground)(t.X, -t.S);
            if (t.Style == TowerStyle.Rig) Rig(root, (float)t.TopY, (float)t.HalfSize, water, true);
            else Watchtower(root, (float)t.TopY, (float)t.HalfSize, water, bed);
        }

        /// <summary>
        /// A coastal fire-control tower in cast concrete: four battered legs on a pile cap, ring beams and
        /// X bracing, a closed observation room with horizontal firing slits, and the open roof the
        /// sniper lies on: sandbagged at the front corners, railed on three sides, with a gap in the
        /// front rail so nothing stands between the two shooters. A steel switchback stair on the back
        /// face climbs from a timber boat jetty; a caged ladder finishes the climb to the roof. The roof
        /// slab top is exactly `top` and its extent exactly ±hs, the platform the rules know.
        /// </summary>
        static void Watchtower(Transform root, float top, float hs, float water, float bed)
        {
            var rng = new System.Random(4711 + (int)root.localPosition.z);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // Weathered cast concrete: the room a shade warmer than the legs, green algae on the cap
            // and the tide-washed foot of each leg, dark run-off streaks under every slit.
            var room = Library.Surface("concrete_wall_008", 3, new Color(0.70f, 0.69f, 0.64f));
            var legs = Library.Surface("concrete_wall_008", 3, new Color(0.60f, 0.60f, 0.56f));
            var algae = Library.Surface("concrete_wall_008", 3, new Color(0.34f, 0.38f, 0.27f));
            var streakMat = Library.Surface("concrete_wall_008", 3, new Color(0.42f, 0.41f, 0.37f));
            var steelMat = Library.Surface("rusty_painted_metal", 2, new Color(0.55f, 0.58f, 0.47f));
            var sackMat = Library.Surface("coast_sand_01", 1.2f, new Color(0.74f, 0.66f, 0.50f));
            var timberMat = Library.Surface("weathered_planks", 3, new Color(0.72f, 0.70f, 0.64f));
            var roofMat = Library.Surface("corrugated_iron_02", 2, new Color(0.78f, 0.64f, 0.52f));
            var voidMat = Library.Plain(new Color(0.025f, 0.025f, 0.025f), 0.05f);

            var cast = new MeshBuilder();
            var legMb = new MeshBuilder();
            var wet = new MeshBuilder();
            var streaks = new MeshBuilder();
            var steel = new MeshBuilder();
            var sacks = new MeshBuilder();
            var timber = new MeshBuilder();
            var dark = new MeshBuilder();

            float plinthTop = water + 0.6f;
            float floorTop = top - 3.2f;          // observation room floor
            float legTop = floorTop - 0.45f;      // underside of the room's floor slab
            float roofUnder = top - 0.35f;        // underside of the roof slab
            // Leg centre lines batter in from ±(hs+0.6) at the cap to ±(hs−0.5) under the room, and the
            // section tapers 0.95 → 0.6 m, like a tower designed for wave load at the foot.
            float cBase = hs + 0.6f, cTop = hs - 0.5f;
            float Lc(float y) => Mathf.Lerp(cBase, cTop, Mathf.InverseLerp(plinthTop, legTop, y));
            float Lw(float y) => Mathf.Lerp(0.95f, 0.6f, Mathf.InverseLerp(plinthTop, legTop, y));
            Vector3 Leg(int sx, int sz, float y) => new Vector3(sx * Lc(y), y, sz * Lc(y));
            // Corners in order round the plan, so consecutive pairs are the four faces.
            var ring = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) };

            // Pile cap: a sloping-sided block from below the mud to just above the water.
            wet.Frustum(new Vector3(0, bed - 1, 0), Vector2.one * 2 * (cBase + 1.6f), new Vector3(0, plinthTop, 0), Vector2.one * 2 * (cBase + 1.0f));

            foreach (var (sx, sz) in ring)
            {
                float stain = plinthTop + R(1.1f, 1.7f);
                wet.Frustum(Leg(sx, sz, plinthTop), Vector2.one * Lw(plinthTop), Leg(sx, sz, stain), Vector2.one * Lw(stain));
                legMb.Frustum(Leg(sx, sz, stain), Vector2.one * Lw(stain), Leg(sx, sz, legTop - 0.7f), Vector2.one * Lw(legTop - 0.7f));
                // Flared capital spreading the load into the floor slab.
                legMb.Frustum(Leg(sx, sz, legTop - 0.7f), Vector2.one * Lw(legTop - 0.7f), Leg(sx, sz, legTop), Vector2.one * (Lw(legTop) + 0.5f));
            }
            // Ring beams at the quarter points and X bracing in every bay of every face.
            var levels = new float[5];
            for (int k = 0; k < 5; k++) levels[k] = Mathf.Lerp(plinthTop + 0.5f, legTop - 0.6f, k / 4f);
            for (int f = 0; f < 4; f++)
            {
                var (ax, az) = ring[f];
                var (bx, bz) = ring[(f + 1) % 4];
                for (int k = 1; k < 4; k++) legMb.Beam(Leg(ax, az, levels[k]), Leg(bx, bz, levels[k]), 0.34f, 0.5f);
                for (int k = 0; k < 4; k++)
                {
                    legMb.Beam(Leg(ax, az, levels[k]), Leg(bx, bz, levels[k + 1]), 0.26f, 0.26f);
                    legMb.Beam(Leg(bx, bz, levels[k]), Leg(ax, az, levels[k + 1]), 0.26f, 0.26f);
                }
            }

            // Observation room: floor slab with a drip lip, 0.3 m walls with a band of firing slits at
            // the eye height of a seated observer, a steel door at the back, the roof slab on top.
            cast.Box(new Vector3(0, floorTop - 0.225f, 0), new Vector3(2 * hs + 0.3f, 0.45f, 2 * hs + 0.3f));
            const float Th = 0.3f;
            float wo = hs - 0.2f;
            float bandLo = floorTop + 1.25f, bandHi = floorTop + 1.5f;
            void Wall(Vector3 mid, Vector3 along, Vector3 outward, float half, (float c, float w, float lo, float hi)[] holes)
            {
                bool alongX = Mathf.Abs(along.x) > 0.5f;
                Vector3 At(float u, float y) => mid + along * u + Vector3.up * y;
                Vector3 Size(float len, float h, float depth) => alongX ? new Vector3(len, h, depth) : new Vector3(depth, h, len);
                void Solid(float u0, float u1, float y0, float y1)
                {
                    if (u1 - u0 > 0.01f && y1 - y0 > 0.01f) cast.Box(At((u0 + u1) / 2, (y0 + y1) / 2), Size(u1 - u0, y1 - y0, Th));
                }
                float u = -half;
                foreach (var h in holes)
                {
                    float l = h.c - h.w / 2, r = h.c + h.w / 2;
                    Solid(u, l, floorTop, roofUnder);
                    Solid(l, r, floorTop, h.lo);
                    Solid(l, r, h.hi, roofUnder);
                    u = r;
                    if (h.lo <= floorTop + 0.01f) continue;
                    // Cast sill under the slit, and the grime washed off it down the wall.
                    cast.Box(At(h.c, h.lo - 0.03f) + outward * (Th / 2 + 0.05f), Size(h.w + 0.14f, 0.07f, 0.12f));
                    int runs = 1 + rng.Next(2);
                    for (int i = 0; i < runs; i++)
                    {
                        float sc = h.c + R(-h.w / 2.6f, h.w / 2.6f), sw = R(0.12f, 0.35f), sh = R(0.6f, h.lo - floorTop - 0.05f);
                        streaks.Box(At(sc, h.lo - 0.06f - sh / 2) + outward * (Th / 2 + 0.004f), Size(sw, sh, 0.008f));
                    }
                }
                Solid(u, half, floorTop, roofUnder);
            }
            var slits3 = new[] { (-1.75f, 1.1f, bandLo, bandHi), (0f, 1.1f, bandLo, bandHi), (1.75f, 1.1f, bandLo, bandHi) };
            var sideSlits = new[] { (-1.55f, 0.9f, bandLo, bandHi), (0f, 0.9f, bandLo, bandHi), (1.55f, 0.9f, bandLo, bandHi) };
            var backHoles = new[] { (-1.8f, 0.9f, bandLo, bandHi), (0f, 0.9f, floorTop, floorTop + 2.05f), (1.8f, 0.9f, bandLo, bandHi) };
            Wall(new Vector3(0, 0, wo - Th / 2), Vector3.right, Vector3.forward, wo, slits3);
            Wall(new Vector3(0, 0, -(wo - Th / 2)), Vector3.right, Vector3.back, wo, backHoles);
            Wall(new Vector3(wo - Th / 2, 0, 0), Vector3.forward, Vector3.right, wo - Th, sideSlits);
            Wall(new Vector3(-(wo - Th / 2), 0, 0), Vector3.forward, Vector3.left, wo - Th, sideSlits);
            // The dark room behind the slits, and the steel door.
            float inner = 2 * (wo - Th) - 0.02f;
            dark.Box(new Vector3(0, (floorTop + roofUnder) / 2, 0), new Vector3(inner, roofUnder - floorTop - 0.02f, inner));
            steel.Box(new Vector3(0, floorTop + 1.02f, -(wo - Th / 2)), new Vector3(0.86f, 2.02f, 0.06f));
            cast.Box(new Vector3(0, top - 0.175f, 0), new Vector3(2 * hs, 0.35f, 2 * hs));
            // Low upstand round the back half of the roof (never the front: the shooters lie there).
            cast.Box(new Vector3(-(hs + 0.5f) / 2, top + 0.15f, -(hs - 0.1f)), new Vector3(hs - 0.5f, 0.3f, 0.2f));
            cast.Box(new Vector3((hs + 0.5f) / 2, top + 0.15f, -(hs - 0.1f)), new Vector3(hs - 0.5f, 0.3f, 0.2f));
            foreach (float sx in new[] { -1f, 1f })
            {
                cast.Box(new Vector3(sx * (hs - 0.1f), top + 0.15f, -hs / 2 - 0.1f), new Vector3(0.2f, 0.3f, hs - 0.2f));
                // Rainwater spout through the upstand, and its stain down the slab edge.
                steel.Tube(new Vector3(sx * (hs - 0.3f), top + 0.06f, -hs + 0.7f), new Vector3(sx * (hs + 0.35f), top - 0.02f, -hs + 0.7f), 0.06f, 0.06f, 8);
                streaks.Box(new Vector3(sx * (hs + 0.004f), top - 0.175f, -hs + 0.7f), new Vector3(0.008f, 0.35f, 0.3f));
            }

            // Guard rail on three sides; the front rail stops 1.5 m either side of the centre line so
            // the shooters see each other at prone and standing height alike.
            float e = hs - 0.08f;
            const float Gap = 1.5f;
            Steelwork.Rail(steel, new Vector3(-e, top, -e), new Vector3(-e, top, e));
            Steelwork.Rail(steel, new Vector3(e, top, -e), new Vector3(e, top, e));
            Steelwork.Rail(steel, new Vector3(-e, top, -e), new Vector3(-0.45f, top, -e));
            Steelwork.Rail(steel, new Vector3(0.45f, top, -e), new Vector3(e, top, -e));
            Steelwork.Rail(steel, new Vector3(-e, top, e), new Vector3(-Gap, top, e));
            Steelwork.Rail(steel, new Vector3(Gap, top, e), new Vector3(e, top, e));

            // Sandbag walls, three courses in running bond, along the front half of each side and
            // round the front corners: cover for an observer, clear of the firing line.
            void Bags(Vector3 from, Vector3 to, int courses)
            {
                var d = to - from;
                bool alongX = Mathf.Abs(d.x) > Mathf.Abs(d.z);
                int n = Mathf.Max(1, Mathf.RoundToInt(d.magnitude / 0.56f));
                for (int c = 0; c < courses; c++)
                {
                    int count = n - (c % 2);
                    for (int i = 0; i < count; i++)
                    {
                        float f = (i + 0.5f + (c % 2) * 0.5f) / n;
                        var p = Vector3.Lerp(from, to, f) + new Vector3(R(-0.03f, 0.03f), c * 0.19f, R(-0.03f, 0.03f));
                        float len = R(0.54f, 0.6f), wid = R(0.32f, 0.37f), ht = R(0.2f, 0.23f);
                        var bottom = alongX ? new Vector2(len, wid) : new Vector2(wid, len);
                        sacks.Frustum(p, bottom, p + Vector3.up * ht, bottom - new Vector2(0.13f, 0.13f));
                    }
                }
            }
            foreach (float sx in new[] { -1f, 1f })
            {
                Bags(new Vector3(sx * (hs - 0.38f), top, 0.3f), new Vector3(sx * (hs - 0.38f), top, hs - 0.2f), 3);
                Bags(new Vector3(sx * (Gap + 0.05f), top, hs - 0.38f), new Vector3(sx * (hs - 0.55f), top, hs - 0.38f), 3);
            }

            // Lean-to shelter over the back of the roof: corrugated sheet on two purlins, four posts.
            float shelterFront = -1.5f;
            float slope = Mathf.Atan2(0.25f, 1.6f);
            float sheetZ = (-hs + shelterFront) / 2, sheetY = top + 2.25f;
            float RoofAt(float z) => sheetY + (z - sheetZ) * Mathf.Tan(slope);
            var sheet = new MeshBuilder();
            sheet.Box(new Vector3(0, sheetY, sheetZ), new Vector3(2 * hs + 0.3f, 0.04f, (hs + shelterFront + 0.2f) / Mathf.Cos(slope)), Quaternion.Euler(-slope * Mathf.Rad2Deg, 0, 0));
            sheet.Object("Shelter roof", roofMat, root);
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float z in new[] { -(hs - 0.12f), shelterFront })
                {
                    float h = RoofAt(z) - 0.06f - top;
                    steel.Box(new Vector3(sx * (hs - 0.12f), top + h / 2, z), new Vector3(0.08f, h, 0.08f));
                }
            }
            foreach (float z in new[] { -(hs - 0.15f), shelterFront + 0.1f })
                steel.Box(new Vector3(0, RoofAt(z) - 0.07f, z), new Vector3(2 * hs, 0.08f, 0.1f));

            // Radio mast on brackets off the back-left corner: lattice, crossarm dipoles, a whip and a
            // red obstruction light.
            var mastFoot = new Vector3(-(hs + 0.4f), floorTop - 0.4f, -(hs + 0.4f));
            float mastTop = top + 7.5f;
            Steelwork.Lattice(steel, mastFoot, new Vector3(mastFoot.x, mastTop, mastFoot.z), 0.45f, 0.28f, 0.05f, 0.025f, 14, Vector3.forward);
            foreach (float y in new[] { floorTop - 0.3f, floorTop + 1.6f, top - 0.2f })
            {
                float c = y > top - 1 ? hs - 0.05f : y < floorTop ? hs + 0.1f : wo;
                steel.Beam(new Vector3(mastFoot.x, y, mastFoot.z), new Vector3(-c, y, -c), 0.08f, 0.08f);
            }
            steel.Beam(new Vector3(mastFoot.x - 0.7f, mastTop - 1.4f, mastFoot.z), new Vector3(mastFoot.x + 0.7f, mastTop - 1.4f, mastFoot.z), 0.05f, 0.05f);
            foreach (float dx in new[] { -0.65f, 0.65f })
                steel.Beam(new Vector3(mastFoot.x + dx, mastTop - 2.2f, mastFoot.z), new Vector3(mastFoot.x + dx, mastTop - 0.4f, mastFoot.z), 0.025f, 0.025f);
            steel.Beam(new Vector3(mastFoot.x, mastTop, mastFoot.z), new Vector3(mastFoot.x, mastTop + 4, mastFoot.z), 0.03f, 0.03f);
            var lamp = new MeshBuilder();
            lamp.Box(new Vector3(mastFoot.x, mastTop + 0.15f, mastFoot.z), new Vector3(0.22f, 0.22f, 0.22f));
            lamp.Object("Obstruction light", Library.Glow(new Color(0.3f, 0.02f, 0.02f), new Color(4f, 0.25f, 0.15f)), root, shadows: false);

            // Switchback stair on the back face, outside the legs, on its own four posts: 3 m flights
            // at 40°, a landing at each turn, tied back to the legs at every landing.
            const float StairW = 0.9f;
            float zIn = -(cBase + 0.9f), zOut = zIn - 2 * StairW - 0.2f;
            float zA = zIn - StairW / 2 - 0.05f, zB = zOut + StairW / 2 + 0.05f;
            const float Run = 1.8f, LandEnd = 2.8f;
            float y0 = plinthTop + 0.2f;
            int flights = Mathf.Max(1, Mathf.RoundToInt((floorTop - y0) / 3));
            float rise = (floorTop - y0) / flights;
            void Landing(float xSign, float y)
            {
                float x0 = xSign * Run, x1 = xSign * LandEnd;
                steel.Box(new Vector3((x0 + x1) / 2, y - 0.04f, (zIn + zOut) / 2), new Vector3(Mathf.Abs(x1 - x0), 0.08f, zIn - zOut));
                Steelwork.Rail(steel, new Vector3(x1, y, zOut), new Vector3(x1, y, zIn));
                Steelwork.Rail(steel, new Vector3(x0, y, zOut), new Vector3(x1, y, zOut));
            }
            Landing(-1, y0);
            for (int k = 0; k < flights; k++)
            {
                float yl = y0 + k * rise, yh = yl + rise;
                float dir = k % 2 == 0 ? 1 : -1;
                float z = k % 2 == 0 ? zA : zB;
                Steelwork.Flight(steel, new Vector3(-dir * Run, yl, z), new Vector3(dir * Run, yh, z), StairW);
                Landing(dir, yh);
                foreach (float sx in new[] { -1f, 1f })
                    steel.Beam(new Vector3(sx * LandEnd, yh - 0.1f, zIn), Leg((int)sx, -1, yh - 0.1f), 0.12f, 0.12f);
            }
            foreach (float sx in new[] { -1f, 1f })
                foreach (float z in new[] { zIn, zOut })
                    steel.Box(new Vector3(sx * LandEnd, (bed - 0.5f + floorTop + 1) / 2, z), new Vector3(0.12f, floorTop + 1.5f - bed, 0.12f));
            // Walkway from the top landing to the door, and the caged ladder up to the roof.
            float walkOut = zIn + 0.05f, walkIn = -(hs + 0.15f);
            steel.Box(new Vector3(0, floorTop - 0.04f, (walkOut + walkIn) / 2), new Vector3(2 * LandEnd, 0.08f, walkIn - walkOut));
            // Guard the stairwell beside the walkway, leaving the way through from the top landing.
            float lastDir = (flights - 1) % 2 == 0 ? 1 : -1;
            Steelwork.Rail(steel, new Vector3(-lastDir * LandEnd, floorTop, walkOut), new Vector3(lastDir * Run, floorTop, walkOut));
            Steelwork.Ladder(steel, new Vector3(0, floorTop, -(hs + 0.3f)), top + 1.1f - floorTop, Vector3.back, 2.3f);

            // Timber boat jetty off the foot of the stair, on piles, with a few boards gone.
            float jx0 = -LandEnd, jx1 = -LandEnd - 6.5f, jz0 = zOut + 0.1f, jz1 = zIn - 0.1f;
            float deckY = y0 - 0.05f;
            for (float x = jx0 - 0.11f; x > jx1; x -= 0.23f)
                if (rng.NextDouble() > 0.1) timber.Box(new Vector3(x, deckY - 0.03f, (jz0 + jz1) / 2 + R(-0.03f, 0.03f)), new Vector3(0.2f, 0.06f, jz1 - jz0), Quaternion.Euler(0, R(-2, 2), 0));
            foreach (float z in new[] { jz0 + 0.15f, jz1 - 0.15f })
            {
                timber.Box(new Vector3((jx0 + jx1) / 2, deckY - 0.17f, z), new Vector3(jx0 - jx1, 0.22f, 0.12f));
                for (float x = jx0 - 0.3f; x >= jx1 - 0.01f; x -= 2.1f)
                {
                    bool end = x - 2.1f < jx1;
                    timber.Tube(new Vector3(x, bed - 0.5f, z), new Vector3(x + R(-0.08f, 0.08f), end ? deckY + 0.9f : deckY - 0.1f, z), 0.13f, 0.11f, 7, caps: true);
                }
            }

            // A portable searchlight on the back corner, out of the shooter's way.
            var light = Library.Place("portable_searchlight", new Vector3(hs - 0.75f, top, -hs + 0.75f), 135, 1, root);
            Library.Ground(light, root.TransformPoint(new Vector3(0, top, 0)).y);

            wet.Object("Tower foot", algae, root);
            legMb.Object("Tower frame", legs, root);
            cast.Object("Tower room", room, root);
            streaks.Object("Tower stains", streakMat, root, shadows: false);
            dark.Object("Tower interior", voidMat, root, shadows: false);
            steel.Object("Tower steel", steelMat, root);
            sacks.Object("Sandbags", sackMat, root);
            timber.Object("Jetty", timberMat, root);
        }
    }
}
