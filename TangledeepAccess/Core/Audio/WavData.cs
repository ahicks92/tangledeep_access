using System;

namespace TangledeepAccess.Audio {
    /// <summary>
    /// Decoded PCM from a little-endian RIFF/WAVE buffer: interleaved samples in [-1, 1] plus the
    /// channel count and sample rate. Pure (BCL only) so it lives in Core and is unit-tested
    /// off-engine; the engine layer turns the result into a Unity <c>AudioClip</c>. The mod ships
    /// 16-bit PCM WAVs; 8-bit PCM and 32-bit float are handled too, mono or stereo, for robustness.
    /// </summary>
    public sealed class WavData {
        /// <summary>Interleaved samples (L, R, L, R, … for stereo) in [-1, 1].</summary>
        public readonly float[] Samples;
        public readonly int Channels;
        public readonly int SampleRate;

        private WavData(float[] samples, int channels, int sampleRate) {
            Samples = samples;
            Channels = channels;
            SampleRate = sampleRate;
        }

        public static WavData Parse(byte[] bytes) {
            if (bytes == null || bytes.Length < 12) {
                throw new ArgumentException("Not a WAV: buffer too small");
            }
            if (Tag(bytes, 0) != "RIFF" || Tag(bytes, 8) != "WAVE") {
                throw new ArgumentException("Not a RIFF/WAVE buffer");
            }

            int format = 0, channels = 0, sampleRate = 0, bits = 0;
            int dataOffset = -1, dataLength = 0;

            // Walk the chunk list. Chunks are word-aligned: an odd size has a pad byte.
            int pos = 12;
            while (pos + 8 <= bytes.Length) {
                string id = Tag(bytes, pos);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                int body = pos + 8;
                if (id == "fmt " && body + 16 <= bytes.Length) {
                    format = BitConverter.ToUInt16(bytes, body);
                    channels = BitConverter.ToUInt16(bytes, body + 2);
                    sampleRate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToUInt16(bytes, body + 14);
                } else if (id == "data") {
                    dataOffset = body;
                    dataLength = size;
                }
                pos = body + size + (size & 1);
            }

            if (dataOffset < 0 || channels <= 0 || sampleRate <= 0) {
                throw new ArgumentException("WAV missing fmt/data chunk");
            }
            // Tolerate a size field that overruns the buffer (some encoders pad).
            if (dataOffset + dataLength > bytes.Length || dataLength <= 0) {
                dataLength = bytes.Length - dataOffset;
            }

            return new WavData(Decode(bytes, dataOffset, dataLength, format, bits), channels, sampleRate);
        }

        // format 1 = PCM int, 3 = IEEE float.
        private static float[] Decode(byte[] b, int off, int len, int format, int bits) {
            if (format == 3 && bits == 32) {
                int n = len / 4;
                var s = new float[n];
                for (int i = 0; i < n; i++) {
                    s[i] = BitConverter.ToSingle(b, off + i * 4);
                }
                return s;
            }
            if (format == 1 && bits == 16) {
                int n = len / 2;
                var s = new float[n];
                for (int i = 0; i < n; i++) {
                    s[i] = BitConverter.ToInt16(b, off + i * 2) / 32768f;
                }
                return s;
            }
            if (format == 1 && bits == 8) {
                // 8-bit WAV samples are unsigned: 0..255 with 128 as zero.
                var s = new float[len];
                for (int i = 0; i < len; i++) {
                    s[i] = (b[off + i] - 128) / 128f;
                }
                return s;
            }
            throw new NotSupportedException("Unsupported WAV format=" + format + " bits=" + bits);
        }

        private static string Tag(byte[] b, int off) {
            return new string(new[] { (char)b[off], (char)b[off + 1], (char)b[off + 2], (char)b[off + 3] });
        }
    }
}
