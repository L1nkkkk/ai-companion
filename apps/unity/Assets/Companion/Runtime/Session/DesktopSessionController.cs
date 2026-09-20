using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Contracts;

namespace AICompanion.Preview.Session
{
    /// <summary>Single UI state owner. Construct and issue public commands on the Unity main thread.</summary>
    public sealed class DesktopSessionController : ISessionController
    {
        private readonly IConversationGateway _gateway;
        private readonly IAudioPlayer _audio;
        private readonly IMicrophoneCapture _microphone;
        private readonly IHistoryStore _history;
        private readonly SynchronizationContext _main;
        private readonly string _settingsPath;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<HistoryTurn> _messages = new List<HistoryTurn>();
        private Task _pendingWrites = Task.CompletedTask;
        private readonly Dictionary<(OperationKey operation, MessageRole role), PreviewError> _historyWriteFailures =
            new Dictionary<(OperationKey operation, MessageRole role), PreviewError>();
        private readonly object _settingsTimerGate = new object();
        private CancellationTokenSource _settingsDelay;
        private ConversationHistory _conversation;
        private PreviewCapabilities _capabilities;
        private Operation _current;
        private uint _generation;
        private ulong _draftRevision, _settingsRevision, _voiceRevision, _navigationRevision;
        private bool _disposed, _navigating, _stopping;
        private SessionPhase _phase = SessionPhase.Offline;
        private PreviewError _error;
        private string _draft = "", _full = "", _partial = "";
        private ClientSettingsSnapshot _settings;
        private VoiceOptionsSnapshot _voices = new VoiceOptionsSnapshot(0, VoiceOptionsState.NotLoaded, Array.Empty<VoiceOption>(), null);
        public event Action<SessionSnapshot> SnapshotChanged;
        public event Action<DraftUpdate> DraftUpdated;
        public event Action<TurnKey, Emotion> SpeechExpression;
        public SessionSnapshot Snapshot => new SessionSnapshot(_phase, _current?.Key, _current?.Turn, _current?.Mode ?? _capabilities?.Chat.Mode ?? PreviewMode.Unknown,
            _current?.TtsMode ?? _capabilities?.Tts.Mode ?? SpeechMode.Unknown, _capabilities?.Asr.Mode ?? PreviewMode.Unknown, _error,
            _conversation?.ConversationId ?? Guid.Empty, _draftRevision, _draft, _full, _partial, _audio.Snapshot, _settings, _voices, _history.Capacity, _messages);

        public DesktopSessionController(IConversationGateway gateway, IAudioPlayer audio, IMicrophoneCapture microphone, IHistoryStore history, string settingsPath)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _microphone = microphone ?? throw new ArgumentNullException(nameof(microphone));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
            _main = SynchronizationContext.Current;
            _settings = ReadSettings();
            _audio.SetVolume(_settings.Volume01);
            _audio.PlaybackStarted += OnPlaybackStarted;
            _audio.PlaybackEnded += OnPlaybackEnded;
            _audio.ProgressChanged += OnPlaybackProgress;
            _history.CapacityChanged += OnCapacityChanged;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            try
            {
                var page = await _history.ListAsync(new ConversationListQuery(1, null), cancellationToken);
                if (page.Succeeded && page.Value.Items.Count > 0) await SelectConversationAsync(page.Value.Items[0].ConversationId, cancellationToken);
                else await NewConversationAsync(cancellationToken);
                await RefreshVoiceOptionsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e) { _error = SafeError(e, "history_unavailable", "无法读取本机历史，请检查存储权限。", null); _phase = SessionPhase.Error; Publish(); }
        }

        public CommandReceipt SubmitText(SubmitTextCommand command)
        {
            if (_disposed || _navigating || _conversation == null) return Reject("not_ready", "会话仍在准备，请稍后发送。");
            if (_capabilities == null || !_capabilities.Chat.Configured || !_capabilities.Chat.Available) return Reject("backend_unavailable", "后台尚未就绪，请先重试连接。");
            if (command == null || string.IsNullOrWhiteSpace(command.Text) || CountUnicode(command.Text) > 2000) return Reject("invalid_request", "请输入 1–2000 个字符。");
            if (!Contains(_capabilities.CharacterIds, command.CharacterId)) return Reject("invalid_character", "后台未公布此角色，请重试连接。");
            if (command.VoiceId != null && !ContainsVoice(command.VoiceId)) return Reject("invalid_voice", "所选音色暂不可用，请改选默认音色。");
            if (command.GenerateAudio && (!_capabilities.Tts.Configured || !_capabilities.Tts.Available)) return Reject("provider_unavailable", "朗读服务未就绪，可以关闭朗读后发送文字。");
            Cancel(StopReason.Replaced);
            if (!NextGeneration()) return Reject("generation_exhausted", "请求计数已满，请重启程序。");
            var key = new OperationKey(Guid.NewGuid(), _generation, _conversation.ConversationId);
            var context = BuildContext(command.Text);
            var op = new Operation(key, _conversation.WriteToken, command.GenerateAudio, _capabilities.Chat.Mode, _capabilities.Tts.Mode, _lifetime.Token);
            _current = op; _error = null; _phase = SessionPhase.Thinking; _full = _partial = ""; _draft = ""; _draftRevision++;
            _audio.Arm(key);
            SetRecord(op, MessageRole.User, command.Text, DeliveryState.Displayed, null);
            SetRecord(op, MessageRole.Assistant, "", DeliveryState.Generating, null);
            Publish();
            _ = RunTurnAsync(op, new TurnSubmission(key, command.CharacterId, command.Text, context, command.GenerateAudio, command.VoiceId));
            return new CommandReceipt(true, key, null);
        }

        private async Task RunTurnAsync(Operation op, TurnSubmission request)
        {
            ValidatedPcm pcm = null;
            try
            {
                await _pendingWrites;
                if (!Current(op)) return;
                await _gateway.ConsumeTurnAsync(request, (item, token) => MainAsync(() => Receive(op, item)), op.Cancel.Token);
                if (!Current(op)) return;
                if (!op.GenerationCompleted) throw new InvalidDataException("Missing complete generation.");
                if (!op.GenerateAudio)
                {
                    SetRecord(op, MessageRole.Assistant, _full, DeliveryState.Displayed, null);
                    _phase = SessionPhase.Ready; op.Terminal = true; op.Cancel.Dispose(); Publish(); return;
                }
                if (op.Audio == null) throw new InvalidDataException("Missing validated audio descriptor.");
                _phase = SessionPhase.PreparingSpeech; Publish();
                pcm = await _gateway.DownloadAudioAsync(op.Audio, op.Cancel.Token);
                if (!Current(op)) return;
                var result = _audio.Play(pcm, op.Audio.Turn);
                if (!result.Accepted) { Fail(op, result.Error ?? Error("playback_unavailable", "无法播放音频。", op.Key)); return; }
                pcm = null; // Only accepted playback transfers ownership.
                Publish();
            }
            catch (OperationCanceledException)
            {
                if (Current(op) && !op.Cancel.IsCancellationRequested) Fail(op, Error("provider_timeout", "请求等待超时，请手动重新发送。", op.Key, true));
            }
            catch (Exception e)
            {
                if (Current(op)) Fail(op, SafeError(e, "protocol_error", "后台响应无效或不完整，请重试连接后手动发送。", op.Key));
            }
            finally { pcm?.Dispose(); }
        }

        private void Receive(Operation op, PreviewEvent e)
        {
            if (!Current(op)) return;
            if (e.Operation != op.Key || (op.Turn.HasValue && e.Turn != op.Turn.Value)) { Fail(op, Error("protocol_error", "收到不属于当前请求的响应。", op.Key)); return; }
            op.Turn = e.Turn;
            if (e is TurnAcceptedEvent accepted) op.Mode = accepted.Mode;
            else if (e is TextDeltaEvent delta) { _partial += delta.Text; UpdateVisible(op, DeliveryState.Generating); }
            else if (e is TextCompletedEvent completed)
            {
                _full = completed.Text; _partial = ""; op.Emotion = completed.Emotion;
                SetRecord(op, MessageRole.Assistant, _full, DeliveryState.Generated, null);
                _phase = op.GenerateAudio ? SessionPhase.PreparingSpeech : SessionPhase.Thinking;
            }
            else if (e is AudioReadyEvent audio) { op.Audio = audio.Audio; op.TotalSamples = audio.Audio.TotalSamples; }
            else if (e is GenerationCompletedEvent) op.GenerationCompleted = true;
            else if (e is TurnErrorEvent error) { Fail(op, error.Error); return; }
            else if (e is TurnCancelledEvent) { Cancel(StopReason.Failure); return; }
            Publish();
        }

        public void Cancel(StopReason reason)
        {
            if (_disposed) return;
            var op = _current;
            _stopping = true;
            // Synchronous local mute/zero always precedes network cancellation.
            _audio.Stop(reason);
            _microphone.Abort();
            _stopping = false;
            if (op != null && !op.Terminal)
            {
                var playback = _audio.Snapshot;
                if (playback.Turn.HasValue && playback.Turn.Value.Operation == op.Key) op.PlayedSamples = playback.PlayedSamples;
                SetRecord(op, MessageRole.Assistant, _full.Length > 0 ? _full : _partial, DeliveryState.Interrupted, null);
                op.Terminal = true;
                op.Cancel.Cancel();
                op.Cancel.Dispose();
                _ = CancelRemoteAsync(op.Key);
            }
            _current = null; NextGeneration(); _partial = "";
            _phase = _capabilities?.Chat.Available == true ? SessionPhase.Ready : SessionPhase.Offline;
            Publish();
        }

        private async Task CancelRemoteAsync(OperationKey key)
        {
            // Cancellation is idempotent but is never retried automatically.
            try { await _gateway.CancelAsync(key, _lifetime.Token); }
            catch { /* Already locally silent. Backend operation also observes disconnected consumption. */ }
        }
        private void Fail(Operation op, PreviewError error)
        {
            if (!Current(op)) return;
            _stopping = true; _audio.Stop(StopReason.Failure); _microphone.Abort(); _stopping = false;
            var playback = _audio.Snapshot;
            if (playback.Turn.HasValue && playback.Turn.Value.Operation == op.Key) op.PlayedSamples = playback.PlayedSamples;
            _error = error; _phase = SessionPhase.Error;
            SetRecord(op, MessageRole.Assistant, _full.Length > 0 ? _full : _partial, DeliveryState.Failed, error);
            op.Terminal = true; op.Cancel.Cancel(); op.Cancel.Dispose(); _ = CancelRemoteAsync(op.Key); Publish();
        }

        private void OnPlaybackStarted(PlaybackStarted e)
        {
            if (_current == null || !Current(_current) || _current.Turn != e.Turn) return;
            _phase = SessionPhase.Speaking;
            SpeechExpression?.Invoke(e.Turn, _current.Emotion); Publish();
        }
        private void OnPlaybackProgress(PlaybackProgress e)
        {
            if (_current == null || !Current(_current) || _current.Turn != e.Turn) return;
            _current.PlayedSamples = e.PlayedSamples; _current.TotalSamples = e.TotalSamples; Publish();
        }
        private void OnPlaybackEnded(PlaybackEnded e)
        {
            if (_stopping || _current == null || !Current(_current) || _current.Turn != e.Turn) return;
            var op = _current; op.PlayedSamples = e.PlayedSamples; op.TotalSamples = e.TotalSamples;
            if (e.Reason == PlaybackEndReason.Completed && e.PlayedSamples == e.TotalSamples && e.TotalSamples > 0 && op.GenerationCompleted)
            {
                SetRecord(op, MessageRole.Assistant, _full, DeliveryState.Played, null);
                op.Terminal = true; op.Cancel.Dispose(); _phase = SessionPhase.Ready; Publish();
            }
            else if (e.Reason == PlaybackEndReason.Stopped) Cancel(StopReason.User);
            else Fail(op, e.Error ?? Error("playback_interrupted", "音频未完整播放。", op.Key, true));
        }

        private void SetRecord(Operation op, MessageRole role, string text, DeliveryState state, PreviewError error)
        {
            var record = new HistoryTurn(op.Key, op.Turn?.TurnId, role, text,
                role == MessageRole.User || !op.GenerateAudio ? DeliveryKind.Text : DeliveryKind.Audio, state,
                role == MessageRole.User ? 0 : op.PlayedSamples, role == MessageRole.User ? 0 : op.TotalSamples,
                DateTimeOffset.UtcNow, op.Mode, op.TtsMode, error);
            ReplaceVisible(record);
            var previous = _pendingWrites;
            _pendingWrites = WriteAfterAsync(previous, op.WriteToken, record);
        }
        private async Task WriteAfterAsync(Task previous, HistoryWriteToken token, HistoryTurn record)
        {
            try
            {
                await previous;
                var result = await _history.AppendOrUpdateAsync(token, record, CancellationToken.None);
                var key = (record.Operation, record.Role);
                if (result.Accepted) _historyWriteFailures.Remove(key);
                else
                {
                    var error = result.Error ?? Error("history_write_failed", "历史记录保存失败。", record.Operation);
                    _historyWriteFailures[key] = error;
                    if (!_disposed && _conversation?.WriteToken == token) { _error = error; Publish(); }
                }
            }
            catch (Exception e)
            {
                var error = SafeError(e, "history_write_failed", "历史记录保存失败，请检查磁盘空间。", record.Operation);
                _historyWriteFailures[(record.Operation, record.Role)] = error;
                if (!_disposed && _conversation?.WriteToken == token)
                { _error = error; Publish(); }
            }
        }
        private void UpdateVisible(Operation op, DeliveryState state)
        {
            ReplaceVisible(new HistoryTurn(op.Key, op.Turn?.TurnId, MessageRole.Assistant, _partial, op.GenerateAudio ? DeliveryKind.Audio : DeliveryKind.Text,
                state, op.PlayedSamples, op.TotalSamples, DateTimeOffset.UtcNow, op.Mode, op.TtsMode, null));
        }
        private void ReplaceVisible(HistoryTurn record)
        {
            if (_conversation == null || record.Operation.ConversationId != _conversation.ConversationId) return;
            int index = _messages.FindIndex(row => row.Operation == record.Operation && row.Role == record.Role);
            if (index >= 0) _messages[index] = record;
            else
            {
                _messages.Add(record);
                while (_messages.Count > 80)
                {
                    OperationKey oldest = _messages[0].Operation;
                    _messages.RemoveAll(row => row.Operation == oldest);
                }
            }
        }

        private IReadOnlyList<HistoryContextItem> BuildContext(string currentText)
        {
            var selected = new List<HistoryContextItem>(); int characters = CountUnicode(currentText);
            for (int i = _messages.Count - 1; i >= 0 && selected.Count < 6; i--)
            {
                var row = _messages[i];
                if (row.Text.Length == 0) continue;
                if (row.Role == MessageRole.Assistant && !((row.DeliveryKind == DeliveryKind.Audio && row.DeliveryState == DeliveryState.Played) || (row.DeliveryKind == DeliveryKind.Text && row.DeliveryState == DeliveryState.Displayed))) continue;
                int count = CountUnicode(row.Text); if (count > 2000 || characters + count > 12000) break;
                selected.Add(new HistoryContextItem(row.Role, row.Text)); characters += count;
            }
            selected.Reverse(); return selected;
        }

        public async Task<LocalResult<VoiceOptionsSnapshot>> RefreshVoiceOptionsAsync(CancellationToken cancellationToken)
        {
            _voices = new VoiceOptionsSnapshot(++_voiceRevision, VoiceOptionsState.Loading, _voices.Items, null); Publish();
            try
            {
                var caps = await _gateway.GetCapabilitiesAsync(cancellationToken);
                if (_disposed) throw new OperationCanceledException();
                _capabilities = caps;
                _voices = new VoiceOptionsSnapshot(++_voiceRevision, caps.Tts.Available ? VoiceOptionsState.Ready : VoiceOptionsState.Unavailable, caps.Voices, null);
                if (_current == null || _current.Terminal) { _phase = caps.Chat.Available ? SessionPhase.Ready : SessionPhase.Offline; _error = caps.Chat.Available ? null : Error("provider_unavailable", "后台已连接，但对话服务未配置。", null); }
                Publish(); return new LocalResult<VoiceOptionsSnapshot>(true, _voices, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                var error = SafeError(e, "backend_unavailable", "后台未连接，请启动后重试。", null);
                _voices = new VoiceOptionsSnapshot(++_voiceRevision, VoiceOptionsState.Failed, _voices.Items, error);
                if (_current == null || _current.Terminal) { _capabilities = null; _phase = SessionPhase.Offline; _error = error; }
                Publish(); return new LocalResult<VoiceOptionsSnapshot>(false, null, error);
            }
        }

        public CommandReceipt BeginRecording(string deviceId)
        {
            _draftRevision++;
            return Reject("microphone_unavailable", "此桌面基础版尚未启用录音，请使用文字输入。");
        }
        public CommandReceipt EndRecording() => Reject("microphone_unavailable", "此桌面基础版尚未启用录音。");
        public Task<LocalResult<MicrophoneDeviceList>> GetMicrophoneDevicesAsync(CancellationToken token) => _microphone.GetDevicesAsync(token);
        public void NotifyDraftEdited(string text) { _draft = text ?? ""; _draftRevision++; Publish(); }
        public Task<LocalResult<ConversationPage>> ListConversationsAsync(ConversationListQuery query, CancellationToken token) => _history.ListAsync(query, token);
        public async Task<LocalResult<HistoryExport>> ExportConversationAsync(Guid id, CancellationToken token)
        {
            // A successful store read cannot make a failed terminal write durable. Keep
            // failures separate from the UI error, which another command may clear.
            Task pending;
            do { pending = _pendingWrites; await pending; token.ThrowIfCancellationRequested(); }
            while (!ReferenceEquals(pending, _pendingWrites));
            if (_disposed) throw new ObjectDisposedException(nameof(DesktopSessionController));
            foreach (var failure in _historyWriteFailures)
                if (failure.Key.operation.ConversationId == id)
                    return new LocalResult<HistoryExport>(false, null, Error("history_write_failed",
                        "会话尚有未保存的记录，暂时无法导出。请检查本机存储后重试。", failure.Key.operation, true));
            return await _history.ExportAsync(id, token);
        }
        /// <summary>
        /// Await after Cancel(WindowClosing), before Dispose. Drains every already queued history
        /// update without blocking Unity's synchronization context; Composition owns the quit deadline.
        /// Persistence errors remain explicit in Snapshot.Error and are never reported as saved.
        /// </summary>
        public Task FlushHistoryAsync() => _pendingWrites;

        public Task NewConversationAsync(CancellationToken token) => NavigateAsync(Guid.NewGuid(), true, StopReason.NewConversation, token);
        public Task SelectConversationAsync(Guid id, CancellationToken token) => NavigateAsync(id, false, StopReason.SelectConversation, token);
        private async Task NavigateAsync(Guid id, bool create, StopReason reason, CancellationToken token)
        {
            if (id == Guid.Empty) throw new ArgumentException("Invalid conversation.");
            Cancel(reason); ulong revision = ++_navigationRevision; _navigating = true; _draftRevision++; _draft = ""; Publish();
            try
            {
                await _pendingWrites;
                var conversation = create ? await _history.CreateAsync(id, token) : await _history.LoadAsync(id, token);
                if (_disposed || revision != _navigationRevision) return;
                _conversation = conversation; _messages.Clear(); _messages.AddRange(conversation.Records);
                _full = _partial = ""; _error = null;
                _phase = _capabilities?.Chat.Available == true ? SessionPhase.Ready : SessionPhase.Offline;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) { if (revision == _navigationRevision) { _error = SafeError(e, "history_unavailable", "无法打开所选会话。", null); _phase = SessionPhase.Error; } }
            finally { if (revision == _navigationRevision) { _navigating = false; Publish(); } }
        }
        public async Task DeleteConversationAsync(Guid id, CancellationToken token)
        {
            bool active = _conversation?.ConversationId == id;
            if (active) { Cancel(StopReason.DeleteHistory); _navigating = true; _navigationRevision++; }
            try
            {
                await _pendingWrites; await _history.DeleteConversationAsync(id, token);
                RemoveHistoryWriteFailures(id);
                if (active) { _conversation = null; _messages.Clear(); await NewConversationAsync(token); }
                Publish();
            }
            finally { if (active) _navigating = false; }
        }
        public async Task ClearHistoryAsync(CancellationToken token)
        {
            Cancel(StopReason.DeleteHistory); _navigating = true; _navigationRevision++;
            try
            {
                await _pendingWrites; await _history.ClearAllAsync(token);
                _historyWriteFailures.Clear();
                _conversation = null; _messages.Clear(); await NewConversationAsync(token);
            }
            finally { _navigating = false; Publish(); }
        }

        private void RemoveHistoryWriteFailures(Guid conversationId)
        {
            var removed = new List<(OperationKey operation, MessageRole role)>();
            foreach (var key in _historyWriteFailures.Keys)
                if (key.operation.ConversationId == conversationId) removed.Add(key);
            foreach (var key in removed) _historyWriteFailures.Remove(key);
        }

        public LocalCommandResult SetVolume(float volume)
        {
            if (float.IsNaN(volume) || float.IsInfinity(volume) || volume < 0 || volume > 1) return new LocalCommandResult(false, Error("invalid_volume", "音量必须介于 0 和 1 之间。", null));
            _audio.SetVolume(volume);
            _settings = Settings(volume, _settings.AutoRead, _settings.VoiceId, _settings.ContinuePlaybackOnFocusLost, _settings.MicrophoneDeviceId, SettingsSaveState.Pending, null);
            CancelSettingsDelay();
            lock (_settingsTimerGate)
            {
                _settingsDelay = new CancellationTokenSource();
                _ = SaveSettingsAfterIdleAsync(_settingsDelay, _settings.Revision);
            }
            Publish();
            return new LocalCommandResult(true, null);
        }
        public LocalCommandResult UpdateSettings(UpdateSettingsCommand command)
        {
            if (command == null || (command.VoiceId != null && command.VoiceId.Length > 64) || (command.MicrophoneDeviceId != null && command.MicrophoneDeviceId.Length > 512))
                return new LocalCommandResult(false, Error("invalid_settings", "设置值无效。", null));
            _settings = Settings(_settings.Volume01, command.AutoRead, command.VoiceId, command.ContinuePlaybackOnFocusLost, command.MicrophoneDeviceId, SettingsSaveState.Pending, null);
            CancelSettingsDelay();
            SaveSettings(); return new LocalCommandResult(_settings.SaveError == null, _settings.SaveError);
        }
        private async Task SaveSettingsAfterIdleAsync(CancellationTokenSource delay, ulong revision)
        {
            try
            {
                await Task.Delay(250, delay.Token);
                if (!_disposed && _settings.Revision == revision) SaveSettings();
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_settingsTimerGate)
                {
                    if (ReferenceEquals(_settingsDelay, delay)) _settingsDelay = null;
                    delay.Dispose();
                }
            }
        }
        private void CancelSettingsDelay()
        {
            lock (_settingsTimerGate)
            {
                var previous = _settingsDelay; _settingsDelay = null;
                if (previous != null) previous.Cancel();
            }
            // The awaiting task owns disposal, including callbacks already queued on the main thread.
        }
        private ClientSettingsSnapshot Settings(float volume, bool read, string voice, bool focus, string mic, SettingsSaveState state, PreviewError error) =>
            new ClientSettingsSnapshot(1, ++_settingsRevision, volume, read, voice, focus, mic, "Windows 默认播放设备", false, state, error);
        private ClientSettingsSnapshot ReadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    if (new FileInfo(_settingsPath).Length > 65536) throw new InvalidDataException();
                    using (var stream = File.OpenRead(_settingsPath))
                    {
                        var data = (SavedSettings)new DataContractJsonSerializer(typeof(SavedSettings)).ReadObject(stream);
                        if (data.Schema != 1 || float.IsNaN(data.Volume) || data.Volume < 0 || data.Volume > 1 || (data.Voice != null && data.Voice.Length > 64) || (data.Microphone != null && data.Microphone.Length > 512)) throw new InvalidDataException();
                        return Settings(data.Volume, data.AutoRead, data.Voice, data.ContinueOnFocusLost, data.Microphone, SettingsSaveState.Saved, null);
                    }
                }
            }
            catch { return Settings(0.8f, true, null, false, null, SettingsSaveState.Failed, Error("settings_recovered", "设置文件不可用，已恢复默认值。", null)); }
            return Settings(0.8f, true, null, false, null, SettingsSaveState.Saved, null);
        }
        private void SaveSettings()
        {
            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(_settingsPath)); Directory.CreateDirectory(directory);
                var data = new SavedSettings { Schema = 1, Volume = _settings.Volume01, AutoRead = _settings.AutoRead, Voice = _settings.VoiceId, ContinueOnFocusLost = _settings.ContinuePlaybackOnFocusLost, Microphone = _settings.MicrophoneDeviceId };
                string temporary = _settingsPath + ".tmp";
                using (var stream = File.Create(temporary)) { new DataContractJsonSerializer(typeof(SavedSettings)).WriteObject(stream, data); stream.Flush(true); }
                ReplaceSettingsAtomically(temporary, _settingsPath);
                _settings = Settings(_settings.Volume01, _settings.AutoRead, _settings.VoiceId, _settings.ContinuePlaybackOnFocusLost, _settings.MicrophoneDeviceId, SettingsSaveState.Saved, null);
            }
            catch { _settings = Settings(_settings.Volume01, _settings.AutoRead, _settings.VoiceId, _settings.ContinuePlaybackOnFocusLost, _settings.MicrophoneDeviceId, SettingsSaveState.Failed, Error("settings_save_failed", "设置尚未保存，请检查本机存储权限。", null)); }
            Publish();
        }
        private static void ReplaceSettingsAtomically(string temporary, string destination)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // Unity Mono can reject File.Replace even for an ordinary writable file on Windows.
                // Both paths are in the same directory. Never delete the old settings before replacement.
                if (!MoveFileEx(temporary, destination, 0x1 | 0x8))
                    throw new IOException("Atomic settings replacement failed.");
                return;
            }
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

        private void OnCapacityChanged(HistoryCapacitySnapshot capacity) { _ = MainAsync(Publish); }
        private Task MainAsync(Action action)
        {
            if (_disposed) return Task.CompletedTask;
            if (_main == null || SynchronizationContext.Current == _main) { action(); return Task.CompletedTask; }
            var completion = new TaskCompletionSource<bool>();
            _main.Post(_ => { try { if (!_disposed) action(); completion.TrySetResult(true); } catch (Exception e) { completion.TrySetException(e); } }, null);
            return completion.Task;
        }
        private bool Current(Operation op) => !_disposed && ReferenceEquals(op, _current) && !op.Terminal && !op.Cancel.IsCancellationRequested && _conversation?.WriteToken == op.WriteToken;
        private bool NextGeneration() { if (_generation == uint.MaxValue) return false; _generation++; return true; }
        private void Publish() { if (!_disposed) SnapshotChanged?.Invoke(Snapshot); }
        private CommandReceipt Reject(string code, string message) { var e = Error(code, message, null); _error = e; Publish(); return new CommandReceipt(false, null, e); }
        private static PreviewError Error(string code, string message, OperationKey? key, bool retry = false) => new PreviewError(code, message, retry, key);
        private static PreviewError SafeError(Exception e, string code, string message, OperationKey? operation)
        {
            if (e.Data["preview_error"] is PreviewError declared) return new PreviewError(declared.Code, declared.Message, declared.Retryable, operation ?? declared.Operation);
            if (e is OperationCanceledException) return Error("provider_timeout", "请求等待超时，请手动重试。", operation, true);
            return Error(code, message, operation, true);
        }
        private static bool Contains(IReadOnlyList<string> list, string value) { foreach (var item in list) if (item == value) return true; return false; }
        private bool ContainsVoice(string id) { foreach (var voice in _capabilities.Voices) if (voice.Id == id) return true; return false; }
        private static int CountUnicode(string value)
        {
            int count = 0;
            for (int i = 0; i < value.Length; i++) { if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) i++; else if (char.IsSurrogate(value[i])) return int.MaxValue; count++; }
            return count;
        }
        public void Dispose()
        {
            if (_disposed) return;
            Cancel(StopReason.WindowClosing);
            CancelSettingsDelay();
            if (_settings.SaveState == SettingsSaveState.Pending) SaveSettings();
            _disposed = true; _lifetime.Cancel();
            _audio.PlaybackStarted -= OnPlaybackStarted; _audio.PlaybackEnded -= OnPlaybackEnded; _audio.ProgressChanged -= OnPlaybackProgress; _history.CapacityChanged -= OnCapacityChanged;
            _audio.Dispose(); _microphone.Dispose(); _gateway.Dispose();
            // Do not dispose History while queued terminal writes still own it.
            _ = DisposeHistoryAsync();
        }
        private async Task DisposeHistoryAsync() { await _pendingWrites; _history.Dispose(); _lifetime.Dispose(); }
        [DataContract]
        private sealed class SavedSettings
        {
            [DataMember(Name = "schema", IsRequired = true)] public int Schema;
            [DataMember(Name = "volume", IsRequired = true)] public float Volume;
            [DataMember(Name = "auto_read", IsRequired = true)] public bool AutoRead;
            [DataMember(Name = "voice")] public string Voice;
            [DataMember(Name = "continue_on_focus_lost", IsRequired = true)] public bool ContinueOnFocusLost;
            [DataMember(Name = "microphone")] public string Microphone;
        }
        private sealed class Operation
        {
            internal readonly OperationKey Key;
            internal readonly HistoryWriteToken WriteToken;
            internal readonly bool GenerateAudio;
            internal readonly SpeechMode TtsMode;
            internal readonly CancellationTokenSource Cancel;
            internal TurnKey? Turn;
            internal PreviewMode Mode;
            internal Emotion Emotion = Emotion.Neutral;
            internal bool GenerationCompleted, Terminal;
            internal AudioReadyDescriptor Audio;
            internal long PlayedSamples, TotalSamples;
            internal Operation(OperationKey key, HistoryWriteToken token, bool audio, PreviewMode mode, SpeechMode tts, CancellationToken lifetime)
            { Key = key; WriteToken = token; GenerateAudio = audio; Mode = mode; TtsMode = tts; Cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime); }
        }
    }
}
