using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The life in the air that makes a still scene read as a real place: specks of dust or pollen
    /// drifting through the sunlight round the camera, birds wheeling over the valley (gulls over
    /// the sea and the harbour), low mist lying on the marsh and the sea, and gusts lifting sand off
    /// the desert floor. All of it drifts with the lane wind and stays clear of the firing spots.
    /// </summary>
    public static class Ambience
    {
        /// <summary>What drifts in a map's air.</summary>
        sealed class Air
        {
            public Color Motes = new Color(1f, 0.95f, 0.85f, 0.55f);
            public float MoteRate = 14;
            public bool Gulls;
            public int Birds = 7;
            public bool Mist, Dust;
        }

        static Air AirFor(string id) => id switch
        {
            "dry-valley" => new Air { Motes = new Color(0.86f, 0.74f, 0.58f, 0.6f), MoteRate = 30, Birds = 4, Dust = true },
            "alpine-ridge" => new Air { Motes = new Color(0.95f, 0.97f, 1f, 0.7f), MoteRate = 10, Birds = 3 },
            "oil-rigs" => new Air { Motes = new Color(0.9f, 0.93f, 0.95f, 0.4f), MoteRate = 8, Gulls = true, Birds = 9, Mist = true },
            "harbor-rooftops" => new Air { MoteRate = 10, Gulls = true, Birds = 8 },
            "watchtowers" => new Air { Motes = new Color(0.95f, 0.95f, 0.85f, 0.5f), MoteRate = 18, Birds = 10, Mist = true },
            "twin-bridges" => new Air { MoteRate = 12, Gulls = true, Birds = 7 },
            _ => new Air(),
        };

        static ParticleSystem[] drifting = System.Array.Empty<ParticleSystem>();

        public static void Build(DuelMap map, Transform parent)
        {
            var air = AirFor(map.Id);
            var root = new GameObject("Ambience").transform;
            root.SetParent(parent, false);
            float mid = (float)map.LaneLength / 2;
            float water = (float)(map.Water ?? -1000);
            var ground = map.Terrain ?? map.Ground;
            var list = new System.Collections.Generic.List<ParticleSystem> { Motes(root, air) };
            if (air.Mist && water > -999) list.Add(Mist(root, mid, water, (float)map.LaneLength));
            if (air.Dust) list.Add(Dust(root, mid, (float)map.LaneLength, (x, s) => (float)ground(x, -s)));
            drifting = list.ToArray();
            // Birds circle well off to one side of the lane, high enough never to cross a shot.
            var rng = new System.Random(map.Id.GetHashCode());
            float side = rng.NextDouble() < 0.5 ? -1 : 1;
            float baseY = Mathf.Max(water, (float)ground(side * 160, -mid)) + 45;
            Flock.Create(root, new Vector3(side * (160 + (float)rng.NextDouble() * 80), baseY, mid + ((float)rng.NextDouble() - 0.5f) * 200),
                air.Birds, air.Gulls, rng.Next());
        }

        /// <summary>Lets the dust, mist and motes drift with the lane wind (Unity world m/s).</summary>
        public static void SetWind(Vector3 wind)
        {
            foreach (var ps in drifting)
            {
                if (ps == null) continue;
                var v = ps.velocityOverLifetime;
                v.enabled = true;
                v.space = ParticleSystemSimulationSpace.World;
                float k = ps.name == "Motes" ? 0.35f : 1f;
                v.x = new ParticleSystem.MinMaxCurve(wind.x * k);
                v.y = new ParticleSystem.MinMaxCurve(0);
                v.z = new ParticleSystem.MinMaxCurve(wind.z * k);
            }
        }

        /// <summary>Specks in the sunlight a few metres in front of the eye; they ride along with the camera.</summary>
        static ParticleSystem Motes(Transform root, Air air)
        {
            var ps = Emitter(root, "Motes", Effects.SmokeMaterial);
            ps.gameObject.AddComponent<FollowCamera>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6, 11);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startColor = air.Motes;
            main.maxParticles = 300;
            var em = ps.emission;
            em.rateOverTime = air.MoteRate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(14, 4, 12);
            shape.position = new Vector3(0, 0.5f, 9);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.1f;
            Fade(ps, 0.15f, 0.85f);
            ps.Play();
            return ps;
        }

        /// <summary>Low banks of mist lying on the water, very faint, drifting downwind.</summary>
        static ParticleSystem Mist(Transform root, float mid, float water, float lane)
        {
            var ps = Emitter(root, "Mist", Effects.SmokeMaterial);
            ps.transform.position = new Vector3(0, water + 3, mid);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 30;
            main.startLifetime = 45;
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(40, 90);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(0.86f, 0.89f, 0.9f, 0.09f);
            main.maxParticles = 80;
            var em = ps.emission;
            em.rateOverTime = 1.6f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Out to the sides and beyond the far spots; the lane itself stays clear for the shot.
            shape.scale = new Vector3(900, 2, lane + 900);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.sortingFudge = 50;
            Fade(ps, 0.3f, 0.7f);
            ps.gameObject.AddComponent<ClearLane>().HalfWidth = 60;
            ps.Play();
            return ps;
        }

        /// <summary>Gusts lifting a veil of sand off the desert floor in the middle distance.</summary>
        static ParticleSystem Dust(Transform root, float mid, float lane, System.Func<float, float, float> ground)
        {
            var ps = Emitter(root, "Dust", Effects.SmokeMaterial);
            ps.transform.position = new Vector3(0, ground(0, mid) + 1, mid);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 20;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8, 14);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(5, 14);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(0.8f, 0.68f, 0.52f, 0.13f);
            main.gravityModifier = -0.005f;
            main.maxParticles = 120;
            var em = ps.emission;
            em.rateOverTime = 4;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1400, 1, lane + 600);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 0.5f, 1, 1.6f));
            Fade(ps, 0.25f, 0.6f);
            ps.gameObject.AddComponent<ClearLane>().HalfWidth = 40;
            ps.Play();
            return ps;
        }

        static ParticleSystem Emitter(Transform root, string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        /// <summary>Fades each particle in over the first `inEnd` of its life and out after `outStart`.</summary>
        static void Fade(ParticleSystem ps, float inEnd, float outStart)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, inEnd), new GradientAlphaKey(1, outStart), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        /// <summary>Keeps an emitter on the main camera (position and heading, not tilt).</summary>
        sealed class FollowCamera : MonoBehaviour
        {
            void LateUpdate()
            {
                var cam = Camera.main;
                if (cam == null) return;
                transform.position = cam.transform.position;
                var f = cam.transform.forward;
                f.y = 0;
                if (f.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(f);
            }
        }

        /// <summary>Takes out any particle that drifts into the lane between the spots, so no mist or dust ever veils a shot.</summary>
        sealed class ClearLane : MonoBehaviour
        {
            public float HalfWidth;
            ParticleSystem ps = null!;
            ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[0];
            float next;

            void Awake() => ps = GetComponent<ParticleSystem>();

            void LateUpdate()
            {
                if (Time.time < next) return;
                next = Time.time + 0.25f;
                int max = ps.main.maxParticles;
                if (buffer.Length < max) buffer = new ParticleSystem.Particle[max];
                int n = ps.GetParticles(buffer);
                bool changed = false;
                for (int i = 0; i < n; i++)
                {
                    // A particle's reach is half its size: none may hang over the lane.
                    if (Mathf.Abs(buffer[i].position.x) < HalfWidth + buffer[i].GetCurrentSize(ps) / 2)
                    {
                        buffer[i].remainingLifetime = 0;
                        changed = true;
                    }
                }
                if (changed) ps.SetParticles(buffer, n);
            }
        }

        /// <summary>A loose flock wheeling round a point: each bird a body and two flapping wings, gliding between flaps.</summary>
        sealed class Flock : MonoBehaviour
        {
            sealed class Bird
            {
                public Transform Body = null!, Left = null!, Right = null!;
                public float Radius, Height, Speed, Angle, Phase, Flap;
            }

            Bird[] birds = new Bird[0];

            public static void Create(Transform parent, Vector3 centre, int count, bool gulls, int seed)
            {
                var go = new GameObject(gulls ? "Gulls" : "Birds");
                go.transform.SetParent(parent, false);
                go.transform.position = centre;
                var flock = go.AddComponent<Flock>();
                var rng = new System.Random(seed);
                float R() => (float)rng.NextDouble();
                var mat = Library.Plain(gulls ? new Color(0.82f, 0.83f, 0.82f) : new Color(0.12f, 0.11f, 0.1f), 0.2f);
                float span = gulls ? 0.6f : 0.4f;
                var wing = WingMesh(span);
                var body = new MeshBuilder();
                body.Box(Vector3.zero, new Vector3(0.07f, 0.07f, gulls ? 0.42f : 0.3f));
                flock.birds = new Bird[count];
                for (int i = 0; i < count; i++)
                {
                    var b = new GameObject("Bird").transform;
                    b.SetParent(go.transform, false);
                    body.Object("Body", mat, b, shadows: false);
                    flock.birds[i] = new Bird
                    {
                        Body = b,
                        Left = Wing(b, wing, mat, -1),
                        Right = Wing(b, wing, mat, 1),
                        Radius = 30 + R() * 50,
                        Height = R() * 25,
                        Speed = 9 + R() * 4,
                        Angle = R() * Mathf.PI * 2,
                        Phase = R() * 10,
                    };
                }
            }

            static Mesh WingMesh(float span)
            {
                var m = new Mesh { name = "Wing" };
                m.vertices = new[] { new Vector3(0, 0, 0.1f), new Vector3(0, 0, -0.08f), new Vector3(span, 0, -0.05f), new Vector3(span * 0.6f, 0, 0.06f) };
                m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }

            static Transform Wing(Transform body, Mesh mesh, Material mat, int side)
            {
                var w = new GameObject(side < 0 ? "Left" : "Right").transform;
                w.SetParent(body, false);
                w.localScale = new Vector3(side, 1, 1);
                w.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = w.gameObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return w;
            }

            void Update()
            {
                float t = Time.time;
                foreach (var b in birds)
                {
                    b.Angle += b.Speed / b.Radius * Time.deltaTime;
                    float wobble = Mathf.Sin(t * 0.3f + b.Phase) * 6;
                    var pos = new Vector3(Mathf.Cos(b.Angle) * (b.Radius + wobble), b.Height + Mathf.Sin(t * 0.5f + b.Phase) * 3, Mathf.Sin(b.Angle) * (b.Radius + wobble));
                    var ahead = new Vector3(-Mathf.Sin(b.Angle), 0, Mathf.Cos(b.Angle));
                    b.Body.localPosition = pos;
                    // Banked into the turn.
                    b.Body.localRotation = Quaternion.LookRotation(ahead) * Quaternion.Euler(0, 0, 18);
                    // Flap in bursts, glide in between.
                    float burst = Mathf.Sin(t * 0.7f + b.Phase) > 0.2f ? 1 : 0;
                    b.Flap = Mathf.Lerp(b.Flap, burst, Time.deltaTime * 3);
                    float a = Mathf.Lerp(4, Mathf.Sin(t * 14 + b.Phase) * 38, b.Flap);
                    b.Left.localRotation = Quaternion.Euler(0, 0, -a);
                    b.Right.localRotation = Quaternion.Euler(0, 0, a);
                }
            }
        }
    }
}
