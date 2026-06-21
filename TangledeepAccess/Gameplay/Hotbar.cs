using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Game-touching hotbar operations, shared by the <see cref="Controls.HotbarInputDrainer"/>
    /// (read a bank in any context) and the assign UIs (skill sheet abilities, inventory
    /// consumables). The hotbar is the game's own state — a flat <c>hotbarAbilities</c> array of two
    /// 8-slot banks, with <c>indexOfActiveHotbar</c> (a private static) selecting which bank the
    /// game's own number-key firing hits. The mod removes the swap concept: bar 1 (slots 0-7) is
    /// fired by bare 1-8, bar 2 (slots 8-15) by Ctrl+1-8 — the latter by momentarily forcing the
    /// active index to 1 around the game's own firing (see the input patch). We address banks by
    /// explicit index here; never cache state, re-query on demand.
    /// </summary>
    internal static class Hotbar {
        public const int PageSize = 8;

        // The active hotbar page index is a private static on UIManagerScript.
        private static readonly AccessTools.FieldRef<int> ActivePageField =
            AccessTools.StaticFieldRefAccess<int>(
                AccessTools.Field(typeof(UIManagerScript), "indexOfActiveHotbar")
            );

        /// <summary>Set the game's active-bank index directly (no HUD swap animation). The input
        /// patch forces this to 1 for the frame the game fires a Ctrl+digit press, then back to 0 —
        /// the resting value — so the game's own firing path hits bar 2. A raw field write, not
        /// <c>ToggleSecondaryHotbar</c>, which would also drive the on-screen swap animation.</summary>
        public static void SetActivePage(int page) {
            ActivePageField() = page;
        }

        /// <summary>
        /// Bind a learned ability to slot 1-8 (1-based) on <paramref name="bank"/> (0 or 1), deduping
        /// so the same ability is not left on two slots. Returns false on a bad slot.
        /// </summary>
        public static bool Assign(AbilityScript ability, int slotOneBased, int bank) {
            if (ability == null || slotOneBased < 1 || slotOneBased > PageSize) {
                return false;
            }

            int flat = bank * PageSize + (slotOneBased - 1);
            UIManagerScript.AddAbilityToSlot(ability, flat, removeDupes: true);
            return true;
        }

        /// <summary>
        /// Bind a consumable to slot 1-8 (1-based) on <paramref name="bank"/> (0 or 1). Returns false
        /// on a bad slot. Unlike abilities we do not dedupe — a stack can legitimately sit on more
        /// than one slot, matching the game's own drag-to-hotbar.
        /// </summary>
        public static bool Assign(Consumable consumable, int slotOneBased, int bank) {
            if (consumable == null || slotOneBased < 1 || slotOneBased > PageSize) {
                return false;
            }

            int flat = bank * PageSize + (slotOneBased - 1);
            UIManagerScript.AddItemToSlot(consumable, flat, removeDupes: false);
            return true;
        }

        /// <summary>Where <paramref name="ability"/> is currently bound ("on hotbar 2 slot 3"), or
        /// null if it is on no slot. Compared by refName, since the bound and learned instances may
        /// differ.</summary>
        public static string FindBinding(AbilityScript ability) {
            HotbarBindable[] hb = UIManagerScript.hotbarAbilities;
            if (hb == null || ability == null) {
                return null;
            }

            for (int i = 0; i < hb.Length; i++) {
                HotbarBindable slot = hb[i];
                if (
                    slot != null
                    && slot.actionType == HotbarBindableActions.ABILITY
                    && slot.ability != null
                    && slot.ability.refName == ability.refName
                ) {
                    return ModStrings.OnHotbar(i / PageSize + 1, i % PageSize + 1);
                }
            }

            return null;
        }

        /// <summary>
        /// Read one bank (<paramref name="page"/> 0 or 1) positionally: just the slot contents in
        /// order — "&lt;name&gt;, &lt;name&gt;, …, empty, empty" — with empty slots read as "empty"
        /// and no slot numbers or bank prefix. The slot is implied by position (players fill left to
        /// right), so dropping the numbers makes the common case far faster to hear; the caller
        /// already knows which bar they asked for (backtick vs Ctrl+backtick). An entirely empty bar
        /// collapses to a single "empty" rather than eight.
        /// </summary>
        public static void Read(MessageBuilder message, int page) {
            HotbarBindable[] hb = UIManagerScript.hotbarAbilities;
            if (hb == null) {
                message.Fragment(ModStrings.HotbarUnavailable);
                return;
            }

            bool any = false;
            for (int i = 0; i < PageSize; i++) {
                int idx = page * PageSize + i;
                if (idx < hb.Length && SlotName(hb[idx]) != null) {
                    any = true;
                    break;
                }
            }

            if (!any) {
                message.Fragment(ModStrings.HotbarEmpty);
                return;
            }

            for (int i = 0; i < PageSize; i++) {
                int idx = page * PageSize + i;
                string name = (idx < hb.Length ? SlotName(hb[idx]) : null) ?? ModStrings.HotbarEmpty;
                message.ListItem(name);
            }
        }

        public static string SlotName(HotbarBindable slot) {
            if (slot == null) {
                return null;
            }

            if (slot.actionType == HotbarBindableActions.ABILITY && slot.ability != null) {
                return GameLabelReader.Clean(slot.ability.GetNameForUI());
            }

            if (slot.actionType == HotbarBindableActions.CONSUMABLE && slot.consume != null) {
                return GameLabelReader.Clean(slot.consume.GetNameForUI());
            }

            return null;
        }
    }
}
