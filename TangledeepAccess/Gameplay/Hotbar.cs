using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Game-touching hotbar operations, shared by the <see cref="Controls.HotbarInputDrainer"/>
    /// (cycle/read in any context) and the skill sheet (assign a learned ability to a slot, report
    /// where one is bound). The hotbar is the game's own state — a flat <c>hotbarAbilities</c> array
    /// of two 8-slot pages, with <c>indexOfActiveHotbar</c> (a private static) selecting the page
    /// that number keys 1-8 fire and that we assign into. We read and flip that state on demand;
    /// never cache it.
    /// </summary>
    internal static class Hotbar {
        public const int PageSize = 8;

        // The active hotbar page index is a private static on UIManagerScript.
        private static readonly AccessTools.FieldRef<int> ActivePageField =
            AccessTools.StaticFieldRefAccess<int>(
                AccessTools.Field(typeof(UIManagerScript), "indexOfActiveHotbar")
            );

        public static int ActivePage => ActivePageField();

        /// <summary>Flip to the next page via the game's own mutator, which keeps ability-firing
        /// (number keys 1-8) pointed at the right page.</summary>
        public static void Cycle() {
            UIManagerScript.ToggleSecondaryHotbar();
        }

        /// <summary>
        /// Bind a learned ability to slot 1-8 (1-based) on the active page, deduping so the same
        /// ability is not left on two slots. Returns false on a bad slot.
        /// </summary>
        public static bool Assign(AbilityScript ability, int slotOneBased) {
            if (ability == null || slotOneBased < 1 || slotOneBased > PageSize) {
                return false;
            }

            int flat = ActivePage * PageSize + (slotOneBased - 1);
            UIManagerScript.AddAbilityToSlot(ability, flat, removeDupes: true);
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

        /// <summary>Read the active page: "hotbar &lt;page&gt;, 1 &lt;name&gt;, 2 &lt;name&gt;, …".</summary>
        public static void Read(MessageBuilder message) {
            HotbarBindable[] hb = UIManagerScript.hotbarAbilities;
            message.Fragment(ModStrings.Hotbar);
            if (hb == null) {
                message.Fragment(ModStrings.HotbarUnavailable);
                return;
            }

            int page = ActivePage;
            message.Fragment((page + 1).ToString());

            bool any = false;
            for (int i = 0; i < PageSize; i++) {
                int idx = page * PageSize + i;
                if (idx >= hb.Length) {
                    break;
                }

                string name = SlotName(hb[idx]);
                if (name != null) {
                    message.ListItem((i + 1) + ", " + name);
                    any = true;
                }
            }

            if (!any) {
                message.Fragment(ModStrings.HotbarEmpty);
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
