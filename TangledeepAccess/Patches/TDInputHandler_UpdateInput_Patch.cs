using HarmonyLib;
using TangledeepAccess.Ui;
using UnityEngine;

namespace TangledeepAccess.Patches {
    /// <summary>
    /// Our in-game key handling. TDInputHandler.UpdateInput is the single chokepoint for
    /// in-game input (it is NOT called on the title screen, which is why this naturally
    /// scopes us to gameplay menus and leaves the title flow on the game's own handling).
    ///
    /// When a menu is open and our active overlay is a real navigable tree, we read arrows +
    /// enter ourselves, stash the command for the pump, and return false to skip the game's
    /// input for the frame — replacing the game's navigation. Any other key (or no key, or no
    /// navigable overlay) returns true and passes straight through to the game.
    /// </summary>
    [HarmonyPatch(typeof(TDInputHandler), "UpdateInput")]
    internal static class TDInputHandler_UpdateInput_Patch {
        private static bool Prefix() {
            OverlayDispatcher dispatcher = UiRuntime.Dispatcher;
            if (dispatcher == null || !dispatcher.WantsInputCapture) {
                return true; // nothing to drive — let the game handle input
            }

            if (!UIManagerScript.AnyInteractableWindowOpen()) {
                return true; // only take over while a menu is actually open
            }

            NavCommand? command = ReadNavKey();
            if (!command.HasValue) {
                return true; // unrecognized key — pass through to the game
            }

            UiRuntime.SetPendingNav(command.Value);
            return false; // we handled it; suppress the game's input this frame
        }

        private static NavCommand? ReadNavKey() {
            if (Input.GetKeyDown(KeyCode.UpArrow)) {
                return NavCommand.Up;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow)) {
                return NavCommand.Down;
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow)) {
                return NavCommand.Left;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow)) {
                return NavCommand.Right;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) {
                return NavCommand.Activate;
            }

            return null;
        }
    }
}
