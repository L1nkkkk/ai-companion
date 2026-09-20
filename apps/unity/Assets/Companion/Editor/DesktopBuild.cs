using System;
using System.IO;
using AICompanion.Preview.Composition;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Companion.Foundation.Editor
{
    public static class DesktopBuild
    {
        public const string ScenePath = "Assets/Companion/Scenes/Desktop.unity";
        private const string FontAssetPath = "Assets/Companion/Settings/NotoDesktop.asset";

        public static void Prepare()
        {
            RequireVersion();
            if (Shader.Find("TextMeshPro/Distance Field") == null) throw new BuildFailedException("Import TMP Essential Resources first.");
            var font = Required<Font>("Assets/ThirdParty/Fonts/NotoSansCJKsc-Regular.otf");
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (asset == null)
            {
                asset = TMP_FontAsset.CreateFontAsset(font, 40, 5, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                asset.name = "NotoDesktop";
                AssetDatabase.CreateAsset(asset, FontAssetPath);
                AssetDatabase.AddObjectToAsset(asset.material, asset);
                foreach (var texture in asset.atlasTextures) AssetDatabase.AddObjectToAsset(texture, asset);
            }
            // Register subassets before serializing their references (CreateAsset serializes
            // transient references as null). Also repairs an interrupted preparation safely.
            foreach (var subasset in AssetDatabase.LoadAllAssetsAtPath(FontAssetPath))
            {
                if (subasset is Material material) asset.material = material;
                if (subasset is Texture2D texture) asset.atlasTextures = new[] { texture };
            }
            if (asset.material == null || asset.atlasTextures.Length != 1 || asset.atlasTextures[0] == null)
                throw new BuildFailedException("Noto font subassets are incomplete.");
            asset.material.mainTexture = asset.atlasTextures[0];
            EditorUtility.SetDirty(asset.material);
            EditorUtility.SetDirty(asset);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bootstrap = new GameObject("Desktop Companion").AddComponent<DesktopBootstrap>();
            bootstrap.ModelPrefab = Required<GameObject>("Assets/Live2D/Cubism/Samples/Models/Mao/Mao.prefab");
            bootstrap.UiFont = asset;
            bootstrap.IdleClip = Required<AnimationClip>("Assets/Live2D/Cubism/Samples/Models/Mao/motions/mtn_01.anim");
            bootstrap.GreetingClip = Required<AnimationClip>("Assets/Live2D/Cubism/Samples/Models/Mao/motions/mtn_02.anim");
            EditorSceneManager.SaveScene(scene, ScenePath);
            PlayerSettings.productName = "Neuro Saki Desktop Preview";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("U01_DESKTOP_PREPARED");
        }

        public static void BuildWindows()
        {
            RequireVersion();
            if (!File.Exists(ScenePath)) throw new BuildFailedException("Prepare and review Desktop scene first.");
            string output = Environment.GetEnvironmentVariable("COMPANION_BUILD_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new BuildFailedException("Missing build output directory.");
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, target = BuildTarget.StandaloneWindows64,
                locationPathName = Path.Combine(output, "NeuroSaki.exe"), options = BuildOptions.Development | BuildOptions.StrictMode
            });
            File.WriteAllText(Path.Combine(output, "unity-build-result.json"), JsonUtility.ToJson(new Result {
                unity = Application.unityVersion, result = report.summary.result.ToString(),
                errors = report.summary.totalErrors, warnings = report.summary.totalWarnings,
                bytes = report.summary.totalSize, seconds = report.summary.totalTime.TotalSeconds
            }, true));
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Desktop build failed.");
            Debug.Log("U01_DESKTOP_BUILD_SUCCEEDED");
        }

        public static void CheckModules()
        {
            foreach (var typeName in new[] {
                "AICompanion.Preview.Tests.HistoryChecks, Companion.History.Checks",
                "AICompanion.Preview.Tests.UiChecks, Companion.UI.Checks",
                "AICompanion.Preview.Tests.SessionAudioChecks, Companion.SessionAudio.Tests"
            })
            {
                var type = Type.GetType(typeName, true);
                type.GetMethod("Run").Invoke(null, null);
            }
            Debug.Log("U01_DESKTOP_CHECKS_PASSED");
        }

        [Serializable] private sealed class Result { public string unity; public string result; public int errors; public int warnings; public ulong bytes; public double seconds; }
        private static T Required<T>(string path) where T : UnityEngine.Object
        { return AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new BuildFailedException("Missing resource: " + path); }
        private static void RequireVersion()
        { if (Application.unityVersion != FoundationBuild.UnityVersion) throw new BuildFailedException("Use the frozen Unity 2022.3.62f3c1 Editor."); }
    }
}
