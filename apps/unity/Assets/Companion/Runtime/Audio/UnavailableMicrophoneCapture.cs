using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Contracts;

namespace AICompanion.Preview.Audio
{
    /// <summary>The desktop foundation deliberately exposes no fabricated recording or ASR success.</summary>
    public sealed class UnavailableMicrophoneCapture : IMicrophoneCapture
    {
        private static PreviewError Error(OperationKey? operation = null) => new PreviewError("microphone_unavailable", "此桌面基础版尚未启用录音，请使用文字输入。", false, operation);
        public Task<LocalResult<MicrophoneDeviceList>> GetDevicesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new LocalResult<MicrophoneDeviceList>(false, null, Error()));
        }
        public Task<CaptureStarted> BeginAsync(DraftKey draft, string deviceId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new CaptureStarted(draft, deviceId, 0, 0, Stopwatch.GetTimestamp(), Error(draft.Operation)));
        }
        public Task<CaptureResult> EndAsync(DraftKey draft, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new CaptureResult(draft, null, Error(draft.Operation)));
        }
        public void Abort() { }
        public void Dispose() { }
    }
}
