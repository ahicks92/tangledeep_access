using System;
using System.IO;
using System.Runtime.InteropServices;
using TangledeepAccess.Util;

namespace TangledeepAccess.Native
{
    /// <summary>
    /// Preloads native DLLs by absolute path before any P/Invoke runs.
    ///
    /// BepInEx loads the managed plugin from <c>BepInEx\plugins</c>, which is NOT
    /// on the OS native-DLL search path, so a bare <c>[DllImport("prism")]</c>
    /// would fail to find prism.dll sitting beside the plugin. Calling
    /// <c>LoadLibrary</c> with the full path puts the module in the process by its
    /// base name; the later by-name P/Invoke then binds to the already-loaded
    /// module. prism.dll is self-contained (its screen-reader clients, including the
    /// NVDA controller, are statically linked; verified against its import table),
    /// so nothing else needs preloading.
    /// </summary>
    public static class NativeLoader
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        /// <summary>
        /// Preload the vendored Prism runtime from <paramref name="directory"/>
        /// (the folder holding the plugin DLL). Returns true if prism.dll loaded.
        /// </summary>
        public static bool LoadPrism(string directory)
        {
            return Preload(Path.Combine(directory, "prism.dll"), required: true);
        }

        private static bool Preload(string path, bool required)
        {
            if (!File.Exists(path))
            {
                if (required)
                    Log.Error("native: missing " + path);
                return false;
            }
            var handle = LoadLibraryW(path);
            if (handle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Log.Error("native: LoadLibrary failed (" + err + ") for " + path);
                return false;
            }
            Log.Info("native: loaded " + Path.GetFileName(path));
            return true;
        }
    }
}
