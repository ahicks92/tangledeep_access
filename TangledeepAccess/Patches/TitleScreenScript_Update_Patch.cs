using HarmonyLib;
using TangledeepAccess.Controls;

namespace TangledeepAccess.Patches {
    /// <summary>
    /// Title-screen input chokepoint. The new-game flow (title menu, slot screen, story dialogs) is
    /// pumped by <c>TitleScreenScript.Update</c>, where our in-game hook never runs. That method also
    /// runs the background-scroll animation, so we must not blunt-suppress it; <see cref="InputRouter"/>
    /// claims only our own nav keys and passes the rest (including the animation's own work) through.
    /// </summary>
    [HarmonyPatch(typeof(TitleScreenScript), "Update")]
    internal static class TitleScreenScript_Update_Patch {
        private static bool Prefix() {
            return InputRouter.RouteTitle();
        }
    }
}
