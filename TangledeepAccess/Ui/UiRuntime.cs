using TangledeepAccess.Ui;

namespace TangledeepAccess
{
    /// <summary>
    /// Process-wide handle to the live <see cref="OverlayDispatcher"/>. The dispatcher is
    /// created in <c>Plugin.Awake</c>; static Harmony patches (which cannot hold instance
    /// state) reach it here to record game-driven focus changes. Outside Core/ but
    /// Unity-free — it only holds the reference.
    /// </summary>
    internal static class UiRuntime
    {
        internal static OverlayDispatcher Dispatcher;
    }
}
