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
        // ui → look → scanner → gameplay. The menu wins over the look cursor (a dialog opened
        // mid-look still takes keys); the look cursor wins over free play (it owns its movement keys
        // while active); the scanner is modeless and claims only its own dedicated nav keys, so its
        // position relative to look/gameplay does not matter; free play is the floor (it claims only
        // our query hotkeys, passing movement to the game). The hotbar drainer sits ABOVE this whole
        // list (handled in RouteInGame before the capture check), since its keys must survive even
        // an overlay that owns input.
        private static readonly InputDrainer[] InGame = {
            MenuInputDrainer.Instance,
            LookInputDrainer.Instance,
            ScannerInputDrainer.Instance,
            GameplayInputDrainer.Instance,
        };

        /// <summary>In-game pump: offer ui, then the look cursor, then free play.</summary>
        public static bool RouteInGame() {
            // Top priority, always: the hotbar keys (cycle/read) work even while a full-screen overlay
            // owns input — so the player can flip to the page they want before assigning a skill in
            // the slot sheet. Offered before the capture check; claims only its own two keys.
            if (HotbarInputDrainer.Instance.Claim(suppressWhileHeld: false)) {
                return false;
            }

            // A full-screen overlay that declared it owns input takes the WHOLE frame: only the menu
            // drainer runs, and it keeps claiming a HELD nav key (suppressWhileHeld) — like the title
            // pump — so the key's auto-repeat can't leak to the game on the frames between our
            // key-down edges. Without this, the game's own TDInputHandler.UpdateInput runs on those
            // repeat frames and walks its parallel full-screen-UI cursor (playing its cursor sounds),
            // and non-menu keys fall through to the other mod drainers (e.g. semicolon would toggle
            // the look cursor from inside a menu). Keys the menu drainer does not claim still reach
            // the GAME (so its own menu hotkeys — close, tab-switch, hotbar slotting — keep working),
            // but the mod's free-play drainers (look cursor, scanner, gameplay queries) stay dormant.
            if (UiRuntime.Dispatcher != null && UiRuntime.Dispatcher.CapturesInput) {
                return !MenuInputDrainer.Instance.Claim(suppressWhileHeld: true);
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
