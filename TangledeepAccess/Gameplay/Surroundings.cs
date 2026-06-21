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
        public RadarCategory Category; // which radar voice the scanner pings it with
    }

    /// <summary>
    /// Collects everything in the hero's line of sight — actors (NPCs, monsters, stairs,
    /// destructibles) from <c>actorsInMap</c> plus ground items on visible tiles — as a list of
    /// <see cref="Poi"/>. Drives the F2 object radar (<see cref="ObjectRadar"/>), which pings the
    /// whole set; the textual scanner builds its own explored-map snapshot, not this. Terrain is the
    /// exception: it is clustered (over in-sight tiles, the same <see cref="TerrainClusterer"/> the
    /// scanner uses on explored tiles) into one Poi per pool at its nearest point, so a water field
    /// pings once rather than per tile. Re-queries live state every call.
    /// </summary>
    internal static class Surroundings {
        public static List<Poi> CollectVisible(HeroPC hero) {
            Vector2 hp = hero.GetPos();
            var found = new List<Poi>();
            // Some pickups appear both as an actor and a tile item; dedupe by name + tile.
            var seen = new HashSet<string>();

            Map map = MapMasterScript.activeMap;
            foreach (Actor actor in map.actorsInMap) {
                if (actor == hero || ActorPresence.IsGone(actor)) {
                    continue; // an opened crate (isDestroyed husk) / slain monster lingers here — skip it
                }
                if (TerrainFeature.Is(actor)) {
                    continue; // terrain — clustered below into one Poi per pool, not pinged per tile
                }

                Vector2 p = actor.GetPos();
                if (!IsVisible(hero, p)) {
                    continue;
                }

                string name = GameLabelReader.Clean(actor.displayName) ?? SpecialActorName(actor);
                if (name != null && seen.Add(Key(name, p))) {
                    found.Add(Make(name, actor.actorfaction == Faction.ENEMY, hp, p, actor, Classify(actor)));
                }
            }

            // Terrain clusters over the in-sight set: one ping per pool, at its nearest cell. The
            // handle is an interned key (kind + bounding-box corner) so a steady pool keeps its ring
            // slot across reconciles even though the cluster object is rebuilt each sweep.
            foreach (TerrainCluster cluster in TerrainFeature.Cluster(map, Visibility.VisibleNow)) {
                TerrainCell near = cluster.NearestCellTo((int)hp.x, (int)hp.y);
                var at = new Vector2(near.X, near.Y);
                string name = TerrainFeature.Name(cluster.Kind);
                object handle = string.Intern("terrain:" + cluster.Kind + ":" + cluster.MinX + "," + cluster.MinY);
                if (seen.Add(Key(name, at))) {
                    found.Add(Make(name, false, hp, at, handle, RadarCategory.Default));
                }
            }

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
                            found.Add(Make("item: " + name, false, hp, at, item, RadarCategory.Powerup));
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

        private static Poi Make(string name, bool hostile, Vector2 from, Vector2 to, object handle, RadarCategory category) {
            int dx = (int)to.x - (int)from.x;
            int dy = (int)to.y - (int)from.y;
            return new Poi {
                Name = name,
                Pos = to,
                Hostile = hostile,
                Steps = GridDirection.Steps(dx, dy),
                Handle = handle,
                Category = category,
            };
        }

        // The radar voice category for an actor, from its game type. Monsters, stairs/portals, and
        // powerups map straight from the actor type; a destructible is a container only when it is a
        // breakable (targetable) non-terrain object — crates and pots, not props, fountains, or terrain;
        // an NPC is a "shop" only when it actually runs a shop (a non-empty shopRef). Everything else
        // (doors, props, story NPCs, terrain) falls back to the default triangle tone.
        private static RadarCategory Classify(Actor actor) {
            switch (actor.GetActorType()) {
                case ActorTypes.MONSTER:
                    return RadarCategory.Monster;
                case ActorTypes.STAIRS:
                    return RadarCategory.Stairs;
                case ActorTypes.POWERUP:
                    return RadarCategory.Powerup;
                case ActorTypes.NPC:
                    return actor is NPC npc && !string.IsNullOrEmpty(npc.shopRef)
                        ? RadarCategory.Shop
                        : RadarCategory.Default;
                case ActorTypes.DESTRUCTIBLE:
                    return actor is Destructible d && d.targetable && !d.isTerrainTile
                        ? RadarCategory.Container
                        : RadarCategory.Default;
                default:
                    return RadarCategory.Default;
            }
        }

        private static string Key(string name, Vector2 pos) {
            return name + "@" + (int)pos.x + "," + (int)pos.y;
        }
    }
}
