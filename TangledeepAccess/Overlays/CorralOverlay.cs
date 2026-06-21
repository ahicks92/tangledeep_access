using System.Collections.Generic;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The monster corral keeper's screen (<c>MonsterCorralScript</c>), reached by talking to the
    /// corral NPC and choosing "manage". The game models it as a 12-slot scrolling list of tamed
    /// monsters, each row a 2-D cluster of buttons (feed / groom / info / make-pet / release), plus a
    /// separate full-screen <b>food picker</b> and a <b>monster stats</b> view — all driven by the
    /// game's <c>uiObjectFocus</c> neighbour graph, NOT <c>currentFullScreenUI</c>. We ignore that
    /// topology and re-present it as one owned grid, built fresh every tick:
    ///
    /// <list type="bullet">
    /// <item>a <b>header</b> anchor (count), then one <b>monster row</b> each: a summary cell
    /// (read-info reads the full battle/relationship readout) followed by make-pet / feed / groom /
    /// release action cells, then an <b>exit</b> row;</item>
    /// <item>when the player picks <i>feed</i>, the game swaps to its food interface; we follow by
    /// rebuilding as a <b>food picker</b> — one row per edible consumable (confirm feeds it) then a
    /// back-to-list row.</item>
    /// </list>
    ///
    /// <para>We fold the game's separate <i>info</i> stats screen into the summary cell's read-info,
    /// so we never open it (and never have to claim <c>monsterStatsInterfaceOpen</c>). Grooming and
    /// release both hand off to the game's own dialogs, which <see cref="DialogOverlay"/> owns; the
    /// JP-spend prompts and naming prompts likewise live there.</para>
    ///
    /// <para><b>Data source:</b> <c>MetaProgressScript.localTamedMonstersForThisSlot</c> for the list,
    /// <c>MonsterCorralScript.playerItemList</c> for the food. Read live each build; never cached.
    /// <see cref="SubIdentity"/> flips between "list" and "food" so focus resets cleanly on the swap.</para>
    /// </summary>
    internal sealed class CorralOverlay : IUiOverlay, ISubIdentified {
        public OverlayId Id => OverlayId.Corral;

        public OverlayResult Handler() {
            return MonsterCorralScript.corralInterfaceOpen || MonsterCorralScript.corralFoodInterfaceOpen
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        // The food picker and the list are one overlay id; a sub-identity change (list <-> food, or a
        // different monster being fed) is treated as a fresh open so focus resets to the start node.
        public string SubIdentity() {
            if (MonsterCorralScript.corralFoodInterfaceOpen) {
                return "food:" + (MonsterCorralScript.tcmSelected != null
                    ? MonsterCorralScript.tcmSelected.monsterID
                    : -1);
            }

            return "list";
        }

        public void Build(IOverlayBuilder builder) {
            builder.CaptureInput();

            // Food picker takes precedence: feeding closes the list and opens the food interface, so
            // both bools are never meaningfully "list" while food is up.
            if (MonsterCorralScript.corralFoodInterfaceOpen) {
                BuildFood(builder);
                return;
            }

            if (MonsterCorralScript.corralInterfaceOpen) {
                BuildList(builder);
            }
        }

        // --- Monster list ----------------------------------------------------------------------

        private static void BuildList(IOverlayBuilder builder) {
            List<TamedCorralMonster> monsters = MetaProgressScript.localTamedMonstersForThisSlot;
            int count = monsters?.Count ?? 0;

            builder.StartRow("header");
            builder.AddLabel(
                ControlId.Structural("corral:header"),
                ctx => {
                    ctx.Message.Fragment(ModStrings.CorralHeader);
                    ctx.Message.ListItem(ModStrings.CorralCount(count));
                }
            );
            builder.EndRow();

            if (count == 0) {
                builder.AddLabel(ControlId.Structural("corral:empty"), ctx => ctx.Message.Fragment(ModStrings.None));
                return;
            }

            for (int i = 0; i < count; i++) {
                int index = i;
                TamedCorralMonster tcm = monsters[index];
                int id = tcm.monsterID;

                // Distinct row key per monster so up/down lands on the summary cell (see InventoryOverlay).
                builder.StartRow("mon:" + id);

                builder.AddItem(
                    ControlId.Structural("corral:mon:" + id),
                    new NodeVtable {
                        Label = ctx => MonsterSummary(ctx.Message, tcm),
                        // Confirm reads the full info (no obvious single "primary" action on a monster).
                        OnClick = (ctx, mods) => ReadInfo(ctx, tcm),
                        OnReadInfo = ctx => ReadInfo(ctx, tcm),
                    }
                );

                AddAction(builder, "pet:" + id, ModStrings.MakePetAction,
                    () => MonsterCorralScript.singleton.PutOrGetMonsterInCorral(index));
                AddAction(builder, "feed:" + id, ModStrings.FeedAction,
                    () => MonsterCorralScript.singleton.FeedMonster(index));
                AddAction(builder, "groom:" + id, ModStrings.GroomAction,
                    () => MonsterCorralScript.singleton.OpenGroomMonsterInterface(index));
                AddAction(builder, "release:" + id, ModStrings.ReleaseAction,
                    () => MonsterCorralScript.singleton.ReleaseMonster(index));

                builder.EndRow();
            }

            // Exit row: closes the corral back to the dungeon. (Escape also passes through to the game.)
            builder.StartRow("exit");
            builder.AddClickable(
                ControlId.Structural("corral:exit"),
                ctx => ctx.Message.Fragment(ModStrings.ExitCorral),
                (ctx, mods) => MonsterCorralScript.CloseCorralInterface()
            );
            builder.EndRow();
        }

        // An action cell. The game methods play their own sounds and write their own game-log lines
        // (announced via the game-log speech path), and a successful action either swaps the screen or
        // closes it, so the cell appends no message of its own.
        private static void AddAction(IOverlayBuilder builder, string idPart, string word, System.Action act) {
            builder.AddClickable(
                ControlId.Structural("corral:act:" + idPart),
                ctx => ctx.Message.Fragment(word),
                (ctx, mods) => act()
            );
        }

        private static void MonsterSummary(MessageBuilder message, TamedCorralMonster tcm) {
            Monster mon = tcm.monsterObject;
            message.Fragment(GameLabelReader.Clean(mon.displayName));
            message.ListItem("level " + mon.myStats.GetLevel());
            message.ListItem();
            message.PushFraction(
                (int)mon.myStats.GetCurStat(StatTypes.HEALTH),
                (int)mon.myStats.GetStat(StatTypes.HEALTH, StatDataTypes.MAX),
                "HP"
            );
            message.ListItem(GameLabelReader.Clean(Monster.GetFamilyName(tcm.family)));
            message.ListItem(ModStrings.Happiness);
            message.Fragment(GameLabelReader.Clean(tcm.GetHappinessString()));
            message.ListItem(ModStrings.FoodMeter(tcm.foodMeter, tcm.CalculateFoodThresholdForPet()));
            message.ListItem(ModStrings.Rarity);
            message.Fragment(GameLabelReader.Clean(tcm.GetRarityString()));
            message.ListItem(ModStrings.Beauty);
            message.Fragment(GameLabelReader.Clean(tcm.GetBeautyString()));
            message.ListItem(ModStrings.Weight);
            message.Fragment(GameLabelReader.Clean(tcm.GetWeightString()));

            if (tcm.IsAngryAtPlayer()) {
                message.ListItem(ModStrings.AngryAtYou);
            } else if (!tcm.CanMonsterBePet()) {
                message.ListItem(ModStrings.TooUnhappyForPet);
            }
        }

        // Read-info folds in the game's full battle/relationship readout (the separate stats screen),
        // then this monster's feelings toward each other corral monster.
        private static void ReadInfo(OverlayCtx ctx, TamedCorralMonster tcm) {
            ctx.Message.Fragment(GameLabelReader.Clean(tcm.GetBattlePowerStats()));

            List<TamedCorralMonster> monsters = MetaProgressScript.localTamedMonstersForThisSlot;
            foreach (TamedCorralMonster other in monsters) {
                if (other == tcm) {
                    continue;
                }

                ctx.Message.ListItem(GameLabelReader.Clean(other.monsterObject.displayName));
                ctx.Message.Fragment(GameLabelReader.Clean(tcm.GetRelationshipString(other)));
            }
        }

        // --- Food picker -----------------------------------------------------------------------

        private static void BuildFood(IOverlayBuilder builder) {
            TamedCorralMonster tcm = MonsterCorralScript.tcmSelected;
            List<Item> foods = MonsterCorralScript.playerItemList;
            int count = foods?.Count ?? 0;

            builder.AddLabel(
                ControlId.Structural("corral:food:header"),
                ctx => {
                    ctx.Message.Fragment(tcm != null
                        ? ModStrings.Feeding(GameLabelReader.Clean(tcm.monsterObject.displayName))
                        : ModStrings.FeedAction);
                    ctx.Message.ListItem(ModStrings.ItemCount(count));
                }
            );

            if (count == 0) {
                builder.AddLabel(ControlId.Structural("corral:food:none"), ctx => ctx.Message.Fragment(ModStrings.NoFood));
            } else {
                foreach (Item item in foods) {
                    Item food = item;
                    builder.AddClickable(
                        ControlId.Structural("corral:food:" + food.actorUniqueID),
                        ctx => FoodLabel(ctx.Message, tcm, food),
                        (ctx, mods) => Feed(tcm, food)
                    );
                }
            }

            builder.AddClickable(
                ControlId.Structural("corral:food:back"),
                ctx => ctx.Message.Fragment(ModStrings.BackToList),
                (ctx, mods) => MonsterCorralScript.singleton.BackToMonsterList(0)
            );
        }

        private static void FoodLabel(MessageBuilder message, TamedCorralMonster tcm, Item food) {
            message.Fragment(GameLabelReader.Clean(food.displayName));
            message.PushQuantity(food.GetQuantity());

            // Only preferences the player has already discovered (by feeding) are surfaced.
            if (tcm != null && tcm.knownLoveFoods.Contains(food.actorRefName)) {
                message.ListItem(ModStrings.Loves);
            } else if (tcm != null && tcm.knownHateFoods.Contains(food.actorRefName)) {
                message.ListItem(ModStrings.Hates);
            }
        }

        // Feed the item through the monster's own FeedMonster (which writes the reaction log line,
        // announced via the game-log path), then refresh the food list so a depleted stack drops out.
        private static void Feed(TamedCorralMonster tcm, Item food) {
            if (tcm == null) {
                return;
            }

            tcm.FeedMonster(food);
            MonsterCorralScript.OpenCorralFoodInterface(tcm);
        }
    }
}
