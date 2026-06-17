using TangledeepAccess.Ui;
using UnityEngine;

namespace TangledeepAccess.Patches {
    /// <summary>
    /// Shared keyboard mapping for menu/dialog navigation, used by every input chokepoint we
    /// hook (the in-game <c>TDInputHandler.UpdateInput</c> and the title-screen
    /// <c>TitleScreenScript.Update</c>). One place so the keys can never drift between contexts.
    /// </summary>
    internal static class MenuInput {
        /// <summary>The nav/confirm key pressed this frame, or null. Arrows move; Enter activates.</summary>
        public static NavCommand? ReadNavKey() {
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
