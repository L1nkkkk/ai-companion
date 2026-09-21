using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Motion;
using UnityEngine;

namespace Companion.Foundation
{
    // Deterministic rendering evidence only. This is not the U01-01 AvatarPresenter.
    public sealed class FoundationBehaviourProbe : MonoBehaviour, ICubismUpdatable
    {
        private static readonly string[] ParameterIds = {
            "ParamA", "ParamEyeLOpen", "ParamEyeROpen", "ParamBreath",
            "ParamEyeBallX", "ParamEyeBallY", "ParamAngleX", "ParamAngleY", "ParamAngleZ"
        };
        private static readonly string[] StageNames = {
            "idle", "neutral", "mouth-open", "mouth-closed", "left-eye-closed",
            "right-eye-closed", "both-eyes-closed", "eyes-open", "breath-low",
            "breath-high", "breath-wave", "look-left", "look-right", "look-up",
            "look-down", "look-center", "greeting-tapbody-0", "mask-stress-special-01", "return-idle"
        };
        private static readonly double[] StageStarts = {
            0, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 42, 52
        };
        private readonly Dictionary<string, CubismParameter> parameters = new Dictionary<string, CubismParameter>();
        private readonly List<StageSample> samples = new List<StageSample>(600);
        private readonly List<MotionEvent> events = new List<MotionEvent>(200);
        private readonly List<string> failures = new List<string>();
        private readonly HashSet<string> visibleMasked = new HashSet<string>();
        private readonly HashSet<string> visibleInverted = new HashSet<string>();
        private CubismModel model;
        private CubismMotionController motion;
        private AnimationClip idle;
        private AnimationClip greeting;
        private AnimationClip maskStress;
        private string evidenceDirectory;
        private double started;
        private double stageEntered;
        private int currentStage = -1;
        private int currentCycle = -1;
        private bool initialized;
        private bool finished;
        private bool sampledStage;
        private int dynamicCallbacks;
        private int pendingCaptures;
        private float[] initialPartOpacities;
        private float[] submittedValues = new float[ParameterIds.Length];
        private Vector3[][] neutralGeometry;
        private MotionRun activeMotion;
        private readonly List<MotionRun> motionRuns = new List<MotionRun>(90);

        public int ExecutionOrder { get { return 900; } }
        public bool NeedsUpdateOnEditing { get { return false; } }
        public bool HasUpdateController { get; set; }
        public bool HasFailures { get { return failures.Count != 0; } }

        [Serializable]
        private sealed class StageSample
        {
            public int cycle;
            public string stage;
            public double elapsedSeconds;
            public float[] submittedParameterValues;
            public int changedDrawablesFromNeutral;
            public string[] visibleMaskedIds;
            public string[] visibleInvertedIds;
            public int geometryDirtyDrawables;
            public int renderOrderDirtyDrawables;
            public bool animationPlaying;
            public string screenshot;
        }

        [Serializable]
        private sealed class MotionEvent
        {
            public double elapsedSeconds;
            public string kind;
            public int instanceId;
        }

        [Serializable]
        private sealed class MotionRun
        {
            public int cycle;
            public string stage;
            public string clip;
            public int instanceId;
            public bool loop;
            public double startedSeconds;
            public bool playingAfterRequest;
            public bool beginObserved;
            public bool endObserved;
            public bool completedStage;
        }

        [Serializable]
        private sealed class Summary
        {
            public string scope = "U01-00 deterministic rendering fixture; manual parameter sweeps and SDK clips, no actual audio, pointer interaction or AvatarPresenter";
            public string greetingMapping = "Fixture greeting = model3 TapBody[0] / mtn_02; official model does not name this clip greeting";
            public string sampleSemantics = "Values are CubismParameter proxies submitted at order 900. Dynamic drawable data comes from asynchronous Core evaluation after a stable stage delay; not an exact same-frame parameter/geometry assertion.";
            public string maskingLimit = "Visible means Core IsVisible and opacity > 0.001, not guaranteed on-screen pixel contribution. Screenshots need visual review; mask counts do not prove every mask path correct.";
            public double elapsedSeconds;
            public int fullyElapsedCycles;
            public int dynamicCallbacks;
            public int pendingCaptures;
            public bool hasFailures;
            public string[] failures;
            public string[] parameterIds;
            public bool[] parameterAvailable;
            public string[] allMaskedIds;
            public string[] allInvertedIds;
            public string[] observedVisibleMaskedIds;
            public string[] observedVisibleInvertedIds;
            public string[] unobservedMaskedIds;
            public string[] unobservedInvertedIds;
            public StageSample[] samples;
            public MotionRun[] motionRuns;
            public MotionEvent[] motionEvents;
        }

        public void Initialize(CubismModel model, CubismMotionController motion,
            AnimationClip idle, AnimationClip greeting, AnimationClip maskStress,
            string evidenceDirectory, double started)
        {
            if (initialized) throw new InvalidOperationException("Behaviour probe already initialized.");
            this.model = model;
            this.motion = motion;
            this.idle = idle;
            this.greeting = greeting;
            this.maskStress = maskStress;
            this.evidenceDirectory = evidenceDirectory;
            this.started = started;
            if (model == null || motion == null) throw new ArgumentException("Probe needs model and motion controller.");
            if (gameObject != model.gameObject) throw new ArgumentException("Probe must be attached to the model root.");
            foreach (var parameter in model.Parameters) parameters.Add(parameter.Id, parameter);
            foreach (var id in ParameterIds)
                if (!parameters.ContainsKey(id)) Fail("Missing parameter; fixture stage degrades without substitution: " + id);
            if (idle == null) Fail("Missing idle clip.");
            if (greeting == null) Fail("Missing TapBody[0] greeting candidate clip.");
            if (maskStress == null) Fail("Missing special_01 mask stress clip.");
            initialPartOpacities = new float[model.Parts.Length];
            for (var i = 0; i < initialPartOpacities.Length; i++) initialPartOpacities[i] = model.Parts[i].Opacity;
            model.OnDynamicDrawableData += SampleDynamicData;
            motion.AnimationBeginHandler += OnMotionBegin;
            motion.AnimationEndHandler += OnMotionEnd;
            var controller = model.GetComponent<CubismUpdateController>();
            if (controller == null) controller = model.gameObject.AddComponent<CubismUpdateController>();
            HasUpdateController = true;
            initialized = true;
            controller.Refresh();
        }

        public void OnLateUpdate()
        {
            if (!initialized || finished) return;
            var elapsed = Math.Max(0, Time.realtimeSinceStartupAsDouble - started);
            var cycle = (int)(elapsed / 60);
            var second = elapsed % 60;
            var stage = StageStarts.Length - 1;
            while (stage > 0 && second < StageStarts[stage]) stage--;
            if (stage != currentStage || cycle != currentCycle) EnterStage(stage, cycle);
            if (stage >= 1 && stage <= 15)
            {
                // All manual stages share the same neutral pose. This isolates the tested
                // parameter from SDK animation, expression, look and physics controllers.
                foreach (var parameter in model.Parameters) parameter.Value = parameter.DefaultValue;
                for (var i = 0; i < initialPartOpacities.Length; i++) model.Parts[i].Opacity = initialPartOpacities[i];
                if (stage == 2) Set("ParamA", 1);
                if (stage == 4 || stage == 6) Set("ParamEyeLOpen", 0);
                if (stage == 5 || stage == 6) Set("ParamEyeROpen", 0);
                if (stage == 9) Set("ParamBreath", 1);
                if (stage == 10) Set("ParamBreath", (float)(0.5 + 0.5 * Math.Sin((second - 24) * Math.PI)));
                if (stage == 11) { Set("ParamEyeBallX", -0.8f); Set("ParamAngleX", -15); }
                if (stage == 12) { Set("ParamEyeBallX", 0.8f); Set("ParamAngleX", 15); }
                if (stage == 13) { Set("ParamEyeBallY", 0.8f); Set("ParamAngleY", 15); }
                if (stage == 14) { Set("ParamEyeBallY", -0.8f); Set("ParamAngleY", -15); }
            }
            for (var i = 0; i < ParameterIds.Length; i++)
            {
                CubismParameter parameter;
                // The availability array disambiguates a missing parameter's placeholder
                // from a valid zero, and keeps evidence strict JSON without NaN literals.
                submittedValues[i] = parameters.TryGetValue(ParameterIds[i], out parameter) ? parameter.Value : 0;
            }
        }

        private void EnterStage(int stage, int cycle)
        {
            CompleteMotionStage();
            currentStage = stage;
            currentCycle = cycle;
            stageEntered = Time.realtimeSinceStartupAsDouble;
            sampledStage = false;
            if (stage == 1) motion.StopAllAnimation();
            if (stage == 0 || stage == 18) Play(idle, true);
            if (stage == 16) Play(greeting, false);
            if (stage == 17) Play(maskStress, false);
        }

        private void Play(AnimationClip clip, bool loop)
        {
            if (clip == null) return;
            var id = 0;
            var foundId = false;
            foreach (var clipEvent in clip.events)
                if (clipEvent.functionName == "InstanceId") { id = clipEvent.intParameter; foundId = true; break; }
            if (!foundId) Fail("Clip has no Cubism fade InstanceId: " + clip.name);
            activeMotion = new MotionRun {
                cycle = currentCycle + 1, stage = StageNames[currentStage], clip = clip.name,
                instanceId = id, loop = loop, startedSeconds = Time.realtimeSinceStartupAsDouble - started
            };
            motionRuns.Add(activeMotion);
            motion.PlayAnimation(clip, priority: CubismMotionPriority.PriorityForce, isLoop: loop);
            activeMotion.playingAfterRequest = motion.IsPlayingAnimation();
            if (!activeMotion.playingAfterRequest) Fail("SDK did not start clip: " + clip.name);
        }

        private void CompleteMotionStage()
        {
            if (activeMotion == null) return;
            activeMotion.completedStage = true;
            if (!activeMotion.beginObserved) Fail("No SDK begin callback: " + activeMotion.clip);
            if (!activeMotion.loop && !activeMotion.endObserved) Fail("No SDK end callback before stage transition: " + activeMotion.clip);
            activeMotion = null;
        }

        private void OnMotionBegin(int id)
        {
            events.Add(new MotionEvent { elapsedSeconds = Time.realtimeSinceStartupAsDouble - started, kind = "begin", instanceId = id });
            if (activeMotion != null && activeMotion.instanceId == id) activeMotion.beginObserved = true;
        }

        private void OnMotionEnd(int id)
        {
            events.Add(new MotionEvent { elapsedSeconds = Time.realtimeSinceStartupAsDouble - started, kind = "end", instanceId = id });
            if (activeMotion != null && activeMotion.instanceId == id) activeMotion.endObserved = true;
        }

        private void Set(string id, float value)
        {
            CubismParameter parameter;
            if (parameters.TryGetValue(id, out parameter))
                parameter.Value = Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue);
        }

        private void SampleDynamicData(CubismModel sender, CubismDynamicDrawableData[] data)
        {
            if (!initialized || finished || currentStage < 0) return;
            dynamicCallbacks++;
            // Coverage uses every delivered Core frame; detailed evidence stays bounded.
            for (var i = 0; i < data.Length; i++)
                if (data[i].IsVisible && data[i].Opacity > 0.001f)
                {
                    if (model.Drawables[i].IsMasked) visibleMasked.Add(model.Drawables[i].Id);
                    if (model.Drawables[i].IsInverted) visibleInverted.Add(model.Drawables[i].Id);
                }
            var delay = currentStage == 16 ? 1.5 : currentStage == 17 ? 3.5 : 0.8;
            if (sampledStage || Time.realtimeSinceStartupAsDouble - stageEntered < delay) return;
            sampledStage = true;
            if (currentStage == 1 && neutralGeometry == null)
            {
                neutralGeometry = new Vector3[data.Length][];
                for (var i = 0; i < data.Length; i++) neutralGeometry[i] = (Vector3[])data[i].VertexPositions.Clone();
            }
            var masked = new List<string>();
            var inverted = new List<string>();
            var changed = neutralGeometry == null ? -1 : 0;
            var geometryDirty = 0;
            var orderDirty = 0;
            for (var i = 0; i < data.Length; i++)
            {
                if (data[i].AreVertexPositionsDirty) geometryDirty++;
                if (data[i].IsRenderOrderDirty) orderDirty++;
                if (neutralGeometry != null && GeometryChanged(neutralGeometry[i], data[i].VertexPositions)) changed++;
                if (!data[i].IsVisible || data[i].Opacity <= 0.001f) continue;
                if (model.Drawables[i].IsMasked) masked.Add(model.Drawables[i].Id);
                if (model.Drawables[i].IsInverted) inverted.Add(model.Drawables[i].Id);
            }
            if ((currentStage == 2 || currentStage == 4 || currentStage == 5 || currentStage == 6 ||
                 currentStage == 9 || (currentStage >= 11 && currentStage <= 14)) && changed <= 0)
                Fail("No Core geometry response in stage " + StageNames[currentStage]);
            var screenshot = currentCycle == 0 ? "behavior-" + currentStage.ToString("D2") + "-" + StageNames[currentStage] + ".png" : null;
            samples.Add(new StageSample {
                cycle = currentCycle + 1, stage = StageNames[currentStage],
                elapsedSeconds = Time.realtimeSinceStartupAsDouble - started,
                submittedParameterValues = (float[])submittedValues.Clone(), changedDrawablesFromNeutral = changed,
                visibleMaskedIds = masked.ToArray(), visibleInvertedIds = inverted.ToArray(),
                geometryDirtyDrawables = geometryDirty, renderOrderDirtyDrawables = orderDirty,
                animationPlaying = motion.IsPlayingAnimation(), screenshot = screenshot
            });
            if (screenshot != null && !string.IsNullOrEmpty(evidenceDirectory)) StartCoroutine(Capture(screenshot));
        }

        private static bool GeometryChanged(Vector3[] before, Vector3[] after)
        {
            if (before.Length != after.Length) return true;
            for (var i = 0; i < before.Length; i++)
                if ((before[i] - after[i]).sqrMagnitude > 0.0000000001f) return true;
            return false;
        }

        private IEnumerator Capture(string name)
        {
            pendingCaptures++;
            yield return new WaitForEndOfFrame();
            Texture2D texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(evidenceDirectory, name), texture.EncodeToPNG());
            }
            catch (Exception exception) { Fail("Screenshot failed: " + name + ": " + exception.GetType().Name); }
            finally { if (texture != null) Destroy(texture); pendingCaptures--; }
        }

        public void Finish()
        {
            if (!initialized || finished) return;
            var elapsed = Time.realtimeSinceStartupAsDouble - started;
            if (dynamicCallbacks == 0) Fail("No Core dynamic drawable callbacks.");
            if (pendingCaptures != 0) Fail("Evidence capture still pending at finish.");
            if (elapsed >= 60)
            {
                var expectedCycles = (int)(elapsed / 60);
                for (var cycle = 1; cycle <= expectedCycles; cycle++)
                    for (var stage = 0; stage < StageNames.Length; stage++)
                        if (!samples.Exists(sample => sample.cycle == cycle && sample.stage == StageNames[stage]))
                            Fail("Missing sample cycle " + cycle + " stage " + StageNames[stage]);
            }
            // Only completed non-loop stages require an end; a deliberate early quit may
            // interrupt the current clip and is reported as incomplete, not a fake end.
            finished = true;
            var allMasked = new List<string>();
            var allInverted = new List<string>();
            var unobservedMasked = new List<string>();
            var unobservedInverted = new List<string>();
            foreach (var drawable in model.Drawables)
            {
                if (drawable.IsMasked) { allMasked.Add(drawable.Id); if (!visibleMasked.Contains(drawable.Id)) unobservedMasked.Add(drawable.Id); }
                if (drawable.IsInverted) { allInverted.Add(drawable.Id); if (!visibleInverted.Contains(drawable.Id)) unobservedInverted.Add(drawable.Id); }
            }
            if (string.IsNullOrEmpty(evidenceDirectory)) return;
            Directory.CreateDirectory(evidenceDirectory);
            var summary = new Summary {
                elapsedSeconds = elapsed, fullyElapsedCycles = (int)(elapsed / 60), dynamicCallbacks = dynamicCallbacks,
                pendingCaptures = pendingCaptures, hasFailures = HasFailures, failures = failures.ToArray(), parameterIds = ParameterIds,
                parameterAvailable = Array.ConvertAll(ParameterIds, id => parameters.ContainsKey(id)),
                allMaskedIds = allMasked.ToArray(), allInvertedIds = allInverted.ToArray(),
                observedVisibleMaskedIds = Sorted(visibleMasked), observedVisibleInvertedIds = Sorted(visibleInverted),
                unobservedMaskedIds = unobservedMasked.ToArray(), unobservedInvertedIds = unobservedInverted.ToArray(),
                samples = samples.ToArray(), motionRuns = motionRuns.ToArray(), motionEvents = events.ToArray()
            };
            File.WriteAllText(Path.Combine(evidenceDirectory, "behavior-summary.json"), JsonUtility.ToJson(summary, true));
            using (var writer = new StreamWriter(Path.Combine(evidenceDirectory, "behavior-stages.csv")))
            {
                writer.WriteLine("cycle,stage,elapsed_seconds," + string.Join(",", ParameterIds) + ",changed_drawables_from_neutral,geometry_dirty,render_order_dirty,animation_playing,visible_masked_ids,visible_inverted_ids,screenshot");
                foreach (var sample in samples)
                {
                    var values = Array.ConvertAll(sample.submittedParameterValues, value => value.ToString("R", CultureInfo.InvariantCulture));
                    writer.WriteLine(sample.cycle + "," + sample.stage + "," + sample.elapsedSeconds.ToString("F4", CultureInfo.InvariantCulture) + "," +
                        string.Join(",", values) + "," + sample.changedDrawablesFromNeutral + "," + sample.geometryDirtyDrawables + "," +
                        sample.renderOrderDirtyDrawables + "," + sample.animationPlaying + "," + string.Join(";", sample.visibleMaskedIds) + "," +
                        string.Join(";", sample.visibleInvertedIds) + "," + sample.screenshot);
                }
            }
        }

        private static string[] Sorted(HashSet<string> values)
        {
            var result = new List<string>(values);
            result.Sort(StringComparer.Ordinal);
            return result.ToArray();
        }

        private void Fail(string message)
        {
            if (failures.Contains(message)) return;
            failures.Add(message);
            Debug.LogError("U01-00 behaviour fixture: " + message);
        }

        private void OnDestroy()
        {
            if (model != null) model.OnDynamicDrawableData -= SampleDynamicData;
            if (motion != null) { motion.AnimationBeginHandler -= OnMotionBegin; motion.AnimationEndHandler -= OnMotionEnd; }
        }
    }
}
