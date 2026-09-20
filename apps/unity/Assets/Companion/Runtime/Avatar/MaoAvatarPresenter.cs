using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Contracts;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Motion;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AICompanion.Preview.Avatar
{
    // All SDK types and parameter choices stay in the presentation assembly.
    public sealed class MaoAvatarPresenter : MonoBehaviour, IAvatarPresenter, ICubismUpdatable
    {
        private readonly Dictionary<string, CubismParameter> parameters = new Dictionary<string, CubismParameter>();
        private readonly System.Random blinkRandom = new System.Random(1701);
        private CubismModel model;
        private CubismMotionController motion;
        private Camera view;
        private AnimationClip idle;
        private AnimationClip greeting;
        private OperationKey? operation;
        private TurnKey? playbackTurn;
        private bool speaking;
        private bool suspended;
        private bool disposed;
        private bool initialized;
        private float mouth;
        private float targetMouth;
        private double lastLevelAt;
        private double nextBlink = 2.4;
        private double blinkStarted = -100;
        private double greetingEnds;
        private Vector2 gaze;
        private readonly List<MeshRenderer> excludedRenderers = new List<MeshRenderer>();

        // These sample-only ink/effect paths were outside the accepted visual evidence.
        // They are explicitly excluded from this product's neutral/idle/TapBody[0] set.
        public static readonly string[] ExcludedDrawableIds = { "ArtMesh274", "ArtMesh273", "ArtMesh256", "ArtMesh193", "ArtMesh192" };
        public AvatarCapabilities Capabilities { get; private set; }
        public int ExecutionOrder => 900;
        public bool NeedsUpdateOnEditing => false;
        public bool HasUpdateController { get; set; }
        public float MouthValue => mouth;
        public float BlinkValue { get; private set; } = 1;
        public float BreathValue { get; private set; }
        public Vector2 GazeValue => gaze;
        public int GreetingCount { get; private set; }
        public int BlinkCount { get; private set; }

        public void Initialize(Camera camera, AnimationClip idleClip, AnimationClip greetingClip)
        {
            if (initialized) throw new InvalidOperationException("Avatar already initialized.");
            model = GetComponent<CubismModel>();
            if (model == null) throw new InvalidOperationException("Mao resource is unavailable.");
            view = camera;
            idle = idleClip;
            greeting = greetingClip;
            motion = GetComponent<CubismMotionController>();
            if (motion == null) motion = gameObject.AddComponent<CubismMotionController>();
            var available = new List<AvatarParameter>();
            foreach (var parameter in model.Parameters)
            {
                parameters.Add(parameter.Id, parameter);
                available.Add(new AvatarParameter(parameter.Id, parameter.MinimumValue, parameter.MaximumValue, parameter.DefaultValue));
            }
            var excluded = new HashSet<string>(ExcludedDrawableIds);
            foreach (var drawable in model.Drawables)
                if (excluded.Contains(drawable.Id))
                {
                    var renderer = drawable.GetComponent<MeshRenderer>();
                    if (renderer != null) { renderer.forceRenderingOff = true; excludedRenderers.Add(renderer); }
                }
            Capabilities = new AvatarCapabilities("mao", "cubism-r41/Mao.model3.json", available,
                Has("ParamA"), Has("ParamEyeLOpen") && Has("ParamEyeROpen"), Has("ParamBreath"),
                Has("ParamEyeBallX") && Has("ParamEyeBallY"), new[] { Emotion.Neutral },
                greeting != null ? new[] { "greeting" } : Array.Empty<string>());
            var controller = GetComponent<CubismUpdateController>();
            if (controller == null) controller = gameObject.AddComponent<CubismUpdateController>();
            initialized = true;
            HasUpdateController = true;
            controller.Refresh();
            nextBlink = Time.unscaledTimeAsDouble + 2.4;
            // Start() in CubismMotionController must run before PlayAnimation.
            StartCoroutine(StartIdle());
        }

        private System.Collections.IEnumerator StartIdle()
        {
            yield return null;
            if (!disposed && idle != null) motion.PlayAnimation(idle, priority: CubismMotionPriority.PriorityForce, isLoop: true);
        }

        public Task<AvatarLoadResult> LoadCharacterAsync(Guid loadRequestId, string characterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(!disposed && initialized && characterId == "mao"
                ? new AvatarLoadResult(loadRequestId, characterId, Capabilities, null)
                : new AvatarLoadResult(loadRequestId, characterId, null, new PreviewError("character_unavailable", "角色资源未就绪，请按资源说明恢复 Mao。", false, null)));
        }

        public void BindOperation(OperationKey? value)
        {
            if (operation == value) return;
            operation = value;
            playbackTurn = null;
            CloseMouth();
        }

        public void SetPlaybackState(TurnKey turn, AvatarPlaybackState state)
        {
            if (!operation.HasValue || operation.Value != turn.Operation || disposed) return;
            if (state == AvatarPlaybackState.Speaking)
            {
                playbackTurn = turn;
                speaking = true;
                lastLevelAt = Time.unscaledTimeAsDouble;
            }
            else if (!playbackTurn.HasValue || playbackTurn.Value == turn)
            {
                CloseMouth();
                playbackTurn = null;
            }
        }

        public void SetAudioLevel(AudioLevelSample sample)
        {
            if (disposed || suspended || !speaking || !playbackTurn.HasValue || sample.Turn != playbackTurn.Value) return;
            if (float.IsNaN(sample.Level01) || float.IsInfinity(sample.Level01)) return;
            // Audio supplies actual post-volume RMS. Silence resets immediately; only audible
            // samples use visual smoothing. No timer or animation generates mouth activity.
            targetMouth = Mathf.Clamp01(sample.Level01 * 4f);
            lastLevelAt = Time.unscaledTimeAsDouble;
            if (targetMouth == 0) { mouth = 0; Set("ParamA", 0); }
        }

        public AvatarActionResult ApplyExpression(AvatarActionContext context, Emotion emotion)
        {
            var failure = Guard(context);
            if (failure != null) return failure;
            // No guessed expression labels: the supplied exp_01..08 have no semantic mapping.
            return new AvatarActionResult(emotion == Emotion.Neutral ? AvatarActionStatus.Applied : AvatarActionStatus.Unsupported,
                emotion != Emotion.Neutral, null);
        }

        public AvatarActionResult ApplyMotion(AvatarActionContext context, string motionId)
        {
            var failure = Guard(context);
            if (failure != null) return failure;
            if (motionId != "greeting" || greeting == null)
                return new AvatarActionResult(AvatarActionStatus.Unsupported, true, null);
            Greet();
            return new AvatarActionResult(AvatarActionStatus.Applied, false, null);
        }

        private AvatarActionResult Guard(AvatarActionContext context)
        {
            if (disposed || suspended || !initialized) return new AvatarActionResult(AvatarActionStatus.Unavailable, true, null);
            if (context == null || !operation.HasValue || context.Operation != operation.Value ||
                (context.TurnId.HasValue && (!playbackTurn.HasValue || context.TurnId != playbackTurn.Value.TurnId)))
                return new AvatarActionResult(AvatarActionStatus.StaleOperation, false, null);
            return null;
        }

        // A direct local greeting is independent of AI operations and never starts audio.
        public void Greet()
        {
            if (!initialized || disposed || suspended || greeting == null || Time.unscaledTimeAsDouble < greetingEnds) return;
            motion.PlayAnimation(greeting, priority: CubismMotionPriority.PriorityForce, isLoop: false);
            greetingEnds = Time.unscaledTimeAsDouble + greeting.length;
            GreetingCount++;
        }

        private void Update()
        {
            if (!initialized || disposed || suspended) return;
            if (greetingEnds > 0 && Time.unscaledTimeAsDouble >= greetingEnds)
            {
                greetingEnds = 0;
                if (idle != null) motion.PlayAnimation(idle, priority: CubismMotionPriority.PriorityForce, isLoop: true);
            }
            if (view != null && Application.isFocused)
            {
                var point = view.ScreenToViewportPoint(Input.mousePosition);
                bool inside = point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1;
                var target = inside ? new Vector2((point.x - .5f) * 1.5f, (point.y - .5f) * 1.2f) : Vector2.zero;
                gaze = Vector2.Lerp(gaze, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 6));
                if (inside && Input.GetMouseButtonDown(0) && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())) Greet();
            }
        }

        public void OnLateUpdate()
        {
            if (!initialized || disposed) return;
            double now = Time.unscaledTimeAsDouble;
            if (!suspended && now >= nextBlink)
            {
                blinkStarted = now;
                nextBlink = now + 2.8 + blinkRandom.NextDouble() * 2.5;
                BlinkCount++;
            }
            double blinkTime = now - blinkStarted;
            BlinkValue = blinkTime < .09 ? 1f - (float)(blinkTime / .09) :
                blinkTime < .16 ? 0 : blinkTime < .30 ? (float)((blinkTime - .16) / .14) : 1;
            BreathValue = suspended ? .5f : .5f + .5f * Mathf.Sin((float)now * 1.45f);
            Set("ParamEyeLOpen", BlinkValue);
            Set("ParamEyeROpen", BlinkValue);
            Set("ParamBreath", BreathValue);
            if (greetingEnds == 0)
            {
                Set("ParamEyeBallX", gaze.x);
                Set("ParamEyeBallY", gaze.y);
                Set("ParamAngleX", gaze.x * 12);
                Set("ParamAngleY", gaze.y * 10);
            }
            if (!speaking || suspended || now - lastLevelAt > .25) { mouth = 0; targetMouth = 0; }
            else if (targetMouth > 0) mouth = Mathf.Lerp(mouth, targetMouth, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 24));
            Set("ParamA", mouth);
            Set("ParamI", 0); Set("ParamU", 0); Set("ParamE", 0); Set("ParamO", 0);
            // Unreviewed sample-only ink/heart paths cannot become visible via a clip.
            foreach (var renderer in excludedRenderers) if (renderer != null) renderer.forceRenderingOff = true;
        }

        private bool Has(string id) => parameters.ContainsKey(id);
        private void Set(string id, float value)
        {
            if (parameters.TryGetValue(id, out var parameter)) parameter.Value = Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue);
        }
        private void CloseMouth() { speaking = false; mouth = 0; targetMouth = 0; Set("ParamA", 0); }
        public void Suspend() { suspended = true; CloseMouth(); }
        public void Resume() { if (!disposed) suspended = false; }
        public void Dispose() { if (disposed) return; CloseMouth(); disposed = true; operation = null; }
        private void OnDestroy() { Dispose(); }
    }
}
