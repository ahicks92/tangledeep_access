using System.Collections.Generic;
using TangledeepAccess.Focus;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>One thing the hero can currently see: a named actor or ground item at a tile.</summary>
    internal struct Poi {
        public string Name;
        public Vector2 Pos;
        public bool Hostile;
        public int Steps; // king-move distance from the hero
        public object Handle; // the underlying Actor/Item, for stable reference identity (scanner)
    }

    /// <summary>
    /// Collects everything in the hero's line of sight — actors (NPCs, monsters, stairs,
    /// destructibles) from <c>actorsInMap</c> plus ground items on visible tiles — as a list of
    /// <see cref="Poi"/>. Shared by the scanner (which reads the whole list) and the look
    /// cursor's jump-to-point-of-interest (which steps the cursor through it), so the
    /// visibility/dedup logic lives in one place. Re-queries live state every call.
    /// </summary>
    internal static class Surroundings {
        public static List<Poi> CollectVisible(HeroPC hero) {
            Vector2 hp = hero.GetPos();
            var found = new List<Poi>();
            // Some pickups appear both as an actor and a tile item; dedupe by name + tile.
            var seen = new HashSet<string>();

            foreach (Actor actor in MapMasterScript.activeMap.actorsInMap) {
                if (actor == null || actor == hero) {
                    continue;
                }

                Vector2 p = actor.GetPos();
                if (!IsVisible(hero, p)) {
                    continue;
                }

                string name = GameLabelReader.Clean(actor.displayName) ?? SpecialActorName(actor);
                if (name != null && seen.Add(Key(name, p))) {
                    found.Add(Make(name, actor.actorfaction == Faction.ENEMY, hp, p, actor));
                }
            }

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
                            found.Add(Make("item: " + name, false, hp, at, item));
                        }
                    }
                }
            }

            return found;
        }

        // Stairs and portals carry an empty displayName, so the generic name filter drops them even
        // though they are real, scannable points of interest. Give them a short synthesized label;
        // other unnamed actors stay unnamed (and skipped).
        private static string SpecialActorName(Actor actor) {
            if (actor.GetActorType() != ActorTypes.STAIRS) {
                return null;
            }

            Stairs stairs = actor as Stairs;
            if (stairs == null) {
                return null;
            }
            if (stairs.isPortal) {
                return "portal";
            }
            return stairs.stairsUp ? "stairs up" : "stairs down";
        }

        private static bool IsVisible(HeroPC hero, Vector2 p) {
            if (!MapMasterScript.InBounds(p)) {
                return false;
            }

            bool[,] visible = hero.visibleTilesArray;
            return visible != null && visible[(int)p.x, (int)p.y];
        }

        private static Poi Make(string name, bool hostile, Vector2 from, Vector2 to, object handle) {
            int dx = (int)to.x - (int)from.x;
            int dy = (int)to.y - (int)from.y;
            return new Poi {
                Name = name,
                Pos = to,
                Hostile = hostile,
                Steps = GridDirection.Steps(dx, dy),
                Handle = handle,
            };
        }

        private static string Key(string name, Vector2 pos) {
            return name + "@" + (int)pos.x + "," + (int)pos.y;
        }
    }
}
