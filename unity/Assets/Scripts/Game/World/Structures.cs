using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Bridges and towers from the map's structure list, built from scanned concrete, asphalt and
    /// painted-steel materials. Dimensions follow the hit model: the deck top is the
    /// ground players stand on, its extent is the platform the rules know about. Towers are in
    /// Watchtower.cs and Rig.cs.
    /// </summary>
    public static partial class Structures
    {
        public static void Build(DuelMap map, Transform parent)
        {
            if (map.Look.Structures == null) return;
            foreach (var s in map.Look.Structures)
            {
                if (s.Kind == StructureKind.Bridge) Bridge(map, s, parent);
                else Tower(map, s, parent);
            }
        }

        static void Bridge(DuelMap map, Structure b, Transform parent)
        {
            var root = new GameObject($"Bridge s={b.S:0}").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(0, 0, (float)b.S);
            float deck = (float)b.DeckY;
            float hw = (float)b.HalfWidth;
            var ground = map.Terrain ?? map.Ground;
            // The road runs on past the platform the rules know about until it meets the bank.
            float reach = (float)b.HalfSpan;
            while (reach < 900 && ground(reach, -b.S) < deck - 0.3) reach += 5;

            // Weathered grey concrete for piers and pylons, a blue-grey painted steel box girder under
            // a thin concrete deck, light grey sheathed stay cables: the palette of a real cable-stayed bridge.
            var concrete = Library.Surface("concrete_wall_008", 5, new Color(0.56f, 0.6f, 0.68f));
            var asphalt = Library.Surface("asphalt_02", 6);
            var steel = Library.Surface("rusty_painted_metal", 2, new Color(0.55f, 0.6f, 0.62f));
            var girderPaint = Library.Surface("metal_plate", 3, new Color(0.30f, 0.36f, 0.42f), 0.6f);
            var cable = Library.Plain(new Color(0.42f, 0.44f, 0.45f), 0.5f, 0.2f);
            var dark = Library.Plain(new Color(0.08f, 0.09f, 0.1f), 0.35f, 0.6f);
            var paint = Library.Plain(new Color(0.85f, 0.82f, 0.72f), 0.35f);

            // Deck: a 0.35 m concrete slab with an overhanging edge, raised walkways along both edges.
            var c = new MeshBuilder();
            c.Box(new Vector3(0, deck - 0.2f, 0), new Vector3(2 * reach, 0.4f, 2 * hw + 0.5f));
            c.Box(new Vector3(0, deck + 0.1f, -hw + 0.9f), new Vector3(2 * reach, 0.2f, 1.8f));
            c.Box(new Vector3(0, deck + 0.1f, hw - 0.9f), new Vector3(2 * reach, 0.2f, 1.8f));

            // Steel box girder under the deck: sloping webs, a bottom flange and a stiffener every 4 m.
            var g = new MeshBuilder();
            const float GirderDepth = 2.8f;
            float gTop = deck - 0.4f, gBottom = gTop - GirderDepth;
            g.Box(new Vector3(0, gTop - 0.1f, 0), new Vector3(2 * reach, 0.2f, 2 * hw - 0.6f));
            g.Box(new Vector3(0, gBottom + 0.1f, 0), new Vector3(2 * reach, 0.2f, 2 * hw - 4.4f));
            foreach (float side in new[] { -1f, 1f })
            {
                // Inclined web from the deck edge down to the narrower bottom flange.
                var top = new Vector3(0, gTop, side * (hw - 0.3f));
                var bottom = new Vector3(0, gBottom, side * (hw - 2.2f));
                var mid = (top + bottom) / 2;
                float tilt = Mathf.Atan2(top.z - bottom.z, top.y - bottom.y) * Mathf.Rad2Deg;
                g.Box(mid, new Vector3(2 * reach, Vector3.Distance(top, bottom), 0.06f), Quaternion.Euler(tilt, 0, 0));
                for (float x = -reach + 2; x < reach; x += 4)
                    g.Box(new Vector3(x, mid.y, mid.z + side * 0.12f), new Vector3(0.18f, Vector3.Distance(top, bottom) - 0.2f, 0.22f), Quaternion.Euler(tilt, 0, 0));
                // Drip edge: a dark fascia strip that shadows the slab edge.
                g.Box(new Vector3(0, deck - 0.45f, side * (hw + 0.2f)), new Vector3(2 * reach, 0.18f, 0.12f));
            }
            g.Object("Girder", girderPaint, root);

            // Piers down to the river bed, every 60 m: a tapered column with a cap under the girder.
            for (float x = -reach + 30; x < reach; x += 60)
            {
                if (Mathf.Abs(x) > reach - 10) continue;
                float bed = (float)ground(x, -b.S);
                if (bed > gBottom - 1) continue;
                c.Beam(new Vector3(x, bed - 2, 0), new Vector3(x, gBottom - 0.9f, 0), 2.6f, 2 * hw - 5);
                c.Box(new Vector3(x, gBottom - 0.5f, 0), new Vector3(3.6f, 0.8f, 2 * hw - 2));
                // Bearings between cap and girder.
                foreach (float side in new[] { -1f, 1f })
                    c.Box(new Vector3(x, gBottom - 0.05f, side * (hw - 3)), new Vector3(0.9f, 0.1f, 0.9f));
            }
            // H pylons: legs taper from 4 m at the waterline to 2.4 m at the top, with cross beams under
            // the deck, at mid height and at the crown, and a cable anchorage block up top.
            var aviation = new List<Vector3>();
            foreach (double px in b.PylonsX)
            {
                float x = (float)px;
                float bed = (float)ground(x, -b.S);
                float top = deck + 46;
                foreach (float side in new[] { -1f, 1f })
                {
                    float zBase = side * (hw + 2.4f), zTop = side * (hw + 1.4f);
                    const int Segments = 6;
                    for (int k = 0; k < Segments; k++)
                    {
                        float t0 = (float)k / Segments, t1 = (float)(k + 1) / Segments;
                        float y0 = Mathf.Lerp(bed - 2, top, t0), y1 = Mathf.Lerp(bed - 2, top, t1);
                        float z = Mathf.Lerp(zBase, zTop, (t0 + t1) / 2);
                        float w = Mathf.Lerp(4f, 2.4f, (t0 + t1) / 2);
                        c.Box(new Vector3(x, (y0 + y1) / 2, z), new Vector3(w, y1 - y0 + 0.02f, w * 0.8f));
                    }
                    // Anchorage housing where the cables land.
                    c.Box(new Vector3(x, top - 7.5f, zTop), new Vector3(3.0f, 13f, 2.6f));
                    aviation.Add(new Vector3(x, top + 0.4f, zTop));
                }
                c.Box(new Vector3(x, gBottom - 1.2f, 0), new Vector3(3.6f, 2.4f, 2 * hw + 7));
                c.Box(new Vector3(x, deck + 22, 0), new Vector3(2.8f, 1.8f, 2 * hw + 4));
                c.Box(new Vector3(x, top - 1.2f, 0), new Vector3(2.8f, 2.4f, 2 * hw + 3));
            }
            c.Object("Concrete", concrete, root);

            // Red aviation lights on the pylon tops.
            var lights = new MeshBuilder();
            foreach (var p in aviation) lights.Box(p, new Vector3(0.5f, 0.5f, 0.5f));
            var red = Library.Glow(new Color(0.3f, 0.02f, 0.02f), new Color(4f, 0.25f, 0.15f));
            lights.Object("Aviation lights", red, root, shadows: false);

            var road = new MeshBuilder();
            road.Box(new Vector3(0, deck + 0.01f, 0), new Vector3(2 * reach, 0.04f, 2 * hw - 3.6f));
            road.Object("Road", asphalt, root);

            // Lane markings: dashed centre line and solid edges.
            var marks = new MeshBuilder();
            for (float x = -reach; x < reach; x += 12) marks.Box(new Vector3(x + 3, deck + 0.035f, 0), new Vector3(6, 0.01f, 0.15f));
            marks.Box(new Vector3(0, deck + 0.035f, -hw + 2.1f), new Vector3(2 * reach, 0.01f, 0.15f));
            marks.Box(new Vector3(0, deck + 0.035f, hw - 2.1f), new Vector3(2 * reach, 0.01f, 0.15f));
            marks.Object("Markings", paint, root, shadows: false);

            // Open steel railings (players must see and be seen through them): posts every 2.5 m, two rails.
            var rail = new MeshBuilder();
            foreach (float side in new[] { -1f, 1f })
            {
                float z = side * (hw - 0.15f);
                // A 4 m gap at mid-span where the shooters lie, so nothing stands in the firing line.
                const float Gap = 2;
                for (float x = -reach; x <= reach; x += 2.5f)
                    if (Mathf.Abs(x) > Gap) rail.Box(new Vector3(x, deck + 0.75f, z), new Vector3(0.08f, 1.1f, 0.08f));
                foreach (float dir in new[] { -1f, 1f })
                {
                    float mid = dir * (Gap + reach) / 2, len = reach - Gap;
                    rail.Box(new Vector3(mid, deck + 1.28f, z), new Vector3(len, 0.07f, 0.1f));
                    rail.Box(new Vector3(mid, deck + 0.7f, z), new Vector3(len, 0.05f, 0.06f));
                    rail.Box(new Vector3(dir * Gap, deck + 0.75f, z), new Vector3(0.1f, 1.1f, 0.1f));
                }
                // Lamp posts every 40 m.
                for (float x = -reach + 20; x < reach; x += 40)
                {
                    // Nothing tall in the firing line: keep lamp posts clear of mid-span.
                    if (Mathf.Abs(x) < 14) continue;
                    rail.Box(new Vector3(x, deck + 4.2f, z), new Vector3(0.18f, 8.2f, 0.18f));
                    rail.Box(new Vector3(x, deck + 8.2f, z - side * 1.1f), new Vector3(0.12f, 0.12f, 2.3f));
                    rail.Box(new Vector3(x, deck + 8.05f, z - side * 2.1f), new Vector3(0.35f, 0.18f, 0.7f));
                }
            }
            rail.Object("Railings", steel, root);

            // Stay cables fanning from the anchorage to the deck edge, each with a steel guide pipe and
            // anchor plate where it meets the deck.
            var cables = new MeshBuilder();
            var anchors = new MeshBuilder();
            foreach (double px in b.PylonsX)
            {
                float x = (float)px;
                foreach (float side in new[] { -1f, 1f })
                {
                    float z = side * (hw + 1.4f);
                    for (int k = 1; k <= 9; k++)
                    {
                        foreach (float dir in new[] { -1f, 1f })
                        {
                            var top = new Vector3(x + dir * 0.9f, deck + 45 - k * 1.3f, z);
                            var anchor = new Vector3(x + dir * (10 + k * 8), deck + 0.25f, side * (hw + 0.05f));
                            cables.Beam(top, anchor, 0.12f, 0.12f);
                            var along = (top - anchor).normalized;
                            anchors.Beam(anchor, anchor + along * 1.6f, 0.26f, 0.26f);
                            anchors.Box(anchor + new Vector3(0, -0.05f, 0), new Vector3(0.7f, 0.25f, 0.6f));
                        }
                    }
                }
            }
            anchors.Object("Cable anchors", dark, root);
            cables.Object("Cables", cable, root);
        }
    }
}
