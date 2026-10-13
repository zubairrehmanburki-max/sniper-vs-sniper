using System;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The bullet cam: the camera rides just behind the round along its real trajectory in slow
    /// motion, slowing further over the last stretch, then holds on the impact.
    /// </summary>
    public sealed class BulletCam : MonoBehaviour
    {
        const float Fast = 0.25f, Slow = 0.04f, SlowFrom = 0.82f, Hold = 1.6f;

        ShotOutcome o = null!;
        CameraRig rig = null!;
        Transform bullet = null!;
        Action<float, float>? progress;
        Action done = null!;
        Figure? target;
        XRay? xray;
        double t;
        float held;
        public float Speed { get; private set; } = Fast;
        public bool Paused;

        public static BulletCam Play(ShotOutcome o, CameraRig rig, Transform parent, Action<float, float>? progress, Action done, Figure? target = null)
        {
            var go = new GameObject("BulletCam");
            go.transform.SetParent(parent, false);
            var bc = go.AddComponent<BulletCam>();
            bc.o = o;
            bc.rig = rig;
            bc.progress = progress;
            bc.done = done;
            bc.target = o.Verdict != Verdict.Miss ? target : null;
            rig.Current = CameraRig.Mode.Director;
            rig.Cam.fieldOfView = 40;

            // A .338 round: brass case with a copper ogive, along +z.
            var mb = new MeshBuilder();
            mb.Cylinder(Vector3.zero, 0.0043f, 0.024f, 16);
            var bullet = mb.Object("Bullet", Library.Plain(new Color(0.72f, 0.42f, 0.24f), 0.85f, 1), go.transform, shadows: false);
            var tip = new MeshBuilder();
            for (int i = 0; i < 6; i++)
            {
                float r = 0.0043f * (1 - i / 6f);
                tip.Cylinder(new Vector3(0, 0.024f + i * 0.003f, 0), r, 0.003f, 16);
            }
            tip.Object("Tip", Library.Plain(new Color(0.75f, 0.45f, 0.26f), 0.85f, 1), bullet.transform, shadows: false);
            // Cylinders grow along +y; lay the round along +z.
            var holder = new GameObject("Round").transform;
            holder.SetParent(go.transform, false);
            bullet.transform.SetParent(holder, false);
            bullet.transform.localRotation = Quaternion.Euler(90, 0, 0);
            bc.bullet = holder;
            bc.Place();
            return bc;
        }

        Vector3 At(double time, out Vector3 dir)
        {
            var pts = o.Trajectory.Points;
            int i = 1;
            while (i < pts.Count - 1 && pts[i].T < time) i++;
            var a = Library.ToUnity(Rules.World.ShotToWorld(pts[i - 1], o.Eye, o.Yaw));
            var b = Library.ToUnity(Rules.World.ShotToWorld(pts[i], o.Eye, o.Yaw));
            dir = (b - a).sqrMagnitude > 1e-8f ? (b - a).normalized : Vector3.forward;
            float f = (float)((time - pts[i - 1].T) / Math.Max(1e-6, pts[i].T - pts[i - 1].T));
            return Vector3.Lerp(a, b, Mathf.Clamp01(f));
        }

        void Place()
        {
            double end = o.Impact.T;
            var p = At(Math.Min(t, end), out var dir);
            bullet.position = p;
            bullet.rotation = Quaternion.LookRotation(dir);
            // Spin: a 1:10 twist at ~800 m/s is ~3000 rev/s; show it slowed with the clock.
            bullet.Rotate(0, 0, (float)(t * 360 * 30), Space.Self);
            float side = Mathf.Lerp(0.05f, 0.12f, (float)(t / Math.Max(0.01, end)));
            var cam = rig.Cam.transform;
            cam.position = p - dir * 0.14f + Vector3.up * 0.03f + Vector3.Cross(Vector3.up, dir) * side;
            cam.rotation = Quaternion.LookRotation(p + dir * 0.6f - cam.position);
            progress?.Invoke((float)(t / Math.Max(0.01, end)), Speed);
        }

        void Update()
        {
            if (Paused) return;
            double end = o.Impact.T;
            if (t < end)
            {
                Speed = t / end > SlowFrom ? Slow : Fast;
                // Into the last stretch on a hit: the X-ray shows where it's going in.
                if (Speed == Slow && xray == null && target != null)
                {
                    var impact = At(end, out var dir);
                    xray = XRay.Begin(target, impact, dir);
                }
                t = Math.Min(end, t + Time.deltaTime * Speed);
                Place();
                if (t >= end) Effects.Impact(Library.ToUnity(o.Impact.Point), false, o.Verdict != Verdict.Miss, transform.parent);
            }
            else
            {
                // Hold on the impact, easing the camera back a little.
                held += Time.deltaTime;
                rig.Cam.transform.position -= rig.Cam.transform.forward * Time.deltaTime * 0.6f;
                if (held > Hold) Finish();
            }
        }

        bool finished;

        public void Finish()
        {
            if (finished) return;
            finished = true;
            if (xray != null) xray.End();
            Destroy(gameObject);
            rig.Current = CameraRig.Mode.Eye;
            done();
        }
    }
}
