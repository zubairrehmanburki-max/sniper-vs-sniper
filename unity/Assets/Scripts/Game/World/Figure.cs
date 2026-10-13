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
    public sealed class Figure : MonoBehaviour
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

        public static Figure Create(Stance stance, float heightM, Transform parent, Color tint)
        {
            var root = new GameObject("Figure " + stance);
            root.transform.SetParent(parent, false);
            var fig = root.AddComponent<Figure>();
            var pose = Poses[stance];
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            fig.Body = body;
            // The detailed CC-BY soldier when the asset set has it, else the bundled low-detail one.
            var hd = Library.Model("soldier_tactical");
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
                    if (!fig.bones.ContainsKey(n)) fig.bones[n] = t;
                }
                fig.Dress(model, hd != null, tint);
            }
            else Debug.LogWarning("[Figure] no soldier model loaded");
            Debug.Log($"[Figure] {stance}: {fig.bones.Count} bones, {root.GetComponentsInChildren<Renderer>().Length} renderers");

            if (model != null) fig.Strike(model, pose, stance == Stance.Prone ? 1.75f : heightM);
            fig.Bake();

            // Ground him: the lowest point of the posed body on the road (which sits 4 cm proud).
            float ground = root.transform.position.y + 0.04f;
            var b = Library.BoundsOf(body.gameObject);
            body.position += new Vector3(0, ground - b.min.y, 0);

            // The rifle in his hands: grip at the right hand, aimed straight down-range.
            var rifle = Rifle(root.transform);
            if (fig.bones.TryGetValue("RightHand", out var rh))
            {
                var dir = root.transform.forward;
                rifle.transform.rotation = Quaternion.LookRotation(dir, root.transform.up);
                rifle.transform.position = rh.position + dir * 0.28f + root.transform.up * 0.03f;
                if (stance == Stance.Prone && Library.BoundsOf(rifle).min.y < ground)
                    rifle.transform.position += new Vector3(0, ground - Library.BoundsOf(rifle).min.y, 0);
            }
            else rifle.transform.localPosition = new Vector3(0.1f, stance == Stance.Prone ? 0.15f : 1.3f, 0.5f);
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
                    var tex = src == null ? null : src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : src.mainTexture;
                    var mat = new Material(Library.Template("Lit")) { name = "Soldier " + r.name };
                    mat.SetTexture("_BaseMap", tex);
                    mat.SetFloat("_Metallic", 0);
                    // The Lit template reads metallic from a map; without one it defaults to white,
                    // fully metallic, and the cloth turns to chrome.
                    mat.SetTexture("_MetallicGlossMap", NonMetal);
                    if (hd)
                    {
                        var nor = src != null && src.HasProperty("normalTexture") ? src.GetTexture("normalTexture") : null;
                        if (nor != null) mat.SetTexture("_BumpMap", nor);
                        var factor = src != null && src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : Color.white;
                        mat.SetColor("_BaseColor", factor * Color.Lerp(Color.white, tint * 1.5f, 0.25f));
                        // Matte fabric: the sheen of a wet road is wrong on cloth.
                        mat.SetFloat("_Smoothness", 0.08f);
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
        void Strike(Transform model, Pose pose, float height)
        {
            if (!bones.TryGetValue("Hips", out var hips) || !bones.TryGetValue("Head", out var head)
                || !bones.TryGetValue("LeftUpLeg", out var lul) || !bones.TryGetValue("RightUpLeg", out var rul)) return;
            var frame = transform;
            Vector3 up = (head.position - hips.position).normalized;
            Vector3 right = Vector3.ProjectOnPlane(rul.position - lul.position, up).normalized;
            Vector3 chest = Vector3.Cross(right, up);

            // Height: crown to sole along the body's up axis.
            float top = bones.TryGetValue("HeadTop_End", out var crown) ? Vector3.Dot(crown.position, up) : Vector3.Dot(head.position, up) + 0.12f * 1.7f;
            float sole = float.MaxValue;
            foreach (var n in new[] { "LeftToeBase", "RightToeBase", "LeftFoot", "RightFoot", "LeftToe_End", "RightToe_End" })
                if (bones.TryGetValue(n, out var f)) sole = Mathf.Min(sole, Vector3.Dot(f.position, up));
            float rest = top - sole;
            if (rest > 1e-4f && sole < float.MaxValue) model.localScale *= height / (rest * 1.04f);

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
            var anchor = pose.Up == Vector3.forward ? head.position : hips.position;
            var shift = frame.position - anchor;
            model.position += new Vector3(shift.x, 0, shift.z);
        }

        /// <summary>
        /// The sniper rifle, 1.3 m long with its barrel along +z: the detailed L115A3 when the asset
        /// set has it, else the scanned bolt-action rifle. Used by the figures and the first-person view.
        /// </summary>
        public static GameObject Rifle(Transform parent)
        {
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
            rb = Library.BoundsOf(rifle);
            // Centre it on its parent.
            rifle.transform.position -= rb.center - inner.position;
            return inner.gameObject;
        }

        /// <summary>
        /// Turns a model's skinned meshes into static ones (they draw nothing in the capture build)
        /// and gives every renderer a URP Lit material in its imported colour and texture.
        /// </summary>
        static void Freeze(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = r.sharedMaterials[i];
                    var m = new Material(Library.Template("Lit")) { name = "Rifle " + r.name };
                    var tex = src == null ? null : src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : src.mainTexture;
                    m.SetTexture("_BaseMap", tex);
                    m.SetColor("_BaseColor", src != null && src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : Color.white);
                    m.SetTexture("_MetallicGlossMap", NonMetal);
                    m.SetFloat("_Metallic", 0);
                    m.SetFloat("_Smoothness", 0.35f);
                    mats[i] = m;
                }
                if (r is SkinnedMeshRenderer skin)
                {
                    var mesh = new Mesh { name = skin.name + " (frozen)" };
                    skin.BakeMesh(mesh, true);
                    mesh.RecalculateBounds();
                    var host = skin.gameObject;
                    DestroyImmediate(skin);
                    host.AddComponent<MeshFilter>().sharedMesh = mesh;
                    host.AddComponent<MeshRenderer>().sharedMaterials = mats;
                }
                else r.sharedMaterials = mats;
            }
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
        static Texture2D NonMetal
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
