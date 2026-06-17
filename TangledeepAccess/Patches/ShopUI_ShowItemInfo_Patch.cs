using System;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Gameplay;
using TangledeepAccess.Util;

namespace TangledeepAccess.Patches {
    /// <summary>
    /// Speaks a shop item's details and price as it is focused. The shop is a legacy
    /// uiObjectFocus screen, so the generic overlay already voices each item button's name; what
    /// it misses is the rank/rarity/description and the gold cost, which the game renders into
    /// <c>ShopUIScript.shopItemInfoText</c> as a side effect of <c>ShowItemInfo(index)</c> (the
    /// focused button's select action). A postfix reads that freshly-rendered text — which ends
    /// with "COST: Ng" — into <see cref="PanelReader"/> for the pump to speak. The name is left
    /// to the generic overlay (the info text leads with the rank, not the name, so no double).
    /// </summary>
    [HarmonyPatch(typeof(ShopUIScript), "ShowItemInfo")]
    internal static class ShopUI_ShowItemInfo_Patch {
        private static void Postfix() {
            try {
                if (ShopUIScript.shopItemInfoText == null) {
                    return;
                }

                PanelReader.Announce(GameLabelReader.Clean(ShopUIScript.shopItemInfoText.text));
            } catch (Exception e) {
                Log.Warn("shop item info capture failed: " + e.Message);
            }
        }
    }
}
