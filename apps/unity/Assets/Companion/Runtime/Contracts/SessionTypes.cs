using System;
using System.Collections.Generic;

namespace AICompanion.Preview.Contracts
{
    public sealed class UpdateSettingsCommand
    {
        public bool AutoRead { get; }
        public string VoiceId { get; }
        public bool ContinuePlaybackOnFocusLost { get; }
        public string MicrophoneDeviceId { get; }

        public UpdateSettingsCommand(bool autoRead, string voiceId, bool continuePlaybackOnFocusLost, string microphoneDeviceId)
        {
            AutoRead = autoRead;
            VoiceId = voiceId;
            ContinuePlaybackOnFocusLost = continuePlaybackOnFocusLost;
            MicrophoneDeviceId = microphoneDeviceId;
        }
    }

    public sealed class ClientSettingsSnapshot
    {
        public int SchemaVersion { get; }
        public ulong Revision { get; }
        public float Volume01 { get; }
        public bool AutoRead { get; }
        public string VoiceId { get; }
        public bool ContinuePlaybackOnFocusLost { get; }
        public string MicrophoneDeviceId { get; }
        public string PlaybackDeviceLabel { get; }
        public bool CanSelectPlaybackDevice { get; }
        public SettingsSaveState SaveState { get; }
        public PreviewError SaveError { get; }

        public ClientSettingsSnapshot(int schemaVersion, ulong revision, float volume01, bool autoRead, string voiceId, bool continuePlaybackOnFocusLost, string microphoneDeviceId, string playbackDeviceLabel, bool canSelectPlaybackDevice, SettingsSaveState saveState, PreviewError saveError)
        {
            SchemaVersion = schemaVersion;
            Revision = revision;
            Volume01 = volume01;
            AutoRead = autoRead;
            VoiceId = voiceId;
            ContinuePlaybackOnFocusLost = continuePlaybackOnFocusLost;
            MicrophoneDeviceId = microphoneDeviceId;
            PlaybackDeviceLabel = ContractCopy.Required(playbackDeviceLabel, nameof(playbackDeviceLabel));
            CanSelectPlaybackDevice = canSelectPlaybackDevice;
            SaveState = saveState;
            SaveError = saveError;
        }
    }

    public sealed class VoiceOption
    {
        public string Id { get; }
        public string DisplayName { get; }

        public VoiceOption(string id, string displayName)
        {
            Id = ContractCopy.Required(id, nameof(id));
            DisplayName = ContractCopy.Required(displayName, nameof(displayName));
        }
    }

    public sealed class VoiceOptionsSnapshot
    {
        public ulong Revision { get; }
        public VoiceOptionsState State { get; }
        public IReadOnlyList<VoiceOption> Items { get; }
        public PreviewError Error { get; }

        public VoiceOptionsSnapshot(ulong revision, VoiceOptionsState state, IReadOnlyList<VoiceOption> items, PreviewError error)
        {
            Revision = revision;
            State = state;
            Items = ContractCopy.List(items, 128);
            Error = error;
        }
    }

    public sealed class MicrophoneDevice
    {
        public string Id { get; }
        public string DisplayName { get; }
        public bool IsDefault { get; }

        public MicrophoneDevice(string id, string displayName, bool isDefault)
        {
            Id = ContractCopy.Required(id, nameof(id));
            DisplayName = ContractCopy.Required(displayName, nameof(displayName));
            IsDefault = isDefault;
        }
    }

    public sealed class MicrophoneDeviceList
    {
        public IReadOnlyList<MicrophoneDevice> Items { get; }
        public DateTimeOffset EnumeratedAtUtc { get; }

        public MicrophoneDeviceList(IReadOnlyList<MicrophoneDevice> items, DateTimeOffset enumeratedAtUtc)
        {
            Items = ContractCopy.List(items, 64);
            EnumeratedAtUtc = enumeratedAtUtc;
        }
    }

    public sealed class SubmitTextCommand
    {
        public string Text { get; }
        public string CharacterId { get; }
        public string VoiceId { get; }
        public bool GenerateAudio { get; }

        public SubmitTextCommand(string text, string characterId, string voiceId, bool generateAudio)
        {
            Text = ContractCopy.Required(text, nameof(text));
            CharacterId = ContractCopy.Required(characterId, nameof(characterId));
            VoiceId = voiceId;
            GenerateAudio = generateAudio;
        }
    }

    public sealed class DraftUpdate
    {
        public DraftKey Draft { get; }
        public string Text { get; }

        public DraftUpdate(DraftKey draft, string text)
        {
            Draft = draft;
            Text = ContractCopy.Required(text, nameof(text));
        }
    }

    public sealed class SessionSnapshot
    {
        public SessionPhase Phase { get; }
        public OperationKey? Operation { get; }
        public TurnKey? Turn { get; }
        public PreviewMode Mode { get; }
        public SpeechMode TtsMode { get; }
        public PreviewMode AsrMode { get; }
        public PreviewError Error { get; }
        public Guid ConversationId { get; }
        public ulong DraftRevision { get; }
        public string DraftText { get; }
        public string FullText { get; }
        public string PartialText { get; }
        public PlaybackSnapshot Playback { get; }
        public ClientSettingsSnapshot Settings { get; }
        public VoiceOptionsSnapshot VoiceOptions { get; }
        public HistoryCapacitySnapshot HistoryCapacity { get; }
        public IReadOnlyList<HistoryTurn> Messages { get; }

        public SessionSnapshot(SessionPhase phase, OperationKey? operation, TurnKey? turn, PreviewMode mode, SpeechMode ttsMode, PreviewMode asrMode, PreviewError error, Guid conversationId, ulong draftRevision, string draftText, string fullText, string partialText, PlaybackSnapshot playback, ClientSettingsSnapshot settings, VoiceOptionsSnapshot voiceOptions, HistoryCapacitySnapshot historyCapacity, IReadOnlyList<HistoryTurn> messages)
        {
            Phase = phase;
            Operation = operation;
            Turn = turn;
            Mode = mode;
            TtsMode = ttsMode;
            AsrMode = asrMode;
            Error = error;
            ConversationId = conversationId;
            DraftRevision = draftRevision;
            DraftText = ContractCopy.Required(draftText, nameof(draftText));
            FullText = ContractCopy.Required(fullText, nameof(fullText));
            PartialText = ContractCopy.Required(partialText, nameof(partialText));
            Playback = playback ?? throw new ArgumentNullException(nameof(playback));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            VoiceOptions = voiceOptions ?? throw new ArgumentNullException(nameof(voiceOptions));
            HistoryCapacity = historyCapacity ?? throw new ArgumentNullException(nameof(historyCapacity));
            Messages = ContractCopy.List(messages, 80);
        }
    }

    public sealed class CommandReceipt
    {
        public bool Accepted { get; }
        public OperationKey? Operation { get; }
        public PreviewError Error { get; }

        public CommandReceipt(bool accepted, OperationKey? operation, PreviewError error)
        {
            if (accepted ? !operation.HasValue || error != null : error == null)
                throw new ArgumentException("Accepted commands need an operation; rejected commands need an error.");
            Accepted = accepted;
            Operation = operation;
            Error = error;
        }
    }
}
