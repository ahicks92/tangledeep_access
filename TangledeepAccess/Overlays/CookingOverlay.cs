using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;
using UIObject = UIManagerScript.UIObject;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The town cooking station (the game's <c>UITabs.COOKING</c> window, opened from the chef's
    /// "cooking" conversation — a windowed UI under the PlayerHUD, not a <c>currentFullScreenUI</c>).
    /// It is a little crafting workbench: pick from your bag's cookable ingredients and seasonings to
    /// fill a pan (up to three ingredients + one seasoning), then cook — the game evaluates the
    /// combination into a dish (or Tangledeep Stew on a miss). There is deliberately no preview; you
    /// cook to find out, same as the sighted UI.
    ///
    /// <para>We ignore the game's drag-and-drop pan layout and re-present it as one owned grid, built
    /// fresh every tick:
    /// <list type="bullet">
    /// <item>a <b>pan</b> row — the three ingredient slots, the seasoning slot, and the last dish;
    /// confirm on a filled slot removes it;</item>
    /// <item>an <b>ingredients</b> row and a <b>seasonings</b> row — one cell per available item
    /// (name + quantity); confirm adds it to the pan, <c>k</c> reads its tooltip;</item>
    /// <item>an <b>actions</b> row — cook, clear pan, repeat last, exit.</item>
    /// </list></para>
    ///
    /// <para><b>Data source:</b> the game's own live cooking state — the pan (<c>cookingIngredientItems</c>
    /// / <c>cookingSeasoningItem</c>), the available pools (<c>cookingPlayerIngredientList</c> with
    /// <c>ingredientQuantities</c>, and <c>cookingPlayerSeasoningList</c>), and the result
    /// (<c>cookingResultItem</c>). All are public static, so no reflection. Every action drives the
    /// game's own handler (<c>SelectCookingIngredient</c>, <c>DragCookingItem</c>, <c>CookItems</c>, …),
    /// which keeps the pools refreshed; we read them live each build, never cached.</para>
    /// </summary>
    internal sealed class CookingOverlay : IUiOverlay {
        public OverlayId Id => OverlayId.Cooking;

        public OverlayResult Handler() {
            // Defensive: yield to a dialog if one is somehow up (the cooking UI closes dialogs on open).
            if (UIManagerScript.dialogBoxOpen) {
                return OverlayResult.Inactive;
            }

            return Open() ? OverlayResult.Active(this) : OverlayResult.Inactive;
        }

        private static bool Open() {
            if (UIManagerScript.singletonUIMS == null || !UIManagerScript.GetWindowState(UITabs.COOKING)) {
                return false;
            }

            return UIManagerScript.cookingUI != null && UIManagerScript.cookingUI.activeInHierarchy;
        }

        public void Build(IOverlayBuilder builder) {
            builder.CaptureInput();

            if (!Open()) {
                return;
            }

            BuildPanRow(builder);
            BuildIngredientsRow(builder);
            BuildSeasoningsRow(builder);
            BuildActionsRow(builder);
        }

        // --- Pan row ---------------------------------------------------------------------------

        private static void BuildPanRow(IOverlayBuilder builder) {
            builder.StartRow("pan");
            builder.AddLabel(
                ControlId.Structural("cook:pan:anchor"),
                ctx => ctx.Message.Fragment(ModStrings.CookPanRow)
            );

            // The three ingredient slots. Confirm on a filled slot pulls it back out (DragCookingItem
            // doubles as a clear for pan-slot codes 200-202).
            for (int i = 0; i < UIManagerScript.cookingIngredientItems.Length; i++) {
                int slot = i;
                builder.AddItem(
                    ControlId.Structural("cook:pan:ing:" + i),
                    new NodeVtable {
                        Label = ctx => {
                            ctx.Message.Fragment(ModStrings.CookIngredientSlot(slot + 1));
                            Item item = UIManagerScript.cookingIngredientItems[slot];
                            ctx.Message.Fragment(item == null
                                ? ModStrings.EmptySlot
                                : GameLabelReader.Clean(item.displayName));
                        },
                        OnClick = (ctx, mods) => RemovePanIngredient(ctx, slot),
                    }
                );
            }

            // The single seasoning slot (clear code 203).
            builder.AddItem(
                ControlId.Structural("cook:pan:seasoning"),
                new NodeVtable {
                    Label = ctx => {
                        ctx.Message.Fragment(ModStrings.CookSeasoningSlot);
                        Item item = UIManagerScript.cookingSeasoningItem;
                        ctx.Message.Fragment(item == null
                            ? ModStrings.EmptySlot
                            : GameLabelReader.Clean(item.displayName));
                    },
                    OnClick = (ctx, mods) => RemovePanSeasoning(ctx),
                }
            );

            // The last cooked dish (set after a cook). Read-only; k reads its full tooltip.
            builder.AddItem(
                ControlId.Structural("cook:result"),
                new NodeVtable {
                    Label = ctx => {
                        ctx.Message.Fragment(ModStrings.CookResult);
                        Item item = UIManagerScript.cookingResultItem;
                        ctx.Message.Fragment(item == null
                            ? ModStrings.CookNoDish
                            : GameLabelReader.Clean(item.displayName));
                    },
                    OnReadInfo = ctx => {
                        Item item = UIManagerScript.cookingResultItem;
                        if (item != null) {
                            ctx.Message.Fragment(GameLabelReader.Clean(item.GetInformationForTooltip()));
                        }
                    },
                }
            );

            builder.EndRow();
        }

        private static void RemovePanIngredient(OverlayCtx ctx, int slot) {
            Item item = UIManagerScript.cookingIngredientItems[slot];
            if (item == null) {
                return;
            }

            string name = GameLabelReader.Clean(item.displayName);
            UIManagerScript.singletonUIMS.DragCookingItem(200 + slot);
            ctx.Message.Fragment(ModStrings.RemovedFromPan).Fragment(name);
        }

        private static void RemovePanSeasoning(OverlayCtx ctx) {
            Item item = UIManagerScript.cookingSeasoningItem;
            if (item == null) {
                return;
            }

            string name = GameLabelReader.Clean(item.displayName);
            UIManagerScript.singletonUIMS.DragCookingItem(203);
            ctx.Message.Fragment(ModStrings.RemovedFromPan).Fragment(name);
        }

        // --- Ingredient / seasoning pools ------------------------------------------------------

        private static void BuildIngredientsRow(IOverlayBuilder builder) {
            builder.StartRow("ingredients");
            builder.AddLabel(
                ControlId.Structural("cook:ing:anchor"),
                ctx => ctx.Message.Fragment(ModStrings.CookIngredientsRow)
            );

            Item[] pool = UIManagerScript.cookingPlayerIngredientList;
            int[] quantities = UIManagerScript.ingredientQuantities;
            bool any = false;

            if (pool != null) {
                for (int i = 0; i < pool.Length; i++) {
                    Item item = pool[i];
                    if (item == null) {
                        continue;
                    }

                    any = true;
                    int index = i;
                    int qty = quantities != null && i < quantities.Length ? quantities[i] : 1;
                    builder.AddItem(
                        ControlId.Structural("cook:ing:" + i),
                        new NodeVtable {
                            Label = ctx => {
                                ctx.Message.Fragment(GameLabelReader.Clean(item.displayName));
                                ctx.Message.PushQuantity(qty);
                            },
                            // Confirm adds to the first empty pan ingredient slot.
                            OnClick = (ctx, mods) => AddIngredient(ctx, index, item),
                            OnReadInfo = ctx =>
                                ctx.Message.Fragment(GameLabelReader.Clean(item.GetInformationForTooltip())),
                        }
                    );
                }
            }

            if (!any) {
                builder.AddLabel(
                    ControlId.Structural("cook:ing:none"),
                    ctx => ctx.Message.Fragment(ModStrings.NoCookIngredients)
                );
            }

            builder.EndRow();
        }

        private static void BuildSeasoningsRow(IOverlayBuilder builder) {
            builder.StartRow("seasonings");
            builder.AddLabel(
                ControlId.Structural("cook:seas:anchor"),
                ctx => ctx.Message.Fragment(ModStrings.CookSeasoningsRow)
            );

            Item[] pool = UIManagerScript.cookingPlayerSeasoningList;
            bool any = false;

            if (pool != null) {
                for (int j = 0; j < pool.Length; j++) {
                    Item item = pool[j];
                    if (item == null) {
                        continue;
                    }

                    any = true;
                    int index = j;
                    builder.AddItem(
                        ControlId.Structural("cook:seas:" + j),
                        new NodeVtable {
                            Label = ctx => {
                                ctx.Message.Fragment(GameLabelReader.Clean(item.displayName));
                                ctx.Message.PushQuantity(SeasoningQuantity(item));
                            },
                            // Confirm sets the pan's seasoning slot (replacing any previous).
                            OnClick = (ctx, mods) => AddSeasoning(ctx, index, item),
                            OnReadInfo = ctx =>
                                ctx.Message.Fragment(GameLabelReader.Clean(item.GetInformationForTooltip())),
                        }
                    );
                }
            }

            if (!any) {
                builder.AddLabel(
                    ControlId.Structural("cook:seas:none"),
                    ctx => ctx.Message.Fragment(ModStrings.NoSeasonings)
                );
            }

            builder.EndRow();
        }

        // The pool excludes the in-pan seasoning from its displayed count, mirroring the game's own
        // UpdateCookingPlayerLists; there is no public seasoning-quantity array, so compute it.
        private static int SeasoningQuantity(Item item) {
            int qty = (item as Consumable)?.Quantity ?? 1;
            if (UIManagerScript.cookingSeasoningItem == item) {
                qty--;
            }

            return qty;
        }

        private static void AddIngredient(OverlayCtx ctx, int index, Item item) {
            int before = PanIngredientCount();
            UIManagerScript.singletonUIMS.SelectCookingIngredient(index);
            if (PanIngredientCount() > before) {
                ctx.Message.Fragment(GameLabelReader.Clean(item.displayName)).Fragment(ModStrings.AddedToPan);
            } else {
                // SelectCookingIngredient already played the error cue when the pan was full.
                ctx.Message.Fragment(ModStrings.PanFull);
            }
        }

        private static void AddSeasoning(OverlayCtx ctx, int index, Item item) {
            UIManagerScript.singletonUIMS.SelectCookingIngredient(100 + index);
            ctx.Message.Fragment(GameLabelReader.Clean(item.displayName)).Fragment(ModStrings.SetAsSeasoning);
        }

        // --- Actions ---------------------------------------------------------------------------

        private static void BuildActionsRow(IOverlayBuilder builder) {
            builder.StartRow("actions");

            AddAction(builder, "cook", UIManagerScript.cookButton, ModStrings.CookAction, Cook);
            AddAction(builder, "reset", UIManagerScript.cookingReset, ModStrings.ClearPanAction, ClearPan);
            AddAction(builder, "repeat", UIManagerScript.cookingRepeat, ModStrings.RepeatMealAction, RepeatLast);
            AddAction(builder, "exit", UIManagerScript.cookingExit, ModStrings.ExitCookingAction, Exit);

            builder.EndRow();
        }

        // An action button: prefer the game's own button caption, fall back to our label.
        private static void AddAction(
            IOverlayBuilder builder,
            string idPart,
            UIObject button,
            string fallback,
            System.Action<OverlayCtx> onClick
        ) {
            string label = GameLabelReader.ReadLabel(button);
            if (string.IsNullOrEmpty(label)) {
                label = fallback;
            }

            builder.AddItem(
                ControlId.Structural("cook:act:" + idPart),
                new NodeVtable {
                    Label = ctx => ctx.Message.Fragment(label),
                    OnClick = (ctx, mods) => onClick(ctx),
                }
            );
        }

        private static void Cook(OverlayCtx ctx) {
            if (PanIngredientCount() < 2) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment(ModStrings.NeedTwoIngredients);
                return;
            }

            // CookItems evaluates the pan, adds the dish to the bag, clears the pan, and plays its own
            // success/failure cue. It writes no game-log line, so we announce the result ourselves.
            UIManagerScript.singletonUIMS.CookItems();
            Item dish = UIManagerScript.cookingResultItem;
            if (dish != null) {
                ctx.Message.Fragment(ModStrings.Cooked).Fragment(GameLabelReader.Clean(dish.displayName));
            }
        }

        private static void ClearPan(OverlayCtx ctx) {
            UIManagerScript.singletonUIMS.ResetCookingFromInterface();
            ctx.Message.Fragment(ModStrings.PanCleared);
        }

        private static void RepeatLast(OverlayCtx ctx) {
            int before = PanIngredientCount();
            UIManagerScript.singletonUIMS.RepeatLastRecipeFromInterface();
            ctx.Message.Fragment(PanIngredientCount() > before ? ModStrings.RepeatedMeal : ModStrings.CantRepeatMeal);
        }

        private static void Exit(OverlayCtx ctx) {
            UIManagerScript.CloseCookingInterface();
        }

        private static int PanIngredientCount() {
            int count = 0;
            foreach (Item item in UIManagerScript.cookingIngredientItems) {
                if (item != null) {
                    count++;
                }
            }

            return count;
        }
    }
}
