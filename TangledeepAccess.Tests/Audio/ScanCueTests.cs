using TangledeepAccess.Audio;
using Xunit;

namespace TangledeepAccess.Tests.Audio {
    public class ScanCueTests {
        private static double Freq(GrainPlacement p) {
            var adsr = Assert.IsType<AdsrGrain>(p.Grain);
            return Assert.IsType<TriangleGrain>(adsr.Inner).Frequency;
        }

        [Fact]
        public void PanMapsXAndClamps() {
            Assert.Equal(0.0, ScanCue.Pan(0), 9);
            Assert.Equal(-1.0, ScanCue.Pan(-8), 9);
            Assert.Equal(1.0, ScanCue.Pan(8), 9);
            Assert.Equal(-1.0, ScanCue.Pan(-50), 9); // clamps
            Assert.Equal(1.0, ScanCue.Pan(50), 9);
        }

        [Fact]
        public void SecondGrainPitchTracksY() {
            Assert.Equal(ScanCue.ReferenceFrequencyHz, ScanCue.SecondFrequencyHz(0), 6);
            Assert.True(ScanCue.SecondFrequencyHz(3) > ScanCue.ReferenceFrequencyHz);  // north = up
            Assert.True(ScanCue.SecondFrequencyHz(-3) < ScanCue.ReferenceFrequencyHz); // south = down
        }

        [Fact]
        public void BuildPlacesTwoGrainsSamePanReferenceThenSecond() {
            var p = ScanCue.Build(4, 3).Placements;
            Assert.Equal(2, p.Count);

            // Both panned by x; the reference plays first (start 0), the second after the gap.
            Assert.Equal(p[0].Pan, p[1].Pan);
            Assert.Equal(ScanCue.Pan(4), p[0].Pan, 9);
            Assert.Equal(0.0, p[0].Start, 9);
            Assert.Equal(ScanCue.GapSeconds, p[1].Start, 9);

            // First grain is the fixed reference; second is the y-pitched one.
            Assert.Equal(ScanCue.ReferenceFrequencyHz, Freq(p[0]), 6);
            Assert.Equal(ScanCue.SecondFrequencyHz(3), Freq(p[1]), 6);
        }

        private static void ChannelRms(float[] pcm, out double left, out double right) {
            double sl = 0.0, sr = 0.0;
            int frames = pcm.Length / 2;
            for (int i = 0; i < frames; i++) {
                sl += (double)pcm[2 * i] * pcm[2 * i];
                sr += (double)pcm[2 * i + 1] * pcm[2 * i + 1];
            }
            left = System.Math.Sqrt(sl / frames);
            right = System.Math.Sqrt(sr / frames);
        }

        [Fact]
        public void VerticallyAlignedEntityRendersDeadCenter() {
            // x = 0 (directly north/south) must put equal energy in both channels.
            double l, r;
            ChannelRms(ScanCue.Build(0, 4).RenderStereo(48000), out l, out r);
            Assert.Equal(l, r, 5);
        }

        [Fact]
        public void WestPansLeftEastPansRight() {
            double wl, wr, el, er;
            ChannelRms(ScanCue.Build(-6, 0).RenderStereo(48000), out wl, out wr);
            ChannelRms(ScanCue.Build(6, 0).RenderStereo(48000), out el, out er);
            Assert.True(wl > wr, "west should be louder on the left");
            Assert.True(er > el, "east should be louder on the right");
        }

        [Fact]
        public void AllGrainsUseTriangleRegardlessOfAlignment() {
            var aligned = ScanCue.Build(0, 3).Placements;   // x = 0: directly aligned
            var offAxis = ScanCue.Build(3, 3).Placements;   // x != 0

            // Triangle throughout now — the dead-center image is held by ITD, so no aligned special case.
            Assert.IsType<TriangleGrain>(((AdsrGrain)aligned[0].Grain).Inner);
            Assert.IsType<TriangleGrain>(((AdsrGrain)aligned[1].Grain).Inner);
            Assert.IsType<TriangleGrain>(((AdsrGrain)offAxis[0].Grain).Inner);
            Assert.IsType<TriangleGrain>(((AdsrGrain)offAxis[1].Grain).Inner);
        }

        [Fact]
        public void ToneLengthIsTheEnvelopeSum() {
            Assert.Equal(
                ScanCue.Attack + ScanCue.Decay + ScanCue.Sustain + ScanCue.Release,
                ScanCue.ToneSeconds,
                9);
        }
    }
}
