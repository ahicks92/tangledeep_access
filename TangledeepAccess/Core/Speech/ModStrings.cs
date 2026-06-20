namespace TangledeepAccess.Speech {
    /// <summary>
    /// The single home for <b>mod-authored</b> spoken strings — the words we put in a player's ear
    /// that the game does not provide (status adjectives, our own labels, composed phrases). Game
    /// text is still spoken verbatim through <c>StringManager</c> / the game's description builders;
    /// this is only for the connective and descriptive language we add around it.
    ///
    /// <para>Centralizing them here (instead of inline literals scattered through overlays) gives one
    /// file to audit wording in, and one place a future translation pass swaps to a per-language
    /// lookup. Composed phrases are methods so the number/name interpolation lives beside the
    /// template; fixed words are consts. BCL-only, so it lives under <c>Core/</c> and is unit-tested.</para>
    ///
    /// <para>Spoken units that recur across screens (e.g. <see cref="Jp"/>) live at the top; per-screen
    /// blocks follow. Keep the "JP" abbreviation, not "job points" — it matches the game's own UI and
    /// reads correctly.</para>
    /// </summary>
    internal static class ModStrings {
        // --- Shared units ----------------------------------------------------------------------

        /// <summary>A job-points amount, e.g. "250 JP". The one home for how JP reads.</summary>
        public static string Jp(int amount) {
            return amount + " JP";
        }

        // --- Hotbar ----------------------------------------------------------------------------

        public const string Hotbar = "hotbar";
        public const string HotbarEmpty = "empty";
        public const string HotbarUnavailable = "unavailable";

        /// <summary>Where an ability is bound, e.g. "on hotbar 2 slot 3".</summary>
        public static string OnHotbar(int page, int slot) {
            return "on hotbar " + page + " slot " + slot;
        }

        /// <summary>Confirmation verb when an ability is bound, e.g. "Fireball assigned on hotbar 1 slot 3".</summary>
        public const string Assigned = "assigned";

        // --- Skill sheet: header / modes -------------------------------------------------------

        public const string SkillsHeader = "Skills";
        public const string LearnMode = "learn abilities";
        public const string SlotMode = "slot abilities";
        public const string ModeLearning = "learning abilities";
        public const string ModeSlotting = "slotting abilities";
        public const string Selected = "selected";

        // --- Skill sheet: ability status -------------------------------------------------------

        public const string Learned = "learned";
        public const string Mastered = "mastered";
        public const string NotEnough = "not enough";
        public const string Repeatable = "repeatable";
        public const string ToggledOn = "toggled on";

        /// <summary>Cost to learn an ability, e.g. "costs 75 JP".</summary>
        public static string Costs(int jp) {
            return "costs " + Jp(jp);
        }

        /// <summary>Cost to master an owned ability, e.g. "can master for 150 JP".</summary>
        public static string CanMasterFor(int jp) {
            return "can master for " + Jp(jp);
        }

        // --- Skill sheet: row labels -----------------------------------------------------------

        /// <summary>The first cell of the abilities row, naming what the rest of the row holds.</summary>
        public const string LearnAbilityRow = "learn ability";

        // --- Skill sheet: innate bonuses -------------------------------------------------------

        public const string InnateBonuses = "innate job bonuses";

        /// <summary>Prefix for a passive tier whose JP / mastery requirement is not yet met.</summary>
        public const string Locked = "locked";

        // --- Skill sheet: learn results --------------------------------------------------------

        public const string AlreadyLearned = "already learned";
        public const string NotEnoughJp = "not enough JP";
        public const string CantLearn = "can't learn that";

        /// <summary>JP left after a purchase, e.g. "175 JP remaining".</summary>
        public static string JpRemaining(int jp) {
            return Jp(jp) + " remaining";
        }

        // --- Skill sheet: slot mode ------------------------------------------------------------

        public const string EquippedRow = "equipped abilities";
        public const string EquippablePassivesRow = "equippable passives";
        public const string AlwaysOnPassivesRow = "always-on passives";
        public const string None = "none";
        public const string Equipped = "equipped";
        public const string AlwaysOn = "always on";
        public const string Unlearned = "unlearned";
        public const string Unequipped = "unequipped";
        public const string NoFreePassiveSlots = "no free passive slots";
        public const string CantChangeHere = "can't change skills here";
        public const string CantUseFromHere = "can't use abilities from here";
        public const string LearnInLearnTab = "learn it in the learn tab";

        /// <summary>The passive-slot budget, e.g. "2 of 4 used".</summary>
        public static string PassiveSlotsUsed(int used) {
            return used + " of 4 used";
        }

        /// <summary>An active-ability row, named for the job it came from, e.g. "Brigand abilities".</summary>
        public static string JobAbilitiesRow(string jobName) {
            return jobName + " abilities";
        }

        /// <summary>The show-unlearned toggle's label, reflecting its state.</summary>
        public static string ShowUnlearned(bool on) {
            return "show unlearned abilities, " + (on ? "on" : "off");
        }

        // --- Equipment sheet -------------------------------------------------------------------

        // Row anchors (the first cell of each row, naming what the row holds).
        public const string EquippedGearRow = "equipped gear";
        public const string GearBonusesRow = "gear bonuses";
        public const string CategoryRow = "category";
        public const string FilterRow = "filter";
        public const string SortRow = "sort";
        public const string ItemsRow = "items";

        // Equipped-slot state words.
        public const string EmptySlot = "empty";
        public const string ActiveWeapon = "active";
        public const string On = "on";
        public const string Off = "off";

        // Equipped-slot names. The four weapon-hotbar slots and the two accessory slots are numbered.
        public static string WeaponSlot(int n) {
            return "weapon " + n;
        }

        public const string OffhandSlot = "offhand";
        public const string ArmorSlot = "armor";

        public static string AccessorySlot(int n) {
            return "accessory " + n;
        }

        public const string EmblemSlot = "emblem";

        // Item-row action verbs.
        public const string EquipAction = "equip";
        public const string EquipOffhandAction = "equip offhand";

        public static string EquipWeaponSlotAction(int n) {
            return "equip to weapon " + n;
        }

        public static string EquipAccessoryAction(int n) {
            return "equip accessory " + n;
        }

        public const string PairAction = "pair with main hand";
        public const string UnpairAction = "unpair from main hand";
        public const string DropAction = "drop";

        // Item-row action results.
        public const string Paired = "paired";
        public const string Unpaired = "unpaired";
        public const string CantEquip = "can't equip that";
        public const string CantDrop = "can't drop that";
        public const string Dropped = "dropped";
        public const string Favorited = "favorited";
        public const string NoLongerFavorite = "no longer favorite";
        public const string MarkedTrash = "marked as trash";
        public const string NoLongerTrash = "no longer trash";

        // Comparison (Ctrl+K) on an item, against the gear it would replace.
        public const string NothingToCompare = "nothing equipped to compare";
        public const string NoDifference = "no change";

        /// <summary>Prefix for an item comparison, e.g. "compared to Iron Sword".</summary>
        public static string ComparedTo(string equippedName) {
            return "compared to " + equippedName;
        }

        // --- Dialogs ---------------------------------------------------------------------------

        /// <summary>The slider value control's label, e.g. "amount 50". Left/right adjust it.</summary>
        public static string SliderAmount(int value) {
            return "amount " + value;
        }

        /// <summary>The (deferred) free-text entry control's label.</summary>
        public const string TextBox = "text box";

        /// <summary>Spoken when the player activates a text-entry control we cannot yet drive.</summary>
        public const string TextBoxUnsupported = "text boxes not yet supported";

        /// <summary>The dialog portrait/illustration control's label (no description yet).</summary>
        public const string Image = "image";

        // --- Shop ------------------------------------------------------------------------------

        // Sort buttons (the shop offers only these two).
        public const string SortByType = "sort by type";
        public const string SortByValue = "sort by value";

        /// <summary>The buy-screen header anchor, e.g. "buying from Katie Twinkles".</summary>
        public static string ShopBuying(string merchant) {
            return "buying from " + merchant;
        }

        /// <summary>The sell-screen header anchor, e.g. "selling to Katie Twinkles".</summary>
        public static string ShopSelling(string merchant) {
            return "selling to " + merchant;
        }

        /// <summary>A gold amount for prices and money, e.g. "50 gold".</summary>
        public static string Gold(int amount) {
            return amount + " gold";
        }

        /// <summary>How many of an item the hero already owns, e.g. "you own 2".</summary>
        public static string Owned(int count) {
            return "you own " + count;
        }

        /// <summary>The item count in the header, e.g. "13 items".</summary>
        public static string ItemCount(int count) {
            return count + (count == 1 ? " item" : " items");
        }

        /// <summary>Spoken on a buy the hero cannot afford.</summary>
        public const string TooExpensive = "too expensive";

        /// <summary>Spoken when there is no equipment to compare a shop item against.</summary>
        public const string NothingToCompareShop = "nothing to compare";

        /// <summary>The favorited-item sell guard: a plain Enter warns, Ctrl+Enter proceeds.</summary>
        public const string FavoriteSellConfirm = "favorited item, press control enter to confirm";

        // --- Quantity prompt (auxiliary overlay) -----------------------------------------------

        /// <summary>The quantity slider's label, e.g. "how many? 5".</summary>
        public static string HowMany(int count) {
            return "how many? " + count;
        }

        // --- Character sheet -------------------------------------------------------------------

        // Section anchors for the flattened character sheet.
        public const string CharSheetCore = "core stats";
        public const string CharSheetElements = "elements and defense";
        public const string CharSheetStatusEffects = "status effects";
        public const string CharSheetAdventure = "adventure";
        public const string CharSheetFeats = "feats";

        /// <summary>Job and level for the header, e.g. "Brigand level 2".</summary>
        public static string JobLevel(string job, int level) {
            return job + " level " + level;
        }

        /// <summary>XP progress for the header, e.g. "XP 72 of 160".</summary>
        public static string Xp(int cur, int next) {
            return "XP " + cur + " of " + next;
        }

        /// <summary>An element's resistance value, e.g. "defense 8%".</summary>
        public static string ElementDefense(string value) {
            return "defense " + value;
        }

        /// <summary>An element's bonus-damage value, e.g. "damage 0%".</summary>
        public static string ElementDamage(string value) {
            return "damage " + value;
        }

    }
}
