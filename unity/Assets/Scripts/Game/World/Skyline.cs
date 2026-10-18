using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// A modern waterfront city on the river banks, densest in a downtown beyond the far bridge so it
    /// stands behind the enemy in the scope. Buildings sit on a street grid of lots: point towers with
    /// rounded or chamfered corners, setbacks and crowns, slab towers, mid-rise blocks with flat,
    /// penthouse or mansard roofs, and low brick sheds. Facades are generated textures (glass curtain
    /// walls, ribbon-window offices, stone and brick with punched windows, concrete grids, balconied
    /// flats) with smoothness in the alpha so the glass shines and the stone doesn't; nothing needs
    /// downloading. Everything is merged into one mesh per material.
    /// </summary>
    public static class Skyline
    {
        const float BayM = 3f, FloorM = 3.4f;
        const int Bays = 4, Floors = 8;

        internal enum Style { Curtain, Ribbon, Punched, Grid, Residential }
        enum Corner { Sharp, Chamfer, Round }

        /// <summary>A facade finish and the mesh of every wall that wears it.</summary>
        sealed class Finish
        {
            public Style Style;
            public Material Mat = null!;
            public float Floor;
            public readonly MeshBuilder Mesh = new MeshBuilder();
            /// <summary>A height rounded to whole floors, so the window grid ends cleanly at the roof.</summary>
            public float Snap(float h) => Mathf.Max(1, Mathf.Round(h / Floor)) * Floor;
            /// <summary>How far the facade runs up past the roof deck as a parapet.</summary>
            public float Parapet => Floor * (Style switch
            {
                Style.Curtain => 0.4f,
                Style.Ribbon => 0.38f,
                Style.Punched => 0.26f,
                Style.Grid => 0.16f,
                _ => 0.36f,
            });
        }

        public static void Build(DuelMap map, Transform parent, float riverHalfWidth)
        {
            var root = new GameObject("Skyline").transform;
            root.SetParent(parent, false);
            var ground = map.Terrain ?? map.Ground;
            var rng = new System.Random(map.Id.GetHashCode() ^ 0x5eed);
            float lane = (float)map.LaneLength;

            // One coherent palette: cool blue, green, smoked and silver glass for the towers; white and
            // charcoal ribbon offices; limestone, red and brown brick; pale and bronze grids; white
            // and terracotta flats. Colours are sRGB albedo.
            Finish F(Style style, Color wall, Color accent, Color glass, float metallic, float lit)
            {
                var f = new Finish { Style = style, Floor = Module(style).floor };
                f.Mat = Facade(rng, style, wall, accent, glass, metallic, lit);
                return f;
            }
            var fin = new[]
            {
                /* 0 */ F(Style.Curtain, new Color(0.34f, 0.37f, 0.4f), default, new Color(0.24f, 0.36f, 0.48f), 0.45f, 0.015f),
                /* 1 */ F(Style.Curtain, new Color(0.26f, 0.29f, 0.28f), default, new Color(0.18f, 0.33f, 0.33f), 0.45f, 0.015f),
                /* 2 */ F(Style.Curtain, new Color(0.11f, 0.11f, 0.12f), default, new Color(0.12f, 0.13f, 0.155f), 0.4f, 0.015f),
                /* 3 */ F(Style.Curtain, new Color(0.62f, 0.64f, 0.66f), default, new Color(0.4f, 0.47f, 0.54f), 0.65f, 0.01f),
                /* 4 */ F(Style.Ribbon, new Color(0.74f, 0.74f, 0.72f), new Color(0.5f, 0.52f, 0.54f), new Color(0.2f, 0.29f, 0.38f), 0.15f, 0.02f),
                /* 5 */ F(Style.Ribbon, new Color(0.17f, 0.17f, 0.18f), new Color(0.11f, 0.11f, 0.12f), new Color(0.2f, 0.3f, 0.3f), 0.25f, 0.02f),
                /* 6 */ F(Style.Punched, new Color(0.72f, 0.67f, 0.58f), new Color(0.2f, 0.2f, 0.2f), new Color(0.12f, 0.15f, 0.19f), 0, 0.03f),
                /* 7 */ F(Style.Punched, new Color(0.47f, 0.24f, 0.18f), new Color(0.82f, 0.82f, 0.8f), new Color(0.1f, 0.12f, 0.15f), 0, 0.03f),
                /* 8 */ F(Style.Punched, new Color(0.31f, 0.21f, 0.17f), new Color(0.14f, 0.14f, 0.14f), new Color(0.12f, 0.14f, 0.16f), 0, 0.03f),
                /* 9 */ F(Style.Grid, new Color(0.64f, 0.64f, 0.62f), new Color(0.3f, 0.3f, 0.3f), new Color(0.15f, 0.2f, 0.25f), 0.05f, 0.02f),
                /* 10 */ F(Style.Grid, new Color(0.33f, 0.24f, 0.16f), new Color(0.2f, 0.16f, 0.12f), new Color(0.16f, 0.19f, 0.22f), 0.45f, 0.02f),
                /* 11 */ F(Style.Residential, new Color(0.8f, 0.8f, 0.78f), new Color(0.46f, 0.44f, 0.41f), new Color(0.2f, 0.27f, 0.34f), 0.05f, 0.03f),
                /* 12 */ F(Style.Residential, new Color(0.74f, 0.72f, 0.68f), new Color(0.62f, 0.37f, 0.25f), new Color(0.18f, 0.24f, 0.3f), 0, 0.03f),
            };
            int[] towerFins = { 0, 0, 1, 1, 2, 3, 3, 4, 5, 9, 10 };
            int[] slabFins = { 0, 2, 4, 5, 9, 11, 11, 12 };
            int[] podiumFins = { 2, 5, 6, 9, 10 };
            int[] midFins = { 4, 5, 6, 6, 7, 8, 9, 11, 12 };
            int[] lowFins = { 6, 7, 7, 8, 8, 12, 9 };
            int[] penthouseFins = { 0, 2, 3, 5 };
            var deck = new MeshBuilder();   // roof decks and the backs of parapets
            var plant = new MeshBuilder();  // galvanised plant, lift overruns
            var metal = new MeshBuilder();  // crowns, screens, mansards, masts
            var marks = new MeshBuilder();  // helipad markings
            var beacons = new MeshBuilder();

            // Downtown stands on a quay that runs out to the river, so its towers rise from solid
            // ground rather than floating over the water where the terrain mesh ends.
            const float QuayTop = 3;
            float downFrom = lane + 250, downTo = lane + 1650;
            var quay = new MeshBuilder();
            foreach (float side in new[] { -1f, 1f })
            {
                float inner = riverHalfWidth + 40, outer = riverHalfWidth + 900;
                quay.Box(new Vector3(side * (inner + outer) / 2, QuayTop / 2 - 4, (downFrom + downTo) / 2), new Vector3(outer - inner, QuayTop + 8, downTo - downFrom));
            }
            quay.Object("Quay", Library.Surface("concrete_wall_008", 6, new Color(0.5f, 0.5f, 0.5f)), root);

            float R() => (float)rng.NextDouble();
            int Pick(int[] from) => from[rng.Next(from.Length)];
            int helipads = 0;

            // Where a footprint can stand: on dry ground or the quay. Its base sinks 3 m so no slope
            // shows under it.
            bool Site(Vector2 c, float w, float d, out float baseY)
            {
                float r = 0.5f * Mathf.Sqrt(w * w + d * d);
                float yMin = (float)ground(c.x, -c.y);
                foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1), (0, 1), (0, -1), (1, 0), (-1, 0) })
                    yMin = Mathf.Min(yMin, (float)ground(c.x + dx * r * 0.75f, -(c.y + dz * r * 0.75f)));
                baseY = yMin - 3;
                if (c.y > downFrom && c.y < downTo) { baseY = Mathf.Max(yMin, QuayTop) - 3; return true; }
                return yMin >= 2;
            }

            Corner AnyCorner(out float rFrac)
            {
                double p = rng.NextDouble();
                rFrac = 0.1f + 0.15f * R();
                if (p < 0.35) return Corner.Sharp;
                if (p < 0.65) return Corner.Chamfer;
                rFrac = 0.15f + 0.35f * R();
                return Corner.Round;
            }

            // A mass of one finish: walls from y0 up `height` (whole floors) and on as a parapet, and
            // the roof deck. The deck is level with the parapet's top (it is only ever seen from far
            // below, and that saves the parapet's back). Returns the deck height.
            float Mass(int f, Vector2 c, float w, float d, float yaw, Corner corner, float r, float y0, float height, bool parapet = true)
            {
                var ring = Footprint(c, w, d, yaw, corner, r);
                float top = y0 + fin[f].Snap(height) + (parapet ? fin[f].Parapet : 0);
                fin[f].Mesh.Extrude(ring, y0, top, y0);
                deck.Lid(ring, top);
                return top;
            }

            Vector2 Off(Vector2 c, float yaw, float lx, float lz) => Turn(new Vector2(lx, lz), yaw) + c;

            Vector3 At(Vector2 c, float yaw, float lx, float lz, float y)
            {
                var p = Turn(new Vector2(lx, lz), yaw) + c;
                return new Vector3(p.x, y, p.y);
            }
            Quaternion Yaw(float yaw) => Quaternion.Euler(0, -yaw, 0);

            // Rooftop plant: a few galvanised boxes and a lift overrun inside the parapet.
            void Plant(Vector2 c, float w, float d, float yaw, float y, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    float sx = 2.5f + R() * Mathf.Min(7, w * 0.25f), sz = 2.5f + R() * Mathf.Min(6, d * 0.25f), sy = 1.8f + R() * 1.6f;
                    var p = At(c, yaw, (R() - 0.5f) * (w - sx - 2) * 0.9f, (R() - 0.5f) * (d - sz - 2) * 0.9f, y + sy / 2);
                    plant.Box(p, new Vector3(sx, sy, sz), Yaw(yaw));
                }
            }

            void Mast(Vector3 at, float height, bool beacon)
            {
                metal.Tube(at, at + Vector3.up * height, 0.45f, 0.18f, 6);
                if (beacon) beacons.Box(at + Vector3.up * (height + 0.4f), Vector3.one * 0.9f);
            }

            void Helipad(Vector2 c, float yaw, float y, float size)
            {
                float r = Mathf.Clamp(size * 0.36f, 7, 12);
                var mid = new Vector3(c.x, y, c.y);
                plant.Box(mid + Vector3.up * 1.2f, new Vector3(r * 0.8f, 2.4f, r * 0.8f), Yaw(yaw));
                deck.Cylinder(mid + Vector3.up * 2.4f, r, 0.5f, 16);
                marks.Annulus(mid + Vector3.up * 2.92f, r * 0.72f, r * 0.8f, 24);
                // The H.
                float hw = r * 0.22f, hh = r * 0.32f;
                marks.Box(At(c, yaw, -hw, 0, y + 2.92f), new Vector3(0.9f, 0.04f, hh * 2), Yaw(yaw));
                marks.Box(At(c, yaw, hw, 0, y + 2.92f), new Vector3(0.9f, 0.04f, hh * 2), Yaw(yaw));
                marks.Box(At(c, yaw, 0, 0, y + 2.92f), new Vector3(hw * 2, 0.04f, 0.9f), Yaw(yaw));
                beacons.Box(At(c, yaw, r * 0.9f, 0, y + 3.1f), Vector3.one * 0.5f);
            }

            // The top of a tower: a glass crown, a taper, a stepped top, a helipad or a plain roof
            // with a plant screen; masts on the tall ones.
            void Crown(int f, Vector2 c, float w, float d, float yaw, Corner corner, float r, float y, float height, bool landmark)
            {
                int kind = landmark ? 5 : helipads < 2 && height > 110 && rng.NextDouble() < 0.35 ? 4 : rng.Next(4);
                var ring = Footprint(c, w, d, yaw, corner, r);
                switch (kind)
                {
                    case 1:
                    {
                        // The curtain wall runs on two or three floors past the roof as an open glass crown.
                        float top = y + fin[f].Floor * (2 + rng.Next(2));
                        fin[f].Mesh.Extrude(ring, y, top, y - fin[f].Parapet);
                        fin[f].Mesh.Extrude(ring, y, top, y - fin[f].Parapet, inward: true);
                        Plant(c, w, d, yaw, y, 2);
                        break;
                    }
                    case 2:
                    {
                        // A tapered metal cap.
                        float k = 0.25f + 0.25f * R();
                        var tip = Footprint(c, w * k, d * k, yaw, corner, r * k);
                        metal.Extrude(ring, y, y + 0.08f * height + 6, y, cap: true, top: tip);
                        break;
                    }
                    case 3:
                    {
                        // Two short setback tiers in the same finish, then a plant screen.
                        float yy = y;
                        float k = 0.72f;
                        for (int t = 0; t < 2; t++, k *= 0.7f)
                            yy = Mass(f, c, w * k, d * k, yaw, corner, r * k, yy, fin[f].Floor * (1 + rng.Next(2)));
                        var screen = Footprint(c, w * k, d * k, yaw, Corner.Sharp, 0);
                        metal.Extrude(screen, yy, yy + 4, yy, cap: true);
                        break;
                    }
                    case 4:
                        helipads++;
                        Helipad(c, yaw, y, Mathf.Min(w, d));
                        break;
                    case 5:
                    {
                        // A landmark: tapered crown and a spire.
                        var tip = Footprint(c, w * 0.4f, d * 0.4f, yaw, corner, r * 0.4f);
                        float top = y + 0.1f * height;
                        fin[f].Mesh.Extrude(ring, y, top, y - fin[f].Parapet, top: tip);
                        metal.Lid(tip, top);
                        Mast(new Vector3(c.x, top, c.y), 0.18f * height, true);
                        return;
                    }
                    default:
                    {
                        // Plant screen: a louvred metal enclosure hiding the plant.
                        var screen = Footprint(c, w * 0.55f, d * 0.5f, yaw, Corner.Sharp, 0);
                        metal.Extrude(screen, y, y + 4 + R() * 3, y, cap: true);
                        Plant(c, w, d, yaw, y, 1 + rng.Next(2));
                        break;
                    }
                }
                if (height > 120 && rng.NextDouble() < 0.6)
                {
                    int masts = 1 + rng.Next(3);
                    for (int i = 0; i < masts; i++)
                        Mast(At(c, yaw, (R() - 0.5f) * w * 0.4f, (R() - 0.5f) * d * 0.4f, y), 8 + R() * 14, i == 0);
                }
            }

            // A point tower: maybe a podium, one to three setback tiers, a crown.
            void Tower(Vector2 c, float baseY, float height, float w, float d, float lotW, float lotD, bool landmark)
            {
                int f = landmark ? Pick(new[] { 0, 1, 3 }) : Pick(towerFins);
                float yaw = rng.NextDouble() < 0.18 ? 45 : rng.NextDouble() < 0.15 ? (R() - 0.5f) * 36 : 0;
                var corner = AnyCorner(out float rf);
                if (landmark && corner == Corner.Sharp) corner = Corner.Chamfer;
                float r = rf * Mathf.Min(w, d);
                float y = baseY, top = baseY + height;
                if (rng.NextDouble() < 0.55)
                {
                    int p = Pick(podiumFins);
                    if (p == f) p = 6;
                    float pw = Mathf.Min(lotW, w * 1.45f), pd = Mathf.Min(lotD, d * 1.45f);
                    y = Mass(p, c, pw, pd, 0, rng.NextDouble() < 0.5 ? Corner.Sharp : Corner.Chamfer, 4, y, 3 + fin[p].Floor * (3 + rng.Next(4)));
                    Plant(c + new Vector2(pw * 0.3f, 0), pw * 0.3f, pd * 0.6f, 0, y, 1);
                }
                int tiers = landmark ? 3 : height > 130 ? 1 + rng.Next(3) : 1 + rng.Next(2);
                for (int t = 0; t < tiers; t++)
                {
                    bool last = t == tiers - 1;
                    float h = last ? top - y : (top - y) * (0.55f + 0.2f * R());
                    if (h < fin[f].Floor * 2) h = fin[f].Floor * 2;
                    y = Mass(f, c, w, d, yaw, corner, r, y, h);
                    if (last) break;
                    float k = 0.78f + 0.12f * R();
                    w *= k;
                    d *= k;
                    r *= k;
                }
                Crown(f, c, w, d, yaw, corner, r, y, height, landmark);
            }

            // A slab tower: long and thin, sometimes with a lower wing at one end and a fly roof.
            void Slab(Vector2 c, float baseY, float height, float len, float dep, float yaw)
            {
                int f = Pick(slabFins);
                var corner = rng.NextDouble() < 0.7 ? Corner.Sharp : Corner.Chamfer;
                float y = Mass(f, c, len, dep, yaw, corner, dep * 0.2f, baseY, height);
                if (rng.NextDouble() < 0.4)
                {
                    // A lower wing off the back of one end.
                    float wl = len * (0.3f + 0.2f * R()), wd = 10 + R() * 6;
                    var wc = Off(c, yaw, (len - wl) / 2, -(dep + wd) / 2 + 1);
                    int g = Pick(midFins);
                    float wy = Mass(g, wc, wl, wd, yaw, Corner.Sharp, 0, baseY, height * (0.3f + 0.25f * R()));
                    Plant(wc, wl, wd, yaw, wy, 1);
                }
                Plant(c, len * 0.6f, dep, yaw, y, 2);
                if (rng.NextDouble() < 0.35)
                {
                    // A fly roof floating over the top floor on thin posts.
                    var lid = Footprint(c, len + 2, dep + 2, yaw, corner, dep * 0.2f);
                    metal.Extrude(lid, y + 3.4f, y + 3.9f, y, cap: true);
                    plant.Box(At(c, yaw, len * 0.2f, 0, y + 1.7f), new Vector3(5, 3.4f, 4), Yaw(yaw));
                }
                else if (height > 70) Mast(At(c, yaw, len * 0.35f, 0, y), 6 + R() * 6, true);
            }

            // A mid-rise block: a plain, L-shaped or corner-cut plan; a flat, penthouse, mansard or
            // stepped roof.
            void MidRise(Vector2 c, float baseY, float height, float w, float d, float yaw, int[] fins)
            {
                int f = Pick(fins);
                int plan = rng.Next(3);
                var corner = plan == 2 ? Corner.Chamfer : Corner.Sharp;
                float r = Mathf.Min(w, d) * 0.22f;
                if (plan == 1)
                {
                    // An L: the main bar plus a wing along one side.
                    float wd = d * 0.45f, ow = w;
                    w = ow * 0.55f;
                    var wc = Off(c, yaw, ow * 0.225f, -(d - wd) / 2);
                    c = Off(c, yaw, -ow * 0.225f, 0);
                    float wy = Mass(f, wc, w, wd, yaw, Corner.Sharp, 0, baseY, height * (0.7f + 0.3f * R()));
                    Plant(wc, w, wd, yaw, wy, 1);
                }
                int roof = rng.Next(4);
                float y = Mass(f, c, w, d, yaw, corner, r, baseY, height, roof != 2);
                switch (roof)
                {
                    case 1:
                    {
                        // A glazed penthouse set back from the edge.
                        int p = Pick(penthouseFins);
                        float pw = w - 6, pd = d - 6;
                        if (pw < 8 || pd < 8) goto default;
                        float py = Mass(p, c, pw, pd, yaw, Corner.Sharp, 0, y, fin[p].Floor * (1 + rng.Next(2)));
                        Plant(c, pw, pd, yaw, py, 1);
                        break;
                    }
                    case 2:
                    {
                        // A zinc mansard.
                        var ring = Footprint(c, w, d, yaw, corner, r);
                        var tip = Footprint(c, w - 5, d - 5, yaw, corner, Mathf.Max(0, r - 2.5f));
                        metal.Extrude(ring, y, y + 4.2f, y, cap: true, top: tip);
                        break;
                    }
                    case 3:
                    {
                        // A stepped top floor over half the plan.
                        float k = 0.5f + 0.2f * R();
                        var sc = Off(c, yaw, w * (1 - k) / 2, 0);
                        float sy = Mass(f, sc, w * k, d, yaw, Corner.Sharp, 0, y, fin[f].Floor * (1 + rng.Next(2)));
                        Plant(sc, w * k, d, yaw, sy, 1);
                        break;
                    }
                    default:
                        Plant(c, w, d, yaw, y, 1 + rng.Next(3));
                        break;
                }
            }

            // A low brick or concrete shed with a flat, gabled or sawtooth roof.
            void LowRise(Vector2 c, float baseY, float height, float w, float d, float yaw)
            {
                int f = Pick(lowFins);
                int roof = rng.Next(3);
                float y = Mass(f, c, w, d, yaw, Corner.Sharp, 0, baseY, height, roof == 0);
                if (roof == 0) { Plant(c, w, d, yaw, y, 1 + rng.Next(2)); return; }
                // Pitched roofs run across the long side; sawtooth ones in three or four bays.
                bool along = w >= d;
                float span = along ? d : w, run = along ? w : d;
                int teeth = roof == 1 ? 1 : 3 + rng.Next(2);
                float rise = roof == 1 ? Mathf.Min(6, span * 0.25f) : 3;
                for (int i = 0; i < teeth; i++)
                {
                    float a0 = -span / 2 + span * i / teeth, a1 = -span / 2 + span * (i + 1) / teeth;
                    Vector3 P(float across, float alongRun, float h) => along ? At(c, yaw, alongRun, across, y + h) : At(c, yaw, across, alongRun, y + h);
                    if (roof == 1)
                    {
                        float m = (a0 + a1) / 2;
                        metal.Face(P(a0, -run / 2, 0), P(a0, run / 2, 0), P(m, run / 2, rise), P(m, -run / 2, rise), Vector3.up);
                        metal.Face(P(m, -run / 2, rise), P(m, run / 2, rise), P(a1, run / 2, 0), P(a1, -run / 2, 0), Vector3.up);
                        Gable(fin[f].Mesh, P(a0, -run / 2, 0), P(a1, -run / 2, 0), P(m, -run / 2, rise), P(m, 0, 0));
                        Gable(fin[f].Mesh, P(a0, run / 2, 0), P(a1, run / 2, 0), P(m, run / 2, rise), P(m, 0, 0));
                    }
                    else
                    {
                        // Sawtooth: a sloping roof up to a glazed north light.
                        metal.Face(P(a0, -run / 2, 0), P(a0, run / 2, 0), P(a1, run / 2, rise), P(a1, -run / 2, rise), Vector3.up);
                        fin[2].Mesh.Face(P(a1, -run / 2, 0), P(a1, run / 2, 0), P(a1, run / 2, rise), P(a1, -run / 2, rise), P(a1, 0, 0) - P(a0, 0, 0));
                        Gable(fin[f].Mesh, P(a0, -run / 2, 0), P(a1, -run / 2, 0), P(a1, -run / 2, rise), P((a0 + a1) / 2, 0, 0));
                        Gable(fin[f].Mesh, P(a0, run / 2, 0), P(a1, run / 2, 0), P(a1, run / 2, rise), P((a0 + a1) / 2, 0, 0));
                    }
                }
            }

            bool NearBridge(float s) => map.Look.Structures != null && System.Array.Exists(map.Look.Structures, st => Mathf.Abs(s - (float)st.S) < 45);

            foreach (float side in new[] { -1f, 1f })
            {
                // Downtown: a street grid of 72 × 80 m lots past the enemy's bridge, tallest around a
                // core near the water, thinning out to mid-rise blocks and the odd empty plaza.
                var core = new Vector2(side * (riverHalfWidth + 300), lane + 820);
                var lots = new List<Vector2>();
                for (float s = lane + 290; s < lane + 1580; s += 80)
                    for (float off = 92; off < 700; off += 72)
                        lots.Add(new Vector2(side * (riverHalfWidth + off + 36) + (R() - 0.5f) * 8, s + 40 + (R() - 0.5f) * 8));
                lots.Sort((a, b) => (a - core).sqrMagnitude.CompareTo((b - core).sqrMagnitude));
                for (int i = 0; i < lots.Count; i++)
                {
                    var c = lots[i];
                    float k = Mathf.Exp(-(c - core).sqrMagnitude / (480f * 480f));
                    bool landmark = i < 2;
                    if (!landmark && rng.NextDouble() > 0.5 + 0.4 * k) continue;
                    const float LotW = 56, LotD = 62;
                    if (!Site(c, LotW, LotD, out float baseY)) continue;
                    float h = landmark ? (i == 0 ? 235 : 185) + R() * 30 : 28 + 170 * k * (0.35f + 0.65f * R()) + 45 * R();
                    if (h > 85)
                    {
                        if (!landmark && rng.NextDouble() < 0.25)
                            Slab(c, baseY, h * 0.75f, 44 + R() * 10, 17 + R() * 5, rng.NextDouble() < 0.3 ? (R() - 0.5f) * 30 : 0);
                        else
                        {
                            float w = 26 + R() * 14, d = landmark ? w : 24 + R() * 18;
                            Tower(c, baseY, h, w, d, LotW, LotD, landmark);
                        }
                    }
                    else if (rng.NextDouble() < 0.55)
                    {
                        // Two mid-rise blocks sharing the lot.
                        foreach (float half in new[] { -1f, 1f })
                            MidRise(c + new Vector2(0, half * LotD / 4), baseY, h * (0.6f + 0.5f * R()), 30 + R() * 24, LotD / 2 - 4, 0, midFins);
                    }
                    else MidRise(c, baseY, h, 36 + R() * 20, 40 + R() * 20, 0, midFins);
                }

                // The lower city all along both banks: mid-rise blocks and slabs by the water, low
                // sheds further back, the lots emptier away from the river. Bridge approaches stay open.
                for (float s = -1400; s < lane + 250; s += 76)
                {
                    if (NearBridge(s + 38)) continue;
                    for (float off = 112; off < 840; off += 70)
                    {
                        if (rng.NextDouble() > 0.58 - 0.4 * off / 840) continue;
                        var c = new Vector2(side * (riverHalfWidth + off + 35) + (R() - 0.5f) * 10, s + 38 + (R() - 0.5f) * 10);
                        if (!Site(c, 54, 60, out float baseY)) continue;
                        float yaw = rng.NextDouble() < 0.12 ? (R() - 0.5f) * 24 : 0;
                        float h = 12 + R() * 26 + (off < 320 ? R() * 20 : 0);
                        if (off < 360 && rng.NextDouble() < 0.12)
                            Slab(c, baseY, 45 + R() * 40, 38 + R() * 12, 15 + R() * 4, yaw);
                        else if (h < 20) LowRise(c, baseY, h, 30 + R() * 22, 22 + R() * 30, yaw);
                        else MidRise(c, baseY, h, 28 + R() * 22, 22 + R() * 28, yaw, midFins);
                    }
                }
            }

            int verts = deck.VertexCount + plant.VertexCount + metal.VertexCount + marks.VertexCount + beacons.VertexCount;
            for (int k = 0; k < fin.Length; k++)
            {
                verts += fin[k].Mesh.VertexCount;
                if (fin[k].Mesh.VertexCount > 0) fin[k].Mesh.Object("Blocks " + k + " " + fin[k].Style, fin[k].Mat, root);
            }
            deck.Object("Roof decks", Library.Plain(new Color(0.3f, 0.3f, 0.29f), 0.15f), root);
            plant.Object("Rooftop plant", Library.Plain(new Color(0.44f, 0.45f, 0.45f), 0.4f, 0.3f), root);
            if (metal.VertexCount > 0) metal.Object("Crowns", Library.Plain(new Color(0.3f, 0.32f, 0.34f), 0.6f, 0.7f), root);
            if (marks.VertexCount > 0) marks.Object("Helipad marks", Library.Plain(new Color(0.75f, 0.75f, 0.72f), 0.2f), root, shadows: false);
            var red = Library.Glow(new Color(0.3f, 0.02f, 0.02f), new Color(5f, 0.3f, 0.2f));
            if (beacons.VertexCount > 0) beacons.Object("Beacons", red, root, shadows: false);
            Debug.Log($"[Skyline] {verts} vertices, {helipads} helipads");
        }

        /// <summary>A triangular gable end, a–b along the eaves and c the ridge, facing away from `inside`.</summary>
        static void Gable(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, (a + b + c) / 3 - inside) < 0) mb.Triangle(a, c, b);
            else mb.Triangle(a, b, c);
        }

        static Vector2 Turn(Vector2 p, float yawDeg)
        {
            float a = yawDeg * Mathf.Deg2Rad, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            return new Vector2(p.x * cs - p.y * sn, p.x * sn + p.y * cs);
        }

        /// <summary>
        /// A w × d footprint (x, z) centred on `c`, turned by `yawDeg`, its corners square, chamfered
        /// or rounded with radius `r` (a round corner as big as the plan allows makes a drum).
        /// </summary>
        static Vector2[] Footprint(Vector2 c, float w, float d, float yawDeg, Corner corner, float r)
        {
            r = Mathf.Min(r, Mathf.Min(w, d) * 0.49f);
            if (r < 0.5f) corner = Corner.Sharp;
            int segs = corner == Corner.Round ? 3 : corner == Corner.Chamfer ? 1 : 0;
            var pts = new Vector2[segs == 0 ? 4 : 4 * (segs + 1)];
            int n = 0;
            for (int k = 0; k < 4; k++)
            {
                float sx = k == 0 || k == 3 ? 1 : -1, sz = k < 2 ? 1 : -1;
                if (segs == 0) { pts[n++] = Turn(new Vector2(sx * w / 2, sz * d / 2), yawDeg) + c; continue; }
                var cc = new Vector2(sx * (w / 2 - r), sz * (d / 2 - r));
                for (int i = 0; i <= segs; i++)
                {
                    float a = (k + (float)i / segs) * Mathf.PI / 2;
                    pts[n++] = Turn(cc + r * new Vector2(Mathf.Cos(a), Mathf.Sin(a)), yawDeg) + c;
                }
            }
            return pts;
        }

        /// <summary>Bay width, floor height and bays per texture repeat of each facade style, metres.</summary>
        static (float bay, float floor, int bays) Module(Style s) => s switch
        {
            Style.Curtain => (1.5f, 4f, 8),
            Style.Ribbon => (1.5f, 3.8f, 8),
            Style.Punched => (3f, 3.6f, 4),
            Style.Grid => (3f, 3.9f, 4),
            _ => (3.6f, 3.1f, 4),
        };

        /// <summary>
        /// A facade texture of eight floors: glazing that reflects (dark tinted albedo, smoothness
        /// near 1 in the alpha, a lighter top where it catches the sky, blinds and lit offices behind
        /// some panes) set in the style's frame of matte stone, brick, concrete or metal. `wall` is the
        /// frame or wall, `accent` the mullions or panels, `glass` the pane tint, all sRGB.
        /// </summary>
        static Material Facade(System.Random rng, Style style, Color wall, Color accent, Color glass, float metallic, float litShare)
        {
            const int Px = 32, Rows = 8; // pixels per bay and per floor
            var (bayM, floorM, bays) = Module(style);
            int w = bays * Px, h = Rows * Px;
            var albedo = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Facade " + style, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var glow = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Facade glow", wrapMode = TextureWrapMode.Repeat };
            var a = new Color32[w * h];
            var e = new Color32[w * h];
            if (style == Style.Curtain) accent = wall;
            // Window proportions for punched walls; which bays of a block of flats have balconies.
            float winW = 0.48f + 0.16f * (float)rng.NextDouble(), winH = 0.46f + 0.12f * (float)rng.NextDouble(), sill = 0.3f;
            float x0 = 0.5f - winW / 2, x1 = 0.5f + winW / 2, y0 = sill, y1 = sill + winH;
            var balcony = new bool[bays];
            for (int i = 0; i < bays; i++) balcony[i] = i % 2 == 0 || rng.NextDouble() < 0.3;
            float seed = (float)rng.NextDouble() * 100;
            var blinds = new Color(0.66f, 0.64f, 0.6f);
            for (int fy = 0; fy < Rows; fy++)
            {
                for (int bx = 0; bx < bays; bx++)
                {
                    bool lit = rng.NextDouble() < litShare;
                    var light = Color.Lerp(new Color(1f, 0.8f, 0.55f), new Color(0.88f, 0.92f, 1f), (float)rng.NextDouble());
                    // Each pane reflects a little differently (its tilt, its coating).
                    var pane = Color.Lerp(glass * 0.85f, glass * 1.35f + new Color(0.03f, 0.03f, 0.03f), (float)rng.NextDouble());
                    float blind = rng.NextDouble() < 0.4 ? 0.1f + 0.6f * (float)rng.NextDouble() : 0;
                    float gloss = 0.88f + 0.1f * (float)rng.NextDouble();
                    for (int y = 0; y < Px; y++)
                    {
                        for (int x = 0; x < Px; x++)
                        {
                            float u = (x + 0.5f) / Px, v = (y + 0.5f) / Px;
                            float grain = 0.9f + 0.1f * Mathf.PerlinNoise(seed + (bx * Px + x) * 0.23f, (fy * Px + y) * 0.23f);
                            Color c = wall;
                            float sm = 0.15f, dim = 1, g0 = 0, g1 = 1;
                            bool glazed = false;
                            switch (style)
                            {
                                case Style.Curtain:
                                    if (x < 2 || v < 0.05f) { c = wall; sm = 0.55f; }        // mullion, transom
                                    else if (v < 0.2f) { c = pane * 0.5f; sm = gloss; }    // spandrel glass, opaque behind
                                    else { glazed = true; g0 = 0.2f; }
                                    break;
                                case Style.Ribbon:
                                    if (v < 0.4f) { c = wall * grain * (x == 0 ? 0.8f : 1) * (v > 0.36f ? 1.08f : 1); sm = 0.35f; }
                                    else if (v < 0.44f) { c = pane * 0.3f; sm = 0.3f; } // reveal in the band's shadow
                                    else if (x < 2) { c = accent; sm = 0.5f; }
                                    else { glazed = true; g0 = 0.44f; }
                                    break;
                                case Style.Punched:
                                    if (u > x0 && u < x1 && v > y0 && v < y1)
                                    {
                                        if (v > y1 - 0.06f || u < x0 + 0.04f) { c = wall * 0.4f; sm = 0.1f; } // reveal
                                        else if (Mathf.Abs(u - 0.5f) < 0.025f || Mathf.Abs(v - (y0 + winH * 0.72f)) < 0.016f) { c = accent; sm = 0.4f; }
                                        else { glazed = true; g0 = y0; g1 = y1 - 0.06f; }
                                    }
                                    else
                                    {
                                        c = wall * grain * (0.95f + 0.1f * (float)rng.NextDouble());
                                        sm = 0.1f;
                                        if (v < y0 && v > y0 - 0.05f && u > x0 - 0.04f && u < x1 + 0.04f) c = wall * 1.2f; // sill
                                        else if (v < 0.035f) c = wall * 0.8f;                                            // floor joint
                                    }
                                    break;
                                case Style.Grid:
                                    if (u < 0.1f || u > 0.9f || v < 0.17f)
                                    {
                                        // Piers and floor beams, the right flank of each pier in shade.
                                        c = wall * grain * (u > 0.9f && v >= 0.17f ? 0.7f : 1) * (v < 0.04f ? 0.82f : 1);
                                        sm = 0.25f;
                                    }
                                    else if (v > 0.9f || u < 0.16f) { c = pane * 0.3f; sm = 0.5f; } // shadow in the recess
                                    else if (Mathf.Abs(u - 0.5f) < 0.02f) { c = accent; sm = 0.45f; }
                                    else { glazed = true; g0 = 0.17f; g1 = 0.9f; }
                                    break;
                                default:
                                    if (v < 0.09f) { c = wall * 1.04f; sm = 0.25f; } // slab edge
                                    else if (balcony[bx] && v < 0.37f) { c = Color.Lerp(pane, wall, 0.3f) * (x < 1 ? 0.6f : 1); sm = 0.8f; } // glass balustrade
                                    else if (balcony[bx])
                                    {
                                        // Doors recessed behind the balcony, in the shade of the slab above.
                                        if (v > 0.86f) { c = accent * 0.35f; sm = 0.1f; }
                                        else if (u < 0.1f || u > 0.9f) { c = accent * 0.7f; sm = 0.2f; }
                                        else { glazed = true; g0 = 0.37f; g1 = 0.86f; dim = 0.75f; }
                                    }
                                    else if (u > 0.2f && u < 0.8f && v > 0.32f && v < 0.82f) { glazed = true; g0 = 0.32f; g1 = 0.82f; }
                                    else { c = accent * grain; sm = 0.18f; }
                                    break;
                            }
                            if (glazed)
                            {
                                float t = Mathf.Clamp01((v - g0) / (g1 - g0));
                                c = pane * (0.8f + 0.35f * t) * dim;
                                if (blind > 0 && t > 1 - blind) c = Color.Lerp(c, blinds * dim, 0.35f);
                                if (lit) c = light * 0.85f;
                                sm = gloss;
                            }
                            int i = (fy * Px + y) * w + bx * Px + x;
                            a[i] = new Color(c.r, c.g, c.b, sm);
                            e[i] = glazed && lit ? light : Color.black;
                        }
                    }
                }
            }
            albedo.SetPixels32(a);
            albedo.Apply(true);
            glow.SetPixels32(e);
            glow.Apply(true);

            // Smoothness comes from the albedo's alpha (glass ~0.95, stone ~0.1); metallic is the
            // material's, so a metal-and-glass wall mirrors the sky in its own tint.
            var mat = new Material(Library.Template("LitFacade")) { name = "Facade " + style };
            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_SmoothnessTextureChannel", 1);
            mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            mat.SetFloat("_Smoothness", 1);
            mat.SetFloat("_Metallic", metallic);
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", glow);
            mat.SetColor("_EmissionColor", Color.white * 0.9f);
            // MeshBuilder UVs are metres × 0.25: one texture repeat per `bays` bays and eight floors.
            var scale = new Vector2(4f / (bays * bayM), 4f / (Rows * floorM));
            mat.SetTextureScale("_BaseMap", scale);
            mat.SetTextureScale("_EmissionMap", scale);
            mat.mainTextureScale = scale;
            return mat;
        }

        /// <summary>
        /// A window-grid facade: wall colour, glazing with sky reflection, some windows lit. A curtain
        /// wall is glass from floor to floor between thin mullions, and shinier.
        /// </summary>
        internal static Material Facade(System.Random rng, Color wall, float litShare, bool curtain)
        {
            const int Px = 32; // pixels per bay and per floor
            int w = Bays * Px, h = Floors * Px;
            var albedo = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Facade", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var glow = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Facade glow", wrapMode = TextureWrapMode.Repeat };
            var a = new Color32[w * h];
            var e = new Color32[w * h];
            for (int fy = 0; fy < Floors; fy++)
            {
                for (int bx = 0; bx < Bays; bx++)
                {
                    bool lit = rng.NextDouble() < litShare;
                    float shade = 0.75f + (float)rng.NextDouble() * 0.3f;
                    var glass = new Color(0.16f, 0.2f, 0.25f) * shade + new Color(0.1f, 0.1f, 0.1f) * (float)rng.NextDouble() * 0.5f;
                    var light = Color.Lerp(new Color(1f, 0.78f, 0.5f), new Color(0.85f, 0.9f, 1f), (float)rng.NextDouble());
                    for (int y = 0; y < Px; y++)
                    {
                        for (int x = 0; x < Px; x++)
                        {
                            // Window opening: 70 % of the bay wide, 55 % of the floor high, with a sill.
                            bool window = curtain
                                ? x > 0 && x < Px - 1 && y > Px * 0.1f && y < Px * 0.95f
                                : x > Px * 0.15f && x < Px * 0.85f && y > Px * 0.25f && y < Px * 0.8f;
                            bool mullion = window && Mathf.Abs(x - Px / 2) < 1;
                            float grain = 0.93f + 0.07f * Mathf.PerlinNoise((bx * Px + x) * 0.3f, (fy * Px + y) * 0.3f);
                            Color pane = curtain ? Color.Lerp(wall, new Color(0.35f, 0.45f, 0.55f), 0.25f + 0.2f * y / Px) * shade : glass * (0.85f + 0.3f * y / Px);
                            Color c = window && !mullion ? (lit ? light * 0.8f : pane) : (curtain ? wall * 0.6f : wall * grain);
                            if (!window && y < Px * 0.25f && y > Px * 0.2f) c *= 0.8f; // sill shadow
                            int i = (fy * Px + y) * w + bx * Px + x;
                            a[i] = c;
                            e[i] = window && !mullion && lit ? light : Color.black;
                        }
                    }
                }
            }
            albedo.SetPixels32(a);
            albedo.Apply(true);
            glow.SetPixels32(e);
            glow.Apply(true);

            var mat = new Material(Library.Template("LitEmissive")) { name = "Facade" };
            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", curtain ? 0.8f : 0.4f);
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", glow);
            mat.SetColor("_EmissionColor", Color.white * 0.9f);
            // MeshBuilder UVs are metres × 0.25: one texture repeat per Bays × Floors.
            var scale = new Vector2(4f / (Bays * BayM), 4f / (Floors * FloorM));
            mat.SetTextureScale("_BaseMap", scale);
            mat.SetTextureScale("_EmissionMap", scale);
            mat.mainTextureScale = scale;
            return mat;
        }
    }
}
