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
    /// <item>a <b>sort</b> row — an anchor reading the current sort and item count, then a sort
    /// button per sort type;</item>
    /// <item>one <b>item</b> row per consumable — the item (confirm reads its full tooltip) then
    /// its action cells.</item>
    /// </list>
    ///
    /// <para>The hero's HP/resources/JP/XP/gold are deliberately NOT here: they are persistent
    /// PlayerHUD chrome shown on every screen, not part of the inventory's own UI.</para>
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
    /// <para><b>Scope:</b> stats and item identity/info, favorite/trash (Enter on the cell or the
    /// row-wide F / Minus keys), sort buttons, use/eat, and single-item drop all work. Two gaps
    /// remain: dropping part of a <i>stack</i> needs the quantity-slider dialog we do not handle yet
    /// (gated with a spoken notice), and using a <i>targeted</i> item hands off to the game's
    /// targeting, which is only partially narrated until the targeting controller is built.</para>
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
            if (ums == null) {
                return null;
            }

            // Targeting takes precedence: using a targeted item enters targeting synchronously while
            // the inventory's close is still a pending fade, so currentFullScreenUI briefly remains
            // this screen. Standing down here releases input to the game's targeting instead of
            // fighting it for the arrow keys during that window.
            if (ums.CheckTargeting()) {
                return null;
            }

            if (!(ums.currentFullScreenUI is Switch_UIInventoryScreen inv)) {
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

            BuildSortRow(builder, items);
            BuildItemRows(builder, items);
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

            AddSortButton(builder, "type", InventorySortTypes.ITEMTYPE);
            AddSortButton(builder, "name", InventorySortTypes.ALPHA);
            AddSortButton(builder, "value", InventorySortTypes.VALUE);
            AddSortButton(builder, "rank", InventorySortTypes.RANK);
            AddSortButton(builder, "rarity", InventorySortTypes.RARITY);

            builder.EndRow();
        }

        // Confirm runs the game's own SortPlayerInventory — same call as the game's sort button:
        // it sorts the bag, plays the cue, flips direction when re-pressing the active sort, and
        // refreshes the column (so our next rebuild reads the new order). We speak the resulting
        // sort state. The cursor stays on the button (stable key).
        private static void AddSortButton(IOverlayBuilder builder, string word, InventorySortTypes sort) {
            builder.AddClickable(
                ControlId.Structural("inv:sortbtn:" + sort),
                ctx => ctx.Message.Fragment("sort by " + word),
                (ctx, mods) => {
                    UIManagerScript.singletonUIMS.SortPlayerInventory((int)sort);
                    ctx.Message.Fragment("sorted by " + SortName(Switch_UIInventoryScreen.lastSortType));
                    if (!Switch_UIInventoryScreen.lastSortForward) {
                        ctx.Message.Fragment("reversed");
                    }
                }
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
                        // Confirm is the primary action players expect on an item (use / eat).
                        OnClick = (ctx, mods) => UseItem(ctx, item),
                        // Read-info (K) is the tooltip. NOTE: GetInformationForTooltip /
                        // GetItemInformationNoName has a WRITE side effect — it clears the item's
                        // newlyPickedUp ("new") flag, which is saved state. That is precisely the
                        // chosen "inspected" moment (option B), so it belongs here; never call it
                        // speculatively (e.g. to pre-build a label) or "new" clears unseen.
                        OnReadInfo = ctx =>
                            ctx.Message.Fragment(GameLabelReader.Clean(item.GetInformationForTooltip())),
                    }
                );

                AddRowCell(
                    builder,
                    ControlId.Structural("inv:action:" + primary + ":" + uid),
                    item,
                    new NodeVtable {
                        Label = ctx => ctx.Message.Fragment(primary),
                        OnClick = (ctx, mods) => UseItem(ctx, item),
                    }
                );
                AddRowCell(
                    builder,
                    ControlId.Structural("inv:action:drop:" + uid),
                    item,
                    new NodeVtable {
                        Label = ctx => ctx.Message.Fragment("drop"),
                        OnClick = (ctx, mods) => DropItem(ctx, item),
                    }
                );
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

        // Use (or eat) the item. Mirrors the game's submenu gating: food checks food-full, other
        // consumables must be usable (valuables aren't). Then closes the inventory and runs the
        // game's own use path — for a TARGETED item this enters targeting (a one-way exit from the
        // inventory, matching vanilla; cancel returns to the map, not here). We speak nothing on
        // success: the effect's own log lines and any targeting narration carry the feedback.
        private static void UseItem(OverlayCtx ctx, Item item) {
            if (item.IsItemFood()) {
                if (GameMasterScript.heroPCActor.myStats.CheckHasStatusName("status_foodfull")) {
                    UIManagerScript.PlayCursorSound("Error");
                    ctx.Message.Fragment("too full to eat");
                    return;
                }
            } else if (!item.CanBeUsed()) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment("can't use that");
                return;
            }

            UIManagerScript.ForceCloseFullScreenUI();
            GameMasterScript.gmsSingleton.PlayerUseConsumable(item as Consumable);
        }

        // Drop the item. A single item drops to the hero's tile and leaves the list (we stay in the
        // inventory and rebuild). A stack would open the game's quantity-slider dialog, which we do
        // not handle yet, so splitting a stack is gated until that framework piece lands.
        private static void DropItem(OverlayCtx ctx, Item item) {
            if (item is Consumable consumable && consumable.Quantity > 1) {
                UIManagerScript.PlayCursorSound("Error");
                ctx.Message.Fragment("can't drop part of a stack yet");
                return;
            }

            string name = GameLabelReader.Clean(item.displayName);
            UIManagerScript.DropItemFromSheet(item);
            // DropItemFromSheet removes from the bag but does not refresh the screen's cached
            // item list (the game's own drop path follows it with this). We read that cache, so
            // without the refresh the dropped item lingers in our list.
            UIManagerScript.UpdateFullScreenUIContent();
            ctx.Message.Fragment("dropped").Fragment(name);
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
