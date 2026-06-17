using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace TangledeepAccess.Focus {
    /// <summary>
    /// Reads a spoken label off a raw game UI element. This is the lowest-common-denominator
    /// description used by the generic fallback overlay (and previously by FocusAnnouncer):
    /// prefer the element's own TextMeshPro label, then any TMP under its GameObject, then
    /// the button's text, with TMP markup (color/sprite tags) stripped.
    ///
    /// Touches game types (TextMeshProUGUI, CustomAlgorithms), so it lives outside Core/.
    /// Richer overlays bypass this and read structured game data instead.
    /// </summary>
    internal static class GameLabelReader {
        public static string ReadLabel(UIManagerScript.UIObject obj) {
            if (obj == null) {
                return null;
            }

            string raw = null;
            if (obj.subObjectTMPro != null) {
                raw = obj.subObjectTMPro.text;
            }

            if (string.IsNullOrEmpty(raw) && obj.gameObj != null) {
                TextMeshProUGUI tmp = obj.gameObj.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) {
                    raw = tmp.text;
                }
            }
            if (string.IsNullOrEmpty(raw) && obj.button != null) {
                raw = obj.button.buttonText;
            }

            if (string.IsNullOrEmpty(raw)) {
                return null;
            }

            return Clean(raw);
        }

        // Any remaining TMP rich-text tag after color stripping (<size>, <sprite>, <b>, ...).
        // The game's StripColors removes only <color>/<#...>, so multi-section readouts keep
        // their size/sprite tags; strip them all for clean speech.
        private static readonly Regex AnyTag = new Regex("<[^>]+>", RegexOptions.Compiled);

        // Runs of whitespace, including the "\n\n" section breaks in tooltip readouts.
        private static readonly Regex WhitespaceRun = new Regex("\\s+", RegexOptions.Compiled);

        /// <summary>
        /// Normalize raw game text to a spoken string: strip color tags (game stripper), then
        /// any other TMP markup, then collapse all whitespace (incl. newlines) to single
        /// spaces. Null/empty in (or all-markup) returns null.
        /// </summary>
        public static string Clean(string raw) {
            if (string.IsNullOrEmpty(raw)) {
                return null;
            }

            string cleaned = AnyTag.Replace(CustomAlgorithms.StripColors(raw), "");
            cleaned = WhitespaceRun.Replace(cleaned, " ").Trim();
            return string.IsNullOrEmpty(cleaned) ? null : cleaned;
        }
    }
}
