namespace TangledeepAccess.Dev {
    /// <summary>
    /// Injects logical UI controls by calling the game's own handlers, NOT OS synthetic keys:
    /// we drive the game while its window is unfocused, where SendInput (needs foreground) and
    /// PostMessage (won't reach Rewired's raw input) don't work. Confirm routes through the
    /// game's single CursorConfirm dispatcher; directional moves walk the focused UIObject's
    /// neighbor compass (the same orthogonal slots GameMenuMirror mirrors) via ChangeUIFocus,
    /// which also trips the mod's focus hook so the move gets spoken.
    ///
    /// This covers the uiObjectFocus menu model (title, dialogs, most screens). Save-slot and
    /// in-game movement have their own paths and will be added as verbs when needed.
    /// </summary>
    internal static class InputInjector {
        // UIObject.neighbors is an 8-slot compass; orthogonals only (matches GameMenuMirror).
        private const int Up = 0;
        private const int Right = 2;
        private const int Down = 4;
        private const int Left = 6;

        public static string Inject(string verb) {
            switch ((verb ?? "").Trim().ToLowerInvariant()) {
                case "confirm":
                case "enter":
                case "ok":
                    UIManagerScript ums = UIManagerScript.singletonUIMS;
                    if (ums == null) {
                        return "confirm: no UIManagerScript\n";
                    }
                    ums.CursorConfirm();
                    return "confirm -> CursorConfirm()\n";
                case "up":
                    return Move(Up, "up");
                case "right":
                    return Move(Right, "right");
                case "down":
                    return Move(Down, "down");
                case "left":
                    return Move(Left, "left");
                default:
                    return "[unknown verb] '" + verb + "' - use up|down|left|right|confirm\n";
            }
        }

        private static string Move(int slot, string name) {
            UIManagerScript.UIObject focus = UIManagerScript.uiObjectFocus;
            if (focus == null) {
                return name + ": no uiObjectFocus to move from\n";
            }
            UIManagerScript.UIObject[] neighbors = focus.neighbors;
            if (neighbors == null || slot >= neighbors.Length || neighbors[slot] == null) {
                return name + ": no neighbor in that direction\n";
            }
            UIManagerScript.ChangeUIFocusAndAlignCursor(neighbors[slot]);
            UIManagerScript.UIObject now = UIManagerScript.uiObjectFocus;
            return name + " -> focus now " + (now != null && now.gameObj != null ? now.gameObj.name : "?") + "\n";
        }
    }
}
