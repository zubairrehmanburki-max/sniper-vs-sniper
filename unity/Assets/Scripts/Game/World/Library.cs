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

        public static Material Plain(Color color, float smoothness = 0.3f, float metallic = 0)
        {
            var mat = new Material(Template("LitPlain"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
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
