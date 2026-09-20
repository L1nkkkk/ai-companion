using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Audio;
using AICompanion.Preview.Avatar;
using AICompanion.Preview.Contracts;
using AICompanion.Preview.History;
using AICompanion.Preview.Session;
using AICompanion.Preview.Transport;
using AICompanion.Preview.UI;
using Live2D.Cubism.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AICompanion.Preview.Composition
{
    public sealed class DesktopBootstrap : MonoBehaviour
    {
        public GameObject ModelPrefab;
        public TMP_FontAsset UiFont;
        public AnimationClip IdleClip;
        public AnimationClip GreetingClip;
        public DesktopSessionController Session { get; private set; }
        public UnityAudioPlayer Player { get; private set; }
        public HttpConversationGateway Gateway { get; private set; }
        public MaoAvatarPresenter Avatar { get; private set; }
        public Camera AvatarCamera { get; private set; }
        public string UserDataDirectory { get; private set; }
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CubismModel model;
        private GameObject modelObject;
        private int lastWidth;
        private int lastHeight;
        private string startupError;
        private bool stopped;
        private bool quitPending;
        private bool quitAllowed;

        [Serializable] private sealed class RuntimeConfig { public string protocol; public string base_url; public string token; }

        private void Awake() { Application.wantsToQuit += WantsToQuit; }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            UserDataDirectory = Argument("-userDataPath") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuroSaki", "desktop");
            Directory.CreateDirectory(UserDataDirectory);
            var backdrop = new GameObject("Window Background").AddComponent<Camera>();
            backdrop.depth = -10;
            backdrop.clearFlags = CameraClearFlags.SolidColor;
            backdrop.backgroundColor = new Color(.075f, .093f, .145f);
            backdrop.cullingMask = 0;
            AvatarCamera = new GameObject("Mao Camera").AddComponent<Camera>();
            AvatarCamera.tag = "MainCamera";
            AvatarCamera.orthographic = true;
            AvatarCamera.clearFlags = CameraClearFlags.SolidColor;
            AvatarCamera.backgroundColor = new Color(.075f, .093f, .145f);
            AvatarCamera.nearClipPlane = .01f;
            AvatarCamera.farClipPlane = 100;
            AvatarCamera.rect = new Rect(0, .245f, .42f, .63f);
            AvatarCamera.transform.position = new Vector3(0, 0, -10);
            AvatarCamera.gameObject.AddComponent<AudioListener>();
            if (ModelPrefab != null)
            {
                modelObject = Instantiate(ModelPrefab);
                model = modelObject.GetComponent<CubismModel>();
                Avatar = modelObject.AddComponent<MaoAvatarPresenter>();
                Avatar.Initialize(AvatarCamera, IdleClip, GreetingClip);
            }
            else startupError = "Mao 资源缺失，请先按资源说明恢复模型。";
            yield return null;
            yield return null;
            FitCamera();
            StartSession();
        }

        private async void StartSession()
        {
            try
            {
                Player = gameObject.AddComponent<UnityAudioPlayer>();
                Player.PlaybackStarted += OnPlaybackStarted;
                Player.PlaybackEnded += OnPlaybackEnded;
                Player.PostVolumeLevel += OnLevel;
                Gateway = new HttpConversationGateway(new Uri("http://127.0.0.1:8000"), ReadToken(), WavValidator.ValidateAsync, ReadToken);
                var history = new JsonHistoryStore(Path.Combine(UserDataDirectory, "history"));
                Session = new DesktopSessionController(Gateway, Player, new UnavailableMicrophoneCapture(), history, Path.Combine(UserDataDirectory, "settings.json"));
                Session.SnapshotChanged += OnSnapshot;
                Session.SpeechExpression += OnExpression;
                var ui = gameObject.AddComponent<DesktopChatView>();
                ui.Initialize(Session, UiFont, "mao");
                await Session.InitializeAsync(lifetime.Token);
                if (!string.IsNullOrEmpty(history.RecoveryMessage)) ui.ShowNotice(history.RecoveryMessage, true);
                if (!stopped && !string.IsNullOrEmpty(Argument("-evidenceDirectory")))
                    gameObject.AddComponent<DesktopEvidenceRunner>().Initialize(this, Argument("-evidenceDirectory"));
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                startupError = "桌面初始化失败，请检查本机数据目录权限和启动说明。";
                Debug.LogError("DESKTOP_STARTUP_FAILED: inspect resource and local storage availability.");
            }
        }

        private static string ReadToken()
        {
            try
            {
                string path = Argument("-previewConfig") ?? Environment.GetEnvironmentVariable("U01_PREVIEW_CONFIG") ??
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuroSaki", "preview-runtime", "config.json");
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 4096) return "unconfigured";
                var config = JsonUtility.FromJson<RuntimeConfig>(File.ReadAllText(path));
                if (config == null || config.protocol != "unity-preview/1" || config.base_url != "http://127.0.0.1:8000" ||
                    string.IsNullOrEmpty(config.token) || config.token.Length < 32 || config.token.Length > 512) return "unconfigured";
                return config.token;
            }
            catch (Exception) { return "unconfigured"; }
        }

        private void OnSnapshot(SessionSnapshot snapshot) { if (Avatar != null) Avatar.BindOperation(snapshot.Operation); }
        private void OnPlaybackStarted(PlaybackStarted started) { if (Avatar != null) Avatar.SetPlaybackState(started.Turn, AvatarPlaybackState.Speaking); }
        private void OnPlaybackEnded(PlaybackEnded ended) { if (Avatar != null) Avatar.SetPlaybackState(ended.Turn, ended.Reason == PlaybackEndReason.Failed ? AvatarPlaybackState.Failed : AvatarPlaybackState.Stopped); }
        private void OnLevel(AudioLevelSample sample) { if (Avatar != null) Avatar.SetAudioLevel(sample); }
        private void OnExpression(TurnKey turn, Emotion emotion) { if (Avatar != null) Avatar.ApplyExpression(new AvatarActionContext(turn.Operation, turn.TurnId), emotion); }

        private void Update()
        {
            if (Screen.width != lastWidth || Screen.height != lastHeight) FitCamera();
        }

        private void FitCamera()
        {
            lastWidth = Screen.width; lastHeight = Screen.height;
            if (model == null || AvatarCamera == null) return;
            // Frame the published idle/greeting character set with motion headroom. The
            // sample's unpublished full-screen special-effect meshes must not shrink Mao.
            Bounds bounds = new Bounds(); bool found = false;
            foreach (var drawable in model.Drawables)
            {
                var renderer = drawable.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff) continue;
                var mesh = drawable.GetComponent<MeshFilter>();
                if (mesh == null || mesh.sharedMesh == null) continue;
                bool visible = false;
                var colors = mesh.sharedMesh.colors32;
                if (colors.Length == 0) visible = true;
                foreach (var color in colors) if (color.a > 2) { visible = true; break; }
                if (!visible) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found) return;
            AvatarCamera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10);
            float aspect = (float)Screen.width * AvatarCamera.rect.width / (Screen.height * AvatarCamera.rect.height);
            AvatarCamera.orthographicSize = Mathf.Max(bounds.extents.y * 1.22f, bounds.extents.x / aspect * 1.22f, .5f);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && Session != null && !Session.Snapshot.Settings.ContinuePlaybackOnFocusLost) Session.Cancel(StopReason.User);
        }

        private void OnGUI()
        {
            if (!string.IsNullOrEmpty(startupError)) GUI.Label(new Rect(20, 20, 850, 50), startupError);
        }

        private void Shutdown()
        {
            if (stopped) return;
            stopped = true;
            Session?.Cancel(StopReason.WindowClosing);
            lifetime.Cancel();
            Session?.Dispose();
            Avatar?.Dispose();
            if (modelObject != null) Destroy(modelObject);
        }
        private bool WantsToQuit()
        {
            if (quitAllowed || Session == null) return true;
            if (!quitPending)
            {
                quitPending = true;
                if (EventSystem.current != null) EventSystem.current.enabled = false;
                FinishQuitAsync();
            }
            return false;
        }
        private async void FinishQuitAsync()
        {
            // Keep Unity's synchronization context alive until queued terminal records
            // reach disk. The first action still silences local playback synchronously.
            Session.Cancel(StopReason.WindowClosing);
            await Task.Yield(); // Let the first wantsToQuit callback return before retrying.
            try
            {
                var flush = Session.FlushHistoryAsync();
                if (await Task.WhenAny(flush, Task.Delay(2000)) == flush) await flush;
                else Debug.LogWarning("DESKTOP_HISTORY_CLOSE_TIMEOUT: terminal write did not finish within two seconds.");
                if (Session.Snapshot.Error?.Code == "history_write_failed")
                    Debug.LogWarning("DESKTOP_HISTORY_CLOSE_FAILED: queued write reported a local storage error.");
            }
            catch (Exception) { Debug.LogWarning("DESKTOP_HISTORY_CLOSE_FAILED: check local storage availability."); }
            quitAllowed = true;
            Shutdown();
            Application.Quit();
        }
        private void OnApplicationQuit() { Shutdown(); }
        private void OnDestroy() { Application.wantsToQuit -= WantsToQuit; Shutdown(); lifetime.Dispose(); }
        public static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
