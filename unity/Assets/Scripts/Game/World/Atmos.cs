using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SniperDuel.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Sky, sun, ambient light, haze and reflections. The sky is a real HDR photo; the sun light is
    /// lined up with the sun in that photo and the photo is turned so the sun sits where the map
    /// wants it (Palette.SunAzimuthDeg, clockwise from the enemy's direction).
    /// </summary>
    public static class Atmos
    {
        public sealed class Sky
        {
            public string Id = "";
            public Light Sun = null!;
            public Color FogColor;
            public ReflectionProbe Probe = null!;
        }

        static readonly Dictionary<SkyKind, string[]> Photos = new Dictionary<SkyKind, string[]>
        {
            [SkyKind.Sunrise] = new[] { "belfast_sunset_puresky", "qwantani_sunrise_puresky" },
            [SkyKind.Sunset] = new[] { "belfast_sunset_puresky", "qwantani_sunrise_puresky" },
            [SkyKind.Clear] = new[] { "kloofendal_48d_partly_cloudy_puresky", "kloofendal_misty_morning_puresky" },
        };

        static float hazeDensity;

        /// <summary>
        /// Good glass cuts through the haze: while scoped (0 bare eye, 1 through the scope) the air
        /// thins so the far bridge and the hills behind it keep their colour and edges.
        /// </summary>
        public static void ScopeHaze(float scope)
        {
            if (hazeDensity <= 0) return;
            RenderSettings.fogDensity = hazeDensity * Mathf.Lerp(1, 0.5f, Mathf.Clamp01(scope));
        }

        public static Sky Build(DuelMap map, Transform parent)
        {
            var palette = map.Look.Palette ?? new Palette();
            var kind = palette.Sky ?? SkyKind.Clear;
            Material? sky = null;
            string id = "";
            foreach (var candidate in Photos[kind])
            {
                sky = Resources.Load<Material>("Materials/Sky-" + candidate);
                if (sky == null) continue;
                id = candidate;
                break;
            }

            // Where the sun is in the photo (found at build time), as azimuth/elevation.
            float u = 0.5f, v = kind == SkyKind.Clear ? 0.8f : 0.55f;
            var suns = Resources.Load<TextAsset>("Generated/sky-suns");
            if (suns != null && id.Length > 0)
            {
                try
                {
                    var j = JObject.Parse(suns.text)[id];
                    if (j != null) { u = (float)j["u"]!; v = (float)j["v"]!; }
                }
                catch (Exception e) { Debug.LogWarning("[Atmos] sky-suns.json: " + e.Message); }
            }
            // Skybox/Panoramic: u = 0.5 − atan2(z, x)/2π, v = 1 − acos(y)/π. With _Rotation a (deg), a world
            // direction at angle φ (atan2(z, x)) samples the photo at φ + a.
            float phiSky = (0.5f - u) * 2 * Mathf.PI;
            float elevation = Mathf.Clamp((v - 0.5f) * Mathf.PI, 4 * Mathf.Deg2Rad, 70 * Mathf.Deg2Rad);
            float az = (float)(palette.SunAzimuthDeg ?? 140) * Mathf.Deg2Rad;
            var horiz = new Vector3(Mathf.Sin(az), 0, Mathf.Cos(az));
            float phiWorld = Mathf.Atan2(horiz.z, horiz.x);
            if (sky != null)
            {
                sky = new Material(sky);
                sky.SetFloat("_Rotation", Mathf.Repeat((phiSky - phiWorld) * Mathf.Rad2Deg, 360));
                RenderSettings.skybox = sky;
            }

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(parent, false);
            var toSun = (horiz * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)).normalized;
            sunGo.transform.rotation = Quaternion.LookRotation(-toSun);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            // Low sun: warm and a little weaker; high sun: near white.
            float warm = Mathf.InverseLerp(30 * Mathf.Deg2Rad, 5 * Mathf.Deg2Rad, elevation);
            sun.color = Color.Lerp(new Color(1f, 0.96f, 0.9f), new Color(1f, 0.72f, 0.45f), warm);
            sun.intensity = Mathf.Lerp(2.2f, 1.6f, warm);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.3f;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();

            // Haze: the far bank should go soft and blue-grey, the near water stay crisp.
            var fog = Color.Lerp(new Color(0.62f, 0.68f, 0.74f), new Color(0.8f, 0.68f, 0.6f), warm * 0.35f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // Thin enough that the city a kilometre or two away reads as hazy silhouettes, not a wall.
            // Thin mountain air is clearer: far ranges stay distinct layers, each hazier than the one in
            // front, instead of melting into one pale wall.
            float thin = Mathf.Lerp(1, 0.62f, Mathf.InverseLerp(1500, 2400, (float)map.Climate.AltitudeM));
            hazeDensity = (kind == SkyKind.Clear ? 0.00045f : 0.0006f) * thin;
            RenderSettings.fogDensity = hazeDensity;
            RenderSettings.fogColor = fog;

            // One realtime probe over the lane so water and metal reflect the bridges and the sky.
            var probeGo = new GameObject("Reflections");
            probeGo.transform.SetParent(parent, false);
            probeGo.transform.position = new Vector3(0, 12, (float)map.LaneLength / 2);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(6000, 800, 6000);
            probe.resolution = 256;
            probe.hdr = true;
            probe.farClipPlane = 3000;
            probe.importance = 1;

            return new Sky { Id = id, Sun = sun, FogColor = fog, Probe = probe };
        }
    }
}
