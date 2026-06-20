using System.Collections.Generic;

namespace TangledeepAccess.Audio {
    /// <summary>A moved monster's tile offset from the hero (east +x, north +y).</summary>
    public readonly struct MonsterPing {
        public readonly int Dx;
        public readonly int Dy;

        public MonsterPing(int dx, int dy) {
            Dx = dx;
            Dy = dy;
        }
    }

    /// <summary>
    /// Builds the monster-moved pings for the combat radar. Each moved monster becomes one ping with the
    /// exact shape of a scanner ping (<see cref="Ping.Pair"/>) — a base-pitch reference grain then a
    /// y-pitched offset a gap later, panned by x — but voiced with monster_moved.wav instead of a sine.
    ///
    /// <para>Every timing, pan, pitch, level, and envelope value is taken straight from
    /// <see cref="ScanCue"/>, so the monster ping and the radar are the same cue and can never drift
    /// apart; the only difference is the grain source. Pure (Core): float samples + integer offsets in,
    /// placements out.</para>
    /// </summary>
    public static class MonsterMovedCue {
        /// <summary>
        /// Add one ping per moved monster to <paramref name="timeline"/>. <paramref name="samples"/> is
        /// the mono PCM of monster_moved.wav recorded at <paramref name="sampleRate"/> Hz. Returns true
        /// if any ping was added (nothing moved, or no sample loaded, → false).
        /// </summary>
        public static bool AddPings(GrainTimeline timeline, IReadOnlyList<MonsterPing> moved, float[] samples, int sampleRate) {
            if (moved == null || moved.Count == 0 || samples == null || samples.Length == 0) {
                return false;
            }

            double start = 0.0; // first ping at the start of the radar buffer
            for (int i = 0; i < moved.Count; i++) {
                double pan = Ping.Pan(moved[i].Dx, ScanCue.MaxPanTiles);
                double offsetRate = Ping.PitchRate(moved[i].Dy, ScanCue.SemitonesPerTileY);
                // Both grains are the same recording; the offset's varispeed encodes y as the interval.
                Ping.Pair(timeline, Voice(samples, sampleRate), Voice(samples, sampleRate),
                          offsetRate, pan, start, ScanCue.Volume, ScanCue.GapSeconds);
                start += ScanCue.IntervalSeconds;
            }
            return true;
        }

        // The radar voice for a sample: the wav wrapped in the scanner's tone envelope, which both shapes
        // the ping and bounds it to ScanCue.ToneSeconds (these recordings are longer than the envelope).
        private static Grain Voice(float[] samples, int sampleRate) {
            return new AdsrGrain(
                new BufferGrain(samples, 0, samples.Length, sampleRate),
                ScanCue.Attack, ScanCue.Decay, ScanCue.Sustain, ScanCue.Release, ScanCue.SustainLevel);
        }
    }
}
