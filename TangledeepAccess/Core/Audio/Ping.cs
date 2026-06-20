using System;

namespace TangledeepAccess.Audio {
    /// <summary>
    /// Turns a grain into a positioned "ping": places it on a <see cref="GrainTimeline"/> panned by an
    /// east/west tile offset and (optionally) pitch-shifted by a north/south tile offset. This is the
    /// spatialization primitive extracted from the entity scanner (<see cref="ScanCue"/>) so other
    /// earcons can sit on a radar too — e.g. monster-moved pings joining the wall-echo timeline
    /// (<see cref="MonsterMovedCue"/>). Pure (Core): integer tile offsets in, a timeline placement out.
    /// </summary>
    public static class Ping {
        /// <summary>Stereo pan in [-1, 1] for an east(+)/west(-) tile offset, clamped past <paramref name="maxPanTiles"/>.</summary>
        public static double Pan(int dx, double maxPanTiles) {
            double p = dx / maxPanTiles;
            if (p < -1.0) {
                return -1.0;
            }
            if (p > 1.0) {
                return 1.0;
            }
            return p;
        }

        /// <summary>Varispeed playback rate that shifts pitch by <paramref name="semitonesPerTile"/> per north(+) tile.</summary>
        public static double PitchRate(int dy, double semitonesPerTile) {
            return Math.Pow(2.0, dy * semitonesPerTile / 12.0);
        }

        /// <summary>Place <paramref name="grain"/> on the timeline at an explicit pan, start, gain, and rate.</summary>
        public static void Place(GrainTimeline timeline, Grain grain, double pan, double start, double gain, double rate = 1.0) {
            timeline.Add(grain, start, rate, pan, gain);
        }

        /// <summary>
        /// Place one two-grain "ping" — the shape the F2 pinger plays for a single entity: a base-pitch
        /// <paramref name="reference"/> grain at <paramref name="start"/>, then the
        /// <paramref name="offset"/> grain <paramref name="gap"/> seconds later at
        /// <paramref name="offsetRate"/>, both panned to <paramref name="pan"/>. The north/south axis is
        /// read from the interval between the two, so the reference is what makes the offset legible —
        /// an offset alone is just a detuned sound with nothing to anchor it.
        /// </summary>
        public static void Pair(GrainTimeline timeline, Grain reference, Grain offset, double offsetRate,
                                double pan, double start, double gain, double gap) {
            Place(timeline, reference, pan, start, gain);
            Place(timeline, offset, pan, start + gap, gain, offsetRate);
        }
    }
}
