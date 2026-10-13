using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The sniper figure: the rigged soldier posed per stance with bone rotations over its bind pose
    /// (ported from the web client's figure.ts), with a slow breath. Faces +z; the caller turns it.
    /// The origin is on the ground under the head when prone, under the feet otherwise.
    /// </summary>
    public sealed class Figure : MonoBehaviour
    {
        sealed class Pose
        {
            public Dictionary<string, Vector3> Bones = new Dictionary<string, Vector3>();
            public Vector3 Hips;
            public float? Lie;
            public Vector3 Shift;
            public Vector3 RifleAt;
            public float RifleTilt;
        }

        static readonly Dictionary<Stance, Pose> Poses = new Dictionary<Stance, Pose>
        {
            [Stance.Standing] = new Pose
            {
                Bones = { ["RightArm"] = new Vector3(85, 0, 35), ["RightForeArm"] = new Vector3(0, 0, -55), ["LeftArm"] = new Vector3(95, 0, 40), ["LeftForeArm"] = new Vector3(0, 0, -30), ["Spine1"] = new Vector3(4, 0, 0), ["Head"] = new Vector3(-4, 0, 0) },
                RifleAt = new Vector3(0.1f, 1.35f, 0.3f),
            },
            [Stance.Crouch] = new Pose
            {
                Bones =
                {
                    ["RightUpLeg"] = new Vector3(95, 0, 8), ["LeftUpLeg"] = new Vector3(95, 0, -8), ["RightLeg"] = new Vector3(-100, 0, 0), ["LeftLeg"] = new Vector3(-100, 0, 0),
                    ["Spine"] = new Vector3(20, 0, 0), ["Spine1"] = new Vector3(8, 0, 0), ["Head"] = new Vector3(-22, 0, 0),
                    ["RightArm"] = new Vector3(85, 0, 35), ["RightForeArm"] = new Vector3(0, 0, -55), ["LeftArm"] = new Vector3(95, 0, 40), ["LeftForeArm"] = new Vector3(0, 0, -30),
                },
                Hips = new Vector3(0, 0, -58),
                Shift = new Vector3(0, 0, 0.25f),
                RifleAt = new Vector3(0.1f, 0.8f, 0.5f),
            },
            [Stance.Prone] = new Pose
            {
                Bones =
                {
                    ["RightArm"] = new Vector3(80, 0, 60), ["RightForeArm"] = new Vector3(55, 0, 0), ["LeftArm"] = new Vector3(95, 0, 60), ["LeftForeArm"] = new Vector3(30, 0, 0),
                    ["RightUpLeg"] = new Vector3(0, 0, 10), ["LeftUpLeg"] = new Vector3(0, 0, -10),
                    ["Spine"] = new Vector3(-10, 0, 0), ["Spine1"] = new Vector3(-10, 0, 0), ["Spine2"] = new Vector3(-10, 0, 0), ["Neck"] = new Vector3(-25, 0, 0), ["Head"] = new Vector3(-30, 0, 0),
                },
                Lie = -90,
                RifleAt = new Vector3(0.03f, 0.27f, 0.25f),
            },
        };

        const float ModelHeight = 1.83f;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        /// <summary>Joints by Mixamo name without the prefix ("Hips", "Spine2", "LeftForeArm"...).</summary>
        public IReadOnlyDictionary<string, Transform> Bones => bones;
        /// <summary>The soldier model's root (the rifle sits outside it).</summary>
        public Transform Body = null!;
        readonly Dictionary<string, Quaternion> rest = new Dictionary<string, Quaternion>();
        Pose pose = null!;
        Transform? hips;
        Vector3 hipsRest;
        float clock;
        readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();

        public static Figure Create(Stance stance, float heightM, Transform parent, Color tint)
        {
            var root = new GameObject("Figure " + stance);
            root.transform.SetParent(parent, false);
            var fig = root.AddComponent<Figure>();
            fig.pose = Poses[stance];
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            fig.Body = body;
            // The detailed CC-BY soldier when the asset set has it, else the bundled low-detail one.
            var hd = Library.Model("soldier_tactical");
            var prefab = hd != null ? hd : Resources.Load<GameObject>("Characters/Soldier");
            if (prefab != null)
            {
                var model = Instantiate(prefab, body);
                model.name = "Soldier";
                foreach (var anim in model.GetComponentsInChildren<Animation>()) anim.enabled = false;
                foreach (var animator in model.GetComponentsInChildren<Animator>()) animator.enabled = false;
                if (hd != null) Retarget(model.transform, body);
                foreach (var t in model.GetComponentsInChildren<Transform>())
                {
                    string n = BoneName(t.name);
                    if (!fig.bones.ContainsKey(n)) { fig.bones[n] = t; fig.rest[n] = t.localRotation; }
                }
                foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // On the same URP Lit template as the structures: the imported glTF materials
                    // draw nothing on some renderers. The old soldier is tinted olive drab; the detailed
                    // one keeps its own colours and normal maps.
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var src = r.sharedMaterials[i];
                        var tex = src == null ? null : src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : src.mainTexture;
                        var mat = new Material(Library.Template("Lit")) { name = "Soldier " + r.name };
                        mat.SetTexture("_BaseMap", tex);
                        mat.SetFloat("_Metallic", 0);
                        if (hd != null)
                        {
                            var nor = src != null && src.HasProperty("normalTexture") ? src.GetTexture("normalTexture") : null;
                            if (nor != null) mat.SetTexture("_BumpMap", nor);
                            var factor = src != null && src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : Color.white;
                            mat.SetColor("_BaseColor", factor * Color.Lerp(Color.white, tint * 1.5f, 0.25f));
                            mat.SetFloat("_Smoothness", 0.22f);
                            if (src != null && src.HasProperty("alphaCutoff") && src.renderQueue >= 2450)
                            {
                                mat.SetFloat("_AlphaClip", 1);
                                mat.SetFloat("_Cutoff", 0.5f);
                                mat.EnableKeyword("_ALPHATEST_ON");
                            }
                        }
                        else
                        {
                            mat.SetColor("_BaseColor", r.name.Contains("visor") ? new Color(0.17f, 0.19f, 0.17f) : tint);
                            mat.SetFloat("_Smoothness", 0.12f);
                        }
                        mats[i] = mat;
                    }
                    r.sharedMaterials = mats;
                    fig.skins.Add(r);
                }
            }
            else Debug.LogWarning("[Figure] no soldier model loaded");
            Debug.Log($"[Figure] {stance}: {fig.bones.Count} bones, {root.GetComponentsInChildren<Renderer>().Length} renderers");
            fig.bones.TryGetValue("Hips", out fig.hips);
            if (fig.hips != null) fig.hipsRest = fig.hips.localPosition;

            float scale = stance == Stance.Standing ? heightM / ModelHeight : 1.75f / ModelHeight;
            body.localScale = Vector3.one * scale;
            if (fig.pose.Lie.HasValue)
            {
                // Lying down: the model's up points along −z (feet trailing behind the head, which faces +z).
                body.localRotation = Quaternion.Euler(-fig.pose.Lie.Value, 0, 0);
                body.localPosition = new Vector3(0, 0.22f, -1.5f);
            }
            body.localPosition += fig.pose.Shift;

            var rifle = Rifle(root.transform);
            rifle.transform.localPosition = fig.pose.RifleAt;
            float yaw = rifle.transform.localEulerAngles.y;
            rifle.transform.localRotation = Quaternion.Euler(fig.pose.RifleTilt, yaw, 0);
            if (fig.pose.Lie.HasValue)
            {
                // Prone: the rifle lies out in front of his head on its bipod, stock at the shoulder.
                rifle.transform.localPosition = new Vector3(0.12f, 0, 0.75f);
                Library.Ground(rifle, root.transform.position.y + 0.06f);
            }
            fig.LateUpdate();
            fig.Bake();
            if (fig.pose.Lie.HasValue)
            {
                // Rest the hips a hand's depth above the ground (the road sits 4 cm proud) so the torso
                // lies flat; grounding the lowest point instead props him up on his hands.
                float ground = root.transform.position.y + 0.04f;
                float lift = fig.hips != null ? ground + 0.16f - fig.hips.position.y : ground - Library.BoundsOf(body.gameObject).min.y;
                body.position += new Vector3(0, lift, 0);
            }
            return fig;
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
            var rb = Library.BoundsOf(rifle);
            // The long axis may run along x; turn it so it runs along z.
            if (rb.size.x > rb.size.z) rifle.transform.localRotation = Quaternion.Euler(0, 90, 0);
            rb = Library.BoundsOf(rifle);
            float len = Mathf.Max(rb.size.x, rb.size.z);
            if (hd && len > 1e-3f) rifle.transform.localScale *= 1.3f / len;
            rb = Library.BoundsOf(rifle);
            // Centre it on its parent.
            rifle.transform.position -= rb.center - inner.position;
            return inner.gameObject;
        }

        static readonly System.Text.RegularExpressions.Regex Suffix = new System.Text.RegularExpressions.Regex(@"_\d+$");

        /// <summary>A joint's Mixamo name without prefix or the "_12" index Sketchfab exports add.</summary>
        static string BoneName(string n)
        {
            int i = n.IndexOf("mixamorig", System.StringComparison.Ordinal);
            if (i >= 0) n = n.Substring(i + 9).TrimStart(':');
            return Suffix.Replace(n, "");
        }

        static Dictionary<string, Quaternion>? refRotations;
        static Vector3 refHips;
        static float refSpan;

        /// <summary>
        /// Puts a different Mixamo-rigged model into the bundled soldier's rest pose, at its size and
        /// place, so the stance poses (offsets from that rest pose) work on it unchanged. Copies each
        /// joint's orientation relative to the body, parents first.
        /// </summary>
        static void Retarget(Transform model, Transform body)
        {
            if (refRotations == null)
            {
                refRotations = new Dictionary<string, Quaternion>();
                var refPrefab = Resources.Load<GameObject>("Characters/Soldier");
                if (refPrefab == null) return;
                var tmp = Instantiate(refPrefab);
                tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                tmp.transform.localScale = Vector3.one;
                Transform? h = null, head = null;
                foreach (var t in tmp.GetComponentsInChildren<Transform>())
                {
                    string n = BoneName(t.name);
                    if (refRotations.ContainsKey(n)) continue;
                    refRotations[n] = t.rotation;
                    if (n == "Hips") h = t;
                    if (n == "Head") head = t;
                }
                if (h != null && head != null) { refHips = h.position; refSpan = Vector3.Distance(h.position, head.position); }
                DestroyImmediate(tmp);
            }
            Transform? hips = null, top = null;
            foreach (var t in model.GetComponentsInChildren<Transform>())
            {
                string n = BoneName(t.name);
                if (n == "Hips" && hips == null) hips = t;
                if (n == "Head" && top == null) top = t;
                // Only real joints (from the hips down) take the reference pose; the wrapper nodes
                // above them keep whatever turns the model upright.
                if (hips == null || !t.IsChildOf(hips)) continue;
                if (refRotations.TryGetValue(n, out var q)) t.rotation = body.rotation * q;
            }
            if (hips == null || top == null || refSpan <= 0) return;
            float span = Vector3.Distance(hips.position, top.position) / body.lossyScale.y;
            if (span > 1e-4f) model.localScale *= refSpan / span;
            model.position += body.TransformPoint(refHips) - hips.position;
            Debug.Log($"[Figure] retargeted: span {span:0.000} -> {refSpan:0.000}");
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
            enabled = false;
        }

        /// <summary>three.js XYZ Euler (degrees, right-handed) to a Unity local rotation (glTF X flipped).</summary>
        static Quaternion FromThree(Vector3 deg)
        {
            var qx = Quaternion.AngleAxis(deg.x, Vector3.right);
            var qy = Quaternion.AngleAxis(-deg.y, Vector3.up);
            var qz = Quaternion.AngleAxis(-deg.z, Vector3.forward);
            return qx * qy * qz;
        }

        void LateUpdate()
        {
            clock += Time.deltaTime;
            if (hips != null) hips.localPosition = hipsRest + new Vector3(-pose.Hips.x, pose.Hips.y, pose.Hips.z) * 0.01f * (hips.localPosition.magnitude > 10 ? 100 : 1);
            foreach (var kv in pose.Bones)
            {
                if (!bones.TryGetValue(kv.Key, out var b)) continue;
                b.localRotation = rest[kv.Key] * FromThree(kv.Value);
            }
            if (bones.TryGetValue("Spine2", out var spine2))
                spine2.localRotation *= Quaternion.AngleAxis(Mathf.Sin(clock * 1.4f) * 1.2f, Vector3.right);
        }
    }
}
