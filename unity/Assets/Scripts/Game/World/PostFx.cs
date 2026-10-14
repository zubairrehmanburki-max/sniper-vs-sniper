using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The camera's film look: ACES tonemapping, soft bloom on the sun and water glints, a touch of
    /// contrast and a light vignette. One global volume; the scope view draws its own vignette,
    /// fringing and lens tint on top (ScopeMask), and the glass adds a little contrast (Scope).
    /// </summary>
    public static class PostFx
    {
        static ColorAdjustments? grade;
        static float graded = -1;

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
            bloom.highQualityFiltering.Override(true);
            grade = profile.Add<ColorAdjustments>(true);
            Scope(0);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            return volume;
        }

        /// <summary>
        /// Blends in the scope's glass: 0 the bare eye, 1 looking through the scope. Good optics
        /// read a little crisper: more contrast and colour, a touch darker for the light lost in the glass.
        /// </summary>
        public static void Scope(float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (grade == null || Mathf.Abs(amount - graded) < 0.005f) return;
            graded = amount;
            grade.postExposure.Override(Mathf.Lerp(0.15f, 0.05f, amount));
            grade.contrast.Override(Mathf.Lerp(12f, 18f, amount));
            grade.saturation.Override(Mathf.Lerp(-6f, 0f, amount));
        }
    }
}
