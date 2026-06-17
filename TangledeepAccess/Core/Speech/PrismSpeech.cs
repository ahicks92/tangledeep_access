using System;
using TangledeepAccess.Util;

namespace TangledeepAccess.Speech {
    /// <summary>
    /// Managed lifetime wrapper over the Prism context + a single owned backend.
    /// One instance speaks for the whole mod. All native handles stay inside here;
    /// callers deal only in strings.
    ///
    /// The native prism.dll must already be loaded into the process before
    /// <see cref="Initialize"/> runs (the plugin preloads it by full path via
    /// NativeLoader), otherwise the first P/Invoke throws DllNotFoundException.
    /// </summary>
    public sealed class PrismSpeech : IDisposable {
        /// <summary>
        /// Optional tap invoked with every non-empty string sent to speech. The dev
        /// server sets this to capture spoken text for read-back (it can't hear TTS).
        /// Null in normal play. Kept here because Speak is the single speech chokepoint.
        /// </summary>
#pragma warning disable CS0649 // assigned in the plugin build (Dev/DevServer), not in the test build
        internal static Action<string> Observer;
#pragma warning restore CS0649

        private IntPtr _ctx;
        private IntPtr _backend;

        /// <summary>True once a backend was created and initialized successfully.</summary>
        public bool Available { get; private set; }

        /// <summary>Name of the chosen backend (e.g. "NVDA", "SAPI"), once available.</summary>
        public string BackendName { get; private set; }

        /// <summary>
        /// Stand up the Prism context and acquire the best available backend.
        /// Returns true on success. On any failure it logs the cause and leaves
        /// the instance unavailable rather than throwing, so a missing screen
        /// reader degrades to silence instead of crashing the game.
        /// </summary>
        public bool Initialize() {
            if (Available) {
                return true;
            }

            var cfg = new PrismNative.PrismConfig { Version = PrismNative.ConfigVersion };
            _ctx = PrismNative.prism_init(ref cfg);
            if (_ctx == IntPtr.Zero) {
                Log.Error("Prism: prism_init returned null context");
                return false;
            }

            // create_best gives us an owned backend (freed in Dispose). It picks the
            // highest-priority backend that is usable at runtime (running screen reader,
            // else SAPI).
            _backend = PrismNative.prism_registry_create_best(_ctx);
            if (_backend == IntPtr.Zero) {
                Log.Error(
                    "Prism: no speech backend available (prism_registry_create_best returned null)"
                );
                PrismNative.prism_shutdown(_ctx);
                _ctx = IntPtr.Zero;
                return false;
            }

            var err = PrismNative.prism_backend_initialize(_backend);
            if (
                err != PrismNative.PrismError.Ok
                && err != PrismNative.PrismError.AlreadyInitialized
            ) {
                Log.Error("Prism: backend initialize failed: " + PrismNative.ErrorString(err));
                PrismNative.prism_backend_free(_backend);
                PrismNative.prism_shutdown(_ctx);
                _backend = IntPtr.Zero;
                _ctx = IntPtr.Zero;
                return false;
            }

            BackendName =
                PrismNative.FromUtf8(PrismNative.prism_backend_name(_backend)) ?? "unknown";
            Available = true;
            Log.Info("Prism speech ready, backend: " + BackendName);
            return true;
        }

        /// <summary>
        /// Speak <paramref name="text"/> through the screen-reader output path
        /// (speech plus braille where the backend supports it). No-op when
        /// unavailable. <paramref name="interrupt"/> cuts off current speech.
        /// </summary>
        public void Speak(string text, bool interrupt = true) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            // Capture for the dev /speech tap BEFORE the availability gate, so the driver can
            // read spoken text even when Prism is disabled (headless/overnight, no NVDA).
            Observer?.Invoke(text);

            if (!Available) {
                return;
            }

            var err = PrismNative.prism_backend_output(
                _backend,
                PrismNative.ToUtf8(text),
                interrupt
            );
            if (err != PrismNative.PrismError.Ok) {
                Log.Warn("Prism: output failed: " + PrismNative.ErrorString(err));
            }
        }

        /// <summary>Silence any in-progress speech.</summary>
        public void Stop() {
            if (!Available) {
                return;
            }

            PrismNative.prism_backend_stop(_backend);
        }

        public void Dispose() {
            if (_backend != IntPtr.Zero) {
                PrismNative.prism_backend_free(_backend);
                _backend = IntPtr.Zero;
            }
            if (_ctx != IntPtr.Zero) {
                PrismNative.prism_shutdown(_ctx);
                _ctx = IntPtr.Zero;
            }
            Available = false;
        }
    }
}
