using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Bridges and towers from the map's structure list, built from boxes with scanned concrete,
    /// asphalt and painted-steel materials. Dimensions follow the hit model: the deck top is the
    /// ground players stand on, its extent is the platform the rules know about.
    /// </summary>
    public static class Structures
    {
        public static void Build(DuelMap map, Transform parent)
        {
            if (map.Look.Structures == null) return;
            foreach (var s in map.Look.Structures)
            {
                if (s.Kind == StructureKind.Bridge) Bridge(map, s, parent);
                else Tower(s, parent);
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

            var concrete = Library.Surface("concrete_wall_008", 3);
            var asphalt = Library.Surface("asphalt_02", 6);
            var steel = Library.Surface("rusty_painted_metal", 2, new Color(0.55f, 0.6f, 0.62f));
            var cable = Library.Plain(new Color(0.22f, 0.23f, 0.24f), 0.5f, 0.8f);
            var paint = Library.Plain(new Color(0.85f, 0.82f, 0.72f), 0.35f);

            // Deck slab and girders.
            var c = new MeshBuilder();
            c.Box(new Vector3(0, deck - 0.8f, 0), new Vector3(2 * reach, 1.6f, 2 * hw));
            c.Box(new Vector3(0, deck - 2.9f, -hw + 2.2f), new Vector3(2 * reach, 2.6f, 1.6f));
            c.Box(new Vector3(0, deck - 2.9f, hw - 2.2f), new Vector3(2 * reach, 2.6f, 1.6f));
            // Raised walkways along both edges.
            c.Box(new Vector3(0, deck + 0.1f, -hw + 0.9f), new Vector3(2 * reach, 0.2f, 1.8f));
            c.Box(new Vector3(0, deck + 0.1f, hw - 0.9f), new Vector3(2 * reach, 0.2f, 1.8f));

            // Piers down to the river bed, every 60 m, plus the main pylons.
            for (float x = -reach + 30; x < reach; x += 60)
            {
                if (Mathf.Abs(x) > reach - 10) continue;
                float bed = (float)ground(x, -b.S);
                if (bed > deck - 4) continue;
                float h = deck - 4.2f - bed + 2;
                c.Box(new Vector3(x, bed - 2 + h / 2, 0), new Vector3(3, h, 2 * hw - 3));
                c.Box(new Vector3(x, deck - 4.6f, 0), new Vector3(4.2f, 0.8f, 2 * hw + 0.4f));
            }
            foreach (double px in b.PylonsX)
            {
                float x = (float)px;
                float bed = (float)ground(x, -b.S);
                float top = deck + 46;
                // Two legs either side of the deck joined by cross beams: an H pylon.
                foreach (float side in new[] { -1f, 1f })
                {
                    float z = side * (hw + 1.6f);
                    float h = top - bed + 2;
                    c.Box(new Vector3(x, bed - 2 + h / 2, z), new Vector3(3.2f, h, 2.4f));
                }
                c.Box(new Vector3(x, deck - 5, 0), new Vector3(3.6f, 2.4f, 2 * hw + 6));
                c.Box(new Vector3(x, top - 3, 0), new Vector3(3.2f, 2.2f, 2 * hw + 6));
                c.Box(new Vector3(x, deck + 22, 0), new Vector3(3.2f, 1.6f, 2 * hw + 6));
            }
            c.Object("Concrete", concrete, root);

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
                for (float x = -reach; x <= reach; x += 2.5f) rail.Box(new Vector3(x, deck + 0.75f, z), new Vector3(0.08f, 1.1f, 0.08f));
                rail.Box(new Vector3(0, deck + 1.28f, z), new Vector3(2 * reach, 0.07f, 0.1f));
                rail.Box(new Vector3(0, deck + 0.7f, z), new Vector3(2 * reach, 0.05f, 0.06f));
                // Lamp posts every 40 m.
                for (float x = -reach + 20; x < reach; x += 40)
                {
                    rail.Box(new Vector3(x, deck + 4.2f, z), new Vector3(0.18f, 8.2f, 0.18f));
                    rail.Box(new Vector3(x, deck + 8.2f, z - side * 1.1f), new Vector3(0.12f, 0.12f, 2.3f));
                    rail.Box(new Vector3(x, deck + 8.05f, z - side * 2.1f), new Vector3(0.35f, 0.18f, 0.7f));
                }
            }
            rail.Object("Railings", steel, root);

            // Stay cables fanning from the pylon tops to the deck edges.
            var cables = new MeshBuilder();
            foreach (double px in b.PylonsX)
            {
                float x = (float)px;
                foreach (float side in new[] { -1f, 1f })
                {
                    float z = side * (hw + 1.6f);
                    for (int k = 1; k <= 7; k++)
                    {
                        foreach (float dir in new[] { -1f, 1f })
                        {
                            var top = new Vector3(x, deck + 44 - k * 2.4f, z);
                            var anchor = new Vector3(x + dir * (8 + k * 11), deck + 0.2f, side * (hw - 0.4f));
                            cables.Beam(top, anchor, 0.14f, 0.14f);
                        }
                    }
                }
            }
            cables.Object("Cables", cable, root);
        }

        static void Tower(Structure t, Transform parent)
        {
            var root = new GameObject($"Tower s={t.S:0}").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3((float)t.X, 0, (float)t.S);
            float top = (float)t.TopY, hs = (float)t.HalfSize;
            var mat = t.Style == TowerStyle.Rig ? Library.Surface("rusty_metal_02", 3) : Library.Surface("concrete_wall_008", 3);
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0, top - 0.4f, 0), new Vector3(2 * hs, 0.8f, 2 * hs));
            foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
                mb.Box(new Vector3(dx * (hs - 0.4f), (top - 0.8f) / 2 - 5, dz * (hs - 0.4f)), new Vector3(0.7f, top + 9.2f, 0.7f));
            for (float y = 4; y < top - 2; y += 6)
            {
                mb.Box(new Vector3(0, y, -hs + 0.4f), new Vector3(2 * hs, 0.35f, 0.35f));
                mb.Box(new Vector3(0, y, hs - 0.4f), new Vector3(2 * hs, 0.35f, 0.35f));
                mb.Box(new Vector3(-hs + 0.4f, y, 0), new Vector3(0.35f, 0.35f, 2 * hs));
                mb.Box(new Vector3(hs - 0.4f, y, 0), new Vector3(0.35f, 0.35f, 2 * hs));
            }
            mb.Object("Tower", mat, root);
            var rail = new MeshBuilder();
            foreach (var (dx, dz) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                var from = new Vector3(dx * hs + (dz != 0 ? -hs : 0), top + 1.05f, dz * hs + (dx != 0 ? -hs : 0));
                var to = new Vector3(dx * hs + (dz != 0 ? hs : 0), top + 1.05f, dz * hs + (dx != 0 ? hs : 0));
                rail.Beam(from, to, 0.06f, 0.06f);
            }
            rail.Object("Railing", Library.Surface("rusty_painted_metal", 2), root);
        }
    }
}
