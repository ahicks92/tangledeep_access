using HarmonyLib;
using TangledeepAccess.Controls;

namespace TangledeepAccess.Patches {
    /// <summary>
    /// Our in-game input chokepoint. <c>TDInputHandler.UpdateInput</c> is the single point the game
    /// reads in-game input (it is NOT called on the title screen, which is why this naturally scopes
    /// us to gameplay). The own-or-passthrough decision is delegated to <see cref="InputRouter"/>,
    /// shared with the title hook so the policy lives in exactly one place.
    /// </summary>
    [HarmonyPatch(typeof(TDInputHandler), "UpdateInput")]
    internal static class TDInputHandler_UpdateInput_Patch {
        private static bool Prefix() {
            return InputRouter.RouteInGame();
        }
    }
}
