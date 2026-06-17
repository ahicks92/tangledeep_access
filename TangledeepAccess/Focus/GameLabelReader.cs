using TMPro;
using UnityEngine;

namespace TangledeepAccess.Focus
{
    /// <summary>
    /// Reads a spoken label off a raw game UI element. This is the lowest-common-denominator
    /// description used by the generic fallback overlay (and previously by FocusAnnouncer):
    /// prefer the element's own TextMeshPro label, then any TMP under its GameObject, then
    /// the button's text, with TMP markup (color/sprite tags) stripped.
    ///
    /// Touches game types (TextMeshProUGUI, CustomAlgorithms), so it lives outside Core/.
    /// Richer overlays bypass this and read structured game data instead.
    /// </summary>
    internal static class GameLabelReader
    {
        public static string ReadLabel(UIManagerScript.UIObject obj)
        {
            if (obj == null)
                return null;

            string raw = null;
            if (obj.subObjectTMPro != null)
                raw = obj.subObjectTMPro.text;
            if (string.IsNullOrEmpty(raw) && obj.gameObj != null)
            {
                TextMeshProUGUI tmp = obj.gameObj.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                    raw = tmp.text;
            }
            if (string.IsNullOrEmpty(raw) && obj.button != null)
                raw = obj.button.buttonText;
            if (string.IsNullOrEmpty(raw))
                return null;

            return CustomAlgorithms.StripColors(raw).Trim();
        }
    }
}
