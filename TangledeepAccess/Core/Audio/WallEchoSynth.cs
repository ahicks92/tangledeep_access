namespace TangledeepAccess.Audio {
    /// <summary>
    /// Builds a wall-echo ping. Owns one pre-filtered <see cref="NoisePool"/> per band (the shared
    /// left/right base pitch, plus the up and down pitches) and, for each wall in range, pulls a
    /// fresh noise slice, wraps it in the ADSR envelope, and places it with the distance-derived
    /// delay/gain and the direction's pan. Left and right draw from the same base pool but as
    /// different slices, so they decorrelate and stay two sources when panned apart. Stateful (the
    /// pools advance their cursors), so the engine keeps one instance per sample rate; all numbers
    /// come from <see cref="WallEchoCue"/>.
    /// </summary>
    public sealed class WallEchoSynth {
        private readonly NoisePool _baseBand;
        private readonly NoisePool _upBand;
        private readonly NoisePool _downBand;

        public int SampleRate { get; }

        public WallEchoSynth(int sampleRate) {
            SampleRate = sampleRate;
            int length = (int)(WallEchoCue.PoolSeconds * sampleRate);

            _baseBand = MakePool(WallEchoCue.BaseFrequencyHz, sampleRate, length);
            _upBand = MakePool(WallEchoCue.UpFrequencyHz, sampleRate, length);
            _downBand = MakePool(WallEchoCue.DownFrequencyHz, sampleRate, length);
        }

        private static NoisePool MakePool(double centerHz, int sampleRate, int length) {
            return new NoisePool(
                centerHz,
                WallEchoCue.Q,
                sampleRate,
                length,
                WallEchoCue.WarmupSamples,
                WallEchoCue.TargetRms,
                WallEchoCue.NoiseSeed);
        }

        /// <summary>
        /// One ping. Each argument is the tile distance to that direction's nearest wall, or null if
        /// none is in sight there.
        /// </summary>
        public GrainTimeline Build(double? left, double? right, double? up, double? down) {
            var timeline = new GrainTimeline();
            AddWall(timeline, left, _baseBand, WallEchoCue.LeftPan, WallEchoCue.HorizontalLoudnessGain);
            AddWall(timeline, right, _baseBand, WallEchoCue.RightPan, WallEchoCue.HorizontalLoudnessGain);
            AddWall(timeline, up, _upBand, WallEchoCue.VerticalPan, WallEchoCue.UpLoudnessGain);
            AddWall(timeline, down, _downBand, WallEchoCue.VerticalPan, WallEchoCue.DownLoudnessGain);
            return timeline;
        }

        private static void AddWall(GrainTimeline timeline, double? distance, NoisePool pool, double pan, double loudnessGain) {
            if (!distance.HasValue) {
                return;
            }

            double d = distance.Value;
            Grain tone = new AdsrGrain(
                pool.Take(WallEchoCue.ToneSeconds),
                WallEchoCue.Attack,
                WallEchoCue.Decay,
                WallEchoCue.Sustain,
                WallEchoCue.Release,
                WallEchoCue.SustainLevel);

            timeline.Add(tone, WallEchoCue.DelaySeconds(d), 1.0, pan, WallEchoCue.Gain(d) * loudnessGain);
        }
    }
}
