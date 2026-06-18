using TangledeepAccess.Speech;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// Something the pump can hand a queued event back to for realization. The pump dispatches each
    /// <see cref="PendingInput"/> straight to its producer's <see cref="Realize"/>, so the decision
    /// of who handles an event stays authoritative and is never re-derived from context.
    ///
    /// <para>A physical-key <see cref="InputDrainer"/> is one realizer — but its claim/suppress
    /// half (recognizing a key in the game's input hook and suppressing the game) is a separate
    /// concern. A non-keyboard event source — e.g. the game-focus watcher — is a realizer with no
    /// claim half at all: it never suppresses game input because it owns no key. Splitting the
    /// realize seam out lets both feed the one <see cref="InputQueue"/> and be drained uniformly.</para>
    /// </summary>
    public interface IInputRealizer {
        /// <summary>Realize one event this producer enqueued, on the main thread from the pump.
        /// Speaks through <paramref name="speech"/> and may touch the live game.</summary>
        void Realize(ModInputAction action, PrismSpeech speech);
    }
}
