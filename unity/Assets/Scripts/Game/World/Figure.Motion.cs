using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The live soldier: the player's own figure stays skinned and is posed every frame. Each stance
    /// has an idle pose and a gait (a walk standing, a crouched walk, a crawl on the belly), worked
    /// out as limb directions like the frozen poses; the figure blends between stances when it
    /// stands up or gets down, and between idle and gait as it starts and stops moving.
    /// </summary>
    public sealed partial class Figure
    {
        /// <summary>Limbs in the order they are swung (parents first): the standing pose's order.</summary>
        static readonly (string bone, string next)[] Chain;
        /// <summary>Each stance's idle limb directions, in Chain order (index = (int)Stance).</summary>
        static readonly Vector3[][] Idle;
        static readonly int Rul, Rl, Rf, Lul, Ll, Lf, Ra, La, Sp, Sp1, Sp2, Nk;

        // After every field initialiser in both halves of the class, so the poses exist.
        static Figure()
        {
            Chain = ChainOf(Poses[Stance.Standing]);
            Idle = new[] { Aligned(Poses[Stance.Prone]), Aligned(Poses[Stance.Crouch]), Aligned(Poses[Stance.Standing]) };
            Rul = Ix("RightUpLeg"); Rl = Ix("RightLeg"); Rf = Ix("RightFoot");
            Lul = Ix("LeftUpLeg"); Ll = Ix("LeftLeg"); Lf = Ix("LeftFoot");
            Ra = Ix("RightArm"); La = Ix("LeftArm");
            Sp = Ix("Spine"); Sp1 = Ix("Spine1"); Sp2 = Ix("Spine2"); Nk = Ix("Neck");
        }
        /// <summary>Metres covered per full stride cycle (two steps), by stance.</summary>
        static readonly float[] Stride = { 0.75f, 1.0f, 1.45f };
        /// <summary>Seconds to go from one stance to the next.</summary>
        const float StanceSeconds = 0.5f;

        bool live;
        Transform? model, rifle;
        Vector3 modelPos;
        Quaternion modelRot;
        readonly List<(Transform t, Quaternion r)> rest = new List<(Transform, Quaternion)>();
        readonly List<Transform> skeleton = new List<Transform>();
        /// <summary>How far below the lowest joint the mesh reaches, per stance (the sole, the belly).</summary>
        readonly float[] depth = new float[3];
        /// <summary>How much to lift the rifle off the deck when lying down.</summary>
        float rifleLift;

        Stance want;
        readonly float[] weight = new float[3];
        float speed, moving, phase;
        Vector3 heading = Vector3.forward;
        Vector3[] dirs = System.Array.Empty<Vector3>(), gait = System.Array.Empty<Vector3>();

        /// <summary>The stance it is in or going to.</summary>
        public Stance Current => want;
        /// <summary>True while it is still changing stance.</summary>
        public bool Changing => weight[(int)want] < 0.999f;

        /// <summary>
        /// Tells a live figure what to do: the stance to be in, how fast it moves (m/s) and which
        /// way in its own frame (+z ahead). It gets there over the next frames.
        /// </summary>
        public void Drive(Stance stance, float metresPerSecond, Vector3 localDirection)
        {
            want = stance;
            speed = metresPerSecond;
            localDirection.y = 0;
            if (localDirection.sqrMagnitude > 1e-4f) heading = localDirection.normalized;
        }

        static (string, string)[] ChainOf(Pose p)
        {
            var r = new (string, string)[p.Limbs.Length];
            for (int i = 0; i < r.Length; i++) r[i] = (p.Limbs[i].bone, p.Limbs[i].next);
            return r;
        }

        static Vector3[] Aligned(Pose p)
        {
            var r = new Vector3[Chain.Length];
            for (int i = 0; i < Chain.Length; i++)
            {
                r[i] = Vector3.down;
                foreach (var (bone, next, dir) in p.Limbs)
                    if (bone == Chain[i].bone && next == Chain[i].next) r[i] = dir.normalized;
            }
            return r;
        }

        static int Ix(string bone)
        {
            for (int i = 0; i < Chain.Length; i++) if (Chain[i].bone == bone) return i;
            return -1;
        }

        /// <summary>Notes the rest pose (after sizing) so every frame can start again from it.</summary>
        void Remember(Transform m)
        {
            live = true;
            model = m;
            modelPos = m.localPosition;
            modelRot = m.localRotation;
            rest.Clear();
            foreach (var t in bones.Values) rest.Add((t, t.localRotation));
            skeleton.Clear();
            if (bones.TryGetValue("Hips", out var hips))
                foreach (var t in hips.GetComponentsInChildren<Transform>()) skeleton.Add(t);
            foreach (var r in skins) r.updateWhenOffscreen = true;
        }

        void Unpose()
        {
            if (model == null) return;
            model.localPosition = modelPos;
            model.localRotation = modelRot;
            foreach (var (t, r) in rest) t.localRotation = r;
            Body.localPosition = Vector3.zero;
        }

        float LowestJoint()
        {
            float y = float.MaxValue;
            foreach (var t in skeleton) y = Mathf.Min(y, t.position.y);
            return y;
        }

        /// <summary>
        /// Strikes each stance once and measures how far the mesh reaches below the lowest joint,
        /// so a blended pose can be stood on the deck from its joints alone; and how far the rifle
        /// would sink into the deck lying down.
        /// </summary>
        void Calibrate()
        {
            for (int s = 0; s < 3; s++)
            {
                Unpose();
                Strike(model!, Poses[(Stance)s], s == (int)Stance.Prone ? 1 : 0);
                float joint = LowestJoint(), mesh = float.MaxValue;
                foreach (var r in skins)
                {
                    var baked = new Mesh();
                    r.BakeMesh(baked, true);
                    foreach (var v in baked.vertices) mesh = Mathf.Min(mesh, r.transform.TransformPoint(v).y);
                    Destroy(baked);
                }
                depth[s] = mesh < float.MaxValue ? Mathf.Clamp(joint - mesh, 0, 0.4f) : 0.05f;
                if (s == (int)Stance.Prone && rifle != null)
                {
                    float ground = transform.position.y + 0.04f;
                    Body.position += new Vector3(0, ground + depth[s] - joint, 0);
                    PlaceRifle(0);
                    rifleLift = Mathf.Max(0, ground - Library.BoundsOf(rifle.gameObject).min.y);
                }
            }
            Unpose();
        }

        void PlaceRifle(float lift)
        {
            if (rifle == null || !bones.TryGetValue("RightHand", out var rh)) return;
            var dir = transform.forward;
            rifle.rotation = Quaternion.LookRotation(dir, transform.up);
            rifle.position = rh.position + dir * 0.28f + transform.up * (0.03f + lift);
        }

        void LateUpdate()
        {
            if (live) Animate(Time.deltaTime);
        }

        /// <summary>Moves the blends along by dt and poses the body for this frame.</summary>
        void Animate(float dt)
        {
            if (model == null) return;
            float total = 0;
            for (int s = 0; s < 3; s++)
            {
                weight[s] = Mathf.MoveTowards(weight[s], s == (int)want ? 1 : 0, dt / StanceSeconds);
                total += weight[s];
            }
            for (int s = 0; s < 3; s++) weight[s] /= Mathf.Max(1e-4f, total);
            moving = Mathf.MoveTowards(moving, speed > 0.05f ? 1 : 0, dt / 0.2f);
            float stride = 0;
            for (int s = 0; s < 3; s++) stride += weight[s] * Stride[s];
            phase = Mathf.Repeat(phase + dt * 2 * Mathf.PI * speed / Mathf.Max(0.3f, stride), 2 * Mathf.PI);

            // Each stance's pose (idle eased into its gait), weighted together.
            Vector3 up = Vector3.zero, chest = Vector3.zero;
            if (dirs.Length != Chain.Length)
            {
                dirs = new Vector3[Chain.Length];
                gait = new Vector3[Chain.Length];
            }
            System.Array.Clear(dirs, 0, dirs.Length);
            for (int s = 0; s < 3; s++)
            {
                float w = weight[s];
                if (w <= 0) continue;
                var p = Poses[(Stance)s];
                up += w * p.Up;
                chest += w * p.Chest;
                Gait((Stance)s, phase, heading, gait);
                for (int i = 0; i < dirs.Length; i++) dirs[i] += w * Vector3.Lerp(Idle[s][i], gait[i], moving).normalized;
            }
            var pose = new Pose { Up = up.normalized, Chest = chest.normalized, Limbs = new (string, string, Vector3)[Chain.Length] };
            for (int i = 0; i < Chain.Length; i++) pose.Limbs[i] = (Chain[i].bone, Chain[i].next, dirs[i]);

            Unpose();
            float prone = weight[(int)Stance.Prone];
            Strike(model, pose, prone);
            // Stand it on the deck: the lowest joint, less the flesh under it.
            float ground = transform.position.y + 0.04f, under = 0;
            for (int s = 0; s < 3; s++) under += weight[s] * depth[s];
            Body.position += new Vector3(0, ground + under - LowestJoint(), 0);
            PlaceRifle(rifleLift * prone);
        }

        /// <summary>
        /// A stance's moving pose at a point in the stride, in the figure's frame (+z ahead): the
        /// legs swing along the way it is going and each knee folds as its foot comes through.
        /// </summary>
        static void Gait(Stance stance, float ph, Vector3 d, Vector3[] into)
        {
            System.Array.Copy(Idle[(int)stance], into, into.Length);
            var x = Vector3.right;
            foreach (var (sgn, off, ul, l, f) in new[] { (1f, Mathf.PI, Rul, Rl, Rf), (-1f, 0f, Lul, Ll, Lf) })
            {
                float phi = ph + off;
                float swing = Mathf.Sin(phi), through = Mathf.Max(0, Mathf.Cos(phi));
                switch (stance)
                {
                    case Stance.Standing:
                    {
                        // A steady walk: thighs swing about 25 degrees either way, the knee folds
                        // up to 55 degrees as the foot passes under the body.
                        var thigh = (Vector3.down + 0.45f * swing * d + sgn * 0.09f * x).normalized;
                        into[ul] = thigh;
                        into[l] = Quaternion.AngleAxis((0.12f + 0.85f * through) * Mathf.Rad2Deg, x) * thigh;
                        into[f] = new Vector3(sgn * 0.15f, -0.5f + 0.3f * through, 1).normalized;
                        break;
                    }
                    case Stance.Crouch:
                    {
                        // Low and bent: thighs well forward, shins near upright, short quick steps.
                        var thigh = (new Vector3(0, -0.45f, 0.9f) + 0.35f * swing * d + sgn * 0.16f * x).normalized;
                        into[ul] = thigh;
                        into[l] = Quaternion.AngleAxis((1.3f + 0.35f * through) * Mathf.Rad2Deg, x) * thigh;
                        into[f] = new Vector3(sgn * 0.1f, -0.4f, 1).normalized;
                        break;
                    }
                    default:
                    {
                        // Belly crawl: one knee at a time drawn up and out to the side, then pushed
                        // back straight; the elbows pull forward in turn.
                        float draw = Mathf.Max(0, swing);
                        into[ul] = new Vector3(sgn * (0.2f + 0.75f * draw), -0.02f, -1 + 0.6f * draw).normalized;
                        into[l] = new Vector3(sgn * (0.24f - 0.35f * draw), -0.02f, -1).normalized;
                        into[f] = new Vector3(sgn * 0.1f, -0.8f, -0.5f).normalized;
                        break;
                    }
                }
            }
            if (stance == Stance.Crouch)
            {
                // Leaning into the walk over the rifle.
                into[Sp] = new Vector3(0, 1, 0.45f).normalized;
                into[Sp1] = new Vector3(0, 1, 0.3f).normalized;
                into[Sp2] = new Vector3(0, 1, 0.15f).normalized;
                into[Nk] = new Vector3(0, 1, 0.22f).normalized;
            }
            else if (stance == Stance.Prone)
            {
                float pull = Mathf.Sin(ph);
                into[Ra] = (into[Ra] + new Vector3(0, 0, 0.35f * pull)).normalized;
                into[La] = (into[La] - new Vector3(0, 0, 0.35f * pull)).normalized;
            }
        }
    }
}
