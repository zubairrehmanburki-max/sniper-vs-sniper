using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// A city on the river banks: office and apartment blocks with window facades, densest in a
    /// downtown beyond the far bridge so it stands behind the enemy in the scope. Facades are a
    /// generated texture (window grid, lit windows glowing), so nothing needs downloading.
    /// </summary>
    public static class Skyline
    {
        const float BayM = 3f, FloorM = 3.4f;
        const int Bays = 4, Floors = 8;

        public static void Build(DuelMap map, Transform parent, float riverHalfWidth)
        {
            var root = new GameObject("Skyline").transform;
            root.SetParent(parent, false);
            var ground = map.Terrain ?? map.Ground;
            var rng = new System.Random(map.Id.GetHashCode() ^ 0x5eed);
            float lane = (float)map.LaneLength;

            var facades = new[] { Facade(rng, new Color(0.56f, 0.57f, 0.58f), 0.10f), Facade(rng, new Color(0.44f, 0.42f, 0.40f), 0.07f), Facade(rng, new Color(0.30f, 0.36f, 0.42f), 0.05f) };
            var builders = new[] { new MeshBuilder(), new MeshBuilder(), new MeshBuilder() };
            var roofs = new MeshBuilder();
            var beacons = new MeshBuilder();

            // Downtown clusters on both banks past the far bridge, then lower blocks spreading up and
            // down the river. Nothing stands on the water.
            void Block(float x, float s, float height, float width, float depth)
            {
                float y = (float)ground(x, -s);
                float yMin = y;
                foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
                    yMin = Mathf.Min(yMin, (float)ground(x + dx * width / 2, -(s + dz * depth / 2)));
                if (yMin < 2) return;
                // Heights snap to whole floors so the window grid ends cleanly at the roof.
                height = Mathf.Max(2, Mathf.Round(height / FloorM)) * FloorM;
                int kind = rng.Next(builders.Length);
                float baseY = yMin - 3;
                builders[kind].Box(new Vector3(x, baseY + (height + 3) / 2, s), new Vector3(width, height + 3, depth));
                float top = baseY + height + 3;
                // Parapet and rooftop plant.
                roofs.Box(new Vector3(x, top + 0.5f, s), new Vector3(width + 0.4f, 1f, depth + 0.4f));
                int plant = 1 + rng.Next(3);
                for (int i = 0; i < plant; i++)
                {
                    float px = x + ((float)rng.NextDouble() - 0.5f) * width * 0.6f, pz = s + ((float)rng.NextDouble() - 0.5f) * depth * 0.6f;
                    roofs.Box(new Vector3(px, top + 1.6f, pz), new Vector3(3 + (float)rng.NextDouble() * 5, 2.2f, 3 + (float)rng.NextDouble() * 4));
                }
                if (height > 70)
                {
                    roofs.Box(new Vector3(x, top + 9, s), new Vector3(0.6f, 18, 0.6f));
                    beacons.Box(new Vector3(x, top + 18.4f, s), new Vector3(0.9f, 0.9f, 0.9f));
                }
            }

            foreach (float side in new[] { -1f, 1f })
            {
                // Downtown: tall towers past the enemy's bridge.
                for (int i = 0; i < 46; i++)
                {
                    float s = lane + 280 + (float)rng.NextDouble() * 1300;
                    float x = side * (riverHalfWidth + 90 + (float)rng.NextDouble() * 650);
                    float near = 1 - Mathf.Clamp01((Mathf.Abs(x) - riverHalfWidth - 90) / 650);
                    float h = 30 + (float)rng.NextDouble() * 70 + near * (float)rng.NextDouble() * 90;
                    Block(x, s, h, 22 + (float)rng.NextDouble() * 24, 22 + (float)rng.NextDouble() * 24);
                }
                // Lower city all along both banks.
                for (int i = 0; i < 70; i++)
                {
                    float s = -1400 + (float)rng.NextDouble() * (lane + 1600);
                    float x = side * (riverHalfWidth + 120 + (float)rng.NextDouble() * 900);
                    // Keep the bridge approaches open.
                    if (map.Look.Structures != null && System.Array.Exists(map.Look.Structures, st => Mathf.Abs(s - (float)st.S) < 40)) continue;
                    Block(x, s, 12 + (float)rng.NextDouble() * 30, 18 + (float)rng.NextDouble() * 22, 14 + (float)rng.NextDouble() * 18);
                }
            }

            for (int k = 0; k < builders.Length; k++)
                if (builders[k].VertexCount > 0) builders[k].Object("Blocks " + k, facades[k], root);
            roofs.Object("Rooftops", Library.Plain(new Color(0.32f, 0.32f, 0.31f), 0.2f), root);
            var red = Library.Plain(new Color(0.3f, 0.02f, 0.02f), 0.6f);
            red.EnableKeyword("_EMISSION");
            red.SetColor("_EmissionColor", new Color(5f, 0.3f, 0.2f));
            if (beacons.VertexCount > 0) beacons.Object("Beacons", red, root, shadows: false);
        }

        /// <summary>A window-grid facade: wall colour, glazing with sky reflection, some windows lit.</summary>
        static Material Facade(System.Random rng, Color wall, float litShare)
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
                            bool window = x > Px * 0.15f && x < Px * 0.85f && y > Px * 0.25f && y < Px * 0.8f;
                            bool mullion = window && Mathf.Abs(x - Px / 2) < 1;
                            float grain = 0.93f + 0.07f * Mathf.PerlinNoise((bx * Px + x) * 0.3f, (fy * Px + y) * 0.3f);
                            Color c = window && !mullion ? (lit ? light * 0.8f : glass * (0.85f + 0.3f * y / Px)) : wall * grain;
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

            var mat = new Material(Library.Template("Lit")) { name = "Facade" };
            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.45f);
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", glow);
            mat.SetColor("_EmissionColor", Color.white * 1.6f);
            // MeshBuilder UVs are metres × 0.25: one texture repeat per Bays × Floors.
            var scale = new Vector2(4f / (Bays * BayM), 4f / (Floors * FloorM));
            mat.SetTextureScale("_BaseMap", scale);
            mat.SetTextureScale("_EmissionMap", scale);
            mat.mainTextureScale = scale;
            return mat;
        }
    }
}
