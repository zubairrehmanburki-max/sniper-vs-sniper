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
            var prefab = Resources.Load<GameObject>("Characters/Soldier");
            if (prefab != null)
            {
                var model = Instantiate(prefab, body);
                model.name = "Soldier";
                foreach (var anim in model.GetComponentsInChildren<Animation>()) anim.enabled = false;
                foreach (var animator in model.GetComponentsInChildren<Animator>()) animator.enabled = false;
                foreach (var t in model.GetComponentsInChildren<Transform>())
                {
                    string n = t.name.Replace("mixamorig:", "").Replace("mixamorig", "");
                    if (!fig.bones.ContainsKey(n)) { fig.bones[n] = t; fig.rest[n] = t.localRotation; }
                }
                foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // Olive drab over the original texture, on the same URP Lit template as the
                    // structures: the imported glTF materials draw nothing on some renderers.
                    var tex = r.sharedMaterial == null ? null
                        : r.sharedMaterial.HasProperty("baseColorTexture") ? r.sharedMaterial.GetTexture("baseColorTexture")
                        : r.sharedMaterial.mainTexture;
                    var mat = new Material(Library.Template("Lit")) { name = "Soldier " + r.name };
                    mat.SetTexture("_BaseMap", tex);
                    mat.SetColor("_BaseColor", r.name.Contains("visor") ? new Color(0.17f, 0.19f, 0.17f) : tint);
                    mat.SetFloat("_Smoothness", 0.25f);
                    r.sharedMaterials = new[] { mat };
                    fig.skins.Add(r);
                }
            }
            else Debug.LogWarning("[Figure] Characters/Soldier did not load");
            Debug.Log($"[Figure] {stance}: {fig.bones.Count} bones, {root.GetComponentsInChildren<Renderer>().Length} renderers");
            fig.bones.TryGetValue("Hips", out fig.hips);
            if (fig.hips != null) fig.hipsRest = fig.hips.localPosition;

            float scale = stance == Stance.Standing ? heightM / ModelHeight : 1.75f / ModelHeight;
            body.localScale = Vector3.one * scale;
            if (fig.pose.Lie.HasValue)
            {
                // Lying down: the model's up points along −z (feet trailing behind the head, which faces +z).
                // Rolled over on the long axis so he lies face down on his elbows, not on his back.
                body.localRotation = Quaternion.Euler(0, 0, 180) * Quaternion.Euler(-fig.pose.Lie.Value, 0, 0);
                body.localPosition = new Vector3(0, 0.22f, -1.5f);
            }
            body.localPosition += fig.pose.Shift;

            var rifle = Library.Place("bolt_action_rifle_7_62", Vector3.zero, 0, 1, root.transform);
            rifle.transform.localPosition = fig.pose.RifleAt;
            rifle.transform.localRotation = Quaternion.Euler(fig.pose.RifleTilt, 0, 0);
            fig.LateUpdate();
            fig.Bake();
            if (fig.pose.Lie.HasValue)
            {
                // Rest the lowest point of the lying body just on the ground (the road sits 4 cm proud).
                var b = Library.BoundsOf(body.gameObject);
                body.position += new Vector3(0, root.transform.position.y + 0.04f - b.min.y, 0);
            }
            return fig;
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
