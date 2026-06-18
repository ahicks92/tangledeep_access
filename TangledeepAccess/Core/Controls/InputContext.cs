namespace TangledeepAccess.Controls {
    /// <summary>
    /// Which input state is active this frame. The selector (<c>InputRouter</c>) decides it from
    /// live game state; it selects both the keymap the hook reads and — carried through the
    /// one-slot pending input to the pump — the realizer that cashes the action out.
    /// </summary>
    public enum InputContext {
        /// <summary>A UI is open and an overlay owns input; the overlay dispatcher realizes it.</summary>
        Menu,

        /// <summary>The exploration cursor is active and owns ALL input; GameplayReader realizes it.</summary>
        Look,

        /// <summary>Free play: our query hotkeys overlay the game's own controls; GameplayReader realizes it.</summary>
        Gameplay,
    }
}
