using TangledeepAccess.Controls;
using TangledeepAccess.Speech;

namespace TangledeepAccess.Ui {
    /// <summary>
    /// Input for the menu/overlay system, beside the <see cref="OverlayDispatcher"/> it drives. We
    /// recognize only our own nav/confirm keys and pass everything else through, so the game's own
    /// menu hotkeys keep working with no need to enumerate them. We claim keys only when the active
    /// overlay declared it owns input (<see cref="OverlayDispatcher.CapturesInput"/>); a non-capturing
    /// overlay like the save-slot screen leaves navigation to the game.
    /// </summary>
    public sealed class MenuInputDrainer : InputDrainer {
        public static readonly MenuInputDrainer Instance = new MenuInputDrainer();

        public override bool Claim(bool suppressWhileHeld) {
            OverlayDispatcher dispatcher = UiRuntime.Dispatcher;
            bool capturing = dispatcher != null && dispatcher.CapturesInput;
            if (!capturing) {
                return false; // claim nothing this frame — the game runs normally
            }

            ModInputAction? action = InputKeys.MenuNav();
            if (action.HasValue) {
                InputQueue.Enqueue(this, action.Value);
                return true; // an owned key — the pump drives it, suppress the game
            }

            // Not one of our keys: pass through (game hotkeys keep working), unless a nav key is
            // still held in a context that would otherwise auto-repeat over us.
            if (suppressWhileHeld && InputKeys.AnyMenuNavHeld()) {
                return true;
            }

            return false;
        }

        public override void Realize(ModInputAction action, PrismSpeech speech) {
            TickAndAct(action, speech);
        }

        /// <summary>
        /// Tick the dispatcher on an input-free frame so it still follows the game's own focus
        /// changes. Called by the pump only when no menu event was realized this frame.
        /// </summary>
        public void IdleTick(PrismSpeech speech) {
            TickAndAct(null, speech);
        }

        // The dispatcher is BCL-only and cannot touch the engine, so it returns a TickResult and we
        // apply the side effects here: follow the game's focus when we moved under our own nav (and
        // play its move sound, since the game didn't move it — we suppressed its input), confirm a
        // game-backed control when activated, and speak. Whenever we write the game's focus we tell
        // the FocusWatcher, so it does not re-observe our own write as an external FocusChanged echo.
        private static void TickAndAct(ModInputAction? command, PrismSpeech speech) {
            TickResult result = UiRuntime.Dispatcher.Tick(command);

            if (result.Moved && result.FocusReference is UIManagerScript.UIObject moveTarget) {
                UIManagerScript.ChangeUIFocusAndAlignCursor(moveTarget);
                FocusWatcher.NoticeSelfWrite(moveTarget);
                UIManagerScript.PlayCursorSound("Move");
            }

            if (result.Activated) {
                if (result.FocusReference is UIManagerScript.UIObject confirmTarget) {
                    UIManagerScript.ChangeUIFocusAndAlignCursor(confirmTarget);
                    FocusWatcher.NoticeSelfWrite(confirmTarget);
                }

                UIManagerScript.singletonUIMS?.CursorConfirm();
            }

            // Speak no-ops on a null/empty builder, so no guard is needed.
            speech.Speak(result.Message);
        }
    }
}
