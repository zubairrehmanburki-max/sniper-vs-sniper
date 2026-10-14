using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SniperDuel.EditorTools
{
    /// <summary>
    /// Build-time preparation of the downloaded assets, run before every build:
    /// - material templates in Resources/Materials, so the shaders (with the keywords the game
    ///   uses) are included in the player; the game copies them and plugs in textures;
    /// - URP mask maps (metal, occlusion, smoothness) from Poly Haven's AO/rough/metal maps;
    /// - a tiling water normal map;
    /// - where the sun is in each sky photo, so the scene's light can line up with it.
    /// </summary>
    public static class Prepare
    {
        const string Tp = "Assets/Resources/TP";
        const string Gen = "Assets/Resources/Generated";
        const string Mats = "Assets/Resources/Materials";

        public static void All()
        {
            Directory.CreateDirectory(Gen);
            Directory.CreateDirectory(Mats);
            MaskMaps();
            WaterNormal();
            AssetDatabase.Refresh();
            SkySuns();
            Materials();
            AssetDatabase.SaveAssets();
        }

        static void Materials()
        {
            Template("Lit", "Universal Render Pipeline/Lit", "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP");
            Template("LitPlain", "Universal Render Pipeline/Lit");
            // Emissive variants (lit windows, warning lights) are stripped unless a material uses them.
            Template("LitEmissive", "Universal Render Pipeline/Lit", "_EMISSION");
            // City facades: smoothness from the albedo's alpha, so glass and stone share one material.
            var facade = Template("LitFacade", "Universal Render Pipeline/Lit", "_EMISSION", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            if (facade != null)
            {
                facade.SetFloat("_SmoothnessTextureChannel", 1);
                EditorUtility.SetDirty(facade);
            }
            var water = Template("Water", "Universal Render Pipeline/Lit", "_NORMALMAP", "_DETAIL_MULX2");
            if (water != null)
            {
                water.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/water_normal.png"));
                water.SetTexture("_DetailNormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/water_normal.png"));
                water.SetFloat("_Smoothness", 0.93f);
                water.SetFloat("_Metallic", 0f);
                water.SetColor("_BaseColor", new Color(0.05f, 0.11f, 0.12f));
                EditorUtility.SetDirty(water);
            }
            var cloth = Template("Cloth", "Universal Render Pipeline/Lit");
            if (cloth != null)
            {
                cloth.SetFloat("_Cull", 0f);
                cloth.doubleSidedGI = true;
                EditorUtility.SetDirty(cloth);
            }
            Template("Terrain", "Universal Render Pipeline/Terrain/Lit", "_NORMALMAP", "_MASKMAP");
            Template("Unlit", "Universal Render Pipeline/Unlit");
            var add = Template("Additive", "Universal Render Pipeline/Particles/Unlit", "_SURFACE_TYPE_TRANSPARENT", "_BLENDMODE_ADD");
            if (add != null)
            {
                add.SetFloat("_Surface", 1);
                add.SetFloat("_Blend", 2);
                add.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                add.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                add.SetFloat("_ZWrite", 0);
                add.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(add);
            }
            var alpha = Template("Fade", "Universal Render Pipeline/Particles/Unlit", "_SURFACE_TYPE_TRANSPARENT");
            if (alpha != null)
            {
                alpha.SetFloat("_Surface", 1);
                alpha.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                alpha.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                alpha.SetFloat("_ZWrite", 0);
                alpha.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(alpha);
            }
            // One sky material per downloaded photo.
            if (Directory.Exists(Tp + "/Hdris"))
            {
                foreach (var exr in Directory.GetFiles(Tp + "/Hdris", "*.exr", SearchOption.AllDirectories))
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(exr.Replace('\\', '/'));
                    if (tex == null) continue;
                    string id = Path.GetFileName(Path.GetDirectoryName(exr))!;
                    var sky = Template("Sky-" + id, "Skybox/Panoramic");
                    if (sky == null) continue;
                    sky.SetTexture("_MainTex", tex);
                    sky.SetFloat("_Mapping", 1);
                    sky.SetFloat("_ImageType", 0);
                    sky.SetFloat("_Exposure", 1);
                    EditorUtility.SetDirty(sky);
                }
            }
        }

        static Material? Template(string name, string shaderName, params string[] keywords)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[Prepare] shader not found: {shaderName}");
                return null;
            }
            string path = $"{Mats}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            foreach (var k in keywords) mat.EnableKeyword(k);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Poly Haven ARM (R occlusion, G roughness, B metal) → URP mask (R metal, G occlusion, A smoothness).</summary>
        static void MaskMaps()
        {
            if (!Directory.Exists(Tp + "/Textures")) return;
            foreach (var arm in Directory.GetFiles(Tp + "/Textures", "*_arm_*.jpg", SearchOption.AllDirectories))
            {
                string path = arm.Replace('\\', '/');
                string id = Path.GetFileName(Path.GetDirectoryName(path))!;
                string outPath = $"{Gen}/{id}_mask.png";
                if (File.Exists(outPath)) continue;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!tex.LoadImage(File.ReadAllBytes(path))) continue;
                var px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    px[i] = new Color32(c.b, c.r, 0, (byte)(255 - c.g));
                }
                // LoadImage turns a JPEG into RGB24, which would drop the smoothness alpha (every
                // surface then reads as mirror-smooth), so write the mask from a fresh RGBA texture.
                var mask = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, true);
                mask.SetPixels32(px);
                File.WriteAllBytes(outPath, mask.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        /// <summary>Tiling wave normals: a sum of directional sine waves, seamless over the tile.</summary>
        static void WaterNormal()
        {
            string outPath = Gen + "/water_normal.png";
            if (File.Exists(outPath)) return;
            const int N = 512;
            var rng = new System.Random(7);
            var waves = new List<(int kx, int ky, float amp, float phase)>();
            for (int i = 0; i < 28; i++)
            {
                int kx = rng.Next(-14, 15), ky = rng.Next(-14, 15);
                if (kx == 0 && ky == 0) continue;
                float k = Mathf.Sqrt(kx * kx + ky * ky);
                waves.Add((kx, ky, 1f / Mathf.Pow(k, 1.3f), (float)rng.NextDouble() * Mathf.PI * 2));
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = 0, dy = 0;
                    foreach (var (kx, ky, amp, phase) in waves)
                    {
                        float a = 2 * Mathf.PI * (kx * x + ky * y) / N + phase;
                        float c = Mathf.Cos(a) * amp;
                        dx += c * kx;
                        dy += c * ky;
                    }
                    var n = new Vector3(-dx * 0.09f, -dy * 0.09f, 1).normalized;
                    px[y * N + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255), (byte)((n.y * 0.5f + 0.5f) * 255), (byte)((n.z * 0.5f + 0.5f) * 255), 255);
                }
            }
            tex.SetPixels32(px);
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>Finds the brightest spot of each sky photo and saves its position (u, v) to Generated/sky-suns.json.</summary>
        static void SkySuns()
        {
            if (!Directory.Exists(Tp + "/Hdris")) return;
            var lines = new List<string>();
            foreach (var exr in Directory.GetFiles(Tp + "/Hdris", "*.exr", SearchOption.AllDirectories))
            {
                string path = exr.Replace('\\', '/');
                string id = Path.GetFileName(Path.GetDirectoryName(path))!;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                bool readable = importer.isReadable;
                var compression = importer.textureCompression;
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                float best = -1, bu = 0.5f, bv = 0.7f;
                if (tex != null)
                {
                    int w = tex.width, h = tex.height, step = Math.Max(1, w / 1024);
                    // Only the upper half: a bright reflection in water below the horizon isn't the sun.
                    for (int y = h / 2; y < h; y += step)
                    {
                        for (int x = 0; x < w; x += step)
                        {
                            var c = tex.GetPixel(x, y);
                            float l = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                            if (l > best) { best = l; bu = (x + 0.5f) / w; bv = (y + 0.5f) / h; }
                        }
                    }
                }
                lines.Add($"  \"{id}\": {{ \"u\": {bu.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"v\": {bv.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"peak\": {best.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}");
                importer.isReadable = readable;
                importer.textureCompression = compression;
                importer.SaveAndReimport();
                Debug.Log($"[Prepare] sky {id}: sun at u={bu:0.000} v={bv:0.000} (peak {best:0.0})");
            }
            File.WriteAllText(Gen + "/sky-suns.json", "{\n" + string.Join(",\n", lines) + "\n}\n");
            AssetDatabase.ImportAsset(Gen + "/sky-suns.json");
        }
    }
}
