using TangledeepAccess.Speech;

namespace TangledeepAccess.Ui
{
    /// <summary>Keyboard modifier state for an interaction. Garbage but non-null for non-key events.</summary>
    public struct Modifiers
    {
        public bool Control;
        public bool Alt;
        public bool Shift;

        public static readonly Modifiers None = new Modifiers();
    }

    /// <summary>
    /// Operations a control callback can perform on the owning graph. Supplied by the
    /// graph engine; do not construct directly. Kept minimal — grows as behaviors land.
    /// </summary>
    public interface IOverlayController
    {
        /// <summary>Close this overlay (the dispatcher drops its focus cache).</summary>
        void Close();

        /// <summary>
        /// Ask the graph to silently move focus to <paramref name="key"/> on the next
        /// render, if that control still exists. Used when an action restructures the UI.
        /// </summary>
        void SuggestMove(ControlId key);
    }

    /// <summary>
    /// Context passed to node/transition callbacks. Callbacks append speech to
    /// <see cref="Message"/>; the dispatcher speaks the built result. No engine type
    /// here touches Unity — concrete overlays supply callbacks that read game state, but
    /// the framework only invokes them.
    /// </summary>
    public sealed class OverlayCtx
    {
        public MessageBuilder Message { get; }
        public Modifiers Modifiers { get; }

        /// <summary>Set by the engine before invoking callbacks; may be null in bare tests.</summary>
        public IOverlayController Controller { get; internal set; }

        public OverlayCtx(MessageBuilder message, Modifiers modifiers)
        {
            Message = message;
            Modifiers = modifiers;
        }
    }
}
