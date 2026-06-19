using TangledeepAccess.Audio;
using Xunit;

namespace TangledeepAccess.Tests.Audio {
    public class GrainTimelineTests {
        [Fact]
        public void EmptyTimelineIsZeroLength() {
            var t = new GrainTimeline();
            Assert.Equal(0.0, t.Duration);
            Assert.Empty(t.RenderStereo(48000));
        }

        [Fact]
        public void DurationAccountsForStartAndRate() {
            // 1 s sine at start 0.5, rate 2.0 -> occupies 0.5 .. 0.5 + 0.5 = 1.0.
            var t = new GrainTimeline().Add(new SineGrain(440), 0.5, 2.0, 0.0);
            Assert.Equal(1.0, t.Duration, 9);
        }

        [Fact]
        public void DurationIsLatestEnd() {
            var t = new GrainTimeline()
                .Add(new SineGrain(440), 0.0, 1.0, 0.0)   // ends at 1.0
                .Add(new SineGrain(440), 0.5, 1.0, 0.0);  // ends at 1.5
            Assert.Equal(1.5, t.Duration, 9);
        }

        [Fact]
        public void RenderLengthIsInterleavedStereo() {
            var t = new GrainTimeline().Add(new SineGrain(440), 0.0, 1.0, 0.0);
            float[] pcm = t.RenderStereo(48000);
            // 1 s of frames plus the ITD tail padding, interleaved (× 2).
            Assert.Equal((48000 + GrainTimeline.ItdPaddingFrames(48000)) * 2, pcm.Length);
        }

        [Fact]
        public void InterauralDelayMatchesPanWithSubSamplePrecision() {
            // Pan right → right ear is near (no delay), left ear is far (delayed by |pan|·max).
            // At pan 0.5 the delay is 0.5·0.0007·48000 = 16.8 samples — a fractional value the
            // allpass path must reproduce, which integer rounding could not.
            const int sr = 48000;
            const double pan = 0.5;
            var tone = new AdsrGrain(new SineGrain(440), 0.01, 0.02, 0.04, 0.03);
            float[] pcm = new GrainTimeline().Add(tone, 0.0, 1.0, pan).RenderStereo(sr);

            float[] near = Channel(pcm, 1);  // right
            float[] far = Channel(pcm, 0);   // left
            double lag = EstimateLag(near, far, 40);

            double expected = pan * GrainTimeline.MaxInterauralDelaySeconds * sr;
            Assert.Equal(expected, lag, 1);  // within 0.05 samples
        }

        // Deinterleave one channel (0 left, 1 right) into a mono array.
        private static float[] Channel(float[] pcm, int channel) {
            var mono = new float[pcm.Length / 2];
            for (int f = 0; f < mono.Length; f++) {
                mono[f] = pcm[2 * f + channel];
            }
            return mono;
        }

        // Fractional lag (in frames) by which `far` trails `near`, via cross-correlation with
        // parabolic interpolation around the integer peak. Searches lags [0, maxLag].
        private static double EstimateLag(float[] near, float[] far, int maxLag) {
            double Corr(int lag) {
                double sum = 0.0;
                for (int n = lag < 0 ? -lag : 0; n < near.Length && n + lag < near.Length; n++) {
                    sum += near[n] * far[n + lag];
                }
                return sum;
            }

            int best = 0;
            double bestCorr = double.NegativeInfinity;
            for (int lag = 0; lag <= maxLag; lag++) {
                double c = Corr(lag);
                if (c > bestCorr) {
                    bestCorr = c;
                    best = lag;
                }
            }
            double cm = Corr(best - 1), c0 = Corr(best), cp = Corr(best + 1);
            double denom = cm - 2.0 * c0 + cp;
            double offset = denom != 0.0 ? 0.5 * (cm - cp) / denom : 0.0;
            return best + offset;
        }

        [Fact]
        public void HardLeftLeavesRightChannelSilent() {
            // Enveloped tone (so it has real energy) panned hard left.
            var tone = new AdsrGrain(new SineGrain(440), 0.01, 0.02, 0.04, 0.03);
            float[] pcm = new GrainTimeline().Add(tone, 0.0, 1.0, -1.0).RenderStereo(48000);

            float leftPeak = 0f, rightPeak = 0f;
            for (int i = 0; i < pcm.Length; i += 2) {
                leftPeak = System.Math.Max(leftPeak, System.Math.Abs(pcm[i]));
                rightPeak = System.Math.Max(rightPeak, System.Math.Abs(pcm[i + 1]));
            }
            Assert.True(leftPeak > 0.1f, "left channel should carry the signal");
            Assert.Equal(0f, rightPeak, 5);
        }

        [Fact]
        public void GainScalesAmplitude() {
            var tone = new AdsrGrain(new SineGrain(440), 0.01, 0.02, 0.04, 0.03);

            float[] full = new GrainTimeline().Add(tone, 0.0, 1.0, 0.0).RenderStereo(48000);
            float[] half = new GrainTimeline().Add(tone, 0.0, 1.0, 0.0, gain: 0.5).RenderStereo(48000);

            float fullPeak = 0f, halfPeak = 0f;
            for (int i = 0; i < full.Length; i++) {
                fullPeak = System.Math.Max(fullPeak, System.Math.Abs(full[i]));
                halfPeak = System.Math.Max(halfPeak, System.Math.Abs(half[i]));
            }
            Assert.Equal(0.5 * fullPeak, halfPeak, 4);
        }

        [Fact]
        public void OverlappingGrainsSum() {
            // Two identical centered tones at the same start should sum to ~2x one tone.
            var tone = new AdsrGrain(new SineGrain(440), 0.01, 0.02, 0.04, 0.03);

            float[] one = new GrainTimeline().Add(tone, 0.0, 1.0, 0.0).RenderStereo(48000);
            float[] two = new GrainTimeline()
                .Add(tone, 0.0, 1.0, 0.0)
                .Add(tone, 0.0, 1.0, 0.0)
                .RenderStereo(48000);

            float onePeak = 0f, twoPeak = 0f;
            for (int i = 0; i < one.Length; i++) {
                onePeak = System.Math.Max(onePeak, System.Math.Abs(one[i]));
                twoPeak = System.Math.Max(twoPeak, System.Math.Abs(two[i]));
            }
            Assert.Equal(2.0 * onePeak, twoPeak, 4);
        }
    }
}
