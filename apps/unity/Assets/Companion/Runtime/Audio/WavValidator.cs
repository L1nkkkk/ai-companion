using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Contracts;

namespace AICompanion.Preview.Audio
{
    /// <summary>Complete, bounded RIFF parser. Input bytes are borrowed only during this call.</summary>
    public static class WavValidator
    {
        public static Task<ValidatedPcm> ValidateAsync(ReadOnlyMemory<byte> bytes, AudioReadyDescriptor descriptor, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try { return Task.FromResult(Parse(bytes.Span, descriptor, token)); }
            catch (InvalidDataException e)
            {
                e.Data["preview_error"] = new PreviewError("invalid_audio", "测试音频损坏或格式不受支持，请重新发送。", false, descriptor?.Turn.Operation);
                throw;
            }
        }

        private static ValidatedPcm Parse(ReadOnlySpan<byte> bytes, AudioReadyDescriptor d, CancellationToken token)
        {
            if (d == null || d.SampleRate != 24000 || d.Channels != 1 || d.Codec != "pcm_s16le" || d.TotalSamples < 1 || d.TotalSamples > 24000 * 120)
                throw Bad();
            if (bytes.Length < 44 || bytes.Length > 8 * 1024 * 1024 || !Tag(bytes, 0, "RIFF") || !Tag(bytes, 8, "WAVE") || U32(bytes, 4) != bytes.Length - 8) throw Bad();
            int cursor = 12, dataStart = -1, dataLength = 0;
            bool format = false;
            while (cursor < bytes.Length)
            {
                token.ThrowIfCancellationRequested();
                if (bytes.Length - cursor < 8) throw Bad();
                uint size = U32(bytes, cursor + 4);
                long next = (long)cursor + 8 + size + (size & 1);
                if (next > bytes.Length) throw Bad();
                int content = cursor + 8;
                if (Tag(bytes, cursor, "fmt "))
                {
                    if (format || (size != 16 && size != 18)) throw Bad();
                    if (U16(bytes, content) != 1 || U16(bytes, content + 2) != 1 || U32(bytes, content + 4) != 24000 || U32(bytes, content + 8) != 48000 || U16(bytes, content + 12) != 2 || U16(bytes, content + 14) != 16) throw Bad();
                    if (size == 18 && U16(bytes, content + 16) != 0) throw Bad();
                    format = true;
                }
                else if (Tag(bytes, cursor, "data"))
                {
                    if (dataStart >= 0 || size == 0 || (size & 1) != 0 || size / 2 != d.TotalSamples) throw Bad();
                    dataStart = content;
                    dataLength = checked((int)size);
                }
                cursor = (int)next;
            }
            if (!format || dataStart < 0 || cursor != bytes.Length) throw Bad();
            var samples = new short[dataLength / 2];
            try
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    samples[i] = unchecked((short)U16(bytes, dataStart + i * 2));
                }
                token.ThrowIfCancellationRequested();
                var pcm = new ValidatedPcm(samples, 24000, 1);
                if (token.IsCancellationRequested) { pcm.Dispose(); token.ThrowIfCancellationRequested(); }
                return pcm;
            }
            finally { Array.Clear(samples, 0, samples.Length); }
        }

        private static InvalidDataException Bad() => new InvalidDataException("Invalid preview WAV.");
        private static ushort U16(ReadOnlySpan<byte> b, int p) => (ushort)(b[p] | b[p + 1] << 8);
        private static uint U32(ReadOnlySpan<byte> b, int p) => (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24);
        private static bool Tag(ReadOnlySpan<byte> b, int p, string tag) => b[p] == tag[0] && b[p + 1] == tag[1] && b[p + 2] == tag[2] && b[p + 3] == tag[3];
    }
}
