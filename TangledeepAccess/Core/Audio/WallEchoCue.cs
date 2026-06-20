using System;

namespace TangledeepAccess.Audio {
    /// <summary>
    /// Tuning constants for the "wall echo" navigation cue, and the per-band tone frequencies
    /// derived from them. The cue pings the nearest wall in each cardinal direction as a short
    /// burst of band-pass-filtered white noise: left/right panned hard and sharing a base pitch,
    /// up/down centered and pitched above/below it. Distance maps to start delay (nearer = sooner)
    /// and gain (nearer = louder).
    ///
    /// Filtered noise (not a pure sine) is deliberate: it is less harsh, localizes better, and —
    /// crucially — two *decorrelated* noise bursts panned left and right stay perceptually separate
    /// instead of fusing the way two identical sines do, so equidistant horizontal walls read as two
    /// sources. The decorrelation comes from each ping drawing a different slice of the pool; see
    /// <see cref="NoisePool"/> and <see cref="WallEchoSynth"/>. Everything here is a constant to tune.
    /// </summary>
    public static class WallEchoCue {
        // ===================== Tuning constants — tweak freely =====================

        /// <summary>Center pitch of the left/right wall tones. ~Middle C (C4).</summary>
        public const double BaseFrequencyHz = 261.63;

        /// <summary>
        /// Up wall is this many semitones above base; down wall the same below. A perfect fifth
        /// (7) rather than an octave (12): at an octave down the south tone landed near 130 Hz,
        /// where the ear and small speakers are far less sensitive, so it read as much quieter
        /// than the other walls despite equal RMS. A fifth keeps the up/down axis distinct while
        /// holding the down tone (~175 Hz) out of that low-end hole.
        /// </summary>
        public const double VerticalSemitones = 7.0;

        /// <summary>
        /// Band-pass resonance. Higher Q = narrower band = purer, more sine-like tone; lower Q =
        /// wider band = airier, noisier, more percussive. This is the main timbre knob to tune.
        /// </summary>
        public const double Q = 30.0;

        /// <summary>Start delay added per tile of distance (nearer walls sound sooner).</summary>
        public const double MsPerTile = 35.0;

        /// <summary>Amplitude change per tile of distance, in decibels (negative = farther is quieter).</summary>
        public const double DbPerTile = -3;

        /// <summary>Base gain for an adjacent wall. A raw full-scale tone is harsh, so start below 1.</summary>
        public const double InitialVolume = 0.8;

        // Per-band perceptual loudness trims, multiplied into the distance gain. The noise pools are
        // RMS-normalized, which equalizes electrical level but not *loudness*: lower-pitched bands
        // still read quieter to the ear (and on small speakers). Raising VerticalSemitones' tone out
        // of the deep low end got most of the way; these close the rest of the gap, tuned by ear.
        // 1.0 = no change.

        /// <summary>Loudness trim for the left/right (base-pitch) walls.</summary>
        public const double HorizontalLoudnessGain = 1.0;

        /// <summary>Loudness trim for the up (north) wall.</summary>
        public const double UpLoudnessGain = 1.0;

        /// <summary>Loudness trim for the down (south) wall — lowest pitch, so it gets a boost.</summary>
        public const double DownLoudnessGain = 1.3;

        // 100 ms ADSR envelope over the tone (the four segments sum to ToneSeconds, the slice length).
        public const double Attack = 0.002;
        public const double Decay = 0.030;
        public const double Sustain = 0.060;
        public const double Release = 0.01;
        public const double SustainLevel = 0.8;

        /// <summary>Length of one tone, i.e. the slice each ping pulls from its pool. = the envelope.</summary>
        public const double ToneSeconds = Attack + Decay + Sustain + Release;

        // Pan positions per axis.
        public const double LeftPan = -1.0;
        public const double RightPan = 1.0;
        public const double VerticalPan = 0.0;

        // --- Noise pool generation ---

        /// <summary>RMS each filtered pool is normalized to, so changing Q changes timbre, not loudness.</summary>
        public const double TargetRms = 0.3;

        /// <summary>Seconds of filtered noise held per band before regeneration (amortizes filtering).</summary>
        public const double PoolSeconds = 2.0;

        /// <summary>Filtered samples discarded from the front of each pool: the filter's warm-up transient.</summary>
        public const int WarmupSamples = 128;

        /// <summary>Fixed seed: noise is noise, and a stable seed makes runs reproducible for debugging.</summary>
        public const int NoiseSeed = 12345;

        // ==========================================================================

        /// <summary>Pitch of the up-wall tone (base raised <see cref="VerticalSemitones"/>).</summary>
        public static readonly double UpFrequencyHz = BaseFrequencyHz * Math.Pow(2.0, VerticalSemitones / 12.0);

        /// <summary>Pitch of the down-wall tone (base lowered <see cref="VerticalSemitones"/>).</summary>
        public static readonly double DownFrequencyHz = BaseFrequencyHz * Math.Pow(2.0, -VerticalSemitones / 12.0);

        /// <summary>
        /// Delay (seconds) before a wall at <paramref name="distanceTiles"/> sounds, referenced to an
        /// adjacent wall (distance 1) playing immediately. The absolute onset carries no information —
        /// only the spacing between the four walls does — so the nearest possible wall must add zero
        /// latency; each tile beyond adjacent adds <see cref="MsPerTile"/>. (Clamped at 0 for safety;
        /// real distances are always ≥ 1.)
        /// </summary>
        public static double DelaySeconds(double distanceTiles) {
            return Math.Max(0.0, distanceTiles - 1.0) * MsPerTile / 1000.0;
        }

        /// <summary>
        /// Gain for a wall at <paramref name="distanceTiles"/>, referenced to an adjacent wall
        /// (distance 1) at full <see cref="InitialVolume"/> — same adjacent-is-the-reference rule as
        /// <see cref="DelaySeconds"/>. Each tile beyond adjacent attenuates by <see cref="DbPerTile"/>.
        /// (Clamped at 0 for safety; real distances are always ≥ 1.)
        /// </summary>
        public static double Gain(double distanceTiles) {
            return InitialVolume * Math.Pow(10.0, DbPerTile * Math.Max(0.0, distanceTiles - 1.0) / 20.0);
        }
    }
}
