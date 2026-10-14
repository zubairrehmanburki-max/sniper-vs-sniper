using System;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The bullet cam: the camera rides beside the spinning round along its real trajectory in slow
    /// motion, swinging slowly round it, slowing further as the target closes. A few metres out the
    /// target turns to X-ray; at the body the view cuts to the side for the impact, the round going
    /// in, a freeze with a white flash as it meets bone or organ, then the damage playing out.
    /// </summary>
    public sealed class BulletCam : MonoBehaviour
    {
        const float Fast = 0.25f, Slow = 0.04f, SlowFrom = 0.82f, Hold = 1.6f;
        /// <summary>The X-ray comes on when the round is this many metres from the body.</summary>
        const float RevealAt = 6;
        /// <summary>The impact replays from this far before the entry.</summary>
        const float PreRoll = 0.3f;
        /// <summary>Seconds the frame holds still at the first strike.</summary>
        const float Freeze = 0.5f;

        Func<double, (Vector3 p, Vector3 dir)> path = null!;
        double end;
        float impactSpeed = 800;
        HitZone? zone;
        bool hit;
        Vector3 impactPoint;
        CameraRig rig = null!;
        Round round = null!;
        Action<float, float>? progress;
        Action done = null!;
        XRay? xray;
        Vector3 correction;
        double t;
        float clock, held;
        // The impact: the round's distance past the entry, the freeze, the flash.
        bool impact, frozen, struck;
        float s, freezeLeft, flash;
        public float Speed { get; private set; } = Fast;
        public bool Paused;

        static BulletCam Create(Transform parent, CameraRig rig, Action<float, float>? progress, Action done)
        {
            var go = new GameObject("BulletCam");
            go.transform.SetParent(parent, false);
            var bc = go.AddComponent<BulletCam>();
            bc.rig = rig;
            bc.progress = progress;
            bc.done = done;
            bc.round = Round.Create(go.transform);
            rig.Current = CameraRig.Mode.Director;
            rig.Cam.fieldOfView = 40;
            return bc;
        }

        public static BulletCam Play(ShotOutcome o, CameraRig rig, Transform parent, Action<float, float>? progress, Action done, Figure? target = null)
        {
            var bc = Create(parent, rig, progress, done);
            bc.end = o.Impact.T;
            bc.impactSpeed = Mathf.Max(100, (float)o.Impact.Speed);
            bc.zone = o.Zone;
            bc.hit = o.Verdict != Verdict.Miss;
            bc.impactPoint = Library.ToUnity(o.Impact.Point);
            var pts = o.Trajectory.Points;
            bc.path = time =>
            {
                int i = 1;
                while (i < pts.Count - 1 && pts[i].T < time) i++;
                var a = Library.ToUnity(Rules.World.ShotToWorld(pts[i - 1], o.Eye, o.Yaw));
                var b = Library.ToUnity(Rules.World.ShotToWorld(pts[i], o.Eye, o.Yaw));
                var dir = (b - a).sqrMagnitude > 1e-8f ? (b - a).normalized : Vector3.forward;
                float f = (float)((time - pts[i - 1].T) / Math.Max(1e-6, pts[i].T - pts[i - 1].T));
                return (Vector3.Lerp(a, b, Mathf.Clamp01(f)), dir);
            };
            bc.Setup(bc.hit ? target : null);
            bc.Place();
            Sound.KillCamBegin();
            return bc;
        }

        /// <summary>
        /// A bullet cam on a straight shot from `from` to `to`, paused, for review captures: Seek
        /// to a point in the flight or SeekImpact into the X-ray, then Finish.
        /// </summary>
        public static BulletCam Rehearse(Vector3 from, Vector3 to, CameraRig rig, Transform parent, Figure? target, HitZone? zone)
        {
            var bc = Create(parent, rig, null, () => { });
            float dist = Mathf.Max(1, Vector3.Distance(from, to));
            var along = (to - from) / dist;
            float rise = dist * 0.0004f;
            bc.end = dist / 800.0;
            bc.impactSpeed = 760;
            bc.zone = zone;
            bc.hit = true;
            bc.impactPoint = to;
            bc.Paused = true;
            bc.path = time =>
            {
                float f = Mathf.Clamp01((float)(time / bc.end));
                var p = Vector3.Lerp(from, to, f) + Vector3.up * 4 * rise * f * (1 - f);
                return (p, (along + Vector3.up * 4 * rise * (1 - 2 * f) / dist).normalized);
            };
            bc.Setup(target);
            return bc;
        }

        /// <summary>For review: the round a fraction of the way along its flight.</summary>
        public void Seek(float fraction)
        {
            t = Mathf.Clamp01(fraction) * end;
            clock = 1.3f;
            Place();
        }

        /// <summary>For review: the impact, `seconds` after the round meets its first part.</summary>
        public void SeekImpact(float seconds)
        {
            if (xray == null) return;
            t = end;
            clock = 2;
            BeginImpact();
            frozen = struck = true;
            s = xray.FirstHit + seconds * XRay.Pace;
            xray.Evaluate(s);
            PlaceImpact();
        }

        /// <summary>Builds the X-ray for a hit and steers the end of the flight onto its entry.</summary>
        void Setup(Figure? target)
        {
            if (target == null) return;
            xray = XRay.Build(target);
            var (pEnd, dEnd) = path(end);
            var entry = xray.Aim(pEnd, dEnd, zone);
            correction = entry - pEnd;
        }

        (Vector3 p, Vector3 dir) Pos(double time)
        {
            var (p, dir) = path(Math.Min(time, end));
            float f = (float)(time / Math.Max(1e-6, end));
            return (p + correction * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.9f, 1, f)), dir);
        }

        float Remaining(Vector3 p) => Vector3.Distance(p, Pos(end).p);

        void Place()
        {
            var (p, dir) = Pos(t);
            float near = Mathf.Clamp01(1 - Remaining(p) / 12);
            var cam = rig.Cam.transform;
            // Above the round, swinging slowly from side to side; dropping back as the target nears.
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            var up = Vector3.Cross(dir, side);
            float th = Mathf.PI * 0.5f + 0.95f * Mathf.Sin(clock * 0.45f);
            float radial = Mathf.Lerp(0.09f, 0.11f, near) + 0.015f * Mathf.Sin(clock * 0.8f);
            float back = Mathf.Lerp(0.13f, 0.3f, near);
            cam.position = p - dir * back + (side * Mathf.Cos(th) + up * Mathf.Sin(th)) * radial;
            cam.rotation = Quaternion.LookRotation(p + dir * Mathf.Lerp(0.4f, 0.9f, near) - cam.position, Vector3.up);
            rig.Cam.fieldOfView = 40;
            // Spin: a 1:10 twist at ~800 m/s is ~3000 rev/s; show it slowed with the clock.
            round.Place(p, dir, (float)(t * 360 * 30) + clock * 220, Mathf.Lerp(2.5f, 1.2f, near), cam.position, clock);
            progress?.Invoke((float)(t / Math.Max(0.01, end)), Speed);
        }

        void BeginImpact()
        {
            if (xray == null) return;
            xray.Show();
            impact = true;
            s = -PreRoll;
            xray.Backdrop(true);
        }

        /// <summary>The cut: side on to the round's path through the body, drifting round as it plays out.</summary>
        void PlaceImpact()
        {
            if (xray == null) return;
            var e = xray.Entry;
            var d = xray.Dir;
            var cam = rig.Cam.transform;
            var side = Vector3.Cross(Vector3.up, d).normalized;
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            float total = xray.Exit + PreRoll + XRay.Pace * Hold;
            float prog = Mathf.Clamp01((s + PreRoll) / total);
            var mid = e + d * Mathf.Min(0.14f, xray.Exit * 0.5f);
            var off = Quaternion.AngleAxis(Mathf.Lerp(-16, 14, prog), Vector3.up) * (side * 0.92f - d * 0.32f) + Vector3.up * 0.36f;
            float punch = freezeLeft > 0 ? freezeLeft / Freeze : 0;
            cam.position = mid + off * (1 - 0.1f * prog - 0.12f * punch);
            if (punch > 0)
            {
                // A little shake in the freeze.
                cam.position += new Vector3(Mathf.Sin(clock * 71), Mathf.Sin(clock * 53), Mathf.Sin(clock * 67)) * 0.004f * punch;
            }
            cam.rotation = Quaternion.LookRotation(mid - cam.position, Vector3.up);
            rig.Cam.fieldOfView = 42;
            round.Place(e + d * s, d, (float)(t * 360 * 30) + clock * 220, Mathf.Clamp(s + PreRoll, 0.02f, 0.5f), cam.position, clock);
            progress?.Invoke(1, freezeLeft > 0 ? 0 : XRay.Pace / impactSpeed);
        }

        void Update()
        {
            Sound.KillCamPaused(Paused);
            if (Paused) return;
            float dt = Time.deltaTime;
            clock += dt;
            if (impact && xray != null)
            {
                if (freezeLeft > 0) freezeLeft -= dt;
                else s += dt * XRay.Pace;
                if (!struck && s >= 0)
                {
                    struck = true;
                    Sound.KillCamImpact(true);
                }
                if (!frozen && s >= xray.FirstHit)
                {
                    // Hold on the moment the round meets the first bone or organ.
                    frozen = true;
                    s = xray.FirstHit + 0.012f;
                    freezeLeft = Freeze;
                    flash = 1;
                }
                flash = Mathf.Max(0, flash - dt * 3.2f);
                PostFx.Flash(flash);
                xray.Evaluate(s);
                PlaceImpact();
                if (s > xray.Exit + XRay.Pace * Hold) Finish();
                return;
            }
            if (t < end)
            {
                var (p, _) = Pos(t);
                float rem = Remaining(p);
                Speed = t / end > SlowFrom ? Slow : Fast;
                // Easing right down over the last few metres.
                if (rem < 10) Speed = Slow * Mathf.Lerp(0.15f, 1, rem / 10);
                Sound.KillCamSlow(Speed < Fast);
                // Close in on a hit: the X-ray shows where it's going in.
                if (xray != null && !xray.Shown && rem < RevealAt) xray.Show();
                t = Math.Min(end, t + dt * Speed);
                Place();
                if (t >= end)
                {
                    if (xray != null)
                    {
                        BeginImpact();
                        xray.Evaluate(s);
                        PlaceImpact();
                    }
                    else
                    {
                        Effects.Impact(impactPoint, false, hit, transform.parent);
                        Sound.KillCamImpact(hit);
                    }
                }
            }
            else
            {
                // A miss (no X-ray): hold on the impact, easing the camera back a little.
                held += dt;
                rig.Cam.transform.position -= rig.Cam.transform.forward * dt * 0.6f;
                if (held > Hold) Finish();
            }
        }

        bool finished;

        public void Finish()
        {
            if (finished) return;
            finished = true;
            Sound.KillCamPaused(false);
            Sound.KillCamEnd();
            PostFx.Flash(0);
            if (xray != null) xray.End();
            Destroy(gameObject);
            rig.Current = CameraRig.Mode.Eye;
            done();
        }

        /// <summary>
        /// The round in flight: a copper-jacketed boat-tail bullet with rifling engraved round its
        /// bearing surface (so the spin reads), trailing a widening shimmer of disturbed air and the
        /// faint shock cone off its nose. Local +z is the direction of flight.
        /// </summary>
        sealed class Round
        {
            const int Rings = 36, Sides = 10, ConeRings = 6;
            Transform holder = null!, spinner = null!;
            Mesh trail = null!;
            Vector3[] tv = null!, tn = null!;
            Color[] tc = null!;

            public static Round Create(Transform parent)
            {
                var r = new Round();
                r.holder = new GameObject("Round").transform;
                r.holder.SetParent(parent, false);
                r.spinner = new GameObject("Spin").transform;
                r.spinner.SetParent(r.holder, false);

                // Profile of a .338 250 gr boat-tail: base, tail, bearing surface, tangent ogive.
                const float R = 0.0043f, Bear = 0.017f, Ogive = 0.023f;
                float rho = (R * R + Ogive * Ogive) / (2 * R);
                var prof = new System.Collections.Generic.List<(float z, float r)> { (0, 0.0034f), (0, 0.0034f) };
                for (int i = 1; i <= 4; i++) prof.Add((0.0045f * i / 4, Mathf.Lerp(0.0034f, R, i / 4f)));
                prof.Add((Bear, R));
                for (int i = 1; i <= 14; i++)
                {
                    float x = i / 14f * Ogive;
                    prof.Add((Bear + x, Mathf.Max(0.0006f, Mathf.Sqrt(rho * rho - x * x) - (rho - R))));
                }
                var rings = new Vector3[prof.Count][];
                for (int k = 0; k < prof.Count; k++)
                {
                    rings[k] = new Vector3[20];
                    for (int j = 0; j < 20; j++)
                    {
                        float a = j * Mathf.PI * 2 / 20;
                        rings[k][j] = new Vector3(Mathf.Cos(a) * prof[k].r, Mathf.Sin(a) * prof[k].r, prof[k].z);
                    }
                }
                var mb = new MeshBuilder();
                Shapes.Loft(mb, rings, Vector3.zero, new Vector3(0, 0, Bear + Ogive + 0.0003f));
                mb.Object("Bullet", Library.Plain(new Color(0.86f, 0.5f, 0.3f), 0.85f, 1), r.spinner, shadows: false);
                // Rifling: six engraved grooves twisting along the bearing surface.
                var grooves = new MeshBuilder();
                for (int g = 0; g < 6; g++)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float z0 = Mathf.Lerp(0.0048f, 0.0166f, i / 4f), z1 = Mathf.Lerp(0.0048f, 0.0166f, (i + 1) / 4f);
                        float a0 = g * Mathf.PI / 3 + z0 * 26, a1 = g * Mathf.PI / 3 + z1 * 26;
                        Vector3 P(float a, float z) => new Vector3(Mathf.Cos(a) * R * 1.004f, Mathf.Sin(a) * R * 1.004f, z);
                        var outward = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0);
                        grooves.Face(P(a0 - 0.1f, z0), P(a0 + 0.1f, z0), P(a1 + 0.1f, z1), P(a1 - 0.1f, z1), outward);
                    }
                }
                grooves.Object("Rifling", Library.Plain(new Color(0.5f, 0.26f, 0.15f), 0.55f, 1), r.spinner, shadows: false);

                // The air: a tube of rings trailing back, then the shock cone off the tip.
                var go = new GameObject("Trail");
                go.transform.SetParent(r.holder, false);
                r.trail = new Mesh { name = "Trail" };
                r.trail.MarkDynamic();
                int n = (Rings + ConeRings) * Sides;
                r.tv = new Vector3[n];
                r.tn = new Vector3[n];
                r.tc = new Color[n];
                var tris = new System.Collections.Generic.List<int>();
                foreach (var (first, count) in new[] { (0, Rings), (Rings, ConeRings) })
                {
                    for (int k = first; k < first + count - 1; k++)
                    {
                        for (int j = 0; j < Sides; j++)
                        {
                            int a = k * Sides + j, b = k * Sides + (j + 1) % Sides, c = (k + 1) * Sides + (j + 1) % Sides, d = (k + 1) * Sides + j;
                            // Both faces: the haze is seen from inside and out.
                            tris.AddRange(new[] { a, b, c, a, c, d, a, c, b, a, d, c });
                        }
                    }
                }
                r.trail.vertices = r.tv;
                r.trail.SetTriangles(tris, 0);
                go.AddComponent<MeshFilter>().sharedMesh = r.trail;
                var mr = go.AddComponent<MeshRenderer>();
                var haze = new Material(Library.Template("Fade"));
                haze.SetColor("_BaseColor", new Color(0.88f, 0.92f, 1f, 1));
                mr.sharedMaterial = haze;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                return r;
            }

            /// <summary>Puts the round at `p` heading `dir`, spun `spin` degrees, with `trailLen` metres of wake.</summary>
            public void Place(Vector3 p, Vector3 dir, float spin, float trailLen, Vector3 eye, float time)
            {
                holder.position = p;
                holder.rotation = Quaternion.LookRotation(dir);
                spinner.localRotation = Quaternion.Euler(0, 0, spin);
                var e = holder.InverseTransformPoint(eye);
                for (int k = 0; k < Rings + ConeRings; k++)
                {
                    bool cone = k >= Rings;
                    Vector3 c;
                    float r, alpha;
                    if (!cone)
                    {
                        float a = k / (float)(Rings - 1);
                        float d = 0.008f + trailLen * Mathf.Pow(a, 1.6f);
                        // Rolling, widening, wavering with the turbulence behind the base.
                        r = 0.0042f + d * 0.011f;
                        float wob = 0.0012f * (1 + d * 3) * Mathf.Clamp01(d * 6);
                        c = new Vector3(Mathf.Sin(d * 23 + time * 7) * wob, Mathf.Cos(d * 19 - time * 6) * wob, -d);
                        alpha = 0.3f * Mathf.Pow(1 - a, 1.3f) * Mathf.Clamp01(d / 0.03f);
                    }
                    else
                    {
                        // Mach ~2.3: a cone half-angle of about 26° back from the tip.
                        float a = (k - Rings) / (float)(ConeRings - 1);
                        float back = a * 0.07f;
                        c = new Vector3(0, 0, 0.04f - back);
                        r = 0.0002f + back * 0.49f;
                        alpha = 0.16f * (1 - a) * Mathf.Clamp01(a * 5);
                    }
                    for (int j = 0; j < Sides; j++)
                    {
                        float ph = j * Mathf.PI * 2 / Sides;
                        var nrm = new Vector3(Mathf.Cos(ph), Mathf.Sin(ph), 0);
                        var v = c + nrm * r;
                        int i = k * Sides + j;
                        tv[i] = v;
                        tn[i] = nrm;
                        // Like a refraction edge: clear face-on, bright where it turns away from the eye.
                        var view = (e - v).normalized;
                        float rim = 1 - Mathf.Abs(Vector3.Dot(nrm, view));
                        tc[i] = new Color(1, 1, 1, alpha * (0.15f + 0.85f * rim * rim));
                    }
                }
                trail.vertices = tv;
                trail.normals = tn;
                trail.colors = tc;
                trail.bounds = new Bounds(new Vector3(0, 0, -1.5f), new Vector3(1, 1, 4));
            }
        }
    }
}
