using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Live2D.Cubism.Core;
using Live2D.Cubism.Core.Unmanaged;
using Live2D.Cubism.Framework.Motion;
using Live2D.Cubism.Rendering;
using UnityEngine;

namespace Companion.Foundation
{
    // A rendering fixture only. U01-01 owns the eventual AvatarPresenter.
    public sealed class FoundationBootstrap : MonoBehaviour
    {
        public GameObject ModelPrefab;
        public Font LabelFont;
        public AnimationClip IdleClip;
        public AnimationClip GreetingClip;
        public AnimationClip MaskStressClip;
        private FoundationBehaviourProbe behaviour;
        private double lastFrame;
        private double nextMemorySample;
        private long baselineWorkingSet;

        private Camera view;
        private CubismModel model;
        private readonly List<float> frameTimes = new List<float>(40000);
        private readonly ProbeResult result = new ProbeResult();
        private string evidenceDirectory;
        private float duration;
        private double started;
        private bool finished;

        [Serializable]
        private sealed class ProbeResult
        {
            public string unity;
            public string core;
            public string model = "Mao";
            public string graphicsDevice;
            public string graphicsApi;
            public string renderPipeline;
            public int width;
            public int height;
            public int drawables;
            public int maskedDrawables;
            public int invertedMasks;
            public int unsupportedMaterials;
            public bool idleClipPlaying;
            public int errors;
            public int warnings;
            public int sampledFrames;
            public bool frameSamplesTruncated;
            public double elapsedSeconds;
            public float p95FrameMs;
            public float maxFrameMs;
            public float framesAtMost33_3MsPercent;
            public int framesAtLeast1000Ms;
            public string memoryMethod = "Windows GetProcessMemoryInfo: OS peak working set, 5-second private/Unity allocation samples, baseline after 30 seconds";
            public long memoryBaselineAt30sBytes;
            public long peakWorkingSetBytes;
            public long finalWorkingSetBytes;
            public long workingSetIncreaseFrom30sBytes;
            public long peakPrivateBytes;
            public long peakUnityAllocatedBytes;
            public bool behaviourProbeFailed;
            public string contractsAssembly;
            public string scope = "U01-00 rendering fixture; direct parameter tests, no audio-driven lip sync, cloud, chat or recording";
        }

        private void Awake()
        {
            Application.logMessageReceived += RecordLog;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            started = Time.realtimeSinceStartupAsDouble;
            lastFrame = started;
            evidenceDirectory = Argument("-evidenceDirectory");
            if (!string.IsNullOrEmpty(evidenceDirectory))
                Directory.CreateDirectory(evidenceDirectory);
            float.TryParse(Argument("-smokeSeconds"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out duration);
            duration = Mathf.Clamp(duration, 0, 1800);
        }

        private IEnumerator Start()
        {
            if (ModelPrefab == null || LabelFont == null)
                throw new InvalidOperationException("Run tools/unity/restore_assets.py and Prepare first.");
            view = new GameObject("Foundation Camera").AddComponent<Camera>();
            view.tag = "MainCamera";
            view.orthographic = true;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(0.08f, 0.11f, 0.16f);
            view.nearClipPlane = 0.01f;
            view.farClipPlane = 100f;
            view.transform.position = new Vector3(0, 0, -10);
            model = Instantiate(ModelPrefab).GetComponent<CubismModel>();
            if (model == null)
                throw new InvalidOperationException("Mao prefab has no CubismModel.");
            yield return null;
            yield return null;
            FitCamera();
            var motion = model.GetComponent<CubismMotionController>();
            if (motion == null) motion = model.gameObject.AddComponent<CubismMotionController>();
            yield return null;
            if (IdleClip == null) throw new InvalidOperationException("The fixture idle clip is missing.");
            motion.PlayAnimation(IdleClip, isLoop: true);
            result.idleClipPlaying = motion.IsPlayingAnimation();
            behaviour = model.gameObject.AddComponent<FoundationBehaviourProbe>();
            behaviour.Initialize(model, motion, IdleClip, GreetingClip, MaskStressClip,
                evidenceDirectory, started);
            result.contractsAssembly = typeof(AICompanion.Preview.Contracts.ISessionController).Assembly.GetName().Name;
            if (!result.idleClipPlaying) Debug.LogError("The SDK idle animation did not start.");

            result.unity = Application.unityVersion;
            result.core = "0x" + CubismCoreDll.GetVersion().ToString("X8");
            result.graphicsDevice = SystemInfo.graphicsDeviceName;
            result.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            result.renderPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null ? "Built-in" : UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name;
            result.width = Screen.width;
            result.height = Screen.height;
            result.drawables = model.Drawables.Length;
            foreach (var drawable in model.Drawables)
            {
                if (drawable.IsMasked) result.maskedDrawables++;
                if (drawable.IsInverted) result.invertedMasks++;
                var renderer = drawable.GetComponent<CubismRenderer>();
                var material = renderer == null ? null : renderer.Material;
                if (material == null || material.shader == null || !material.shader.isSupported)
                    result.unsupportedMaterials++;
            }
            Debug.Log("U01-00_MODEL_INITIALIZED: " + JsonUtility.ToJson(result));
            yield return new WaitForSecondsRealtime(3);
            yield return Capture("player-03s.png");
            yield return new WaitForSecondsRealtime(7);
            yield return Capture("player-10s.png");
        }

        private void FitCamera()
        {
            var bounds = new Bounds();
            bool found = false;
            int visible = 0;
            foreach (var drawable in model.Drawables)
            {
                var renderer = drawable.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled) continue;
                var filter = drawable.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var colors = filter.sharedMesh.colors32;
                bool opaque = colors.Length == 0;
                foreach (var color in colors) if (color.a > 2) { opaque = true; break; }
                if (!opaque) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
                visible++;
            }
            if (!found) throw new InvalidOperationException("No visible drawable bounds.");
            view.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10);
            view.orthographicSize = Mathf.Max(bounds.extents.y * 1.25f,
                bounds.extents.x / view.aspect * 1.25f, 0.5f);
            Debug.Log("U01-00_CAMERA_VISIBLE_BOUNDS count=" + visible + " bounds=" + bounds +
                " orthographicSize=" + view.orthographicSize);
        }

        private GUIStyle labelStyle;
        private void OnGUI()
        {
            if (LabelFont == null) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.font = LabelFont;
                labelStyle.fontSize = 22;
                labelStyle.normal.textColor = Color.white;
            }
            GUI.Label(new Rect(24, 16, 1000, 110),
                "U01-00 渲染验证 / Mao\nUnity 2022.3 + Cubism R4_1 / Built-in\n仅工程与渲染验证 · 无聊天/录音/云调用 · Esc 退出", labelStyle);
        }

        private IEnumerator Capture(string name)
        {
            if (string.IsNullOrEmpty(evidenceDirectory)) yield break;
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(evidenceDirectory, name), texture.EncodeToPNG());
            Destroy(texture);
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - started > 3)
            {
                if (frameTimes.Count < 120000)
                    frameTimes.Add((float)((now - lastFrame) * 1000));
                else if (!result.frameSamplesTruncated)
                {
                    result.frameSamplesTruncated = true;
                    Debug.LogError("Frame evidence capacity exceeded; this run cannot establish full-duration performance.");
                }
            }
            lastFrame = now;
            if (now >= nextMemorySample)
            {
                nextMemorySample = now + 5;
                var memory = FoundationMemoryProbe.Read();
                long working = memory.WorkingSetBytes;
                result.peakWorkingSetBytes = Math.Max(result.peakWorkingSetBytes, memory.PeakWorkingSetBytes);
                result.peakPrivateBytes = Math.Max(result.peakPrivateBytes, memory.PrivateBytes);
                result.peakUnityAllocatedBytes = Math.Max(result.peakUnityAllocatedBytes,
                    UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
                if (baselineWorkingSet == 0 && now - started >= 30) baselineWorkingSet = working;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
                Application.Quit();
            if (!finished && duration > 0 && Time.realtimeSinceStartupAsDouble - started >= duration)
            {
                finished = true;
                if (behaviour != null) behaviour.Finish();
                result.behaviourProbeFailed = behaviour == null || behaviour.HasFailures;
                SaveResult();
                Application.Quit(result.errors == 0 && result.unsupportedMaterials == 0 &&
                    result.drawables > 0 && !result.behaviourProbeFailed ? 0 : 1);
            }
        }

        private void RecordLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                result.errors++;
            if (type == LogType.Warning) result.warnings++;
        }

        private void SaveResult()
        {
            if (string.IsNullOrEmpty(evidenceDirectory)) return;
            result.elapsedSeconds = Time.realtimeSinceStartupAsDouble - started;
            result.sampledFrames = frameTimes.Count;
            using (var writer = new StreamWriter(Path.Combine(evidenceDirectory, "frame-times.csv")))
            {
                writer.WriteLine("frame,monotonic_frame_gap_ms");
                for (int i = 0; i < frameTimes.Count; i++)
                    writer.WriteLine(i + "," + frameTimes[i].ToString("F4", CultureInfo.InvariantCulture));
            }
            int within = 0;
            foreach (float frame in frameTimes)
            {
                if (frame <= 33.3f) within++;
                if (frame >= 1000f) result.framesAtLeast1000Ms++;
            }
            result.framesAtMost33_3MsPercent = frameTimes.Count == 0 ? 0 : 100f * within / frameTimes.Count;
            var finalMemory = FoundationMemoryProbe.Read();
            result.peakWorkingSetBytes = Math.Max(result.peakWorkingSetBytes, finalMemory.PeakWorkingSetBytes);
            result.memoryBaselineAt30sBytes = baselineWorkingSet;
            result.finalWorkingSetBytes = finalMemory.WorkingSetBytes;
            result.workingSetIncreaseFrom30sBytes = baselineWorkingSet == 0 ? 0 : result.finalWorkingSetBytes - baselineWorkingSet;
            frameTimes.Sort();
            if (frameTimes.Count > 0)
            {
                result.p95FrameMs = frameTimes[(int)Math.Ceiling(frameTimes.Count * 0.95) - 1];
                result.maxFrameMs = frameTimes[frameTimes.Count - 1];
            }
            File.WriteAllText(Path.Combine(evidenceDirectory, "player-result.json"),
                JsonUtility.ToJson(result, true));
        }

        private void OnApplicationQuit()
        {
            if (!finished) SaveResult();
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= RecordLog;
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
