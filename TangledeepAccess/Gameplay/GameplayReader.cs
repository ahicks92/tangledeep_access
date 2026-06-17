using System.Collections.Generic;
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
        /// <summary>Speak the result of a gameplay query, or null if not in play.</summary>
        public static string Execute(GameplayCommand command) {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null) {
                return null;
            }

            switch (command) {
                case GameplayCommand.LookToggle:
                    return LookCursor.Toggle();
                case GameplayCommand.LookRecenter:
                    return LookCursor.Recenter();
                case GameplayCommand.LookNorth:
                    return LookCursor.Move(0, 1);
                case GameplayCommand.LookSouth:
                    return LookCursor.Move(0, -1);
                case GameplayCommand.LookEast:
                    return LookCursor.Move(1, 0);
                case GameplayCommand.LookWest:
                    return LookCursor.Move(-1, 0);
                case GameplayCommand.LookNortheast:
                    return LookCursor.Move(1, 1);
                case GameplayCommand.LookNorthwest:
                    return LookCursor.Move(-1, 1);
                case GameplayCommand.LookSoutheast:
                    return LookCursor.Move(1, -1);
                case GameplayCommand.LookSouthwest:
                    return LookCursor.Move(-1, -1);
            }

            var message = new MessageBuilder();
            switch (command) {
                case GameplayCommand.ReadHere:
                    ReadHere(message, hero);
                    break;
                case GameplayCommand.Scan:
                    Scan(message, hero);
                    break;
                case GameplayCommand.ReadStatus:
                    ReadStatus(message, hero);
                    break;
            }

            return message.Build();
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
        }

        // --- Scan ---

        private static void Scan(MessageBuilder message, HeroPC hero) {
            Vector2 hp = hero.GetPos();
            var found = new List<Sighting>();
            // Some pickups (e.g. food) appear both as an actor and as a tile item; dedupe by
            // name + tile so the same thing is not announced twice.
            var seen = new HashSet<string>();

            // Actors (NPCs, monsters, stairs, destructibles) currently in line of sight.
            foreach (Actor actor in MapMasterScript.activeMap.actorsInMap) {
                if (actor == null || actor == hero) {
                    continue;
                }

                Vector2 p = actor.GetPos();
                if (!IsVisible(hero, p)) {
                    continue;
                }

                string name = GameLabelReader.Clean(actor.displayName);
                if (name != null && seen.Add(Key(name, p))) {
                    found.Add(Sighting.Make(name, actor.actorfaction == Faction.ENEMY, hp, p));
                }
            }

            // Ground items on visible tiles.
            CollectGroundItems(hero, hp, found, seen);

            if (found.Count == 0) {
                message.Fragment("Nothing in view.");
                return;
            }

            // Hostiles first, then by distance — what to react to leads.
            found.Sort((a, b) => a.Hostile != b.Hostile ? (a.Hostile ? -1 : 1) : a.Steps - b.Steps);

            message.Fragment(found.Count + (found.Count == 1 ? " thing in view" : " things in view"));
            foreach (Sighting s in found) {
                message.ListItem(s.Name);
                message.Fragment(s.Hostile ? "(hostile)" : null);
                message.Fragment(s.Offset);
            }
        }

        private static void CollectGroundItems(HeroPC hero, Vector2 hp, List<Sighting> found, HashSet<string> seen) {
            Map map = MapMasterScript.activeMap;
            for (int x = 0; x < map.columns; x++) {
                for (int y = 0; y < map.rows; y++) {
                    if (!hero.visibleTilesArray[x, y]) {
                        continue;
                    }

                    MapTileData tile = map.GetTile(x, y);
                    List<Item> items = tile?.GetItemsInTile();
                    if (items == null) {
                        continue;
                    }

                    var at = new Vector2(x, y);
                    foreach (Item item in items) {
                        string name = GameLabelReader.Clean(item.GetNameForUI());
                        if (name != null && seen.Add(Key(name, at))) {
                            found.Add(Sighting.Make("item: " + name, false, hp, at));
                        }
                    }
                }
            }
        }

        private static string Key(string name, Vector2 pos) {
            return name + "@" + (int)pos.x + "," + (int)pos.y;
        }

        // --- Helpers ---

        private static bool IsVisible(HeroPC hero, Vector2 p) {
            if (!MapMasterScript.InBounds(p)) {
                return false;
            }

            bool[,] visible = hero.visibleTilesArray;
            return visible != null && visible[(int)p.x, (int)p.y];
        }

        private struct Sighting {
            public string Name;
            public bool Hostile;
            public int Steps;
            public string Offset;

            public static Sighting Make(string name, bool hostile, Vector2 from, Vector2 to) {
                int dx = (int)to.x - (int)from.x;
                int dy = (int)to.y - (int)from.y;
                return new Sighting {
                    Name = name,
                    Hostile = hostile,
                    Steps = GridDirection.Steps(dx, dy),
                    Offset = GridDirection.Offset(dx, dy),
                };
            }
        }
    }
}
