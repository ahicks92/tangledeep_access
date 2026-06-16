using BepInEx.Logging;
using TangledeepAccess.Util;

namespace TangledeepAccess
{
    /// <summary>
    /// Routes the Core <see cref="Log"/> seam to the BepInEx logger. Installed in
    /// Awake so even the earliest setup is captured in BepInEx\LogOutput.log.
    /// </summary>
    internal sealed class LogBepInExBackend : Log.ISink
    {
        private readonly ManualLogSource _log;

        private LogBepInExBackend(ManualLogSource log) => _log = log;

        public static void Install(ManualLogSource log) => Log.Install(new LogBepInExBackend(log));

        public void Info(string message) => _log.LogInfo(message);

        public void Warn(string message) => _log.LogWarning(message);

        public void Error(string message) => _log.LogError(message);
    }
}
