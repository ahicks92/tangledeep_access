using System;
using TangledeepAccess.Audio;
using Xunit;

namespace TangledeepAccess.Tests.Audio {
    public class AllpassFractionalDelayTests {
        // An allpass filter leaves the magnitude response flat: a steady tone comes out at the
        // same amplitude it went in, only phase-shifted.
        [Fact]
        public void PreservesMagnitude() {
            var ap = new AllpassFractionalDelay(0.8);
            const double freq = 0.1;  // cycles/sample
            const int n = 4000;
            var outp = new float[n];
            double inSum = 0.0;
            for (int i = 0; i < n; i++) {
                float x = (float)Math.Sin(2.0 * Math.PI * freq * i);
                outp[i] = ap.Process(x);
                inSum += x * x;
            }
            // Compare RMS past the warmup transient.
            double inRms = Math.Sqrt(inSum / n);
            double outSum = 0.0;
            int skip = 100;
            for (int i = skip; i < n; i++) {
                outSum += outp[i] * outp[i];
            }
            double outRms = Math.Sqrt(outSum / (n - skip));
            Assert.Equal(inRms, outRms, 2);
        }

        // The phase delay at low frequency equals the requested fractional delay.
        [Theory]
        [InlineData(0.5)]
        [InlineData(0.8)]
        [InlineData(1.2)]
        public void DelaysByRequestedFraction(double d) {
            var ap = new AllpassFractionalDelay(d);
            const double freq = 0.002;  // low: phase delay ≈ group delay ≈ d
            const int n = 8000;
            var input = new float[n];
            var output = new float[n];
            for (int i = 0; i < n; i++) {
                input[i] = (float)Math.Sin(2.0 * Math.PI * freq * i);
                output[i] = ap.Process(input[i]);
            }
            Assert.Equal(d, EstimateLag(input, output, 4), 1);  // within 0.05 samples
        }

        // Fractional lag by which `delayed` trails `reference`, via cross-correlation with
        // parabolic interpolation around the integer peak.
        private static double EstimateLag(float[] reference, float[] delayed, int maxLag) {
            double Corr(int lag) {
                double sum = 0.0;
                for (int i = lag < 0 ? -lag : 0; i < reference.Length && i + lag < reference.Length; i++) {
                    sum += reference[i] * delayed[i + lag];
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
    }
}
