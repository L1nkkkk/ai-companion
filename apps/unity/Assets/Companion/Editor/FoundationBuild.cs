using System;
using System.IO;
using Companion.Foundation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Companion.Foundation.Editor
{
    public static class FoundationBuild
    {
        public const string UnityVersion = "6000.3.11f1";
        public const string ScenePath = "Assets/Companion/Scenes/Foundation.unity";
        private const string PipelinePath = "Assets/Companion/Settings/FoundationURP.asset";
        private const string ModelPath = "Assets/Live2D/Cubism/Samples/Models/Mao/Mao.prefab";
        private const string FontPath = "Assets/ThirdParty/Fonts/NotoSansCJKsc-Regular.otf";

        [MenuItem("Companion/U01-00/Prepare foundation scene")]
        public static void Prepare()
        {
            RequireVersion();
            EditorSettings.serializationMode = SerializationMode.ForceText;
            UnityEditor.VersionControlSettings.mode = "Visible Meta Files";
            PlayerSettings.companyName = "AICompanion";
            PlayerSettings.productName = "AI Companion U01-00";
            PlayerSettings.bundleVersion = "0.0.1";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11 });
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/ProjectSettings.asset")[0]);
            settings.FindProperty("activeInputHandler").intValue = 1;
            settings.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory("Assets/Companion/Settings");
            Directory.CreateDirectory("Assets/Companion/Scenes");
            AssetDatabase.Refresh();
            var renderer = RequiredAsset<UniversalRendererData>(
                "Assets/Live2D/Cubism/Rendering/URP/CubismURPRenderer.asset");
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            pipeline.supportsHDR = false;
            pipeline.msaaSampleCount = 1;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.vSyncCount = 1;
            EditorUtility.SetDirty(pipeline);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bootstrap = new GameObject("U01-00 Rendering Fixture").AddComponent<FoundationBootstrap>();
            bootstrap.ModelPrefab = RequiredAsset<GameObject>(ModelPath);
            bootstrap.LabelFont = RequiredAsset<Font>(FontPath);
            bootstrap.IdleClip = RequiredAsset<AnimationClip>(
                "Assets/Live2D/Cubism/Samples/Models/Mao/motions/mtn_01.anim");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("U01-00_PREPARE_SUCCEEDED " + Application.unityVersion);
        }

        [MenuItem("Companion/U01-00/Build Windows x64")]
        public static void BuildWindows()
        {
            RequireVersion();
            if (!File.Exists(ScenePath))
                throw new BuildFailedException("Run Prepare in a separate Unity invocation first.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64))
                throw new BuildFailedException("Windows Standalone build support is missing.");
            RequiredAsset<GameObject>(ModelPath);
            RequiredAsset<Font>(FontPath);
            var pipeline = RequiredAsset<UniversalRenderPipelineAsset>(PipelinePath);
            if (GraphicsSettings.defaultRenderPipeline != pipeline || pipeline.supportsHDR)
                throw new BuildFailedException("Expected Foundation URP pipeline with HDR disabled.");
            var output = Environment.GetEnvironmentVariable("COMPANION_BUILD_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("Builds/Windows-x64");
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = Path.Combine(output, "AICompanion.Foundation.exe"),
                options = BuildOptions.Development | BuildOptions.StrictMode
            });
            Debug.Log($"U01-00_BUILD_RESULT {report.summary.result}, errors={report.summary.totalErrors}, " +
                $"warnings={report.summary.totalWarnings}, bytes={report.summary.totalSize}");
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Windows build failed; see full Unity log.");
        }

        private static T RequiredAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new BuildFailedException("Missing asset: " + path);
            return asset;
        }

        private static void RequireVersion()
        {
            if (Application.unityVersion != UnityVersion)
                throw new BuildFailedException($"Expected Unity {UnityVersion}; got {Application.unityVersion}.");
        }
    }
}
