using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The rifle seen from the shooter's eyes, parented to the camera. With the first-person asset
    /// (a textured sniper rifle held in gloved hands) the hands come with it; otherwise it is the
    /// same static rifle the figures carry. At rest it sits shouldered low and right, canted a
    /// little, with the barrel running in toward the target and the left hand out on the fore-end;
    /// raising it brings the eyepiece up onto the eye, where the scope view takes over. The
    /// scope's eyepiece is the model's rear lens, or found from its shape (the rear end of the
    /// tall run over the action); a model without a scope gets a simple one mounted.
    /// </summary>
    public sealed class Viewmodel : MonoBehaviour
    {
        /// <summary>Eyepiece to eye at the cheek weld, metres.</summary>
        const float EyeRelief = 0.075f;
        /// <summary>Shouldered, head up off the stock: where the eyepiece sits in the camera's frame.</summary>
        static readonly Vector3 ReadyOffset = new Vector3(0.075f, -0.062f, 0.2f);
        static readonly Vector3 ReadyTilt = new Vector3(-1.2f, -2.4f, 5f);
        /// <summary>
        /// The same with the hands: out to the right and a little further off, the barrel turned in
        /// toward the middle of the screen, so the gloved hand on the fore-end is in view.
        /// </summary>
        static readonly Vector3 ArmsReadyOffset = new Vector3(0.13f, -0.11f, 0.22f);
        static readonly Vector3 ArmsReadyTilt = new Vector3(0, -10f, 6f);

        /// <summary>The first-person asset: rifle and arms, posed holding the rifle in its rest pose.</summary>
        const string ArmsModel = "fps_sniper_arms";
        /// <summary>The asset's rifle is drawn this long (metres), which makes its hands life-size.</summary>
        const float ArmsRifleLength = 1.48f;

        Transform rifle = null!;
        /// <summary>The middle of the eyepiece's rear lens in the rifle's frame.</summary>
        Vector3 eyepiece;
        bool arms;

        public static Viewmodel Create(Transform cam)
        {
            // Built at the origin, unrotated, so the rifle's frame is the world's while it is measured.
            var go = new GameObject("Viewmodel");
            var vm = go.AddComponent<Viewmodel>();
            var held = Arms(go.transform, out var lens);
            if (held != null)
            {
                vm.rifle = held;
                vm.eyepiece = lens;
                vm.arms = true;
            }
            else
            {
                vm.rifle = Figure.Rifle(go.transform).transform;
                vm.eyepiece = vm.FindEyepiece();
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                // Its shadow would fall where the hidden body lies, not where the rifle seems to be.
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = true;
            }
            go.transform.SetParent(cam, false);
            Debug.Log($"[Viewmodel] {(vm.arms ? "hands and rifle" : "rifle")}, eyepiece at {vm.eyepiece}");
            return vm;
        }

        /// <summary>
        /// Places the rifle for a raise from 0 (shouldered, eye off the scope) to 1 (eye at the
        /// eyepiece). `kick` is the recoil climb in radians; `clock` drives the breathing drift.
        /// </summary>
        public void Pose(float raise, float kick, float clock)
        {
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(raise));
            var tilt = Quaternion.Euler(Vector3.Lerp(arms ? ArmsReadyTilt : ReadyTilt, Vector3.zero, t));
            var at = Vector3.Lerp(arms ? ArmsReadyOffset : ReadyOffset, new Vector3(0, 0, EyeRelief), t);
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
        /// The first-person asset, frozen in its rest pose (the hands on the grip and fore-end) and
        /// turned so the barrel runs along +z with the scope on top, sized to <see cref="ArmsRifleLength"/>.
        /// `eyepiece` is the middle of the scope's rear lens in the returned frame. Null when the
        /// asset set doesn't have it.
        /// </summary>
        static Transform? Arms(Transform parent, out Vector3 eyepiece)
        {
            eyepiece = default;
            if (Library.Model(ArmsModel) == null) return null;
            var inner = new GameObject("Hands and rifle").transform;
            inner.SetParent(parent, false);
            var model = Library.Place(ArmsModel, Vector3.zero, 0, 1, inner);
            // The asset's one clip runs fire, bolt and reload; its rest pose is the hold, so nothing plays.
            foreach (var a in model.GetComponentsInChildren<Animation>(true)) DestroyImmediate(a);
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) DestroyImmediate(a);
            Figure.Freeze(model);

            Renderer? body = null, lens = null, scope = null;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name.ToLowerInvariant();
                if (n.StartsWith("reticle") || n.StartsWith("scoperender"))
                {
                    // The asset's own scope picture; the game draws its own.
                    r.enabled = false;
                    continue;
                }
                if (n.StartsWith("base")) body = r;
                else if (n.StartsWith("lens")) lens = r;
                else if (n.StartsWith("scope")) scope = r;
                Dress(r);
            }
            if (body == null)
            {
                Destroy(inner.gameObject);
                return null;
            }

            // Barrel along +z: the long axis, pointing away from the eyepiece (or the scope's middle).
            var bb = body.bounds;
            var rear = lens != null ? lens.bounds.center : scope != null ? scope.bounds.center : bb.center;
            if (bb.size.x > bb.size.z) model.transform.localRotation = Quaternion.Euler(0, bb.center.x > rear.x ? 90 : -90, 0) * model.transform.localRotation;
            else if (bb.center.z < rear.z) model.transform.localRotation = Quaternion.Euler(0, 180, 0) * model.transform.localRotation;
            bb = body.bounds;
            if (bb.size.z > 1e-4f) model.transform.localScale *= ArmsRifleLength / bb.size.z;

            if (lens != null)
            {
                var lb = lens.bounds;
                eyepiece = inner.InverseTransformPoint(new Vector3(lb.center.x, lb.center.y, lb.min.z));
            }
            else if (scope != null)
            {
                var sb = scope.bounds;
                eyepiece = inner.InverseTransformPoint(new Vector3(sb.center.x, sb.center.y, sb.min.z));
            }
            else eyepiece = inner.InverseTransformPoint(new Vector3(bb.center.x, bb.max.y - 0.03f, bb.min.z + bb.size.z * 0.3f));
            return inner;
        }

        /// <summary>
        /// A URP Lit material from the asset's glTF one: colour, normal and occlusion maps, with the
        /// glTF metal/roughness map repacked into Unity's mask (metal in red, smoothness in alpha).
        /// The eyepiece lens becomes dark coated glass.
        /// </summary>
        static void Dress(Renderer r)
        {
            var src = r.sharedMaterials;
            var mats = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var s = src[i];
                if (r.name.ToLowerInvariant().StartsWith("lens"))
                {
                    mats[i] = Library.Plain(new Color(0.02f, 0.035f, 0.032f), 0.96f);
                    continue;
                }
                var m = new Material(Library.Template("Lit")) { name = "Viewmodel " + (s != null ? s.name : r.name) };
                var tex = Tex(s, "baseColorTexture");
                if (tex != null) m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", s != null && s.HasProperty("baseColorFactor") ? s.GetColor("baseColorFactor") : Color.white);
                var nor = Tex(s, "normalTexture");
                if (nor != null) m.SetTexture("_BumpMap", nor);
                var mask = Repack(Tex(s, "metallicRoughnessTexture"));
                if (mask != null)
                {
                    m.SetTexture("_MetallicGlossMap", mask);
                    m.SetTexture("_OcclusionMap", mask);
                    m.SetFloat("_Smoothness", 1);
                }
                else
                {
                    m.SetTexture("_MetallicGlossMap", Figure.NonMetal);
                    m.SetFloat("_Smoothness", 0.3f);
                }
                m.SetFloat("_Metallic", 0);
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }

        static Texture? Tex(Material? m, string prop) => m != null && m.HasProperty(prop) ? m.GetTexture(prop) : null;

        static readonly Dictionary<Texture, Texture2D> Repacked = new Dictionary<Texture, Texture2D>();

        /// <summary>
        /// glTF packs occlusion, roughness and metal in red, green and blue; URP Lit reads metal from
        /// red and smoothness from alpha of its mask, and occlusion from green.
        /// </summary>
        static Texture2D? Repack(Texture? orm)
        {
            if (orm == null) return null;
            if (Repacked.TryGetValue(orm, out var done) && done != null) return done;
            int w = Mathf.Min(orm.width, 2048), h = Mathf.Min(orm.height, 2048);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(orm, rt);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = orm.name + " (mask)", wrapMode = orm.wrapMode };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                px[i] = new Color32(p.b, p.r, 0, (byte)(255 - p.g));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            Repacked[orm] = tex;
            return tex;
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
