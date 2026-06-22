using System.Collections.Generic;
using System.Reflection;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;
using TMPro;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The journal (Alt+Q — the game's <c>UITabs.RUMORS</c> window, a <c>JournalScript</c>-driven
    /// panel under the PlayerHUD rather than a <c>currentFullScreenUI</c>). It has four tabs the game
    /// switches between with a button bar: <b>recipes</b>, <b>rumors</b> (the quest log), <b>combat
    /// log</b>, and the <b>monsterpedia</b>. We re-present it as one owned grid built fresh every tick:
    ///
    /// <list type="bullet">
    /// <item>a <b>tab bar</b> first row (recipes / rumors / combat log / monsterpedia); confirm on a
    /// cell drives the game's own <c>SwitchJournalTab</c> so the visible panel and our content stay in
    /// step, and marks the active tab;</item>
    /// <item>below it, one build function per tab emits that tab's content as further rows — the
    /// active tab is the game's live <c>JournalScript.journalState</c>.</item>
    /// </list>
    ///
    /// <para><b>Data source:</b> each tab reads the underlying live game data directly, independent of
    /// which tab the game has visually active — recipes from <c>MetaProgressScript.recipesKnown</c>,
    /// rumors from the hero's <c>myQuests</c>, the combat log from
    /// <c>GameLogScript.journalLogStringBuffer</c>, and the monsterpedia from
    /// <c>BakedMonsterpedia.GetAllMonstersInPedia()</c>. Read live each build; never cached.</para>
    ///
    /// <para><b>Monsterpedia:</b> we list every monster in the pedia as one flat row each. That is
    /// deliberately a long list (a search/filter is planned), but each entry's description reuses the
    /// game's own progressive-reveal monsterpedia text (more detail as you defeat more of a kind).</para>
    /// </summary>
    internal sealed class JournalOverlay : IUiOverlay {
        // GetRecipeInfo writes the full recipe readout into this private TMP rather than returning it,
        // so we read it back after calling — the same reuse-the-game's-builder trick the character
        // sheet uses for its tooltip scroll. Assigned during the journal's init coroutine (always done
        // by the time the screen can be opened); we still null-guard it.
        private static readonly FieldInfo RecipeTextField =
            typeof(JournalScript).GetField("recipeText", BindingFlags.NonPublic | BindingFlags.Static);

        public OverlayId Id => OverlayId.Journal;

        public OverlayResult Handler() {
            // Yield to the dialog overlay for the abandon-rumor confirm conversation (TryQuitRumor
            // opens it), exactly as the options menu yields for its save/quit confirms.
            if (UIManagerScript.dialogBoxOpen) {
                return OverlayResult.Inactive;
            }

            return Open() ? OverlayResult.Active(this) : OverlayResult.Inactive;
        }

        /// <summary>True while the journal window is the open UI. The journal is not a
        /// <c>currentFullScreenUI</c>; its liveness is the RUMORS window state plus the quest-sheet
        /// object being active.</summary>
        private static bool Open() {
            if (UIManagerScript.singletonUIMS == null || !UIManagerScript.GetWindowState(UITabs.RUMORS)) {
                return false;
            }

            return UIManagerScript.questSheet != null && UIManagerScript.questSheet.activeInHierarchy;
        }

        public void Build(IOverlayBuilder builder) {
            builder.CaptureInput();

            if (!Open()) {
                return;
            }

            BuildTabBar(builder);

            switch (JournalScript.journalState) {
                case JournalTabs.RECIPES:
                    BuildRecipesTab(builder);
                    break;
                case JournalTabs.RUMORS:
                    BuildRumorsTab(builder);
                    break;
                case JournalTabs.COMBATLOG:
                    BuildCombatLogTab(builder);
                    break;
                case JournalTabs.MONSTERPEDIA:
                    BuildMonsterpediaTab(builder);
                    break;
            }
        }

        // --- Tab bar ---------------------------------------------------------------------------

        private static void BuildTabBar(IOverlayBuilder builder) {
            builder.StartRow("tabs");
            AddTab(builder, JournalTabs.RECIPES, "ui_btn_recipes");
            AddTab(builder, JournalTabs.RUMORS, "ui_btn_rumors");
            AddTab(builder, JournalTabs.COMBATLOG, "ui_btn_combatlog");
            AddTab(builder, JournalTabs.MONSTERPEDIA, "ui_btn_monsterpedia");
            builder.EndRow();
        }

        private static void AddTab(IOverlayBuilder builder, JournalTabs tab, string stringKey) {
            builder.AddClickable(
                ControlId.Structural("journal:tab:" + tab),
                ctx => {
                    ctx.Message.Fragment(GameLabel(stringKey));
                    if (JournalScript.journalState == tab) {
                        ctx.Message.Fragment(ModStrings.Selected);
                    }
                },
                (ctx, mods) => SwitchTab(ctx, tab, stringKey)
            );
        }

        // Drive the game's own tab switch (plays its sound, swaps the visible panel, persists the
        // choice in journalState — which our next rebuild reads to pick the content block). It moves
        // the game's uiObjectFocus, but we capture input and do not chase game focus, so our cursor
        // stays on the tab cell; the player then steps down into the new content.
        private static void SwitchTab(OverlayCtx ctx, JournalTabs tab, string stringKey) {
            if (JournalScript.singleton != null && JournalScript.journalState != tab) {
                JournalScript.singleton.SwitchJournalTab((int)tab);
            }

            ctx.Message.Fragment(GameLabel(stringKey)).Fragment(ModStrings.Selected);
        }

        private static string GameLabel(string key) {
            return GameLabelReader.Clean(StringManager.GetString(key));
        }

        // --- Recipes tab -----------------------------------------------------------------------

        private static void BuildRecipesTab(IOverlayBuilder builder) {
            List<string> known = MetaProgressScript.recipesKnown;
            if (known == null || known.Count == 0) {
                builder.AddLabel(
                    ControlId.Structural("journal:recipe:none"),
                    ctx => ctx.Message.Fragment(ModStrings.NoRecipesKnown)
                );
                return;
            }

            for (int i = 0; i < known.Count; i++) {
                int idx = i;
                string refName = known[i];
                Recipe recipe = CookingScript.FindRecipe(refName);

                builder.AddItem(
                    ControlId.Structural("journal:recipe:" + i),
                    new NodeVtable {
                        Label = ctx => RecipeLabel(ctx.Message, recipe, refName),
                        // Confirm cooks it if a cooking station is adjacent and the ingredients are on
                        // hand; read-info reads the game's full recipe readout.
                        OnClick = (ctx, mods) => TryCook(ctx, recipe),
                        OnReadInfo = ctx => {
                            string info = RecipeInfo(idx);
                            if (!string.IsNullOrEmpty(info)) {
                                ctx.Message.Fragment(info);
                            }
                        },
                    }
                );
            }
        }

        private static void RecipeLabel(MessageBuilder message, Recipe recipe, string refName) {
            if (recipe == null) {
                message.Fragment(refName);
                return;
            }

            message.Fragment(GameLabelReader.Clean(recipe.displayName));
            if (CanCookNow(recipe)) {
                message.Fragment(ModStrings.CanCook);
            }
        }

        // The game's own recipe readout (item description, ingredients, healing, effects). GetRecipeInfo
        // writes it into the private recipeText TMP, which we read back; we never call it if that field
        // is unset (it would NRE), though it is always set once the journal exists.
        private static string RecipeInfo(int index) {
            var tmp = RecipeTextField != null ? RecipeTextField.GetValue(null) as TextMeshProUGUI : null;
            if (tmp == null) {
                return null;
            }

            JournalScript.GetRecipeInfo(index);
            return GameLabelReader.Clean(tmp.text);
        }

        private static bool CanCookNow(Recipe recipe) {
            return CookingScript.CheckRecipe(
                recipe.refName,
                GameMasterScript.heroPCActor.myInventory.GetAllCookingIngredients()
            ) != null;
        }

        // Cook a recipe straight from the journal, mirroring GetRecipeInfoAndTryCook's core (we skip its
        // food-sprite flourish, which indexes the on-screen button grid and would break for a recipe
        // beyond the 18 grid slots). Gated with spoken feedback the game omits: no station, or no
        // ingredients. On success the game-log "cooked_food" line is announced via the log→speech path.
        private static void TryCook(OverlayCtx ctx, Recipe recipe) {
            if (recipe == null) {
                UIManagerScript.PlayCursorSound("Error");
                return;
            }

            if (!CookingStationAdjacent()) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.NoCookingStation);
                return;
            }

            if (!CanCookNow(recipe)) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.MissingIngredients);
                return;
            }

            Item item = CookingScript.MakeRecipeIfPossible(recipe);
            if (item == null) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.MissingIngredients);
                return;
            }

            GameMasterScript.heroPCActor.myInventory.AddItem(item, stackItems: true);
            StringManager.SetTag(0, item.displayName);
            UIManagerScript.PlayCursorSound("CookingSuccess");
            GameLogScript.LogWriteStringRef("cooked_food");
        }

        private static bool CookingStationAdjacent() {
            CustomAlgorithms.GetTilesAroundPoint(
                GameMasterScript.heroPCActor.GetPos(), 1, MapMasterScript.activeMap
            );
            for (int i = 0; i < CustomAlgorithms.numTilesInBuffer; i++) {
                foreach (Actor actor in CustomAlgorithms.tileBuffer[i].GetAllActors()) {
                    if (actor.GetActorType() == ActorTypes.NPC && (actor as NPC).cookingPossible) {
                        return true;
                    }
                }
            }

            return false;
        }

        // --- Rumors (quest log) tab ------------------------------------------------------------

        private static void BuildRumorsTab(IOverlayBuilder builder) {
            List<QuestScript> quests = GameMasterScript.heroPCActor.myQuests;
            bool any = false;

            if (quests != null) {
                for (int i = 0; i < quests.Count; i++) {
                    QuestScript quest = quests[i];
                    if (quest == null || quest.complete) {
                        continue;
                    }

                    any = true;
                    int questIndex = i;

                    builder.StartRow("journal:rumor:" + i);

                    // The objective text on the cell; the rewards on read-info.
                    builder.AddItem(
                        ControlId.Structural("journal:rumor:text:" + i),
                        new NodeVtable {
                            Label = ctx =>
                                ctx.Message.Fragment(GameLabelReader.Clean(quest.GetAllQuestTextExceptRewards(40))),
                            OnReadInfo = ctx =>
                                ctx.Message.Fragment(GameLabelReader.Clean(quest.GetRewardText(40))),
                        }
                    );

                    // Abandon runs the game's own quit-rumor path, which closes the journal and opens a
                    // confirmation conversation (narrated by the dialog overlay), so we speak nothing.
                    builder.AddItem(
                        ControlId.Structural("journal:rumor:abandon:" + i),
                        new NodeVtable {
                            Label = ctx => ctx.Message.Fragment(ModStrings.AbandonRumor),
                            OnClick = (ctx, mods) => {
                                if (JournalScript.singleton != null) {
                                    JournalScript.singleton.TryQuitRumor(questIndex);
                                }
                            },
                        }
                    );

                    builder.EndRow();
                }
            }

            if (!any) {
                builder.AddLabel(
                    ControlId.Structural("journal:rumor:none"),
                    ctx => ctx.Message.Fragment(ModStrings.NoRumors)
                );
            }
        }

        // --- Combat log tab --------------------------------------------------------------------

        private static void BuildCombatLogTab(IOverlayBuilder builder) {
            Queue<string> buffer = GameLogScript.journalLogStringBuffer;
            if (buffer == null || buffer.Count == 0) {
                builder.AddLabel(
                    ControlId.Structural("journal:log:none"),
                    ctx => ctx.Message.Fragment(ModStrings.CombatLogEmpty)
                );
                return;
            }

            // The queue is oldest-first; the game shows newest-first, so we walk it in reverse.
            var lines = new List<string>(buffer);
            for (int i = lines.Count - 1; i >= 0; i--) {
                string clean = GameLabelReader.Clean(lines[i]);
                if (string.IsNullOrEmpty(clean)) {
                    continue;
                }

                builder.AddLabel(
                    ControlId.Structural("journal:log:" + i),
                    ctx => ctx.Message.Fragment(clean)
                );
            }
        }

        // --- Monsterpedia tab ------------------------------------------------------------------

        private static void BuildMonsterpediaTab(IOverlayBuilder builder) {
            List<MonsterTemplateData> all = BakedMonsterpedia.GetAllMonstersInPedia();
            if (all == null || all.Count == 0) {
                builder.AddLabel(
                    ControlId.Structural("journal:mon:none"),
                    ctx => ctx.Message.Fragment(ModStrings.None)
                );
                return;
            }

            for (int i = 0; i < all.Count; i++) {
                MonsterTemplateData md = all[i];
                builder.AddItem(
                    ControlId.Structural("journal:mon:" + i),
                    new NodeVtable {
                        Label = ctx => MonsterLabel(ctx.Message, md),
                        OnReadInfo = ctx => MonsterInfo(ctx.Message, md),
                    }
                );
            }
        }

        // The list label: the monster's name once it has been defeated at least once, else a generic
        // "undiscovered" (mirroring the game's silhouette).
        private static void MonsterLabel(MessageBuilder message, MonsterTemplateData md) {
            int defeated = MetaProgressScript.GetMonstersDefeated(md.refName);
            message.Fragment(defeated > 0 ? GameLabelReader.Clean(md.monsterName) : ModStrings.Undiscovered);
        }

        // The game's progressive monsterpedia readout, ported from JournalScript.HoverMonsterInfo as a
        // side-effect-free string build (no focus change, no scroll): more lines unlock as the kill
        // count crosses the game's thresholds. Reuses the game's own monsterpedia_statsN templates.
        private static void MonsterInfo(MessageBuilder message, MonsterTemplateData md) {
            int defeated = MetaProgressScript.GetMonstersDefeated(md.refName);
            if (defeated == 0) {
                message.Fragment(ModStrings.Undiscovered);
                return;
            }

            message.Fragment(GameLabelReader.Clean(md.monsterName));

            StringManager.SetTag(0, md.baseLevel.ToString());
            StringManager.SetTag(1, Monster.GetFamilyName(md.monFamily));
            message.ListItem(GameLabelReader.Clean(StringManager.GetString("monsterpedia_stats1")));

            StringManager.SetTag(0, defeated.ToString());
            message.ListItem(GameLabelReader.Clean(StringManager.GetString("monsterpedia_stats2")));

            if (md.isBoss || defeated >= 3) {
                float[] stats = {
                    0f, 0f, 0f, md.strength, md.swiftness, md.spirit, md.discipline, md.guile, 0f, 0f, 0f, 0f,
                };
                float best = 0f;
                int bestIdx = -1;
                for (int i = 0; i < stats.Length; i++) {
                    if (stats[i] > best) {
                        best = stats[i];
                        bestIdx = i;
                    }
                }

                StringManager.SetTag(0, ((int)md.hp).ToString());
                StringManager.SetTag(1, bestIdx >= 0 ? StatBlock.statNames[bestIdx] : "");
                StringManager.SetTag(2, ((int)best).ToString());
                StringManager.SetTag(3, md.aggroRange.ToString());
                message.ListItem(GameLabelReader.Clean(StringManager.GetString("monsterpedia_stats3")));
            }

            if (md.isBoss || defeated >= 5) {
                string learnText = null;
                Item witem;
                Weapon weapon = GameMasterScript.masterItemList.TryGetValue(md.weaponID, out witem)
                    ? witem as Weapon
                    : null;
                StringManager.SetTag(0, weapon != null ? ((float)(int)weapon.power * 10f).ToString() : "?");

                string abilities = "";
                int count = 0;
                foreach (MonsterPowerData power in md.monsterPowers) {
                    if (!string.IsNullOrEmpty(power.abilityRef.teachPlayerAbility)) {
                        abilities += power.abilityRef.abilityName;
                        StringManager.SetTag(
                            4, GameMasterScript.masterAbilityList[power.abilityRef.teachPlayerAbility].abilityName
                        );
                        learnText = StringManager.GetString("monsterpedia_playerlearn");
                    } else {
                        abilities += power.abilityRef.abilityName;
                    }

                    if (count < md.monsterPowers.Count - 1) {
                        abilities += ", ";
                    }

                    count++;
                }

                StringManager.SetTag(1, abilities);
                message.ListItem(GameLabelReader.Clean(StringManager.GetString("monsterpedia_stats4")));
                if (!string.IsNullOrEmpty(learnText)) {
                    message.ListItem(GameLabelReader.Clean(learnText));
                }
            }

            if (md.isBoss || defeated >= 10) {
                int total = 0;
                for (int j = 0; j < 27; j++) {
                    if (md.monAttributes[j] > 0 && !string.IsNullOrEmpty(Monster.GetAttributeName(j))) {
                        total++;
                    }
                }

                string attributes = "";
                int written = 0;
                for (int k = 0; k < 27; k++) {
                    if (md.monAttributes[k] > 0) {
                        attributes += Monster.GetAttributeName(k) + " (" + md.monAttributes[k]
                            + StringManager.GetLocalizedSymbol(AbbreviatedSymbols.PERCENT) + ")";
                        if (written < total - 1) {
                            attributes += ", ";
                        }

                        written++;
                    }
                }

                StringManager.SetTag(0, attributes);
                message.ListItem(GameLabelReader.Clean(StringManager.GetString("monsterpedia_stats5")));
            }
        }
    }
}
