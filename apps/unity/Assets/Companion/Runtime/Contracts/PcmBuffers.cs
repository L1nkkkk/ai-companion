using System;
using System.Threading;

namespace AICompanion.Preview.Contracts
{
    /// <summary>Owned immutable PCM16 samples. Only Companion.Audio's validator may construct this type.</summary>
    public sealed class ValidatedPcm : IDisposable
    {
        private short[] _samples;
        public int SampleRate { get; }
        public int Channels { get; }
        public long TotalSamples { get; }
        public bool IsDisposed => Volatile.Read(ref _samples) == null;
        public ReadOnlyMemory<short> Samples
        {
            get
            {
                short[] value = Volatile.Read(ref _samples);
                if (value == null) throw new ObjectDisposedException(nameof(ValidatedPcm));
                return value;
            }
        }

        // This is a trusted boundary after full RIFF/path/size/identity validation, not a WAV parser.
        internal ValidatedPcm(ReadOnlyMemory<short> samples, int sampleRate, int channels)
        {
            if (sampleRate != 24000 || channels != 1 || samples.IsEmpty || samples.Length > 24000 * 120)
                throw new ArgumentException("Validated output must be nonempty 24 kHz mono PCM16, at most 120 seconds.");
            SampleRate = sampleRate;
            Channels = channels;
            TotalSamples = samples.Length;
            _samples = samples.ToArray();
        }

        // Play accepted: caller transfers ownership and stops using this instance.
        // Play rejected: caller retains ownership. No view remains usable after Dispose.
        public void Dispose()
        {
            short[] value = Interlocked.Exchange(ref _samples, null);
            if (value != null) Array.Clear(value, 0, value.Length);
        }
    }

    /// <summary>Owned interleaved float PCM at the actual device format, before upload resampling.</summary>
    public sealed class CapturedPcm : IDisposable
    {
        private float[] _samples;
        public int SampleRate { get; }
        public int Channels { get; }
        public long TotalFrames { get; }
        public bool IsDisposed => Volatile.Read(ref _samples) == null;
        public ReadOnlyMemory<float> InterleavedSamples
        {
            get
            {
                float[] value = Volatile.Read(ref _samples);
                if (value == null) throw new ObjectDisposedException(nameof(CapturedPcm));
                return value;
            }
        }

        public CapturedPcm(ReadOnlyMemory<float> interleavedSamples, int sampleRate, int channels)
        {
            if (sampleRate <= 0 || channels <= 0 || interleavedSamples.IsEmpty ||
                interleavedSamples.Length % channels != 0 ||
                interleavedSamples.Length / channels > (long)sampleRate * 30 ||
                interleavedSamples.Length > 16 * 1024 * 1024)
                throw new ArgumentException("Capture must contain whole frames within 30 seconds and 64 MiB.");
            SampleRate = sampleRate;
            Channels = channels;
            TotalFrames = interleavedSamples.Length / channels;
            _samples = interleavedSamples.ToArray();
        }

        // Session owns a successful CaptureResult. Gateway borrows until TranscribeAsync completes.
        public void Dispose()
        {
            float[] value = Interlocked.Exchange(ref _samples, null);
            if (value != null) Array.Clear(value, 0, value.Length);
        }
    }
}
