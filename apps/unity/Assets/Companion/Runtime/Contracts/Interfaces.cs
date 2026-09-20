using System;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Preview.Contracts
{
    /// <summary>UI's only service boundary. Commands and public UI events run on the main thread.</summary>
    public interface ISessionController : IDisposable
    {
        SessionSnapshot Snapshot { get; }
        event Action<SessionSnapshot> SnapshotChanged;
        event Action<DraftUpdate> DraftUpdated;
        CommandReceipt SubmitText(SubmitTextCommand command);
        CommandReceipt BeginRecording(string deviceId);
        CommandReceipt EndRecording();
        LocalCommandResult SetVolume(float volume01);
        LocalCommandResult UpdateSettings(UpdateSettingsCommand command);
        Task<LocalResult<VoiceOptionsSnapshot>> RefreshVoiceOptionsAsync(CancellationToken cancellationToken);
        Task<LocalResult<MicrophoneDeviceList>> GetMicrophoneDevicesAsync(CancellationToken cancellationToken);
        Task<LocalResult<ConversationPage>> ListConversationsAsync(ConversationListQuery query, CancellationToken cancellationToken);
        Task<LocalResult<HistoryExport>> ExportConversationAsync(Guid conversationId, CancellationToken cancellationToken);
        void NotifyDraftEdited(string text);
        void Cancel(StopReason reason);
        Task NewConversationAsync(CancellationToken cancellationToken);
        Task SelectConversationAsync(Guid conversationId, CancellationToken cancellationToken);
        Task DeleteConversationAsync(Guid conversationId, CancellationToken cancellationToken);
        Task ClearHistoryAsync(CancellationToken cancellationToken);
    }

    public interface IConversationGateway : IDisposable
    {
        Task<PreviewCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken);
        Task ConsumeTurnAsync(TurnSubmission request, Func<PreviewEvent, CancellationToken, Task> onEvent, CancellationToken cancellationToken);
        Task<CancelResult> CancelAsync(OperationKey operation, CancellationToken cancellationToken);
        Task<ValidatedPcm> DownloadAudioAsync(AudioReadyDescriptor audio, CancellationToken cancellationToken);
        Task<TranscriptionResult> TranscribeAsync(OperationKey operation, CapturedPcm recording, CancellationToken cancellationToken);
    }

    public interface IAudioPlayer : IDisposable
    {
        PlaybackSnapshot Snapshot { get; }
        event Action<PlaybackStarted> PlaybackStarted;
        event Action<PlaybackEnded> PlaybackEnded;
        event Action<PlaybackProgress> ProgressChanged;
        event Action<AudioLevelSample> PostVolumeLevel;
        void Arm(OperationKey operation);
        PlayResult Play(ValidatedPcm pcm, TurnKey turn);
        void Stop(StopReason reason);
        void SetVolume(float volume01);
    }

    public interface IMicrophoneCapture : IDisposable
    {
        Task<LocalResult<MicrophoneDeviceList>> GetDevicesAsync(CancellationToken cancellationToken);
        Task<CaptureStarted> BeginAsync(DraftKey draft, string deviceId, CancellationToken cancellationToken);
        Task<CaptureResult> EndAsync(DraftKey draft, CancellationToken cancellationToken);
        void Abort();
    }

    public interface IAvatarPresenter : IDisposable
    {
        AvatarCapabilities Capabilities { get; }
        Task<AvatarLoadResult> LoadCharacterAsync(Guid loadRequestId, string characterId, CancellationToken cancellationToken);
        void BindOperation(OperationKey? operation);
        void SetPlaybackState(TurnKey turn, AvatarPlaybackState state);
        void SetAudioLevel(AudioLevelSample sample);
        AvatarActionResult ApplyExpression(AvatarActionContext context, Emotion emotion);
        AvatarActionResult ApplyMotion(AvatarActionContext context, string motionId);
        void Suspend();
        void Resume();
    }

    public interface IHistoryStore : IDisposable
    {
        HistoryCapacitySnapshot Capacity { get; }
        event Action<HistoryCapacitySnapshot> CapacityChanged;
        Task<LocalResult<ConversationPage>> ListAsync(ConversationListQuery query, CancellationToken cancellationToken);
        Task<ConversationHistory> CreateAsync(Guid conversationId, CancellationToken cancellationToken);
        Task<ConversationHistory> LoadAsync(Guid conversationId, CancellationToken cancellationToken);
        Task<HistoryWriteResult> AppendOrUpdateAsync(HistoryWriteToken writeToken, HistoryTurn turn, CancellationToken cancellationToken);
        Task<LocalResult<HistoryExport>> ExportAsync(Guid conversationId, CancellationToken cancellationToken);
        Task DeleteConversationAsync(Guid conversationId, CancellationToken cancellationToken);
        Task ClearAllAsync(CancellationToken cancellationToken);
    }
}
