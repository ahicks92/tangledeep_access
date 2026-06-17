using System;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Gameplay;
using TangledeepAccess.Util;

namespace TangledeepAccess.Patches {
    /// <summary>
    /// Speaks the item/ability selected in a full-screen panel. The inventory, equipment,
    /// skills, and character-sheet screens are ImpactUI panels whose scrolling button columns
    /// do NOT drive the legacy uiObjectFocus graph the generic overlay mirrors — they raise
    /// selection changes through <c>ImpactUI_Base.OnColumnUpdateFocus</c>. One postfix on that
    /// base method therefore covers every such panel: read the selected button's contained data
    /// (an <c>ISelectableUIObject</c>: Item, Equipment, AbilityScript, JobAbility) and speak the
    /// uniform name + tooltip.
    ///
    /// <para>The hook only records the text; the per-frame pump speaks it. The column handles
    /// its own arrow navigation, so the mod does not capture input here — this is a pure
    /// announce-on-change, like the game log.</para>
    /// </summary>
    [HarmonyPatch(typeof(ImpactUI_Base), "OnColumnUpdateFocus")]
    internal static class ImpactUI_OnColumnUpdateFocus_Patch {
        private static void Postfix(Switch_UIButtonColumn column) {
            try {
                Switch_InvItemButton button = column?.GetSelectedButtonInList();
                ISelectableUIObject data = button?.GetContainedData();
                if (data == null) {
                    return;
                }

                string name = GameLabelReader.Clean(data.GetNameForUI());
                string tip = GameLabelReader.Clean(data.GetInformationForTooltip());

                // The generic focus overlay already speaks the selected button's name as focus
                // lands, so here we speak the tooltip detail only. Ability tooltips lead with the
                // name (strip it to avoid saying it twice); item tooltips are already name-less.
                string text = tip;
                if (tip != null && name != null && tip.StartsWith(name, StringComparison.Ordinal)) {
                    text = tip.Substring(name.Length).TrimStart(' ', ',', '.', ':', '\n');
                }

                PanelReader.Announce(string.IsNullOrEmpty(text) ? name : text);
            } catch (Exception e) {
                Log.Warn("panel selection capture failed: " + e.Message);
            }
        }
    }
}
