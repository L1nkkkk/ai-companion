using System;
using System.Collections.Generic;

namespace AICompanion.Preview.Contracts
{
    public sealed class PlaybackSnapshot
    {
        public TurnKey? Turn { get; }
        public bool IsPlaying { get; }
        public long PlayedSamples { get; }
        public long TotalSamples { get; }
        public int SampleRate { get; }
        public long MonotonicTicks { get; }
        public float Volume01 { get; }

        public PlaybackSnapshot(TurnKey? turn, bool isPlaying, long playedSamples, long totalSamples, int sampleRate, long monotonicTicks, float volume01)
        {
            Turn = turn;
            IsPlaying = isPlaying;
            PlayedSamples = playedSamples;
            TotalSamples = totalSamples;
            SampleRate = sampleRate;
            MonotonicTicks = monotonicTicks;
            Volume01 = volume01;
        }
    }

    public sealed class PlaybackStarted
    {
        public TurnKey Turn { get; }
        public long TotalSamples { get; }
        public int SampleRate { get; }
        public long MonotonicTicks { get; }

        public PlaybackStarted(TurnKey turn, long totalSamples, int sampleRate, long monotonicTicks)
        {
            Turn = turn;
            TotalSamples = totalSamples;
            SampleRate = sampleRate;
            MonotonicTicks = monotonicTicks;
        }
    }

    public sealed class PlaybackEnded
    {
        public TurnKey Turn { get; }
        public PlaybackEndReason Reason { get; }
        public long PlayedSamples { get; }
        public long TotalSamples { get; }
        public int SampleRate { get; }
        public long MonotonicTicks { get; }
        public PreviewError Error { get; }

        public PlaybackEnded(TurnKey turn, PlaybackEndReason reason, long playedSamples, long totalSamples, int sampleRate, long monotonicTicks, PreviewError error)
        {
            Turn = turn;
            Reason = reason;
            PlayedSamples = playedSamples;
            TotalSamples = totalSamples;
            SampleRate = sampleRate;
            MonotonicTicks = monotonicTicks;
            Error = error;
        }
    }

    public sealed class PlaybackProgress
    {
        public TurnKey Turn { get; }
        public long PlayedSamples { get; }
        public long TotalSamples { get; }
        public int SampleRate { get; }
        public long MonotonicTicks { get; }

        public PlaybackProgress(TurnKey turn, long playedSamples, long totalSamples, int sampleRate, long monotonicTicks)
        {
            Turn = turn;
            PlayedSamples = playedSamples;
            TotalSamples = totalSamples;
            SampleRate = sampleRate;
            MonotonicTicks = monotonicTicks;
        }
    }

    public sealed class PlayResult
    {
        public bool Accepted { get; }
        public PreviewError Error { get; }

        public PlayResult(bool accepted, PreviewError error)
        {
            Accepted = accepted;
            Error = error;
        }
    }

    public sealed class CaptureStarted
    {
        public DraftKey Draft { get; }
        public string DeviceId { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public long MonotonicTicks { get; }
        public PreviewError Error { get; }

        public CaptureStarted(DraftKey draft, string deviceId, int sampleRate, int channels, long monotonicTicks, PreviewError error)
        {
            Draft = draft;
            DeviceId = deviceId;
            SampleRate = sampleRate;
            Channels = channels;
            MonotonicTicks = monotonicTicks;
            Error = error;
        }
        public bool Succeeded => Error == null;
    }

    public readonly struct AudioLevelSample
    {
        public TurnKey Turn { get; }
        public float Level01 { get; }
        public long SampleStart { get; }
        public int SampleCount { get; }
        public long MonotonicTicks { get; }

        public AudioLevelSample(TurnKey turn, float level01, long sampleStart, int sampleCount, long monotonicTicks)
        {
            Turn = turn;
            Level01 = level01;
            SampleStart = sampleStart;
            SampleCount = sampleCount;
            MonotonicTicks = monotonicTicks;
        }
    }

    public sealed class CaptureResult
    {
        public DraftKey Draft { get; }
        public CapturedPcm Recording { get; }
        public PreviewError Error { get; }
        public bool Succeeded => Recording != null;

        public CaptureResult(DraftKey draft, CapturedPcm recording, PreviewError error)
        {
            if ((recording == null) == (error == null)) throw new ArgumentException("Capture result needs either PCM or an error.");
            Draft = draft;
            Recording = recording;
            Error = error;
        }
    }
}
