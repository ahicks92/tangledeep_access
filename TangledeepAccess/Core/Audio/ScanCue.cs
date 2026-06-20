using System;

namespace TangledeepAccess.Audio {
    /// <summary>
    /// The entity-scanner cue and its tuning constants. One visible entity at tile offset (x, y)
    /// from the hero is conveyed by TWO grains played at the same pan: a <b>reference</b> grain at a
    /// fixed pitch, then — a constant gap later — a <b>second</b> grain pitched up or down by an
    /// interval set by y. Both are panned by x. The point is relative pitch: you read y from the
    /// interval between the two grains, not from absolute pitch, while x comes from the shared pan.
    /// Distance falls out of x and y together, so there is no separate loudness-by-distance mapping.
    ///
    /// The scanner sweeps entities one at a time at <see cref="IntervalSeconds"/>; this builds the
    /// cue for a single entity. Triangle grains for now; swap the inner grain later for a richer source (per-entity sample files).
    /// </summary>
    public static class ScanCue {
        // ===================== Tuning constants — tweak freely =====================

        /// <summary>Fixed pitch of the reference grain — the anchor the second grain's interval is read against.</summary>
        public const double ReferenceFrequencyHz = 440.0;

        /// <summary>Semitones the second grain is shifted per tile of y (north positive); the y → pitch map.</summary>
        public const double SemitonesPerTileY = 2.0;

        /// <summary>x at this many tiles pans fully; beyond it clamps. The x → pan map.</summary>
        public const double MaxPanTiles = 8.0;

        /// <summary>Constant delay of the second grain after the reference (may be shorter than a grain: overlap).</summary>
        public const double GapSeconds = 0.1;

        /// <summary>Gain of each grain. Fixed — distance is carried by pan+pitch, not volume.</summary>
        public const double Volume = 0.2;

        /// <summary>Cadence: seconds between pinging successive entities in the sweep.</summary>
        public const double IntervalSeconds = 0.5;

        // Envelope shared by both grains (their fixed "configuration"). The attack is kept near-zero
        // because this same envelope currently also wraps the percussive sample-based radar pings
        // (monster moves, and the other radar .wavs to come), whose loudest transient is at t=0 — a
        // longer fade-in would mush exactly that punch. A hair of attack (not a hard 0) still avoids an
        // onset click on samples that don't start at a zero crossing. (Temporary shared compromise: a
        // sine wants a softer attack than a percussive sample, so these will likely split later.)
        public const double Attack = 0.001;
        public const double Decay = 0.020;
        public const double Sustain = 0.040;
        public const double Release = 0.015;
        public const double SustainLevel = 0.8;

        /// <summary>Length of one grain (the envelope sum), i.e. how long each tone lasts.</summary>
        public const double ToneSeconds = Attack + Decay + Sustain + Release;

        // ==========================================================================

        /// <summary>Pan in [-1, 1] for a tile offset of <paramref name="x"/> east of the hero.</summary>
        public static double Pan(int x) {
            return Ping.Pan(x, MaxPanTiles);
        }

        /// <summary>Pitch of the second grain for a tile offset of <paramref name="y"/> north of the hero.</summary>
        public static double SecondFrequencyHz(int y) {
            return ReferenceFrequencyHz * Math.Pow(2.0, y * SemitonesPerTileY / 12.0);
        }

        /// <summary>
        /// Build the two-grain cue for an entity at tile offset (x, y) from the hero: a base-pitch
        /// reference grain then a y-pitched offset a gap later, both panned by x. Triangle timbre
        /// throughout: the x = 0 dead-center case used to be special-cased to a triangle (a pure sine's
        /// centered image was hard to place), but with triangle now universal and the timeline's
        /// interaural time delay carrying left/right placement, that special case is gone.
        /// </summary>
        public static GrainTimeline Build(int x, int y) {
            double pan = Pan(x);
            var timeline = new GrainTimeline();
            // Pitch is baked into the offset grain's frequency, so its placement rate stays 1 — the
            // scanner reads y from the reference→offset interval.
            Ping.Pair(timeline, Tone(ReferenceFrequencyHz), Tone(SecondFrequencyHz(y)),
                      1.0, pan, 0.0, Volume, GapSeconds);
            return timeline;
        }

        private static Grain Tone(double frequencyHz) {
            return new AdsrGrain(new TriangleGrain(frequencyHz), Attack, Decay, Sustain, Release, SustainLevel);
        }
    }
}
