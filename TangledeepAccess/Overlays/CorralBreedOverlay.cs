using System.Collections.Generic;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The corral breeding screen (<c>CorralBreedScript</c>), reached by sharing a romantic meal at
    /// the corral. The game shows up to 12 tamed monsters as a grid of toggle buttons plus a confirm
    /// that only appears once two are picked; selecting two reveals their mutual feelings and whether
    /// they are willing. We re-present it as one owned grid, built fresh every tick:
    ///
    /// <list type="bullet">
    /// <item>a <b>status</b> header (how many of two are selected, and — once two are — each one's
    /// feelings toward the other plus the willing / unwilling verdict);</item>
    /// <item>one <b>monster row</b> each (confirm toggles its selection, read-info reads the battle
    /// readout);</item>
    /// <item>a <b>breed</b> row that confirms once two are selected.</item>
    /// </list>
    ///
    /// <para>The game's own selection list (<c>monstersSelected</c>, private) is the source of truth
    /// for what is picked — read by reflection — so our toggles drive <c>SelectMonsterForBreeding</c>
    /// and reflect its result rather than tracking a parallel set. Confirming hands off to the game,
    /// which either opens the JP-spend slider dialog or runs the breed routine (and then the naming
    /// prompt) — all of which <see cref="DialogOverlay"/> owns.</para>
    /// </summary>
    internal sealed class CorralBreedOverlay : IUiOverlay {
        // The game's selected-pair list; private static on the breed script.
        private static readonly AccessTools.FieldRef<List<Monster>> Selected =
            AccessTools.StaticFieldRefAccess<List<Monster>>(
                AccessTools.Field(typeof(CorralBreedScript), "monstersSelected")
            );

        public OverlayId Id => OverlayId.CorralBreed;

        public OverlayResult Handler() {
            return CorralBreedScript.corralBreedInterfaceOpen
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            builder.CaptureInput();

            List<TamedCorralMonster> monsters = MetaProgressScript.localTamedMonstersForThisSlot;
            int count = monsters?.Count ?? 0;

            builder.StartRow("header");
            builder.AddLabel(ControlId.Structural("breed:header"), StatusLabel);
            builder.EndRow();

            for (int i = 0; i < count; i++) {
                int index = i;
                TamedCorralMonster tcm = monsters[index];
                int id = tcm.monsterID;

                builder.StartRow("mon:" + id);
                builder.AddItem(
                    ControlId.Structural("breed:mon:" + id),
                    new NodeVtable {
                        Label = ctx => MonsterLabel(ctx.Message, tcm),
                        OnClick = (ctx, mods) => Toggle(ctx, index, tcm),
                        OnReadInfo = ctx => ctx.Message.Fragment(GameLabelReader.Clean(tcm.GetBattlePowerStats())),
                    }
                );
                builder.EndRow();
            }

            builder.StartRow("breed");
            builder.AddClickable(
                ControlId.Structural("breed:confirm"),
                ctx => ctx.Message.Fragment(ModStrings.BreedAction),
                Confirm
            );
            builder.EndRow();
        }

        // The header anchor: the selection count, and — once two are chosen — the same mutual-feelings
        // and willingness verdict the game shows, using the game's own strings.
        private static void StatusLabel(OverlayCtx ctx) {
            ctx.Message.Fragment(ModStrings.BreedHeader);

            List<Monster> sel = Selected();
            int n = sel?.Count ?? 0;
            ctx.Message.ListItem(ModStrings.BreedSelectionCount(n));

            if (n < 2) {
                ctx.Message.ListItem(ModStrings.BreedSelectTwo);
                return;
            }

            TamedCorralMonster a = sel[0].tamedMonsterStuff;
            TamedCorralMonster b = sel[1].tamedMonsterStuff;

            ctx.Message.ListItem(GameLabelReader.Clean(sel[0].displayName));
            ctx.Message.Fragment(Feelings(sel[1].displayName));
            ctx.Message.Fragment(GameLabelReader.Clean(a.GetRelationshipString(b)));
            ctx.Message.ListItem(GameLabelReader.Clean(sel[1].displayName));
            ctx.Message.Fragment(Feelings(sel[0].displayName));
            ctx.Message.Fragment(GameLabelReader.Clean(b.GetRelationshipString(a)));

            int ab = a.TryGetRelationshipAmount(b);
            int ba = b.TryGetRelationshipAmount(a);
            string verdict = (ab < 0 || ba < 0)
                ? "ui_monsterbreed_1"
                : (ab < 7 || ba < 7) ? "ui_monsterbreed_2" : "ui_monsterbreed_3";
            ctx.Message.ListItem(GameLabelReader.Clean(StringManager.GetString(verdict)));
        }

        private static void MonsterLabel(MessageBuilder message, TamedCorralMonster tcm) {
            message.Fragment(GameLabelReader.Clean(tcm.monsterObject.displayName));
            if (IsSelected(tcm)) {
                message.Fragment(ModStrings.BreedSelected);
            }

            message.ListItem("level " + tcm.monsterObject.myStats.GetLevel());
            message.ListItem(ModStrings.Happiness);
            message.Fragment(GameLabelReader.Clean(tcm.GetHappinessString()));
            message.ListItem(GameLabelReader.Clean(tcm.GetRarityString()));
        }

        // The game's "feelings toward X" label is a tag-templated string (slot 0 = the target's name);
        // set the tag before resolving it so each direction reads the correct target, not a stale one.
        private static string Feelings(string targetName) {
            StringManager.SetTag(0, targetName);
            return GameLabelReader.Clean(StringManager.GetString("ui_corral_feelings"));
        }

        private static bool IsSelected(TamedCorralMonster tcm) {
            List<Monster> sel = Selected();
            return sel != null && sel.Contains(tcm.monsterObject);
        }

        // Toggle this monster's selection through the game's own handler, then report the resulting
        // state. The game plays an Error cue itself when a third pick is refused.
        private static void Toggle(OverlayCtx ctx, int index, TamedCorralMonster tcm) {
            bool wasSelected = IsSelected(tcm);
            CorralBreedScript.singleton.SelectMonsterForBreeding(index);
            bool nowSelected = IsSelected(tcm);

            if (nowSelected != wasSelected) {
                ctx.Message.Fragment(nowSelected ? ModStrings.BreedSelected : ModStrings.BreedDeselected);
            }
        }

        private static void Confirm(OverlayCtx ctx, Modifiers mods) {
            List<Monster> sel = Selected();
            if ((sel?.Count ?? 0) < 2) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.BreedNeedTwo);
                return;
            }

            // Hands off to the game: opens the JP-spend slider dialog (when both are willing) or runs
            // the breed routine. Either way the breed screen closes; the dialog overlay takes it from
            // here, so we add no message.
            CorralBreedScript.singleton.ConfirmMonstersForBreeding(0);
        }
    }
}
