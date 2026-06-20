using TangledeepAccess.Focus;
using TangledeepAccess.Speech;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Announces a change of the active weapon-hotbar slot, e.g. "sword slot 2". Switching the
    /// active weapon only ever happens from a player input, so the change is self-explanatory and
    /// needs no "switched to" preamble. Polled once per frame from the pump; it edge-detects the
    /// game's <see cref="UIManagerScript.GetActiveWeaponSlot"/> and speaks only when the slot index
    /// changes. Re-queries the live weapon at speak time — no caching beyond the last slot index.
    ///
    /// <para>An empty slot (the default fists) reads as "fists slot N". The first poll after
    /// entering play arms silently so loading a save does not announce the starting slot.</para>
    /// </summary>
    internal static class WeaponWatcher {
        private static bool _have;
        private static int _slot;

        /// <summary>The active-weapon-slot announcement for this frame, or null. Call every frame.</summary>
        public static MessageBuilder Poll() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted) {
                _have = false; // dropped out of play; re-arm so re-entry doesn't fire on spawn
                return null;
            }

            int slot = UIManagerScript.GetActiveWeaponSlot();
            if (_have && slot == _slot) {
                return null;
            }

            // Keep state armed (so we don't fire spuriously later) but say nothing when the
            // verbose combat log is on — the game already logs the switch and that line is spoken.
            if (PlayerOptions.verboseCombatLog) {
                _have = true;
                _slot = slot;
                return null;
            }

            bool first = !_have;
            _have = true;
            _slot = slot;
            if (first) {
                return null; // don't announce the slot the game loaded into
            }

            Weapon[] weapons = UIManagerScript.hotbarWeapons;
            Weapon w = (weapons != null && slot >= 0 && slot < weapons.Length) ? weapons[slot] : null;
            bool empty = w == null || hero.myEquipment.IsDefaultWeapon(w, onlyActualFists: true);
            string name = empty ? ModStrings.UnarmedWeapon : GameLabelReader.Clean(w.displayName);

            return new MessageBuilder()
                .Fragment(name)
                .Fragment(ModStrings.SwitchedWeaponSlot(slot + 1));
        }
    }
}
