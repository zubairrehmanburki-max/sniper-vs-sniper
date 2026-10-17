using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The sniper figure: a rigged soldier posed per stance, frozen into static meshes, with the
    /// rifle in his hands. Faces +z; the caller turns it. The origin is on the ground under the head
    /// when prone, under the hips otherwise.
    /// </summary>
    public sealed partial class Figure : MonoBehaviour
    {
        /// <summary>
        /// A stance as directions in the figure's frame (+z ahead, +y up, +x right): which way the
        /// body's long axis and chest point, then for each joint the way its bone should run to the
        /// next joint. Each bone is swung onto its direction from wherever the model's rest pose has
        /// it, so any Mixamo-rigged model takes the pose regardless of its bind pose or joint axes.
        /// </summary>
        sealed class Pose
        {
            public Vector3 Up = Vector3.up, Chest = Vector3.forward;
            public (string bone, string next, Vector3 dir)[] Limbs = System.Array.Empty<(string, string, Vector3)>();
        }

        static readonly (string, string, Vector3)[] AimArms =
        {
            // Right elbow out at shoulder height, hand back at the grip by the cheek; left arm
            // reaching forward under the handguard.
            ("RightArm", "RightForeArm", new Vector3(0.6f, -0.35f, 0.45f)), ("RightForeArm", "RightHand", new Vector3(-0.65f, 0.25f, 0.7f)),
            ("LeftArm", "LeftForeArm", new Vector3(-0.3f, -0.5f, 0.8f)), ("LeftForeArm", "LeftHand", new Vector3(0.2f, 0.25f, 1f)),
        };

        static readonly Dictionary<Stance, Pose> Poses = new Dictionary<Stance, Pose>
        {
            [Stance.Standing] = new Pose
            {
                Limbs = Join(new[]
                {
                    ("Spine", "Spine1", new Vector3(0, 1, 0.03f)), ("Spine1", "Spine2", new Vector3(0, 1, 0.05f)), ("Spine2", "Neck", new Vector3(0, 1, 0.08f)),
                    ("Neck", "Head", new Vector3(0, 1, 0.15f)), ("Head", "HeadTop_End", new Vector3(0.05f, 1, 0.2f)),
                    ("RightUpLeg", "RightLeg", new Vector3(0.12f, -1, -0.04f)), ("RightLeg", "RightFoot", new Vector3(0.1f, -1, -0.06f)), ("RightFoot", "RightToeBase", new Vector3(0.3f, -0.5f, 1)),
                    ("LeftUpLeg", "LeftLeg", new Vector3(-0.1f, -1, 0.14f)), ("LeftLeg", "LeftFoot", new Vector3(-0.08f, -1, 0.04f)), ("LeftFoot", "LeftToeBase", new Vector3(-0.1f, -0.5f, 1)),
                }, AimArms),
            },
            [Stance.Crouch] = new Pose
            {
                // Kneeling on the right knee, left foot planted forward, leaning into the rifle.
                Limbs = Join(new[]
                {
                    ("Spine", "Spine1", new Vector3(0, 1, 0.3f)), ("Spine1", "Spine2", new Vector3(0, 1, 0.2f)), ("Spine2", "Neck", new Vector3(0, 1, 0.12f)),
                    ("Neck", "Head", new Vector3(0, 1, 0.2f)), ("Head", "HeadTop_End", new Vector3(0.05f, 1, 0.25f)),
                    ("RightUpLeg", "RightLeg", new Vector3(0.15f, -1, 0.1f)), ("RightLeg", "RightFoot", new Vector3(0.05f, -0.12f, -1)), ("RightFoot", "RightToeBase", new Vector3(0, -1, -0.3f)),
                    ("LeftUpLeg", "LeftLeg", new Vector3(-0.2f, -0.1f, 1)), ("LeftLeg", "LeftFoot", new Vector3(-0.05f, -1, 0.08f)), ("LeftFoot", "LeftToeBase", new Vector3(-0.1f, -0.4f, 1)),
                }, AimArms),
            },
            [Stance.Prone] = new Pose
            {
                // Flat on his front along +z, propped on the elbows, legs spread, toes dug in.
                Up = Vector3.forward,
                Chest = Vector3.down,
                Limbs = new[]
                {
                    ("Spine", "Spine1", new Vector3(0, 0.04f, 1)), ("Spine1", "Spine2", new Vector3(0, 0.1f, 1)), ("Spine2", "Neck", new Vector3(0, 0.2f, 1)),
                    ("Neck", "Head", new Vector3(0, 0.5f, 1)), ("Head", "HeadTop_End", new Vector3(0.05f, 0.35f, 1)),
                    ("RightArm", "RightForeArm", new Vector3(0.4f, -0.55f, 0.7f)), ("RightForeArm", "RightHand", new Vector3(-0.35f, 0.1f, 1)),
                    ("LeftArm", "LeftForeArm", new Vector3(-0.3f, -0.55f, 0.8f)), ("LeftForeArm", "LeftHand", new Vector3(0.15f, 0.05f, 1)),
                    ("RightUpLeg", "RightLeg", new Vector3(0.22f, 0, -1)), ("RightLeg", "RightFoot", new Vector3(0.25f, 0, -1)), ("RightFoot", "RightToeBase", new Vector3(0.1f, -0.8f, -0.5f)),
                    ("LeftUpLeg", "LeftLeg", new Vector3(-0.15f, 0, -1)), ("LeftLeg", "LeftFoot", new Vector3(-0.18f, 0, -1)), ("LeftFoot", "LeftToeBase", new Vector3(-0.1f, -0.8f, -0.5f)),
                },
            },
        };

        static (string, string, Vector3)[] Join((string, string, Vector3)[] a, (string, string, Vector3)[] b)
        {
            var r = new (string, string, Vector3)[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }

        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        /// <summary>Joints by Mixamo name without the prefix ("Hips", "Spine2", "LeftForeArm"...).</summary>
        public IReadOnlyDictionary<string, Transform> Bones => bones;
        /// <summary>The soldier model's root (the rifle sits outside it).</summary>
        public Transform Body = null!;
        readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
        /// <summary>
        /// Makes a soldier in a stance. A live one stays skinned and can be driven (Drive) to walk,
        /// crouch-walk, crawl and change stance with a blend; otherwise the pose is frozen into
        /// static meshes, which is all the enemy and the menus need.
        /// </summary>
        /// <param name="soldier">The soldier model (one of the asset set's soldier_* ids); the tactical one by default.</param>
        /// <param name="rifle">The rifle model (see <see cref="RifleModel"/>); the L115A3 by default.</param>
        public static Figure Create(Stance stance, float heightM, Transform parent, Color tint, bool live = false, string? soldier = null, string? rifle = null)
        {
            var root = new GameObject("Figure " + stance);
            root.transform.SetParent(parent, false);
            var fig = root.AddComponent<Figure>();
            var pose = Poses[stance];
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            fig.Body = body;
            // The detailed CC-BY soldier when the asset set has it, else the bundled low-detail one.
            string id = soldier ?? DefaultSoldier;
            var hd = Library.Model(id);
            if (hd == null && id != DefaultSoldier) hd = Library.Model(id = DefaultSoldier);
            var prefab = hd != null ? hd : Resources.Load<GameObject>("Characters/Soldier");
            Transform? model = null;
            if (prefab != null)
            {
                model = Instantiate(prefab, body).transform;
                model.name = "Soldier";
                foreach (var anim in model.GetComponentsInChildren<Animation>()) anim.enabled = false;
                foreach (var animator in model.GetComponentsInChildren<Animator>()) animator.enabled = false;
                foreach (var t in model.GetComponentsInChildren<Transform>())
                {
                    string n = BoneName(t.name);
                    if (CcBones.TryGetValue(n, out var mixamo)) n = mixamo;
                    if (!fig.bones.ContainsKey(n)) fig.bones[n] = t;
                }
                fig.Dress(model, hd != null, tint);
                // Only the tactical soldier's helmet floats off his head as imported.
                if (hd == null || id == DefaultSoldier) fig.Headgear();
            }
            else Debug.LogWarning("[Figure] no soldier model loaded");
            Debug.Log($"[Figure] {stance}: {fig.bones.Count} bones, {root.GetComponentsInChildren<Renderer>().Length} renderers");

            if (model != null && live && fig.bones.ContainsKey("Hips"))
            {
                fig.Fit(model, heightM);
                fig.Remember(model);
                fig.rifle = Rifle(root.transform, rifle).transform;
                fig.Calibrate();
                fig.want = stance;
                fig.weight[(int)stance] = 1;
                fig.Animate(0);
                return fig;
            }
            if (model != null)
            {
                fig.Fit(model, stance == Stance.Prone ? 1.75f : heightM);
                fig.Strike(model, pose, stance == Stance.Prone ? 1 : 0);
            }
            fig.Bake();

            // Ground him: the lowest point of the posed body on the road (which sits 4 cm proud).
            float ground = root.transform.position.y + 0.04f;
            var b = Library.BoundsOf(body.gameObject);
            body.position += new Vector3(0, ground - b.min.y, 0);

            // The rifle in his hands: grip at the right hand, aimed straight down-range.
            var gun = Rifle(root.transform, rifle);
            if (fig.bones.TryGetValue("RightHand", out var rh))
            {
                var dir = root.transform.forward;
                gun.transform.rotation = Quaternion.LookRotation(dir, root.transform.up);
                gun.transform.position = rh.position + dir * 0.28f + root.transform.up * 0.03f;
                if (stance == Stance.Prone && Library.BoundsOf(gun).min.y < ground)
                    gun.transform.position += new Vector3(0, ground - Library.BoundsOf(gun).min.y, 0);
            }
            else gun.transform.localPosition = new Vector3(0.1f, stance == Stance.Prone ? 0.15f : 1.3f, 0.5f);
            return fig;
        }

        /// <summary>Gives each skin a URP Lit material built from its imported one.</summary>
        void Dress(Transform model, bool hd, Color tint)
        {
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                // On the same URP Lit template as the structures: the imported glTF materials draw
                // nothing on some renderers. The old soldier is tinted olive drab; the detailed one
                // keeps its own colours and normal maps.
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = r.sharedMaterials[i];
                    var tex = Library.Lean(src == null ? null : src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : src.mainTexture);
                    var mat = new Material(Library.Template("Lit")) { name = "Soldier " + r.name };
                    mat.SetTexture("_BaseMap", tex);
                    mat.SetFloat("_Metallic", 0);
                    // The Lit template reads metallic from a map; without one it defaults to white,
                    // fully metallic, and the cloth turns to chrome.
                    mat.SetTexture("_MetallicGlossMap", NonMetal);
                    if (hd)
                    {
                        // The model's own maps (ghillie fringes stay cut out), as matte fabric: the
                        // sheen of a wet road is wrong on cloth. The outfit colour tints it a little.
                        Destroy(mat);
                        mat = Library.Pbr(src, 0, 0.18f);
                        mat.name = "Soldier " + r.name;
                        mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * Color.Lerp(Color.white, tint * 1.5f, 0.25f));
                    }
                    else
                    {
                        mat.SetColor("_BaseColor", r.name.Contains("visor") ? new Color(0.17f, 0.19f, 0.17f) : tint);
                        mat.SetFloat("_Smoothness", 0.12f);
                    }
                    mats[i] = mat;
                }
                r.sharedMaterials = mats;
                skins.Add(r);
            }
        }

        /// <summary>
        /// Sizes and poses the model: measures its rest pose (which way is up, right and the chest),
        /// scales it to the height, turns the whole body into the stance's frame, then swings each
        /// limb bone, parents first, onto its direction.
        /// </summary>
        void Fit(Transform model, float height)
        {
            if (!bones.TryGetValue("Hips", out var hips) || !bones.TryGetValue("Head", out var head)) return;
            Vector3 up = (head.position - hips.position).normalized;
            // Height: crown to sole along the body's up axis.
            float top = bones.TryGetValue("HeadTop_End", out var crown) ? Vector3.Dot(crown.position, up) : Vector3.Dot(head.position, up) + 0.12f * 1.7f;
            float sole = float.MaxValue;
            foreach (var n in new[] { "LeftToeBase", "RightToeBase", "LeftFoot", "RightFoot", "LeftToe_End", "RightToe_End" })
                if (bones.TryGetValue(n, out var f)) sole = Mathf.Min(sole, Vector3.Dot(f.position, up));
            float rest = top - sole;
            if (rest > 1e-4f && sole < float.MaxValue) model.localScale *= height / (rest * 1.04f);
        }

        /// <summary>
        /// Turns the model, from its rest pose, into the pose's frame and swings each limb onto its
        /// direction, then stands it over the origin: under the hips, or under the head as
        /// `proneAnchor` goes to 1.
        /// </summary>
        void Strike(Transform model, Pose pose, float proneAnchor)
        {
            if (!bones.TryGetValue("Hips", out var hips) || !bones.TryGetValue("Head", out var head)
                || !bones.TryGetValue("LeftUpLeg", out var lul) || !bones.TryGetValue("RightUpLeg", out var rul)) return;
            var frame = transform;
            Vector3 up = (head.position - hips.position).normalized;
            Vector3 right = Vector3.ProjectOnPlane(rul.position - lul.position, up).normalized;
            Vector3 chest = Vector3.Cross(right, up);

            var have = Quaternion.LookRotation(chest, up);
            var want = Quaternion.LookRotation(frame.TransformDirection(pose.Chest), frame.TransformDirection(pose.Up));
            var turn = want * Quaternion.Inverse(have);
            var pivot = hips.position;
            model.rotation = turn * model.rotation;
            model.position = pivot + turn * (model.position - pivot);

            foreach (var (bone, next, dir) in pose.Limbs)
            {
                if (!bones.TryGetValue(bone, out var b) || !bones.TryGetValue(next, out var c)) continue;
                var now = c.position - b.position;
                if (now.sqrMagnitude < 1e-8f) continue;
                b.rotation = Quaternion.FromToRotation(now, frame.TransformDirection(dir.normalized)) * b.rotation;
            }

            // Put him over his origin: under the head when prone, under the hips otherwise.
            var anchor = Vector3.Lerp(hips.position, head.position, proneAnchor);
            var shift = frame.position - anchor;
            model.position += new Vector3(shift.x, 0, shift.z);
        }

        public const string DefaultSoldier = "soldier_tactical";

        /// <summary>Character Creator joints by the Mixamo names the poses use.</summary>
        static readonly Dictionary<string, string> CcBones = CcMap();

        static Dictionary<string, string> CcMap()
        {
            var m = new Dictionary<string, string>
            {
                ["CC_Base_Hip"] = "Hips", ["CC_Base_Waist"] = "Spine", ["CC_Base_Spine01"] = "Spine1",
                ["CC_Base_Spine02"] = "Spine2", ["CC_Base_NeckTwist01"] = "Neck", ["CC_Base_Head"] = "Head",
            };
            foreach (var (cc, side) in new[] { ("L", "Left"), ("R", "Right") })
            {
                m[$"CC_Base_{cc}_Clavicle"] = side + "Shoulder";
                m[$"CC_Base_{cc}_Upperarm"] = side + "Arm";
                m[$"CC_Base_{cc}_Forearm"] = side + "ForeArm";
                m[$"CC_Base_{cc}_Hand"] = side + "Hand";
                m[$"CC_Base_{cc}_Thigh"] = side + "UpLeg";
                m[$"CC_Base_{cc}_Calf"] = side + "Leg";
                m[$"CC_Base_{cc}_Foot"] = side + "Foot";
                m[$"CC_Base_{cc}_ToeBase"] = side + "ToeBase";
            }
            return m;
        }

        /// <summary>
        /// A rifle model as the asset set has it: its real length in metres and parts to leave off
        /// (night sights).
        /// </summary>
        sealed class RifleSpec
        {
            public float Length;
            public string[] Hide = System.Array.Empty<string>();
        }

        static readonly Dictionary<string, RifleSpec> Armory = new Dictionary<string, RifleSpec>
        {
            ["rifle_m40a5"] = new RifleSpec { Length = 1.12f },
            ["rifle_fn_spr"] = new RifleSpec { Length = 1.06f },
            ["rifle_l115a3_hd"] = new RifleSpec { Length = 1.3f, Hide = new[] { "night_" } },
            ["rifle_hecate"] = new RifleSpec { Length = 1.38f },
            ["rifle_sv98m"] = new RifleSpec { Length = 1.2f },
            ["rifle_mosin"] = new RifleSpec { Length = 1.23f },
            ["rifle_timberwolf"] = new RifleSpec { Length = 1.2f },
            ["rifle_l118a1"] = new RifleSpec { Length = 1.18f },
        };

        /// <summary>Every rifle model the asset set offers, for the range's display benches.</summary>
        public static IEnumerable<string> RifleModels => Armory.Keys;

        /// <summary>The model a rifle (config id, fitted or not) is drawn with.</summary>
        public static string RifleModel(string rifleId)
        {
            int plus = rifleId.IndexOf('+');
            if (plus >= 0) rifleId = rifleId.Substring(0, plus);
            return rifleId switch
            {
                "bolt308" => "rifle_m40a5",
                "bolt65cm" => "rifle_fn_spr",
                "am50" => "rifle_hecate",
                _ => "rifle_l115a3_hd",
            };
        }

        /// <summary>
        /// A sniper rifle with its barrel along +z, centred on `parent`: the model named (see
        /// <see cref="RifleModel"/>) in its own textures at its real length, else the older L115A3
        /// repainted in a service finish, else the scanned bolt-action rifle. Used by the figures,
        /// the first-person view and the range's benches.
        /// </summary>
        public static GameObject Rifle(Transform parent, string? model = null)
        {
            model ??= "rifle_l115a3_hd";
            if (Armory.TryGetValue(model, out var spec) && Library.Model(model) != null) return Textured(parent, model, spec);
            bool hd = Library.Model("rifle_l115a3") != null;
            var rifle = Library.Place(hd ? "rifle_l115a3" : "bolt_action_rifle_7_62", Vector3.zero, 0, 1, parent);
            var inner = new GameObject("Rifle").transform;
            inner.SetParent(parent, false);
            rifle.transform.SetParent(inner, false);
            if (hd) Freeze(rifle);
            var rb = Library.BoundsOf(rifle);
            // The long axis may run along x; turn it so it runs along z.
            if (rb.size.x > rb.size.z) rifle.transform.localRotation = Quaternion.Euler(0, 90, 0);
            rb = Library.BoundsOf(rifle);
            float len = Mathf.Max(rb.size.x, rb.size.z);
            if (hd && len > 1e-3f) rifle.transform.localScale *= 1.3f / len;
            // The barrel end is the slim one: if the bulk (stock) is at +z, turn it round.
            if (hd && StockAhead(rifle, inner)) rifle.transform.localRotation = Quaternion.Euler(0, 180, 0) * rifle.transform.localRotation;
            if (hd) Finish(rifle, inner);
            rb = Library.BoundsOf(rifle);
            // Centre it on its parent.
            rifle.transform.position -= rb.center - inner.position;
            return inner.gameObject;
        }

        /// <summary>
        /// One of the textured rifles: loose parts set beside it in the scene dropped, turned
        /// muzzle-forward, sized to its real length, its own maps on URP Lit, and the stock's
        /// materials marked so <see cref="PaintRifle"/> can tint them.
        /// </summary>
        static GameObject Textured(Transform parent, string id, RifleSpec spec)
        {
            var inner = new GameObject("Rifle").transform;
            inner.SetParent(parent, false);
            // Built at the parent's origin, unrotated, so the rifle's own frame is measured.
            var rifle = Library.Prop(id, inner, metal: 0.75f);
            rifle.transform.localPosition = Vector3.zero;
            var rs = rifle.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rs)
                foreach (var h in spec.Hide)
                    if (r.name.StartsWith(h, System.StringComparison.OrdinalIgnoreCase)) r.enabled = false;
            // Loose parts (spare rounds, a magazine laid alongside): anything clear of the main body.
            Renderer? main = null;
            float most = 0;
            foreach (var r in rs)
            {
                if (!r.enabled) continue;
                var sz = r.bounds.size;
                float v = sz.x + sz.y + sz.z;
                if (v > most) { most = v; main = r; }
            }
            if (main != null)
            {
                var body = main.bounds;
                body.Expand(body.size * 0.08f);
                foreach (var r in rs)
                    if (r.enabled && r != main && !body.Intersects(r.bounds)) r.enabled = false;
            }
            foreach (var r in rs)
            {
                if (r.enabled) continue;
                var mf = r.GetComponent<MeshFilter>();
                DestroyImmediate(r);
                if (mf != null) DestroyImmediate(mf);
            }
            Orient(rifle, inner);
            var b = Enabled(rifle);
            if (b.size.z > 1e-4f) rifle.transform.localScale *= spec.Length / b.size.z;
            b = Enabled(rifle);
            rifle.transform.position -= b.center - inner.position;

            // Mark the stock: by name where the model names its parts, else by colour, unless it is
            // one material (a single atlas tells nothing apart).
            int materials = 0;
            foreach (var r in rifle.GetComponentsInChildren<Renderer>()) materials += r.sharedMaterials.Length;
            foreach (var r in rifle.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    var part = ByName(m.name + " " + r.name) ?? (materials > 1 ? ByColour(MeanColour(m)) : Part.Metal);
                    m.name = $"Rifle {part} (textured) {m.name}";
                }
            }
            return inner.gameObject;
        }

        /// <summary>
        /// Lays a rifle model level along +z of `frame` whatever axes it was exported with: its
        /// long axis onto z, its thin side across x, the bulky stock end to the rear, and the
        /// stock hanging below the bore (the butt drops below the barrel's line on every rifle).
        /// </summary>
        static void Orient(GameObject rifle, Transform frame)
        {
            var t = rifle.transform;
            var b = Enabled(rifle);
            if (b.size.y > b.size.x && b.size.y > b.size.z) t.localRotation = Quaternion.Euler(90, 0, 0) * t.localRotation;
            else if (b.size.x > b.size.z) t.localRotation = Quaternion.Euler(0, 90, 0) * t.localRotation;
            b = Enabled(rifle);
            if (b.size.x > b.size.y) t.localRotation = Quaternion.Euler(0, 0, 90) * t.localRotation;
            if (StockAhead(rifle, frame)) t.localRotation = Quaternion.Euler(0, 180, 0) * t.localRotation;
            // Upright: the rear of the rifle (the stock) sits lower than its muzzle.
            float zMin = float.MaxValue, zMax = float.MinValue;
            var pts = new List<Vector3>();
            foreach (var mf in rifle.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable || mf.GetComponent<Renderer>() == null) continue;
                var v = mf.sharedMesh.vertices;
                for (int i = 0; i < v.Length; i += 3)
                {
                    var p = frame.InverseTransformPoint(mf.transform.TransformPoint(v[i]));
                    pts.Add(p);
                    zMin = Mathf.Min(zMin, p.z);
                    zMax = Mathf.Max(zMax, p.z);
                }
            }
            if (pts.Count == 0) return;
            float len = zMax - zMin, rear = 0, front = 0;
            int nr = 0, nf = 0;
            foreach (var p in pts)
            {
                if (p.z < zMin + len * 0.15f) { rear += p.y; nr++; }
                else if (p.z > zMax - len * 0.08f) { front += p.y; nf++; }
            }
            if (nr > 0 && nf > 0 && rear / nr > front / nf) t.localRotation = Quaternion.Euler(0, 0, 180) * t.localRotation;
        }

        /// <summary>Bounds of the renderers still shown under an object.</summary>
        static Bounds Enabled(GameObject go)
        {
            Bounds? b = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (b is Bounds have) { have.Encapsulate(r.bounds); b = have; }
                else b = r.bounds;
            }
            return b ?? new Bounds(go.transform.position, Vector3.zero);
        }

        /// <summary>
        /// Turns a model's skinned meshes into static ones (they draw nothing in the capture build),
        /// keeping the imported materials for Finish to read.
        /// </summary>
        internal static void Freeze(GameObject go)
        {
            foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = skin.sharedMaterials;
                var mesh = new Mesh { name = skin.name + " (frozen)" };
                skin.BakeMesh(mesh, true);
                mesh.RecalculateBounds();
                var host = skin.gameObject;
                DestroyImmediate(skin);
                host.AddComponent<MeshFilter>().sharedMesh = mesh;
                host.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
        }

        enum Part { Furniture, Metal, Glass }

        static readonly string[] GlassWords = { "lens", "glass" };
        static readonly string[] MetalWords = { "barrel", "scope", "bolt", "metal", "steel", "rail", "bipod", "mag", "trigger", "muzzle", "brake", "suppress", "ring", "mount", "screw", "receiver", "action", "black" };
        static readonly string[] FurnitureWords = { "stock", "wood", "grip", "cheek", "butt", "forend", "fore_end", "handguard", "polymer", "furniture" };

        static Part? ByName(string name)
        {
            name = name.ToLowerInvariant();
            foreach (var w in GlassWords) if (name.Contains(w)) return Part.Glass;
            foreach (var w in FurnitureWords) if (name.Contains(w)) return Part.Furniture;
            foreach (var w in MetalWords) if (name.Contains(w)) return Part.Metal;
            return null;
        }

        /// <summary>Light warm (tan, salmon, wood) or green colours are the stock; dark and grey ones metal.</summary>
        static Part ByColour(Color c)
        {
            float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            bool warm = c.r - c.b > 0.06f && lum > 0.08f;
            bool green = c.g > c.b + 0.03f && c.g >= c.r * 0.85f && lum > 0.08f;
            return warm || green ? Part.Furniture : Part.Metal;
        }

        /// <summary>
        /// Gives the rifle a service finish: an olive-drab stock with a faint stipple and a dull
        /// sheen, dark gunmetal barrel, action, scope and bipod, near-black lenses. Parts are told
        /// apart by material or object name where the model names them, else per triangle by the
        /// colour the model gives it (texture, factor and vertex colour: the furniture is the light,
        /// warm or green part, metal the dark grey). A model with no usable colour, or one coloured
        /// all alike, is split by position: forward of the fore-end, and above the bore over the
        /// action (the scope), is metal. `frame` has the barrel along +z.
        /// </summary>
        static void Finish(GameObject go, Transform frame)
        {
            // The rifle's extent and bore height in the frame.
            float zMin = float.MaxValue, zMax = float.MinValue, muzzleY = 0;
            int muzzleN = 0;
            var filters = go.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    float z = frame.InverseTransformPoint(mf.transform.TransformPoint(v)).z;
                    zMin = Mathf.Min(zMin, z);
                    zMax = Mathf.Max(zMax, z);
                }
            }
            float len = Mathf.Max(1e-3f, zMax - zMin);
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = frame.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (p.z > zMax - len * 0.08f) { muzzleY += p.y; muzzleN++; }
                }
            }
            muzzleY = muzzleN > 0 ? muzzleY / muzzleN : 0;
            Part ByPlace(Vector3 p)
            {
                float t = (p.z - zMin) / len;
                return t > 0.64f || t > 0.3f && p.y > muzzleY + 0.035f ? Part.Metal : Part.Furniture;
            }

            // First pass: each triangle's part by name or colour, counting how much of the rifle
            // the colours make stock.
            var jobs = new List<(MeshFilter mf, Renderer r, List<int>[] tris, List<int>[] byColour, List<int> unknown)>();
            int furniture = 0, coloured = 0;
            var seen = new List<string>();
            foreach (var mf in filters)
            {
                var r = mf.GetComponent<Renderer>();
                var mesh = mf.sharedMesh;
                if (r == null || mesh == null) continue;
                var srcs = r.sharedMaterials;
                // Triangles by name (sure), by colour (likely) and with nothing to go by.
                var tris = new List<int>[3];
                var byColour = new List<int>[3];
                for (int k = 0; k < 3; k++)
                {
                    tris[k] = new List<int>();
                    byColour[k] = new List<int>();
                }
                var unknown = new List<int>();
                if (!mesh.isReadable)
                {
                    // Whole sub-meshes only: by name, else the material's mean colour.
                    var parts = new Material[srcs.Length];
                    for (int i = 0; i < srcs.Length; i++)
                    {
                        var part = ByName((srcs[i] != null ? srcs[i].name : "") + " " + r.name) ?? ByColour(MeanColour(srcs[i]));
                        parts[i] = RifleMaterial(part, srcs[i]);
                    }
                    r.sharedMaterials = parts;
                    continue;
                }
                var uv = mesh.uv;
                var colors = mesh.colors;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var src = s < srcs.Length ? srcs[s] : null;
                    var named = ByName((src != null ? src.name : "") + " " + r.name);
                    seen.Add($"{r.name}/{(src != null ? src.name : "-")}={(named?.ToString() ?? "?")}");
                    var factor = src != null && src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : src != null && src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white;
                    var tex = named == null ? Readable(BaseTexture(src)) : null;
                    bool informative = tex != null || colors.Length > 0 || factor != Color.white;
                    var ix = mesh.GetTriangles(s);
                    for (int t = 0; t + 2 < ix.Length; t += 3)
                    {
                        int a = ix[t], b = ix[t + 1], c = ix[t + 2];
                        List<int> into;
                        if (named != null) into = tris[(int)named.Value];
                        else if (!informative) into = unknown;
                        else
                        {
                            var col = factor;
                            if (tex != null && uv.Length > 0)
                            {
                                var m = (uv[a] + uv[b] + uv[c]) / 3;
                                col *= (tex.GetPixelBilinear(m.x, m.y) + tex.GetPixelBilinear(uv[a].x, uv[a].y)) * 0.5f;
                            }
                            if (colors.Length > 0) col *= (colors[a] + colors[b] + colors[c]) / 3;
                            var part = ByColour(col);
                            coloured++;
                            if (part == Part.Furniture) furniture++;
                            into = byColour[(int)part];
                        }
                        into.Add(a);
                        into.Add(b);
                        into.Add(c);
                    }
                }
                jobs.Add((mf, r, tris, byColour, unknown));
            }

            // Colours that make (nearly) everything stock, or none at all, tell nothing: then all
            // but the named parts go by place. Otherwise only the triangles with no colour do.
            bool byPlace = coloured == 0 || furniture > coloured * 0.85f;
            foreach (var (mf, r, tris, byColour, unknown) in jobs)
            {
                var mesh = mf.sharedMesh;
                for (int k = 0; k < 3; k++) (byPlace ? unknown : tris[k]).AddRange(byColour[k]);
                var verts = mesh.vertices;
                for (int t = 0; t + 2 < unknown.Count; t += 3)
                {
                    int a = unknown[t], b = unknown[t + 1], c = unknown[t + 2];
                    var into = tris[(int)ByPlace(frame.InverseTransformPoint(mf.transform.TransformPoint((verts[a] + verts[b] + verts[c]) / 3)))];
                    into.Add(a);
                    into.Add(b);
                    into.Add(c);
                }
                // One sub-mesh per part present, on a copy so the imported mesh is untouched.
                var copy = Instantiate(mesh);
                copy.name = mesh.name + " (finished)";
                var mats = new List<Material>();
                int n = 0;
                for (int k = 0; k < 3; k++) if (tris[k].Count > 0) n++;
                copy.subMeshCount = Mathf.Max(1, n);
                int sub = 0;
                var src0 = r.sharedMaterials.Length > 0 ? r.sharedMaterials[0] : null;
                for (int k = 0; k < 3; k++)
                {
                    if (tris[k].Count == 0) continue;
                    copy.SetTriangles(tris[k], sub++);
                    mats.Add(RifleMaterial((Part)k, src0));
                }
                if (mats.Count == 0) continue;
                mf.sharedMesh = copy;
                r.sharedMaterials = mats.ToArray();
            }
            Debug.Log($"[Figure] rifle finish: {string.Join(", ", seen)}; {furniture}/{coloured} triangles stock by colour{(byPlace ? ", split by place" : "")}");
        }

        static Texture? BaseTexture(Material? src) =>
            src == null ? null : src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture;

        static Color MeanColour(Material? src)
        {
            if (src == null) return Color.white;
            var c = src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white;
            var tex = Readable(BaseTexture(src));
            if (tex == null) return c;
            var px = tex.GetPixels32(tex.mipmapCount - 1);
            var sum = Color.black;
            foreach (var p in px) sum += (Color)p;
            return c * (sum / Mathf.Max(1, px.Length));
        }

        static readonly Dictionary<Texture, Texture2D?> Readables = new Dictionary<Texture, Texture2D?>();

        /// <summary>A small CPU-readable copy of a texture (imported ones usually aren't readable).</summary>
        static Texture2D? Readable(Texture? tex)
        {
            if (tex == null) return null;
            if (Readables.TryGetValue(tex, out var done)) return done;
            Texture2D? copy = null;
            try
            {
                const int S = 128;
                var rt = RenderTexture.GetTemporary(S, S, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                copy = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = tex.name + " (readable)" };
                copy.ReadPixels(new Rect(0, 0, S, S), 0, 0);
                copy.Apply(true);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Figure] can't read {tex.name}: {ex.Message}");
                copy = null;
            }
            Readables[tex] = copy;
            return copy;
        }

        static readonly Dictionary<Part, Material> RifleMats = new Dictionary<Part, Material>();
        static readonly Dictionary<(Material, Color), Material> Painted = new Dictionary<(Material, Color), Material>();

        /// <summary>Drops the cached rifle materials and readable texture copies (a map is being unloaded).</summary>
        public static void Forget()
        {
            Readables.Clear();
            RifleMats.Clear();
            Painted.Clear();
        }

        /// <summary>
        /// Repaints the rifle furniture (stock and grip) under `root` in a finish colour, keeping each
        /// material's maps; the metal and glass stay as they are.
        /// </summary>
        public static void PaintRifle(GameObject root, Color finish)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.name.StartsWith("Rifle " + Part.Furniture)) continue;
                    if (!Painted.TryGetValue((m, finish), out var p) || p == null)
                    {
                        p = new Material(m) { name = m.name + " (finish)" };
                        // A textured stock keeps its grain and wear, toned toward the finish.
                        bool textured = m.name.Contains("(textured)");
                        p.SetColor("_BaseColor", textured ? m.GetColor("_BaseColor") * Color.Lerp(Color.white, finish * 2.2f, 0.7f) : finish);
                        Painted[(m, finish)] = p;
                    }
                    mats[i] = p;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>The finish for one part, keeping the model's normal map if it has one.</summary>
        static Material RifleMaterial(Part part, Material? src)
        {
            var nor = Library.Lean(src != null && src.HasProperty("normalTexture") ? src.GetTexture("normalTexture") : null);
            if (nor == null && RifleMats.TryGetValue(part, out var shared) && shared != null) return shared;
            var m = new Material(Library.Template("Lit")) { name = "Rifle " + part };
            // Every Lit material needs a mask map, or the template's metallic map reads as chrome.
            switch (part)
            {
                case Part.Furniture:
                    // Olive drab polymer: a faint stipple, matte with a slight sheen where it's handled.
                    m.SetTexture("_BaseMap", Stipple);
                    m.SetColor("_BaseColor", new Color(0.31f, 0.33f, 0.23f));
                    m.SetTexture("_MetallicGlossMap", Mask(0, 0.55f));
                    m.SetFloat("_Smoothness", 0.34f);
                    break;
                case Part.Metal:
                    // Dark gunmetal: phosphated steel and anodised aluminium.
                    m.SetTexture("_BaseMap", Stipple);
                    m.SetColor("_BaseColor", new Color(0.2f, 0.21f, 0.22f));
                    m.SetTexture("_MetallicGlossMap", Mask(0.6f, 0.8f));
                    m.SetFloat("_Smoothness", 0.5f);
                    break;
                default:
                    m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    m.SetColor("_BaseColor", new Color(0.03f, 0.05f, 0.07f));
                    m.SetTexture("_MetallicGlossMap", NonMetal);
                    m.SetFloat("_Smoothness", 0.95f);
                    break;
            }
            m.SetTextureScale("_BaseMap", Vector2.one * 3);
            m.SetFloat("_Metallic", 0);
            if (nor != null) m.SetTexture("_BumpMap", nor);
            else RifleMats[part] = m;
            return m;
        }

        static Texture2D? stipple;
        /// <summary>A pale grey speckle that keeps a flat colour from reading as plastic.</summary>
        static Texture2D Stipple
        {
            get
            {
                if (stipple != null) return stipple;
                const int S = 64;
                var rng = new System.Random(115);
                var px = new Color32[S * S];
                for (int i = 0; i < px.Length; i++)
                {
                    float x = i % S, y = i / S;
                    float g = 0.86f + 0.08f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f) + 0.06f * (float)rng.NextDouble();
                    px[i] = new Color(g, g, g, 1);
                }
                stipple = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "Stipple", wrapMode = TextureWrapMode.Repeat };
                stipple.SetPixels32(px);
                stipple.Apply(true);
                return stipple;
            }
        }

        static readonly Dictionary<(float, float), Texture2D> Masks = new Dictionary<(float, float), Texture2D>();
        /// <summary>A mask map: metallic in red, smoothness in alpha varying from `low` to 1 (scaled by _Smoothness).</summary>
        static Texture2D Mask(float metallic, float low)
        {
            if (Masks.TryGetValue((metallic, low), out var done) && done != null) return done;
            const int S = 64;
            var px = new Color32[S * S];
            byte m = (byte)Mathf.RoundToInt(metallic * 255);
            for (int i = 0; i < px.Length; i++)
            {
                float g = Mathf.Lerp(low, 1, Mathf.PerlinNoise(i % S * 0.11f + 7, i / S * 0.11f + 3));
                px[i] = new Color32(m, 0, 0, (byte)Mathf.RoundToInt(g * 255));
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true, true) { name = "Rifle mask", wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels32(px);
            tex.Apply(true);
            Masks[(metallic, low)] = tex;
            return tex;
        }

        /// <summary>Whether the deeper (stock) end of a model lies toward +z of `frame`.</summary>
        static bool StockAhead(GameObject go, Transform frame)
        {
            float zMin = float.MaxValue, zMax = float.MinValue;
            var pts = new List<Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = frame.InverseTransformPoint(mf.transform.TransformPoint(v));
                    pts.Add(p);
                    zMin = Mathf.Min(zMin, p.z);
                    zMax = Mathf.Max(zMax, p.z);
                }
            }
            if (pts.Count == 0) return false;
            float cut = (zMax - zMin) * 0.12f;
            float loA = float.MaxValue, hiA = float.MinValue, loB = float.MaxValue, hiB = float.MinValue;
            foreach (var p in pts)
            {
                if (p.z < zMin + cut) { loA = Mathf.Min(loA, p.y); hiA = Mathf.Max(hiA, p.y); }
                if (p.z > zMax - cut) { loB = Mathf.Min(loB, p.y); hiB = Mathf.Max(hiB, p.y); }
            }
            return hiB - loB > hiA - loA;
        }

        static readonly System.Text.RegularExpressions.Regex Suffix = new System.Text.RegularExpressions.Regex(@"_\d+$");

        /// <summary>A joint's Mixamo name without prefix or the "_12" index Sketchfab exports add.</summary>
        static string BoneName(string n)
        {
            int i = n.IndexOf("mixamorig", System.StringComparison.Ordinal);
            if (i >= 0) n = n.Substring(i + 9).TrimStart(':');
            return Suffix.Replace(n, "");
        }

        static Texture2D? nonMetal;
        /// <summary>Metallic 0, smoothness 1 (scaled by _Smoothness).</summary>
        internal static Texture2D NonMetal
        {
            get
            {
                if (nonMetal != null) return nonMetal;
                nonMetal = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = "Non-metal" };
                nonMetal.SetPixels32(new[] { new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255) });
                nonMetal.Apply();
                return nonMetal;
            }
        }

        /// <summary>
        /// Freezes the posed soldier into plain meshes. The pose never changes while it is shown,
        /// and skinned meshes rendered nothing in testing, so each skin becomes a static mesh.
        /// </summary>
        void Bake()
        {
            foreach (var r in skins)
            {
                var mesh = new Mesh { name = r.name + " (posed)" };
                r.BakeMesh(mesh, true);
                mesh.RecalculateBounds();
                var go = r.gameObject;
                var mats = r.sharedMaterials;
                // A GameObject holds one renderer, so the skin goes before the mesh renderer arrives.
                DestroyImmediate(r);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            skins.Clear();
        }
    }
}
