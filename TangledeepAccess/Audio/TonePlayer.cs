using UnityEngine;

namespace TangledeepAccess.Audio {
    /// <summary>
    /// Plays mod-synthesized interleaved-stereo PCM through mod-owned <see cref="AudioSource"/>s,
    /// bypassing the game's mixer and all DSP so the buffer reaches the output sample-for-sample (2D,
    /// centered, no mixer group, effect/listener/reverb bypasses on). A small round-robin pool of
    /// voices means a new ping does not cut off the previous one — successive cues (the scanner's
    /// sweep) overlap naturally. The caller passes the rate it rendered at, since the clip is created
    /// to match. Engine-side glue (touches Unity); the buffer math lives in Core.
    /// </summary>
    public static class TonePlayer {
        private const int Voices = 8;
        private static AudioSource[] _voices;
        private static int _next;

        private static AudioSource[] Pool() {
            if (_voices == null) {
                var go = new GameObject("TangledeepAccess.TonePlayer");
                Object.DontDestroyOnLoad(go);
                _voices = new AudioSource[Voices];
                for (int i = 0; i < Voices; i++) {
                    _voices[i] = Configure(go.AddComponent<AudioSource>());
                }
            }
            return _voices;
        }

        private static AudioSource Configure(AudioSource source) {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;          // 2D: no distance attenuation / 3D panning
            source.panStereo = 0f;             // leave the buffer's own L/R intact
            source.pitch = 1f;                 // no resample-by-pitch
            source.volume = 1f;
            source.outputAudioMixerGroup = null; // straight to the listener, not the game mixer
            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;
            return source;
        }

        /// <summary>
        /// Render a grain timeline at the device output rate and play it. The single play path for the
        /// scanner ping, the wall echo, and the combat radar; no-ops on an empty timeline.
        /// </summary>
        public static void PlayTimeline(GrainTimeline timeline) {
            if (timeline == null || timeline.Placements.Count == 0) {
                return;
            }

            int sampleRate = AudioSettings.outputSampleRate;
            PlayStereo(timeline.RenderStereo(sampleRate), sampleRate);
        }

        /// <summary>Play an interleaved stereo buffer (L, R, L, R, …) rendered at <paramref name="sampleRate"/> Hz.</summary>
        public static void PlayStereo(float[] pcm, int sampleRate) {
            if (pcm == null || pcm.Length == 0) {
                return;
            }

            AudioSource voice = Pool()[_next];
            _next = (_next + 1) % Voices;

            var clip = AudioClip.Create("tone", pcm.Length / 2, 2, sampleRate, false);
            clip.SetData(pcm, 0);
            voice.Stop();
            voice.volume = 1f; // synth bakes amplitude into the buffer; clear any prior PlayClip volume
            voice.clip = clip;
            voice.Play();
        }

        /// <summary>
        /// Play a pre-built (typically cached) clip at <paramref name="volume"/> on a pooled voice.
        /// Unlike <see cref="PlayStereo"/> this allocates nothing per call — the caller owns the clip
        /// — so it suits cues fired often (e.g. one per cursor step).
        /// </summary>
        public static void PlayClip(AudioClip clip, float volume) {
            if (clip == null) {
                return;
            }

            AudioSource voice = Pool()[_next];
            _next = (_next + 1) % Voices;
            voice.Stop();
            voice.volume = volume;
            voice.clip = clip;
            voice.Play();
        }
    }
}
