using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TangledeepAccess.Speech
{
    /// <summary>
    /// Raw P/Invoke surface for prism.dll (Prism v0.16.x, https://github.com/ethindp/prism).
    ///
    /// ABI facts pinned from include/prism.h (vendored at third_party/prism/prism.h):
    ///   - Calling convention is <c>__cdecl</c> on Windows (PRISM_CALL).
    ///   - All strings crossing the boundary are UTF-8 (the API has a dedicated
    ///     PRISM_ERROR_INVALID_UTF8). .NET Framework / Unity-Mono net472 has neither
    ///     <c>UnmanagedType.LPUTF8Str</c> nor <c>Marshal.PtrToStringUTF8</c> in its
    ///     reference surface, so we marshal UTF-8 by hand: inbound strings are passed
    ///     as NUL-terminated <c>byte[]</c> (see <see cref="ToUtf8"/>), and returned
    ///     <c>const char*</c> are decoded with <see cref="FromUtf8"/>.
    ///   - C <c>bool</c> is one byte: every bool here is <c>UnmanagedType.I1</c>.
    ///   - C <c>size_t</c> is pointer-width: bound as <see cref="UIntPtr"/>.
    ///   - PrismContext* / PrismBackend* are opaque: bound as <see cref="IntPtr"/>.
    ///
    /// This is the complete exported surface; the managed wrapper (<see cref="PrismSpeech"/>)
    /// uses only a subset, but the full binding is kept so later work does not re-derive it.
    /// </summary>
    internal static class PrismNative
    {
        // The native module's base name. NativeLoader preloads the full path from the
        // plugin folder first, so this by-name reference resolves to the loaded module.
        private const string Dll = "prism";

        /// <summary>PrismConfig: a single version byte (see PRISM_CONFIG_VERSION).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct PrismConfig
        {
            public byte Version;
        }

        /// <summary>Current config schema version expected by prism_init.</summary>
        public const byte ConfigVersion = 2;

        public enum PrismError
        {
            Ok = 0,
            NotInitialized,
            InvalidParam,
            NotImplemented,
            NoVoices,
            VoiceNotFound,
            SpeakFailure,
            MemoryFailure,
            RangeOutOfBounds,
            Internal,
            NotSpeaking,
            NotPaused,
            AlreadyPaused,
            InvalidUtf8,
            InvalidOperation,
            AlreadyInitialized,
            BackendNotAvailable,
            Unknown,
            InvalidAudioFormat,
            InternalBackendLimitExceeded,
            BackendEnteredUndefinedState,
            Count,
        }

        /// <summary>speak_to_memory audio sink. cdecl; samples are interleaved floats.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void PrismAudioCallback(
            IntPtr userdata,
            IntPtr samples,
            UIntPtr sampleCount,
            UIntPtr channels,
            UIntPtr sampleRate
        );

        // --- Lifecycle ---

        // Returns PrismConfig by value (one byte, returned in a register). We normally
        // construct the config ourselves to sidestep any small-struct-return ABI quirk,
        // but bind it for completeness.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismConfig prism_config_init();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_init(ref PrismConfig cfg);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_shutdown(IntPtr ctx);

        // --- Registry ---

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr prism_registry_count(IntPtr ctx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong prism_registry_id_at(IntPtr ctx, UIntPtr index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong prism_registry_id(IntPtr ctx, byte[] utf8Name);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_registry_name(IntPtr ctx, ulong id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_registry_priority(IntPtr ctx, ulong id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool prism_registry_exists(IntPtr ctx, ulong id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_registry_get(IntPtr ctx, ulong id);

        // Allocating constructors: the returned backend is owned by the caller and
        // must be released with prism_backend_free.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_registry_create(IntPtr ctx, ulong id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_registry_create_best(IntPtr ctx);

        // Non-owning accessors: do NOT free the returned backend.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_registry_acquire(IntPtr ctx, ulong id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_registry_acquire_best(IntPtr ctx);

        // --- Backend ---

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_backend_free(IntPtr backend);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_backend_name(IntPtr backend);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong prism_backend_get_features(IntPtr backend);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_initialize(IntPtr backend);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_speak(
            IntPtr backend,
            byte[] utf8Text,
            [MarshalAs(UnmanagedType.I1)] bool interrupt
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_speak_to_memory(
            IntPtr backend,
            byte[] utf8Text,
            PrismAudioCallback callback,
            IntPtr userdata
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_braille(IntPtr backend, byte[] utf8Text);

        // "output" = the screen-reader path (speech + braille as the backend supports),
        // mirroring Tolk's Output. Preferred for screen-reader backends; speak is TTS-only.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_output(
            IntPtr backend,
            byte[] utf8Text,
            [MarshalAs(UnmanagedType.I1)] bool interrupt
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_stop(IntPtr backend);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_pause(IntPtr backend);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_resume(IntPtr backend);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_is_speaking(
            IntPtr backend,
            [MarshalAs(UnmanagedType.I1)] out bool outSpeaking
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_set_volume(IntPtr backend, float volume);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_set_rate(IntPtr backend, float rate);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_set_pitch(IntPtr backend, float pitch);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_get_volume(
            IntPtr backend,
            out float outVolume
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_get_rate(IntPtr backend, out float outRate);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_get_pitch(IntPtr backend, out float outPitch);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_refresh_voices(IntPtr backend);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_count_voices(
            IntPtr backend,
            out UIntPtr outCount
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_get_voice_name(
            IntPtr backend,
            UIntPtr voiceId,
            out IntPtr outName
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_get_voice_language(
            IntPtr backend,
            UIntPtr voiceId,
            out IntPtr outLanguage
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_set_voice(IntPtr backend, UIntPtr voiceId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_get_voice(
            IntPtr backend,
            out UIntPtr outVoiceId
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_get_channels(
            IntPtr backend,
            out UIntPtr outChannels
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_get_sample_rate(
            IntPtr backend,
            out UIntPtr outSampleRate
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_backend_get_bit_depth(
            IntPtr backend,
            out UIntPtr outBitDepth
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_error_string(PrismError error);

        // --- UTF-8 marshaling helpers (hand-rolled; see class remarks) ---

        /// <summary>Encode a managed string as a NUL-terminated UTF-8 byte buffer.</summary>
        public static byte[] ToUtf8(string s)
        {
            if (s == null)
                s = string.Empty;
            int len = Encoding.UTF8.GetByteCount(s);
            var buf = new byte[len + 1];
            Encoding.UTF8.GetBytes(s, 0, s.Length, buf, 0);
            buf[len] = 0;
            return buf;
        }

        /// <summary>Decode a NUL-terminated UTF-8 <c>const char*</c> the library owns.</summary>
        public static string FromUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return null;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0)
                len++;
            if (len == 0)
                return string.Empty;
            var buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        /// <summary>Human-readable text for a PrismError, via the library's own table.</summary>
        public static string ErrorString(PrismError error)
        {
            return FromUtf8(prism_error_string(error));
        }
    }
}
