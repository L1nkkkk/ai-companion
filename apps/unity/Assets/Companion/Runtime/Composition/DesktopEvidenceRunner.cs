using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using AICompanion.Preview.Contracts;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AICompanion.Preview.Composition
{
    // Opt-in instrumentation of the real Player. It never runs a cloud request or reads
    // a user's history. Automated interaction requires a separate explicit test directory.
    public sealed class DesktopEvidenceRunner : MonoBehaviour
    {
        private DesktopBootstrap app;
        private string directory;
        private double started;
        private double lastFrame;
        private double nextSample;
        private double duration;
        private StreamWriter frames;
        private StreamWriter samples;
        private StreamWriter levels;
        private StreamWriter events;
        private StreamWriter stops;
        private int downloadGeneration = -1;
        private bool finished;
        private bool scripted;
        private readonly Result result = new Result();
        private readonly List<string> failures = new List<string>();
        private readonly long[] histogram = new long[10002];
        private long frameCount;
        private long baselineWorking;
        private long lastLevelTick;
        private double nextScreenshot;

        [Serializable] private sealed class Result
        {
            public string scope = "Real Windows Player, fixture HTTP/WAV, Unity output callback sampling. No cloud/ASR or OS loopback latency claim.";
            public string unity;
            public string graphics;
            public string audioDriver;
            public int outputSampleRate;
            public int dspBufferFrames;
            public int dspBufferCount;
            public double durationSeconds;
            public long frames;
            public double p95FrameMs;
            public double maximumFrameMs;
            public double framesWithin33_3Percent;
            public long framesOverOneSecond;
            public long baselineWorkingBytes;
            public long finalWorkingBytes;
            public long peakWorkingBytes;
            public long peakPrivateBytes;
            public int actualPlaybackStarts;
            public int actualPlaybackCompletions;
            public int playbackStops;
            public int generationStops;
            public int acceptedGenerationStops;
            public int downloadStops;
            public int textOnlyCompleted;
            public int nonzeroLevelSamples;
            public int zeroLevelSamples;
            public int errors;
            public int warnings;
            public int blinks;
            public int greetings;
            public bool scriptedChecksCompleted;
            public string[] failures;
        }

        [StructLayout(LayoutKind.Sequential)] private struct MemoryCounters
        {
            public uint Size; public uint PageFaultCount; public UIntPtr PeakWorking; public UIntPtr Working;
            public UIntPtr QuotaPeakPaged; public UIntPtr QuotaPaged; public UIntPtr QuotaPeakNonPaged;
            public UIntPtr QuotaNonPaged; public UIntPtr Pagefile; public UIntPtr PeakPagefile; public UIntPtr Private;
        }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool QueryPerformanceCounter(out long counter);
        [DllImport("kernel32.dll")] private static extern bool QueryPerformanceFrequency(out long frequency);
        [DllImport("psapi.dll")] private static extern bool GetProcessMemoryInfo(IntPtr handle, ref MemoryCounters memory, uint size);

        public void Initialize(DesktopBootstrap bootstrap, string path)
        {
            app = bootstrap; directory = Path.GetFullPath(path); Directory.CreateDirectory(directory);
            started = Time.realtimeSinceStartupAsDouble; lastFrame = started;
            double.TryParse(DesktopBootstrap.Argument("-smokeSeconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
            if (duration <= 0) duration = 600;
            duration = Math.Min(duration, 1800);
            string testMode = DesktopBootstrap.Argument("-desktopTest");
            scripted = testMode == "fixture" || testMode == "fault" || testMode == "late-audio" || testMode == "generation";
            if (scripted && (string.IsNullOrEmpty(DesktopBootstrap.Argument("-userDataPath")) || app.Session.Snapshot.Mode != PreviewMode.Fixture))
            { scripted = false; failures.Add("Scripted checks require an explicit isolated userDataPath and fixture capability."); }
            frames = Writer("frame-times.csv", "seconds,frame_ms");
            samples = Writer("memory-avatar.csv", "seconds,working_bytes,private_bytes,unity_bytes,mouth,blink,breath,gaze_x,gaze_y,phase");
            levels = Writer("output-levels.csv", "monotonic_ticks,request_id,generation,turn_id,sample_start,sample_count,post_volume_rms,volume");
            events = Writer("events.csv", "monotonic_ticks,event,request_id,generation,played_samples,total_samples");
            stops = Writer("stop-measurements.csv", "sample,request_id,generation,stop_ticks,last_nonzero_before_ticks,last_nonzero_after_ticks,return_ticks,clock_frequency,output_block_frames,output_sample_rate,stop_qpc_ticks,qpc_frequency");
            app.Gateway.AudioDownloadStarted += OnDownloadStarted;
            result.unity = Application.unityVersion; result.graphics = SystemInfo.graphicsDeviceName;
            result.audioDriver = AudioSettings.driverCapabilities.ToString(); result.outputSampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out result.dspBufferFrames, out result.dspBufferCount);
            app.Player.PostVolumeLevel += OnLevel;
            app.Player.PlaybackStarted += OnStarted;
            app.Player.PlaybackEnded += OnEnded;
            Application.logMessageReceived += OnLog;
            nextScreenshot = 2;
            if (scripted)
            {
                var settings = app.Session.Snapshot.Settings;
                app.Session.UpdateSettings(new UpdateSettingsCommand(settings.AutoRead, settings.VoiceId, true, null));
                if (testMode == "fault") StartCoroutine(ExerciseFault());
                else if (testMode == "late-audio") StartCoroutine(ExerciseLateAudio());
                else if (testMode == "generation") StartCoroutine(ExerciseGenerating());
                else StartCoroutine(Exercise());
            }
        }

        private StreamWriter Writer(string name, string header)
        {
            var writer = new StreamWriter(Path.Combine(directory, name)); writer.WriteLine(header); return writer;
        }
        private static string F(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

        private void Update()
        {
            if (finished || app == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - started; double gap = (now - lastFrame) * 1000; lastFrame = now;
            if (elapsed > 3)
            {
                frameCount++; histogram[Math.Min(10001, (int)Math.Ceiling(gap * 10))]++;
                if (gap > 1000) result.framesOverOneSecond++;
                if (gap > result.maximumFrameMs) result.maximumFrameMs = gap;
                frames.WriteLine(F(elapsed) + "," + F(gap));
            }
            if (elapsed >= nextSample)
            {
                nextSample = elapsed + 5;
                var memory = new MemoryCounters(); memory.Size = (uint)Marshal.SizeOf<MemoryCounters>();
                if (GetProcessMemoryInfo(GetCurrentProcess(), ref memory, memory.Size))
                {
                    long working = (long)memory.Working.ToUInt64(); long privateBytes = (long)memory.Private.ToUInt64();
                    result.finalWorkingBytes = working;
                    result.peakWorkingBytes = Math.Max(result.peakWorkingBytes, (long)memory.PeakWorking.ToUInt64());
                    result.peakPrivateBytes = Math.Max(result.peakPrivateBytes, privateBytes);
                    if (elapsed >= 30 && baselineWorking == 0) baselineWorking = working;
                    var avatar = app.Avatar;
                    samples.WriteLine(F(elapsed) + "," + working + "," + privateBytes + "," + UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() +
                        "," + F(avatar.MouthValue) + "," + F(avatar.BlinkValue) + "," + F(avatar.BreathValue) + "," + F(avatar.GazeValue.x) + "," + F(avatar.GazeValue.y) + "," + app.Session.Snapshot.Phase);
                }
                frames.Flush(); samples.Flush(); levels.Flush(); events.Flush(); stops.Flush();
            }
            if (nextScreenshot > 0 && elapsed >= nextScreenshot)
            {
                nextScreenshot = -1;
                StartCoroutine(Capture("desktop-start.png"));
            }
            if (elapsed >= duration) { Finish(); Application.Quit(failures.Count == 0 && result.errors == 0 ? 0 : 1); }
        }

        private IEnumerator Exercise()
        {
            yield return new WaitForSecondsRealtime(3);
            app.Avatar.Greet();
            yield return new WaitForSecondsRealtime(2);
            app.Session.SubmitText(new SubmitTextCommand("你好，请演示桌面聊天。", "mao", "fixture-tone", true));
            yield return WaitReady(20);
            if (result.actualPlaybackCompletions == 0) failures.Add("Fixture audio did not finish through the real output path.");
            yield return Capture("desktop-chat.png");
            for (int i = 0; i < 20; i++)
            {
                long submittedAt = Stopwatch.GetTimestamp();
                app.Session.SubmitText(new SubmitTextCommand("停止测试 " + i, "mao", "fixture-tone", true));
                double deadline = Time.realtimeSinceStartupAsDouble + 15;
                while (app.Player.LastNonzeroOutputTicks <= submittedAt && Time.realtimeSinceStartupAsDouble < deadline && app.Session.Snapshot.Phase != SessionPhase.Error) yield return null;
                if (!app.Player.Snapshot.IsPlaying || app.Player.LastNonzeroOutputTicks <= submittedAt) { failures.Add("Playback stop sample did not reach audible callback: " + i); break; }
                Mark("stop-command");
                var stopTurn = app.Player.Snapshot.Turn.Value;
                long before = app.Player.LastNonzeroOutputTicks;
                long commandTick = Stopwatch.GetTimestamp();
                QueryPerformanceFrequency(out long nativeFrequency);
                QueryPerformanceCounter(out long nativeCommandTick);
                app.Session.Cancel(StopReason.User);
                stops.WriteLine(i + "," + stopTurn.Operation.RequestId + "," + stopTurn.Operation.Generation + "," + commandTick + "," + before + "," + app.Player.LastNonzeroOutputTicks + "," + Stopwatch.GetTimestamp() + "," + Stopwatch.Frequency + "," + app.Player.OutputBlockFrames + "," + AudioSettings.outputSampleRate + "," + nativeCommandTick + "," + nativeFrequency);
                if (app.Player.Snapshot.IsPlaying || app.Avatar.MouthValue != 0) failures.Add("Stop did not synchronously clear playback and mouth: " + i);
                result.playbackStops++;
                yield return new WaitForSecondsRealtime(.15f);
                if (app.Player.Snapshot.IsPlaying) failures.Add("Old playback revived after stop: " + i);
            }
            for (int i = 0; i < 10; i++)
            {
                app.Session.SubmitText(new SubmitTextCommand("生成取消测试 " + i, "mao", "fixture-tone", true));
                app.Session.Cancel(StopReason.User); result.generationStops++;
                yield return new WaitForSecondsRealtime(.15f);
                if (app.Player.Snapshot.IsPlaying) failures.Add("Generation cancellation revived audio: " + i);
            }
            app.Session.SubmitText(new SubmitTextCommand("关闭朗读，只显示演示文字。", "mao", "fixture-tone", false));
            yield return WaitReady(15);
            bool displayed = false;
            foreach (var record in app.Session.Snapshot.Messages)
                if (record.Role == MessageRole.Assistant && record.DeliveryKind == DeliveryKind.Text && record.DeliveryState == DeliveryState.Displayed) displayed = true;
            if (displayed) result.textOnlyCompleted++; else failures.Add("Completed text-only response was not marked displayed.");
            var export = app.Session.ExportConversationAsync(app.Session.Snapshot.ConversationId, CancellationToken.None);
            while (!export.IsCompleted) yield return null;
            if (export.IsFaulted || !export.Result.Succeeded) failures.Add("Conversation export failed.");
            else File.WriteAllBytes(Path.Combine(directory, "test-conversation-export.json"), export.Result.Value.Utf8Json.ToArray());
            // Volume and mute are applied during real playback; sample CSV retains both.
            app.Session.SubmitText(new SubmitTextCommand("音量和静音测试。", "mao", "fixture-tone", true));
            double until = Time.realtimeSinceStartupAsDouble + 15;
            while (!app.Player.Snapshot.IsPlaying && Time.realtimeSinceStartupAsDouble < until) yield return null;
            app.Session.SetVolume(.8f); yield return new WaitForSecondsRealtime(.6f);
            app.Session.SetVolume(.2f); yield return new WaitForSecondsRealtime(.6f);
            app.Session.SetVolume(0); yield return new WaitForSecondsRealtime(.3f);
            if (app.Avatar.MouthValue != 0) failures.Add("Mute left avatar mouth open.");
            yield return Capture("desktop-muted.png");
            app.Session.Cancel(StopReason.User); app.Session.SetVolume(.65f);
            result.scriptedChecksCompleted = true;
            // Repeat only accepted local greeting to observe post-warmup memory bounds.
            while (!finished)
            {
                app.Avatar.Greet();
                yield return new WaitForSecondsRealtime(15);
            }
        }

        private IEnumerator WaitReady(double timeout)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + timeout;
            yield return null;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                var phase = app.Session.Snapshot.Phase;
                if (phase == SessionPhase.Ready || phase == SessionPhase.Error || phase == SessionPhase.Offline) break;
                yield return null;
            }
            if (app.Session.Snapshot.Phase != SessionPhase.Ready) failures.Add("Request did not return Ready: " + app.Session.Snapshot.Phase);
        }

        private IEnumerator ExerciseFault()
        {
            yield return new WaitForSecondsRealtime(2);
            app.Session.SubmitText(new SubmitTextCommand("演示故障验证", "mao", "fixture-tone", true));
            double deadline = Time.realtimeSinceStartupAsDouble + 15;
            while (app.Session.Snapshot.Phase != SessionPhase.Error && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            string expected = DesktopBootstrap.Argument("-expectedError");
            var state = app.Session.Snapshot;
            if (state.Phase != SessionPhase.Error || state.Error == null || state.Error.Code != expected)
                failures.Add("Expected error " + expected + "; observed " + state.Phase + "/" + state.Error?.Code);
            if (result.actualPlaybackStarts != 0 || app.Player.Snapshot.IsPlaying || app.Avatar.MouthValue != 0)
                failures.Add("Invalid/failed response reached playback.");
            yield return Capture("desktop-fault.png");
            app.Session.Cancel(StopReason.User);
            if (app.Session.Snapshot.Phase != SessionPhase.Ready) failures.Add("Stop did not recover from a fault.");
            result.scriptedChecksCompleted = true;
        }

        private IEnumerator ExerciseLateAudio()
        {
            yield return new WaitForSecondsRealtime(2);
            for (int i = 0; i < 10; i++)
            {
                Volatile.Write(ref downloadGeneration, -1);
                var receipt = app.Session.SubmitText(new SubmitTextCommand("下载中取消测试 " + i, "mao", "fixture-tone", true));
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (Volatile.Read(ref downloadGeneration) != unchecked((int)receipt.Operation.Value.Generation) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                if (Volatile.Read(ref downloadGeneration) != unchecked((int)receipt.Operation.Value.Generation)) { failures.Add("Did not observe successful download headers for sample " + i); break; }
                Mark("download-cancel-command"); app.Session.Cancel(StopReason.User);
                result.downloadStops++;
                yield return new WaitForSecondsRealtime(.8f);
                if (app.Player.Snapshot.IsPlaying || app.Avatar.MouthValue != 0) failures.Add("Late audio revived after download cancellation: " + i);
            }
            if (result.actualPlaybackStarts != 0) failures.Add("Late download cancellation emitted playback.");
            result.scriptedChecksCompleted = true;
            yield return Capture("desktop-download-cancel.png");
        }
        private void OnDownloadStarted(TurnKey turn) { Volatile.Write(ref downloadGeneration, unchecked((int)turn.Operation.Generation)); }

        private IEnumerator ExerciseGenerating()
        {
            yield return new WaitForSecondsRealtime(2);
            for (int i = 0; i < 10; i++)
            {
                app.Session.SubmitText(new SubmitTextCommand("已接受生成中取消 " + i, "mao", "fixture-tone", true));
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (!app.Session.Snapshot.Turn.HasValue && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                if (!app.Session.Snapshot.Turn.HasValue || app.Session.Snapshot.Phase != SessionPhase.Thinking || app.Session.Snapshot.FullText.Length != 0)
                { failures.Add("Did not observe accepted generation stage: " + i); break; }
                Mark("accepted-generation-cancel"); app.Session.Cancel(StopReason.User);
                result.acceptedGenerationStops++;
                yield return new WaitForSecondsRealtime(.5f);
                if (app.Player.Snapshot.IsPlaying || app.Avatar.MouthValue != 0) failures.Add("Cancelled generation revived output.");
            }
            yield return new WaitForSecondsRealtime(3.2f);
            if (result.actualPlaybackStarts != 0) failures.Add("Old generation reached the player.");
            result.scriptedChecksCompleted = true;
            yield return Capture("desktop-generation-cancel.png");
        }
        private IEnumerator Capture(string name)
        {
            if (DesktopBootstrap.Argument("-noScreenshots") == "true") yield break;
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(directory, name), texture.EncodeToPNG()); }
            finally { Destroy(texture); }
        }
        private void OnLevel(AudioLevelSample sample)
        {
            if (finished) return;
            if (sample.Level01 > 0) result.nonzeroLevelSamples++; else result.zeroLevelSamples++;
            lastLevelTick = sample.MonotonicTicks;
            levels.WriteLine(sample.MonotonicTicks + "," + sample.Turn.Operation.RequestId + "," + sample.Turn.Operation.Generation + "," + sample.Turn.TurnId + "," + sample.SampleStart + "," + sample.SampleCount + "," + F(sample.Level01) + "," + F(app.Player.Snapshot.Volume01));
        }
        private void OnStarted(PlaybackStarted item) { result.actualPlaybackStarts++; Mark("playback-started"); }
        private void OnEnded(PlaybackEnded item) { if (item.Reason == PlaybackEndReason.Completed) result.actualPlaybackCompletions++; Mark("playback-ended-" + item.Reason); }
        private void Mark(string name)
        {
            if (finished) return;
            var snapshot = app.Player.Snapshot;
            var operation = app.Session.Snapshot.Operation ?? snapshot.Turn?.Operation;
            events.WriteLine(Stopwatch.GetTimestamp() + "," + name + "," + operation?.RequestId + "," + operation?.Generation + "," + snapshot.PlayedSamples + "," + snapshot.TotalSamples);
        }
        private void OnLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) result.errors++; if (type == LogType.Warning) result.warnings++; }
        private void Finish()
        {
            if (finished || app == null) return;
            finished = true;
            result.durationSeconds = Time.realtimeSinceStartupAsDouble - started;
            result.frames = frameCount; result.baselineWorkingBytes = baselineWorking;
            long cumulative = 0, within = 0; bool got95 = false;
            for (int i = 0; i < histogram.Length; i++)
            {
                cumulative += histogram[i]; if (i <= 333) within += histogram[i];
                if (!got95 && cumulative >= frameCount * .95) { result.p95FrameMs = i / 10d; got95 = true; }
            }
            result.framesWithin33_3Percent = frameCount > 0 ? within * 100d / frameCount : 0;
            result.blinks = app.Avatar.BlinkCount; result.greetings = app.Avatar.GreetingCount;
            if (scripted && !result.scriptedChecksCompleted) failures.Add("Scripted checks did not finish within the run.");
            result.failures = failures.ToArray();
            frames?.Dispose(); samples?.Dispose(); levels?.Dispose(); events?.Dispose(); stops?.Dispose();
            app.Gateway.AudioDownloadStarted -= OnDownloadStarted;
            File.WriteAllText(Path.Combine(directory, "player-result.json"), JsonUtility.ToJson(result, true));
            Application.logMessageReceived -= OnLog;
        }
        private void OnApplicationQuit() { Finish(); }
        private void OnDestroy() { Finish(); }
    }
}
