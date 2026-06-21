using TangledeepAccess.Speech;

namespace TangledeepAccess.Ui {
    /// <summary>Keyboard modifier state for an interaction. Garbage but non-null for non-key events.</summary>
    public struct Modifiers {
        public bool Control;
        public bool Alt;
        public bool Shift;

        public static readonly Modifiers None = new Modifiers();
    }

    /// <summary>
    /// Operations a control callback can perform on the owning graph. Supplied by the
    /// graph engine; do not construct directly. Kept minimal — grows as behaviors land.
    /// </summary>
    public interface IOverlayController {
        /// <summary>Close this overlay (the dispatcher drops its focus cache). For an auxiliary
        /// overlay this is a plain <i>cancel</i>: it closes and refocuses the main overlay's anchor
        /// node without firing the anchor's <see cref="Graph.NodeVtable.OnAuxCommit"/>.</summary>
        void Close();

        /// <summary>
        /// Ask the graph to silently move focus to <paramref name="key"/> on the next
        /// render, if that control still exists. Used when an action restructures the UI.
        /// </summary>
        void SuggestMove(ControlId key);

        /// <summary>
        /// Open <paramref name="aux"/> as an auxiliary (modal) overlay anchored to the focused
        /// control of the current overlay. While it is open, input drives the aux; the main overlay's
        /// focus is preserved and restored when the aux closes. The aux carries no commit closure —
        /// it reports a scalar result via <see cref="CommitAuxiliary"/>.
        /// </summary>
        void OpenAuxiliary(IUiOverlay aux);

        /// <summary>
        /// Close the active auxiliary overlay and deliver <paramref name="result"/> to the anchor
        /// node's <see cref="Graph.NodeVtable.OnAuxCommit"/> (which runs against the main overlay's
        /// live state on its next rebuild). Called from an aux node's own action.
        /// </summary>
        void CommitAuxiliary(int result);
    }

    /// <summary>
    /// Context passed to node/transition callbacks. Callbacks append speech to
    /// <see cref="Message"/>; the dispatcher speaks the built result. No engine type
    /// here touches Unity — concrete overlays supply callbacks that read game state, but
    /// the framework only invokes them.
    /// </summary>
    public sealed class OverlayCtx {
        public MessageBuilder Message { get; }
        public Modifiers Modifiers { get; }

        /// <summary>Set by the engine before invoking callbacks; may be null in bare tests.</summary>
        public IOverlayController Controller { get; internal set; }

        /// <summary>An integer payload for the triggering command, set by the dispatcher before
        /// invoking a node action — currently the 1-8 slot for <see cref="Controls.ModInputKind.AssignHotbar"/>.
        /// Zero for commands that carry no argument.</summary>
        public int Arg { get; internal set; }

        /// <summary>A secondary integer payload, set by the dispatcher before invoking a node action —
        /// currently the hotbar bank (0 = bar 1, 1 = bar 2) for
        /// <see cref="Controls.ModInputKind.AssignHotbar"/>. Zero for commands that carry no bank.</summary>
        public int Bank { get; internal set; }

        public OverlayCtx(MessageBuilder message, Modifiers modifiers) {
            Message = message;
            Modifiers = modifiers;
        }
    }
}
