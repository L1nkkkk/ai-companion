using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Audio;
using AICompanion.Preview.Contracts;
using AICompanion.Preview.Session;
using AICompanion.Preview.Transport;
using UnityEngine;

namespace AICompanion.Preview.Tests
{
    /// <summary>Editor executeMethod checks; no Test Framework dependency or product mock path.</summary>
    public static class SessionAudioChecks
    {
        private static readonly List<string> Passed = new List<string>();
        public static void Run()
        {
            var context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                Passed.Clear();
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
                CheckWire(root);
                CheckWav(root).GetAwaiter().GetResult();
                CheckSession().GetAwaiter().GetResult();
                string output = Environment.GetEnvironmentVariable("COMPANION_TEST_OUTPUT") ?? Path.Combine(root, ".bootstrap/desktop/session-audio-checks");
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "session-audio-checks.json"), "{\"passed\":" + Passed.Count + ",\"checks\":[" + string.Join(",", Passed.Select(name => "\"" + name + "\"")) + "]}");
                Debug.Log("Session/audio checks passed: " + Passed.Count);
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        }

        private static void CheckWire(string root)
        {
            string folder = Path.Combine(root, "services/api/app/unity_preview/protocol");
            var operation = new OperationKey(Guid.Parse("11111111-1111-4111-8111-111111111111"), 1, Guid.Parse("22222222-2222-4222-8222-222222222222"));
            foreach (string name in new[] { "normal", "no_audio", "seq_duplicate", "tts_failure", "cancelled", "provider_timeout" })
            {
                var decoder = new EventDecoder(new TurnSubmission(operation, "mao", "测试", Array.Empty<HistoryContextItem>(), name != "no_audio", null));
                foreach (var line in File.ReadAllLines(Path.Combine(folder, name + ".ndjson"))) decoder.Accept(line);
                Assert(decoder.Terminal, "wire_" + name);
            }
            foreach (string name in new[] { "seq_gap", "wrong_ids" })
            {
                var decoder = new EventDecoder(new TurnSubmission(operation, "mao", "测试", Array.Empty<HistoryContextItem>(), true, null));
                Throws(() => { foreach (var line in File.ReadAllLines(Path.Combine(folder, name + ".ndjson"))) decoder.Accept(line); }, "wire_reject_" + name);
            }
            var partial = new EventDecoder(new TurnSubmission(operation, "mao", "测试", Array.Empty<HistoryContextItem>(), true, null));
            foreach (var line in File.ReadAllLines(Path.Combine(folder, "partial_stream.ndjson"))) partial.Accept(line);
            Assert(!partial.Terminal, "wire_partial_is_not_complete");
            string first = File.ReadAllLines(Path.Combine(folder, "normal.ndjson"))[0];
            Throws(() => new EventDecoder(new TurnSubmission(operation, "mao", "测试", Array.Empty<HistoryContextItem>(), true, null)).Accept(first.Replace("\"seq\":0", "\"seq\":0,\"seq\":0")), "wire_duplicate_json_key");
            Throws(() => new EventDecoder(new TurnSubmission(operation, "mao", "测试", Array.Empty<HistoryContextItem>(), true, null)).Accept(first.Replace("\"seq\":0", "\"seq\":0.0")), "wire_fractional_sequence");
            Throws(() => new HttpConversationGateway(new Uri("http://example.com:8000"), "token", WavValidator.ValidateAsync), "transport_nonloopback_rejected");
            Throws(() => new HttpConversationGateway(new Uri("http://127.0.0.1:8000/path"), "token", WavValidator.ValidateAsync), "transport_base_path_rejected");
        }

        private static async Task CheckWav(string root)
        {
            var turn = new TurnKey(new OperationKey(Guid.NewGuid(), 1, Guid.NewGuid()), Guid.NewGuid());
            var descriptor = Descriptor(turn, 192000);
            byte[] wav = File.ReadAllBytes(Path.Combine(root, "services/api/app/unity_preview/fixtures/demo-tone.wav"));
            using (var pcm = await WavValidator.ValidateAsync(wav, descriptor, CancellationToken.None))
            { Assert(pcm.TotalSamples == 192000 && pcm.SampleRate == 24000, "wav_actual_fixture_with_odd_junk_chunk"); }
            byte[] truncated = wav.Take(wav.Length - 1).ToArray();
            await ThrowsAsync(() => WavValidator.ValidateAsync(truncated, descriptor, CancellationToken.None), "wav_truncated_rejected");
            var wrongCount = Descriptor(turn, 191999);
            await ThrowsAsync(() => WavValidator.ValidateAsync(wav, wrongCount, CancellationToken.None), "wav_declared_sample_mismatch");
            var wrongCodec = new AudioReadyDescriptor(turn, descriptor.Path, 24000, 1, "mp3", 192000);
            await ThrowsAsync(() => WavValidator.ValidateAsync(wav, wrongCodec, CancellationToken.None), "wav_codec_spoof_rejected");
            byte[] malformedTail = wav.Concat(new byte[] { 0 }).ToArray();
            await ThrowsAsync(() => WavValidator.ValidateAsync(malformedTail, descriptor, CancellationToken.None), "wav_trailing_unparsed_bytes_rejected");
            using (var cancelled = new CancellationTokenSource())
            { cancelled.Cancel(); await ThrowsAsync(() => WavValidator.ValidateAsync(wav, descriptor, cancelled.Token), "wav_cancel_before_ownership"); }
            var owner = await WavValidator.ValidateAsync(wav, descriptor, CancellationToken.None);
            owner.Dispose(); owner.Dispose(); Assert(owner.IsDisposed, "pcm_dispose_idempotent");
        }

        private static async Task CheckSession()
        {
            string folder = Path.Combine(Path.GetTempPath(), "u01-session-check-" + Guid.NewGuid().ToString("N"));
            var history = new MemoryHistory(); var gateway = new FakeGateway(); var player = new FakePlayer();
            using (var session = new DesktopSessionController(gateway, player, new UnavailableMicrophoneCapture(), history, Path.Combine(folder, "settings.json")))
            {
                await session.InitializeAsync(CancellationToken.None);
                Assert(session.Snapshot.Phase == SessionPhase.Ready, "session_initialized");
                Assert(!session.BeginRecording(null).Accepted, "recording_explicitly_unavailable");
                session.SubmitText(new SubmitTextCommand("文字测试", "mao", null, false));
                await gateway.EmitText("完整文字", false);
                Assert(session.Snapshot.Messages.Last().DeliveryState == DeliveryState.Generated, "text_generation_does_not_complete_before_eof");
                gateway.Complete(); await Until(() => session.Snapshot.Messages.Last().DeliveryState == DeliveryState.Displayed);
                Assert(session.Snapshot.Messages.Last().DeliveryState == DeliveryState.Displayed, "text_displayed_after_complete_eof");
                session.SubmitText(new SubmitTextCommand("声音测试", "mao", null, true));
                Assert(gateway.LastSubmission.History.Any(row => row.Text == "完整文字"), "displayed_text_in_next_context");
                await gateway.EmitText("声音完整回复", true); gateway.Complete();
                await Until(() => player.Owned != null);
                Assert(session.Snapshot.Messages.Last().DeliveryState == DeliveryState.Generated, "generation_does_not_mark_played");
                player.Start(); player.Complete();
                Assert(session.Snapshot.Messages.Last().DeliveryState == DeliveryState.Played, "actual_playback_completed_marks_played");
                Assert(player.LastDisposed, "playback_owns_and_releases_pcm");
                session.SubmitText(new SubmitTextCommand("待停止", "mao", null, true));
                var old = gateway.Current;
                await gateway.EmitText("必须中断的回答", true); gateway.Complete(); await Until(() => player.Owned != null);
                player.Start(); gateway.Events.Clear(); player.Events = gateway.Events;
                session.Cancel(StopReason.User);
                Assert(gateway.Events.Count >= 2 && gateway.Events[0] == "local_stop" && gateway.Events[1] == "network_cancel", "local_stop_precedes_network_cancel");
                Assert(session.Snapshot.Messages.Last().DeliveryState == DeliveryState.Interrupted && player.Owned == null, "cancel_history_interrupted_and_pcm_released");
                await old.OnEvent(new TextDeltaEvent(old.Turn, 99, "迟到文字"), CancellationToken.None);
                Assert(!session.Snapshot.Messages.Last().Text.Contains("迟到"), "late_text_dropped");
                session.SubmitText(new SubmitTextCommand("下一轮", "mao", null, false));
                Assert(!gateway.LastSubmission.History.Any(row => row.Text == "必须中断的回答"), "interrupted_assistant_excluded_from_context");
                var failingAfterCancel = gateway.Current;
                session.Cancel(StopReason.User);
                failingAfterCancel.Done.TrySetException(new OperationCanceledException());
                await Task.Yield();
                Assert(session.Snapshot.Phase == SessionPhase.Ready && session.Snapshot.Operation == null,
                    "cancelled_cts_disposal_late_cancellation_catch_is_safe");
                gateway.DelayDownload = true;
                session.SubmitText(new SubmitTextCommand("下载中停止", "mao", null, true));
                await gateway.EmitText("下载迟到回复", true); gateway.Complete(); await Until(() => gateway.Downloading);
                session.Cancel(StopReason.User); var latePcm = await WavValidator.ValidateAsync(TinyWav(), Descriptor(gateway.Current.Turn, 4), CancellationToken.None);
                gateway.CompleteDownload(latePcm); await Until(() => latePcm.IsDisposed);
                Assert(latePcm.IsDisposed && player.Owned == null, "late_download_released_without_playback");
                var oldId = session.Snapshot.ConversationId;
                await session.DeleteConversationAsync(oldId, CancellationToken.None);
                Assert(session.Snapshot.ConversationId != oldId && session.Snapshot.Messages.Count == 0, "delete_creates_fresh_conversation");
                Assert(session.SetVolume(0).Accepted && player.Snapshot.Volume01 == 0, "volume_reaches_actual_player");
                Assert(session.Snapshot.Settings.SaveState == SettingsSaveState.Pending, "volume_save_is_debounced");
                await Until(() => session.Snapshot.Settings.SaveState == SettingsSaveState.Saved);
                Assert(File.Exists(Path.Combine(folder, "settings.json")), "settings_persisted");
                session.SetVolume(0.35f);
                history.PauseWrites = true;
                session.SubmitText(new SubmitTextCommand("关闭窗口前保存中断", "mao", null, false));
                session.Cancel(StopReason.WindowClosing);
                Task flush = session.FlushHistoryAsync();
                Assert(!flush.IsCompleted, "shutdown_flush_waits_for_queued_history");
                history.ResumeWrites();
                await flush;
                Assert(history.LastRecord != null && history.LastRecord.DeliveryState == DeliveryState.Interrupted,
                    "shutdown_flush_persists_terminal_record_after_prior_writes");
            }
            Assert(File.ReadAllText(Path.Combine(folder, "settings.json")).Contains("0.35"), "shutdown_flushes_last_volume_setting");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        private static async Task Until(Func<bool> condition)
        {
            var end = DateTime.UtcNow.AddSeconds(5);
            while (!condition()) { if (DateTime.UtcNow > end) throw new Exception("Check timed out."); await Task.Delay(1); }
        }
        private static void Assert(bool result, string name) { if (!result) throw new Exception("Failed: " + name); Passed.Add(name); }
        private static void Throws(Action action, string name) { try { action(); } catch { Passed.Add(name); return; } throw new Exception("Expected rejection: " + name); }
        private static async Task ThrowsAsync(Func<Task> action, string name) { try { await action(); } catch { Passed.Add(name); return; } throw new Exception("Expected rejection: " + name); }
        private static AudioReadyDescriptor Descriptor(TurnKey turn, long samples) => new AudioReadyDescriptor(turn, "/preview/unity/v1/turns/" + turn.TurnId.ToString("D") + "/audio.wav", 24000, 1, "pcm_s16le", samples);
        private static byte[] TinyWav()
        {
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            { w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(44); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(24000); w.Write(48000); w.Write((short)2); w.Write((short)16); w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(8); for (int i = 0; i < 4; i++) w.Write((short)1000); return stream.ToArray(); }
        }
        private sealed class FakeGateway : IConversationGateway
        {
            internal sealed class Call
            { internal Func<PreviewEvent, CancellationToken, Task> OnEvent; internal TurnKey Turn; internal TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(); }
            internal Call Current;
            internal TurnSubmission LastSubmission;
            internal bool DelayDownload, Downloading;
            internal readonly List<string> Events = new List<string>();
            private TaskCompletionSource<ValidatedPcm> _download;
            public Task<PreviewCapabilities> GetCapabilitiesAsync(CancellationToken token) => Task.FromResult(new PreviewCapabilities(PreviewProtocol.Name, new ServiceCapability(PreviewMode.Fixture, true, true), new SpeechCapability(SpeechMode.Fixture, true, true), new ServiceCapability(PreviewMode.Fixture, false, false), new[] { "mao" }, new[] { new VoiceOption("fixture-tone", "测试") }, new PreviewLimits(2000, 400, 6, 12000, 65536, 65536, 8388608, 120, 1048576, 30)));
            public Task ConsumeTurnAsync(TurnSubmission request, Func<PreviewEvent, CancellationToken, Task> onEvent, CancellationToken token)
            { LastSubmission = request; Current = new Call { OnEvent = onEvent, Turn = new TurnKey(request.Operation, Guid.NewGuid()) }; return Current.Done.Task; }
            internal async Task EmitText(string text, bool audio)
            {
                var c = Current;
                await c.OnEvent(new TurnAcceptedEvent(c.Turn, 0, PreviewMode.Fixture), CancellationToken.None);
                await c.OnEvent(new TextCompletedEvent(c.Turn, 1, text, Emotion.Happy), CancellationToken.None);
                if (audio) await c.OnEvent(new AudioReadyEvent(Descriptor(c.Turn, 4), 2), CancellationToken.None);
                else await c.OnEvent(new AudioSkippedEvent(c.Turn, 2, AudioSkipReason.UserDisabled), CancellationToken.None);
                await c.OnEvent(new GenerationCompletedEvent(c.Turn, 3), CancellationToken.None);
            }
            internal void Complete() => Current.Done.TrySetResult(true);
            public Task<ValidatedPcm> DownloadAudioAsync(AudioReadyDescriptor audio, CancellationToken token)
            { if (!DelayDownload) return WavValidator.ValidateAsync(TinyWav(), audio, token); _download = new TaskCompletionSource<ValidatedPcm>(); Downloading = true; return _download.Task; }
            internal void CompleteDownload(ValidatedPcm pcm) => _download.SetResult(pcm);
            public Task<CancelResult> CancelAsync(OperationKey operation, CancellationToken token) { Events.Add("network_cancel"); return Task.FromResult(new CancelResult(operation.RequestId, true)); }
            public Task<TranscriptionResult> TranscribeAsync(OperationKey operation, CapturedPcm recording, CancellationToken token) => throw new NotSupportedException();
            public void Dispose() { }
        }
        private sealed class FakePlayer : IAudioPlayer
        {
            internal ValidatedPcm Owned; internal bool LastDisposed; internal List<string> Events;
            private TurnKey _turn; private float _volume;
            public PlaybackSnapshot Snapshot => new PlaybackSnapshot(_turn, Owned != null, 0, Owned?.TotalSamples ?? 0, 24000, 0, _volume);
            public event Action<PlaybackStarted> PlaybackStarted;
            public event Action<PlaybackEnded> PlaybackEnded;
            public event Action<PlaybackProgress> ProgressChanged;
            public event Action<AudioLevelSample> PostVolumeLevel;
            public void Arm(OperationKey operation) { }
            public PlayResult Play(ValidatedPcm pcm, TurnKey turn) { Owned = pcm; _turn = turn; return new PlayResult(true, null); }
            internal void Start() => PlaybackStarted?.Invoke(new PlaybackStarted(_turn, Owned.TotalSamples, 24000, 0));
            internal void Complete() { long total = Owned.TotalSamples; Owned.Dispose(); LastDisposed = Owned.IsDisposed; Owned = null; PlaybackEnded?.Invoke(new PlaybackEnded(_turn, PlaybackEndReason.Completed, total, total, 24000, 0, null)); }
            public void Stop(StopReason reason) { Events?.Add("local_stop"); Owned?.Dispose(); Owned = null; }
            public void SetVolume(float value) { _volume = value; }
            public void Dispose() { Owned?.Dispose(); }
        }
        private sealed class MemoryHistory : IHistoryStore
        {
            private readonly Dictionary<Guid, ConversationHistory> _items = new Dictionary<Guid, ConversationHistory>();
            private ulong _generation = 1;
            internal bool PauseWrites;
            internal HistoryTurn LastRecord;
            private TaskCompletionSource<bool> _writeGate;
            internal void ResumeWrites() { PauseWrites = false; _writeGate?.TrySetResult(true); }
            public HistoryCapacitySnapshot Capacity => new HistoryCapacitySnapshot(_items.Count, 20, 80, HistoryRetentionPolicy.EvictOldestInactive, 1);
            public event Action<HistoryCapacitySnapshot> CapacityChanged;
            public Task<LocalResult<ConversationPage>> ListAsync(ConversationListQuery query, CancellationToken token) => Task.FromResult(new LocalResult<ConversationPage>(true, new ConversationPage(Array.Empty<ConversationSummary>(), null, 1), null));
            public Task<ConversationHistory> CreateAsync(Guid id, CancellationToken token) { var value = new ConversationHistory(1, id, new HistoryWriteToken(id, _generation++), "测试", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<HistoryTurn>()); _items[id] = value; return Task.FromResult(value); }
            public Task<ConversationHistory> LoadAsync(Guid id, CancellationToken token) => Task.FromResult(_items[id]);
            public async Task<HistoryWriteResult> AppendOrUpdateAsync(HistoryWriteToken token, HistoryTurn row, CancellationToken cancel)
            {
                if (PauseWrites)
                {
                    if (_writeGate == null) _writeGate = new TaskCompletionSource<bool>();
                    await _writeGate.Task;
                }
                if (!_items.TryGetValue(token.ConversationId, out var existing) || existing.WriteToken != token)
                    return new HistoryWriteResult(false, 1, new PreviewError("deleted", "已删除", false, row.Operation));
                LastRecord = row;
                return new HistoryWriteResult(true, 1, null);
            }
            public Task<LocalResult<HistoryExport>> ExportAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
            public Task DeleteConversationAsync(Guid id, CancellationToken token) { _items.Remove(id); return Task.CompletedTask; }
            public Task ClearAllAsync(CancellationToken token) { _items.Clear(); return Task.CompletedTask; }
            public void Dispose() { }
        }
    }
}
