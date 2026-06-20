using System.Collections.Generic;
using TangledeepAccess.Audio;
using Xunit;

namespace TangledeepAccess.Tests.Audio {
    public class MonsterMovedCueTests {
        private static float[] Samples(int n) {
            var s = new float[n];
            for (int i = 0; i < n; i++) {
                s[i] = 0.5f;
            }
            return s;
        }

        [Fact]
        public void NothingMovedAddsNothing() {
            var t = new GrainTimeline();
            Assert.False(MonsterMovedCue.AddPings(t, new List<MonsterPing>(), Samples(100), 44100));
            Assert.Empty(t.Placements);
        }

        [Fact]
        public void NullSampleAddsNothing() {
            var t = new GrainTimeline();
            var moved = new List<MonsterPing> { new MonsterPing(1, 0) };
            Assert.False(MonsterMovedCue.AddPings(t, moved, null, 44100));
            Assert.Empty(t.Placements);
        }

        [Fact]
        public void EachMonsterIsAReferenceOffsetPairMatchingTheRadar() {
            var moved = new List<MonsterPing> {
                new MonsterPing(4, 0),
                new MonsterPing(-8, 3),
                new MonsterPing(0, -2),
            };
            var t = new GrainTimeline();
            Assert.True(MonsterMovedCue.AddPings(t, moved, Samples(4410), 44100));
            Assert.Equal(2 * moved.Count, t.Placements.Count); // a reference + offset per monster

            for (int i = 0; i < moved.Count; i++) {
                GrainPlacement reference = t.Placements[2 * i];
                GrainPlacement offset = t.Placements[2 * i + 1];
                double pan = Ping.Pan(moved[i].Dx, ScanCue.MaxPanTiles);
                double cue = i * ScanCue.IntervalSeconds; // cadence taken from the radar

                Assert.Equal(pan, reference.Pan, 9);
                Assert.Equal(cue, reference.Start, 9);
                Assert.Equal(1.0, reference.Rate, 9);          // reference is base pitch
                Assert.Equal(ScanCue.Volume, reference.Gain, 9);

                Assert.Equal(pan, offset.Pan, 9);
                Assert.Equal(cue + ScanCue.GapSeconds, offset.Start, 9);
                Assert.Equal(Ping.PitchRate(moved[i].Dy, ScanCue.SemitonesPerTileY), offset.Rate, 9); // y in the interval
            }
        }

        [Fact]
        public void VoiceWrapsSampleInTheRadarEnvelope() {
            float[] samples = Samples(4410); // 0.1 s at 44100 Hz — longer than the radar envelope
            var t = new GrainTimeline();
            MonsterMovedCue.AddPings(t, new List<MonsterPing> { new MonsterPing(0, 0) }, samples, 44100);

            var adsr = Assert.IsType<AdsrGrain>(t.Placements[0].Grain);
            var buf = Assert.IsType<BufferGrain>(adsr.Inner);
            Assert.Same(samples, buf.Data);
            // Every envelope value comes from the radar, which on this longer recording both shapes and
            // bounds the ping to ScanCue.ToneSeconds.
            Assert.Equal(ScanCue.Attack, adsr.Attack, 9);
            Assert.Equal(ScanCue.Decay, adsr.Decay, 9);
            Assert.Equal(ScanCue.Sustain, adsr.Sustain, 9);
            Assert.Equal(ScanCue.Release, adsr.Release, 9);
            Assert.Equal(ScanCue.SustainLevel, adsr.SustainLevel, 9);
            Assert.Equal(ScanCue.ToneSeconds, adsr.Duration, 9);
        }
    }
}
