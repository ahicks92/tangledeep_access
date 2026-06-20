using TangledeepAccess.Audio;
using Xunit;

namespace TangledeepAccess.Tests.Audio {
    public class WallEchoSynthTests {
        private const int Sr = 48000;

        private static BufferGrain InnerBuffer(GrainPlacement p) {
            var adsr = Assert.IsType<AdsrGrain>(p.Grain);
            return Assert.IsType<BufferGrain>(adsr.Inner);
        }

        [Fact]
        public void NoWallsProducesNoGrains() {
            Assert.Empty(new WallEchoSynth(Sr).Build(null, null, null, null).Placements);
        }

        [Fact]
        public void OneGrainPerSeenWall() {
            Assert.Equal(4, new WallEchoSynth(Sr).Build(1, 2, 3, 4).Placements.Count);
            Assert.Single(new WallEchoSynth(Sr).Build(1, null, null, null).Placements);
        }

        [Fact]
        public void LeftAndRightArePannedAndEnvelopedNoise() {
            var placements = new WallEchoSynth(Sr).Build(3, 3, null, null).Placements;
            Assert.Equal(WallEchoCue.LeftPan, placements[0].Pan);
            Assert.Equal(WallEchoCue.RightPan, placements[1].Pan);
            InnerBuffer(placements[0]); // asserts Adsr-over-BufferGrain
            InnerBuffer(placements[1]);
        }

        [Fact]
        public void LeftAndRightDrawDecorrelatedSlicesOfTheSameBand() {
            var placements = new WallEchoSynth(Sr).Build(3, 3, null, null).Placements;
            BufferGrain left = InnerBuffer(placements[0]);
            BufferGrain right = InnerBuffer(placements[1]);

            // Same base-band pool (same array) but different slices (different offsets) — that
            // offset difference is exactly what makes them decorrelate and not collapse when panned.
            Assert.Same(left.Data, right.Data);
            Assert.NotEqual(left.Offset, right.Offset);
        }

        [Fact]
        public void VerticalWallsUseDistinctBandsFromBaseAndEachOther() {
            var placements = new WallEchoSynth(Sr).Build(3, null, 3, 3).Placements;
            BufferGrain left = InnerBuffer(placements[0]);   // base band
            BufferGrain up = InnerBuffer(placements[1]);     // up band
            BufferGrain down = InnerBuffer(placements[2]);   // down band

            Assert.NotSame(left.Data, up.Data);
            Assert.NotSame(left.Data, down.Data);
            Assert.NotSame(up.Data, down.Data);
        }

        [Fact]
        public void DelayAndGainComeFromTheCue() {
            GrainPlacement p = new WallEchoSynth(Sr).Build(2, null, null, null).Placements[0];
            Assert.Equal(WallEchoCue.DelaySeconds(2), p.Start, 9);
            Assert.Equal(WallEchoCue.Gain(2) * WallEchoCue.HorizontalLoudnessGain, p.Gain, 9);
        }
    }
}
