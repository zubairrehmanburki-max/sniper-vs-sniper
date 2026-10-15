using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The camera's film look: ACES tonemapping, soft bloom on the sun and water glints, a touch of
    /// contrast and a light vignette. One global volume; the scope view draws its own vignette and
    /// tube on top (ScopeMask), the glass adds a little contrast (Scope) and goes soft while the side
    /// focus is off the range (Defocus). The X-ray kill cam drains and darkens the frame (Xray) and
    /// flashes it white at the impact (Flash).
    /// </summary>
    public static class PostFx
    {
        static ColorAdjustments? grade;
        static Vignette? vignette;
        static DepthOfField? dof;
        static float scope = -1, xray, flash, defocus;

        public static Volume Setup(Camera cam, Transform parent)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            var go = new GameObject("PostFx");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.65f);
            // A centimetre-sized polished surface up close (the kill cam round) can return a huge
            // specular value; clamp what feeds the bloom so it can never blow out into a white disc.
            bloom.clamp.Override(12f);
            bloom.highQualityFiltering.Override(true);
            grade = profile.Add<ColorAdjustments>(true);
            scope = 0;
            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Off);
            Apply();
            return volume;
        }

        /// <summary>
        /// Blends in the scope's glass: 0 the bare eye, 1 looking through the scope. Good optics
        /// read a little crisper: more contrast and colour, a touch darker for the light lost in the glass.
        /// </summary>
        public static void Scope(float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (Mathf.Abs(amount - scope) < 0.005f) return;
            scope = amount;
            Atmos.ScopeHaze(amount);
            Apply();
        }

        /// <summary>
        /// The scope's side focus set wrong, 0 sharp to 1 fully soft: a gaussian depth of field
        /// that softens the far picture until the focus knob matches the range.
        /// </summary>
        public static void Defocus(float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (Mathf.Abs(amount - defocus) < 0.01f && (amount > 0 || defocus == 0)) return;
            defocus = amount;
            Apply();
        }

        /// <summary>The X-ray look, 0 to 1: harder contrast, colour drained, a heavier vignette.</summary>
        public static void Xray(float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (Mathf.Abs(amount - xray) < 0.005f) return;
            xray = amount;
            Apply();
        }

        /// <summary>A white-out over the frame, 0 to 1, for the freeze at the moment of impact.</summary>
        public static void Flash(float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (Mathf.Abs(amount - flash) < 0.005f && (amount > 0 || flash == 0)) return;
            flash = amount;
            Apply();
        }

        static void Apply()
        {
            if (grade == null) return;
            float sc = Mathf.Max(0, scope);
            grade.postExposure.Override(Mathf.Lerp(0.15f, 0.05f, sc) - 0.25f * xray + 1.1f * flash * flash);
            grade.contrast.Override(Mathf.Lerp(12f, 18f, sc) + 14f * xray);
            grade.saturation.Override(Mathf.Lerp(-6f, 8f, sc) - 35f * xray);
            if (vignette != null) vignette.intensity.Override(0.22f + 0.2f * xray);
            if (dof != null)
            {
                if (defocus < 0.03f) dof.mode.Override(DepthOfFieldMode.Off);
                else
                {
                    // Everything from well short of the target out goes soft, more as the focus is further off.
                    dof.mode.Override(DepthOfFieldMode.Gaussian);
                    dof.gaussianStart.Override(Mathf.Lerp(300f, 20f, defocus));
                    dof.gaussianEnd.Override(Mathf.Lerp(700f, 120f, defocus));
                    dof.gaussianMaxRadius.Override(Mathf.Lerp(0.6f, 1.5f, defocus));
                    dof.highQualitySampling.Override(true);
                }
            }
        }
    }
}
