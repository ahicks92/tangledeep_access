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
        /// True while any look-cursor directional (arrows or numpad 1-9 less 5) is held — not just
        /// the key-down frame. Lets the look drainer keep suppressing a held movement key on its
        /// repeat frames so it cannot leak to the game and walk the hero alongside the cursor.
        /// </summary>
        public static bool AnyLookDirectionalHeld() {
            return Input.GetKey(KeyCode.UpArrow)
                || Input.GetKey(KeyCode.DownArrow)
                || Input.GetKey(KeyCode.LeftArrow)
                || Input.GetKey(KeyCode.RightArrow)
                || Input.GetKey(KeyCode.Keypad1)
                || Input.GetKey(KeyCode.Keypad2)
                || Input.GetKey(KeyCode.Keypad3)
                || Input.GetKey(KeyCode.Keypad4)
                || Input.GetKey(KeyCode.Keypad6)
                || Input.GetKey(KeyCode.Keypad7)
                || Input.GetKey(KeyCode.Keypad8)
                || Input.GetKey(KeyCode.Keypad9);
        }

        /// <summary>Semicolon toggles the look cursor. The look drainer claims it in both states —
        /// off to turn on, on to turn off — so the cursor owns its whole lifecycle.</summary>
        public static ModInputAction? LookToggle() {
            if (Input.GetKeyDown(KeyCode.Semicolon)) {
                return ModInputAction.Of(ModInputKind.LookToggle);
            }

            return null;
        }

        /// <summary>
        /// Free-play query hotkeys, the gameplay drainer's set: K read here, L scan, Y status,
        /// A hotbar, apostrophe repeat, slash help.
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

            return null;
        }

        /// <summary>
        /// Audio volume nudges: F8 music, F9 sound effects, F10 footsteps. Plain press lowers,
        /// Shift raises (Dx = -1 / +1). A deliberately hacky one-time tuning aid — the game's
        /// default music volume drowns out speech and cues — not a polished settings UI. The game
        /// leaves F8-F10 unbound, and the gameplay drainer suppresses the frame, so this shadows
        /// nothing.
        /// </summary>
        public static ModInputAction? Volume() {
            ModInputKind kind;
            if (Input.GetKeyDown(KeyCode.F8)) {
                kind = ModInputKind.VolumeMusic;
            } else if (Input.GetKeyDown(KeyCode.F9)) {
                kind = ModInputKind.VolumeSfx;
            } else if (Input.GetKeyDown(KeyCode.F10)) {
                kind = ModInputKind.VolumeFootsteps;
            } else {
                return null;
            }

            bool up = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return new ModInputAction { Kind = kind, Dx = up ? 1 : -1 };
        }

        /// <summary>
        /// Scanner navigation, the scanner drainer's set — Factorio Access's Page Up/Down family:
        /// plain Page Up/Down step between entries (Factorio's subcategory axis), Ctrl + Page Up/Down
        /// step between categories. Shift + Page Up/Down is Factorio's instance axis, which we have
        /// not built yet, so we leave it unclaimed (pass through) for now rather than swallow it. The
        /// game binds Page Up/Down only to in-list paging, which never applies in free play, so
        /// claiming them shadows nothing here. Modeless: the scanner keeps its selection between presses.
        /// </summary>
        public static ModInputAction? ScannerNav() {
            bool up = Input.GetKeyDown(KeyCode.PageUp);
            bool down = Input.GetKeyDown(KeyCode.PageDown);
            if (!up && !down) {
                return null;
            }

            // Shift is reserved for the future instance axis — leave it to the game until we build it.
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) {
                return null;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl) {
                return ModInputAction.Of(up ? ModInputKind.ScanPrevCategory : ModInputKind.ScanNextCategory);
            }

            return ModInputAction.Of(up ? ModInputKind.ScanPrevEntry : ModInputKind.ScanNextEntry);
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
