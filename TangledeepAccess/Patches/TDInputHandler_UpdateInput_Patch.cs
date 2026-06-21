using HarmonyLib;
using TangledeepAccess.Controls;
using TangledeepAccess.Gameplay;

namespace TangledeepAccess.Patches {
    /// <summary>
    /// Our in-game input chokepoint. <c>TDInputHandler.UpdateInput</c> is the single point the game
    /// reads in-game input (it is NOT called on the title screen, which is why this naturally scopes
    /// us to gameplay). The own-or-passthrough decision is delegated to <see cref="InputChain"/>,
    /// shared with the title hook so the policy lives in exactly one place.
    ///
    /// <para><b>Bar-2 firing.</b> When <see cref="InputChain.RouteInGame"/> reports a Ctrl+digit
    /// press in free play, the prefix forces the game's active bank to 1 and lets the original method
    /// run, so the game's own firing path (with all its guards and its log→speech) fires bar 2. The
    /// postfix resets the bank to 0 — the resting value — so the flip lasts exactly the one frame.
    /// A guarded flag, not an unconditional reset, so we only ever touch the field on frames we
    /// flipped it (a gamepad bottom-bar swap, were one ever live, is left alone). The postfix runs
    /// even when the prefix suppressed the body, so the reset can never be skipped.</para>
    /// </summary>
    [HarmonyPatch(typeof(TDInputHandler), "UpdateInput")]
    internal static class TDInputHandler_UpdateInput_Patch {
        private static bool _flippedToBankTwo;

        private static bool Prefix() {
            bool runGame = InputChain.RouteInGame(out bool flipBankTwo);
            if (flipBankTwo) {
                Hotbar.SetActivePage(1);
                _flippedToBankTwo = true;
            }

            return runGame;
        }

        private static void Postfix() {
            if (_flippedToBankTwo) {
                Hotbar.SetActivePage(0);
                _flippedToBankTwo = false;
            }
        }
    }
}
