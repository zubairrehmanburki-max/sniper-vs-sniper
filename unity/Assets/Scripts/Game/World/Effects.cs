using SniperDuel.Ballistics;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// What a shot looks like, as it would to a real shooter: the muzzle flash and a blast of dust
    /// and smoke at the rifle, the round itself (a small copper bullet) with the faint vapour trace
    /// that shows through a scope behind it, and a splash, dust plume or spray where it lands. No
    /// glowing tracer: an incoming round is only seen by its shooter's muzzle blast and where it
    /// strikes.
    /// </summary>
    public static class Effects
    {
        static Material? smoke, glow;
        static Texture2D? puff;

        public static Flight Shot(ShotOutcome o, bool mine, Arena arena, bool tracer = true)
        {
            var root = new GameObject("Shot").transform;
            root.SetParent(arena.Root, false);
            var dir = Quaternion.Euler(0, (float)(o.Yaw * Mathf.Rad2Deg), 0) * Vector3.forward;
            var muzzle = Library.ToUnity(o.Eye) + Quaternion.Euler(0, (float)(o.Yaw * Mathf.Rad2Deg), 0) * new Vector3(0, -0.1f, 0.9f);
            Flash(muzzle, root, mine ? 0.06f : 0.12f);
            Blast(muzzle, dir, root, mine);
            // Your own round flies away from you, trace and all; the enemy's is never seen coming,
            // only where it lands.
            var flight = root.gameObject.AddComponent<Flight>();
            flight.Init(o, (float)(arena.Map.Water ?? -1000), visible: mine && tracer, impact: tracer);
            Object.Destroy(root.gameObject, (float)o.Impact.T + 5);
            return flight;
        }

        static void Flash(Vector3 at, Transform parent, float life)
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.4f);
            light.range = 14;
            light.intensity = 30;
            Object.Destroy(go, life);

            // The flash itself: a short-lived hot bloom at the muzzle, a frame or two long.
            var f = Particles("Muzzle flash", at, parent, Glow());
            var main = f.main;
            main.duration = 0.05f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startColor = new Color(1f, 0.72f, 0.38f, 0.9f);
            var em = f.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0, 3) });
            f.Play();
        }

        /// <summary>
        /// The muzzle blast. The brake throws the gas out sideways in a ring; prone, it lifts a
        /// sheet of dust off the ground ahead and to the sides, and a puff of grey smoke drifts
        /// off. At 600 m through a scope this is what gives a shooter away; your own stays out
        /// to the sides, clear of the scope's line of sight.
        /// </summary>
        static void Blast(Vector3 at, Vector3 dir, Transform parent, bool mine)
        {
            var ps = Particles("Muzzle blast", at + dir * 0.35f, parent, Smoke());
            var main = ps.main;
            main.duration = 0.2f;
            main.startLifetime = 2.6f;
            main.gravityModifier = -0.01f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 0.5f, 1, 2.6f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.55f, 0.35f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.5f;
            drag.multiplyDragByParticleSize = false;

            var side = Vector3.Cross(Vector3.up, dir).normalized;
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            var up = Vector3.Cross(dir, side);
            var rng = new System.Random(at.GetHashCode());
            float R() => (float)rng.NextDouble();
            int n = mine ? 14 : 34;
            for (int i = 0; i < n; i++)
            {
                // Round the barrel, flattened toward the ground; a little forward.
                float a = R() * Mathf.PI * 2;
                var ring = side * Mathf.Cos(a) + up * Mathf.Sin(a) * (Mathf.Sin(a) > 0 ? 0.6f : 0.3f);
                var v = (ring * (0.8f + R() * 0.6f) + dir * (mine ? 0.15f : 0.2f + R() * 0.6f)) * (2 + R() * 4);
                var e = new ParticleSystem.EmitParams
                {
                    position = at + dir * 0.35f + ring * (mine ? 0.25f : 0.05f) + Vector3.down * (mine ? 0 : 0.15f),
                    velocity = v,
                    startSize = (mine ? 0.3f : 0.8f) + R() * (mine ? 0.3f : 1.2f),
                    startLifetime = 1.2f + R() * 1.4f,
                    rotation = R() * 360,
                    startColor = Color.Lerp(new Color(0.62f, 0.57f, 0.49f, 0.42f), new Color(0.8f, 0.79f, 0.76f, 0.3f), R()),
                    applyShapeToPosition = false,
                };
                ps.Emit(e, 1);
            }
            Object.Destroy(ps.gameObject, 3.5f);
        }

        /// <summary>A splash on water, a dust plume on ground, a dark spray on a hit, a spark and chips off steel.</summary>
        public static void Impact(Vector3 at, bool water, bool hit, Transform parent, bool steel = false)
        {
            var ps = Particles(water ? "Splash" : steel ? "Strike" : "Dust", at, parent, steel ? Glow() : Smoke());
            var main = ps.main;
            main.duration = 0.4f;
            main.startLifetime = water ? new ParticleSystem.MinMaxCurve(0.8f, 1.4f) : steel ? new ParticleSystem.MinMaxCurve(0.05f, 0.15f) : new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = water ? new ParticleSystem.MinMaxCurve(3, 8) : steel ? new ParticleSystem.MinMaxCurve(2, 6) : new ParticleSystem.MinMaxCurve(0.8f, 3);
            main.startSize = water ? new ParticleSystem.MinMaxCurve(0.15f, 0.45f) : steel ? new ParticleSystem.MinMaxCurve(0.05f, 0.15f) : new ParticleSystem.MinMaxCurve(0.4f, 1.3f);
            main.gravityModifier = water ? 1 : steel ? 0.4f : -0.02f;
            main.startColor = water ? new Color(0.88f, 0.92f, 0.94f, 0.8f)
                : steel ? new Color(1f, 0.8f, 0.5f, 1f)
                : hit ? new Color(0.32f, 0.06f, 0.05f, 0.85f) : new Color(0.56f, 0.5f, 0.42f, 0.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)(water ? 60 : steel ? 12 : 30)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = water ? 12 : steel ? 70 : 40;
            shape.rotation = new Vector3(-90, 0, 0);
            if (!water && !steel)
            {
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 0.7f, 1, 2f));
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
                col.color = g;
            }
            ps.Play();
            Object.Destroy(ps.gameObject, 4);
        }

        static ParticleSystem Particles(string name, Vector3 at, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        /// <summary>A soft round puff with a ragged edge, so smoke and dust read as clouds, not squares.</summary>
        static Texture2D Puff()
        {
            if (puff != null) return puff;
            const int N = 64;
            puff = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Puff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float n = Mathf.PerlinNoise(x * 0.11f + 3.1f, y * 0.11f + 7.3f) * 0.6f + Mathf.PerlinNoise(x * 0.27f, y * 0.27f) * 0.4f;
                    float a = Mathf.Clamp01(1 - r) * Mathf.Clamp01(1 - r);
                    a *= Mathf.Lerp(0.55f, 1.15f, n);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(a * 255, 0, 255));
                }
            puff.SetPixels32(px);
            puff.Apply(true);
            return puff;
        }

        public static Material SmokeMaterial => Smoke();

        static Material Smoke()
        {
            if (smoke != null) return smoke;
            smoke = new Material(Library.Template("Fade")) { name = "Smoke" };
            smoke.SetTexture("_BaseMap", Puff());
            smoke.mainTexture = Puff();
            return smoke;
        }

        static Material Glow()
        {
            if (glow != null) return glow;
            glow = new Material(Library.Template("Additive")) { name = "Muzzle glow" };
            glow.SetTexture("_BaseMap", Puff());
            glow.mainTexture = Puff();
            return glow;
        }
    }

    /// <summary>
    /// The round in flight at its real speed along the real trajectory: a small copper bullet (only
    /// seen close up) and the vapour trace behind it, the faint shimmering wisp of disturbed air a
    /// spotter watches through the scope. The trace is a few pixels wide at any range, pale and
    /// see-through, never glowing; it fades in clear of the muzzle and out over the last part of
    /// the flight. At impact it marks where the round landed.
    /// </summary>
    public sealed class Flight : MonoBehaviour
    {
        const int Points = 18;
        /// <summary>Seconds of flight the trace trails behind the round.</summary>
        const float TraceSeconds = 0.05f;

        ShotOutcome o = null!;
        LineRenderer? line, shimmer;
        Transform? bullet;
        float t;
        bool landed, impact;
        float water;
        readonly Vector3[] pts = new Vector3[Points];

        /// <summary>A round from `outcome`; `visible` draws the bullet and its trace, `impact` the strike where it lands.</summary>
        public void Init(ShotOutcome outcome, float waterY, bool visible, bool impact)
        {
            o = outcome;
            water = waterY;
            this.impact = impact;
            if (!visible) return;
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = Points;
            line.useWorldSpace = true;
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = Library.Template("Fade");
            line.textureMode = LineTextureMode.Stretch;
            var g = new Gradient();
            var c = new Color(0.86f, 0.88f, 0.9f);
            g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.26f, 0.55f), new GradientAlphaKey(0.5f, 0.92f), new GradientAlphaKey(0.2f, 1) });
            line.colorGradient = g;
            // Thin at the round, wider where the disturbed air has spread.
            line.widthCurve = new AnimationCurve(new Keyframe(0, 1.6f), new Keyframe(0.7f, 1.1f), new Keyframe(1, 0.6f));

            // The shimmer round the round itself: the knot of disturbed air a spotter actually picks
            // out, a soft smudge several pixels across that reads even seen from straight behind.
            var sg = new GameObject("Shimmer");
            sg.transform.SetParent(transform, false);
            shimmer = sg.AddComponent<LineRenderer>();
            shimmer.positionCount = 2;
            shimmer.useWorldSpace = true;
            shimmer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shimmer.receiveShadows = false;
            shimmer.sharedMaterial = Effects.SmokeMaterial;
            shimmer.textureMode = LineTextureMode.Stretch;
            shimmer.startColor = shimmer.endColor = new Color(0.9f, 0.92f, 0.94f, 0.4f);

            var b = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            b.name = "Bullet";
            Object.Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(transform, false);
            b.transform.localScale = new Vector3(0.0086f, 0.017f, 0.0086f);
            var r = b.GetComponent<MeshRenderer>();
            r.sharedMaterial = Library.Plain(new Color(0.78f, 0.47f, 0.28f), 0.6f, 0.8f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bullet = b.transform;
        }

        /// <summary>Held where Seek put it (review captures).</summary>
        public bool Frozen;

        /// <summary>Puts the round `seconds` into its flight at once and holds it there (review captures).</summary>
        public void Seek(float seconds)
        {
            t = seconds;
            Frozen = true;
            Draw();
        }

        Vector3 At(double time)
        {
            var p = o.Trajectory.Points;
            if (p.Count == 0) return Library.ToUnity(o.Eye);
            if (time <= p[0].T) return Library.ToUnity(SniperDuel.Rules.World.ShotToWorld(p[0], o.Eye, o.Yaw));
            int i = 1;
            while (i < p.Count && p[i].T < time) i++;
            if (i >= p.Count) return Library.ToUnity(SniperDuel.Rules.World.ShotToWorld(p[p.Count - 1], o.Eye, o.Yaw));
            var a = p[i - 1];
            var b = p[i];
            float f = (float)((time - a.T) / System.Math.Max(1e-6, b.T - a.T));
            return Vector3.Lerp(Library.ToUnity(SniperDuel.Rules.World.ShotToWorld(a, o.Eye, o.Yaw)), Library.ToUnity(SniperDuel.Rules.World.ShotToWorld(b, o.Eye, o.Yaw)), f);
        }

        void Draw()
        {
            double end = o.Impact.T;
            double head = System.Math.Min(t, end);
            var at = At(head);
            if (bullet != null)
            {
                var ahead = At(head + 0.002) - at;
                bullet.position = at;
                if (ahead.sqrMagnitude > 1e-8f) bullet.rotation = Quaternion.LookRotation(ahead) * Quaternion.Euler(90, 0, 0);
                bullet.gameObject.SetActive(t < end);
            }
            if (line == null) return;
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : Library.ToUnity(o.Eye);
            // A couple of pixels wide wherever it is, however far away and whatever the zoom.
            float pixel = cam != null ? 2 * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight) : 0.001f;
            float range = Vector3.Distance(eye, at);
            line.widthMultiplier = Mathf.Max(0.006f, range * pixel * 2.2f);
            float travelled = Vector3.Distance(Library.ToUnity(o.Eye), at);
            // Clear of the muzzle blast before it shows, gone over the last metres before the strike.
            float alpha = Mathf.Clamp01((travelled - 25) / 60f) * Mathf.Clamp01((float)(end - head) / 0.08f + 0.25f);
            float wob = line.widthMultiplier * 0.35f;
            for (int k = 0; k < Points; k++)
            {
                float f = k / (Points - 1f);
                double tt = System.Math.Max(0, head - TraceSeconds * (1 - f));
                var p = At(tt);
                // The disturbed air curls a little as it trails.
                float ph = (float)tt * 900 + k;
                pts[k] = p + new Vector3(Mathf.Sin(ph) * wob, Mathf.Cos(ph * 1.3f) * wob, 0) * (1 - f);
            }
            line.SetPositions(pts);
            if (shimmer != null)
            {
                shimmer.widthMultiplier = Mathf.Max(0.03f, range * pixel * 11);
                var back = At(System.Math.Max(0, head - 0.006));
                // Long enough along the path to stay a soft round smudge even end-on.
                var along = (at - back).normalized * shimmer.widthMultiplier * 0.6f;
                shimmer.SetPosition(0, back - along);
                shimmer.SetPosition(1, at + along);
                shimmer.enabled = alpha > 0.01f && t < end;
                shimmer.startColor = shimmer.endColor = new Color(0.9f, 0.92f, 0.94f, 0.5f * alpha);
            }
            line.enabled = alpha > 0.01f && t < end;
            var gr = line.colorGradient;
            var keys = gr.alphaKeys;
            keys[1].alpha = 0.26f * alpha;
            keys[2].alpha = 0.5f * alpha;
            keys[3].alpha = 0.2f * alpha;
            gr.alphaKeys = keys;
            line.colorGradient = gr;
        }

        void Update()
        {
            if (!Frozen) t += Time.deltaTime;
            Draw();
            if (!landed && t >= o.Impact.T)
            {
                landed = true;
                if (line != null) line.enabled = false;
                if (shimmer != null) shimmer.enabled = false;
                if (!impact) return;
                var p = Library.ToUnity(o.Impact.Point);
                bool splash = o.Verdict == Verdict.Miss && o.Impact.Kind != ImpactKind.Target && p.y <= water + 0.3f;
                bool steel = o.Verdict == Verdict.Miss && o.Impact.Kind == ImpactKind.Target;
                Effects.Impact(p, splash, o.Verdict != Verdict.Miss, transform, steel);
            }
        }
    }
}
