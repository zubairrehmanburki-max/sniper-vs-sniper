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
            /// <summary>The model's own height in metres.</summary>
            public float Height;
        }

        static readonly Dictionary<string, Card?> Cards = new Dictionary<string, Card?>();
        static readonly List<(string id, Card card, Texture2D tex, int tries)> Pending = new List<(string, Card, Texture2D, int)>();

        /// <summary>
        /// The card for a model, its size known at once; the picture itself is shot a frame or two
        /// later (Runner), once the render pipeline is up, and the cards show nothing until then.
        /// Null when the model is missing.
        /// </summary>
        public static Card? Of(string modelId)
        {
            if (Cards.TryGetValue(modelId, out var done)) return done;
            Card? card = null;
            try { card = Prepare(modelId); }
            catch (System.Exception e) { Debug.LogWarning($"[Impostor] {modelId}: {e.Message}"); }
            Cards[modelId] = card;
            return card;
        }

        static readonly Dictionary<string, int> VariantCounts = new Dictionary<string, int>();

        /// <summary>Drops the cached pictures (a map is being unloaded); the next map shoots its own.</summary>
        public static void Forget()
        {
            Cards.Clear();
            VariantCounts.Clear();
        }

        /// <summary>
        /// How many separate plants a model holds side by side (Poly Haven ships grass, shrubs and
        /// saplings as a row of variants): 1 for a single plant, 0 when the model is missing. Each
        /// is pictured on its own as "id#k".
        /// </summary>
        public static int Variants(string modelId)
        {
            if (VariantCounts.TryGetValue(modelId, out int n)) return n;
            n = 0;
            if (Library.Model(modelId) != null)
            {
                var probe = Library.Prop(modelId);
                probe.transform.position = new Vector3(0, -3000, 0);
                n = Mathf.Max(1, Library.Parts(probe).Count);
                Object.DestroyImmediate(probe);
            }
            return VariantCounts[modelId] = n;
        }

        /// <summary>The model for a card id ("id" or "id#k"), placed and frozen, with only variant k left in it.</summary>
        static GameObject Stage(string cardId, Vector3 at, Transform? parent = null)
        {
            int hash = cardId.IndexOf('#');
            string id = hash < 0 ? cardId : cardId.Substring(0, hash);
            var go = Library.Prop(id, parent);
            go.transform.position = at;
            if (hash >= 0 && int.TryParse(cardId.Substring(hash + 1), out int k))
            {
                var parts = Library.Parts(go);
                if (k < parts.Count)
                {
                    var keep = new HashSet<Renderer>(parts[k]);
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                        if (r != null && !keep.Contains(r)) Object.DestroyImmediate(r);
                }
            }
            return go;
        }

        static Card? Prepare(string modelId)
        {
            string baseId = modelId.Contains("#") ? modelId.Substring(0, modelId.IndexOf('#')) : modelId;
            if (Library.Model(baseId) == null) return null;
            var probe = Stage(modelId, new Vector3(0, -3000, 0));
            var b = Library.BoundsOf(probe);
            Object.DestroyImmediate(probe);
            if (b.size.y < 0.05f) return null;
            float half = Half(b);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Impostor " + modelId, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color32[Size * Size];
            tex.SetPixels32(clear);
            tex.Apply(true);
            var mat = new Material(Library.Template("UnlitCutout")) { name = "Impostor " + modelId };
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Cutoff", 0.4f);
            mat.SetFloat("_AlphaClip", 1);
            mat.SetFloat("_Cull", 0);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            // The picture is framed from the model's foot up, so the quad's bottom edge is the foot.
            var card = new Card { Mat = mat, Side = 2 * half, Foot = 0, Height = b.size.y };
            Pending.Add((modelId, card, tex, 0));
            Runner.Ensure();
            return card;
        }

        static float Half(Bounds b) => 0.52f * Mathf.Max(b.size.y, Mathf.Max(b.size.x, b.size.z));

        /// <summary>
        /// Shoots the pending pictures one at a time through the normal frame loop, as the Operators
        /// studio does: the camera draws the black shot one frame and the white shot the next, and
        /// each is read back the frame after it was drawn.
        /// </summary>
        sealed class Runner : MonoBehaviour
        {
            static Runner? instance;
            Shot? shot;
            int frames;

            public static void Ensure()
            {
                if (instance != null) return;
                instance = new GameObject("Impostor runner").AddComponent<Runner>();
            }

            void Update()
            {
                if (++frames < 2) return;
                if (shot == null)
                {
                    if (Pending.Count == 0) return;
                    var (id, card, tex, tries) = Pending[0];
                    Pending.RemoveAt(0);
                    try { shot = new Shot(id, tex, tries); }
                    catch (System.Exception e) { Debug.LogWarning($"[Impostor] {id}: {e}"); }
                    return;
                }
                bool done = true, ok = false;
                try { done = shot.Step(out ok); }
                catch (System.Exception e) { Debug.LogWarning($"[Impostor] {shot.Id}: {e}"); }
                if (!done) return;
                if (!ok && shot.Attempt < 2) Pending.Add((shot.Id, null!, shot.Into, shot.Attempt + 1));
                shot.Dispose();
                shot = null;
            }
        }

        /// <summary>One picture in the making: the model on a stage far below the map, a camera on it.</summary>
        sealed class Shot
        {
            public readonly string Id;
            public readonly Texture2D Into;
            public readonly int Attempt;
            readonly GameObject stage;
            readonly Camera cam;
            readonly RenderTexture rt;
            readonly Bounds bounds;
            Color[]? black;
            int phase;

            public Shot(string id, Texture2D into, int attempt)
            {
                Id = id;
                Into = into;
                Attempt = attempt;
                stage = new GameObject("Impostor stage " + id);
                stage.transform.position = new Vector3(0, -3000, 0);
                var model = Stage(id, stage.transform.position, stage.transform);
                Library.SetLayer(model, Layer);
                Relight(model);
                bounds = Library.BoundsOf(model);

                // Shot from the sun's side so the picture is the plant in full light.
                var sun = RenderSettings.sun;
                var fromSun = sun != null ? -sun.transform.forward : new Vector3(0.5f, 0.6f, -0.6f);
                var look = Vector3.ProjectOnPlane(-fromSun, Vector3.up);
                if (look.sqrMagnitude < 1e-4f) look = Vector3.forward;
                look.Normalize();
                float half = Half(bounds);
                var centre = new Vector3(bounds.center.x, bounds.min.y + half, bounds.center.z);

                var camGo = new GameObject("Impostor camera");
                camGo.transform.SetParent(stage.transform, false);
                camGo.transform.position = centre - look * (half * 4 + 10);
                camGo.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
                cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = half;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = half * 8 + 20;
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.depth = -50;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderPostProcessing = false;
                    data.renderShadows = false;
                }
                rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "Impostor " + id };
                cam.targetTexture = rt;
                if (Camera.main != null) Camera.main.cullingMask &= ~(1 << Layer);
            }

            /// <summary>Advances a frame; true when finished, with `ok` if the picture was made.</summary>
            public bool Step(out bool ok)
            {
                ok = false;
                if (phase == 0)
                {
                    black = Read();
                    cam.backgroundColor = Color.white;
                    phase = 1;
                    return false;
                }
                cam.enabled = false;
                ok = Develop(Id, black!, Read(), Into, Attempt, bounds);
                return true;
            }

            Color[] Read()
            {
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

            public void Dispose()
            {
                if (cam != null) cam.targetTexture = null;
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(stage);
            }
        }

        /// <summary>
        /// Puts every part of the model on the cut-out Lit template with its own colour map (the
        /// imported glTF materials draw nothing on some renderers, leaves included).
        /// </summary>
        static void Relight(GameObject model)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    var tex = src == null ? null : src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture;
                    var m = new Material(Library.Template("LitCutout")) { name = "Impostor model " + (src != null ? src.name : "") };
                    m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
                    m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_Smoothness", 0.1f);
                    m.SetFloat("_Metallic", 0);
                    m.SetFloat("_Cutoff", 0.5f);
                    m.SetFloat("_AlphaClip", 1);
                    m.SetFloat("_Cull", 0);
                    m.EnableKeyword("_ALPHATEST_ON");
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Alpha from the difference of the shots; colour un-premultiplied from the black one. Clear
        /// pixels take the plant's mean colour so mipmaps don't fringe it with black or white.
        /// </summary>
        static bool Develop(string modelId, Color[] black, Color[] white, Texture2D into, int attempt, Bounds b)
        {
            var px = new Color32[Size * Size];
            var mean = Color.black;
            int solid = 0, lit = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color k = black[i], w = white[i];
                if (k.r + k.g + k.b > 0.02f) lit++;
                float a = Mathf.Clamp01(1 - ((w.r - k.r) + (w.g - k.g) + (w.b - k.b)) / 3);
                var c = a > 0.02f ? new Color(k.r / a, k.g / a, k.b / a) : Color.black;
                px[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), a);
                if (a > 0.5f)
                {
                    mean += c;
                    solid++;
                }
            }
            Debug.Log($"[Impostor] {modelId} try {attempt}: {b.size} m, {solid} solid px, {lit} lit px on black, white {white[0]}");
            if (solid < Size * Size / 400 || solid > Size * Size * 0.98f) return false;
            mean /= solid;
            var fill = (Color32)new Color(mean.r, mean.g, mean.b, 0);
            for (int i = 0; i < px.Length; i++) if (px[i].a < 8) px[i] = fill;
            into.SetPixels32(px);
            into.Apply(true);
            return true;
        }

        /// <summary>A field of one kind of plant: three crossed quads per plant, all in one mesh.</summary>
        public sealed class Field
        {
            readonly Card card;
            readonly Material mat;
            readonly float size;
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();

            /// <summary>
            /// A field whose plants stand `height` metres tall at scale 1, whatever the model's size;
            /// `tint` washes the picture (sun-dried scrub, frosted grass) on a copy of its material.
            /// </summary>
            public Field(Card card, float height, Color? tint = null)
            {
                this.card = card;
                size = height / Mathf.Max(0.1f, card.Height);
                mat = card.Mat;
                if (tint is Color c)
                {
                    mat = new Material(card.Mat) { name = card.Mat.name + " tinted" };
                    mat.SetColor("_BaseColor", c);
                }
            }
            public int Count { get; private set; }

            /// <summary>Plants one at `foot` (its lowest point on the ground), turned and scaled.</summary>
            public void Add(Vector3 foot, float yawDeg, float scale, bool mirror)
            {
                scale *= size;
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
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                return go;
            }
        }
    }
}
