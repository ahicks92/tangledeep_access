using TangledeepAccess.Gameplay;
using TangledeepAccess.Speech;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// The hotbar keys (backtick cycle, A read), at the very top of the in-game priority chain —
    /// above even the menu drainer — so the hotbar stays reachable while a full-screen overlay owns
    /// input. That lets the player cycle to the page they want before assigning a skill in the slot
    /// sheet, and read their bar from anywhere. The hotbar is global game state, so reading and
    /// cycling it are always meaningful in play; it claims only its own two keys and passes the rest.
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
            // Cycle flips the page first; both keys then read the (now-active) page.
            if (action.Kind == ModInputKind.CycleHotbar) {
                Hotbar.Cycle();
            }

            Hotbar.Read(message);
            speech.Speak(message);
        }
    }
}
