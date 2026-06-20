using UnityEngine;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// The keymap: the one place physical keys are read and turned into a <see cref="ModInputAction"/>.
    /// Reads raw <c>UnityEngine.Input</c> — the mod's keys are chosen from those the Default control
    /// layout leaves unbound (see docs/controls.md), so claiming them shadows no game action. Methods
    /// are grouped by the context that consults each set; a context composes the groups it honors.
    /// </summary>
    internal static class InputKeys {
        /// <summary>Menu navigation: arrows step focus, Enter confirms, K reads the focused
        /// control's detailed info (the game's hover-tooltip equivalent), F toggles favorite, and
        /// Minus toggles trash. Holding Shift turns a direction into a skip — focus jumps as far as
        /// the row/column reaches (home/end). The arrow keys are mirrored on the WAXD cluster
        /// (W/X up/down, A/D left/right) so the whole menu is reachable one-handed. Orthogonal nav
        /// only. Claimed only while an overlay owns input, so these shadow the game's own bindings
        /// (F is also "fire ranged weapon" in free play, K is the gameplay drainer's "read here",
        /// WAXD are game movement) without conflict — different context, and MenuInputDrainer has
        /// priority.</summary>
        public static ModInputAction? MenuNav() {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) {
                return Nav(shift, 0, 1);
            }
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.X)) {
                return Nav(shift, 0, -1);
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) {
                return Nav(shift, -1, 0);
            }
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) {
                return Nav(shift, 1, 0);
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) {
                // Ctrl+Enter is the "dangerous"/confirmation-required variant: a node that warns on a
                // plain Enter (e.g. selling a favorited item) proceeds only on this. The screen
                // reader's Ctrl "stop" only silences speech, so the combo is free to claim.
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                return ModInputAction.Of(ctrl ? ModInputKind.DangerousConfirm : ModInputKind.Confirm);
            }
            if (Input.GetKeyDown(KeyCode.K)) {
                // Ctrl+K is the second read channel (e.g. the equipment sheet's item comparison);
                // bare K is the primary tooltip read. The screen reader's Ctrl "stop" only silences
                // speech, so the combo is free to claim while we own menu input.
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                return ModInputAction.Of(ctrl ? ModInputKind.ReadSecondary : ModInputKind.ReadInfo);
            }
            if (Input.GetKeyDown(KeyCode.F)) {
                return ModInputAction.Of(ModInputKind.MarkFavorite);
            }
            if (Input.GetKeyDown(KeyCode.Minus)) {
                return ModInputAction.Of(ModInputKind.MarkTrash);
            }

            // Number keys 1-8 assign the focused control to that hotbar slot (the skill sheet uses
            // this; other overlays have no handler, so it falls back to re-reading the label). The
            // slot number rides in Dx. KeyCode.Alpha1..Alpha8 are consecutive.
            for (KeyCode k = KeyCode.Alpha1; k <= KeyCode.Alpha8; k++) {
                if (Input.GetKeyDown(k)) {
                    return new ModInputAction {
                        Kind = ModInputKind.AssignHotbar,
                        Dx = k - KeyCode.Alpha1 + 1,
                    };
                }
            }

            return null;
        }

        // A menu direction: a plain step, or a skip-to-edge when Shift is held.
        private static ModInputAction Nav(bool shift, int dx, int dy) {
            return shift ? ModInputAction.MoveToEdge(dx, dy) : ModInputAction.Move(dx, dy);
        }

        /// <summary>
        /// The cancel/back key (Escape) for menu overlays. Consulted by the menu drainer only while an
        /// auxiliary overlay owns input, so on a normal screen Escape still passes through to the game
        /// to close that screen.
        /// </summary>
        public static ModInputAction? MenuCancel() {
            return Input.GetKeyDown(KeyCode.Escape) ? ModInputAction.Of(ModInputKind.Cancel) : (ModInputAction?)null;
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
                || Input.GetKey(KeyCode.W)
                || Input.GetKey(KeyCode.X)
                || Input.GetKey(KeyCode.A)
                || Input.GetKey(KeyCode.D)
                || Input.GetKey(KeyCode.Return)
                || Input.GetKey(KeyCode.KeypadEnter);
        }


        /// <summary>
        /// Free-play query hotkeys, the gameplay drainer's set: S reads the player's own tile, Y
        /// status, apostrophe repeat. Cursor reads (K) live with the cursor drainer in
        /// <see cref="CursorKeys"/>; the hotbar keys live in <see cref="Hotbar"/>, claimed by a
        /// top-priority drainer so they work inside menus too.
        /// </summary>
        public static ModInputAction? Query() {
            if (Input.GetKeyDown(KeyCode.S)) {
                return ModInputAction.Of(ModInputKind.ReadHere);
            }
            if (Input.GetKeyDown(KeyCode.Y)) {
                return ModInputAction.Of(ModInputKind.ReadStatus);
            }
            if (Input.GetKeyDown(KeyCode.Quote)) {
                return ModInputAction.Of(ModInputKind.RepeatLast);
            }

            // Combat-log history: Ctrl+[ steps to the older message, Ctrl+] to the newer. The game
            // binds bare [ ] to cycle-weapons and never with a modifier, so the Ctrl combos shadow
            // nothing; Ctrl is also the screen reader's stop key, which only silences the previous
            // utterance before ours speaks — exactly what we want when stepping the log.
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.LeftBracket)) {
                return ModInputAction.Of(ModInputKind.LogHistoryPrev);
            }
            if (ctrl && Input.GetKeyDown(KeyCode.RightBracket)) {
                return ModInputAction.Of(ModInputKind.LogHistoryNext);
            }

            return null;
        }

        /// <summary>
        /// The hotbar key: backtick cycles to the next page and reads it. Claimed by the top-priority
        /// <see cref="HotbarInputDrainer"/> so the hotbar is reachable even while a full-screen overlay
        /// owns input (e.g. flip pages before assigning in the skill sheet). Backtick replaces the
        /// game's Ctrl "Cycle Hotbars" — Ctrl is the screen reader's stop key — and the game's Ctrl
        /// binding is stripped on load (see KeymapPatch).
        /// </summary>
        public static ModInputAction? Hotbar() {
            if (Input.GetKeyDown(KeyCode.BackQuote)) {
                return ModInputAction.Of(ModInputKind.CycleHotbar);
            }

            return null;
        }

        /// <summary>
        /// Navigation-aid hotkeys, the gameplay drainer's set. Each aid sits on an F-key slot
        /// (F1 = index 0 … F4 = 3): <b>Shift</b>+Fn toggles it on/off, <b>Ctrl</b>+Fn fires it once
        /// without moving. The game binds only <i>bare</i> F-keys (F1 help, F2 UI page, F5-F8 weapons)
        /// and never an F-key with a modifier, so claiming the modified combos shadows nothing; a bare
        /// F-key returns null and stays the game's.
        /// </summary>
        public static ModInputAction? NavAids() {
            int index;
            if (Input.GetKeyDown(KeyCode.F1)) {
                index = 0;
            } else if (Input.GetKeyDown(KeyCode.F2)) {
                index = 1;
            } else if (Input.GetKeyDown(KeyCode.F3)) {
                index = 2;
            } else if (Input.GetKeyDown(KeyCode.F4)) {
                index = 3;
            } else {
                return null;
            }

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (shift) {
                return new ModInputAction { Kind = ModInputKind.NavAidToggle, Dx = index };
            }
            if (ctrl) {
                return new ModInputAction { Kind = ModInputKind.NavAidTrigger, Dx = index };
            }

            return null; // a bare F-key belongs to the game
        }

        /// <summary>
        /// Scanner navigation, the scanner drainer's set — Factorio Access's Page Up/Down family:
        /// plain Page Up/Down step between entries (Factorio's subcategory axis), Ctrl + Page Up/Down
        /// step between categories. Home points the exploration cursor at the selected feature (its
        /// readout follows); Shift + Home examines the selected feature (its full tooltip); Alt + Home
        /// toggles auto-jump (navigation then points the cursor as you go, cues only). End rescans —
        /// re-snapshots the list. Shift + Page Up/Down is Factorio's instance axis, which we have not
        /// built yet, so we leave it unclaimed (pass through) for now rather than swallow it. The game
        /// binds Page Up/Down only to in-list paging and leaves Home/End unbound, so claiming them
        /// shadows nothing in free play. Modeless: the scanner keeps its selection between presses.
        /// </summary>
        public static ModInputAction? ScannerNav() {
            if (Input.GetKeyDown(KeyCode.Home)) {
                bool altHome = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool shiftHome = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (altHome) {
                    return ModInputAction.Of(ModInputKind.ScanAutoJumpToggle);
                }
                if (shiftHome) {
                    return ModInputAction.Of(ModInputKind.ScanExamine);
                }
                return ModInputAction.Of(ModInputKind.ScanGoto);
            }
            if (Input.GetKeyDown(KeyCode.End)) {
                return ModInputAction.Of(ModInputKind.ScanRescan);
            }

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
        /// Exploration cursor control, consulted every frame (the cursor is always live). The
        /// speculation ring around K steps the cursor 8-way (+x east, +y north):
        /// <c>u i o / j l / m , .</c> map to NW N NE / W E / SW S SE. Holding Shift turns a ring key
        /// into a skip (jump to the next terrain/shape change or occupant). K reads the cursor's tile
        /// (short form); Shift+K examines it in full (the game's tooltip, including hazard effects);
        /// Alt+K toggles follow mode; Ctrl+K returns the cursor to the hero. These keys are all
        /// unbound in the forced Default game layout, so claiming them shadows nothing.
        /// </summary>
        public static ModInputAction? CursorKeys() {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.K)) {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (alt) {
                    return ModInputAction.Of(ModInputKind.CursorFollowToggle);
                }
                if (ctrl) {
                    return ModInputAction.Of(ModInputKind.CursorRecenter);
                }
                if (shift) {
                    return ModInputAction.Of(ModInputKind.CursorExamine);
                }
                return ModInputAction.Of(ModInputKind.CursorRead);
            }

            if (Input.GetKeyDown(KeyCode.U)) {
                return Ring(shift, -1, 1);   // NW
            }
            if (Input.GetKeyDown(KeyCode.I)) {
                return Ring(shift, 0, 1);    // N
            }
            if (Input.GetKeyDown(KeyCode.O)) {
                return Ring(shift, 1, 1);    // NE
            }
            if (Input.GetKeyDown(KeyCode.J)) {
                return Ring(shift, -1, 0);   // W
            }
            if (Input.GetKeyDown(KeyCode.L)) {
                return Ring(shift, 1, 0);    // E
            }
            if (Input.GetKeyDown(KeyCode.M)) {
                return Ring(shift, -1, -1);  // SW
            }
            if (Input.GetKeyDown(KeyCode.Comma)) {
                return Ring(shift, 0, -1);   // S
            }
            if (Input.GetKeyDown(KeyCode.Period)) {
                return Ring(shift, 1, -1);   // SE
            }

            return null;
        }

        // A ring key: a plain step, or a skip when Shift is held.
        private static ModInputAction Ring(bool shift, int dx, int dy) {
            return shift
                ? new ModInputAction { Kind = ModInputKind.CursorSkip, Dx = dx, Dy = dy }
                : ModInputAction.Move(dx, dy);
        }
    }
}
