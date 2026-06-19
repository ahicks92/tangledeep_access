using System;
using System.Collections.Generic;

namespace TangledeepAccess.Audio {
    /// <summary>One grain placed on a timeline: when it starts, how fast it plays, where it sits in stereo.</summary>
    public readonly struct GrainPlacement {
        public readonly Grain Grain;

        /// <summary>Timeline time (seconds) at which the grain's sample 0 plays.</summary>
        public readonly double Start;

        /// <summary>
        /// Varispeed factor. The grain is sampled at <c>(timelineTime - Start) · Rate</c>,
        /// so rate &gt; 1 is higher-pitched and shorter; its timeline length is
        /// <c>Grain.Duration / Rate</c>. Must be &gt; 0.
        /// </summary>
        public readonly double Rate;

        /// <summary>Pan position in [-1, 1]; see <see cref="PanLaw"/>.</summary>
        public readonly double Pan;

        /// <summary>Linear amplitude scale applied to the grain's samples before panning (1.0 = unchanged).</summary>
        public readonly double Gain;

        public GrainPlacement(Grain grain, double start, double rate, double pan, double gain) {
            Grain = grain;
            Start = start;
            Rate = rate;
            Pan = pan;
            Gain = gain;
        }

        /// <summary>This grain's end time on the timeline, accounting for playback rate.</summary>
        public double End => Start + Grain.Duration / Rate;
    }

    /// <summary>
    /// An ordered collection of grain placements that renders to an interleaved stereo PCM
    /// buffer. Overlapping grains are summed; the output is not clamped (limiting, if any,
    /// is the caller's concern). The timeline's <see cref="Duration"/> is known before any
    /// sample is computed, because every grain reports a finite duration.
    /// </summary>
    public sealed class GrainTimeline {
        /// <summary>
        /// Maximum interaural time delay (ITD), in seconds, applied at full pan (|pan| = 1).
        /// A panned sound reaches the far ear slightly later than the near ear; we model that
        /// by delaying the far channel by <c>|pan| · MaxInterauralDelaySeconds</c> (linear
        /// scaling of pan). The classic Woodworth maximum head ITD is ~0.66 ms; this is a
        /// tunable approximation, not a measured HRTF. Set to 0 to disable ITD entirely.
        /// </summary>
        public const double MaxInterauralDelaySeconds = 0.0007;

        /// <summary>
        /// When true, the far-ear delay is realized with sub-sample precision: an integer
        /// sample shift plus a first-order <see cref="AllpassFractionalDelay"/> for the
        /// fractional remainder, so the ITD moves continuously with pan instead of snapping
        /// in 1-sample (~21 µs at 48 kHz) steps. When false, the delay is rounded to the
        /// nearest whole sample. Kept as a runtime switch (not a const) so the two can be
        /// compared by ear without the rounding branch being compiled out as dead code.
        /// </summary>
        public static readonly bool UseFractionalDelay = true;

        /// <summary>
        /// Extra tail frames rendered past each fractionally-delayed grain so the allpass
        /// filter's ringout is captured rather than clipped at the buffer's end. The
        /// fractional part is held in [0.5, 1.5) (pole magnitude ≤ 1/3), so the tail is
        /// inaudible within a handful of samples; 32 is comfortable margin.
        /// </summary>
        internal const int AllpassFlushFrames = 32;

        private readonly List<GrainPlacement> _placements = new List<GrainPlacement>();

        public IReadOnlyList<GrainPlacement> Placements => _placements;

        /// <summary>Place a grain. Returns this timeline for fluent chaining.</summary>
        public GrainTimeline Add(Grain grain, double start, double rate, double pan, double gain = 1.0) {
            _placements.Add(new GrainPlacement(grain, start, rate, pan, gain));
            return this;
        }

        /// <summary>Total length in seconds: the latest end time across all placements (0 if empty).</summary>
        public double Duration {
            get {
                double max = 0.0;
                foreach (GrainPlacement p in _placements) {
                    double end = p.End;
                    if (end > max) {
                        max = end;
                    }
                }
                return max;
            }
        }

        /// <summary>
        /// Extra frames appended to the render buffer to hold a hard-panned grain's delayed
        /// far-ear tail (plus the allpass ringout when <see cref="UseFractionalDelay"/> is on)
        /// so it never clips off the end. Single source of truth for the padding math.
        /// </summary>
        internal static int ItdPaddingFrames(int sampleRate) {
            int pad = (int)Math.Ceiling(MaxInterauralDelaySeconds * sampleRate);
            if (UseFractionalDelay) {
                pad += AllpassFlushFrames;
            }
            return pad;
        }

        /// <summary>
        /// Render the whole timeline to an interleaved stereo float buffer (L, R, L, R, …)
        /// at <paramref name="sampleRate"/> Hz. Length is
        /// <c>(ceil(Duration · sampleRate) + ItdPaddingFrames) · 2</c>: the buffer is padded so a
        /// hard-panned grain's delayed far-ear tail never clips off the end.
        /// </summary>
        public float[] RenderStereo(int sampleRate) {
            int totalFrames = (int)Math.Ceiling(Duration * sampleRate);
            int paddingFrames = totalFrames == 0 ? 0 : ItdPaddingFrames(sampleRate);
            int paddedFrames = totalFrames + paddingFrames;
            var buffer = new float[paddedFrames * 2];

            foreach (GrainPlacement p in _placements) {
                float leftGain, rightGain;
                PanLaw.Compute(p.Pan, out leftGain, out rightGain);

                // Interaural time delay: the far ear hears a panned sound later. Clamp pan so
                // the offset can't exceed the padding, then split the desired delay into an
                // integer sample shift (write-index offset) and, optionally, a fractional
                // remainder handled by a first-order allpass.
                double pan = p.Pan < -1.0 ? -1.0 : (p.Pan > 1.0 ? 1.0 : p.Pan);
                double delaySamples = Math.Abs(pan) * MaxInterauralDelaySeconds * sampleRate;

                int intDelay;
                AllpassFractionalDelay allpass = null;
                if (!UseFractionalDelay) {
                    intDelay = (int)Math.Round(delaySamples);
                } else if (delaySamples < 0.5) {
                    // Below half a sample the ITD is negligible (only |pan| within ~±0.015 of
                    // center); emit zero delay rather than push the allpass toward its d→0 pole.
                    intDelay = 0;
                } else {
                    // Keep the fractional part in [0.5, 1.5) by borrowing a sample from the
                    // integer part, so the allpass pole stays well inside the unit circle.
                    intDelay = (int)Math.Floor(delaySamples - 0.5);
                    allpass = new AllpassFractionalDelay(delaySamples - intDelay);
                }

                bool farIsLeft = pan > 0.0;   // panned right → left ear is far
                bool farIsRight = pan < 0.0;  // panned left  → right ear is far

                int firstFrame = (int)Math.Floor(p.Start * sampleRate);
                if (firstFrame < 0) {
                    firstFrame = 0;
                }
                int lastFrame = (int)Math.Ceiling(p.End * sampleRate);
                if (lastFrame > totalFrames) {
                    lastFrame = totalFrames;
                }

                for (int i = firstFrame; i < lastFrame; i++) {
                    double timelineTime = (double)i / sampleRate;
                    double grainTime = (timelineTime - p.Start) * p.Rate;
                    if (grainTime < 0.0 || grainTime >= p.Grain.Duration) {
                        continue;
                    }
                    float sample = (float)(p.Grain.Evaluate(grainTime) * p.Gain);
                    if (farIsLeft) {
                        buffer[2 * i + 1] += sample * rightGain;                                  // near
                        float far = allpass != null ? allpass.Process(sample) : sample;
                        buffer[2 * (i + intDelay)] += far * leftGain;                             // far
                    } else if (farIsRight) {
                        buffer[2 * i] += sample * leftGain;                                       // near
                        float far = allpass != null ? allpass.Process(sample) : sample;
                        buffer[2 * (i + intDelay) + 1] += far * rightGain;                        // far
                    } else {
                        buffer[2 * i] += sample * leftGain;
                        buffer[2 * i + 1] += sample * rightGain;
                    }
                }

                // Flush the allpass ringout (fast-decaying) into the far channel's tail.
                if (allpass != null) {
                    for (int k = 0; k < AllpassFlushFrames; k++) {
                        int frame = lastFrame + k + intDelay;
                        if (frame >= paddedFrames) {
                            break;
                        }
                        float far = allpass.Process(0f);
                        if (farIsLeft) {
                            buffer[2 * frame] += far * leftGain;
                        } else {
                            buffer[2 * frame + 1] += far * rightGain;
                        }
                    }
                }
            }

            return buffer;
        }
    }
}
