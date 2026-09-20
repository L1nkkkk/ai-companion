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
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Companion.Foundation
{
    // A rendering fixture only. U01-01 owns the eventual AvatarPresenter.
    public sealed class FoundationBootstrap : MonoBehaviour
    {
        public GameObject ModelPrefab;
        public Font LabelFont;
        public AnimationClip IdleClip;

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
            public double elapsedSeconds;
            public float p95FrameMs;
            public float maxFrameMs;
            public string scope = "U01-00 rendering fixture; no cloud, chat, recording or playback";
        }

        private void Awake()
        {
            Application.logMessageReceived += RecordLog;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            started = Time.realtimeSinceStartupAsDouble;
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
            FitCamera();
            CreateLabel();
            var motion = model.GetComponent<CubismMotionController>();
            if (motion == null) motion = model.gameObject.AddComponent<CubismMotionController>();
            yield return null;
            if (IdleClip == null) throw new InvalidOperationException("The fixture idle clip is missing.");
            motion.PlayAnimation(IdleClip, isLoop: true);
            result.idleClipPlaying = motion.IsPlayingAnimation();
            if (!result.idleClipPlaying) Debug.LogError("The SDK idle animation did not start.");

            result.unity = Application.unityVersion;
            result.core = "0x" + CubismCoreDll.GetVersion().ToString("X8");
            result.graphicsDevice = SystemInfo.graphicsDeviceName;
            result.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            result.renderPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.name;
            result.width = Screen.width;
            result.height = Screen.height;
            result.drawables = model.Drawables.Length;
            foreach (var drawable in model.Drawables)
            {
                if (drawable.IsMasked) result.maskedDrawables++;
                if (drawable.IsInverted) result.invertedMasks++;
                var renderer = drawable.GetComponent<CubismRenderer>();
                var material = renderer == null ? null : renderer.DrawMaterial ?? renderer.Material;
                if (material == null || material.shader == null || !material.shader.isSupported)
                    result.unsupportedMaterials++;
            }
            Debug.Log("U01-00 model initialized: " + JsonUtility.ToJson(result));
            yield return new WaitForSecondsRealtime(3);
            yield return Capture("player-03s.png");
            yield return new WaitForSecondsRealtime(7);
            yield return Capture("player-10s.png");
        }

        private void FitCamera()
        {
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            foreach (var drawable in model.Drawables)
                foreach (var vertex in drawable.VertexPositions)
                    bounds.Encapsulate(drawable.transform.TransformPoint(vertex));
            view.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10);
            view.orthographicSize = Mathf.Max(bounds.extents.y * 1.20f,
                bounds.extents.x / view.aspect * 1.20f, 0.5f);
        }

        private void CreateLabel()
        {
            var canvas = new GameObject("Foundation Status", typeof(Canvas), typeof(CanvasScaler));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);
            var label = new GameObject("Scope Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(canvas.transform, false);
            var text = label.GetComponent<Text>();
            text.font = LabelFont;
            text.fontSize = 22;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.text = "U01-00 · 渲染验证 / 演示模型 Mao\nUnity 6.3 + Cubism R5 / URP\nEsc 退出 · 本场景无聊天、录音或云调用";
            var rect = label.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(24, -20);
            rect.sizeDelta = new Vector2(-48, 110);
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
            if (Time.realtimeSinceStartupAsDouble - started > 3 && frameTimes.Count < 120000)
                frameTimes.Add(Time.unscaledDeltaTime * 1000);
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Application.Quit();
            if (!finished && duration > 0 && Time.realtimeSinceStartupAsDouble - started >= duration)
            {
                finished = true;
                SaveResult();
                Application.Quit(result.errors == 0 && result.unsupportedMaterials == 0 &&
                    result.drawables > 0 ? 0 : 1);
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
                writer.WriteLine("frame,unscaled_delta_ms");
                for (int i = 0; i < frameTimes.Count; i++)
                    writer.WriteLine(i + "," + frameTimes[i].ToString("F4", CultureInfo.InvariantCulture));
            }
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

        private void OnDestroy() => Application.logMessageReceived -= RecordLog;

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
