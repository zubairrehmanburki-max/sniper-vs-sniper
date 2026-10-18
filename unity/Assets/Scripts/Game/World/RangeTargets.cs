using System.Collections.Generic;
using SniperDuel.Ballistics;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Solo practice targets, drawn from the rules' field so what you see is exactly what the round
    /// is tested against: hanging steel (silhouettes, gongs, a know-your-limits rack, a big gong),
    /// spinners, a dueling tree, pepper poppers, metallic-silhouette animals on their rails, prairie
    /// dogs, a mover running behind its berm, paper bullseyes and IPSC cardboard on stakes, and the
    /// plinking line: bottles on a plank over oil drums, cans on fence posts, balloons, water jugs and
    /// a watermelon on a trestle table, clay pigeons stood against an earth mound. Each reacts the way
    /// the real thing does: steel rings and swings, spins, flips or falls; glass, clay and balloons
    /// burst; cans jump off their posts; paper takes a hole and a score.
    /// </summary>
    public sealed class RangeTargets : MonoBehaviour
    {
        sealed class Live
        {
            public Mark Mark = null!;
            public Transform Root = null!, Carrier = null!, Pivot = null!, Body = null!;
            public float Angle, Spin, Goal;
            public float Drop, DropSpeed, Back;
            public bool Moving;
        }

        /// <summary>A shard, a chip, a drop or a knocked-off can, flying under gravity till it lands.</summary>
        sealed class Bit
        {
            public Transform T = null!;
            public Vector3 V, Turn;
            public float Floor;
            public bool Fade;
        }

        RangeField field = new RangeField();
        readonly List<Live> live = new List<Live>();
        readonly List<Bit> bits = new List<Bit>();
        readonly List<Mesh> meshes = new List<Mesh>();
        Transform holder = null!;

        Material white = null!, orange = null!, frame = null!, lead = null!, wood = null!, drum = null!, earth = null!,
            card = null!, cardLine = null!, paper = null!, hole = null!, jug = null!, melon = null!, melonFlesh = null!, clay = null!, rubberString = null!;
        Material[] glass = null!, tins = null!, balloons = null!;
        Mesh shardMesh = null!, chipMesh = null!;

        /// <summary>The range clock: the movers run on it, and shots are tested against it.</summary>
        public double Now;

        public RangeField Field => field;

        public static RangeTargets Create(Transform parent)
        {
            var go = new GameObject("Practice range");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<RangeTargets>();
            r.Materials();
            return r;
        }

        void Materials()
        {
            // Fresh range paint: flat, bright, with a touch of its own light so a plate in shade still reads.
            white = Library.Glow(new Color(0.92f, 0.92f, 0.88f), new Color(0.2f, 0.2f, 0.19f));
            orange = Library.Glow(new Color(0.95f, 0.42f, 0.08f), new Color(0.22f, 0.09f, 0.02f));
            white.SetFloat("_Smoothness", 0.15f);
            orange.SetFloat("_Smoothness", 0.15f);
            frame = Library.Plain(new Color(0.2f, 0.21f, 0.2f), 0.3f, 0.6f);
            lead = Library.Plain(new Color(0.34f, 0.35f, 0.36f), 0.55f, 0.8f);
            wood = Library.Surface("weathered_planks", 1.5f, new Color(0.95f, 0.88f, 0.78f));
            drum = Library.Surface("rusty_painted_metal", 1.2f, new Color(0.55f, 0.7f, 0.85f));
            earth = Library.Surface("brown_mud_leaves_01", 2.5f, new Color(0.72f, 0.62f, 0.52f));
            card = Library.Plain(new Color(0.66f, 0.53f, 0.37f), 0.1f);
            cardLine = Library.Plain(new Color(0.42f, 0.32f, 0.21f), 0.1f);
            paper = Library.Plain(Color.white, 0.1f);
            paper.SetTexture("_BaseMap", Bullseye());
            paper.mainTexture = paper.GetTexture("_BaseMap");
            hole = Library.Plain(new Color(0.03f, 0.03f, 0.03f), 0.05f);
            jug = Library.Plain(new Color(0.93f, 0.93f, 0.89f), 0.45f);
            melon = Library.Plain(new Color(0.13f, 0.3f, 0.09f), 0.55f);
            melonFlesh = Library.Plain(new Color(0.85f, 0.16f, 0.18f), 0.4f);
            clay = Library.Plain(new Color(0.96f, 0.4f, 0.06f), 0.2f);
            rubberString = Library.Plain(new Color(0.85f, 0.85f, 0.82f), 0.2f);
            glass = new[]
            {
                Library.Plain(new Color(0.26f, 0.12f, 0.03f), 0.92f),
                Library.Plain(new Color(0.05f, 0.22f, 0.08f), 0.92f),
                Library.Plain(new Color(0.55f, 0.66f, 0.6f), 0.95f),
            };
            tins = new[]
            {
                Library.Plain(new Color(0.75f, 0.08f, 0.07f), 0.6f, 0.5f),
                Library.Plain(new Color(0.78f, 0.79f, 0.8f), 0.7f, 0.9f),
                Library.Plain(new Color(0.1f, 0.4f, 0.15f), 0.6f, 0.5f),
                Library.Plain(new Color(0.1f, 0.2f, 0.6f), 0.6f, 0.5f),
                Library.Plain(new Color(0.9f, 0.7f, 0.1f), 0.6f, 0.5f),
            };
            balloons = new[]
            {
                Library.Plain(new Color(0.9f, 0.08f, 0.08f), 0.75f),
                Library.Plain(new Color(0.98f, 0.82f, 0.1f), 0.75f),
                Library.Plain(new Color(0.1f, 0.35f, 0.95f), 0.75f),
                Library.Plain(new Color(0.15f, 0.75f, 0.2f), 0.75f),
            };
            var sb = new MeshBuilder();
            sb.Triangle(new Vector3(-0.5f, -0.4f, 0), new Vector3(0, 0.6f, 0), new Vector3(0.5f, -0.3f, 0));
            sb.Triangle(new Vector3(-0.5f, -0.4f, 0), new Vector3(0.5f, -0.3f, 0), new Vector3(0, 0.6f, 0));
            shardMesh = sb.Build("Shard");
            var cb = new MeshBuilder();
            cb.Box(Vector3.zero, Vector3.one);
            chipMesh = cb.Build("Chip");
        }

        /// <summary>A bullseye sheet: white paper, black rings 1 to 6, the black aiming disc over 7 to X with white rings.</summary>
        static Texture2D Bullseye()
        {
            const int N = 512;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Bullseye", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            // The sheet is 0.55 m; the rings span a 0.5 m disc, ten bands of 2.5 cm.
            float scale = 0.55f / N, band = 0.025f, line = 0.0018f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) * scale - 0.275f, v = (y + 0.5f) * scale - 0.275f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float k = r / band;
                    bool onLine = Mathf.Abs(k - Mathf.Round(k)) * band < line && k < 10.5f && k > 0.25f;
                    bool black = r < 4 * band;
                    bool xRing = Mathf.Abs(r - band / 2) < line * 0.8f;
                    bool dark = black ? (onLine || xRing ? false : true) : onLine;
                    byte c = dark ? (byte)18 : (byte)238;
                    px[y * N + x] = new Color32(c, c, (byte)(dark ? 18 : 230), 255);
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ---- laying out -----------------------------------------------------------------------

        /// <summary>Clears the range and sets out a fresh field, everything in plain view of `proneEye`.</summary>
        public void Lay(DuelMap map, Vec3 proneEye, int seed)
        {
            Clear();
            // The rules know the ground and the stands; a bush or a tall tuft only the drawn world knows.
            var scene = transform.parent;
            field = RangeLayout.Lay(map, proneEye, seed, (e, p) =>
            {
                Vector3 a = Library.ToUnity(e), d = Library.ToUnity(p) - a;
                return Pick.Ray(scene, a, d, d.magnitude - 0.8f, transform) == null;
            });
            holder = new GameObject("Field").transform;
            holder.SetParent(transform, false);
            var byStation = new List<List<Mark>>();
            foreach (var _ in field.Stations) byStation.Add(new List<Mark>());
            foreach (var m in field.Marks) byStation[m.Station].Add(m);
            for (int i = 0; i < field.Stations.Count; i++)
            {
                Stand(field.Stations[i], byStation[i], i);
                int n = 0;
                foreach (var m in byStation[i]) live.Add(Make(m, field.Stations[i], n++));
            }
            Debug.Log($"[Range] {field.Stations.Count} stands, {field.Count} targets");
        }

        void Clear()
        {
            live.Clear();
            foreach (var b in bits)
                if (b.T != null) Destroy(b.T.gameObject);
            bits.Clear();
            foreach (var m in meshes) Destroy(m);
            meshes.Clear();
            if (holder != null) Destroy(holder.gameObject);
        }

        void OnDestroy()
        {
            foreach (var m in meshes) Destroy(m);
            meshes.Clear();
            if (paper != null) Destroy(paper.mainTexture);
            if (shardMesh != null) Destroy(shardMesh);
            if (chipMesh != null) Destroy(chipMesh);
        }

        readonly Dictionary<Color, Material> straw = new Dictionary<Color, Material>();

        Material Straw(Color tint)
        {
            if (!straw.TryGetValue(tint, out var m)) straw[tint] = m = Library.Plain(tint, 0.05f);
            return m;
        }

        GameObject Put(MeshBuilder mb, string name, Material mat, Transform parent, bool shadows = true)
        {
            var go = mb.Object(name, mat, parent, shadows);
            meshes.Add(go.GetComponent<MeshFilter>().sharedMesh);
            return go;
        }

        /// <summary>The stand's fixed parts, in its own frame: x across, y up from its ground, z down-range.</summary>
        void Stand(Station st, List<Mark> marks, int index)
        {
            var root = new GameObject($"Stand {index} {st.Kind}").transform;
            root.SetParent(holder, false);
            root.position = Library.ToUnity(st.Position);
            var steel = new MeshBuilder();
            var timber = new MeshBuilder();
            var dirt = new MeshBuilder();
            var drums = new MeshBuilder();
            float Local(Mark m, out float h)
            {
                var p = Library.ToUnity(m.Position) - root.position;
                h = p.y;
                return p.x;
            }
            switch (st.Kind)
            {
                case "bottles":
                    foreach (float u in new[] { -0.9f, 0.9f })
                    {
                        drums.Cylinder(new Vector3(u, 0, 0.28f), 0.28f, 0.88f, 20);
                        foreach (float y in new[] { 0.29f, 0.59f })
                            steel.Tube(new Vector3(u, y, 0.28f), new Vector3(u, y + 0.02f, 0.28f), 0.287f, 0.287f, 20, false);
                    }
                    timber.Box(new Vector3(0, 0.9f, 0.28f), new Vector3(2.5f, 0.04f, 0.28f));
                    break;
                case "cans":
                    foreach (var m in marks)
                    {
                        float u = Local(m, out float top);
                        timber.Box(new Vector3(u, (top - 0.2f) / 2, 0.045f), new Vector3(0.09f, top + 0.2f, 0.09f));
                    }
                    break;
                case "balloons":
                    foreach (var m in marks)
                    {
                        float u = Local(m, out float h);
                        timber.Box(new Vector3(u, 0.35f, 0.05f), new Vector3(0.025f, 1.1f, 0.025f));
                    }
                    break;
                case "table":
                    timber.Box(new Vector3(0, 0.745f, 0.35f), new Vector3(2.0f, 0.05f, 0.7f));
                    foreach (float u in new[] { -0.75f, 0.75f })
                    {
                        timber.Beam(new Vector3(u, -0.1f, 0.0f), new Vector3(u, 0.72f, 0.3f), 0.05f, 0.05f);
                        timber.Beam(new Vector3(u, -0.1f, 0.7f), new Vector3(u, 0.72f, 0.4f), 0.05f, 0.05f);
                        timber.Box(new Vector3(u, 0.7f, 0.35f), new Vector3(0.08f, 0.04f, 0.68f));
                    }
                    break;
                case "paper":
                    foreach (float u in new[] { -0.335f, 0.335f })
                        timber.Box(new Vector3(u, 0.65f, 0.055f), new Vector3(0.05f, 1.9f, 0.09f));
                    break;
                case "cardboard":
                    foreach (float u in new[] { -0.265f, 0.265f })
                        timber.Box(new Vector3(u, 0.6f, 0.055f), new Vector3(0.05f, 1.8f, 0.09f));
                    break;
                case "clays":
                {
                    // Two rows of three straw bales, each a shade different, twine round them.
                    var rng = new System.Random(index);
                    float bale = (float)RangeLayout.BaleTop / 2;
                    for (int row = 0; row < 2; row++)
                        for (int i = 0; i < 3; i++)
                        {
                            var c = new Vector3(-1.0667f + 1.0667f * i, bale * row + bale / 2 - (row == 0 ? 0.15f : 0), 0.32f);
                            var size = new Vector3(1.06f, bale + (row == 0 ? 0.3f : 0), 0.56f);
                            var mb = new MeshBuilder();
                            mb.Box(c, size);
                            var tint = Color.Lerp(new Color(0.78f, 0.66f, 0.38f), new Color(0.7f, 0.6f, 0.36f), (float)rng.NextDouble());
                            Put(mb, "Bale", Straw(tint), root);
                            var twine = new MeshBuilder();
                            foreach (float u in new[] { -0.28f, 0.28f })
                                twine.Box(c + new Vector3(u, 0, 0), new Vector3(0.012f, size.y + 0.006f, size.z + 0.006f));
                            Put(twine, "Twine", cardLine, root, false);
                        }
                    break;
                }
                case "spinner":
                    foreach (float u in new[] { -0.29f, 0.29f })
                    {
                        steel.Box(new Vector3(u, 0.4f, 0.03f), new Vector3(0.06f, 1.2f, 0.06f));
                        steel.Box(new Vector3(u, 0.02f, 0.03f), new Vector3(0.08f, 0.04f, 0.6f));
                    }
                    steel.Tube(new Vector3(-0.29f, 0.75f, 0.03f), new Vector3(0.29f, 0.75f, 0.03f), 0.012f, 8);
                    break;
                case "tree":
                    steel.Box(new Vector3(0, 0.71f, 0.04f), new Vector3(0.08f, 2.02f, 0.08f));
                    steel.Box(new Vector3(0, 0.015f, 0.04f), new Vector3(0.7f, 0.03f, 0.7f));
                    break;
                case "poppers":
                    foreach (var m in marks)
                    {
                        float u = Local(m, out _);
                        steel.Box(new Vector3(u, 0.03f, 0.1f), new Vector3(0.4f, 0.06f, 0.45f));
                    }
                    break;
                case "kyl":
                case "gongs":
                case "biggong":
                case "ipsc":
                {
                    float lo = float.MaxValue, hi = float.MinValue, bar = 0, heavy = st.Kind == "biggong" ? 1.6f : 1;
                    foreach (var m in marks)
                    {
                        float u = Local(m, out float h);
                        var (u0, u1, _, _) = m.Parts[0].Bounds();
                        lo = Mathf.Min(lo, u + (float)u0);
                        hi = Mathf.Max(hi, u + (float)u1);
                        bar = h + (float)m.PivotV;
                    }
                    lo -= 0.35f;
                    hi += 0.35f;
                    foreach (float u in new[] { lo, hi })
                        foreach (float d in new[] { -0.45f, 0.45f })
                            steel.Beam(new Vector3(u, -0.15f, d * heavy), new Vector3(u, bar + 0.03f, 0), 0.05f * heavy, 0.05f * heavy);
                    steel.Beam(new Vector3(lo - 0.05f, bar + 0.04f, 0), new Vector3(hi + 0.05f, bar + 0.04f, 0), 0.06f * heavy, 0.06f * heavy);
                    break;
                }
                case "mover":
                    dirt.Frustum(new Vector3(0, -0.3f, -1.3f), new Vector2(15.8f, 1.5f), new Vector3(0, 0.52f, -1.3f), new Vector2(15.2f, 1.22f));
                    foreach (float d in new[] { -0.12f, 0.12f })
                        steel.Tube(new Vector3(-7.2f, 0.2f, d), new Vector3(7.2f, 0.2f, d), 0.025f, 8);
                    for (float u = -7f; u <= 7.01f; u += 1)
                        timber.Box(new Vector3(u, 0.1f, 0), new Vector3(0.12f, 0.12f, 0.5f));
                    break;
                case "chickens":
                case "pigs":
                case "turkeys":
                case "rams":
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var m in marks)
                    {
                        float u = Local(m, out _);
                        lo = Mathf.Min(lo, u);
                        hi = Mathf.Max(hi, u);
                    }
                    float gap = marks.Count > 1 ? (hi - lo) / (marks.Count - 1) : 1;
                    lo -= gap / 2;
                    hi += gap / 2;
                    float top = (float)RangeLayout.RailTop;
                    steel.Box(new Vector3((lo + hi) / 2, top - 0.06f, 0.15f), new Vector3(hi - lo, 0.12f, 0.3f));
                    for (int i = 0; i < 4; i++)
                        foreach (float d in new[] { 0.04f, 0.26f })
                        {
                            float u = Mathf.Lerp(lo + 0.1f, hi - 0.1f, i / 3f);
                            steel.Box(new Vector3(u, (top - 0.12f - 0.2f) / 2, d), new Vector3(0.05f, top - 0.12f + 0.2f, 0.05f));
                        }
                    break;
                }
                case "prairie":
                    foreach (var m in marks)
                    {
                        float u = Local(m, out float top);
                        steel.Box(new Vector3(u, (top - 0.2f) / 2, 0.05f), new Vector3(0.06f, top + 0.2f, 0.06f));
                    }
                    break;
            }
            if (steel.VertexCount > 0) Put(steel, "Steel", frame, root);
            if (timber.VertexCount > 0) Put(timber, "Timber", wood, root);
            if (dirt.VertexCount > 0) Put(dirt, "Earth", earth, root);
            if (drums.VertexCount > 0) Put(drums, "Drums", drum, root);
        }

        /// <summary>One target: its geometry under a pivot it turns about, on a carrier for the mover.</summary>
        Live Make(Mark m, Station st, int n)
        {
            var root = new GameObject(m.Id).transform;
            root.SetParent(holder, false);
            root.position = Library.ToUnity(m.Position);
            root.rotation = Quaternion.Euler(0, (float)(m.FacingYaw * Mathf.Rad2Deg) + 180, 0);
            var carrier = new GameObject("Carrier").transform;
            carrier.SetParent(root, false);
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(carrier, false);
            pivot.localPosition = new Vector3(0, (float)m.PivotV, 0);
            var body = new GameObject("Body").transform;
            body.SetParent(pivot, false);
            body.localPosition = new Vector3(0, -(float)m.PivotV, 0);
            var l = new Live { Mark = m, Root = root, Carrier = carrier, Pivot = pivot, Body = body, Moving = m.Rail > 0 };
            const float T = 0.012f;
            Material paint = n % 3 == 2 ? orange : white;
            switch (m.Kind)
            {
                case "bottle":
                {
                    int which = Mathf.Max(0, System.Array.FindIndex(RangeLayout.Bottles, b => b[0] == m.Parts[0].Outline![0]));
                    var mb = new MeshBuilder();
                    Lathe(mb, RangeLayout.Bottles[which], 18);
                    Put(mb, "Bottle", glass[which % glass.Length], body);
                    break;
                }
                case "can":
                {
                    var mb = new MeshBuilder();
                    Lathe(mb, new[] { 0.031, 0, 0.033, 0.006, 0.033, 0.117, 0.03, 0.123, 0.0, 0.123 }, 16);
                    Put(mb, "Can", tins[n % tins.Length], body);
                    break;
                }
                case "balloon":
                {
                    var mb = new MeshBuilder();
                    Ellipsoid(mb, new Vector3(0, 0.14f, 0), new Vector3(0.14f, 0.14f, 0.14f), 14, 10);
                    mb.Tube(new Vector3(0, 0.01f, 0), new Vector3(0, -0.005f, 0), 0.008f, 0.014f, 6, true);
                    Put(mb, "Balloon", balloons[n % balloons.Length], body);
                    // The string down to the stake (it stays when the balloon goes).
                    var s = new MeshBuilder();
                    var stake = Library.ToUnity(new Vec3(m.Position.X, st.Ground + 0.9, st.Position.Z - 0.05)) - root.position;
                    s.Beam(Quaternion.Inverse(root.rotation) * stake, new Vector3(0, 0, 0), 0.003f, 0.003f);
                    Put(s, "String", rubberString, root, false);
                    break;
                }
                case "jug":
                {
                    var mb = new MeshBuilder();
                    mb.Box(new Vector3(0, 0.1f, 0), new Vector3(0.15f, 0.2f, 0.15f));
                    mb.Frustum(new Vector3(0, 0.2f, 0), new Vector2(0.15f, 0.15f), new Vector3(0, 0.245f, 0), new Vector2(0.045f, 0.045f));
                    mb.Tube(new Vector3(0, 0.245f, 0), new Vector3(0, 0.268f, 0), 0.02f, 0.02f, 10, true);
                    mb.Beam(new Vector3(0.05f, 0.215f, 0.04f), new Vector3(0.05f, 0.15f, 0.07f), 0.02f, 0.02f);
                    Put(mb, "Jug", jug, body);
                    break;
                }
                case "melon":
                {
                    var mb = new MeshBuilder();
                    Ellipsoid(mb, new Vector3(0, 0.12f, 0), new Vector3(0.18f, 0.12f, 0.12f), 18, 12);
                    Put(mb, "Melon", melon, body);
                    break;
                }
                case "bullseye":
                {
                    var b = new MeshBuilder();
                    b.Box(new Vector3(0, 0.305f, 0.004f), new Vector3(0.61f, 0.61f, 0.006f));
                    Put(b, "Backer", card, body);
                    var p = new MeshBuilder();
                    const float h = 0.275f;
                    // Held a little proud of the backer: two millimetres z-fight at range.
                    const float z = -0.015f;
                    p.Face(new Vector3(-h, 0.305f - h, z), new Vector3(-h, 0.305f + h, z), new Vector3(h, 0.305f + h, z), new Vector3(h, 0.305f - h, z), Vector2.zero, Vector2.one);
                    Put(p, "Paper", paper, body, false);
                    break;
                }
                case "cardboard":
                {
                    var b = new MeshBuilder();
                    Plate(b, m.Parts[0], 0.004f);
                    Put(b, "Cardboard", card, body);
                    // The perforated zone lines: A in the chest and head, C round it.
                    var z = new MeshBuilder();
                    void Outline(float u0, float u1, float v0, float v1)
                    {
                        const float w = 0.006f, f = -0.012f;
                        z.Box(new Vector3((u0 + u1) / 2, v0, f), new Vector3(u1 - u0, w, 0.001f));
                        z.Box(new Vector3((u0 + u1) / 2, v1, f), new Vector3(u1 - u0, w, 0.001f));
                        z.Box(new Vector3(u0, (v0 + v1) / 2, f), new Vector3(w, v1 - v0, 0.001f));
                        z.Box(new Vector3(u1, (v0 + v1) / 2, f), new Vector3(w, v1 - v0, 0.001f));
                    }
                    Outline(-0.075f, 0.075f, 0.22f, 0.50f);
                    Outline(-0.15f, 0.15f, 0.12f, 0.56f);
                    Outline(-0.05f, 0.05f, 0.62f, 0.70f);
                    Put(z, "Zones", cardLine, body, false);
                    break;
                }
                case "clay":
                {
                    var mb = new MeshBuilder();
                    mb.Tube(new Vector3(0, 0.054f, 0.0f), new Vector3(0, 0.054f, -0.012f), 0.054f, 0.05f, 20, false);
                    mb.Tube(new Vector3(0, 0.054f, -0.012f), new Vector3(0, 0.054f, -0.026f), 0.05f, 0.03f, 20, true);
                    Put(mb, "Clay", clay, body);
                    break;
                }
                case "spinner":
                {
                    var mb = new MeshBuilder();
                    Disc(mb, m.Parts[0], T);
                    Disc(mb, m.Parts[1], T);
                    mb.Box(new Vector3(0, -0.02f, 0), new Vector3(0.025f, 0.38f, 0.01f));
                    Put(mb, "Paddles", orange, body);
                    break;
                }
                case "paddle":
                {
                    var mb = new MeshBuilder();
                    Disc(mb, m.Parts[0], T);
                    float side = Mathf.Sign((float)m.Parts[0].U);
                    mb.Box(new Vector3(side * 0.135f, 0, 0), new Vector3(0.19f, 0.025f, 0.012f));
                    Put(mb, "Paddle", n % 2 == 0 ? white : orange, body);
                    break;
                }
                case "mover":
                {
                    var mb = new MeshBuilder();
                    Plate(mb, m.Parts[0], T);
                    Put(mb, "Plate", white, body);
                    var trolley = new MeshBuilder();
                    trolley.Box(new Vector3(0, 0.3f - 0.82f, 0), new Vector3(0.5f, 0.2f, 0.4f));
                    trolley.Box(new Vector3(0, (0.4f - 0.82f) / 2, 0.02f), new Vector3(0.05f, 0.82f - 0.4f, 0.04f));
                    Put(trolley, "Trolley", frame, carrier);
                    break;
                }
                default:
                {
                    // Flat steel: silhouettes, gongs, squares, poppers, animals, prairie dogs.
                    var mb = new MeshBuilder();
                    foreach (var part in m.Parts)
                    {
                        if (part.Shape == PartShape.Disc) Disc(mb, part, m.Kind == "gong" && part.W > 0.5 ? 0.02f : T);
                        else Plate(mb, part, T);
                    }
                    // Match silhouettes are black; the little prairie dogs are painted so they show on the grass.
                    bool animal = m.Kind is "chicken" or "pig" or "turkey" or "ram";
                    Put(mb, "Steel", animal ? frame : m.Kind == "prairie-dog" ? orange : paint, body);
                    if (m.Reaction == MarkReaction.Swing)
                    {
                        // Chains from the bar to the top of the plate.
                        var (u0, u1, _, v1) = m.Parts[0].Bounds();
                        var c = new MeshBuilder();
                        float top = (float)v1 - (m.Parts[0].Shape == PartShape.Disc ? 0.02f : 0.01f);
                        float spread = m.Parts[0].Shape == PartShape.Disc ? 0 : (float)(u1 - u0) * 0.3f;
                        foreach (float side in spread > 0 ? new[] { -1f, 1f } : new[] { 0f })
                            c.Beam(new Vector3(side * spread, top, 0), new Vector3(side * spread * 1.15f, (float)m.PivotV + 0.03f, 0), 0.01f, 0.01f);
                        Put(c, "Chains", frame, body, false);
                    }
                    break;
                }
            }
            return l;
        }

        // ---- shapes ----------------------------------------------------------------------------

        static void Facing(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) (b, c) = (c, b);
            mb.Triangle(a, b, c);
        }

        /// <summary>A flat plate cut to a part's outline, `thick` deep, its face toward the firing line (−z).</summary>
        static void Plate(MeshBuilder mb, MarkPart part, float thick)
        {
            Vector2[] pts;
            if (part.Shape == PartShape.Rect)
            {
                float w = (float)part.W / 2, h = (float)part.H / 2;
                pts = new[] { new Vector2(-w, -h), new Vector2(w, -h), new Vector2(w, h), new Vector2(-w, h) };
            }
            else
            {
                var o = part.Outline!;
                pts = new Vector2[o.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((float)o[2 * i], (float)o[2 * i + 1]);
            }
            var c = new Vector2((float)part.U, (float)part.V);
            float f = -thick / 2, b = thick / 2;
            float area = 0;
            for (int i = 0; i < pts.Length; i++) area += pts[i].x * pts[(i + 1) % pts.Length].y - pts[(i + 1) % pts.Length].x * pts[i].y;
            var tris = Triangulate(pts);
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector2 p = pts[tris[i]] + c, q = pts[tris[i + 1]] + c, r = pts[tris[i + 2]] + c;
                Facing(mb, new Vector3(p.x, p.y, f), new Vector3(q.x, q.y, f), new Vector3(r.x, r.y, f), Vector3.back);
                Facing(mb, new Vector3(p.x, p.y, b), new Vector3(q.x, q.y, b), new Vector3(r.x, r.y, b), Vector3.forward);
            }
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 p = pts[i] + c, q = pts[(i + 1) % pts.Length] + c;
                var e = q - p;
                // Going round anticlockwise, the outside is on the right of each edge.
                var outward = area > 0 ? new Vector3(e.y, -e.x, 0) : new Vector3(-e.y, e.x, 0);
                mb.Face(new Vector3(p.x, p.y, f), new Vector3(q.x, q.y, f), new Vector3(q.x, q.y, b), new Vector3(p.x, p.y, b), outward);
            }
        }

        static void Disc(MeshBuilder mb, MarkPart part, float thick)
        {
            var c = new Vector3((float)part.U, (float)part.V, 0);
            mb.Tube(c + new Vector3(0, 0, -thick / 2), c + new Vector3(0, 0, thick / 2), (float)part.W / 2, (float)part.W / 2, 28, true);
        }

        /// <summary>Ear clipping: triangles (indices) covering a simple polygon given either way round.</summary>
        static List<int> Triangulate(Vector2[] pts)
        {
            var idx = new List<int>();
            float area = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                area += a.x * b.y - b.x * a.y;
            }
            for (int i = 0; i < pts.Length; i++) idx.Add(area > 0 ? i : pts.Length - 1 - i);
            var tris = new List<int>();
            static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 1000)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = pts[ia], b = pts[ib], c = pts[ic];
                    if (Cross(a, b, c) <= 1e-9f) continue;
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        var p = pts[j];
                        if (Cross(a, b, p) >= 0 && Cross(b, c, p) >= 0 && Cross(c, a, p) >= 0)
                        {
                            inside = true;
                            break;
                        }
                    }
                    if (inside) continue;
                    tris.Add(ia);
                    tris.Add(ib);
                    tris.Add(ic);
                    idx.RemoveAt(i);
                    cut = true;
                    break;
                }
                if (!cut) break;
            }
            if (idx.Count == 3) tris.AddRange(idx);
            return tris;
        }

        /// <summary>A round body turned from a (radius, height) profile about the y axis, smooth shaded, capped.</summary>
        static void Lathe(MeshBuilder mb, double[] profile, int sides)
        {
            int n = profile.Length / 2;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float r = (float)profile[2 * i], y = (float)profile[2 * i + 1];
                // The surface's slope here, from its neighbours.
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(n - 1, i + 1);
                float dr = (float)(profile[2 * b] - profile[2 * a]), dy = (float)(profile[2 * b + 1] - profile[2 * a + 1]);
                var side = new Vector2(dy, -dr).normalized;
                for (int s = 0; s <= sides; s++)
                {
                    float ang = s * 2 * Mathf.PI / sides;
                    var e = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                    verts.Add(e * r + Vector3.up * y);
                    normals.Add((e * side.x + Vector3.up * side.y).normalized);
                }
            }
            int row = sides + 1;
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int p = i * row + s;
                    Wound(tris, verts, normals, p, p + 1, p + row);
                    Wound(tris, verts, normals, p + 1, p + row + 1, p + row);
                }
            mb.Smooth(verts, normals, tris);
            // Flat base and top.
            float r0 = (float)profile[0], rTop = (float)profile[2 * n - 2], top = (float)profile[2 * n - 1];
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * 2 * Mathf.PI / sides, a1 = (s + 1) * 2 * Mathf.PI / sides;
                var e0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var e1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Facing(mb, Vector3.zero, e0 * r0, e1 * r0, Vector3.down);
                if (rTop > 0) Facing(mb, Vector3.up * top, e0 * rTop + Vector3.up * top, e1 * rTop + Vector3.up * top, Vector3.up);
            }
        }

        static void Wound(List<int> tris, List<Vector3> v, List<Vector3> n, int a, int b, int c)
        {
            var mean = n[a] + n[b] + n[c];
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), mean) < 0) (b, c) = (c, b);
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }

        static void Ellipsoid(MeshBuilder mb, Vector3 centre, Vector3 radii, int segs, int rings)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float lat = Mathf.PI * i / rings - Mathf.PI / 2;
                for (int s = 0; s <= segs; s++)
                {
                    float lon = 2 * Mathf.PI * s / segs;
                    var unit = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    verts.Add(centre + Vector3.Scale(unit, radii));
                    normals.Add(new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z).normalized);
                }
            }
            int row = segs + 1;
            for (int i = 0; i < rings; i++)
                for (int s = 0; s < segs; s++)
                {
                    int p = i * row + s;
                    if (i > 0) Wound(tris, verts, normals, p, p + 1, p + row);
                    if (i < rings - 1) Wound(tris, verts, normals, p + 1, p + row + 1, p + row);
                }
            mb.Smooth(verts, normals, tris);
        }

        // ---- strikes ---------------------------------------------------------------------------

        public int Count => field.Count;
        public int HitCount => field.HitCount;

        /// <summary>
        /// The round has arrived: the target reacts and the rules' state follows (fallen and broken
        /// targets are gone, a paddle is on its other side). Returns the spotter's call.
        /// </summary>
        public string Strike(MarkHit hit, Impact impact)
        {
            var l = live.Find(x => x.Mark == hit.Mark);
            var m = hit.Mark;
            m.Hit = true;
            if (l == null) return "HIT";
            float energy = Mathf.Clamp((float)impact.Energy / 3000f, 0.4f, 2.5f);
            var at = Library.ToUnity(impact.Point);
            var local = l.Body.InverseTransformPoint(at);
            switch (m.Reaction)
            {
                case MarkReaction.Swing:
                    Splash(l, local);
                    // A heavier round swings it further: a quarter to a half radian on its chains.
                    l.Spin += m.Kind == "mover" ? 1.5f * energy : Mathf.Clamp(0.2f + 0.15f * energy, 0.25f, 0.55f) * Mathf.Sqrt(9.81f / Mathf.Max(0.3f, (float)m.PivotV));
                    return "HIT";
                case MarkReaction.Spin:
                    Splash(l, local);
                    // The top paddle struck spins it one way, the bottom one the other.
                    l.Spin += (hit.Part == 0 ? 1 : -1) * Mathf.Clamp(12 * energy, 9, 20);
                    return "HIT";
                case MarkReaction.Flip:
                {
                    Splash(l, local);
                    float side = Mathf.Sign((float)m.Parts[0].U) * (m.Mirrored ? -1 : 1);
                    m.Mirrored = !m.Mirrored;
                    l.Goal += side > 0 ? -180 : 180;
                    return "HIT";
                }
                case MarkReaction.Fall:
                    Splash(l, local);
                    m.Gone = true;
                    l.Spin = 2.5f + energy;
                    l.Drop = 0.0001f;
                    return "DOWN";
                case MarkReaction.Knock:
                    m.Gone = true;
                    Knock(l, impact);
                    return "HIT";
                case MarkReaction.Break:
                    m.Gone = true;
                    Burst(l, impact);
                    return "HIT";
                default:
                {
                    // Paper: a hole, and the score.
                    var dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    dot.name = "Hole";
                    Destroy(dot.GetComponent<Collider>());
                    dot.transform.SetParent(l.Body, false);
                    dot.transform.localPosition = new Vector3(local.x, local.y, m.Kind == "bullseye" ? -0.02f : -0.016f);
                    dot.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                    dot.transform.localScale = Vector3.one * 0.016f;
                    var r = dot.GetComponent<MeshRenderer>();
                    r.sharedMaterial = hole;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (m.Kind == "bullseye") return hit.Score == 11 ? "X RING" : hit.Score > 0 ? $"{hit.Score} RING" : "PAPER";
                    double u = System.Math.Abs(hit.U), v = hit.V;
                    if ((u <= 0.075 && v >= 0.22 && v <= 0.5) || (u <= 0.05 && v >= 0.62 && v <= 0.7)) return "A ZONE";
                    if (u <= 0.15 && v >= 0.12 && v <= 0.58 || v > 0.58) return "C ZONE";
                    return "D ZONE";
                }
            }
        }

        /// <summary>A bare-steel splash where the lead struck the paint.</summary>
        void Splash(Live l, Vector3 local)
        {
            var mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mark.name = "Strike";
            Destroy(mark.GetComponent<Collider>());
            mark.transform.SetParent(l.Body, false);
            mark.transform.localPosition = new Vector3(local.x, local.y, -0.022f);
            mark.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            mark.transform.localScale = new Vector3(0.05f, 0.04f, 1);
            var r = mark.GetComponent<MeshRenderer>();
            r.sharedMaterial = lead;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Bit Fly(Mesh mesh, Material mat, Vector3 at, Vector3 v, float size, float floor, bool fade)
        {
            var go = new GameObject("Bit");
            go.transform.SetParent(holder, false);
            go.transform.position = at;
            go.transform.rotation = Random.rotation;
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var b = new Bit { T = go.transform, V = v, Turn = Random.insideUnitSphere * 900, Floor = floor, Fade = fade };
            bits.Add(b);
            return b;
        }

        float FloorUnder(Live l) => (float)field.Stations[l.Mark.Station].Ground;

        /// <summary>A can thrown off its post: up and back, tumbling, to lie in the grass behind.</summary>
        void Knock(Live l, Impact impact)
        {
            var can = l.Body.GetComponentInChildren<MeshFilter>();
            if (can == null) return;
            var t = can.transform;
            t.SetParent(holder, true);
            var back = l.Root.forward;
            var b = new Bit { T = t, V = back * Random.Range(2.5f, 4.5f) + Vector3.up * Random.Range(2f, 3.5f) + l.Root.right * Random.Range(-0.8f, 0.8f), Turn = Random.insideUnitSphere * 700, Floor = FloorUnder(l) + 0.03f };
            bits.Add(b);
        }

        /// <summary>Bottles shatter, clays break, balloons pop, jugs and melons burst.</summary>
        void Burst(Live l, Impact impact)
        {
            var m = l.Mark;
            var centre = l.Body.TransformPoint(new Vector3(0, (float)(m.Parts[0].Bounds().v0 + m.Parts[0].Bounds().v1) / 2, 0));
            var away = l.Root.forward;
            float floor = FloorUnder(l);
            Material? keep = null;
            var r = l.Body.GetComponentInChildren<MeshRenderer>();
            if (r != null) keep = r.sharedMaterial;
            l.Body.gameObject.SetActive(false);
            int count;
            switch (m.Material)
            {
                case MarkMaterial.Glass:
                    count = 16;
                    for (int i = 0; i < count; i++)
                        Fly(shardMesh, keep ?? glass[0], centre + Random.insideUnitSphere * 0.04f, away * Random.Range(1f, 4f) + Random.insideUnitSphere * 2.2f + Vector3.up * 1.5f, Random.Range(0.012f, 0.04f), floor + 0.005f, false);
                    break;
                case MarkMaterial.Clay:
                    for (int i = 0; i < 9; i++)
                        Fly(chipMesh, clay, centre + Random.insideUnitSphere * 0.03f, away * Random.Range(0.3f, 1.5f) + Random.insideUnitSphere * 2.5f + Vector3.up * 1.2f, Random.Range(0.012f, 0.03f), floor + 0.01f, false);
                    Effects.Impact(centre, false, false, holder);
                    break;
                case MarkMaterial.Rubber:
                    for (int i = 0; i < 5; i++)
                        Fly(shardMesh, keep ?? balloons[0], centre + Random.insideUnitSphere * 0.1f, Random.insideUnitSphere * 1.5f, Random.Range(0.02f, 0.05f), floor + 0.01f, false);
                    break;
                case MarkMaterial.Water:
                    for (int i = 0; i < 26; i++)
                        Fly(chipMesh, rubberString, centre + Random.insideUnitSphere * 0.05f, away * Random.Range(0.5f, 2.5f) + Random.insideUnitSphere * 2f + Vector3.up * 2f, Random.Range(0.01f, 0.03f), floor, true);
                    Remains(l, jug, new Vector3(0.17f, 0.05f, 0.17f), 0.03f);
                    break;
                case MarkMaterial.Fruit:
                    for (int i = 0; i < 14; i++)
                        Fly(chipMesh, i % 3 == 0 ? melon : melonFlesh, centre + Random.insideUnitSphere * 0.06f, away * Random.Range(0.5f, 3f) + Random.insideUnitSphere * 2.5f + Vector3.up * 2f, Random.Range(0.025f, 0.06f), floor + 0.01f, false);
                    Remains(l, melonFlesh, new Vector3(0.2f, 0.06f, 0.14f), 0.04f);
                    break;
            }
        }

        /// <summary>What's left on the table: a flattened jug, a smashed half melon.</summary>
        void Remains(Live l, Material mat, Vector3 size, float h)
        {
            var go = new GameObject("Remains");
            go.transform.SetParent(l.Root, false);
            go.transform.localPosition = new Vector3(Random.Range(-0.03f, 0.03f), h / 2, Random.Range(0, 0.05f));
            go.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360f), Random.Range(-10f, 10f));
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = chipMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ---- motion ----------------------------------------------------------------------------

        void Update()
        {
            float dt = Mathf.Min(0.05f, Time.deltaTime);
            Now += dt;
            foreach (var l in live)
            {
                var m = l.Mark;
                if (l.Moving) l.Carrier.localPosition = new Vector3((float)m.Offset(Now), 0, 0);
                switch (m.Reaction)
                {
                    case MarkReaction.Swing:
                    {
                        if (l.Angle == 0 && l.Spin == 0) break;
                        // A pendulum on its chains, settling within a few seconds; the mover's plate just shudders.
                        float k = m.Kind == "mover" ? 120 : 9.81f / Mathf.Max(0.3f, (float)m.PivotV);
                        float c = m.Kind == "mover" ? 9 : 1.1f;
                        l.Spin += (-k * Mathf.Sin(l.Angle) - c * l.Spin) * dt;
                        l.Angle += l.Spin * dt;
                        if (Mathf.Abs(l.Angle) < 1e-4f && Mathf.Abs(l.Spin) < 1e-3f) l.Angle = l.Spin = 0;
                        // Struck from the front, the face swings away down-range, the foot kicking back.
                        l.Pivot.localRotation = Quaternion.Euler(-l.Angle * Mathf.Rad2Deg, 0, 0);
                        break;
                    }
                    case MarkReaction.Spin:
                    {
                        if (l.Angle == 0 && l.Spin == 0) break;
                        // Bottom-heavy: it whirls over and over, slows, rocks and comes to rest upright.
                        l.Spin += (-22 * Mathf.Sin(l.Angle) - 0.6f * l.Spin) * dt;
                        l.Angle += l.Spin * dt;
                        float wrapped = Mathf.Repeat(l.Angle + Mathf.PI, 2 * Mathf.PI) - Mathf.PI;
                        if (Mathf.Abs(wrapped) < 2e-3f && Mathf.Abs(l.Spin) < 2e-2f) l.Angle = l.Spin = 0;
                        l.Pivot.localRotation = Quaternion.Euler(l.Angle * Mathf.Rad2Deg, 0, 0);
                        break;
                    }
                    case MarkReaction.Flip:
                    {
                        if (Mathf.Approximately(l.Angle, l.Goal)) break;
                        l.Angle = Mathf.MoveTowards(l.Angle, l.Goal, 900 * dt);
                        l.Pivot.localRotation = Quaternion.Euler(0, l.Angle, 0);
                        break;
                    }
                    case MarkReaction.Fall:
                    {
                        if (l.Drop <= 0) break;
                        // Over backwards on its hinge, then (off a rail or a stake) down into the grass behind.
                        const float Flat = 1.45f;
                        if (l.Angle < Flat)
                        {
                            l.Spin += (14 * Mathf.Sin(l.Angle) + 3) * dt;
                            l.Angle = Mathf.Min(Flat, l.Angle + l.Spin * dt);
                            if (l.Angle >= Flat) l.Spin = -0.25f * l.Spin;
                        }
                        else if (l.Spin < 0)
                        {
                            l.Angle += l.Spin * dt;
                            l.Spin += 8 * dt;
                            if (l.Angle >= Flat) l.Spin = 0;
                        }
                        l.Pivot.localRotation = Quaternion.Euler(l.Angle * Mathf.Rad2Deg, 0, 0);
                        float height = l.Root.position.y - FloorUnder(l);
                        if (height > 0.2f && l.Angle > 0.9f && l.Drop < height - 0.02f)
                        {
                            l.DropSpeed += 9.81f * dt;
                            l.Drop = Mathf.Min(height - 0.02f, l.Drop + l.DropSpeed * dt);
                            l.Back = Mathf.Min(0.35f, l.Back + 1.2f * dt);
                            l.Carrier.localPosition = new Vector3(0, -l.Drop, l.Back);
                        }
                        break;
                    }
                }
            }
            for (int i = bits.Count - 1; i >= 0; i--)
            {
                var b = bits[i];
                if (b.T == null)
                {
                    bits.RemoveAt(i);
                    continue;
                }
                if (b.V == Vector3.zero) continue;
                b.V += Vector3.down * 9.81f * dt;
                var p = b.T.position + b.V * dt;
                b.T.Rotate(b.Turn * dt, Space.Self);
                if (p.y <= b.Floor)
                {
                    p.y = b.Floor;
                    if (b.Fade)
                    {
                        Destroy(b.T.gameObject);
                        bits.RemoveAt(i);
                        continue;
                    }
                    // A small bounce, then it lies where it fell.
                    if (b.V.y < -1.5f) b.V = new Vector3(b.V.x * 0.4f, -b.V.y * 0.25f, b.V.z * 0.4f);
                    else b.V = Vector3.zero;
                    b.Turn *= 0.3f;
                }
                b.T.position = p;
            }
        }

        /// <summary>The impact sound for a struck target.</summary>
        public static Sound.Surface SurfaceOf(Mark m) => m.Material switch
        {
            MarkMaterial.Steel => Sound.Surface.Metal,
            MarkMaterial.Glass => Sound.Surface.Glass,
            MarkMaterial.Tin => Sound.Surface.Tin,
            MarkMaterial.Clay => Sound.Surface.Clay,
            MarkMaterial.Rubber => Sound.Surface.Pop,
            MarkMaterial.Water => Sound.Surface.Water,
            MarkMaterial.Fruit => Sound.Surface.Flesh,
            _ => Sound.Surface.Dirt,
        };
    }
}
