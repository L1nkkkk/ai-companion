using System;
using System.Collections.Generic;

namespace AICompanion.Preview.Contracts
{
    public static class PreviewProtocol
    {
        public const string Name = "unity-preview/1";
    }

    public sealed class ServiceCapability
    {
        public PreviewMode Mode { get; }
        public bool Configured { get; }
        public bool Available { get; }

        public ServiceCapability(PreviewMode mode, bool configured, bool available)
        {
            Mode = mode;
            Configured = configured;
            Available = available;
        }
    }

    public sealed class SpeechCapability
    {
        public SpeechMode Mode { get; }
        public bool Configured { get; }
        public bool Available { get; }

        public SpeechCapability(SpeechMode mode, bool configured, bool available)
        {
            Mode = mode;
            Configured = configured;
            Available = available;
        }
    }

    public sealed class PreviewLimits
    {
        public int MaxInputCharacters { get; }
        public int MaxReplyCharacters { get; }
        public int MaxHistoryMessages { get; }
        public int MaxContextCharacters { get; }
        public int MaxRequestBytes { get; }
        public int MaxEventBytes { get; }
        public int MaxOutputWavBytes { get; }
        public int MaxOutputSeconds { get; }
        public int MaxCaptureWavBytes { get; }
        public int MaxCaptureSeconds { get; }

        public PreviewLimits(int maxInputCharacters, int maxReplyCharacters, int maxHistoryMessages, int maxContextCharacters, int maxRequestBytes, int maxEventBytes, int maxOutputWavBytes, int maxOutputSeconds, int maxCaptureWavBytes, int maxCaptureSeconds)
        {
            MaxInputCharacters = maxInputCharacters;
            MaxReplyCharacters = maxReplyCharacters;
            MaxHistoryMessages = maxHistoryMessages;
            MaxContextCharacters = maxContextCharacters;
            MaxRequestBytes = maxRequestBytes;
            MaxEventBytes = maxEventBytes;
            MaxOutputWavBytes = maxOutputWavBytes;
            MaxOutputSeconds = maxOutputSeconds;
            MaxCaptureWavBytes = maxCaptureWavBytes;
            MaxCaptureSeconds = maxCaptureSeconds;
        }
    }

    public sealed class PreviewCapabilities
    {
        public string Protocol { get; }
        public ServiceCapability Chat { get; }
        public SpeechCapability Tts { get; }
        public ServiceCapability Asr { get; }
        public IReadOnlyList<string> CharacterIds { get; }
        public IReadOnlyList<VoiceOption> Voices { get; }
        public PreviewLimits Limits { get; }

        public PreviewCapabilities(string protocol, ServiceCapability chat, SpeechCapability tts, ServiceCapability asr, IReadOnlyList<string> characterIds, IReadOnlyList<VoiceOption> voices, PreviewLimits limits)
        {
            Protocol = ContractCopy.Required(protocol, nameof(protocol));
            Chat = chat ?? throw new ArgumentNullException(nameof(chat));
            Tts = tts ?? throw new ArgumentNullException(nameof(tts));
            Asr = asr ?? throw new ArgumentNullException(nameof(asr));
            CharacterIds = ContractCopy.List(characterIds, 128);
            Voices = ContractCopy.List(voices, 128);
            Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        }
    }

    public sealed class HistoryContextItem
    {
        public MessageRole Role { get; }
        public string Text { get; }

        public HistoryContextItem(MessageRole role, string text)
        {
            Role = role;
            Text = ContractCopy.Required(text, nameof(text));
        }
    }

    public sealed class TurnSubmission
    {
        public OperationKey Operation { get; }
        public string CharacterId { get; }
        public string Text { get; }
        public IReadOnlyList<HistoryContextItem> History { get; }
        public bool GenerateAudio { get; }
        public string VoiceId { get; }

        public TurnSubmission(OperationKey operation, string characterId, string text, IReadOnlyList<HistoryContextItem> history, bool generateAudio, string voiceId)
        {
            Operation = operation;
            CharacterId = ContractCopy.Required(characterId, nameof(characterId));
            Text = ContractCopy.Required(text, nameof(text));
            History = ContractCopy.List(history, 6);
            GenerateAudio = generateAudio;
            VoiceId = voiceId;
        }
        public string Protocol => PreviewProtocol.Name;
    }

    public sealed class AudioReadyDescriptor
    {
        public TurnKey Turn { get; }
        public string Path { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public string Codec { get; }
        public long TotalSamples { get; }

        public AudioReadyDescriptor(TurnKey turn, string path, int sampleRate, int channels, string codec, long totalSamples)
        {
            Turn = turn;
            Path = ContractCopy.Required(path, nameof(path));
            SampleRate = sampleRate;
            Channels = channels;
            Codec = ContractCopy.Required(codec, nameof(codec));
            TotalSamples = totalSamples;
        }
    }

    public sealed class CancelResult
    {
        public Guid RequestId { get; }
        public bool Cancelled { get; }

        public CancelResult(Guid requestId, bool cancelled)
        {
            RequestId = requestId;
            Cancelled = cancelled;
        }
    }

    public sealed class TranscriptionResult
    {
        public OperationKey Operation { get; }
        public string Text { get; }
        public PreviewMode Mode { get; }

        public TranscriptionResult(OperationKey operation, string text, PreviewMode mode)
        {
            Operation = operation;
            Text = ContractCopy.Required(text, nameof(text));
            Mode = mode;
        }
    }

}
