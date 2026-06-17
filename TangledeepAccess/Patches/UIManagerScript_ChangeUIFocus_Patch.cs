using HarmonyLib;

namespace TangledeepAccess.Patches
{
    /// <summary>
    /// The universal focus chokepoint: every menu (title, dialogs, shop, hotbar, options,
    /// character creation, and the inventory/equipment/skills columns) routes focus changes
    /// through UIManagerScript.ChangeUIFocus. The postfix only records the newly focused
    /// element into the dispatcher; the per-frame pump (Plugin.Update) reconciles and speaks.
    /// It runs regardless of the method's processEvent flag, so the column path (which calls
    /// with processEvent: false) is covered too.
    /// </summary>
    [HarmonyPatch(typeof(UIManagerScript), "ChangeUIFocus")]
    internal static class UIManagerScript_ChangeUIFocus_Patch
    {
        private static void Postfix(UIManagerScript.UIObject obj)
        {
            UiRuntime.Dispatcher?.RecordGameFocus(obj);
        }
    }
}
