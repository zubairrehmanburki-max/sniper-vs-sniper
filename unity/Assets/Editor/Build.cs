using System;
using System.IO;
using System.Reflection;
using SniperDuel.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SniperDuel.EditorTools
{
    /// <summary>
    /// Batch-mode entry points for CI (game-ci/unity-builder `buildMethod`). The project keeps almost
    /// nothing as hand-made assets: this script sets the player up, creates the URP pipeline asset
    /// and the single boot scene, then builds. Everything else is built by code at run time.
    /// </summary>
    public static class Build
    {
        const string BundleId = "com.snipervssniper.game";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string SettingsDir = "Assets/Settings";

        public static void IOS() => Run(BuildTarget.iOS, BuildTargetGroup.iOS, "build/iOS/SniperDuel");

        public static void Linux()
        {
            // The Linux build only exists for CI screenshots on a software renderer (llvmpipe), where
            // GPU skinning draws nothing; skin on the CPU there. Phones keep GPU skinning.
            PlayerSettings.meshDeformation = MeshDeformation.CPU;
            Run(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, "build/StandaloneLinux64/SniperDuel");
        }

        /// <summary>Menu item for local use: set the project up without building.</summary>
        [MenuItem("Sniper Duel/Set up project")]
        public static void Setup()
        {
            ConfigurePlayer();
            EnsurePipeline();
            WriteBuildInfo();
            Prepare.All();
            EnsureScene();
            AssetDatabase.SaveAssets();
        }

        static void Run(BuildTarget target, BuildTargetGroup group, string fallbackPath)
        {
            try
            {
                Setup();
                string path = Arg("-customBuildPath") ?? fallbackPath;
                Debug.Log($"[Build] {target} -> {path}");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = path,
                    target = target,
                    targetGroup = group,
                    options = BuildOptions.None,
                });
                var summary = report.summary;
                Debug.Log($"[Build] {summary.result}: {summary.totalErrors} errors, {summary.totalSize / 1e6:0.0} MB, {summary.totalTime}");
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Puts the build number (Title and Settings show it, to match TestFlight) and a plain-text copy
        /// of the third-party credits (Settings › Credits) where the game can load them.
        /// </summary>
        static void WriteBuildInfo()
        {
            const string info = "Assets/Resources/BuildInfo.txt";
            File.WriteAllText(info, Environment.GetEnvironmentVariable("BUILD_NUMBER") ?? "dev");
            AssetDatabase.ImportAsset(info);
            const string credits = "Assets/Resources/TP/CREDITS.md";
            if (File.Exists(credits))
            {
                File.Copy(credits, "Assets/Resources/TP/CREDITS.txt", true);
                AssetDatabase.ImportAsset("Assets/Resources/TP/CREDITS.txt");
            }
            else Debug.LogWarning("[Build] TP/CREDITS.md missing: Settings › Credits falls back to the short list");
        }

        static string? Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Sniper vs Sniper";
            PlayerSettings.productName = "Sniper vs Sniper";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            // App Store Connect rejects uploads without a 1024 px icon; one square icon fills every size.
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/AppIcon.png");
            if (icon != null)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
                foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.iOS))
                {
                    var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind);
                    foreach (var pi in icons)
                        for (int layer = 0; layer < pi.maxLayerCount; layer++) pi.SetTexture(icon, layer);
                    PlayerSettings.SetPlatformIcons(NamedBuildTarget.iOS, kind, icons);
                }
            }
            else Debug.LogWarning("[Build] Assets/Art/AppIcon.png missing: the iOS upload will be rejected");
            // Version 2.x is the Unity game; build numbers come from the workflow run.
            PlayerSettings.bundleVersion = "2.0";
            PlayerSettings.iOS.buildNumber = Environment.GetEnvironmentVariable("BUILD_NUMBER") ?? "1";
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.requiresFullScreen = true;
            // The home bar stays (dimmed): hiding it makes iOS ignore the gesture deferral below, and a
            // swipe on the zoom ring near the bottom edge then sent the game to the home screen.
            PlayerSettings.iOS.hideHomeButton = false;
            // A thumb sliding off the fire button or a scope knob toward the edge mustn't pull up the
            // home bar or Control Centre: the first swipe at the top and bottom edges goes to the game.
            PlayerSettings.iOS.deferSystemGesturesMode = UnityEngine.iOS.SystemGestureDeferMode.TopEdge | UnityEngine.iOS.SystemGestureDeferMode.BottomEdge;
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.OpenGLCore });

            PlayerSettings.defaultScreenWidth = 1864;
            PlayerSettings.defaultScreenHeight = 860;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            // iOS audio session "Ambient": the game's sound obeys the ring/silent switch (as Apple
            // asks of games) and mixes with the player's own music instead of stopping it.
            PlayerSettings.muteOtherAudioSources = false;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
        }

        /// <summary>URP with HDR, MSAA, soft shadows and post-processing, assigned to every quality level.</summary>
        static void EnsurePipeline()
        {
            Directory.CreateDirectory(SettingsDir);
            string rendererPath = SettingsDir + "/Renderer.asset";
            string pipelinePath = SettingsDir + "/Pipeline.asset";

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                // Post-processing resources: assigned by the editor's own "create renderer" path, which
                // isn't public, so look the default up by reflection on whichever URP version is present.
                var getDefault = typeof(PostProcessData).GetMethod("GetDefaultPostProcessData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var ppField = typeof(UniversalRendererData).GetField("postProcessData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getDefault != null && ppField != null) ppField.SetValue(renderer, getDefault.Invoke(null, null));
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            AddAmbientOcclusion(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            // Starting values; Settings.Apply sets anti-aliasing, scale and shadows from the
            // player's options (defaults by phone class) before the first frame.
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 2;
            pipeline.renderScale = 1f;
            pipeline.shadowDistance = 220f;
            pipeline.shadowCascadeCount = 2;
            // Depth for SSAO and the scope's depth of field. No shader reads the opaque colour
            // copy, so it isn't made (it cost a full-screen copy every frame).
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Screen-space ambient occlusion: the soft contact shading in corners, under the girders,
        /// round barrier feet and between buildings that makes the concrete and steel read as solid.
        /// URP keeps the feature class internal, so it is created and set up by reflection; the
        /// falloff reaches past the far bridge so the scope view keeps it too.
        /// </summary>
        static void AddAmbientOcclusion(UniversalRendererData renderer)
        {
            var type = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            if (type == null) { Debug.LogWarning("[Build] no SSAO feature in this URP"); return; }
            ScriptableRendererFeature? existing = null;
            foreach (var f in renderer.rendererFeatures) if (f != null && f.GetType() == type) existing = f;
            var feature = existing ?? (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = "SSAO";
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var settingsField = type.GetField("m_Settings", Any);
            var settings = settingsField?.GetValue(feature);
            if (settings != null)
            {
                void Set(string name, object value)
                {
                    var f = settings.GetType().GetField(name, Any);
                    if (f == null) { Debug.LogWarning($"[Build] SSAO setting {name} not found"); return; }
                    f.SetValue(settings, f.FieldType.IsEnum ? Enum.ToObject(f.FieldType, value) : Convert.ChangeType(value, f.FieldType));
                }
                Set("Intensity", 1.6f);
                Set("Radius", 0.45f);
                Set("Falloff", 900f);
                Set("Downsample", true);
                // Normals rebuilt from depth rather than a second pass over the whole scene for a
                // normals texture: millions of triangles fewer each frame, for a slightly softer look.
                Set("Source", 0);
                Set("NormalSamples", 1);
                settingsField!.SetValue(feature, settings);
                var names = new System.Collections.Generic.List<string>();
                foreach (var f in settings.GetType().GetFields(Any)) names.Add($"{f.Name}={f.GetValue(settings)}");
                Debug.Log("[Build] SSAO " + string.Join(", ", names));
            }
            if (existing != null)
            {
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return;
            }
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
            // The renderer keeps a parallel list of the features' local file ids; keep it in step.
            var map = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", Any)?.GetValue(renderer) as System.Collections.Generic.List<long>;
            if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long id)) map.Add(id);
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
        }

        static void EnsureScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Boot").AddComponent<Boot>();
            // Unity strips fog shader variants that no scene in the build uses; the maps turn fog
            // on at runtime, so the boot scene declares it to keep the exponential-squared variants.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0006f;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }

    /// <summary>Import settings for the downloaded third-party assets (Assets/Resources/TP).</summary>
    public class ThirdPartyImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/TP/") && !assetPath.Contains("/Resources/Generated/")) return;
            var ti = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            if (assetPath.Contains("/Hdris/"))
            {
                ti.maxTextureSize = 8192;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.mipmapEnabled = true;
                ti.wrapModeU = TextureWrapMode.Repeat;
                ti.wrapModeV = TextureWrapMode.Clamp;
                return;
            }
            ti.maxTextureSize = 2048;
            if (name.Contains("_nor_gl") || name.Contains("_normal")) ti.textureType = TextureImporterType.NormalMap;
            if (name.Contains("_arm") || name.Contains("_rough") || name.Contains("_ao") || name.Contains("_metal") || name.Contains("_mask")) ti.sRGBTexture = false;
            if (assetPath.Contains("/Textures/") || assetPath.Contains("/Generated/")) ti.wrapMode = TextureWrapMode.Repeat;
            ti.anisoLevel = 8;
        }
    }
}
