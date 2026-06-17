using TangledeepAccess.Ui;

namespace TangledeepAccess {
    /// <summary>
    /// Process-wide handle to the live <see cref="OverlayDispatcher"/> and the one-slot
    /// pending navigation command. The dispatcher is created in <c>Plugin.Awake</c>; static
    /// Harmony patches (which cannot hold instance state) reach it here to record game-driven
    /// focus changes (<see cref="OverlayDispatcher.RecordGameFocus"/>) and to hand the
    /// per-frame nav command from the input hook to the pump. Outside Core/ but Unity-free.
    /// </summary>
    internal static class UiRuntime {
        internal static OverlayDispatcher Dispatcher;

        // Set by the input hook (which runs during the game's input pump), consumed by
        // Plugin.Update. One command per frame; latest wins.
        private static NavCommand? _pendingNav;

        internal static void SetPendingNav(NavCommand command) => _pendingNav = command;

        internal static NavCommand? ConsumePendingNav() {
            NavCommand? cmd = _pendingNav;
            _pendingNav = null;
            return cmd;
        }
    }
}
