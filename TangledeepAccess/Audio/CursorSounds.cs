using System.IO;
using TangledeepAccess.Util;
using UnityEngine;

namespace TangledeepAccess.Audio {
    /// <summary>
    /// The exploration cursor's audio cues: ground, impassable, entity, and the skip tick. Loads the
    /// four embedded WAVs once (decoded via <see cref="WavData"/> into cached <see cref="AudioClip"/>s)
    /// and plays them through the shared <see cref="TonePlayer"/> voice pool, so several cues can fire
    /// the same frame (impassable + entity, or skip + the landing tile) without cutting each other.
    /// Engine glue: <c>AudioClip</c> creation must run on the Unity main thread, which is where the
    /// pump realizes cursor input.
    /// </summary>
    public static class CursorSounds {
        // Per-cue playback volume (0..1). Tune freely.
        public const float GroundVolume = 0.5f;
        public const float ImpassableVolume = 0.6f;
        public const float EntityVolume = 0.7f;
        public const float SkippedVolume = 0.5f;

        private static AudioClip _ground;
        private static AudioClip _impassable;
        private static AudioClip _entity;
        private static AudioClip _skipped;
        private static bool _loaded;

        public static void PlayGround() {
            EnsureLoaded();
            TonePlayer.PlayClip(_ground, GroundVolume);
        }

        public static void PlayImpassable() {
            EnsureLoaded();
            TonePlayer.PlayClip(_impassable, ImpassableVolume);
        }

        public static void PlayEntity() {
            EnsureLoaded();
            TonePlayer.PlayClip(_entity, EntityVolume);
        }

        public static void PlaySkipped() {
            EnsureLoaded();
            TonePlayer.PlayClip(_skipped, SkippedVolume);
        }

        private static void EnsureLoaded() {
            if (_loaded) {
                return;
            }

            _loaded = true; // set first: a failed load logs and leaves null clips (PlayClip no-ops)
            _ground = Load("cursor_ground.wav");
            _impassable = Load("cursor_impassable.wav");
            _entity = Load("cursor_entity.wav");
            _skipped = Load("cursor_skipped.wav");
        }

        private static AudioClip Load(string file) {
            string resource = "TangledeepAccess.Sounds." + file;
            using (Stream stream = typeof(CursorSounds).Assembly.GetManifestResourceStream(resource)) {
                if (stream == null) {
                    Log.Warn("Cursor sound resource missing: " + resource);
                    return null;
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
                AudioClip clip = AudioClip.Create(file, wav.Samples.Length / wav.Channels, wav.Channels, wav.SampleRate, false);
                clip.SetData(wav.Samples, 0);
                return clip;
            }
        }
    }
}
