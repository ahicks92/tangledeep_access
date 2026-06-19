using System.Collections.Generic;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The consumables inventory screen (<c>Switch_UIInventoryScreen</c>, the "I" tab). The game
    /// models it as a scrolling 16-button window over a longer list plus side panels of filter and
    /// sort buttons; we ignore that topology and re-present it as one owned grid, built fresh every
    /// tick:
    ///
    /// <list type="bullet">
    /// <item>a <b>stats</b> row — character summary then each resource;</item>
    /// <item>a <b>sort</b> row — an anchor reading the current sort and item count, then a sort
    /// button per sort type;</item>
    /// <item>one <b>item</b> row per consumable — the item (confirm reads its full tooltip) then
    /// its action cells.</item>
    /// </list>
    ///
    /// <para><b>Data source:</b> the screen's own <c>itemColumn.listHeldObjects</c> — the complete
    /// filtered+sorted list (not the 16-wide visible window), so we read every item without
    /// scrolling. Read live each build; never cached.</para>
    ///
    /// <para><b>Navigation:</b> rows do NOT share keys, so up/down always lands on the next row's
    /// first cell — the item — announcing it; left/right steps through that item's actions. We do
    /// not share row keys for column nav because the builder cannot yet label transitions, so a
    /// column of identical action cells would read "drop, drop, drop" with no item context. Action
    /// cells are still keyed by the item's <c>actorUniqueID</c> (the builder rejects duplicate ids).</para>
    ///
    /// <para><b>Scope:</b> stats and item identity/info read for real; favorite and trash work
    /// (toggle on the cell via Enter, or row-wide via the F / Minus keys). The sort buttons and the
    /// use/eat and drop action cells are still labeled stubs that announce "not yet implemented".</para>
    /// </summary>
    internal sealed class InventoryOverlay : IUiOverlay {
        // The complete filtered+sorted list backing the column; private on the column type.
        private static readonly AccessTools.FieldRef<Switch_UIButtonColumn, List<ISelectableUIObject>>
            HeldObjects = AccessTools.FieldRefAccess<Switch_UIButtonColumn, List<ISelectableUIObject>>(
                "listHeldObjects"
            );

        public OverlayId Id => OverlayId.Inventory;

        public OverlayResult Handler() {
            return Screen() != null ? OverlayResult.Active(this) : OverlayResult.Inactive;
        }

        /// <summary>The live inventory screen if it is the open full-screen UI, else null.</summary>
        private static Switch_UIInventoryScreen Screen() {
            UIManagerScript ums = UIManagerScript.singletonUIMS;
            if (ums == null || !(ums.currentFullScreenUI is Switch_UIInventoryScreen inv)) {
                return null;
            }

            return inv.gameObject.activeInHierarchy ? inv : null;
        }

        public void Build(IOverlayBuilder builder) {
            builder.CaptureInput();

            Switch_UIInventoryScreen screen = Screen();
            if (screen == null) {
                return;
            }

            List<ISelectableUIObject> items =
                screen.itemColumn != null ? HeldObjects(screen.itemColumn) : null;

            BuildStatsRow(builder);
            BuildSortRow(builder, items);
            BuildItemRows(builder, items);
        }

        // --- Stats row -------------------------------------------------------------------------

        private static void BuildStatsRow(IOverlayBuilder builder) {
            HeroPC hero = GameMasterScript.heroPCActor;
            StatBlock stats = hero.myStats;

            builder.StartRow("stats");

            builder.AddLabel(
                ControlId.Structural("inv:stat:hero"),
                ctx => ctx.Message
                    .Fragment(GameLabelReader.Clean(hero.displayName))
                    .ListItem("level " + stats.GetLevel())
            );
            builder.AddLabel(ControlId.Structural("inv:stat:hp"), ctx => Bar(ctx.Message, stats, StatTypes.HEALTH, "health"));
            builder.AddLabel(ControlId.Structural("inv:stat:stamina"), ctx => Bar(ctx.Message, stats, StatTypes.STAMINA, "stamina"));
            builder.AddLabel(ControlId.Structural("inv:stat:energy"), ctx => Bar(ctx.Message, stats, StatTypes.ENERGY, "energy"));
            builder.AddLabel(ControlId.Structural("inv:stat:gold"), ctx => ctx.Message.Fragment(hero.GetMoney() + " gold"));
            builder.AddLabel(ControlId.Structural("inv:stat:jp"), ctx => ctx.Message.Fragment((int)hero.GetCurJP() + " job points"));
            builder.AddLabel(
                ControlId.Structural("inv:stat:xp"),
                ctx => ctx.Message.PushFraction(stats.GetXP(), stats.GetXPToNextLevel(), "experience to next level")
            );

            builder.EndRow();
        }

        private static void Bar(MessageBuilder message, StatBlock stats, StatTypes stat, string unit) {
            int cur = (int)stats.GetStat(stat, StatDataTypes.CUR);
            int max = (int)stats.GetStat(stat, StatDataTypes.MAX);
            message.PushFraction(cur, max, unit);
        }

        // --- Sort row --------------------------------------------------------------------------

        private static void BuildSortRow(IOverlayBuilder builder, List<ISelectableUIObject> items) {
            int count = items?.Count ?? 0;

            builder.StartRow("sort");

            builder.AddLabel(
                ControlId.Structural("inv:sort:anchor"),
                ctx => {
                    ctx.Message.Fragment("Inventory");
                    ctx.Message.ListItem(count + (count == 1 ? " item" : " items"));
                    ctx.Message.ListItem("sorted by " + SortName(Switch_UIInventoryScreen.lastSortType));
                    if (!Switch_UIInventoryScreen.lastSortForward) {
                        ctx.Message.Fragment("reversed");
                    }
                }
            );

            AddSortStub(builder, "type", InventorySortTypes.ITEMTYPE);
            AddSortStub(builder, "name", InventorySortTypes.ALPHA);
            AddSortStub(builder, "value", InventorySortTypes.VALUE);
            AddSortStub(builder, "rank", InventorySortTypes.RANK);
            AddSortStub(builder, "rarity", InventorySortTypes.RARITY);

            builder.EndRow();
        }

        private static void AddSortStub(IOverlayBuilder builder, string word, InventorySortTypes sort) {
            builder.AddClickable(
                ControlId.Structural("inv:sortbtn:" + sort),
                ctx => ctx.Message.Fragment("sort by " + word),
                (ctx, mods) => ctx.Message.Fragment("sort by " + word + ", not yet implemented")
            );
        }

        private static string SortName(InventorySortTypes sort) {
            switch (sort) {
                case InventorySortTypes.ALPHA:
                    return "name";
                case InventorySortTypes.RARITY:
                    return "rarity";
                case InventorySortTypes.VALUE:
                    return "value";
                case InventorySortTypes.ITEMTYPE:
                    return "type";
                case InventorySortTypes.RANK:
                    return "rank";
                case InventorySortTypes.CONSUMABLETYPE:
                    return "category";
                default:
                    return sort.ToString().ToLowerInvariant();
            }
        }

        // --- Item rows -------------------------------------------------------------------------

        private static void BuildItemRows(IOverlayBuilder builder, List<ISelectableUIObject> items) {
            if (items == null || items.Count == 0) {
                builder.AddLabel(
                    ControlId.Structural("inv:empty"),
                    ctx => ctx.Message.Fragment("No items")
                );
                return;
            }

            foreach (ISelectableUIObject selectable in items) {
                if (!(selectable is Item item)) {
                    continue;
                }

                int uid = item.actorUniqueID;
                string primary = item.IsItemFood() ? "eat" : "use";
                // A distinct row key per item so up/down never preserves a column (see class docs);
                // it just has to be non-equal to its neighbours' keys.
                builder.StartRow("item:" + uid);

                AddRowCell(
                    builder,
                    ControlId.Structural("inv:item:" + uid),
                    item,
                    new NodeVtable {
                        Label = ctx => ItemSummary(ctx.Message, item),
                        // Confirm is the primary action players expect on an item (use / eat);
                        // stubbed until item actions are wired.
                        OnClick = (ctx, mods) => ctx.Message.Fragment(primary + ", not yet implemented"),
                        // Read-info (K) is the tooltip. NOTE: GetInformationForTooltip /
                        // GetItemInformationNoName has a WRITE side effect — it clears the item's
                        // newlyPickedUp ("new") flag, which is saved state. That is precisely the
                        // chosen "inspected" moment (option B), so it belongs here; never call it
                        // speculatively (e.g. to pre-build a label) or "new" clears unseen.
                        OnReadInfo = ctx =>
                            ctx.Message.Fragment(GameLabelReader.Clean(item.GetInformationForTooltip())),
                    }
                );

                AddStubCell(builder, "inv:action:" + primary + ":" + uid, item, primary);
                AddStubCell(builder, "inv:action:drop:" + uid, item, "drop");
                AddRowCell(
                    builder,
                    ControlId.Structural("inv:action:fav:" + uid),
                    item,
                    new NodeVtable {
                        Label = ctx => ctx.Message.Fragment(item.favorite ? "unfavorite" : "favorite"),
                        OnClick = (ctx, mods) => ToggleFavorite(ctx, item),
                    }
                );
                AddRowCell(
                    builder,
                    ControlId.Structural("inv:action:trash:" + uid),
                    item,
                    new NodeVtable {
                        Label = ctx => ctx.Message.Fragment(item.vendorTrash ? "untrash" : "trash"),
                        OnClick = (ctx, mods) => ToggleTrash(ctx, item),
                    }
                );

                builder.EndRow();
            }
        }

        private static void ItemSummary(MessageBuilder message, Item item) {
            if (item.favorite) {
                message.Fragment("favorite");
            }

            if (item.vendorTrash) {
                message.Fragment("trash");
            }

            if (item.newlyPickedUp) {
                message.Fragment("new");
            }

            message.Fragment(GameLabelReader.Clean(item.displayName));
            message.PushQuantity(item.GetQuantity());
        }

        // Add a cell to the current item row, attaching the row-wide favorite/trash key handlers so
        // the F / Minus keys act on this item from ANY cell in its row (item, use, drop, …), not
        // just the favorite/trash cells. Confirm still runs each cell's own OnClick.
        private static void AddRowCell(IOverlayBuilder builder, ControlId id, Item item, NodeVtable vtable) {
            vtable.OnMarkFavorite = ctx => ToggleFavorite(ctx, item);
            vtable.OnMarkTrash = ctx => ToggleTrash(ctx, item);
            builder.AddItem(id, vtable);
        }

        // A not-yet-implemented action cell (use/eat, drop) — still row-wide markable.
        private static void AddStubCell(IOverlayBuilder builder, string key, Item item, string verb) {
            AddRowCell(
                builder,
                ControlId.Structural(key),
                item,
                new NodeVtable {
                    Label = ctx => ctx.Message.Fragment(verb),
                    OnClick = (ctx, mods) => ctx.Message.Fragment(verb + ", not yet implemented"),
                }
            );
        }

        // Favorite and trash are mutually exclusive flags (setting one clears the other), and these
        // are TOGGLES — F/Enter flips the state both ways, which blind players expect even though the
        // game calls the action "mark". We flip the flag directly rather than via the game's
        // MarkItemFavorite/MarkItemTrash, which look up the on-screen button and early-return —
        // skipping the mutual-exclusion clear — for any item outside the game's 16-wide visible
        // window. No re-sort on toggle: marking several items in a row keeps a stable list (favorites
        // reorder to the top only on the next manual sort / reopen).
        private static void ToggleFavorite(OverlayCtx ctx, Item item) {
            item.favorite = !item.favorite;
            if (item.favorite) {
                item.vendorTrash = false;
                UIManagerScript.PlayCursorSound("GetSparkle");
                ctx.Message.Fragment("favorited");
            } else {
                UIManagerScript.PlayCursorSound("UITock");
                ctx.Message.Fragment("no longer favorite");
            }
        }

        private static void ToggleTrash(OverlayCtx ctx, Item item) {
            item.vendorTrash = !item.vendorTrash;
            if (item.vendorTrash) {
                item.favorite = false;
                UIManagerScript.PlayCursorSound("UITick");
                ctx.Message.Fragment("marked as trash");
            } else {
                UIManagerScript.PlayCursorSound("UITock");
                ctx.Message.Fragment("no longer trash");
            }
        }
    }
}
