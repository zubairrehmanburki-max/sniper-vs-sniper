using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Cheap stand-ins for scanned plants seen from afar. The scans run to a million and more
    /// triangles each (island_tree_01 is 1.6 M), far too many to plant by the hundred on a phone, so
    /// each one is photographed once, lit by the map's own sun and sky, into a cut-out picture; a
    /// field of them is then three crossed quads per plant, merged into one mesh per kind.
    /// The picture's alpha comes from shooting it twice, on black and on white: wherever the
    /// background shows through, the two shots differ. That needs nothing from the render
    /// texture's alpha channel.
    /// </summary>
    public static class Impostor
    {
        const int Layer = 29;
        const int Size = 512;

        /// <summary>One plant's picture and how it stands: the quad's side and where its foot sits.</summary>
        public sealed class Card
        {
            public Material Mat = null!;
            /// <summary>The quad's side in metres at scale 1.</summary>
            public float Side;
            /// <summary>The quad's bottom edge relative to the model's lowest point (≤ 0).</summary>
            public float Foot;
        }

        static readonly Dictionary<string, Card?> Cards = new Dictionary<string, Card?>();

        /// <summary>The picture of a model, made on first use; null when the model or the shot is missing.</summary>
        public static Card? Of(string modelId)
        {
            if (Cards.TryGetValue(modelId, out var done)) return done;
            Card? card = null;
            try { card = Shoot(modelId); }
            catch (System.Exception e) { Debug.LogWarning($"[Impostor] {modelId}: {e.Message}"); }
            Cards[modelId] = card;
            return card;
        }

        static Card? Shoot(string modelId)
        {
            if (Library.Model(modelId) == null) return null;
            var stage = new GameObject("Impostor stage " + modelId);
            stage.transform.position = new Vector3(0, -3000, 0);
            var model = Library.Place(modelId, Vector3.zero, 0, 1, stage.transform);
            Library.SetLayer(model, Layer);
            var b = Library.BoundsOf(model);
            if (b.size.y < 0.05f)
            {
                Object.Destroy(stage);
                return null;
            }

            // Shot from the sun's side so the picture is the plant in full light.
            var sun = RenderSettings.sun;
            var fromSun = sun != null ? -sun.transform.forward : new Vector3(0.5f, 0.6f, -0.6f);
            var look = Vector3.ProjectOnPlane(-fromSun, Vector3.up);
            if (look.sqrMagnitude < 1e-4f) look = Vector3.forward;
            look.Normalize();
            float half = 0.52f * Mathf.Max(b.size.y, Mathf.Max(b.size.x, b.size.z));
            var centre = new Vector3(b.center.x, b.min.y + half, b.center.z);

            var camGo = new GameObject("Impostor camera");
            camGo.transform.SetParent(stage.transform, false);
            camGo.transform.position = centre - look * (half * 4 + 10);
            camGo.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = half;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = half * 8 + 20;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.enabled = false;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.renderShadows = false;
            }
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "Impostor " + modelId };
            cam.targetTexture = rt;

            var black = Grab(cam, rt, Color.black);
            var white = Grab(cam, rt, Color.white);
            cam.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(stage);

            // Alpha from the difference; colour un-premultiplied from the black shot. Clear pixels
            // take the plant's mean colour so mipmaps don't fringe it with black or white.
            var px = new Color32[Size * Size];
            var mean = Color.black;
            int solid = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color k = black[i], w = white[i];
                float a = 1 - ((w.r - k.r) + (w.g - k.g) + (w.b - k.b)) / 3;
                a = Mathf.Clamp01(a);
                var c = a > 0.02f ? new Color(k.r / a, k.g / a, k.b / a) : Color.black;
                px[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), a);
                if (a > 0.5f)
                {
                    mean += c;
                    solid++;
                }
            }
            if (solid < Size * Size / 200)
            {
                Debug.LogWarning($"[Impostor] {modelId}: the shot came out empty ({solid} px)");
                return null;
            }
            mean /= solid;
            var fill = (Color32)new Color(mean.r, mean.g, mean.b, 0);
            for (int i = 0; i < px.Length; i++) if (px[i].a < 8) px[i] = fill;

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Impostor " + modelId, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(true);
            var mat = new Material(Library.Template("UnlitCutout")) { name = "Impostor " + modelId };
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Cutoff", 0.4f);
            mat.SetFloat("_AlphaClip", 1);
            mat.SetFloat("_Cull", 0);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            Debug.Log($"[Impostor] {modelId}: {b.size.y:0.0} m tall, {solid} solid px");
            return new Card { Mat = mat, Side = 2 * half, Foot = centre.y - half - b.min.y };
        }

        static Color[] Grab(Camera cam, RenderTexture rt, Color background)
        {
            cam.backgroundColor = background;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            Object.Destroy(tex);
            return px;
        }

        /// <summary>A field of one kind of plant: three crossed quads per plant, all in one mesh.</summary>
        public sealed class Field
        {
            readonly Card card;
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();

            public Field(Card card) => this.card = card;
            public int Count { get; private set; }

            /// <summary>Plants one at `foot` (its lowest point on the ground), turned and scaled.</summary>
            public void Add(Vector3 foot, float yawDeg, float scale, bool mirror)
            {
                float side = card.Side * scale;
                float y0 = foot.y + card.Foot * scale;
                for (int k = 0; k < 3; k++)
                {
                    float a = (yawDeg + k * 60) * Mathf.Deg2Rad;
                    var across = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (side / 2);
                    var normal = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                    int i = v.Count;
                    var b = new Vector3(foot.x, y0, foot.z);
                    v.Add(b - across); v.Add(b - across + Vector3.up * side);
                    v.Add(b + across + Vector3.up * side); v.Add(b + across);
                    // Lit from above as much as from the side, so the cards don't flicker as they turn.
                    var nm = (normal * 0.3f + Vector3.up).normalized;
                    for (int q = 0; q < 4; q++) n.Add(nm);
                    float u0 = mirror ? 1 : 0, u1 = 1 - u0;
                    uv.Add(new Vector2(u0, 0)); uv.Add(new Vector2(u0, 1)); uv.Add(new Vector2(u1, 1)); uv.Add(new Vector2(u1, 0));
                    t.Add(i); t.Add(i + 1); t.Add(i + 2);
                    t.Add(i); t.Add(i + 2); t.Add(i + 3);
                }
                Count++;
            }

            public GameObject? Build(string name, Transform parent)
            {
                if (Count == 0) return null;
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = card.Mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                return go;
            }
        }
    }
}
