using HarmonyLib;
using TangledeepAccess.Ui;

namespace TangledeepAccess.Patches {
    /// <summary>
    /// Title-screen input chokepoint. The new-game flow runs in title context, where
    /// <c>TDInputHandler.UpdateInput</c> (our in-game hook) is never called — the title menu,
    /// slot screen, and story dialogs are all pumped by <c>TitleScreenScript.Update</c>. That
    /// method also runs the background-scroll animation, so we must not blunt-suppress it.
    ///
    /// <para>Instead we engage only when our active overlay has <em>explicitly</em> claimed
    /// input (<see cref="OverlayDispatcher.CapturesInputExplicitly"/>) — today just the
    /// title-flow dialog overlay — and only on a frame where a nav/confirm key we handle is
    /// actually down. On that frame we stash the command for the pump and return false to skip
    /// the title's own handling; the pump drives the dialog (focus + CursorConfirm) deterministically.
    /// Every other frame (and every other title screen — the menu and slot screen, which only
    /// capture via the generic <c>nodes&gt;1</c> rule, never the explicit flag) passes straight
    /// through, leaving the animation and the game's own navigation untouched.</para>
    /// </summary>
    [HarmonyPatch(typeof(TitleScreenScript), "Update")]
    internal static class TitleScreenScript_Update_Patch {
        private static bool Prefix() {
            OverlayDispatcher dispatcher = UiRuntime.Dispatcher;
            if (dispatcher == null || !dispatcher.CapturesInputExplicitly) {
                return true; // no overlay owns the title input this frame — game runs normally
            }

            NavCommand? command = MenuInput.ReadNavKey();
            if (command.HasValue) {
                UiRuntime.SetPendingNav(command.Value);
                return false; // our overlay owns it; suppress the title's input this frame
            }

            // No key-down this frame, but if a nav key is still held, keep suppressing so the
            // game's own repeat can't move focus alongside us (a doubled step). Only a frame with
            // no relevant key held falls through to the title pump (animation, etc.).
            return !MenuInput.AnyNavKeyHeld();
        }
    }
}
