using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The camera's film look: ACES tonemapping, soft bloom on the sun and water glints, a per-map
    /// colour grade (Grade), fine film grain, a light vignette. One global volume; the scope view draws its own vignette and
    /// tube on top (ScopeMask), the glass adds a little contrast (Scope) and goes soft while the side
    /// focus is off the range (Defocus). The X-ray kill cam drains and darkens the frame (Xray) and
    /// flashes it white at the impact (Flash).
    /// </summary>
    public static class PostFx
    {
        static ColorAdjustments? grade;
        static WhiteBalance? balance;
        static SplitToning? toning;
        static ShadowsMidtonesHighlights? smh;
        static Vignette? vignette;
        static Look look = new Look();
        static DepthOfField? dof;
        static float scope = -1, xray, flash, defocus;
        static Color glass = Color.white;
        static float glassExposure;

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
            balance = profile.Add<WhiteBalance>(true);
            toning = profile.Add<SplitToning>(true);
            smh = profile.Add<ShadowsMidtonesHighlights>(true);
            // A fine film grain and a hair of lens fringing at the frame edge: the camera, not a render.
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.14f);
            grain.response.Override(0.85f);
            var fringe = profile.Add<ChromaticAberration>(true);
            fringe.intensity.Override(0.06f);
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
        /// A map's colour grade: white balance, split toning (cool shade, warm light, the way film and
        /// AAA grades separate them), and the bare-eye contrast and saturation. The HDR sky photos are
        /// bright and very blue; the grade pulls the sky back and warms the ground so the frame reads
        /// as one place in one light instead of a cut-out under a postcard sky.
        /// </summary>
        public sealed class Look
        {
            public float Temperature, Tint, Contrast = 12, Saturation = -6, Exposure = 0.15f;
            public Color Shadows = new Color(0.5f, 0.5f, 0.5f), Highlights = new Color(0.5f, 0.5f, 0.5f);
            public float Balance;
            /// <summary>Lift for the darkest tones (negative deepens them) and the gain on the brightest.</summary>
            public float Lift, Gain;
        }

        static readonly Color Teal = new Color(0.42f, 0.53f, 0.58f), Steel = new Color(0.45f, 0.5f, 0.56f);
        static readonly Color Amber = new Color(0.62f, 0.53f, 0.42f), Straw = new Color(0.58f, 0.54f, 0.45f);

        static readonly System.Collections.Generic.Dictionary<string, Look> Looks = new System.Collections.Generic.Dictionary<string, Look>
        {
            ["default"] = new Look { Temperature = 4, Contrast = 16, Saturation = -8, Shadows = Teal, Highlights = Straw },
            // River city at midday: warm stone and asphalt, the sky's blue held back.
            ["twin-bridges"] = new Look { Temperature = 7, Tint = 2, Contrast = 20, Saturation = -10, Shadows = Teal, Highlights = Amber, Balance = -10, Lift = -0.04f, Gain = 0.03f },
            ["harbor-rooftops"] = new Look { Temperature = 6, Tint = 3, Contrast = 20, Saturation = -12, Shadows = Teal, Highlights = Amber, Balance = -5, Lift = -0.04f, Gain = 0.02f },
            // Desert: hot, bleached light and deep shade.
            ["dry-valley"] = new Look { Temperature = 12, Tint = 4, Contrast = 22, Saturation = -6, Shadows = Steel, Highlights = Amber, Balance = 10, Lift = -0.05f, Gain = 0.02f, Exposure = 0.08f },
            // Thin mountain air: cool and crisp.
            ["alpine-ridge"] = new Look { Temperature = -6, Contrast = 18, Saturation = -6, Shadows = Steel, Highlights = Straw, Balance = -15, Lift = -0.03f },
            // Marsh under a hazy sky: soft, muted, a little green.
            ["watchtowers"] = new Look { Temperature = 3, Tint = -3, Contrast = 16, Saturation = -14, Shadows = Teal, Highlights = Straw, Balance = -5, Lift = -0.03f },
            // Open sea: cold steel water.
            ["oil-rigs"] = new Look { Temperature = -8, Contrast = 18, Saturation = -14, Shadows = Steel, Highlights = Straw, Balance = -20, Lift = -0.04f },
            ["training-lane"] = new Look { Temperature = 6, Contrast = 16, Saturation = -8, Shadows = Teal, Highlights = Amber, Lift = -0.03f },
        };

        /// <summary>Sets the colour grade for the map now on screen.</summary>
        public static void Grade(string mapId)
        {
            look = Looks.TryGetValue(mapId, out var l) ? l : Looks["default"];
            if (balance != null)
            {
                balance.temperature.Override(look.Temperature);
                balance.tint.Override(look.Tint);
            }
            if (toning != null)
            {
                toning.shadows.Override(look.Shadows);
                toning.highlights.Override(look.Highlights);
                toning.balance.Override(look.Balance);
            }
            if (smh != null)
            {
                smh.shadows.Override(new Vector4(1, 1, 1, look.Lift));
                smh.highlights.Override(new Vector4(1, 1, 1, look.Gain));
            }
            Apply();
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

        /// <summary>The colour and light loss of the fitted scope's glass (ScopeStyle), seen while scoped.</summary>
        public static void Glass(Color colour, float exposure)
        {
            glass = colour;
            glassExposure = exposure;
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
            grade.postExposure.Override(Mathf.Lerp(look.Exposure, 0.05f + glassExposure, sc) - 0.25f * xray + 1.1f * flash * flash);
            grade.colorFilter.Override(Color.Lerp(Color.white, glass, sc));
            grade.contrast.Override(Mathf.Lerp(look.Contrast, look.Contrast + 6, sc) + 14f * xray);
            grade.saturation.Override(Mathf.Lerp(look.Saturation, look.Saturation + 14, sc) - 35f * xray);
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
