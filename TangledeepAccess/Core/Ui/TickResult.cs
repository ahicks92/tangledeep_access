using TangledeepAccess.Speech;

namespace TangledeepAccess.Ui {
    /// <summary>
    /// What one <see cref="OverlayDispatcher.Tick"/> produced for the glue to act on. The
    /// dispatcher is BCL-only, so it cannot speak, play sounds, or move the game's focus
    /// itself; it returns this and the (Unity-aware) caller does so on the main thread.
    /// </summary>
    public sealed class TickResult {
        /// <summary>The message to speak this tick, or null when there is nothing to say. Carried
        /// as the builder, not a built string, so it reaches <see cref="PrismSpeech.Speak"/>
        /// unflattened — the dispatcher never calls <c>.Build()</c> itself.</summary>
        public MessageBuilder Message;

        /// <summary>True if our cursor moved under our own navigation — the caller plays the
        /// game's move sound and syncs the game focus to <see cref="FocusReference"/>.</summary>
        public bool Moved;

        /// <summary>True if the player activated a game-backed control with no mod-side
        /// handler — the caller confirms it through the game (e.g. CursorConfirm).</summary>
        public bool Activated;

        /// <summary>The focused node's backing game object (e.g. a UIObject) to sync the
        /// game's focus to, or null when the node maps to no game control.</summary>
        public object FocusReference;

        /// <summary>The shared empty result (nothing to do this tick).</summary>
        public static readonly TickResult Empty = new TickResult();
    }
}
