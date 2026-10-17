using System.Collections.Generic;
using SniperDuel.Ballistics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

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

        /// <summary>
        /// Drops the caches of models, materials and made textures (a map is being unloaded), so
        /// what only that map used can be freed; anything still in use keeps its own reference.
        /// </summary>
        public static void Forget()
        {
            Materials.Clear();
            Models.Clear();
            StreakedMaps.Clear();
            Converted.Clear();
            Repacked.Clear();
            Leaned.Clear();
        }

        static readonly Dictionary<Texture, Texture> Leaned = new Dictionary<Texture, Texture>();

        /// <summary>Textures made lighter by <see cref="Lean"/> since start-up, and the time it took.</summary>
        public static int LeanCount;
        public static double LeanMs;

        /// <summary>
        /// A model's picture made light on memory: the glTF import keeps them uncompressed (four
        /// bytes a pixel), so each is copied through the GPU at most `max` pixels a side and
        /// compressed to the GPU's block format, a quarter of the size or less. Already compressed
        /// textures are used as they are.
        /// </summary>
        public static Texture? Lean(Texture? src, int max = 1024)
        {
            if (src == null || !(src is Texture2D) || GraphicsFormatUtility.IsCompressedFormat(src.graphicsFormat) || src.width < 64 || src.height < 64) return src;
            if (Leaned.TryGetValue(src, out var done) && done != null) return done;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(src.graphicsFormat);
            float scale = Mathf.Min(1, (float)max / Mathf.Max(src.width, src.height));
            // Block compression wants sides in multiples of four.
            int w = Mathf.Max(4, Mathf.RoundToInt(src.width * scale / 4) * 4), h = Mathf.Max(4, Mathf.RoundToInt(src.height * scale / 4) * 4);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, !srgb)
            {
                name = src.name + " (lean)", wrapMode = src.wrapMode, filterMode = src.filterMode, anisoLevel = Mathf.Max(src.anisoLevel, 2),
            };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            Pack(tex);
            Leaned[src] = tex;
            LeanCount++;
            LeanMs += clock.Elapsed.TotalMilliseconds;
            return tex;
        }

        /// <summary>Builds a readable texture's mips, compresses it and lets go of its CPU copy.</summary>
        public static void Pack(Texture2D tex)
        {
            tex.Apply(true, false);
            tex.Compress(false);
            tex.Apply(false, true);
        }

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
            Pack(tex);
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

        static readonly Dictionary<Material, Material> Converted = new Dictionary<Material, Material>();

        static Texture? Tex(Material? m, string prop) => m != null && m.HasProperty(prop) ? m.GetTexture(prop) : null;

        /// <summary>
        /// A URP Lit material from an imported glTF one (the glTF materials draw nothing on some
        /// renderers): colour, normal and occlusion maps, the glTF metal/roughness map repacked
        /// into Unity's mask, and alpha-tested leaves, fringes and fences kept cut out.
        /// `metal` scales the metal (worn paint reads better than polished chrome under the sky),
        /// `smoothness` caps the shine (null keeps the model's own).
        /// </summary>
        public static Material Pbr(Material? src, float metal = 1, float? smoothness = null)
        {
            if (src != null && Converted.TryGetValue(src, out var done) && done != null && metal == 1 && smoothness == null) return done;
            bool cut = src != null && (src.IsKeywordEnabled("_ALPHATEST_ON") || src.HasProperty("alphaCutoff") && src.renderQueue >= 2450 && src.renderQueue < 3000);
            var m = new Material(Template(cut ? "LitCutoutMapped" : "Lit")) { name = "Pbr " + (src != null ? src.name : "") };
            var tex = Lean(Tex(src, "baseColorTexture") ?? Tex(src, "_BaseMap") ?? (src != null && src.HasProperty("_MainTex") ? src.mainTexture : null));
            m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
            var factor = src != null && src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : src != null && src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white;
            m.SetColor("_BaseColor", factor);
            var nor = Lean(Tex(src, "normalTexture") ?? Tex(src, "_BumpMap"));
            if (nor != null) m.SetTexture("_BumpMap", nor);
            float rough = src != null && src.HasProperty("roughnessFactor") ? src.GetFloat("roughnessFactor") : 0.7f;
            float metalFactor = src != null && src.HasProperty("metallicFactor") ? src.GetFloat("metallicFactor") : Tex(src, "metallicRoughnessTexture") != null ? 1 : 0;
            // glTF scales the map's metal by metallicFactor (often 0 on scanned rock and cloth).
            var mask = Repack(Tex(src, "metallicRoughnessTexture"), Tex(src, "occlusionTexture"), metal * metalFactor);
            if (mask != null)
            {
                m.SetTexture("_MetallicGlossMap", mask);
                m.SetTexture("_OcclusionMap", mask);
                m.SetFloat("_Smoothness", smoothness ?? 1);
                m.SetFloat("_Metallic", 1);
            }
            else
            {
                // Without a map the Lit template's metallic map reads as chrome: a flat one instead.
                m.SetTexture("_MetallicGlossMap", Flat(metalFactor * metal, smoothness ?? Mathf.Clamp01(1 - rough)));
                m.SetTexture("_OcclusionMap", Texture2D.whiteTexture);
                m.SetFloat("_Smoothness", 1);
                m.SetFloat("_Metallic", 1);
            }
            if (cut)
            {
                float cutoff = src != null && src.HasProperty("alphaCutoff") ? src.GetFloat("alphaCutoff") : 0.5f;
                m.SetFloat("_Cutoff", Mathf.Clamp(cutoff, 0.05f, 0.9f));
                m.SetFloat("_AlphaClip", 1);
                m.SetFloat("_Cull", 0);
                m.EnableKeyword("_ALPHATEST_ON");
            }
            if (src != null && metal == 1 && smoothness == null) Converted[src] = m;
            return m;
        }

        /// <summary>Converts every material under an object with <see cref="Pbr"/>.</summary>
        public static void PbrAll(GameObject go, float metal = 1, float? smoothness = null)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var mats = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) mats[i] = Pbr(src[i], metal, smoothness);
                r.sharedMaterials = mats;
            }
        }

        static readonly Dictionary<(Texture?, Texture?, float), Texture2D> Repacked = new Dictionary<(Texture?, Texture?, float), Texture2D>();

        /// <summary>
        /// glTF packs occlusion, roughness and metal in red, green and blue (occlusion sometimes in
        /// its own map); URP Lit reads metal from red, occlusion from green and smoothness from alpha.
        /// </summary>
        public static Texture2D? Repack(Texture? orm, Texture? occlusion = null, float metal = 1)
        {
            if (orm == null) return null;
            var key = (orm, occlusion, metal);
            if (Repacked.TryGetValue(key, out var done) && done != null) return done;
            var px = Pixels(orm, out int w, out int h);
            var ao = occlusion != null && occlusion != orm ? Pixels(occlusion, out int aw, out int ah, w, h) : null;
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                byte occ = ao != null ? ao[i].r : p.r;
                // Some exports leave occlusion black where there is none.
                if (ao == null && p.r == 0) occ = 255;
                px[i] = new Color32((byte)(p.b * metal), occ, 0, (byte)(255 - p.g));
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = orm.name + " (mask)", wrapMode = orm.wrapMode };
            tex.SetPixels32(px);
            Pack(tex);
            Repacked[key] = tex;
            return tex;
        }

        /// <summary>A texture's pixels through the GPU (imported textures aren't readable), at most 2048 square.</summary>
        static Color32[] Pixels(Texture src, out int w, out int h, int forceW = 0, int forceH = 0)
        {
            w = forceW > 0 ? forceW : Mathf.Min(src.width, 2048);
            h = forceH > 0 ? forceH : Mathf.Min(src.height, 2048);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            Object.Destroy(tex);
            return px;
        }

        static readonly Dictionary<(int, int), Texture2D> Flats = new Dictionary<(int, int), Texture2D>();

        /// <summary>A 4×4 mask map of one metal and smoothness.</summary>
        public static Texture2D Flat(float metallic, float smoothness)
        {
            var key = (Mathf.RoundToInt(metallic * 255), Mathf.RoundToInt(smoothness * 255));
            if (Flats.TryGetValue(key, out var done) && done != null) return done;
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32((byte)key.Item1, 255, 0, (byte)key.Item2);
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "Flat mask" };
            tex.SetPixels32(px);
            tex.Apply();
            Flats[key] = tex;
            return tex;
        }

        /// <summary>
        /// A downloaded model ready to place: skinned parts frozen into static meshes, every
        /// material on URP Lit (<see cref="Pbr"/>). An empty object when the model is missing.
        /// </summary>
        public static GameObject Prop(string id, Transform? parent = null, float metal = 1, float? smoothness = null)
        {
            var prefab = Model(id);
            var go = prefab != null ? Object.Instantiate(prefab) : new GameObject(id + " (missing)");
            go.name = id;
            if (parent != null) go.transform.SetParent(parent, false);
            if (prefab == null) return go;
            foreach (var a in go.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            Figure.Freeze(go);
            PbrAll(go, metal, smoothness);
            return go;
        }

        /// <summary>
        /// Wraps a model in a holder whose origin is the middle of the model's footprint at its
        /// base, scaled so its largest horizontal side (or its height, `byHeight`) is `size` metres.
        /// </summary>
        public static Transform Fit(GameObject model, float size, bool byHeight = false, Transform? parent = null)
        {
            var holder = new GameObject(model.name + " (fitted)").transform;
            if (parent != null) holder.SetParent(parent, false);
            model.transform.SetParent(holder, false);
            model.transform.localPosition = Vector3.zero;
            var b = BoundsOf(model);
            float now = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            if (now > 1e-4f) model.transform.localScale *= size / now;
            b = BoundsOf(model);
            model.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z) - holder.position;
            return holder;
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

        /// <summary>
        /// The separate things a model holds side by side, each a list of its renderers: renderers
        /// whose footprints overlap belong together (a tree's trunk and its branches), apart they are
        /// separate plants (a row of grass clumps, a stand of spruce). Scraps far shorter than the
        /// tallest are left out. One part when nothing stands apart.
        /// </summary>
        public static List<List<Renderer>> Parts(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var whole = new List<List<Renderer>> { new List<Renderer>(rs) };
            if (rs.Length < 2 || rs.Length > 600) return whole;
            var root = new int[rs.Length];
            for (int i = 0; i < rs.Length; i++) root[i] = i;
            int Find(int i) { while (root[i] != i) i = root[i] = root[root[i]]; return i; }
            for (int i = 0; i < rs.Length; i++)
            {
                var bi = rs[i].bounds;
                for (int j = i + 1; j < rs.Length; j++)
                {
                    var bj = rs[j].bounds;
                    if (bi.min.x <= bj.max.x && bj.min.x <= bi.max.x && bi.min.z <= bj.max.z && bj.min.z <= bi.max.z)
                        root[Find(i)] = Find(j);
                }
            }
            var groups = new Dictionary<int, List<Renderer>>();
            var order = new List<int>();
            for (int i = 0; i < rs.Length; i++)
            {
                int k = Find(i);
                if (!groups.TryGetValue(k, out var list)) { groups[k] = list = new List<Renderer>(); order.Add(k); }
                list.Add(rs[i]);
            }
            if (order.Count < 2 || order.Count > 24) return whole;
            float tallest = 0;
            foreach (var k in order) tallest = Mathf.Max(tallest, Height(groups[k]));
            var parts = new List<List<Renderer>>();
            foreach (var k in order) if (Height(groups[k]) >= tallest * 0.25f) parts.Add(groups[k]);
            return parts.Count < 2 ? whole : parts;
        }

        static float Height(List<Renderer> part)
        {
            var b = part[0].bounds;
            foreach (var r in part) b.Encapsulate(r.bounds);
            return b.size.y;
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
