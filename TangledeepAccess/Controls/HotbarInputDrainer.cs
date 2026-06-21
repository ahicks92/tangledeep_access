using TangledeepAccess.Gameplay;
using TangledeepAccess.Speech;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// The hotbar READ keys (backtick reads bar 1, Ctrl+backtick reads bar 2), at the very top of
    /// the in-game priority chain — above even the menu drainer — so the bars stay readable while a
    /// full-screen overlay owns input (e.g. check what is on a bar before assigning in the slot
    /// sheet). Reading is always meaningful in play and never mutates game state; the drainer claims
    /// only its read keys and passes the rest. Firing the bars (1-8 / Ctrl+1-8) is the game's own job
    /// — see the input patch's page-flip — not this drainer's.
    /// </summary>
    public sealed class HotbarInputDrainer : InputDrainer {
        public static readonly HotbarInputDrainer Instance = new HotbarInputDrainer();

        public override bool Claim(bool suppressWhileHeld) {
            ModInputAction? action = InputKeys.Hotbar();
            if (action.HasValue) {
                InputQueue.Enqueue(this, action.Value);
                return true;
            }

            return false;
        }

        public override void Realize(ModInputAction action, PrismSpeech speech) {
            var message = new MessageBuilder();
            // Dx carries the bank: 0 (bar 1, backtick) or 1 (bar 2, Ctrl+backtick). Read only.
            Hotbar.Read(message, action.Dx);
            speech.Speak(message);
        }
    }
}
