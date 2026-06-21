using TangledeepAccess.Gameplay;
using TangledeepAccess.Ui;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// The priority chain and single entry point for both input chokepoints. Each frame it offers
    /// the active drainers the frame in priority order; the first to <see cref="InputDrainer.Claim"/>
    /// it owns the frame and the game's own input is suppressed. A drainer that does not apply
    /// (no menu open, look cursor off) or does not recognize the key returns false and the next is
    /// tried; if none claims, the game runs. This replaces a state machine that picked one handler:
    /// the drainers decide for themselves whether they apply, so the priority order is the only
    /// policy that lives here.
    /// </summary>
    internal static class InputChain {
        // ui → cursor → scanner → gameplay. The menu wins over the exploration cursor (a dialog
        // opened mid-look still takes keys); the cursor claims only its own dedicated keys (the
        // speculation ring + read/follow/recenter), all game-unbound; the scanner is modeless and
        // claims only its own dedicated nav keys, so its position relative to cursor/gameplay does
        // not matter; free play is the floor (it claims only our query hotkeys, passing movement to
        // the game). The hotbar drainer sits ABOVE this whole list (handled in RouteInGame before
        // the capture check), since its keys must survive even an overlay that owns input.
        private static readonly InputDrainer[] InGame = {
            MenuInputDrainer.Instance,
            ExplorationCursorInputDrainer.Instance,
            ScannerInputDrainer.Instance,
            GameplayInputDrainer.Instance,
        };

        /// <summary>
        /// In-game pump: offer the hotbar reads, then ui, then the exploration cursor, then free play.
        /// <paramref name="flipBankTwo"/> is set true when the player pressed Ctrl+digit in free play:
        /// the caller forces the active bank to 1 and lets the game run (this returns true) so the
        /// game's own firing path hits bar 2, then resets the bank in its postfix.
        /// </summary>
        public static bool RouteInGame(out bool flipBankTwo) {
            flipBankTwo = false;

            // Top priority, always: the hotbar READ keys (backtick / Ctrl+backtick) work even while a
            // full-screen overlay owns input — so the player can check a bar before assigning a skill
            // in the slot sheet. Offered before the capture check; claims only its read keys.
            if (HotbarInputDrainer.Instance.Claim(suppressWhileHeld: false)) {
                return false;
            }

            // A full-screen overlay that declared it owns input takes the WHOLE frame: only the menu
            // drainer runs, and it keeps claiming a HELD nav key (suppressWhileHeld) — like the title
            // pump — so the key's auto-repeat can't leak to the game on the frames between our
            // key-down edges. Without this, the game's own TDInputHandler.UpdateInput runs on those
            // repeat frames and walks its parallel full-screen-UI cursor (playing its cursor sounds),
            // and non-menu keys fall through to the other mod drainers (e.g. a speculation key would
            // step the exploration cursor from inside a menu). Keys the menu drainer does not claim
            // still reach the GAME (so its own menu hotkeys — close, tab-switch, hotbar slotting —
            // keep working), but the mod's free-play drainers (cursor, scanner, gameplay queries)
            // stay dormant. Crucially this sits ABOVE the Ctrl+digit fire check below: inside a menu,
            // Ctrl+digit means ASSIGN to bar 2 (the menu drainer carries it), never fire.
            if (UiRuntime.Dispatcher != null && UiRuntime.Dispatcher.CapturesInput) {
                return !MenuInputDrainer.Instance.Claim(suppressWhileHeld: true);
            }

            // Free-play Ctrl+digit = "fire bar 2". We don't claim it: instead we signal the caller to
            // flip the active bank to 1 and run the game, so the game's own UpdateInput fires the slot
            // with all its guards + log→speech. Bare digits need nothing here — the bank rests at 0
            // and the game fires bar 1 as usual.
            if (InputKeys.HotbarBankTwoFireRequested()) {
                flipBankTwo = true;
                return true; // run the game (it fires bar 2 because we will have flipped the bank)
            }

            foreach (InputDrainer drainer in InGame) {
                if (drainer.Claim(suppressWhileHeld: false)) {
                    return false; // claimed — suppress the game this frame
                }
            }

            return true; // nobody claimed — the game runs
        }

        /// <summary>
        /// Title pump: only menu overlays exist here (no gameplay, no look cursor), and the title
        /// auto-repeats, so we keep suppressing while a nav key is held.
        /// </summary>
        public static bool RouteTitle() {
            return !MenuInputDrainer.Instance.Claim(suppressWhileHeld: true);
        }
    }
}
