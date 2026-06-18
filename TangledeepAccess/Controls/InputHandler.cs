using TangledeepAccess.Ui;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// One per input context. An InputHandler owns its context's keymap and its frame-suppression
    /// policy: each frame the active hook calls <see cref="Route"/>, which reads our keys, stashes a
    /// <see cref="ModInputAction"/> for the pump when it recognizes one, and reports whether the
    /// game's own input should still run. Recognizing input is ALL that happens in the Harmony hook;
    /// the pump (<c>Plugin.Update</c>) realizes the stashed action against the live game and speaks.
    /// The selector that picks the active handler lives in <see cref="InputRouter"/>.
    /// </summary>
    internal abstract class InputHandler {
        /// <summary>
        /// Read this context's keys; stash a recognized action for the pump. Returns true to let the
        /// game's own input run this frame, false to suppress it. <paramref name="suppressWhileHeld"/>
        /// keeps an auto-repeating context suppressed on a held nav key (see <see cref="InputKeys.AnyMenuNavHeld"/>).
        /// </summary>
        public abstract bool Route(bool suppressWhileHeld);

        protected static void Stash(InputContext context, ModInputAction action) {
            UiRuntime.SetPendingInput(context, action);
        }
    }

    /// <summary>
    /// A UI is open and we drive its navigation. We recognize only our own nav/confirm keys and pass
    /// everything else through, so the game's own menu hotkeys keep working with no need to enumerate
    /// them. We claim keys only when the active overlay declared it owns input
    /// (<c>dispatcher.CapturesInput</c>); a non-capturing overlay like the save-slot screen leaves
    /// navigation to the game.
    /// </summary>
    internal sealed class MenuInputHandler : InputHandler {
        public override bool Route(bool suppressWhileHeld) {
            OverlayDispatcher dispatcher = UiRuntime.Dispatcher;
            bool capturing = dispatcher != null && dispatcher.CapturesInput;
            if (!capturing) {
                return true; // we claim nothing this frame — the game runs normally
            }

            ModInputAction? action = InputKeys.MenuNav();
            if (action.HasValue) {
                Stash(InputContext.Menu, action.Value);
                return false; // an owned key — the pump drives it, suppress the game
            }

            // Not one of our keys: pass through (game hotkeys keep working), unless a nav key is
            // still held in a context that would otherwise auto-repeat over us.
            if (suppressWhileHeld && InputKeys.AnyMenuNavHeld()) {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Free play with no menu open: our query hotkeys overlay the game's own controls. We claim only
    /// our query keys and pass everything else (movement, the game's hotkeys) straight through.
    /// </summary>
    internal sealed class GameplayInputHandler : InputHandler {
        public override bool Route(bool suppressWhileHeld) {
            ModInputAction? action = InputKeys.Query();
            if (action.HasValue) {
                Stash(InputContext.Gameplay, action.Value);
                return false;
            }

            return true; // movement and game hotkeys are the game's
        }
    }

    /// <summary>
    /// The look cursor is active: the player is examining, not acting, so we own ALL input. Both our
    /// query keys and the cursor-movement keys are claimed; every other key is suppressed too — not
    /// just on the key-down frame — so a held movement key cannot leak through on its repeat frames
    /// and walk the hero alongside the cursor. Exit with semicolon (a Query key) to act again.
    /// </summary>
    internal sealed class LookInputHandler : InputHandler {
        public override bool Route(bool suppressWhileHeld) {
            ModInputAction? action = InputKeys.Query() ?? InputKeys.LookMove();
            if (action.HasValue) {
                Stash(InputContext.Look, action.Value);
            }

            return false; // look mode owns the frame whether or not we recognized the key
        }
    }
}
