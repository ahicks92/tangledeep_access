using System.Collections.Generic;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;

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
    /// <para><b>Scope (first cut):</b> stats and item identity/info read for real; the sort buttons
    /// and per-item action buttons (use/eat, drop, favorite, trash) are labeled stubs that announce
    /// "not yet implemented" on activation. Wiring them to the game's own methods is the next pass.</para>
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
                // A distinct row key per item so up/down never preserves a column (see class docs);
                // it just has to be non-equal to its neighbours' keys.
                builder.StartRow("item:" + uid);

                builder.AddClickable(
                    ControlId.Referenced(item, "inv:item:" + uid),
                    ctx => ItemSummary(ctx.Message, item),
                    (ctx, mods) => ctx.Message.Fragment(GameLabelReader.Clean(item.GetInformationForTooltip()))
                );

                AddItemActionStub(builder, uid, item.IsItemFood() ? "eat" : "use");
                AddItemActionStub(builder, uid, "drop");
                AddItemActionStub(builder, uid, item.favorite ? "unfavorite" : "favorite");
                AddItemActionStub(builder, uid, item.vendorTrash ? "untrash" : "trash");

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

            int qty = item.GetQuantity();
            if (qty > 1) {
                message.ListItem("quantity " + qty);
            }
        }

        private static void AddItemActionStub(IOverlayBuilder builder, int uid, string verb) {
            builder.AddClickable(
                ControlId.Structural("inv:action:" + verb + ":" + uid),
                ctx => ctx.Message.Fragment(verb),
                (ctx, mods) => ctx.Message.Fragment(verb + ", not yet implemented")
            );
        }
    }
}
