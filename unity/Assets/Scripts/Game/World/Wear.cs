using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Years of traffic on a road surface, laid over the asphalt as thin see-through decals: the
    /// darker polished wheel paths down each lane, oil drips between them, cut-in patch repairs and
    /// the black tar snakes of sealed cracks. Without them a scanned asphalt tile reads as one
    /// clean, endlessly repeated sheet.
    /// </summary>
    public static class Wear
    {
        const int N = 256;
        static Material? material;

        /// <summary>Which picture in the 2×2 atlas.</summary>
        enum Mark { Track, Oil, Patch, Tar }

        /// <summary>
        /// Wear over a straight road: it runs along x from −reach to reach at height `top`, between
        /// z = −halfWidth and halfWidth, in `lanes` lanes. `clear` keeps a stretch round x = 0 free
        /// of the bigger marks (the shooters' spot).
        /// </summary>
        public static void Road(float top, float reach, float halfWidth, int lanes, Transform parent, int seed, float clear = 0)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var mb = new MeshBuilder();
            float y = top + 0.05f;
            float laneW = 2 * halfWidth / lanes;
            for (int k = 0; k < lanes; k++)
            {
                float zc = -halfWidth + laneW * (k + 0.5f);
                // Wheel paths: long streaks with gaps, where the tyres have polished the stone dark.
                foreach (float off in new[] { -0.85f, 0.85f })
                {
                    float x = -reach + R() * 6;
                    while (x < reach)
                    {
                        float len = 8 + R() * 18;
                        float w = 0.55f + R() * 0.25f;
                        float z = zc + off + (R() - 0.5f) * 0.15f;
                        Quad(mb, Mark.Track, x, Mathf.Min(x + len, reach), z - w / 2, z + w / 2, y);
                        x += len + 1 + R() * 6;
                    }
                }
                // Oil drips down the middle of the lane.
                for (float x = -reach + R() * 8; x < reach; x += 3 + R() * 9)
                {
                    float r = 0.2f + R() * 0.55f;
                    float z = zc + (R() - 0.5f) * 0.6f;
                    Quad(mb, Mark.Oil, x - r * 1.6f, x + r * 1.6f, z - r, z + r, y + 0.002f);
                }
            }
            // Patch repairs and sealed cracks, a few per 100 m, clear of the spot.
            int patches = Mathf.RoundToInt(reach / 25);
            for (int i = 0; i < patches; i++)
            {
                float x = (R() * 2 - 1) * reach;
                if (Mathf.Abs(x) < clear) continue;
                float w = 1.2f + R() * 3.5f, d = 0.8f + R() * Mathf.Min(2.5f, halfWidth);
                float z = (R() * 2 - 1) * (halfWidth - d / 2);
                Quad(mb, Mark.Patch, x - w / 2, x + w / 2, z - d / 2, z + d / 2, y + 0.004f);
            }
            int tar = Mathf.RoundToInt(reach / 12);
            for (int i = 0; i < tar; i++)
            {
                float x = (R() * 2 - 1) * reach;
                if (Mathf.Abs(x) < clear * 0.5f) continue;
                float w = 2 + R() * 4, d = 1 + R() * 2.5f;
                float z = (R() * 2 - 1) * (halfWidth - d / 2);
                Quad(mb, Mark.Tar, x - w / 2, x + w / 2, z - d / 2, z + d / 2, y + 0.006f);
            }
            if (mb.VertexCount > 0) mb.Object("Road wear", Mat(), parent, shadows: false);
        }

        static void Quad(MeshBuilder mb, Mark mark, float x0, float x1, float z0, float z1, float y)
        {
            int m = (int)mark;
            var lo = new Vector2((m % 2) * 0.5f, (m / 2) * 0.5f);
            // Half a texel in from the cell edge so the neighbouring picture never bleeds in.
            const float Inset = 1f / N;
            mb.Face(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0),
                lo + Vector2.one * Inset, lo + Vector2.one * (0.5f - Inset));
        }

        static Material Mat()
        {
            if (material != null) return material;
            material = new Material(Library.Template("LitDecal")) { name = "Road wear" };
            var tex = Atlas();
            material.SetTexture("_BaseMap", tex);
            material.mainTexture = tex;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.42f);
            material.SetFloat("_Metallic", 0);
            return material;
        }

        /// <summary>The four pictures, each a quarter of a 256-pixel atlas: near-black, coverage in the alpha.</summary>
        static Texture2D Atlas()
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Road wear", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            var px = new Color32[N * N];
            const int H = N / 2;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int cell = (x / H) + 2 * (y / H);
                    // u runs along the road (the quad's x), v across it, both 0–1 in the cell.
                    float u = (x % H + 0.5f) / H, v = (y % H + 0.5f) / H;
                    float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.6f + Mathf.PerlinNoise(x * 0.31f + 11, y * 0.31f + 7) * 0.4f;
                    float a;
                    Color c;
                    switch ((Mark)cell)
                    {
                        case Mark.Track:
                            // Soft across the path, fading in and out at the ends, broken up along it.
                            float across = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.28f, 2));
                            float ends = Mathf.Clamp01(Mathf.Min(u, 1 - u) * 6);
                            a = 0.32f * across * ends * Mathf.Lerp(0.5f, 1.2f, n);
                            c = new Color(0.04f, 0.04f, 0.045f);
                            break;
                        case Mark.Oil:
                            {
                                float du = (u - 0.5f) * 2, dv = (v - 0.5f) * 2;
                                float r = Mathf.Sqrt(du * du + dv * dv) + (n - 0.5f) * 0.45f;
                                a = Mathf.Clamp01((1 - r) * 2.5f) * 0.55f;
                                c = new Color(0.03f, 0.03f, 0.035f);
                                break;
                            }
                        case Mark.Patch:
                            {
                                // A cut-in rectangle with a ragged rim and a darker sealed joint round it.
                                float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v)) + (n - 0.5f) * 0.05f;
                                a = edge < 0.02f ? 0 : edge < 0.05f ? 0.75f : 0.45f;
                                c = edge < 0.05f ? new Color(0.03f, 0.03f, 0.03f) : new Color(0.11f, 0.11f, 0.115f) * Mathf.Lerp(0.85f, 1.15f, n);
                                break;
                            }
                        default:
                            {
                                // Two wandering sealed cracks.
                                float w1 = 0.5f + 0.25f * Mathf.Sin(u * 9 + n * 3) + 0.1f * (n - 0.5f);
                                float w2 = 0.3f + 0.6f * u + 0.08f * Mathf.Sin(u * 23);
                                float d = Mathf.Min(Mathf.Abs(v - w1), Mathf.Abs(v - w2) + (u < 0.3f ? 1 : 0));
                                float fade = Mathf.Clamp01(Mathf.Min(u, 1 - u) * 8);
                                a = Mathf.Clamp01(1 - d / 0.03f) * 0.85f * fade;
                                c = new Color(0.02f, 0.02f, 0.02f);
                                break;
                            }
                    }
                    c.a = Mathf.Clamp01(a);
                    px[y * N + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }
    }
}
