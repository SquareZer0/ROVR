// Assets/Scripts/ROVR/AudioUtil.cs
// Small audio helpers for the voice pipeline. Plain C#, no Unity types.
using System;

namespace ROVR
{
    public static class AudioUtil
    {
        public static float Rms(float[] samples)
        {
            if (samples == null || samples.Length == 0) return 0f;
            double sum = 0;
            foreach (float s in samples) sum += (double)s * s;
            return (float)Math.Sqrt(sum / samples.Length);
        }

        // Averages interleaved channels down to one.
        public static float[] ToMono(float[] interleaved, int channels)
        {
            if (channels <= 1) return interleaved;
            var mono = new float[interleaved.Length / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
                mono[i] = sum / channels;
            }
            return mono;
        }

        // Linear-interpolation resampler; plenty for speech going into Whisper (headset mics are
        // typically 48 kHz, Whisper wants 16 kHz).
        public static float[] Resample(float[] src, int srcRate, int dstRate)
        {
            if (srcRate == dstRate || src.Length == 0) return src;

            int outLength = (int)((long)src.Length * dstRate / srcRate);
            var dst = new float[outLength];
            double step = (double)srcRate / dstRate;
            for (int i = 0; i < outLength; i++)
            {
                double pos = i * step;
                int i0 = (int)pos;
                int i1 = Math.Min(i0 + 1, src.Length - 1);
                float frac = (float)(pos - i0);
                dst[i] = src[i0] + (src[i1] - src[i0]) * frac;
            }
            return dst;
        }

        // 16-bit PCM mono WAV, the format whisper.cpp's server accepts directly.
        public static byte[] EncodeWav16(float[] samples, int sampleRate)
        {
            int dataBytes = samples.Length * 2;
            var wav = new byte[44 + dataBytes];

            WriteAscii(wav, 0, "RIFF");
            WriteInt32(wav, 4, 36 + dataBytes);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            WriteInt32(wav, 16, 16);              // fmt chunk size
            WriteInt16(wav, 20, 1);               // PCM
            WriteInt16(wav, 22, 1);               // mono
            WriteInt32(wav, 24, sampleRate);
            WriteInt32(wav, 28, sampleRate * 2);  // byte rate
            WriteInt16(wav, 32, 2);               // block align
            WriteInt16(wav, 34, 16);              // bits per sample
            WriteAscii(wav, 36, "data");
            WriteInt32(wav, 40, dataBytes);

            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Math.Max(-1f, Math.Min(1f, samples[i]));
                WriteInt16(wav, 44 + i * 2, (short)Math.Round(clamped * 32767f));
            }
            return wav;
        }

        static void WriteAscii(byte[] b, int at, string s) { for (int i = 0; i < s.Length; i++) b[at + i] = (byte)s[i]; }
        static void WriteInt32(byte[] b, int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
        static void WriteInt16(byte[] b, int at, short v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
    }
}
