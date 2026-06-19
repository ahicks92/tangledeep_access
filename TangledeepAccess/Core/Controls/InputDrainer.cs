using TangledeepAccess.Speech;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// One feature's complete input story, co-located with the feature it serves (the exploration
    /// cursor lives with <c>ExplorationCursor</c>, free-play queries with <c>GameplayReader</c>,
    /// menus beside the overlay dispatcher). A drainer has two halves that run at different times but
    /// read as one unit:
    ///
    /// <list type="bullet">
    /// <item><see cref="Claim"/> runs in the game's input hook. The hook offers each drainer the
    /// frame in priority order (ui → cursor → gameplay); a drainer that does not apply or does not
    /// recognize the key returns false and the next is tried. The first to return true owns the
    /// frame — the hook suppresses the game's own input. Recognizing a key also enqueues it onto
    /// <see cref="InputQueue"/> tagged with <c>this</c>, so the pump knows who realizes it.</item>
    /// <item><see cref="Realize"/> runs in the per-frame pump (<c>Plugin.Update</c>) as the queue is
    /// drained. Each event goes straight back to the drainer that produced it — no central switch,
    /// no re-deriving context. This is where speech and game calls happen; never in the hook.</item>
    /// </list>
    ///
    /// <para>The base is BCL-only; concrete drainers touch the engine and live outside Core. A
    /// drainer is an <see cref="IInputRealizer"/> (the realize seam) plus the claim/suppress half;
    /// a non-keyboard event source implements only <see cref="IInputRealizer"/>.</para>
    /// </summary>
    public abstract class InputDrainer : IInputRealizer {
        /// <summary>
        /// Recognize this feature's keys for the current frame. Return true to claim the frame
        /// (the game's input is suppressed); false to let the chain fall through to the next
        /// drainer and ultimately the game. <paramref name="suppressWhileHeld"/> asks an
        /// auto-repeating context (the title screen) to keep claiming on a held nav key.
        /// </summary>
        public abstract bool Claim(bool suppressWhileHeld);

        /// <summary>
        /// Realize one event this drainer enqueued, on the main thread from the pump. Speaks through
        /// <paramref name="speech"/> and may touch the live game.
        /// </summary>
        public abstract void Realize(ModInputAction action, PrismSpeech speech);
    }
}
