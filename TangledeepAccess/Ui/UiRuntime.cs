using TangledeepAccess.Controls;

namespace TangledeepAccess {
    /// <summary>
    /// Process-wide handle to the live <see cref="Ui.OverlayDispatcher"/> and the one-slot pending
    /// input. The dispatcher is created in <c>Plugin.Awake</c>; the static input handlers reach it
    /// here to record game-driven focus changes (via the dispatcher) and to hand the per-frame
    /// recognized input from the hook to the pump. Outside Core/ but Unity-free.
    /// </summary>
    internal static class UiRuntime {
        internal static Ui.OverlayDispatcher Dispatcher;

        // Set by the active input handler (which runs during the game's input pump), consumed by
        // Plugin.Update. One recognized input per frame; latest wins. Carries its context so the
        // pump routes it to the right realizer (the overlay dispatcher for menus, GameplayReader for
        // free play) without re-deriving the context.
        private static PendingInput? _pending;

        internal static void SetPendingInput(InputContext context, ModInputAction action) {
            _pending = new PendingInput { Context = context, Action = action };
        }

        internal static PendingInput? ConsumePendingInput() {
            PendingInput? p = _pending;
            _pending = null;
            return p;
        }
    }
}
