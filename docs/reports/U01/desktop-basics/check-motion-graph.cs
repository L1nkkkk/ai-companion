// Temporary Editor-only diagnostic. Stage in an Editor assembly only for this check,
// then remove it. Does not save scenes/assets, change SDK files, or contact the backend.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Live2D.Cubism.Framework.Motion;
using Live2D.Cubism.Framework.MotionFade;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;

namespace Companion.Diagnostics
{
    public static class U01GraphCountCheck
    {
        private static readonly FieldInfo GraphField = typeof(CubismMotionController).GetField("_playableGrap", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FadeStatesField = typeof(CubismFadeController).GetField("_fadeStates", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo EnableMethod = typeof(CubismMotionController).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo DisableMethod = typeof(CubismMotionController).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);

        [Serializable] private sealed class Result
        {
            public string scope = "Actual Unity Editor + unmodified SDK graph allocation. Immediate play calls, not a timed Player or visual fade test. Editor lifecycle methods are explicitly invoked only if edit mode does not dispatch them.";
            public string unity;
            public int iterations = 64;
            public int baselineNodes;
            public List<int> unboundedCounts = new List<int>();
            public int countAfterStopAll;
            public List<int> rebuiltCounts = new List<int>();
            public int oldGraphsInvalidated;
            public int maximumNodesAfterReset;
            public bool fadeArrayReused = true;
            public bool fadeCacheStillCurrent = true;
            public int explicitEditorLifecycleCalls;
            public string motionControllerSha256;
            public string motionLayerSha256;
            public string motionStateSha256;
            public bool passed;
        }

        public static void Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run the diagnostic in Editor edit mode.");
            if (GraphField == null || FadeStatesField == null || EnableMethod == null || DisableMethod == null)
                throw new InvalidOperationException("Frozen SDK inspection fields are unavailable.");
            var output = Environment.GetEnvironmentVariable("U01_GRAPH_CHECK_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Set U01_GRAPH_CHECK_OUTPUT to a new JSON path.");
            output = Path.GetFullPath(output);
            if (File.Exists(output)) throw new IOException("Diagnostic output already exists.");
            var result = new Result { unity = Application.unityVersion };
            result.motionControllerSha256 = Hash("Assets/Live2D/Cubism/Framework/Motion/CubismMotionController.cs");
            result.motionLayerSha256 = Hash("Assets/Live2D/Cubism/Framework/Motion/CubismMotionLayer.cs");
            result.motionStateSha256 = Hash("Assets/Live2D/Cubism/Framework/Motion/CubismMotionState.cs");
            var prefab = Required<GameObject>("Assets/Live2D/Cubism/Samples/Models/Mao/Mao.prefab");
            var idle = Required<AnimationClip>("Assets/Live2D/Cubism/Samples/Models/Mao/motions/mtn_01.anim");
            var greeting = Required<AnimationClip>("Assets/Live2D/Cubism/Samples/Models/Mao/motions/mtn_02.anim");
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            CubismMotionController motion = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.hideFlags = HideFlags.HideAndDontSave;
                motion = instance.GetComponent<CubismMotionController>();
                if (motion == null) motion = instance.AddComponent<CubismMotionController>();
                var fade = instance.GetComponent<CubismFadeController>();
                if (fade == null || fade.CubismFadeMotionList == null) throw new Exception("Mao fade assets missing.");
                if (instance.GetComponent<Animator>().runtimeAnimatorController != null)
                    throw new Exception("Unexpected Animator Controller on frozen Mao.");
                ResetGraph(motion, result);
                fade.Refresh();
                result.baselineNodes = ReadGraph(motion).GetPlayableCount();
                if (result.baselineNodes != 2) throw new Exception("Unexpected one-layer graph baseline.");
                for (int i = 0; i < result.iterations; ++i)
                {
                    motion.PlayAnimation((i & 1) == 0 ? greeting : idle,
                        priority: CubismMotionPriority.PriorityForce, isLoop: (i & 1) != 0);
                    int count = ReadGraph(motion).GetPlayableCount();
                    result.unboundedCounts.Add(count);
                    if (count != result.baselineNodes + 2 * (i + 1))
                        throw new Exception("Unexpected repeated PlayAnimation node behavior.");
                }
                motion.StopAllAnimation();
                result.countAfterStopAll = ReadGraph(motion).GetPlayableCount();
                if (result.countAfterStopAll != result.unboundedCounts[result.unboundedCounts.Count - 1])
                    throw new Exception("StopAll changed the expected node count.");
                for (int i = 0; i < result.iterations; ++i)
                {
                    var oldGraph = ReadGraph(motion);
                    var oldStates = motion.GetFadeStates();
                    ResetGraph(motion, result);
                    if (oldGraph.IsValid()) throw new Exception("Disabled controller retained its old graph.");
                    result.oldGraphsInvalidated++;
                    result.fadeArrayReused &= ReferenceEquals(oldStates, motion.GetFadeStates());
                    result.fadeCacheStillCurrent &= ReferenceEquals(FadeStatesField.GetValue(fade), motion.GetFadeStates());
                    if (ReadGraph(motion).GetPlayableCount() != result.baselineNodes)
                        throw new Exception("Graph did not return to its baseline after reset.");
                    motion.PlayAnimation(greeting, priority: CubismMotionPriority.PriorityForce, isLoop: false);
                    motion.PlayAnimation(idle, priority: CubismMotionPriority.PriorityForce, isLoop: true);
                    int count = ReadGraph(motion).GetPlayableCount();
                    result.rebuiltCounts.Add(count);
                    result.maximumNodesAfterReset = Math.Max(result.maximumNodesAfterReset, count);
                    if (count != result.baselineNodes + 4) throw new Exception("Rebuilt graph exceeded the two-clip bound.");
                    var playing = motion.GetFadeStates()[0].GetPlayingMotions();
                    if (playing.Count != 2 || playing[0].Motion == null || playing[1].Motion == null)
                        throw new Exception("Rebuilt controller did not bind both real fade motions.");
                }
                if (!result.fadeArrayReused || !result.fadeCacheStillCurrent) throw new Exception("Fade cache needs an explicit refresh after rebuilding.");
                result.passed = true;
            }
            finally
            {
                if (motion != null) DisableGraph(motion, result);
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(scene);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, JsonUtility.ToJson(result, true));
            }
            Debug.Log("U01_GRAPH_COUNT_CHECK_PASSED initial=" + result.baselineNodes +
                " unbounded=" + result.countAfterStopAll + " rebuiltMax=" + result.maximumNodesAfterReset);
        }

        private static void ResetGraph(CubismMotionController motion, Result result)
        {
            DisableGraph(motion, result);
            motion.enabled = true;
            if (!ReadGraph(motion).IsValid())
            {
                EnableMethod.Invoke(motion, null);
                result.explicitEditorLifecycleCalls++;
            }
            if (!ReadGraph(motion).IsValid()) throw new Exception("SDK OnEnable did not create a graph.");
        }
        private static void DisableGraph(CubismMotionController motion, Result result)
        {
            motion.enabled = false;
            if (ReadGraph(motion).IsValid())
            {
                DisableMethod.Invoke(motion, null);
                result.explicitEditorLifecycleCalls++;
            }
            if (ReadGraph(motion).IsValid()) throw new Exception("SDK OnDisable did not destroy its graph.");
        }
        private static PlayableGraph ReadGraph(CubismMotionController motion) => (PlayableGraph)GraphField.GetValue(motion);
        private static T Required<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException("Diagnostic asset missing.", path);
        private static string Hash(string path)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(fullPath))).Replace("-", "").ToLowerInvariant();
        }
    }
}
