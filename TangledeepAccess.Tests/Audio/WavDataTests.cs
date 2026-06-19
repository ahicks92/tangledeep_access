using System;
using System.IO;
using System.Text;
using TangledeepAccess.Audio;
using Xunit;

namespace TangledeepAccess.Tests.Audio {
    public class WavDataTests {
        private static byte[] BuildPcm16Wav(int channels, int sampleRate, short[] samples) {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms)) {
                int dataBytes = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);                       // PCM
                w.Write((short)channels);
                w.Write(sampleRate);
                w.Write(sampleRate * channels * 2);      // byte rate
                w.Write((short)(channels * 2));          // block align
                w.Write((short)16);                      // bits
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
                foreach (short s in samples) {
                    w.Write(s);
                }
                return ms.ToArray();
            }
        }

        [Fact]
        public void ParsesStereo16BitPcmToInterleavedFloats() {
            byte[] wav = BuildPcm16Wav(2, 44100, new short[] { 16384, -16384, 0, 32767 });

            WavData d = WavData.Parse(wav);

            Assert.Equal(2, d.Channels);
            Assert.Equal(44100, d.SampleRate);
            Assert.Equal(4, d.Samples.Length);
            Assert.Equal(0.5, d.Samples[0], 3);
            Assert.Equal(-0.5, d.Samples[1], 3);
            Assert.Equal(0.0, d.Samples[2], 3);
            Assert.Equal(1.0, d.Samples[3], 3); // 32767/32768 ≈ 1
        }

        [Fact]
        public void RejectsNonRiffBuffer() {
            Assert.Throws<ArgumentException>(() => WavData.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }));
        }
    }
}
