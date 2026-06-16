namespace TangledeepAccess.Util
{
    /// <summary>
    /// Tiny logging seam. Core code logs through here; the plugin installs a sink
    /// that routes to the BepInEx logger. Keeping the sink injectable lets Core
    /// stay free of any BepInEx/Unity reference and lets tests capture output.
    ///
    /// The mod runs on P/Invoke and (soon) Harmony patches that fail invisibly,
    /// so failures must be logged, never swallowed.
    /// </summary>
    public static class Log
    {
        public interface ISink
        {
            void Info(string message);
            void Warn(string message);
            void Error(string message);
        }

        private static ISink _sink;

        public static void Install(ISink sink) => _sink = sink;

        public static void Info(string message) => _sink?.Info(message);

        public static void Warn(string message) => _sink?.Warn(message);

        public static void Error(string message) => _sink?.Error(message);
    }
}
