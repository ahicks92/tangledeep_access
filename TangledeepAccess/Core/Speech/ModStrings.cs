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

        // --- Skill sheet: slot mode (not yet supported) ----------------------------------------

        public const string SlotUnsupportedHead =
            "Slot abilities are not yet supported by the screen reader.";
        public const string SlotUnsupportedHint = "Choose Learn Abilities to learn new skills.";
    }
}
