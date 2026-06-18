using System.Collections.Generic;
using HarmonyLib;
using TangledeepAccess.Controls;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Computes spoken answers to the player's on-demand spatial queries during gameplay:
    /// "read here" (the hero's tile) and "scan" (a Factorio-Access-style sweep of everything in
    /// view, by direction and distance). All reads re-query live game state at call time — no
    /// caching — and respect the hero's line of sight via <c>visibleTilesArray</c>. Runs on the
    /// Unity main thread from the per-frame pump; the input hook only requests the command.
    /// </summary>
    internal static class GameplayReader {
        /// <summary>
        /// Realize a free-play input action (the Look and Gameplay contexts), or null if not in
        /// play. <see cref="ModInputKind.Move"/> here means a look-cursor step — the menu context's
        /// Move never reaches us. <see cref="ModInputKind.RepeatLast"/> is handled in the pump (it
        /// owns the speech instance) and never arrives here.
        /// </summary>
        public static string Execute(ModInputAction action) {
            // Help is static text and useful even mid-transition, so answer it before the
            // in-play gate.
            if (action.Kind == ModInputKind.Help) {
                return "Tangledeep Access commands. K, read here and exits. "
                    + "L, scan in view. Y, status. A, hotbar. Semicolon, look cursor; "
                    + "then arrows or numpad to move it, brackets to jump between things in view, "
                    + "Home to recenter. Apostrophe, repeat. Slash, this help.";
            }

            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null) {
                return null;
            }

            switch (action.Kind) {
                case ModInputKind.LookToggle:
                    return LookCursor.Toggle();
                case ModInputKind.LookRecenter:
                    return LookCursor.Recenter();
                case ModInputKind.Move:
                    return LookCursor.Move(action.Dx, action.Dy);
                case ModInputKind.LookNextPoi:
                    return LookCursor.JumpToPoi(1);
                case ModInputKind.LookPrevPoi:
                    return LookCursor.JumpToPoi(-1);
            }

            var message = new MessageBuilder();
            switch (action.Kind) {
                case ModInputKind.ReadHere:
                    ReadHere(message, hero);
                    break;
                case ModInputKind.Scan:
                    Scan(message, hero);
                    break;
                case ModInputKind.ReadStatus:
                    ReadStatus(message, hero);
                    break;
                case ModInputKind.ReadHotbar:
                    ReadHotbar(message);
                    break;
            }

            return message.Build();
        }

        // --- Hotbar ---

        // The active hotbar page index is a private static on UIManagerScript; slots are
        // page*8 + 0..7 into the flat hotbarAbilities array.
        private static readonly AccessTools.FieldRef<int> ActiveHotbarPage =
            AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(UIManagerScript), "indexOfActiveHotbar"));

        private static void ReadHotbar(MessageBuilder message) {
            HotbarBindable[] hb = UIManagerScript.hotbarAbilities;
            message.Fragment("Hotbar");
            if (hb == null) {
                message.Fragment("unavailable");
                return;
            }

            int page = ActiveHotbarPage();
            bool any = false;
            for (int i = 0; i < 8; i++) {
                int idx = page * 8 + i;
                if (idx >= hb.Length) {
                    break;
                }

                string name = HotbarSlotName(hb[idx]);
                if (name != null) {
                    message.ListItem((i + 1) + ", " + name);
                    any = true;
                }
            }

            if (!any) {
                message.Fragment("empty");
            }
        }

        private static string HotbarSlotName(HotbarBindable slot) {
            if (slot == null) {
                return null;
            }

            if (slot.actionType == HotbarBindableActions.ABILITY && slot.ability != null) {
                return GameLabelReader.Clean(slot.ability.GetNameForUI());
            }

            if (slot.actionType == HotbarBindableActions.CONSUMABLE && slot.consume != null) {
                return GameLabelReader.Clean(slot.consume.GetNameForUI());
            }

            return null;
        }

        // --- Status ---

        private static void ReadStatus(MessageBuilder message, HeroPC hero) {
            StatBlock stats = hero.myStats;
            message.Fragment("Health " + Bar(stats, StatTypes.HEALTH));
            message.ListItem("Stamina " + Bar(stats, StatTypes.STAMINA));
            message.ListItem("Energy " + Bar(stats, StatTypes.ENERGY));
            message.ListItem("Level " + stats.GetLevel());

            foreach (StatusEffect status in stats.GetAllStatuses()) {
                // Match the game's own status-bar filter: HUD-visible, non-passive effects only,
                // so the permanent job/feat passives are not read every time.
                if (!status.showIcon || status.passiveAbility) {
                    continue;
                }

                string name = GameLabelReader.Clean(status.abilityName);
                if (name == null) {
                    continue;
                }

                // Temporary effects carry a turn count; permanent ones do not.
                string duration = status.CheckDurTriggerOn(StatusTrigger.PERMANENT)
                    ? ""
                    : " " + status.curDuration + " turns";
                message.ListItem((status.isPositive ? "" : "bad: ") + name + duration);
            }
        }

        private static string Bar(StatBlock stats, StatTypes stat) {
            int cur = (int)stats.GetStat(stat, StatDataTypes.CUR);
            int max = (int)stats.GetStat(stat, StatDataTypes.MAX);
            return cur + " of " + max;
        }

        // --- Read here ---

        private static void ReadHere(MessageBuilder message, HeroPC hero) {
            Vector2 pos = hero.GetPos();
            int x = (int)pos.x;
            int y = (int)pos.y;
            MapTileData tile = MapMasterScript.GetTile(pos);

            message.Fragment(MapMasterScript.activeMap.GetName());
            message.ListItem(x + ", " + y);
            message.ListItem();
            // The hero's own tile: terrain + items, not the hero actor.
            TileDescriber.Contents(message, tile, includeActor: false);
            AppendExits(message, hero, pos);
        }

        // The 8 directions whose adjacent tile the hero could step into (not a wall/solid/blocked
        // actor), so the player learns where they can walk in one key instead of probing each
        // with the look cursor. +x east, +y north (the game's convention).
        private static readonly (int Dx, int Dy)[] Compass8 = {
            (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1),
        };

        private static void AppendExits(MessageBuilder message, HeroPC hero, Vector2 pos) {
            var open = new List<string>();
            foreach ((int dx, int dy) in Compass8) {
                MapTileData adj = MapMasterScript.GetTile(new Vector2(pos.x + dx, pos.y + dy));
                if (adj != null && !adj.IsCollidable(hero)) {
                    open.Add(GridDirection.Compass(dx, dy));
                }
            }

            message.ListItem(open.Count == 0 ? "no exits" : "exits: " + string.Join(", ", open));
        }

        // --- Scan ---

        private static void Scan(MessageBuilder message, HeroPC hero) {
            Vector2 hp = hero.GetPos();
            List<Poi> found = Surroundings.CollectVisible(hero);
            if (found.Count == 0) {
                message.Fragment("Nothing in view.");
                return;
            }

            // Hostiles first, then by distance — what to react to leads.
            found.Sort((a, b) => a.Hostile != b.Hostile ? (a.Hostile ? -1 : 1) : a.Steps - b.Steps);

            message.Fragment(found.Count + (found.Count == 1 ? " thing in view" : " things in view"));
            foreach (Poi p in found) {
                message.ListItem(p.Name);
                message.Fragment(p.Hostile ? "(hostile)" : null);
                message.Fragment(GridDirection.Offset((int)p.Pos.x - (int)hp.x, (int)p.Pos.y - (int)hp.y));
            }
        }
    }
}
