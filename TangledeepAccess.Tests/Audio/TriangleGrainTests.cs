using TangledeepAccess.Audio;
using Xunit;

namespace TangledeepAccess.Tests.Audio {
    public class TriangleGrainTests {
        [Fact]
        public void DurationIsOneSecond() {
            Assert.Equal(1.0, new TriangleGrain(440).Duration);
        }

        [Fact]
        public void HitsTheCharacteristicPhasePoints() {
            // f = 1 so phase == t: 0 at start, peak at 1/4, 0 at 1/2, trough at 3/4.
            var g = new TriangleGrain(1.0);
            Assert.Equal(0f, g.Evaluate(0.0), 5);
            Assert.Equal(1f, g.Evaluate(0.25), 5);
            Assert.Equal(0f, g.Evaluate(0.5), 5);
            Assert.Equal(-1f, g.Evaluate(0.75), 5);
        }

        [Fact]
        public void IsLinearBetweenVertices() {
            var g = new TriangleGrain(1.0);
            Assert.Equal(0.5f, g.Evaluate(0.125), 5);  // halfway up the first ramp
            Assert.Equal(-0.5f, g.Evaluate(0.625), 5); // halfway down toward the trough
        }

        [Fact]
        public void AmplitudeScales() {
            Assert.Equal(0.5f, new TriangleGrain(1.0, amplitude: 0.5).Evaluate(0.25), 5);
        }

        [Fact]
        public void IsPeriodicAtFrequency() {
            var g = new TriangleGrain(5.0);
            Assert.Equal(g.Evaluate(0.03), g.Evaluate(0.03 + 1.0 / 5.0), 5);
        }

        [Fact]
        public void SilentOutsideDuration() {
            var g = new TriangleGrain(440);
            Assert.Equal(0f, g.Evaluate(-0.01));
            Assert.Equal(0f, g.Evaluate(1.0));
        }
    }
}
