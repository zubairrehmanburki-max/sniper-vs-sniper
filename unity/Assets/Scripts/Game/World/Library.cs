using System.Collections.Generic;
using SniperDuel.Ballistics;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Runtime access to the downloaded assets (Resources/TP) and the material templates the build
    /// prepares (Resources/Materials). Everything degrades to a plain material or an empty object if
    /// a file is missing, so a partial asset set still runs.
    /// </summary>
    public static class Library
    {
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, GameObject?> Models = new Dictionary<string, GameObject?>();

        /// <summary>Rules coordinates (z = −lane distance) to Unity (z = lane distance).</summary>
        public static Vector3 ToUnity(Vec3 p) => new Vector3((float)p.X, (float)p.Y, (float)-p.Z);
        public static Vector3 ToUnity(double x, double y, double z) => new Vector3((float)x, (float)y, (float)-z);
        public static Vec3 ToRules(Vector3 p) => new Vec3(p.x, p.y, -p.z);

        public static Material Template(string name)
        {
            var m = Resources.Load<Material>("Materials/" + name);
            if (m != null) return m;
            Debug.LogWarning($"[Library] missing material template {name}");
            return new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        }

        /// <summary>A PBR material from a Poly Haven texture set, tiled every `tileMetres`.</summary>
        public static Material Surface(string id, float tileMetres, Color? tint = null, float smoothnessScale = 1)
        {
            string key = $"{id}/{tileMetres}/{tint}/{smoothnessScale}";
            if (Materials.TryGetValue(key, out var cached)) return cached;
            var diff = Load<Texture2D>($"TP/Textures/{id}/{id}_diff_2k");
            var nor = Load<Texture2D>($"TP/Textures/{id}/{id}_nor_gl_2k");
            var mask = Load<Texture2D>($"Generated/{id}_mask");
            var mat = new Material(Template(diff != null ? "Lit" : "LitPlain")) { name = id };
            if (diff != null) mat.SetTexture("_BaseMap", diff);
            if (nor != null) mat.SetTexture("_BumpMap", nor);
            if (mask != null)
            {
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.SetTexture("_OcclusionMap", mask);
                mat.SetFloat("_Smoothness", smoothnessScale);
                mat.SetFloat("_Metallic", 1);
            }
            else mat.SetFloat("_Smoothness", 0.2f);
            mat.SetColor("_BaseColor", tint ?? Color.white);
            // UVs from MeshBuilder are in metres × 0.25, so this scale tiles every `tileMetres`.
            var scale = Vector2.one * (4f / tileMetres);
            mat.SetTextureScale("_BaseMap", scale);
            mat.mainTextureScale = scale;
            Materials[key] = mat;
            return mat;
        }

        /// <summary>
        /// A Surface whose colour map carries years of weather: rain streaks running down from the
        /// top of each tile, darker where the water ran longest, and blotchy grime between them.
        /// Built once per texture at load (the scan is copied through the GPU, darkened on the CPU).
        /// MeshBuilder walls map v to height, so the streaks hang straight down.
        /// </summary>
        public static Material Weathered(string id, float tileMetres, Color tint, float strength = 1)
        {
            string key = $"weathered/{id}/{tileMetres}/{tint}/{strength}";
            if (Materials.TryGetValue(key, out var cached)) return cached;
            var mat = new Material(Surface(id, tileMetres, tint)) { name = id + " (weathered)" };
            var diff = mat.GetTexture("_BaseMap");
            if (diff != null)
            {
                mat.SetTexture("_BaseMap", Streaked(diff, id.GetHashCode() & 0x7fffffff, strength));
                mat.SetTextureScale("_BaseMap", Vector2.one * (4f / tileMetres));
            }
            Materials[key] = mat;
            return mat;
        }

        static readonly Dictionary<string, Texture2D> StreakedMaps = new Dictionary<string, Texture2D>();

        static Texture2D Streaked(Texture src, int seed, float strength)
        {
            string key = $"{src.name}/{strength}";
            if (StreakedMaps.TryGetValue(key, out var done) && done != null) return done;
            int w = Mathf.Min(src.width, 2048), h = Mathf.Min(src.height, 2048);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = src.name + " (weathered)", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);

            // How dark each pixel goes, 0..1: streaks first, then blotches.
            var dark = new float[w * h];
            var rng = new System.Random(seed);
            for (int k = 0; k < 70; k++)
            {
                float cx = (float)rng.NextDouble() * w;
                float half = w * (0.004f + 0.022f * (float)rng.NextDouble());
                int top = rng.Next(h);
                int len = (int)(h * (0.25f + 0.7f * (float)rng.NextDouble()));
                float depth = 0.25f + 0.55f * (float)rng.NextDouble();
                float wobble = (float)rng.NextDouble() * 10;
                for (int dy = 0; dy < len; dy++)
                {
                    int y = ((top - dy) % h + h) % h;
                    float t = (float)dy / len;
                    // Strongest just under the source, thinning out and fading toward the tail.
                    float along = depth * Mathf.Clamp01(t * 12) * (1 - t * t);
                    float wide = half * (0.6f + 0.4f * Mathf.Sin(dy * 0.02f + wobble) + 0.5f * t);
                    float c = cx + Mathf.Sin(dy * 0.011f + wobble) * w * 0.003f;
                    for (int x = (int)(c - wide) - 1; x <= (int)(c + wide) + 1; x++)
                    {
                        float edge = 1 - Mathf.Abs(x + 0.5f - c) / wide;
                        if (edge <= 0) continue;
                        int i = y * w + ((x % w) + w) % w;
                        dark[i] = Mathf.Max(dark[i], along * Mathf.SmoothStep(0, 1, edge));
                    }
                }
            }
            // Blotchy grime: value noise on a coarse grid that wraps with the tile.
            const int G = 12;
            var grid = new float[G * G];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)rng.NextDouble();
            float Noise(float u, float v)
            {
                float gx = u * G, gy = v * G;
                int x0 = (int)gx, y0 = (int)gy;
                float fx = Mathf.SmoothStep(0, 1, gx - x0), fy = Mathf.SmoothStep(0, 1, gy - y0);
                float G2(int x, int y) => grid[(y % G) * G + (x % G)];
                return Mathf.Lerp(Mathf.Lerp(G2(x0, y0), G2(x0 + 1, y0), fx), Mathf.Lerp(G2(x0, y0 + 1), G2(x0 + 1, y0 + 1), fx), fy);
            }
            var px = tex.GetPixels32();
            for (int y = 0; y < h; y++)
            {
                float v = (float)y / h;
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float blot = Noise((float)x / w, v);
                    float m = 1 - strength * (0.42f * dark[i] + 0.16f * Mathf.Clamp01(blot * 1.6f - 0.5f));
                    // Run-off tints the stains a little warm (rust and silt), the rest stays as scanned.
                    var p = px[i];
                    px[i] = new Color32((byte)(p.r * (m * 0.93f + 0.07f)), (byte)(p.g * (m * 0.97f + 0.03f)), (byte)(p.b * m), p.a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            StreakedMaps[key] = tex;
            return tex;
        }

        /// <summary>
        /// Bare rock for boulders, outcrops and far hills: the first scanned rock set that was
        /// downloaded (rocky_terrain_02 is grassy ground, not rock).
        /// </summary>
        public static Material Rock(float tileMetres, Color? tint = null)
        {
            foreach (var id in new[] { "rock_boulder_dry", "cliff_side", "aerial_rocks_02" })
                if (Load<Texture2D>($"TP/Textures/{id}/{id}_diff_2k") != null) return Surface(id, tileMetres, tint);
            return Surface("aerial_rocks_02", tileMetres, tint);
        }

        public static Material Plain(Color color, float smoothness = 0.3f, float metallic = 0)
        {
            var mat = new Material(Template("LitPlain"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            return mat;
        }

        /// <summary>A self-lit colour for lamps and warning lights (HDR, so it blooms).</summary>
        public static Material Glow(Color color, Color emission)
        {
            var mat = new Material(Template("LitEmissive"));
            mat.SetColor("_BaseColor", color);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            return mat;
        }

        public static T? Load<T>(string path) where T : Object
        {
            var a = Resources.Load<T>(path);
            return a;
        }

        /// <summary>The imported glTF prefab for a Poly Haven model (any resolution).</summary>
        public static GameObject? Model(string id)
        {
            if (Models.TryGetValue(id, out var m)) return m;
            foreach (var res in new[] { "1k", "2k", "4k" })
            {
                m = Resources.Load<GameObject>($"TP/Models/{id}/{id}_{res}");
                if (m != null) break;
            }
            if (m == null) Debug.LogWarning($"[Library] missing model {id}");
            Models[id] = m;
            return m;
        }

        /// <summary>Instantiates a model; returns an empty object when the model is missing.</summary>
        public static GameObject Place(string id, Vector3 position, float yawDeg = 0, float scale = 1, Transform? parent = null)
        {
            var prefab = Model(id);
            var go = prefab != null ? Object.Instantiate(prefab) : new GameObject(id + " (missing)");
            go.name = id;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yawDeg, 0);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        /// <summary>World-space bounds of everything rendered under an object.</summary>
        public static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>Moves an object so the bottom of its bounds sits at y.</summary>
        public static void Ground(GameObject go, float y)
        {
            var b = BoundsOf(go);
            go.transform.position += new Vector3(0, y - b.min.y, 0);
        }

        public static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
