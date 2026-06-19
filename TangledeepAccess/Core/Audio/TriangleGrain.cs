using System;

namespace TangledeepAccess.Audio {
    /// <summary>
    /// A fixed-frequency triangle wave: same shape role as <see cref="SineGrain"/> (starts at 0,
    /// peaks +A at a quarter period, 0 at the half, -A at three quarters) but with the brighter,
    /// reedier timbre of its odd harmonics. Used for entities directly aligned with the hero, whose
    /// dead-center pan is an unstable image — the distinct timbre carries the "dead ahead/behind"
    /// information the pan can't. Duration is capped at 1 second for the same integral-frequency
    /// periodicity reason as the sine.
    /// </summary>
    public sealed class TriangleGrain : Grain {
        public const double CappedDuration = 1.0;

        public double Frequency { get; }
        public double Amplitude { get; }

        public TriangleGrain(double frequency, double amplitude = 1.0) {
            Frequency = frequency;
            Amplitude = amplitude;
        }

        public override double Duration => CappedDuration;

        public override float Evaluate(double t) {
            if (t < 0.0 || t >= CappedDuration) {
                return 0f;
            }

            double phase = t * Frequency;
            phase -= Math.Floor(phase); // fractional part, in [0, 1)

            double y;
            if (phase < 0.25) {
                y = 4.0 * phase;            // 0 -> +1
            } else if (phase < 0.75) {
                y = 2.0 - 4.0 * phase;      // +1 -> -1
            } else {
                y = 4.0 * phase - 4.0;      // -1 -> 0
            }

            return (float)(Amplitude * y);
        }
    }
}
