using System.Collections.Generic;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The X-ray kill cam: as the bullet closes in, the target's body turns to a faint blue glow
    /// and his skeleton, heart and lungs light up inside it, built from his own posed bones, so the
    /// round can be seen going in. A red flare marks the wound.
    /// </summary>
    public sealed class XRay : MonoBehaviour
    {
        readonly List<(Renderer r, Material[] mats)> saved = new List<(Renderer, Material[])>();

        public static XRay Begin(Figure target, Vector3 impact, Vector3 bulletDir)
        {
            // Built in world coordinates, so the object stays at the origin rather than under the figure.
            var go = new GameObject("XRay");
            var x = go.AddComponent<XRay>();

            // Ghost the flesh: every renderer of the body (not the rifle) goes additive blue.
            var ghost = Tinted(new Color(0.18f, 0.42f, 0.7f, 0.22f));
            foreach (var r in target.GetComponentsInChildren<Renderer>())
            {
                if (!r.transform.IsChildOf(target.Body)) continue;
                x.saved.Add((r, r.sharedMaterials));
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = ghost;
                r.sharedMaterials = mats;
            }

            // Bones: a beam from each joint to its parent, thicker along the spine and legs.
            var bone = new MeshBuilder();
            foreach (var (name, parent) in Links)
            {
                if (!target.Bones.TryGetValue(name, out var t) || !target.Bones.TryGetValue(parent, out var pt)) continue;
                float w = name.Contains("Spine") || name == "Neck" ? 0.045f : name.Contains("Leg") ? 0.04f : name.Contains("Arm") ? 0.03f : 0.022f;
                bone.Beam(pt.position, t.position, w, w);
            }
            // Skull: three hoops round an egg from the neck to the crown, and a jaw bar.
            Bones(target, "Head", b =>
            {
                var crown = target.Bones.TryGetValue("HeadTop_End", out var ht) ? ht.position : b.position + b.up * 0.2f;
                var up = (crown - b.position).normalized;
                var c = Vector3.Lerp(b.position, crown, 0.5f);
                var side = Vector3.ProjectOnPlane(b.right, up).normalized;
                var front = Vector3.Cross(side, up);
                Hoop(bone, c, up, side, front, 0.075f, 0.09f, 0.012f);
                Hoop(bone, c, side, up, front, 0.1f, 0.09f, 0.012f);
                Hoop(bone, c, front, side, up, 0.075f, 0.1f, 0.012f);
                bone.Beam(c - up * 0.07f + front * 0.07f - side * 0.04f, c - up * 0.07f + front * 0.07f + side * 0.04f, 0.02f, 0.02f);
            });
            // Rib cage: pairs of ribs curving from the spine round to the breastbone, sloping down
            // toward the front, with a gap at the sternum.
            Bones(target, "Spine1", s1 =>
            {
                Bones(target, "Spine2", s2 =>
                {
                    var up = (s2.position - s1.position).normalized;
                    var side = s2.right;
                    var front = Vector3.Cross(side, up).normalized;
                    var top = s2.position + up * 0.12f;
                    for (int i = 0; i < 7; i++)
                    {
                        var c = Vector3.Lerp(s1.position - up * 0.02f, top, i / 6f) - front * 0.04f;
                        float rw = 0.14f - Mathf.Abs(i - 3.2f) * 0.012f;
                        foreach (float sgn in new[] { -1f, 1f })
                            Rib(bone, c, up, side * sgn, front, rw, rw * 0.9f, 0.011f);
                    }
                    // Breastbone.
                    bone.Beam(Vector3.Lerp(s1.position, top, 0.15f) + front * 0.1f - up * 0.04f, top + front * 0.08f, 0.025f, 0.012f);
                });
            });
            // Pelvis.
            Bones(target, "Hips", h => Hoop(bone, h.position, h.up, h.right, h.forward, 0.13f, 0.08f, 0.03f));
            bone.Object("Skeleton", Tinted(new Color(0.95f, 0.93f, 0.85f, 0.9f)), go.transform, shadows: false);

            // Organs: heart left of the sternum, two lungs either side.
            var organs = new MeshBuilder();
            var heart = new MeshBuilder();
            Bones(target, "Spine2", s =>
            {
                heart.Box(s.position + s.forward * 0.05f + s.right * 0.03f, new Vector3(0.09f, 0.11f, 0.08f), s.rotation * Quaternion.Euler(0, 0, 20));
                foreach (float side in new[] { -1f, 1f })
                    organs.Box(s.position + s.right * side * 0.08f + s.up * 0.02f, new Vector3(0.1f, 0.2f, 0.12f), s.rotation);
            });
            organs.Object("Lungs", Tinted(new Color(0.9f, 0.45f, 0.5f, 0.35f)), go.transform, shadows: false);
            heart.Object("Heart", Tinted(new Color(1f, 0.12f, 0.1f, 0.85f)), go.transform, shadows: false);

            // Wound: a red flare where the round enters, and the channel it cuts through.
            var wound = new MeshBuilder();
            wound.Beam(impact - bulletDir * 0.05f, impact + bulletDir * 0.35f, 0.03f, 0.03f);
            wound.Box(impact, new Vector3(0.06f, 0.06f, 0.06f), Quaternion.LookRotation(bulletDir));
            wound.Object("Wound", Tinted(new Color(1f, 0.15f, 0.08f, 1f)), go.transform, shadows: false);
            return x;
        }

        /// <summary>The skeleton drawn: each joint and the joint it hangs from.</summary>
        static readonly (string, string)[] Links = BuildLinks();

        static (string, string)[] BuildLinks()
        {
            var l = new List<(string, string)> { ("Spine", "Hips"), ("Spine1", "Spine"), ("Spine2", "Spine1"), ("Neck", "Spine2"), ("Head", "Neck") };
            foreach (var side in new[] { "Left", "Right" })
            {
                l.Add((side + "Shoulder", "Spine2"));
                l.Add((side + "Arm", side + "Shoulder"));
                l.Add((side + "ForeArm", side + "Arm"));
                l.Add((side + "Hand", side + "ForeArm"));
                l.Add((side + "UpLeg", "Hips"));
                l.Add((side + "Leg", side + "UpLeg"));
                l.Add((side + "Foot", side + "Leg"));
                l.Add((side + "ToeBase", side + "Foot"));
            }
            return l.ToArray();
        }

        /// <summary>Half an elliptical hoop: from the spine at the back, round one side, to the front.</summary>
        static void Rib(MeshBuilder mb, Vector3 centre, Vector3 up, Vector3 side, Vector3 front, float ra, float rb, float thick)
        {
            const int Seg = 8;
            for (int i = 0; i < Seg; i++)
            {
                // From the back (−front) through the side to just short of the front.
                float t0 = Mathf.PI * (-0.5f + 0.9f * i / Seg), t1 = Mathf.PI * (-0.5f + 0.9f * (i + 1) / Seg);
                var p0 = centre + side * Mathf.Cos(t0) * ra + front * Mathf.Sin(t0) * rb - up * 0.04f * (i / (float)Seg);
                var p1 = centre + side * Mathf.Cos(t1) * ra + front * Mathf.Sin(t1) * rb - up * 0.04f * ((i + 1) / (float)Seg);
                mb.Beam(p0, p1, thick, thick);
            }
        }

        static bool Skip(string bone) =>
            bone.EndsWith("_End") || bone.Contains("Thumb") || bone.Contains("Index") || bone.Contains("Middle") || bone.Contains("Ring") || bone.Contains("Pinky") || bone.Contains("Eye");

        static void Bones(Figure f, string name, System.Action<Transform> act)
        {
            if (f.Bones.TryGetValue(name, out var t)) act(t);
        }

        /// <summary>An elliptical ring of short beams around `centre` in the plane of `a` and `b`.</summary>
        static void Hoop(MeshBuilder mb, Vector3 centre, Vector3 normal, Vector3 a, Vector3 b, float ra, float rb, float thick)
        {
            const int Seg = 14;
            for (int i = 0; i < Seg; i++)
            {
                float t0 = i * Mathf.PI * 2 / Seg, t1 = (i + 1) * Mathf.PI * 2 / Seg;
                var p0 = centre + a * Mathf.Cos(t0) * ra + b * Mathf.Sin(t0) * rb;
                var p1 = centre + a * Mathf.Cos(t1) * ra + b * Mathf.Sin(t1) * rb;
                mb.Beam(p0, p1, thick, thick);
            }
        }

        static Material Tinted(Color c)
        {
            var m = new Material(Library.Template("Additive"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        /// <summary>Puts the body's own materials back and removes the X-ray parts.</summary>
        public void End()
        {
            foreach (var (r, mats) in saved)
                if (r != null) r.sharedMaterials = mats;
            saved.Clear();
            Destroy(gameObject);
        }
    }
}
