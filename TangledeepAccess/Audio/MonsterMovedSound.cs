using System.Collections.Generic;
using System.IO;
using TangledeepAccess.Util;

namespace TangledeepAccess.Audio {
    /// <summary>
    /// Engine glue for the monster-moved ping: loads monster_moved.wav's PCM once (decoded via
    /// <see cref="WavData"/>) and hands the raw samples to the pure <see cref="MonsterMovedCue"/>, which
    /// places one ping per moved monster onto the combat radar's shared timeline. Mirrors
    /// <see cref="CursorSounds"/>' embedded-resource load, but keeps the float samples (to wrap in a
    /// <see cref="BufferGrain"/> on the timeline) rather than baking a Unity AudioClip.
    /// </summary>
    public static class MonsterMovedSound {
        private static float[] _samples;
        private static int _sampleRate;
        private static bool _loaded;

        /// <summary>Add the moved-monster pings to the shared timeline. Returns true if any were added.</summary>
        public static bool AddPings(GrainTimeline timeline, IReadOnlyList<MonsterPing> moved) {
            EnsureLoaded();
            return MonsterMovedCue.AddPings(timeline, moved, _samples, _sampleRate);
        }

        private static void EnsureLoaded() {
            if (_loaded) {
                return;
            }

            _loaded = true; // set first: a failed load logs and leaves null samples (AddPings no-ops)
            const string resource = "TangledeepAccess.Sounds.monster_moved.wav";
            using (Stream stream = typeof(MonsterMovedSound).Assembly.GetManifestResourceStream(resource)) {
                if (stream == null) {
                    Log.Warn("Monster-moved sound resource missing: " + resource);
                    return;
                }

                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length) {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n == 0) {
                        break;
                    }
                    read += n;
                }

                WavData wav = WavData.Parse(bytes);
                _samples = ToMono(wav);
                _sampleRate = wav.SampleRate;
            }
        }

        // BufferGrain treats its array as a mono stream; a stereo asset would read as interleaved
        // garbage, so collapse to the left channel (and warn) if we are ever handed one.
        private static float[] ToMono(WavData wav) {
            if (wav.Channels == 1) {
                return wav.Samples;
            }

            Log.Warn("monster_moved.wav is not mono (" + wav.Channels + " ch); using the left channel");
            int frames = wav.Samples.Length / wav.Channels;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++) {
                mono[i] = wav.Samples[i * wav.Channels];
            }
            return mono;
        }
    }
}
