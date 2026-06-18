using UnityEngine;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// The keymap: the one place physical keys are read and turned into a <see cref="ModInputAction"/>.
    /// Reads raw <c>UnityEngine.Input</c> — the mod's keys are chosen from those the Default control
    /// layout leaves unbound (see docs/controls.md), so claiming them shadows no game action. Methods
    /// are grouped by the context that consults each set; a context composes the groups it honors.
    /// </summary>
    internal static class InputKeys {
        /// <summary>Menu navigation: arrows step focus, Enter confirms. Orthogonal only.</summary>
        public static ModInputAction? MenuNav() {
            if (Input.GetKeyDown(KeyCode.UpArrow)) {
                return ModInputAction.Move(0, 1);
            }
            if (Input.GetKeyDown(KeyCode.DownArrow)) {
                return ModInputAction.Move(0, -1);
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) {
                return ModInputAction.Move(-1, 0);
            }
            if (Input.GetKeyDown(KeyCode.RightArrow)) {
                return ModInputAction.Move(1, 0);
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) {
                return ModInputAction.Of(ModInputKind.Confirm);
            }

            return null;
        }

        /// <summary>
        /// True while any menu-nav key is held (not just the key-down frame). Lets a context whose
        /// own pump auto-repeats (the title screen) stay suppressed on the repeat frames, so a held
        /// key cannot leak through and double-step focus alongside us.
        /// </summary>
        public static bool AnyMenuNavHeld() {
            return Input.GetKey(KeyCode.UpArrow)
                || Input.GetKey(KeyCode.DownArrow)
                || Input.GetKey(KeyCode.LeftArrow)
                || Input.GetKey(KeyCode.RightArrow)
                || Input.GetKey(KeyCode.Return)
                || Input.GetKey(KeyCode.KeypadEnter);
        }

        /// <summary>
        /// Free-play query hotkeys, live in both Gameplay and Look: K read here, L scan, Y status,
        /// A hotbar, apostrophe repeat, slash help, semicolon toggle the look cursor.
        /// </summary>
        public static ModInputAction? Query() {
            if (Input.GetKeyDown(KeyCode.K)) {
                return ModInputAction.Of(ModInputKind.ReadHere);
            }
            if (Input.GetKeyDown(KeyCode.L)) {
                return ModInputAction.Of(ModInputKind.Scan);
            }
            if (Input.GetKeyDown(KeyCode.Y)) {
                return ModInputAction.Of(ModInputKind.ReadStatus);
            }
            if (Input.GetKeyDown(KeyCode.A)) {
                return ModInputAction.Of(ModInputKind.ReadHotbar);
            }
            if (Input.GetKeyDown(KeyCode.Quote)) {
                return ModInputAction.Of(ModInputKind.RepeatLast);
            }
            if (Input.GetKeyDown(KeyCode.Slash)) {
                return ModInputAction.Of(ModInputKind.Help);
            }
            if (Input.GetKeyDown(KeyCode.Semicolon)) {
                return ModInputAction.Of(ModInputKind.LookToggle);
            }

            return null;
        }

        /// <summary>
        /// Look-cursor control, consulted only while the cursor is active: Home recenters, arrows
        /// and numpad step it (8-way, +x east +y north, mirroring the game's movement keys),
        /// brackets jump between points of interest in view.
        /// </summary>
        public static ModInputAction? LookMove() {
            if (Input.GetKeyDown(KeyCode.Home)) {
                return ModInputAction.Of(ModInputKind.LookRecenter);
            }
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Keypad8)) {
                return ModInputAction.Move(0, 1);
            }
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.Keypad2)) {
                return ModInputAction.Move(0, -1);
            }
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Keypad6)) {
                return ModInputAction.Move(1, 0);
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Keypad4)) {
                return ModInputAction.Move(-1, 0);
            }
            if (Input.GetKeyDown(KeyCode.Keypad9)) {
                return ModInputAction.Move(1, 1);
            }
            if (Input.GetKeyDown(KeyCode.Keypad7)) {
                return ModInputAction.Move(-1, 1);
            }
            if (Input.GetKeyDown(KeyCode.Keypad3)) {
                return ModInputAction.Move(1, -1);
            }
            if (Input.GetKeyDown(KeyCode.Keypad1)) {
                return ModInputAction.Move(-1, -1);
            }
            if (Input.GetKeyDown(KeyCode.RightBracket)) {
                return ModInputAction.Of(ModInputKind.LookNextPoi);
            }
            if (Input.GetKeyDown(KeyCode.LeftBracket)) {
                return ModInputAction.Of(ModInputKind.LookPrevPoi);
            }

            return null;
        }
    }
}
