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
            var girderPaint = Library.Surface("metal_plate", 3, new Color(0.30f, 0.36f, 0.42f), 0.6f);
            var cable = Library.Plain(new Color(0.6f, 0.62f, 0.62f), 0.55f, 0.2f);
            var dark = Library.Plain(new Color(0.08f, 0.09f, 0.1f), 0.35f, 0.6f);
            var paint = Library.Plain(new Color(0.85f, 0.82f, 0.72f), 0.35f);
            var lamps = new MeshBuilder();

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

            // Concrete stained dark at the waterline (piers and footings).
            var wet = new MeshBuilder();
            // Piers down to the river bed, every 60 m: a tapered column with a cap under the girder.
            for (float x = -reach + 30; x < reach; x += 60)
            {
                if (Mathf.Abs(x) > reach - 10) continue;
                float bed = (float)ground(x, -b.S);
                if (bed > gBottom - 1) continue;
                c.Beam(new Vector3(x, bed - 2, 0), new Vector3(x, gBottom - 0.9f, 0), 2.6f, 2 * hw - 5);
                if (bed < -0.5f) wet.Box(new Vector3(x, 0.5f, 0), new Vector3(2.7f, 2f, 2 * hw - 4.9f));
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
                    // One smooth taper from the footing to the crown, with a shallow recess down
                    // each face (two slimmer shafts proud of the core) so the legs catch the light.
                    c.Frustum(new Vector3(x, bed - 2, zBase), new Vector2(4f, 3.2f), new Vector3(x, top, zTop), new Vector2(2.4f, 1.92f));
                    foreach (float e in new[] { -1f, 1f })
                        c.Frustum(new Vector3(x + e * 1.1f, 1.5f, zBase + (zTop - zBase) * 0.03f), new Vector2(1.4f, 3.4f),
                            new Vector3(x + e * 0.62f, top - 0.6f, zTop), new Vector2(0.9f, 2.1f));
                    // The footing: a pile cap standing out of the water, stained dark where the tide wets it.
                    c.Box(new Vector3(x, -1.5f, zBase), new Vector3(7.5f, 7f, 6.5f));
                    wet.Box(new Vector3(x, 0.45f, zBase), new Vector3(7.6f, 1.9f, 6.6f));
                    wet.Box(new Vector3(x, 1.1f, zBase), new Vector3(4.15f, 1.6f, 3.35f));
                    // Anchorage housing where the cables land.
                    c.Box(new Vector3(x, top - 7.5f, zTop), new Vector3(3.0f, 13f, 2.6f));
                    aviation.Add(new Vector3(x, top + 0.4f, zTop));
                }
                c.Box(new Vector3(x, gBottom - 1.2f, 0), new Vector3(3.6f, 2.4f, 2 * hw + 7));
                c.Box(new Vector3(x, deck + 22, 0), new Vector3(2.8f, 1.8f, 2 * hw + 4));
                c.Box(new Vector3(x, top - 1.2f, 0), new Vector3(2.8f, 2.4f, 2 * hw + 3));
            }
            // Where the deck lands on each bank: a bridge seat (abutment) wall down into the slope.
            foreach (float dir in new[] { -1f, 1f })
            {
                float xa = dir * (reach - 14);
                float bank = (float)ground(xa, -b.S);
                if (bank > gBottom - 1) continue;
                c.Box(new Vector3(dir * (reach - 7), (gBottom + bank - 4) / 2, 0), new Vector3(14, gBottom - bank + 4, 2 * hw + 2));
                c.Box(new Vector3(xa, (gBottom + bank - 6) / 2, 0), new Vector3(1.2f, gBottom - bank + 6, 2 * hw + 6));
            }
            c.Object("Concrete", concrete, root);
            wet.Object("Waterline", Library.Surface("concrete_wall_008", 4, new Color(0.22f, 0.24f, 0.22f), 1.4f), root);

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

            // Parapets: a low concrete upstand with an open steel railing on it (posts every 2 m, a
            // top rail and two mid rails), and a galvanised W-beam guardrail between the walkway and
            // the road. All of it stops short of mid-span where the shooters lie, so nothing stands
            // in the firing line, and a crossover gap in the median keeps the spot clear behind them.
            var rail = new MeshBuilder();
            var upstand = new MeshBuilder();
            var guard = new MeshBuilder();
            const float Gap = 2, GuardGap = 4, MedianGap = 10;
            foreach (float side in new[] { -1f, 1f })
            {
                float z = side * (hw - 0.15f);
                foreach (float dir in new[] { -1f, 1f })
                {
                    float mid = dir * (Gap + reach) / 2, len = reach - Gap;
                    upstand.Box(new Vector3(mid, deck + 0.35f, z), new Vector3(len, 0.3f, 0.34f));
                    rail.Box(new Vector3(mid, deck + 1.28f, z), new Vector3(len, 0.09f, 0.12f));
                    rail.Box(new Vector3(mid, deck + 1.0f, z), new Vector3(len, 0.05f, 0.05f));
                    rail.Box(new Vector3(mid, deck + 0.74f, z), new Vector3(len, 0.05f, 0.05f));
                    rail.Box(new Vector3(dir * Gap, deck + 0.85f, z), new Vector3(0.12f, 0.9f, 0.12f));
                    // W-beam guardrail on its posts.
                    float gz = side * (hw - 1.95f);
                    float gm = dir * (GuardGap + reach) / 2, gl = reach - GuardGap;
                    guard.Box(new Vector3(gm, deck + 0.62f, gz), new Vector3(gl, 0.32f, 0.08f));
                    guard.Box(new Vector3(gm, deck + 0.62f, gz - side * 0.06f), new Vector3(gl, 0.1f, 0.06f));
                    for (float x = GuardGap; x <= reach; x += 2)
                        guard.Box(new Vector3(dir * x, deck + 0.42f, gz + side * 0.12f), new Vector3(0.15f, 0.84f, 0.15f));
                }
                for (float x = -reach; x <= reach; x += 2f)
                    if (Mathf.Abs(x) > Gap) rail.Box(new Vector3(x, deck + 0.85f, z), new Vector3(0.1f, 0.9f, 0.07f));
                // Lamp posts every 40 m: a tapered pole, a swept arm out over the road and a slim luminaire.
                for (float x = -reach + 20; x < reach; x += 40)
                {
                    // Nothing tall in the firing line: keep lamp posts clear of mid-span.
                    if (Mathf.Abs(x) < 14) continue;
                    var foot = new Vector3(x, deck + 0.2f, z);
                    lamps.Tube(foot, foot + Vector3.up * 8.6f, 0.13f, 0.08f, 8, true);
                    var bend = foot + new Vector3(0, 8.6f, 0);
                    var tip = foot + new Vector3(0, 9.1f, -side * 2.2f);
                    lamps.Tube(bend, bend + new Vector3(0, 0.4f, -side * 0.6f), 0.08f, 0.07f, 8, true);
                    lamps.Tube(bend + new Vector3(0, 0.4f, -side * 0.6f), tip, 0.07f, 0.06f, 8, true);
                    lamps.Box(tip + new Vector3(0, -0.08f, -side * 0.35f), new Vector3(0.38f, 0.16f, 0.9f));
                    lamps.Cylinder(foot - Vector3.up * 0.2f, 0.24f, 0.45f, 8);
                }
            }
            // Median: a concrete safety barrier down the middle, with a crossover gap at mid-span.
            var median = new MeshBuilder();
            foreach (float dir in new[] { -1f, 1f })
            {
                float a = dir * MedianGap, e = dir * reach;
                var bottom = new Vector3((a + e) / 2, deck + 0.02f, 0);
                median.Frustum(bottom, new Vector2(Mathf.Abs(e - a), 0.62f), bottom + Vector3.up * 0.25f, new Vector2(Mathf.Abs(e - a), 0.5f));
                median.Frustum(bottom + Vector3.up * 0.25f, new Vector2(Mathf.Abs(e - a), 0.5f), bottom + Vector3.up * 0.84f, new Vector2(Mathf.Abs(e - a), 0.2f));
                // Glare screen posts on top.
                for (float x = MedianGap + 1; x < reach; x += 4) rail.Box(new Vector3(dir * x, deck + 1.05f, 0), new Vector3(0.06f, 0.42f, 0.06f));
            }
            median.Object("Median", Library.Surface("concrete_wall_008", 3, new Color(0.84f, 0.84f, 0.82f)), root);
            upstand.Object("Upstands", concrete, root);
            guard.Object("Guardrail", Library.Plain(new Color(0.62f, 0.64f, 0.65f), 0.55f, 0.8f), root);
            lamps.Object("Lamps", Library.Plain(new Color(0.34f, 0.36f, 0.37f), 0.5f, 0.7f), root);

            // Overhead sign gantries well out from mid-span: a box truss on two legs with a green
            // direction sign facing each way.
            var gantry = new MeshBuilder();
            var sign = new MeshBuilder();
            var signEdge = new MeshBuilder();
            foreach (float dir in new[] { -1f, 1f })
            {
                float gx = dir * Mathf.Min(150, reach - 30);
                foreach (float side in new[] { -1f, 1f })
                {
                    var foot = new Vector3(gx, deck + 0.2f, side * (hw + 0.5f));
                    gantry.Box(foot + Vector3.up * 3.9f, new Vector3(0.45f, 7.8f, 0.45f));
                }
                Steelwork.Lattice(gantry, new Vector3(gx, deck + 7.4f, -(hw + 0.5f)), new Vector3(gx, deck + 7.4f, hw + 0.5f), 1.1f, 1.1f, 0.12f, 0.05f, 8, Vector3.up);
                float face = gx + dir * 0.7f;
                foreach (float lane in new[] { -1f, 1f })
                {
                    var cpos = new Vector3(face, deck + 7.0f, lane * 3.4f);
                    sign.Box(cpos, new Vector3(0.12f, 2.2f, 5.2f));
                    signEdge.Box(cpos + new Vector3(dir * 0.07f, 0.98f, 0), new Vector3(0.02f, 0.1f, 5.0f));
                    signEdge.Box(cpos + new Vector3(dir * 0.07f, -0.98f, 0), new Vector3(0.02f, 0.1f, 5.0f));
                    signEdge.Box(cpos + new Vector3(dir * 0.07f, 0, 2.45f), new Vector3(0.02f, 2.0f, 0.1f));
                    signEdge.Box(cpos + new Vector3(dir * 0.07f, 0, -2.45f), new Vector3(0.02f, 2.0f, 0.1f));
                    // A route shield and a stub of lettering, too far off to read.
                    signEdge.Box(cpos + new Vector3(dir * 0.07f, 0.35f, -1.6f), new Vector3(0.02f, 0.7f, 0.8f));
                    for (int l = 0; l < 2; l++)
                        signEdge.Box(cpos + new Vector3(dir * 0.07f, 0.45f - l * 0.62f, 0.45f), new Vector3(0.02f, 0.24f, 2.6f - l * 0.9f));
                }
            }
            gantry.Object("Sign gantries", Library.Plain(new Color(0.55f, 0.57f, 0.58f), 0.5f, 0.8f), root);
            sign.Object("Signs", Library.Plain(new Color(0.05f, 0.27f, 0.15f), 0.35f), root);
            signEdge.Object("Sign lettering", Library.Plain(new Color(0.9f, 0.9f, 0.88f), 0.3f), root, shadows: false);

            // Expansion joints across the road at the ends and over the pylons, and drain grates
            // along both kerbs.
            var joints = new MeshBuilder();
            foreach (float jx in new[] { -reach + 3, reach - 3, (float)b.PylonsX[0], (float)b.PylonsX[b.PylonsX.Length - 1] })
                joints.Box(new Vector3(jx, deck + 0.04f, 0), new Vector3(0.45f, 0.02f, 2 * hw - 3.7f));
            for (float x = -reach + 7; x < reach; x += 15)
                foreach (float side in new[] { -1f, 1f })
                    if (Mathf.Abs(x) > 3) joints.Box(new Vector3(x, deck + 0.04f, side * (hw - 2.15f)), new Vector3(0.9f, 0.02f, 0.35f));
            joints.Object("Joints and drains", dark, root, shadows: false);
            rail.Object("Railings", Library.Plain(new Color(0.2f, 0.25f, 0.24f), 0.45f, 0.5f), root);

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
                            cables.Tube(top, anchor, 0.08f, 6, false);
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
