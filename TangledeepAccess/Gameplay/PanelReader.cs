namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Holds the description of the item/ability currently selected in a full-screen panel
    /// (inventory, equipment, skills, character sheet), captured from the game's column
    /// focus-update hook. Those panels use the newer ImpactUI column model rather than the
    /// legacy uiObjectFocus graph the generic overlay mirrors, so a dedicated channel reads the
    /// rich tooltip the column produces.
    ///
    /// <para>Like the other gameplay channels, the Harmony hook only sets the pending text; the
    /// per-frame pump speaks it (interrupting, since it is menu navigation). Consecutive
    /// identical selections are de-duped so a re-fired update does not repeat.</para>
    /// </summary>
    internal static class PanelReader {
        private static string _pending;
        private static string _last;

        /// <summary>Record the selected item's spoken text. No-op if empty or unchanged.</summary>
        public static void Announce(string text) {
            if (string.IsNullOrEmpty(text) || text == _last) {
                return;
            }

            _last = text;
            _pending = text;
        }

        /// <summary>Take the pending selection text (or null), clearing it.</summary>
        public static string Consume() {
            string p = _pending;
            _pending = null;
            return p;
        }
    }
}
