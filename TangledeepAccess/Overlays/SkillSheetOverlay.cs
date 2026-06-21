using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Gameplay;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The skill / job sheet (<c>Switch_UISkillSheet</c>, the "J" tab). The game models it as two
    /// side-by-side scrolling button columns plus a mode-toggle row, a vertical hotbar, and a
    /// selected-info panel, and it runs in one of two modes: <b>Learn</b> (buy job abilities with
    /// JP) and <b>Slot</b> (assign learned actives to the hotbar, equip passives). We re-present it
    /// as one owned grid, built fresh every tick.
    ///
    /// <para><b>Learn mode</b> (a 2-D grid): a header row (status anchor + the two mode buttons),
    /// then an <b>innate</b> row (a summary cell that reads the whole bonus text, then one cell per
    /// passive tier — tier 2/3 announce "locked" until their JP / mastery gate is met), then a
    /// <b>learn</b> row (a label cell, then one cell per buyable ability — confirm learns/masters,
    /// read-info reads the tooltip).</para>
    ///
    /// <para><b>Slot mode</b>: an <b>equipped passives</b> row (confirm to unequip), an <b>equippable
    /// passives</b> row (the slot-using passives, with the "N of 4" budget; confirm equips/unequips),
    /// an <b>always-on passives</b> row (confirm toggles), then one row per job the hero knows an
    /// active ability from (confirm <i>uses</i> the ability, number keys 1-8 <i>assign</i> it to that
    /// slot on bar 1, Ctrl+1-8 to bar 2), and a <b>show unlearned</b> toggle that adds every job's
    /// unlearned abilities (suffixed "unlearned") so the player can browse the whole tree. Changing
    /// skills is gated to safe areas, mirroring the game.</para>
    ///
    /// <para><b>Data source:</b> the hero's own learned abilities for what they have, and
    /// <c>masterJobList</c> for the unlearned tree. Read live each build; never cached. Each row's
    /// first cell names the row; left/right walks its contents, up/down moves between rows.</para>
    /// </summary>
    internal sealed class SkillSheetOverlay : IUiOverlay {
        // The screen's current mode; private on the screen type.
        private static readonly AccessTools.FieldRef<Switch_UISkillSheet, ESkillSheetMode> SheetMode =
            AccessTools.FieldRefAccess<Switch_UISkillSheet, ESkillSheetMode>("sheetMode");

        // The screen's own builder for the right-column innate-bonus text (tier 1/2/3 + infusions,
        // with live lock states). Private; reflected so we speak the game's exact string.
        private static readonly MethodInfo InnateText =
            AccessTools.Method(typeof(Switch_UISkillSheet), "GetStringForJobInnateBonuses");

        // Slot-mode view toggle (a UI preference, not game state): also list the abilities the hero
        // has not learned yet, so the player can browse the whole tree. Persists across rebuilds.
        private static bool _showUnlearned;

        public OverlayId Id => OverlayId.Skills;

        public OverlayResult Handler() {
            return Screen() != null ? OverlayResult.Active(this) : OverlayResult.Inactive;
        }

        /// <summary>The live skill sheet if it is the open full-screen UI, else null.</summary>
        private static Switch_UISkillSheet Screen() {
            UIManagerScript ums = UIManagerScript.singletonUIMS;
            if (ums == null) {
                return null;
            }

            if (!(ums.currentFullScreenUI is Switch_UISkillSheet sheet)) {
                return null;
            }

            return sheet.gameObject.activeInHierarchy ? sheet : null;
        }

        public void Build(IOverlayBuilder builder) {
            builder.CaptureInput();

            Switch_UISkillSheet screen = Screen();
            if (screen == null) {
                return;
            }

            BuildHeaderRow(builder, screen);

            if (SheetMode(screen) == ESkillSheetMode.purchase_abilities) {
                BuildLearnBody(builder, screen);
            } else {
                BuildSlotBody(builder);
            }
        }

        // --- Header / mode row -----------------------------------------------------------------

        private static void BuildHeaderRow(IOverlayBuilder builder, Switch_UISkillSheet screen) {
            builder.StartRow("header");

            builder.AddLabel(ControlId.Structural("skill:header"), ctx => HeaderLabel(ctx.Message, screen));

            AddModeButton(builder, screen, "learn", ModStrings.LearnMode, ESkillSheetMode.purchase_abilities);
            AddModeButton(builder, screen, "slot", ModStrings.SlotMode, ESkillSheetMode.assign_abilities);

            builder.EndRow();
        }

        private static void HeaderLabel(MessageBuilder message, Switch_UISkillSheet screen) {
            HeroPC hero = GameMasterScript.heroPCActor;
            message.Fragment(ModStrings.SkillsHeader);
            message.ListItem(GameLabelReader.Clean(hero.myJob.DisplayName));
            message.ListItem(ModStrings.Jp((int)hero.GetCurJP()));
            message.ListItem(
                SheetMode(screen) == ESkillSheetMode.purchase_abilities
                    ? ModStrings.ModeLearning
                    : ModStrings.ModeSlotting
            );
        }

        // Confirm switches the game into this mode (its own EnterNewMode — plays the cue, rebuilds
        // the columns). Our cursor stays on the button (stable key); the body rebuilds to match.
        private static void AddModeButton(
            IOverlayBuilder builder,
            Switch_UISkillSheet screen,
            string key,
            string word,
            ESkillSheetMode mode
        ) {
            builder.AddClickable(
                ControlId.Structural("skill:mode:" + key),
                ctx => ctx.Message.Fragment(word).Fragment(SheetMode(screen) == mode ? ModStrings.Selected : null),
                (ctx, mods) => {
                    screen.EnterNewMode(mode);
                    ctx.Message.Fragment(word);
                }
            );
        }

        // --- Learn-mode body -------------------------------------------------------------------

        private static void BuildLearnBody(IOverlayBuilder builder, Switch_UISkillSheet screen) {
            HeroPC hero = GameMasterScript.heroPCActor;
            CharacterJobData job = hero.myJob;
            bool jobMastered = hero.HasMasteredJob(job);

            // Innate row: a summary cell (reads the whole bonus text, infusions included) then one
            // cell per defined passive tier. Tier 2 unlocks at 1000 JP spent in the job, tier 3 at
            // job mastery — mirroring the game's GetStringForJobInnateBonuses gating.
            builder.StartRow("innate");
            builder.AddItem(
                ControlId.Structural("skill:innate"),
                new NodeVtable {
                    Label = ctx => ctx.Message.Fragment(ModStrings.InnateBonuses),
                    OnClick = (ctx, mods) => ReadInnate(ctx, screen),
                    OnReadInfo = ctx => ReadInnate(ctx, screen),
                }
            );
            AddTier(builder, 1, job.BonusDescription1, locked: false);
            AddTier(builder, 2, job.BonusDescription2, locked: hero.jobJPspent[(int)job.jobEnum] < 1000f);
            AddTier(builder, 3, job.BonusDescription3, locked: !jobMastered);
            builder.EndRow();

            // Learn row: a label cell, then one cell per buyable ability. Mirror the game's
            // FillJobAbilitiesList filter: innates are passive/automatic, and post-mastery skills only
            // appear once the job is mastered.
            builder.StartRow("learn");
            builder.AddLabel(
                ControlId.Structural("skill:learn"),
                ctx => ctx.Message.Fragment(ModStrings.LearnAbilityRow)
            );
            foreach (JobAbility ja in job.JobAbilities) {
                if (ja.ability == null || ja.innate || (ja.postMasteryAbility && !jobMastered)) {
                    continue;
                }

                JobAbility ability = ja;
                builder.AddItem(
                    ControlId.Structural("skill:abil:" + ability.ability.refName),
                    new NodeVtable {
                        Label = ctx => AbilityLabel(ctx.Message, ability),
                        // Confirm is the primary action: learn (or master) the ability.
                        OnClick = (ctx, mods) => LearnAbility(ctx, ability),
                        // Read-info (K) is the full tooltip: cost-to-learn, description, repeat-buy.
                        OnReadInfo = ctx =>
                            ctx.Message.Fragment(GameLabelReader.Clean(ability.GetInformationForTooltip())),
                    }
                );
            }

            builder.EndRow();
        }

        // One passive-tier cell, skipped entirely when the job does not define that tier (empty
        // description). The label reads the game's own tier header (which already states the JP /
        // mastery requirement when locked) and the tier's effect text, prefixed with "locked" when
        // its gate is unmet so the state is explicit rather than inferred.
        private static void AddTier(IOverlayBuilder builder, int tier, string description, bool locked) {
            if (string.IsNullOrEmpty(description)) {
                return;
            }

            builder.AddItem(
                ControlId.Structural("skill:tier:" + tier),
                new NodeVtable {
                    Label = ctx => {
                        if (locked) {
                            ctx.Message.Fragment(ModStrings.Locked);
                        }

                        ctx.Message.Fragment(GameLabelReader.Clean(TierHeader(tier, locked)));
                        ctx.Message.Fragment(
                            GameLabelReader.Clean(CustomAlgorithms.ParseRichText(description, false))
                        );
                    },
                }
            );
        }

        // The game's own tier header string; when locked it appends the requirement clause, exactly
        // as GetStringForJobInnateBonuses builds it ("Tier 2 Passive Bonus (Spend 1000+ JP)").
        private static string TierHeader(int tier, bool locked) {
            switch (tier) {
                case 1:
                    return StringManager.GetString("ui_job_innate_bonus1");
                case 2:
                    return StringManager.GetString("ui_job_innate_bonus2")
                        + (locked ? " " + StringManager.GetString("ui_job_bonus2_jp_req") : "");
                case 3:
                    return StringManager.GetString("ui_job_innate_bonus3")
                        + (locked ? " " + StringManager.GetString("ui_job_bonus3_jp_req") : "");
                default:
                    return "";
            }
        }

        // Spoken status of a buyable ability, mirroring the game's eligibility coloring
        // (Action_CheckJobAbilityEligibility): owned/mastered vs. cost-to-learn vs. unaffordable.
        private static void AbilityLabel(MessageBuilder message, JobAbility ja) {
            HeroPC hero = GameMasterScript.heroPCActor;
            AbilityScript ab = ja.ability;

            message.Fragment(GameLabelReader.Clean(ab.abilityName));

            bool isMastered = ja.masterCost > 0 && hero.myAbilities.HasMasteredAbility(ab);
            bool owned = hero.myAbilities.HasAbility(ab);

            if (isMastered) {
                message.ListItem(ModStrings.Mastered);
            } else if (owned && !ja.repeatBuyPossible) {
                message.ListItem(ModStrings.Learned);
                // Owned but still masterable: the game greys it, but confirm will master it.
                if (ja.masterCost > 0) {
                    message.ListItem(ModStrings.CanMasterFor(ja.masterCost));
                    if (hero.GetCurJP() < ja.masterCost) {
                        message.Fragment(ModStrings.NotEnough);
                    }
                }
            } else {
                int cost = hero.GetCostForAbilityBecauseWeDoStuffIfWeArentInOurStartingJob(ja);
                message.ListItem(ModStrings.Costs(cost));
                if (hero.GetCurJP() < cost) {
                    message.Fragment(ModStrings.NotEnough);
                }

                if (ja.repeatBuyPossible) {
                    message.ListItem(ModStrings.Repeatable);
                }
            }

            if (ab.toggled) {
                message.ListItem(ModStrings.ToggledOn);
            }
        }

        // Learn or master the ability via the game's own TryLearnAbility (which picks the master
        // path automatically when the ability is owned, masterable, and not yet mastered, spends the
        // JP, and refreshes the screen). We pre-check so the spoken reason is specific, and play the
        // learn cue ourselves (TryLearnAbility does not — that lives in the game's click animation).
        private static void LearnAbility(OverlayCtx ctx, JobAbility ja) {
            HeroPC hero = GameMasterScript.heroPCActor;
            AbilityScript ab = ja.ability;

            bool owned = hero.myAbilities.HasAbility(ab);
            bool canMaster = owned && ja.masterCost > 0 && !hero.myAbilities.HasMasteredAbility(ab);

            if (owned && !ja.repeatBuyPossible && !canMaster) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.AlreadyLearned);
                return;
            }

            int cost = canMaster
                ? ja.masterCost
                : hero.GetCostForAbilityBecauseWeDoStuffIfWeArentInOurStartingJob(ja);
            if (hero.GetCurJP() < cost) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.NotEnoughJp);
                return;
            }

            if (hero.TryLearnAbility(ja)) {
                UIManagerScript.PlayCursorSound("Ultra Learn");
                ctx.Message.Fragment(canMaster ? ModStrings.Mastered : ModStrings.Learned);
                ctx.Message.Fragment(GameLabelReader.Clean(ab.GetNameForUI()));
                ctx.Message.ListItem(ModStrings.JpRemaining((int)hero.GetCurJP()));
            } else {
                // Pre-checks passed but the game still refused (e.g. wrong job) — it logs the reason.
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.CantLearn);
            }
        }

        private static void ReadInnate(OverlayCtx ctx, Switch_UISkillSheet screen) {
            string raw = (string)InnateText.Invoke(screen, null);
            ctx.Message.Fragment(GameLabelReader.Clean(raw));
        }

        // --- Slot-mode body --------------------------------------------------------------------

        private static void BuildSlotBody(IOverlayBuilder builder) {
            HeroPC hero = GameMasterScript.heroPCActor;

            BuildEquippedRow(builder, hero);
            BuildPassiveRow(builder, "equippable", ModStrings.EquippablePassivesRow, slotUsing: true, hero);
            BuildPassiveRow(builder, "alwayson", ModStrings.AlwaysOnPassivesRow, slotUsing: false, hero);
            BuildActiveJobRows(builder, hero);

            builder.AddClickable(
                ControlId.Structural("skill:showunlearned"),
                ctx => ctx.Message.Fragment(ModStrings.ShowUnlearned(_showUnlearned)),
                (ctx, mods) => {
                    _showUnlearned = !_showUnlearned;
                    ctx.Message.Fragment(ModStrings.ShowUnlearned(_showUnlearned));
                }
            );
        }

        // The hero's currently-equipped passives, for a quick loadout read; confirm unequips. Always
        // the learned set (you cannot equip what you have not learned), so unaffected by show-unlearned.
        private static void BuildEquippedRow(IOverlayBuilder builder, HeroPC hero) {
            builder.StartRow("equipped");

            int count = 0;
            builder.AddLabel(
                ControlId.Structural("skill:equipped"),
                ctx => {
                    ctx.Message.Fragment(ModStrings.EquippedRow);
                    if (count == 0) {
                        ctx.Message.Fragment(ModStrings.None);
                    }
                }
            );

            foreach (AbilityScript a in hero.myAbilities.abilities) {
                if (!a.displayInList || !a.passiveAbility || !a.passiveEquipped) {
                    continue;
                }

                count++;
                AbilityScript ability = a;
                builder.AddItem(
                    ControlId.Structural("skill:eq:" + ability.refName),
                    new NodeVtable {
                        Label = ctx => {
                            ctx.Message.Fragment(GameLabelReader.Clean(ability.GetNameForUI()));
                            if (!ability.UsePassiveSlot) {
                                ctx.Message.Fragment(ModStrings.AlwaysOn);
                            }
                        },
                        OnClick = (ctx, mods) => Unequip(ctx, ability),
                        OnReadInfo = ctx =>
                            ctx.Message.Fragment(GameLabelReader.Clean(ability.GetInformationForTooltip())),
                    }
                );
            }

            builder.EndRow();
        }

        // One passive catalog row (slot-using or always-on), confirm toggles equip. With the show-
        // unlearned view on it also lists every job's matching passive the hero lacks (suffixed
        // "unlearned", confirm just points at the Learn tab).
        private static void BuildPassiveRow(
            IOverlayBuilder builder,
            string key,
            string rowLabel,
            bool slotUsing,
            HeroPC hero
        ) {
            List<AbilityScript> passives = GatherPassives(hero, slotUsing);

            builder.StartRow("passrow:" + key);
            builder.AddLabel(
                ControlId.Structural("skill:passrow:" + key),
                ctx => {
                    ctx.Message.Fragment(rowLabel);
                    if (slotUsing) {
                        ctx.Message.ListItem(ModStrings.PassiveSlotsUsed(hero.NumberOfPassiveSlotsTaken()));
                    }

                    if (passives.Count == 0) {
                        ctx.Message.Fragment(ModStrings.None);
                    }
                }
            );

            foreach (AbilityScript a in passives) {
                AbilityScript ability = a;
                bool learned = hero.myAbilities.HasAbility(ability);
                builder.AddItem(
                    ControlId.Structural("skill:passive:" + key + ":" + ability.refName),
                    new NodeVtable {
                        Label = ctx => PassiveLabel(ctx.Message, ability, learned),
                        OnClick = (ctx, mods) => TogglePassive(ctx, ability, learned),
                        OnReadInfo = ctx =>
                            ctx.Message.Fragment(GameLabelReader.Clean(ability.GetInformationForTooltip())),
                    }
                );
            }

            builder.EndRow();
        }

        // One row per job the hero knows an active ability from (or, with show-unlearned, per job that
        // has any active). Confirm uses the ability; 1-8 assign it to bar 1, Ctrl+1-8 to bar 2.
        private static void BuildActiveJobRows(IOverlayBuilder builder, HeroPC hero) {
            var byJob = new Dictionary<CharacterJobs, List<AbilityScript>>();
            var seen = new HashSet<string>();

            foreach (AbilityScript a in hero.myAbilities.abilities) {
                if (a.displayInList && !a.passiveAbility && seen.Add(a.refName)) {
                    AddToJob(byJob, a.jobLearnedFrom, a);
                }
            }

            if (_showUnlearned) {
                foreach (CharacterJobData job in GameMasterScript.masterJobList) {
                    foreach (JobAbility ja in job.JobAbilities) {
                        AbilityScript a = ja.ability;
                        if (a == null || ja.innate || a.passiveAbility || hero.myAbilities.HasAbility(a)) {
                            continue;
                        }

                        if (seen.Add(a.refName)) {
                            AddToJob(byJob, job.jobEnum, a);
                        }
                    }
                }
            }

            // Emit in master-job order so the rows have a stable, familiar sequence.
            foreach (CharacterJobData job in GameMasterScript.masterJobList) {
                if (!byJob.TryGetValue(job.jobEnum, out List<AbilityScript> actives) || actives.Count == 0) {
                    continue;
                }

                BuildJobRow(builder, job, actives, hero);
            }
        }

        private static void BuildJobRow(
            IOverlayBuilder builder,
            CharacterJobData job,
            List<AbilityScript> actives,
            HeroPC hero
        ) {
            builder.StartRow("job:" + job.jobEnum);
            builder.AddLabel(
                ControlId.Structural("skill:jobrow:" + job.jobEnum),
                ctx => ctx.Message.Fragment(ModStrings.JobAbilitiesRow(GameLabelReader.Clean(job.DisplayName)))
            );

            foreach (AbilityScript a in actives) {
                AbilityScript ability = a;
                bool learned = hero.myAbilities.HasAbility(ability);
                builder.AddItem(
                    ControlId.Structural("skill:active:" + ability.refName),
                    new NodeVtable {
                        Label = ctx => ActiveLabel(ctx.Message, ability, learned),
                        // Confirm uses the ability (gated like the game); read-info reads the tooltip;
                        // 1-8 bind it to that slot on bar 1, Ctrl+1-8 to bar 2.
                        OnClick = (ctx, mods) => UseAbility(ctx, ability, learned),
                        OnReadInfo = ctx =>
                            ctx.Message.Fragment(GameLabelReader.Clean(ability.GetInformationForTooltip())),
                        OnAssignHotbar = ctx => AssignAbility(ctx, ability, learned),
                    }
                );
            }

            builder.EndRow();
        }

        // --- Slot-mode labels ------------------------------------------------------------------

        private static void PassiveLabel(MessageBuilder message, AbilityScript a, bool learned) {
            message.Fragment(GameLabelReader.Clean(a.GetNameForUI()));
            if (!learned) {
                message.Fragment(ModStrings.Unlearned);
                return;
            }

            if (a.passiveEquipped) {
                message.Fragment(ModStrings.Equipped);
            }
        }

        private static void ActiveLabel(MessageBuilder message, AbilityScript a, bool learned) {
            message.Fragment(GameLabelReader.Clean(a.GetNameForUI()));
            if (!learned) {
                message.Fragment(ModStrings.Unlearned);
                return;
            }

            string binding = Hotbar.FindBinding(a);
            if (binding != null) {
                message.ListItem(binding);
            }

            if (a.toggled) {
                message.ListItem(ModStrings.ToggledOn);
            }
        }

        // --- Slot-mode actions -----------------------------------------------------------------

        private static void TogglePassive(OverlayCtx ctx, AbilityScript a, bool learned) {
            if (!learned) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.LearnInLearnTab);
                return;
            }

            if (!CanAlterSkills()) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.CantChangeHere);
                return;
            }

            HeroPC hero = GameMasterScript.heroPCActor;
            if (a.passiveEquipped) {
                hero.myAbilities.UnequipPassiveAbility(a);
                UIManagerScript.PlayCursorSound("UITock");
                ctx.Message.Fragment(GameLabelReader.Clean(a.GetNameForUI())).Fragment(ModStrings.Unequipped);
                return;
            }

            // A slot-using passive needs a free slot; always-on passives never do.
            if (a.UsePassiveSlot && hero.NumberOfPassiveSlotsTaken() >= 4) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.NoFreePassiveSlots);
                return;
            }

            hero.myAbilities.EquipPassiveAbility(a);
            UIManagerScript.PlayCursorSound("UITick");
            ctx.Message.Fragment(GameLabelReader.Clean(a.GetNameForUI())).Fragment(ModStrings.Equipped);
        }

        private static void Unequip(OverlayCtx ctx, AbilityScript a) {
            if (!CanAlterSkills()) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.CantChangeHere);
                return;
            }

            GameMasterScript.heroPCActor.myAbilities.UnequipPassiveAbility(a);
            UIManagerScript.PlayCursorSound("UITock");
            ctx.Message.Fragment(GameLabelReader.Clean(a.GetNameForUI())).Fragment(ModStrings.Unequipped);
        }

        // Enter uses the ability — closing the sheet and casting through the game (into targeting for
        // a targeted ability). Mirrors the game's gate: using from the sheet is only allowed when the
        // player may use abilities outside the hotbar (an option / random-job mode); otherwise you
        // must put it on the bar and fire it there.
        private static void UseAbility(OverlayCtx ctx, AbilityScript a, bool learned) {
            if (!learned) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.LearnInLearnTab);
                return;
            }

            if (
                !GameModifiersScript.CanUseAbilitiesOutsideOfHotbar()
                && !RandomJobMode.IsCurrentGameInRandomJobMode()
            ) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.CantUseFromHere);
                return;
            }

            UIManagerScript.ForceCloseFullScreenUI();
            GameMasterScript.gmsSingleton.CheckAndTryAbility(a);
        }

        private static void AssignAbility(OverlayCtx ctx, AbilityScript a, bool learned) {
            int slot = ctx.Arg;
            int bank = ctx.Bank;
            if (slot < 1 || slot > Hotbar.PageSize) {
                return;
            }

            if (!learned) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.LearnInLearnTab);
                return;
            }

            if (!CanAlterSkills()) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.CantChangeHere);
                return;
            }

            Hotbar.Assign(a, slot, bank);
            UIManagerScript.PlayCursorSound("UITick");
            ctx.Message
                .Fragment(GameLabelReader.Clean(a.GetNameForUI()))
                .Fragment(ModStrings.Assigned)
                .Fragment(ModStrings.OnHotbar(bank + 1, slot));
        }

        // --- Slot-mode helpers -----------------------------------------------------------------

        // The hero's learned passives of one kind (slot-using or always-on), plus — when browsing
        // unlearned — every job's matching passive the hero lacks. Deduped by refName.
        private static List<AbilityScript> GatherPassives(HeroPC hero, bool slotUsing) {
            var seen = new HashSet<string>();
            var result = new List<AbilityScript>();

            foreach (AbilityScript a in hero.myAbilities.abilities) {
                if (a.displayInList && a.passiveAbility && a.UsePassiveSlot == slotUsing && seen.Add(a.refName)) {
                    result.Add(a);
                }
            }

            if (_showUnlearned) {
                foreach (CharacterJobData job in GameMasterScript.masterJobList) {
                    foreach (JobAbility ja in job.JobAbilities) {
                        AbilityScript a = ja.ability;
                        if (
                            a == null
                            || ja.innate
                            || !a.passiveAbility
                            || a.UsePassiveSlot != slotUsing
                            || hero.myAbilities.HasAbility(a)
                        ) {
                            continue;
                        }

                        if (seen.Add(a.refName)) {
                            result.Add(a);
                        }
                    }
                }
            }

            return result;
        }

        private static void AddToJob(
            Dictionary<CharacterJobs, List<AbilityScript>> byJob,
            CharacterJobs job,
            AbilityScript ability
        ) {
            if (!byJob.TryGetValue(job, out List<AbilityScript> list)) {
                list = new List<AbilityScript>();
                byJob[job] = list;
            }

            list.Add(ability);
        }

        // Whether the game permits changing slots/equips right now: only in towns / safe areas (or
        // when a modifier lifts the restriction). Mirrors the skill sheet's own gate.
        private static bool CanAlterSkills() {
            if (GameModifiersScript.CanUseAbilitiesOutsideOfHotbar()
                || RandomJobMode.IsCurrentGameInRandomJobMode()) {
                return true;
            }

            Map map = MapMasterScript.activeMap;
            return map != null && (map.IsTownMap() || map.dungeonLevelData.safeArea);
        }
    }
}
