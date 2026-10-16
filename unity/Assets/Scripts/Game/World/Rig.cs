using System.Collections.Generic;
using UnityEngine;

namespace SniperDuel.Game.World
{
    public static partial class Structures
    {
        /// <summary>
        /// A fixed North Sea production platform, built in the tower's frame (local +z looks at the other
        /// rig). A four-legged steel jacket battered 1:10, K-braced, stands in the sea with marine growth
        /// through the splash zone, well conductors and risers inside it and a boat landing on one face.
        /// On top: a cellar deck of process plant (separators, compressors, pipe racks), a main deck with
        /// the living quarters, containers, lifeboats, two pedestal cranes, a drilling derrick and a
        /// flare boom with a burning tip, and the helideck on the quarters roof. The helideck surface is
        /// exactly `top` and covers exactly ±hs, the platform the rules know; the players lie on it, so
        /// nothing on it stands proud of the paint and everything tall sits off its square. With
        /// `detail` off (a rig on the horizon) the rails, stairs, nets and small pipework are left out.
        /// </summary>
        internal static void Rig(Transform root, float top, float hs, float water, bool detail)
        {
            var rng = new System.Random(9157 + (int)root.localPosition.z);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // Paint scheme of a North Sea jacket: safety-yellow legs and cranes, white-grey process
            // steel, rust-streaked deck plate, white clad quarters, dark green helideck.
            var paintMat = Library.Surface("metal_plate", 3, new Color(0.88f, 0.70f, 0.20f), 0.6f);
            var growthMat = Library.Plain(new Color(0.11f, 0.13f, 0.07f), 0.15f);
            var deckMat = Library.Surface("rusty_metal_02", 3, new Color(0.72f, 0.70f, 0.66f));
            var frameMat = Library.Surface("rusty_painted_metal", 2, new Color(0.80f, 0.82f, 0.82f));
            var safetyMat = Library.Plain(new Color(0.90f, 0.66f, 0.08f), 0.45f);
            var cladMat = Library.Surface("corrugated_iron_02", 2, new Color(0.93f, 0.93f, 0.90f));
            var moduleMat = Library.Surface("corrugated_iron_02", 2, new Color(0.55f, 0.62f, 0.60f));
            var glassMat = Library.Plain(new Color(0.05f, 0.08f, 0.11f), 0.92f, 0.3f);
            var heliMat = Library.Surface("metal_plate", 1.5f, new Color(0.2f, 0.29f, 0.24f), 0.35f);
            var yellowMat = Library.Plain(new Color(0.95f, 0.74f, 0.08f), 0.3f);
            var whiteMat = Library.Plain(new Color(0.92f, 0.92f, 0.88f), 0.3f);
            var netMat = Library.Plain(new Color(0.09f, 0.09f, 0.08f), 0.1f);
            var pipeMat = Library.Plain(new Color(0.36f, 0.45f, 0.36f), 0.45f, 0.3f);
            var orangeMat = Library.Plain(new Color(1f, 0.42f, 0.05f), 0.6f);
            var boxTints = new[] { new Color(0.62f, 0.16f, 0.10f), new Color(0.14f, 0.28f, 0.52f), new Color(0.22f, 0.42f, 0.26f), new Color(0.88f, 0.45f, 0.12f) };

            var paint = new MeshBuilder();
            var growth = new MeshBuilder();
            var deck = new MeshBuilder();
            var frame = new MeshBuilder();
            var safety = new MeshBuilder();
            var clad = new MeshBuilder();
            var module = new MeshBuilder();
            var glass = new MeshBuilder();
            var heli = new MeshBuilder();
            var yellow = new MeshBuilder();
            var white = new MeshBuilder();
            var net = new MeshBuilder();
            var rope = new MeshBuilder();
            var foam = new MeshBuilder();
            var pipe = new MeshBuilder();
            var orange = new MeshBuilder();
            var boxes = new MeshBuilder[boxTints.Length];
            for (int i = 0; i < boxes.Length; i++) boxes[i] = new MeshBuilder();
            var red = new MeshBuilder();
            var green = new MeshBuilder();
            var warm = new MeshBuilder();

            // Levels and plan. The decks run back from under the helideck to carry the drilling end;
            // the helideck cantilevers 8 m past the front of the decks over the sea.
            float main = top - 8, cellar = top - 16, jacketTop = top - 20;
            float yLow = water - 14; // below this the (opaque) sea hides everything
            float DX = hs + 4, ZF = hs - 8, ZR = -(hs + 22), zMid = (ZF + ZR) / 2;
            float LX = hs - 3, zc = -(hs - 5), HZ = hs - 1;
            // Jacket leg centre line: battered 1:10 in both directions below the jacket top.
            Vector3 JL(int sx, int sf, float y)
            {
                float b = (jacketTop - Mathf.Min(y, jacketTop)) * 0.1f;
                return new Vector3(sx * (LX + b), y, zc + sf * (HZ + b));
            }
            // A tubular member, painted above the splash zone and furred with growth below it.
            float growthLine = water + 1.5f;
            void Member(Vector3 a, Vector3 b, float r, int sides = 10)
            {
                if (a.y > b.y) (a, b) = (b, a);
                if (b.y <= growthLine) { growth.Tube(a, b, r + 0.06f, r + 0.06f, sides); return; }
                if (a.y >= growthLine) { paint.Tube(a, b, r, r, sides); return; }
                var m = Vector3.Lerp(a, b, (growthLine - a.y) / (b.y - a.y));
                growth.Tube(a, m, r + 0.06f, r + 0.06f, sides);
                paint.Tube(m, b, r, r, sides);
            }

            // ---- Jacket ----------------------------------------------------------------------------
            var plan = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) };
            foreach (var (sx, sf) in plan)
            {
                Member(JL(sx, sf, yLow), JL(sx, sf, jacketTop), 1.1f, 16);
                // Node can where the jacket meets the deck leg, then the deck leg straight up.
                paint.Tube(JL(sx, sf, jacketTop - 1.4f), JL(sx, sf, jacketTop + 0.6f), 1.3f, 1.3f, 16, caps: true);
                paint.Tube(JL(sx, sf, jacketTop), JL(sx, sf, jacketTop) + Vector3.up * (main - 1.6f - jacketTop), 1.0f, 1.0f, 16);
            }
            float midLevel = water + 4;
            var frameLevels = new[] { yLow, midLevel, jacketTop };
            for (int f = 0; f < 4; f++)
            {
                var (ax, af) = plan[f];
                var (bx, bf) = plan[(f + 1) % 4];
                // Horizontal ring members, then a K in each bay: both legs up to the mid-point above.
                Member(JL(ax, af, midLevel), JL(bx, bf, midLevel), 0.5f);
                Member(JL(ax, af, jacketTop), JL(bx, bf, jacketTop), 0.55f);
                for (int k = 0; k < 2; k++)
                {
                    float y0 = frameLevels[k], y1 = frameLevels[k + 1];
                    var mid = (JL(ax, af, y1) + JL(bx, bf, y1)) / 2;
                    Member(JL(ax, af, y0), mid, 0.42f);
                    Member(JL(bx, bf, y0), mid, 0.42f);
                }
            }
            // Plan bracing across the mid frame.
            Member(JL(-1, -1, midLevel), JL(1, 1, midLevel), 0.4f);
            Member(JL(1, -1, midLevel), JL(-1, 1, midLevel), 0.4f);

            // Nine well conductors under the derrick, held by guide frames at each jacket level.
            float dxC = -5, dzC = ZR + 13;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    Member(new Vector3(dxC + i * 2.6f, yLow, dzC + j * 2.6f), new Vector3(dxC + i * 2.6f, cellar - 0.3f, dzC + j * 2.6f), 0.33f, 8);
            // Wellheads on the cellar deck above them: a valve stack on each.
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    var w = new Vector3(dxC + i * 2.6f, cellar, dzC + j * 2.6f);
                    frame.Tube(w, w + Vector3.up * 2.4f, 0.28f, 0.22f, 8, caps: true);
                    paint.Box(w + Vector3.up * 1.1f, new Vector3(0.8f, 0.5f, 0.8f));
                    pipe.Tube(w + new Vector3(0, 1.6f, 0), w + new Vector3(0.9f, 1.6f, 0), 0.1f, 0.1f, 6);
                }
            foreach (float y in new[] { midLevel, jacketTop })
            {
                foreach (float k in new[] { -1f, 1f })
                {
                    deck.Box(new Vector3(dxC, y, dzC + k * 3.4f), new Vector3(7.6f, 0.4f, 0.3f));
                    deck.Box(new Vector3(dxC + k * 3.4f, y, dzC), new Vector3(0.3f, 0.4f, 7.6f));
                }
                deck.Beam(new Vector3(dxC, y, dzC - 3.4f), new Vector3(dxC, y, JL(1, -1, y).z), 0.3f, 0.4f);
            }
            // Three export risers clamped down the front right leg.
            for (int i = -1; i <= 1; i++)
            {
                var off = new Vector3(2.0f, 0, i * 0.9f);
                var a = JL(1, 1, yLow) + off;
                var b = JL(1, 1, jacketTop) + off;
                Member(a, b, 0.3f, 8);
                frame.Tube(b, new Vector3(b.x, cellar - 0.3f, b.z), 0.3f, 0.3f, 8);
            }
            foreach (float y in new[] { midLevel, jacketTop - 0.8f })
                paint.Box(JL(1, 1, y) + new Vector3(1.4f, 0, 0), new Vector3(1.6f, 0.35f, 3.0f));

            // ---- Decks -----------------------------------------------------------------------------
            foreach (float y in new[] { cellar, main })
            {
                deck.Box(new Vector3(0, y - 0.15f, zMid), new Vector3(2 * DX, 0.3f, ZF - ZR));
                foreach (float x in new[] { -LX, LX })
                    deck.Box(new Vector3(x, y - 1.25f, zMid), new Vector3(1.0f, 1.9f, ZF - ZR));
                foreach (float x in new[] { -DX + 0.3f, -7.5f, 0, 7.5f, DX - 0.3f })
                    deck.Box(new Vector3(x, y - 0.8f, zMid), new Vector3(0.45f, 1.0f, ZF - ZR));
                for (int i = 0; i <= 8; i++)
                    deck.Box(new Vector3(0, y - 0.8f, Mathf.Lerp(ZR + 0.2f, ZF - 0.2f, i / 8f)), new Vector3(2 * DX, 1.0f, 0.4f));
            }
            // Columns and side bracing in the storey between cellar and main deck.
            int bays = 5;
            for (int i = 0; i <= bays; i++)
            {
                float z = Mathf.Lerp(ZR + 0.3f, ZF - 0.3f, (float)i / bays);
                foreach (float sx in new[] { -1f, 1f })
                {
                    frame.Box(new Vector3(sx * (DX - 0.3f), (cellar + main - 1) / 2, z), new Vector3(0.5f, main - 1 - cellar, 0.5f));
                    if (i < bays && i % 2 == 0)
                    {
                        float z1 = Mathf.Lerp(ZR + 0.3f, ZF - 0.3f, (float)(i + 1) / bays);
                        frame.Beam(new Vector3(sx * (DX - 0.3f), cellar, z), new Vector3(sx * (DX - 0.3f), main - 1, z1), 0.3f, 0.3f);
                        frame.Beam(new Vector3(sx * (DX - 0.3f), cellar, z1), new Vector3(sx * (DX - 0.3f), main - 1, z), 0.3f, 0.3f);
                    }
                }
            }
            foreach (float x in new[] { -11f, 0, 11f })
                frame.Box(new Vector3(x, (cellar + main - 1) / 2, ZF - 0.3f), new Vector3(0.5f, main - 1 - cellar, 0.5f));

            // ---- Cellar deck process plant ---------------------------------------------------------
            // Two horizontal separators on saddles, a vertical scrubber, compressor enclosures.
            void Vessel(Vector3 a, Vector3 b, float r)
            {
                frame.Tube(a, b, r, r, 18);
                var axis = (b - a).normalized;
                frame.Tube(b, b + axis * r * 0.45f, r, r * 0.45f, 18, caps: true);
                frame.Tube(a, a - axis * r * 0.45f, r, r * 0.45f, 18, caps: true);
            }
            foreach (var (x0, x1, z) in new[] { (-14f, -3f, ZF - 7), (3f, 14f, ZF - 16) })
            {
                Vessel(new Vector3(x0, cellar + 1.9f, z), new Vector3(x1, cellar + 1.9f, z), 1.4f);
                foreach (float x in new[] { x0 + 1.5f, x1 - 1.5f })
                    deck.Box(new Vector3(x, cellar + 0.25f, z), new Vector3(0.5f, 0.5f, 2.4f));
                // Nozzles and a relief valve on top.
                frame.Tube(new Vector3((x0 + x1) / 2, cellar + 3.2f, z), new Vector3((x0 + x1) / 2, cellar + 4.4f, z), 0.25f, 0.25f, 8, caps: true);
                frame.Tube(new Vector3(x0 + 2, cellar + 3.2f, z), new Vector3(x0 + 2, cellar + 3.8f, z), 0.4f, 0.4f, 8, caps: true);
            }
            frame.Tube(new Vector3(-9, cellar, ZR + 26), new Vector3(-9, cellar + 5.2f, ZR + 26), 1.1f, 1.1f, 16, caps: true);
            module.Box(new Vector3(10, cellar + 1.9f, ZR + 24), new Vector3(7, 3.8f, 4.5f));
            module.Box(new Vector3(13, cellar + 1.9f, ZR + 8), new Vector3(6, 3.8f, 4f));
            module.Box(new Vector3(-15, cellar + 1.9f, ZR + 6), new Vector3(5, 3.8f, 8f));

            // Pipe racks: one across the front of the cellar deck, one down the right side, one across
            // the back of the main deck, each a run of pipes on portal frames, flanged every few metres.
            void Rack(Vector3 a, Vector3 b, float y, int pipes, bool flanges)
            {
                var along = (b - a).normalized;
                var across = Vector3.Cross(Vector3.up, along);
                float len = Vector3.Distance(a, b);
                for (float d = 0; d <= len; d += 6)
                {
                    var p = a + along * d;
                    foreach (float k in new[] { -1f, 1f })
                        frame.Box(new Vector3(p.x, 0, p.z) + across * k * 1.1f + Vector3.up * ((y - 0.3f + a.y) / 2), new Vector3(0.25f, y - 0.3f - a.y, 0.25f));
                    frame.Beam(p + across * -1.25f + Vector3.up * (y - 0.3f - a.y), p + across * 1.25f + Vector3.up * (y - 0.3f - a.y), 0.25f, 0.3f);
                }
                for (int i = 0; i < pipes; i++)
                {
                    float r = new[] { 0.3f, 0.18f, 0.25f, 0.12f, 0.2f, 0.15f }[i % 6];
                    var off = across * Mathf.Lerp(-0.9f, 0.9f, pipes == 1 ? 0.5f : (float)i / (pipes - 1)) + Vector3.up * (y - a.y - 0.15f + r);
                    var mb = i % 3 == 1 ? pipe : frame;
                    mb.Tube(a + off, b + off, r, r, 8);
                    if (!flanges) continue;
                    for (float d = 3 + i; d < len - 1; d += 7)
                        mb.Tube(a + off + along * d, a + off + along * (d + 0.12f), r * 1.5f, r * 1.5f, 8, caps: true);
                }
            }
            Rack(new Vector3(-DX + 1, cellar, ZF - 2.5f), new Vector3(DX - 1, cellar, ZF - 2.5f), cellar + 4.6f, detail ? 6 : 3, detail);
            Rack(new Vector3(DX - 2.5f, cellar, ZR + 1), new Vector3(DX - 2.5f, cellar, ZF - 4), cellar + 4.0f, detail ? 5 : 2, detail);
            Rack(new Vector3(-DX + 7, main, ZR + 3), new Vector3(DX - 6, main, ZR + 3), main + 3.6f, detail ? 4 : 2, detail);
            if (detail)
            {
                // Risers and drains between the decks.
                for (int i = 0; i < 7; i++)
                {
                    var p = new Vector3(R(-DX + 2, DX - 2), 0, R(ZR + 2, ZF - 4));
                    var mb = i % 2 == 0 ? pipe : frame;
                    float r = R(0.1f, 0.22f);
                    mb.Tube(new Vector3(p.x, cellar, p.z), new Vector3(p.x, main - 0.3f, p.z), r, r, 8);
                    frame.Box(new Vector3(p.x, cellar + 1.2f, p.z), new Vector3(r * 3.2f, 0.5f, r * 3.2f));
                }
            }

            // ---- Living quarters and helideck -----------------------------------------------------
            float lqTop = top - 1.1f;
            float lqZ0 = -(hs - 1), lqZ1 = ZF - 0.5f, lqX = hs - 2;
            clad.Box(new Vector3(0, (main + lqTop) / 2, (lqZ0 + lqZ1) / 2), new Vector3(2 * lqX, lqTop - main, lqZ1 - lqZ0));
            // Two storeys of windows round all four sides, and a floor-line band.
            foreach (float wy in new[] { main + 1.9f, main + 5.0f })
            {
                for (float x = -lqX + 1.5f; x <= lqX - 1.4f; x += 2.4f)
                    foreach (float z in new[] { lqZ1 + 0.03f, lqZ0 - 0.03f })
                        glass.Box(new Vector3(x, wy, z), new Vector3(1.3f, 1.1f, 0.06f));
                for (float z = lqZ0 + 1.5f; z <= lqZ1 - 1.4f; z += 2.4f)
                    foreach (float x in new[] { lqX + 0.03f, -lqX - 0.03f })
                        glass.Box(new Vector3(x, wy, z), new Vector3(0.06f, 1.1f, 1.3f));
            }
            frame.Box(new Vector3(0, main + 3.4f, lqZ1 + 0.06f), new Vector3(2 * lqX + 0.24f, 0.25f, 0.12f));
            frame.Box(new Vector3(0, main + 3.4f, lqZ0 - 0.06f), new Vector3(2 * lqX + 0.24f, 0.25f, 0.12f));
            frame.Box(new Vector3(lqX + 0.06f, main + 3.4f, (lqZ0 + lqZ1) / 2), new Vector3(0.12f, 0.25f, lqZ1 - lqZ0));
            frame.Box(new Vector3(-lqX - 0.06f, main + 3.4f, (lqZ0 + lqZ1) / 2), new Vector3(0.12f, 0.25f, lqZ1 - lqZ0));

            // Helideck: a 0.4 m plate on a grid of girders, cantilevered at the front on struts.
            heli.Box(new Vector3(0, top - 0.2f, 0), new Vector3(2 * hs, 0.4f, 2 * hs));
            for (int i = 0; i <= 6; i++)
            {
                float u = Mathf.Lerp(-hs + 0.2f, hs - 0.2f, i / 6f);
                deck.Box(new Vector3(u, top - 0.75f, 0), new Vector3(0.35f, 0.7f, 2 * hs));
                deck.Box(new Vector3(0, top - 0.75f, u), new Vector3(2 * hs, 0.7f, 0.35f));
            }
            foreach (float x in new[] { -15f, -5f, 5f, 15f })
                paint.Tube(new Vector3(x, main + 0.3f, ZF - 0.3f), new Vector3(x, top - 1.1f, hs - 0.6f), 0.3f, 0.3f, 8);
            for (float z = lqZ0; z <= lqZ1; z += 6)
                foreach (float sx in new[] { -1f, 1f })
                    frame.Beam(new Vector3(sx * lqX, top - 3.2f, z), new Vector3(sx * (hs - 0.2f), top - 1.1f, z), 0.25f, 0.25f);

            // Markings to CAP 437 proportions for a 22 m D-value: yellow touchdown circle 11 m inside,
            // 1 m line; white H 4 × 6 m; and the edge line, yellow here, round the whole deck.
            float paintY = top + 0.006f;
            yellow.Annulus(new Vector3(0, top + 0.012f, 0), 5.5f, 6.5f, 64);
            white.Box(new Vector3(-1.625f, paintY, 0), new Vector3(0.75f, 0.012f, 6));
            white.Box(new Vector3(1.625f, paintY, 0), new Vector3(0.75f, 0.012f, 6));
            white.Box(new Vector3(0, paintY, 0), new Vector3(2.5f, 0.012f, 0.75f));
            yellow.Box(new Vector3(0, paintY, hs - 0.2f), new Vector3(2 * hs, 0.012f, 0.4f));
            yellow.Box(new Vector3(0, paintY, -hs + 0.2f), new Vector3(2 * hs, 0.012f, 0.4f));
            yellow.Box(new Vector3(hs - 0.2f, paintY, 0), new Vector3(0.4f, 0.012f, 2 * hs - 0.8f));
            yellow.Box(new Vector3(-hs + 0.2f, paintY, 0), new Vector3(0.4f, 0.012f, 2 * hs - 0.8f));
            // Deck name block and weight box: white panels at the back of the deck.
            white.Box(new Vector3(0, paintY, -hs + 2.2f), new Vector3(9, 0.012f, 2.2f));
            white.Box(new Vector3(-hs + 4.5f, paintY, -hs + 4.8f), new Vector3(2.6f, 0.012f, 1.6f));

            // The landing net: a 12 m square of 20 cm rope mesh over the touchdown circle, a heavier
            // edge rope round it, pegged at the corners and the middles of its sides.
            if (detail)
            {
                const float NetHalf = 6, Gap = 0.25f;
                float ny = top + 0.02f;
                for (float u = -NetHalf; u <= NetHalf + 0.01f; u += Gap)
                {
                    rope.Box(new Vector3(u, ny, 0), new Vector3(0.022f, 0.014f, 2 * NetHalf));
                    rope.Box(new Vector3(0, ny + 0.004f, u), new Vector3(2 * NetHalf, 0.014f, 0.022f));
                }
                foreach (float e in new[] { -NetHalf, NetHalf })
                {
                    rope.Box(new Vector3(e, ny + 0.01f, 0), new Vector3(0.05f, 0.035f, 2 * NetHalf + 0.05f));
                    rope.Box(new Vector3(0, ny + 0.01f, e), new Vector3(2 * NetHalf + 0.05f, 0.035f, 0.05f));
                    foreach (float f in new[] { -NetHalf, 0f, NetHalf })
                    {
                        frame.Cylinder(new Vector3(e, top, f), 0.09f, 0.05f, 8);
                        frame.Cylinder(new Vector3(f, top, e), 0.09f, 0.05f, 8);
                    }
                }
            }

            // Foam where the swell breaks round each leg at the waterline.
            foreach (var (sx, sf) in plan)
            {
                var c = JL(sx, sf, water);
                for (int i = 0; i < 14; i++)
                {
                    float a = R(0, 2 * Mathf.PI), d = R(1.2f, 3.4f);
                    foam.Mound(c + new Vector3(Mathf.Cos(a) * d, 0.02f, Mathf.Sin(a) * d), R(0.4f, 1.3f), R(0.3f, 0.9f), R(0.03f, 0.08f), rng, 0.45f, 2, 10);
                }
            }

            // Helideck stair: down the left side from a landing beside the deck to the main deck.
            float stairX = -(hs + 1.4f);
            float landZ0 = -1.2f, landZ1 = 0.6f;
            if (detail)
            {
                // Perimeter safety net 1.5 m wide just below deck level, on brackets, with green
                // perimeter lights; a gap at the stair landing, and none of it above the deck.
                var edges = new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
                foreach (var nrm in edges)
                {
                    var tan = new Vector3(nrm.z, 0, -nrm.x);
                    Vector3 P(float u, float o, float y) => nrm * (hs + o) + tan * u + Vector3.up * y;
                    bool stairEdge = nrm == Vector3.left;
                    // Along the left edge u runs along +z.
                    bool InGap(float u) => stairEdge && u > landZ0 - 0.2f && u < landZ1 + 0.2f;
                    for (float u = -hs; u <= hs + 0.01f; u += 0.4f)
                    {
                        if (InGap(u)) continue;
                        net.Beam(P(u, 0.05f, top - 0.12f), P(u, 1.5f, top - 0.32f), 0.025f, 0.025f);
                    }
                    void Run(float u0, float u1)
                    {
                        if (u1 - u0 < 0.2f) return;
                        for (int k = 0; k <= 3; k++)
                        {
                            float o = Mathf.Lerp(0.05f, 1.5f, k / 3f), y = Mathf.Lerp(top - 0.12f, top - 0.32f, k / 3f);
                            if (k == 3) frame.Beam(P(u0, o, y), P(u1, o, y), 0.07f, 0.07f);
                            else net.Beam(P(u0, o, y), P(u1, o, y), 0.025f, 0.025f);
                        }
                    }
                    if (stairEdge) { Run(-hs, landZ0 - 0.2f); Run(landZ1 + 0.2f, hs); }
                    else Run(-hs, hs);
                    for (float u = -hs; u <= hs + 0.01f; u += 3)
                    {
                        if (InGap(u)) continue;
                        frame.Beam(P(u, -0.3f, top - 0.9f), P(u, 1.5f, top - 0.34f), 0.08f, 0.08f);
                        // Front edge lights stop short of the firing line.
                        if (nrm == Vector3.forward && Mathf.Abs(u) < 3) continue;
                        green.Box(P(u, 0.18f, top - 0.02f), new Vector3(0.18f, 0.12f, 0.18f));
                    }
                }

                var landing = new Vector3(stairX, top, (landZ0 + landZ1) / 2);
                deck.Box(landing + Vector3.down * 0.05f, new Vector3(2 * (-hs - stairX), 0.1f, landZ1 - landZ0));
                float midY = (main + top) / 2;
                Steelwork.Flight(safety, new Vector3(stairX, main, landZ0 - 11), new Vector3(stairX, midY, landZ0 - 6.2f), 1.1f);
                deck.Box(new Vector3(stairX, midY - 0.05f, landZ0 - 5.6f), new Vector3(1.2f, 0.1f, 1.2f));
                Steelwork.Flight(safety, new Vector3(stairX, midY, landZ0 - 5), new Vector3(stairX, top, landZ0), 1.1f);
                Steelwork.Rail(safety, new Vector3(2 * stairX + hs, top, landZ0), new Vector3(2 * stairX + hs, top, landZ1));
                Steelwork.Rail(safety, new Vector3(2 * stairX + hs, top, landZ1), new Vector3(-hs, top, landZ1));
                foreach (float z in new[] { landZ0 - 5.6f, landZ1 })
                    frame.Box(new Vector3(2 * stairX + hs + 0.05f, (main + top) / 2, z), new Vector3(0.2f, top - main, 0.2f));

                // Outboard stairs on the left face: boat landing up to the cellar deck in four flights,
                // cellar deck to main deck in two.
                float sxA = -(DX + 0.75f), sxB = -(DX + 1.95f);
                float boatY = water + 3.5f;
                float zLo = ZR + 21.6f, zHi = zLo + 4.3f;
                float riseB = (cellar - boatY) / 4;
                for (int k = 0; k < 4; k++)
                {
                    bool up = k % 2 == 0;
                    float x = up ? sxA : sxB;
                    float y0 = boatY + k * riseB;
                    Steelwork.Flight(safety, new Vector3(x, y0, up ? zLo : zHi), new Vector3(x, y0 + riseB, up ? zHi : zLo), 1.0f);
                    float lz = up ? zHi + 0.5f : zLo - 0.5f;
                    deck.Box(new Vector3((sxA + sxB) / 2 + 0.1f, y0 + riseB - 0.05f, lz), new Vector3(2.6f, 0.1f, 1.0f));
                }
                deck.Box(new Vector3((sxA + sxB) / 2 + 0.1f, boatY - 0.05f, zLo - 0.5f), new Vector3(2.6f, 0.1f, 1.0f));
                foreach (float z in new[] { zLo - 1, zHi + 1 })
                    foreach (float x in new[] { sxA + 0.6f, sxB - 0.6f })
                        frame.Box(new Vector3(x, (boatY + cellar) / 2, z), new Vector3(0.2f, cellar - boatY, 0.2f));
                float zc0 = ZR + 29.5f, half = (main - cellar) / 2;
                Steelwork.Flight(safety, new Vector3(sxA, cellar, zc0), new Vector3(sxA, cellar + half, zc0 + 4.8f), 1.0f);
                deck.Box(new Vector3(sxA, cellar + half - 0.05f, zc0 + 5.4f), new Vector3(1.1f, 0.1f, 1.2f));
                Steelwork.Flight(safety, new Vector3(sxA, cellar + half, zc0 + 6), new Vector3(sxA, main, zc0 + 10.8f), 1.0f);
                deck.Box(new Vector3(sxA + 0.1f, cellar - 0.05f, zc0 - 0.6f), new Vector3(1.3f, 0.1f, 1.2f));
                deck.Box(new Vector3(sxA + 0.1f, main - 0.05f, zc0 + 11.4f), new Vector3(1.3f, 0.1f, 1.2f));
                foreach (float z in new[] { zc0 - 1.2f, zc0 + 5.4f, zc0 + 12f })
                    frame.Box(new Vector3(sxA - 0.6f, (cellar + main) / 2, z), new Vector3(0.18f, main - cellar, 0.18f));

                // Boat landing at the foot: a grating on cantilevers off the jacket with vertical fenders.
                float bx0 = sxB - 0.6f, bx1 = JL(-1, 1, boatY).x + 1.2f;
                deck.Box(new Vector3((bx0 + bx1) / 2, boatY - 0.1f, (zLo + zHi) / 2), new Vector3(bx1 - bx0, 0.2f, zHi - zLo + 3));
                for (float z = zLo - 1.4f; z <= zHi + 1.5f; z += 1.6f)
                {
                    paint.Tube(new Vector3(bx0 - 0.4f, water - 3, z), new Vector3(bx0 - 0.4f, boatY + 0.8f, z), 0.3f, 0.3f, 8, caps: true);
                    paint.Beam(new Vector3(bx0 - 0.2f, boatY - 0.2f, z), new Vector3(bx1, boatY - 0.2f, z), 0.25f, 0.3f);
                }
                Steelwork.Rail(safety, new Vector3(bx0, boatY, zHi + 1.5f), new Vector3(bx1, boatY, zHi + 1.5f));

                // Guard rails round both deck edges, broken where the stairs land.
                // Gaps are (from, to) distances along the edge from `a`, in order.
                void Edge(float y, Vector3 a, Vector3 b, params (float lo, float hi)[] gaps)
                {
                    var dir = (b - a).normalized;
                    float u = 0;
                    foreach (var (lo, hi) in gaps)
                    {
                        if (lo > u) Steelwork.Rail(safety, a + dir * u + Vector3.up * y, a + dir * lo + Vector3.up * y, 1.1f, true);
                        u = Mathf.Max(u, hi);
                    }
                    Steelwork.Rail(safety, a + dir * u + Vector3.up * y, b + Vector3.up * y, 1.1f, true);
                }
                foreach (float y in new[] { cellar, main })
                {
                    var c0 = new Vector3(-DX + 0.1f, 0, ZR + 0.1f);
                    var c1 = new Vector3(DX - 0.1f, 0, ZR + 0.1f);
                    var c2 = new Vector3(DX - 0.1f, 0, ZF - 0.1f);
                    var c3 = new Vector3(-DX + 0.1f, 0, ZF - 0.1f);
                    Edge(y, c0, c1);
                    Edge(y, c1, c2);
                    Edge(y, c2, c3);
                    // Along the left edge from the back: distances are z − c0.z.
                    if (y == cellar) Edge(y, c0, c3, (zLo - 1.0f - c0.z, zLo - c0.z), (zc0 - 1.2f - c0.z, zc0 - c0.z));
                    else Edge(y, c0, c3, (zc0 + 10.8f - c0.z, zc0 + 12f - c0.z));
                }
            }

            // ---- Main deck equipment ---------------------------------------------------------------
            // Containers, two high, on the back of the main deck and along the right side.
            void Container(Vector3 baseCentre, bool alongX)
            {
                var size = alongX ? new Vector3(6.06f, 2.59f, 2.44f) : new Vector3(2.44f, 2.59f, 6.06f);
                boxes[rng.Next(boxes.Length)].Box(baseCentre + Vector3.up * 1.295f, size);
            }
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 3; j++)
                {
                    var p = new Vector3(6.5f + i * 6.3f, main, ZR + 5.6f + j * 2.55f);
                    Container(p, true);
                    if (rng.NextDouble() < 0.55) Container(p + Vector3.up * 2.59f, true);
                }
            Container(new Vector3(DX - 2.6f, main, lqZ0 + 4), false);
            Container(new Vector3(DX - 2.6f, main + 2.59f, lqZ0 + 4.4f), false);
            Container(new Vector3(-DX + 2.6f, main, ZR + 12), false);

            // Lifeboats: two enclosed boats in davits off the right side.
            foreach (float z in new[] { lqZ1 - 8, lqZ1 - 16 })
            {
                float bx = DX + 1.9f, by = main + 1.0f;
                orange.Frustum(new Vector3(bx, by, z), new Vector2(1.5f, 6.4f), new Vector3(bx, by + 1.3f, z), new Vector2(2.9f, 8.0f));
                orange.Tube(new Vector3(bx, by + 1.3f, z - 3.3f), new Vector3(bx, by + 1.3f, z + 3.3f), 1.3f, 1.3f, 14, caps: true);
                white.Box(new Vector3(bx, by + 2.65f, z + 1.2f), new Vector3(1.1f, 0.5f, 1.6f));
                foreach (float dz in new[] { -3.3f, 3.3f })
                {
                    paint.Beam(new Vector3(DX - 0.3f, main, z + dz), new Vector3(DX - 0.3f, main + 5.0f, z + dz), 0.4f, 0.4f);
                    paint.Beam(new Vector3(DX - 0.3f, main + 5.0f, z + dz), new Vector3(bx + 0.2f, main + 5.4f, z + dz), 0.3f, 0.3f);
                    frame.Beam(new Vector3(bx, main + 5.3f, z + dz), new Vector3(bx, by + 2.4f, z + dz * 0.8f), 0.05f, 0.05f);
                }
            }

            // Drilling: substructure box frame under a drill floor, the derrick, a driller's cabin.
            var dc = new Vector3(dxC, top, dzC);
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                    frame.Box(new Vector3(dc.x + i * 5, (main + top) / 2, dc.z + j * 5), new Vector3(0.7f, top - main, 0.7f));
            foreach (float k in new[] { -1f, 1f })
            {
                frame.Beam(new Vector3(dc.x - 5, main, dc.z + k * 5), new Vector3(dc.x + 5, top - 0.5f, dc.z + k * 5), 0.4f, 0.4f);
                frame.Beam(new Vector3(dc.x + k * 5, main, dc.z - 5), new Vector3(dc.x + k * 5, top - 0.5f, dc.z + 5), 0.4f, 0.4f);
            }
            deck.Box(new Vector3(dc.x, top - 0.25f, dc.z), new Vector3(12, 0.5f, 12));
            float derrickTop = top + 42;
            Steelwork.Lattice(frame, dc + Vector3.up * 0.1f, new Vector3(dc.x, derrickTop, dc.z), 9, 2.6f, 0.32f, 0.14f, 14, Vector3.forward);
            frame.Box(new Vector3(dc.x, derrickTop + 1, dc.z), new Vector3(3.4f, 2, 3.4f));
            frame.Box(new Vector3(dc.x, top + 26, dc.z + 2.6f), new Vector3(3.2f, 0.15f, 1.4f));
            paint.Box(new Vector3(dc.x, top + 17, dc.z), new Vector3(1.2f, 2.4f, 0.8f));
            foreach (float k in new[] { -0.25f, 0.25f })
                frame.Beam(new Vector3(dc.x + k, top + 18.2f, dc.z), new Vector3(dc.x + k, derrickTop, dc.z), 0.04f, 0.04f);
            module.Box(new Vector3(dc.x + 4.4f, top + 1.4f, dc.z - 4.4f), new Vector3(3, 2.8f, 2.6f));
            glass.Box(new Vector3(dc.x + 2.88f, top + 1.9f, dc.z - 4.4f), new Vector3(0.06f, 1.0f, 2.0f));
            red.Box(new Vector3(dc.x, derrickTop + 2.2f, dc.z), new Vector3(0.45f, 0.45f, 0.45f));

            // Pedestal cranes: one working, boom raised over the side; one parked, boom low over the
            // stern. Pedestal, slewing house with cab, A-frame, lattice boom, luffing and hoist ropes.
            void Crane(Vector3 foot, Vector3 boomDir, float boomLen)
            {
                float pedTop = top + 2.5f;
                paint.Tube(foot, new Vector3(foot.x, pedTop, foot.z), 1.3f, 1.3f, 16, caps: true);
                var flat = new Vector3(boomDir.x, 0, boomDir.z).normalized;
                var side = Vector3.Cross(Vector3.up, flat);
                var house = new Vector3(foot.x, pedTop + 1.6f, foot.z);
                paint.Box(house - flat * 1.0f, new Vector3(3.6f, 3.2f, 3.6f), Quaternion.LookRotation(flat));
                white.Box(house + flat * 1.2f + side * 1.6f + Vector3.up * 0.3f, new Vector3(1.4f, 2.0f, 1.6f), Quaternion.LookRotation(flat));
                glass.Box(house + flat * 2.0f + side * 1.6f + Vector3.up * 0.6f, new Vector3(1.2f, 0.9f, 0.1f), Quaternion.LookRotation(flat));
                var pivot = house + flat * 1.6f + Vector3.up * -0.6f;
                var apex = house - flat * 0.6f + Vector3.up * 5.5f;
                foreach (float k in new[] { -1f, 1f })
                {
                    paint.Beam(house + flat * 1.4f + side * k * 1.4f + Vector3.up * 1.4f, apex + side * k * 0.4f, 0.3f, 0.3f);
                    paint.Beam(house - flat * 2.6f + side * k * 1.4f + Vector3.up * 1.4f, apex + side * k * 0.4f, 0.3f, 0.3f);
                }
                var tip = pivot + boomDir.normalized * boomLen;
                Steelwork.Lattice(paint, pivot, tip, 1.8f, 0.8f, 0.16f, 0.07f, Mathf.RoundToInt(boomLen / 2.4f), Vector3.up);
                foreach (float k in new[] { -1f, 1f })
                    frame.Beam(apex + side * k * 0.3f, tip + side * k * 0.2f + Vector3.up * 0.4f, 0.05f, 0.05f);
                var hook = tip + Vector3.down * Mathf.Min(14, tip.y - water - 4);
                frame.Beam(tip, hook, 0.05f, 0.05f);
                paint.Box(hook + Vector3.down * 0.5f, new Vector3(0.6f, 1.0f, 0.4f));
                red.Box(tip + Vector3.up * 0.6f, new Vector3(0.3f, 0.3f, 0.3f));
            }
            Crane(new Vector3(DX - 2.5f, main, -(hs + 3.5f)), new Vector3(0.78f, 0.42f, -0.46f), 36);
            Crane(new Vector3(-DX + 2.5f, main, ZR + 3.5f), new Vector3(-0.3f, 0.2f, -0.93f), 34);

            // Flare boom off the back right corner, angled up and away, with the burning tip.
            var boomFoot = new Vector3(DX - 1, main + 3, ZR + 1);
            var boomDirF = new Vector3(0.55f, 0.5f, -0.67f).normalized;
            var flareTip = boomFoot + boomDirF * 60;
            Steelwork.Lattice(frame, boomFoot, flareTip, 3.2f, 1.4f, 0.2f, 0.08f, 22, Vector3.up);
            frame.Beam(boomFoot + Vector3.down * 3, boomFoot + boomDirF * 12, 0.4f, 0.4f);
            paint.Tube(flareTip, flareTip + Vector3.up * 3, 0.45f, 0.45f, 10, caps: true);
            frame.Box(flareTip + Vector3.up * 0.2f, new Vector3(2.6f, 0.15f, 2.6f));
            // Wind blows toward world +x on the sea maps: lean the flame that way in this frame.
            var lean = root.InverseTransformDirection(Vector3.right);
            Offshore.Flame(root, flareTip + Vector3.up * 3, lean, detail ? 1 : 1.3f);

            // Telecoms mast on the back left corner.
            if (detail)
            {
                var mf = new Vector3(-DX + 2.5f, main, -(hs + 4));
                Steelwork.Lattice(frame, mf, mf + Vector3.up * 30, 1.4f, 0.6f, 0.1f, 0.05f, 16, Vector3.forward);
                foreach (float y in new[] { 22f, 27f })
                {
                    frame.Tube(mf + new Vector3(0.7f, y, 0), mf + new Vector3(1.1f, y, 0), 0.6f, 0.6f, 12, caps: true);
                    frame.Beam(mf + new Vector3(-1.5f, y + 1.5f, 0), mf + new Vector3(1.5f, y + 1.5f, 0), 0.06f, 0.06f);
                }
                red.Box(mf + Vector3.up * 30.3f, new Vector3(0.3f, 0.3f, 0.3f));
            }

            // Sodium deck lights under the main deck and along the quarters.
            for (float x = -DX + 3; x < DX; x += 7)
            {
                warm.Box(new Vector3(x, main - 1.4f, ZF + 0.1f), new Vector3(0.5f, 0.25f, 0.3f));
                warm.Box(new Vector3(x, cellar - 1.4f, ZF + 0.1f), new Vector3(0.5f, 0.25f, 0.3f));
            }
            foreach (float z in new[] { lqZ0 + 3, lqZ1 - 3 })
                foreach (float sx in new[] { -1f, 1f })
                    warm.Box(new Vector3(sx * (lqX + 0.2f), lqTop - 0.6f, z), new Vector3(0.3f, 0.25f, 0.5f));

            growth.Object("Jacket growth", growthMat, root);
            paint.Object("Jacket", paintMat, root);
            deck.Object("Deck steel", deckMat, root);
            frame.Object("Steelwork", frameMat, root);
            if (safety.VertexCount > 0) safety.Object("Rails and stairs", safetyMat, root);
            clad.Object("Quarters", cladMat, root);
            module.Object("Modules", moduleMat, root);
            glass.Object("Windows", glassMat, root, shadows: false);
            heli.Object("Helideck", heliMat, root);
            yellow.Object("Helideck yellow", yellowMat, root, shadows: false);
            white.Object("Helideck white", whiteMat, root, shadows: false);
            if (net.VertexCount > 0) net.Object("Helideck net", netMat, root, shadows: false);
            if (rope.VertexCount > 0) rope.Object("Landing net", Library.Plain(new Color(0.55f, 0.5f, 0.4f), 0.15f), root);
            foam.Object("Foam", Library.Plain(new Color(0.78f, 0.8f, 0.8f), 0.25f), root, shadows: false);
            pipe.Object("Pipes", pipeMat, root);
            orange.Object("Lifeboats", orangeMat, root);
            for (int i = 0; i < boxes.Length; i++)
                if (boxes[i].VertexCount > 0) boxes[i].Object("Containers " + i, Library.Surface("corrugated_iron_02", 2.5f, boxTints[i]), root);
            red.Object("Obstruction lights", Library.Glow(new Color(0.3f, 0.02f, 0.02f), new Color(4f, 0.25f, 0.15f)), root, shadows: false);
            if (green.VertexCount > 0) green.Object("Helideck lights", Library.Glow(new Color(0.05f, 0.3f, 0.08f), new Color(0.3f, 3.5f, 0.6f)), root, shadows: false);
            warm.Object("Deck lights", Library.Glow(new Color(0.4f, 0.3f, 0.15f), new Color(3.2f, 2.0f, 0.8f)), root, shadows: false);
        }

    }
}
