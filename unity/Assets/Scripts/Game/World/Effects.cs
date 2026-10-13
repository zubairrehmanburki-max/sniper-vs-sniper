using SniperDuel.Ballistics;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// What a shot looks like: a muzzle flash and dust at the shooter, a faint tracer following the
    /// real trajectory at the bullet's real speed, and a splash, dust puff or hit mark where it lands.
    /// </summary>
    public static class Effects
    {
        public static void Shot(ShotOutcome o, bool mine, Arena arena, RifleConfig rifle)
        {
            var root = new GameObject("Shot").transform;
            root.SetParent(arena.Root, false);
            var muzzle = Library.ToUnity(o.Eye) + Quaternion.Euler(0, (float)(o.Yaw * Mathf.Rad2Deg), 0) * new Vector3(0, -0.1f, 0.9f);
            Flash(muzzle, root, mine ? 0.06f : 0.12f);
            var tracer = root.gameObject.AddComponent<Tracer>();
            tracer.Init(o, (float)(arena.Map.Water ?? -1000));
            Object.Destroy(root.gameObject, (float)o.Impact.T + 4);
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
        }

        /// <summary>A splash on water, a dust puff on ground, a dark spray on a hit.</summary>
        public static void Impact(Vector3 at, bool water, bool hit, Transform parent)
        {
            var go = new GameObject(water ? "Splash" : "Dust");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = water ? 1.2f : 1.8f;
            main.startSpeed = water ? new ParticleSystem.MinMaxCurve(3, 7) : new ParticleSystem.MinMaxCurve(0.5f, 2);
            main.startSize = water ? new ParticleSystem.MinMaxCurve(0.15f, 0.4f) : new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.gravityModifier = water ? 1 : -0.02f;
            main.startColor = water ? new Color(0.85f, 0.9f, 0.92f, 0.85f) : hit ? new Color(0.3f, 0.05f, 0.04f, 0.9f) : new Color(0.55f, 0.5f, 0.42f, 0.7f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)(water ? 60 : 30)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = water ? 12 : 40;
            shape.rotation = new Vector3(-90, 0, 0);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Library.Template(water ? "Additive" : "Fade");
            ps.Play();
            Object.Destroy(go, 4);
        }
    }

    /// <summary>Moves a short glowing streak along the trajectory in real time, then marks the impact.</summary>
    public sealed class Tracer : MonoBehaviour
    {
        ShotOutcome o = null!;
        LineRenderer line = null!;
        float t;
        bool landed;
        float water;

        public void Init(ShotOutcome outcome, float waterY)
        {
            o = outcome;
            water = waterY;
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.05f;
            line.numCapVertices = 2;
            line.sharedMaterial = Library.Template("Additive");
            line.startColor = new Color(1, 0.85f, 0.6f, 0);
            line.endColor = new Color(1, 0.85f, 0.6f, 0.8f);
        }

        Vector3 At(double time)
        {
            var pts = o.Trajectory.Points;
            if (pts.Count == 0) return Library.ToUnity(o.Eye);
            int i = 1;
            while (i < pts.Count && pts[i].T < time) i++;
            if (i >= pts.Count) return Library.ToUnity(SniperDuel.Rules.World.ShotToWorld(pts[pts.Count - 1], o.Eye, o.Yaw));
            var a = pts[i - 1];
            var b = pts[i];
            float f = (float)((time - a.T) / System.Math.Max(1e-6, b.T - a.T));
            var pa = Library.ToUnity(SniperDuel.Rules.World.ShotToWorld(a, o.Eye, o.Yaw));
            var pb = Library.ToUnity(SniperDuel.Rules.World.ShotToWorld(b, o.Eye, o.Yaw));
            return Vector3.Lerp(pa, pb, f);
        }

        void Update()
        {
            t += Time.deltaTime;
            double end = o.Impact.T;
            double head = System.Math.Min(t, end);
            line.SetPosition(0, At(System.Math.Max(0, head - 0.035)));
            line.SetPosition(1, At(head));
            // Wider with distance so it stays visible through the scope.
            line.widthMultiplier = 0.03f + 0.0004f * Vector3.Distance(Library.ToUnity(o.Eye), At(head));
            if (!landed && t >= end)
            {
                landed = true;
                line.enabled = false;
                var p = Library.ToUnity(o.Impact.Point);
                bool splash = o.Verdict == Verdict.Miss && p.y <= water + 0.3f;
                Effects.Impact(p, splash, o.Verdict != Verdict.Miss, transform);
            }
        }
    }
}
