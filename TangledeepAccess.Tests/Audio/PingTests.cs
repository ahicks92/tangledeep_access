using TangledeepAccess.Audio;
using Xunit;

namespace TangledeepAccess.Tests.Audio {
    public class PingTests {
        [Fact]
        public void PanMapsAndClamps() {
            Assert.Equal(0.0, Ping.Pan(0, 8.0), 9);
            Assert.Equal(-1.0, Ping.Pan(-8, 8.0), 9);
            Assert.Equal(1.0, Ping.Pan(8, 8.0), 9);
            Assert.Equal(0.5, Ping.Pan(4, 8.0), 9);
            Assert.Equal(-1.0, Ping.Pan(-50, 8.0), 9); // clamps
            Assert.Equal(1.0, Ping.Pan(50, 8.0), 9);
        }

        [Fact]
        public void PitchRateRisesNorthFallsSouth() {
            Assert.Equal(1.0, Ping.PitchRate(0, 2.0), 9);
            Assert.True(Ping.PitchRate(3, 2.0) > 1.0);  // north = up
            Assert.True(Ping.PitchRate(-3, 2.0) < 1.0); // south = down
            Assert.Equal(2.0, Ping.PitchRate(6, 2.0), 6); // +12 semitones = one octave
        }

        [Fact]
        public void PitchRateOfZeroSemitonesIsAlwaysUnity() {
            Assert.Equal(1.0, Ping.PitchRate(5, 0.0), 9);
            Assert.Equal(1.0, Ping.PitchRate(-5, 0.0), 9);
        }

        [Fact]
        public void PlaceExplicitRecordsPanStartGainRate() {
            var timeline = new GrainTimeline();
            Ping.Place(timeline, new SineGrain(440.0), pan: 0.5, start: 0.25, gain: 0.7, rate: 1.5);

            GrainPlacement p = Assert.Single(timeline.Placements);
            Assert.Equal(0.5, p.Pan, 9);
            Assert.Equal(0.25, p.Start, 9);
            Assert.Equal(0.7, p.Gain, 9);
            Assert.Equal(1.5, p.Rate, 9);
        }

        [Fact]
        public void PairPlacesReferenceThenOffsetSamePanGapApart() {
            var timeline = new GrainTimeline();
            var reference = new SineGrain(440.0);
            var offset = new SineGrain(660.0);
            Ping.Pair(timeline, reference, offset, offsetRate: 1.5, pan: 0.5, start: 0.2, gain: 0.7, gap: 0.1);

            Assert.Equal(2, timeline.Placements.Count);
            GrainPlacement r = timeline.Placements[0];
            GrainPlacement o = timeline.Placements[1];

            Assert.Same(reference, r.Grain);
            Assert.Equal(0.2, r.Start, 9);
            Assert.Equal(1.0, r.Rate, 9);   // reference always plays at base rate
            Assert.Equal(0.5, r.Pan, 9);

            Assert.Same(offset, o.Grain);
            Assert.Equal(0.3, o.Start, 9);  // start + gap
            Assert.Equal(1.5, o.Rate, 9);   // offset carries the pitch
            Assert.Equal(0.5, o.Pan, 9);    // same pan as the reference
            Assert.Equal(0.7, o.Gain, 9);
        }
    }
}
