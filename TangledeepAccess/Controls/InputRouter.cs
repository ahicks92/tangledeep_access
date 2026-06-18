using TangledeepAccess.Gameplay;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// The selector and single entry point for both input chokepoints. It picks the active
    /// <see cref="InputContext"/> from live game state and routes the frame to that context's
    /// <see cref="InputHandler"/>. The game has two disjoint input pumps — the in-game
    /// <c>TDInputHandler.UpdateInput</c> and the title <c>TitleScreenScript.Update</c> — so both are
    /// hooked, but the decision of who owns the key lives here once and cannot drift between them.
    /// </summary>
    internal static class InputRouter {
        private static readonly InputHandler Menu = new MenuInputHandler();
        private static readonly InputHandler Look = new LookInputHandler();
        private static readonly InputHandler Gameplay = new GameplayInputHandler();

        /// <summary>In-game pump: a UI owns input, else the look cursor, else free play.</summary>
        public static bool RouteInGame() {
            if (UIManagerScript.AnyInteractableWindowOpen()) {
                return Menu.Route(suppressWhileHeld: false);
            }

            if (LookCursor.Active) {
                return Look.Route(suppressWhileHeld: false);
            }

            return Gameplay.Route(suppressWhileHeld: false);
        }

        /// <summary>
        /// Title pump: only menu overlays exist here (there is no gameplay or look cursor), and the
        /// title auto-repeats, so we keep suppressing while a nav key is held.
        /// </summary>
        public static bool RouteTitle() {
            return Menu.Route(suppressWhileHeld: true);
        }
    }
}
