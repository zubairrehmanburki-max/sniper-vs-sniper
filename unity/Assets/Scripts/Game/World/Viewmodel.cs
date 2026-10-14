using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The rifle seen from the shooter's eyes: the same static model the figures carry, parented to
    /// the camera. At rest it sits shouldered low and right with the scope body and barrel running
    /// out toward the target; raising it brings the eyepiece up onto the eye, where the scope view
    /// takes over. The scope's eyepiece is found on the model itself (the rear end of the tall run
    /// over the action); a model without a scope gets a simple one mounted.
    /// </summary>
    public sealed class Viewmodel : MonoBehaviour
    {
        /// <summary>Eyepiece to eye at the cheek weld, metres.</summary>
        const float EyeRelief = 0.075f;
        /// <summary>Shouldered, head up off the stock: where the eyepiece sits in the camera's frame.</summary>
        static readonly Vector3 ReadyOffset = new Vector3(0.075f, -0.062f, 0.2f);
        static readonly Vector3 ReadyTilt = new Vector3(-1.2f, -2.4f, 5f);

        Transform rifle = null!;
        /// <summary>The middle of the eyepiece's rear lens in the rifle's frame.</summary>
        Vector3 eyepiece;

        public static Viewmodel Create(Transform cam)
        {
            // Built at the origin, unrotated, so the rifle's frame is the world's while it is measured.
            var go = new GameObject("Viewmodel");
            var vm = go.AddComponent<Viewmodel>();
            vm.rifle = Figure.Rifle(go.transform).transform;
            vm.eyepiece = vm.FindEyepiece();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                // Its shadow would fall where the hidden body lies, not where the rifle seems to be.
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = true;
            }
            go.transform.SetParent(cam, false);
            Debug.Log($"[Viewmodel] eyepiece at {vm.eyepiece}");
            return vm;
        }

        /// <summary>
        /// Places the rifle for a raise from 0 (shouldered, eye off the scope) to 1 (eye at the
        /// eyepiece). `kick` is the recoil climb in radians; `clock` drives the breathing drift.
        /// </summary>
        public void Pose(float raise, float kick, float clock)
        {
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(raise));
            var tilt = Quaternion.Euler(Vector3.Lerp(ReadyTilt, Vector3.zero, t));
            var at = Vector3.Lerp(ReadyOffset, new Vector3(0, 0, EyeRelief), t);
            // The rifle comes up in a slight arc: lifted, then drawn back to the cheek.
            float arc = Mathf.Sin(t * Mathf.PI);
            at += new Vector3(-0.01f * arc, 0.008f * arc, 0.02f * arc);
            tilt = Quaternion.Euler(0, 0, -3f * arc) * tilt;
            // Breathing drift while the eye is off the scope.
            float calm = 1 - t;
            at += new Vector3(Mathf.Sin(clock * 0.9f) * 0.0012f, Mathf.Sin(clock * 1.7f) * 0.0018f, 0) * calm;
            // Recoil drives the stock back and the muzzle up.
            float k = Mathf.Clamp(kick * 120, 0, 1);
            at += new Vector3(0, 0.004f * k, -0.05f * k);
            tilt = Quaternion.Euler(-6 * k, 0, 0) * tilt;
            rifle.localRotation = tilt;
            rifle.localPosition = at - tilt * eyepiece;
        }

        /// <summary>
        /// Finds the scope's eyepiece on the model: the tall run along the top over the action, well
        /// above the barrel; its rear end is the eyepiece and the tube axis sits an eyepiece radius
        /// below its top. Mounts a scope when the model has none.
        /// </summary>
        Vector3 FindEyepiece()
        {
            var b = Library.BoundsOf(rifle.gameObject);
            float len = Mathf.Max(0.2f, b.size.z);
            var guess = new Vector3(b.center.x, b.max.y - 0.03f, b.min.z + len * 0.4f);
            var pts = new List<Vector3>();
            foreach (var mf in rifle.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                foreach (var v in mf.sharedMesh.vertices) pts.Add(mf.transform.TransformPoint(v));
            }
            // Unreadable meshes: trust the usual layout of a scoped rifle.
            if (pts.Count < 50) return guess;

            const int N = 48;
            var top = new float[N];
            for (int i = 0; i < N; i++) top[i] = float.NegativeInfinity;
            foreach (var p in pts)
            {
                int i = Mathf.Clamp((int)((p.z - b.min.z) / len * N), 0, N - 1);
                top[i] = Mathf.Max(top[i], p.y);
            }
            float Z(int i) => b.min.z + len * i / N;
            // Barrel height: the lowest top anywhere along the front half.
            float barrel = float.PositiveInfinity;
            for (int i = N / 2; i < N * 19 / 20; i++) if (!float.IsInfinity(top[i])) barrel = Mathf.Min(barrel, top[i]);
            // Highest point over the action.
            int peak = -1;
            for (int i = N / 4; i < N * 2 / 3; i++) if (peak < 0 || top[i] > top[peak]) peak = i;
            if (peak >= 0 && !float.IsInfinity(barrel) && top[peak] - barrel > 0.06f)
            {
                // Walk back from the peak while the top stays well above the barrel: the scope.
                float tall = barrel + 0.05f;
                int rear = peak;
                while (rear > N / 8 && top[rear - 1] > tall) rear--;
                float bell = Mathf.Max(top[rear], rear + 1 < N ? top[rear + 1] : top[rear]);
                // Centre it sideways on the scope's top band.
                float sx = 0, sn = 0;
                foreach (var p in pts)
                    if (p.y > bell - 0.03f && p.z >= Z(rear) && p.z < Z(rear + 3)) { sx += p.x; sn++; }
                return new Vector3(sn > 0 ? sx / sn : b.center.x, bell - 0.021f, Z(rear) + 0.004f);
            }

            // No scope on this rifle: mount one over the action.
            float action = float.NegativeInfinity;
            for (int i = N * 3 / 10; i < N / 2; i++) action = Mathf.Max(action, top[i]);
            if (float.IsInfinity(action)) action = b.max.y;
            var eye = new Vector3(b.center.x, action + 0.045f, b.min.z + len * 0.3f);
            MountScope(eye, action);
            return eye;
        }

        /// <summary>A 30 mm tube scope with a 56 mm objective, turrets and rings, eyepiece at `eye`.</summary>
        void MountScope(Vector3 eye, float rail)
        {
            var body = new MeshBuilder();
            var f = Vector3.forward;
            body.Tube(eye, eye + f * 0.075f, 0.021f, 0.021f, 20);
            body.Tube(eye + f * 0.075f, eye + f * 0.1f, 0.021f, 0.015f, 20);
            body.Tube(eye + f * 0.1f, eye + f * 0.25f, 0.015f, 0.015f, 20);
            body.Tube(eye + f * 0.25f, eye + f * 0.29f, 0.015f, 0.029f, 20);
            body.Tube(eye + f * 0.29f, eye + f * 0.36f, 0.029f, 0.029f, 20);
            // Elevation turret on top, windage on the right, parallax on the left.
            var mid = eye + f * 0.175f;
            body.Tube(mid + Vector3.up * 0.012f, mid + Vector3.up * 0.036f, 0.016f, 0.016f, 16, caps: true);
            body.Tube(mid + Vector3.right * 0.012f, mid + Vector3.right * 0.032f, 0.014f, 0.014f, 16, caps: true);
            body.Tube(mid + Vector3.left * 0.012f, mid + Vector3.left * 0.03f, 0.014f, 0.014f, 16, caps: true);
            // Rings down to the rail.
            foreach (float z in new[] { 0.13f, 0.225f })
            {
                body.Tube(eye + f * (z - 0.009f), eye + f * (z + 0.009f), 0.0175f, 0.0175f, 20);
                body.Box(new Vector3(eye.x, (rail + eye.y - 0.015f) / 2, eye.z + z), new Vector3(0.022f, Mathf.Max(0.005f, eye.y - 0.015f - rail), 0.018f));
            }
            body.Object("Scope", Library.Plain(new Color(0.055f, 0.06f, 0.055f), 0.38f), rifle);
            var glass = new MeshBuilder();
            glass.Tube(eye + f * 0.001f, eye + f * 0.003f, 0.0185f, 0.0185f, 20, caps: true);
            glass.Tube(eye + f * 0.355f, eye + f * 0.362f, 0.026f, 0.026f, 20, caps: true);
            glass.Object("Scope glass", Library.Plain(new Color(0.03f, 0.06f, 0.08f), 0.95f), rifle);
        }
    }
}
