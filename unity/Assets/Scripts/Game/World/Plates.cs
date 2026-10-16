using System.Collections.Generic;
using SniperDuel.Ballistics;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Solo practice steel: torso-and-head silhouettes hung by chains from small A-frames, set out
    /// down the Training Lane at ranges nobody tells you. A hit rings the plate, knocks it swinging
    /// back on its chains and leaves a bare-steel splash in the paint where the round struck.
    /// </summary>
    public sealed class Plates : MonoBehaviour
    {
        /// <summary>Lane bands the plates are spread over, one plate in each (lane metres from A's end).</summary>
        static readonly (float from, float to)[] Bands = { (140, 220), (220, 300), (300, 380), (380, 460), (460, 540), (600, 640) };
        /// <summary>The plate hangs this far above the ground, clear of the grass.</summary>
        const float Lift = 0.35f;
        /// <summary>Crossbar height above the plate's foot.</summary>
        const float Bar = 1.15f;

        sealed class Plate
        {
            public Silhouette Shape = null!;
            public Transform Pivot = null!;
            public float Angle, Spin;
            public bool Down;
        }

        readonly List<Plate> plates = new List<Plate>();
        Material paint = null!, orange = null!, frame = null!, bare = null!;

        public IReadOnlyList<Silhouette> Shapes
        {
            get
            {
                var l = new List<Silhouette>(plates.Count);
                foreach (var p in plates) l.Add(p.Shape);
                return l;
            }
        }

        public int Count => plates.Count;

        public int DownCount
        {
            get
            {
                int n = 0;
                foreach (var p in plates) if (p.Down) n++;
                return n;
            }
        }

        public static Plates Create(Transform parent)
        {
            var go = new GameObject("Practice plates");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<Plates>();
            p.paint = Library.Plain(new Color(0.9f, 0.9f, 0.86f), 0.3f, 0.15f);
            p.orange = Library.Plain(new Color(0.95f, 0.42f, 0.08f), 0.3f, 0.15f);
            p.frame = Library.Plain(new Color(0.2f, 0.21f, 0.2f), 0.3f, 0.6f);
            p.bare = Library.Plain(new Color(0.34f, 0.35f, 0.36f), 0.55f, 0.8f);
            return p;
        }

        /// <summary>Clears the field and sets out a fresh one for a shooter at `eye`, every plate in plain view.</summary>
        public void Lay(DuelMap map, Vec3 eye, int seed, GameConfig cfg)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            plates.Clear();
            var rng = new System.Random(seed);
            var t = cfg.Target;
            var visual = map.Terrain ?? map.Ground;
            for (int band = 0; band < Bands.Length; band++)
            {
                var (from, to) = Bands[band];
                for (int tries = 0; tries < 40; tries++)
                {
                    float s = from + (float)rng.NextDouble() * (to - from);
                    float x = -42 + (float)rng.NextDouble() * 84;
                    double foot = System.Math.Max(map.Ground(x, -s), visual(x, -s)) + Lift;
                    var shape = new Silhouette
                    {
                        Id = $"plate-{band}",
                        Position = new Vec3(x, foot, -s),
                        FacingYaw = System.Math.PI,
                        Width = t.WidthM,
                        Height = t.HeightM,
                        Head = t.HeadM,
                    };
                    if (!Clear(map, eye, shape) || Crowded(eye, shape)) continue;
                    plates.Add(Build(shape, (float)visual(x, -s), band % 3 == 2));
                    break;
                }
            }
        }

        /// <summary>True when the whole plate, foot to head, shows over the ground and cover from the eye.</summary>
        static bool Clear(DuelMap map, Vec3 eye, Silhouette p)
        {
            foreach (double h in new[] { 0.05, p.Height - 0.05 })
            {
                var to = new Vec3(p.Position.X, p.Position.Y + h, p.Position.Z);
                for (int i = 1; i < 60; i++)
                {
                    double f = i / 60.0;
                    double x = eye.X + (to.X - eye.X) * f, y = eye.Y + (to.Y - eye.Y) * f, z = eye.Z + (to.Z - eye.Z) * f;
                    if (map.Ground(x, z) > y - 0.08) return false;
                    if (map.Terrain != null && map.Terrain(x, z) > y - 0.08) return false;
                    foreach (var b in map.Cover)
                        if (x > b.Min.X && x < b.Max.X && y > b.Min.Y && y < b.Max.Y && z > b.Min.Z && z < b.Max.Z) return false;
                }
            }
            return true;
        }

        /// <summary>Too close in the scope to a plate already out (they'd overlap or stand in a row).</summary>
        bool Crowded(Vec3 eye, Silhouette s)
        {
            double Bearing(Vec3 p) => System.Math.Atan2(p.X - eye.X, -(p.Z - eye.Z));
            foreach (var p in plates)
                if (System.Math.Abs(Bearing(p.Shape.Position) - Bearing(s.Position)) < 0.012) return true;
            return false;
        }

        Plate Build(Silhouette s, float groundY, bool painted)
        {
            var foot = Library.ToUnity(s.Position);
            var root = new GameObject(s.Id).transform;
            root.SetParent(transform, false);
            root.position = foot;
            // The plate faces the shooter (down-lane is +z in Unity, so it looks back along -z).
            var fb = new MeshBuilder();
            float bar = Bar, legs = foot.y + bar - groundY;
            foreach (float side in new[] { -1f, 1f })
            {
                var top = new Vector3(side * 0.5f, bar, 0);
                fb.Beam(top + new Vector3(0, -legs, -0.35f), top, 0.05f, 0.05f);
                fb.Beam(top + new Vector3(0, -legs, 0.35f), top, 0.05f, 0.05f);
            }
            fb.Beam(new Vector3(-0.55f, bar, 0), new Vector3(0.55f, bar, 0), 0.06f, 0.06f);
            fb.Object("Frame", frame, root);

            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0, bar, 0);
            float w = (float)s.Width, h = (float)s.Height, head = (float)s.Head;
            var steel = new MeshBuilder();
            const float T = 0.012f;
            // Torso up to the shoulders, the head above (40% of the width, as the hit zones have it).
            steel.Box(new Vector3(0, (h - head) / 2 - bar, 0), new Vector3(w, h - head, T));
            steel.Box(new Vector3(0, h - head / 2 - bar, 0), new Vector3(0.4f * w, head, T));
            steel.Object("Steel", painted ? orange : paint, pivot);
            var chain = new MeshBuilder();
            foreach (float side in new[] { -0.19f, 0.19f })
                chain.Beam(new Vector3(side, h - head - bar, 0), new Vector3(side * 1.2f, 0, 0), 0.012f, 0.012f);
            chain.Object("Chains", frame, pivot, shadows: false);
            return new Plate { Shape = s, Pivot = pivot };
        }

        /// <summary>A round struck plate `id` at `point` with `energyJ`: it swings back and keeps the mark.</summary>
        public void Strike(string id, Vec3 point, double energyJ)
        {
            var p = plates.Find(x => x.Shape.Id == id);
            if (p == null) return;
            p.Down = true;
            p.Spin += Mathf.Clamp((float)energyJ / 3000f * 5f, 2f, 7f);
            var mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mark.name = "Strike";
            Destroy(mark.GetComponent<Collider>());
            mark.transform.SetParent(p.Pivot, false);
            var local = p.Pivot.InverseTransformPoint(Library.ToUnity(point));
            mark.transform.localPosition = new Vector3(local.x, local.y, -0.0075f);
            mark.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            mark.transform.localScale = new Vector3(0.05f, 0.04f, 1);
            var r = mark.GetComponent<MeshRenderer>();
            r.sharedMaterial = bare;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>The plate nearest the line of sight from `eye` along yaw/pitch (rules frame), and its range.</summary>
        public double? RangeNear(Vec3 eye, double yaw, double pitch)
        {
            double best = double.MaxValue;
            double? range = null;
            foreach (var p in plates)
            {
                var c = p.Shape.Position;
                double dx = c.X - eye.X, dz = c.Z - eye.Z, dy = c.Y + p.Shape.Height / 2 - eye.Y;
                double flat = System.Math.Sqrt(dx * dx + dz * dz);
                double by = System.Math.Atan2(dx, -dz) - yaw, bp = System.Math.Atan2(dy, flat) - pitch;
                double off = by * by + bp * bp;
                if (off < best)
                {
                    best = off;
                    range = flat;
                }
            }
            return range;
        }

        void Update()
        {
            float dt = Mathf.Min(0.05f, Time.deltaTime);
            foreach (var p in plates)
            {
                if (p.Angle == 0 && p.Spin == 0) continue;
                // A pendulum on its chains, settling within a few seconds.
                p.Spin += (-16 * Mathf.Sin(p.Angle) - 1.4f * p.Spin) * dt;
                p.Angle += p.Spin * dt;
                if (Mathf.Abs(p.Angle) < 1e-4f && Mathf.Abs(p.Spin) < 1e-3f) p.Angle = p.Spin = 0;
                // Struck from the front, the face swings away down-lane (+z), the foot kicking back.
                p.Pivot.localRotation = Quaternion.Euler(-p.Angle * Mathf.Rad2Deg, 0, 0);
            }
        }
    }
}
