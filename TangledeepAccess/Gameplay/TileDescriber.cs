using System.Collections.Generic;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Shared "what is on this tile" description, used by both the read-here query and the look
    /// cursor. Reuses the game's own hover builder (<c>HoverInfoScript.GetHoverText</c>) for the
    /// actor/feature on a tile — it returns the monster/NPC/object there, or empty for bare
    /// terrain — and falls back to the tile type plus any ground items. Always re-queries live.
    /// </summary>
    internal static class TileDescriber {
        /// <summary>
        /// Append a tile's contents to <paramref name="message"/>. When
        /// <paramref name="includeActor"/>, the actor/feature on the tile (the game's hover text)
        /// leads; otherwise only terrain and items are spoken (e.g. the hero's own tile, where
        /// the hover would just say the hero's name).
        /// </summary>
        public static void Contents(MessageBuilder message, MapTileData tile, bool includeActor) {
            if (tile == null) {
                message.Fragment("off map");
                return;
            }

            string occupant = includeActor ? Occupant(tile) : null;
            message.Fragment(occupant ?? Terrain(tile));
            AppendItems(message, tile);
        }

        /// <summary>
        /// The short spoken form of the actor/feature occupying a tile, or null for bare terrain (and
        /// terrain-only tiles — water/mud are terrain, never occupants). A monster reads as
        /// "name, HP%, attitude"; everything else is its name (the game's hover text, falling back to a
        /// blocking prop the hover ignores). Used both to name the occupant and to decide whether the
        /// entity cue should sound. The full tooltip — exact stats, hazard effects — is <see cref="Examine"/>.
        /// </summary>
        public static string Occupant(MapTileData tile) {
            if (tile == null) {
                return null;
            }

            // GetHoverText sets currentHoveredActor to whatever it described. For an item-only tile it
            // returns the item's name (AppendItems repeats it — treat as no occupant); for a
            // terrain-only tile it returns the terrain tile, which is terrain, not an occupant.
            string actor = GameLabelReader.Clean(HoverInfoScript.GetHoverText(tile));
            Actor described = HoverInfoScript.currentHoveredActor;
            if (actor != null && !IsGroundItemName(tile, actor) && !TerrainFeature.Is(described)) {
                var message = new MessageBuilder();
                AppendShortForm(message, described, ShortFormName(described, actor));
                return message.Build();
            }

            // A blocking object the game doesn't hover (a building/prop destructible like Nando's
            // kitchen) still occupies the tile and matters to a player who can't see it.
            return BlockingObjectName(tile);
        }

        /// <summary>
        /// Append an actor's short spoken form into <paramref name="message"/> as comma-separated
        /// items: the name, then (when <paramref name="offset"/> is given) the hero-relative offset,
        /// then for a monster its HP% and attitude. The shared seam behind the cursor's occupant read
        /// (no offset — "rat, 80% hp, aggressive") and the scanner's entry read, which injects
        /// coordinates after the name ("rat, 2 right 3 down, 80% hp, aggressive").
        /// </summary>
        public static void AppendShortForm(MessageBuilder message, Actor described, string name, Vector2? offset = null) {
            message.ListItem(name);
            if (offset.HasValue) {
                message.ListItem();
                message.PushRelativeCoordinates(offset.Value);
            }
            if (described is Monster mn) {
                int hpPct = (int)(mn.myStats.GetCurStatAsPercentOfMax(StatTypes.HEALTH) * 100f);
                message.ListItem(hpPct + "% hp").ListItem(Attitude(mn));
            }
        }

        // The leading name for the short form: a monster uses its own display name (the hover text
        // carries extra stats we re-derive), everything else keeps its already-short hover name.
        private static string ShortFormName(Actor described, string hoverName) {
            if (described is Monster mn) {
                return GameLabelReader.Clean(mn.displayName) ?? mn.actorRefName;
            }

            return hoverName;
        }

        /// <summary>
        /// The full examine tooltip for a tile — the game's own <c>GetHoverText</c> verbatim
        /// (monster stats/resistances/statuses, or terrain plus its hazard effect like
        /// "chance to root on step"). This is the Shift+K form; the hazard effect deliberately rides
        /// here and not on the terse read, since a player memorizes it after a couple of encounters.
        /// </summary>
        public static string Examine(MapTileData tile) {
            if (tile == null) {
                return null;
            }

            string text = GameLabelReader.Clean(HoverInfoScript.GetHoverText(tile));
            if (!string.IsNullOrEmpty(text) && !IsGroundItemName(tile, text)) {
                return text;
            }

            return BlockingObjectName(tile);
        }

        // The monster's stance toward the hero, mirroring the game's own examine wording.
        private static string Attitude(Monster mn) {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero != null && mn.CheckTarget(hero)) {
                return "hostile";
            }
            if (mn.myBehaviorState == BehaviorState.CURIOUS || mn.myBehaviorState == BehaviorState.SEEKINGITEM) {
                return "curious";
            }
            if (mn.myBehaviorState == BehaviorState.STALKING) {
                return "stalking";
            }

            return mn.aggroRange > 0f ? "aggressive" : "neutral";
        }

        // A collidable object the game's hover text ignores — chiefly non-targetable destructibles
        // (decorative buildings and props). GetHoverText returns empty for them and GetTargetable
        // is null, yet they block the tile, so we name them from the destructible's display name.
        // Terrain tiles are non-targetable destructibles too, but they are terrain, not objects, so
        // they are skipped here.
        private static string BlockingObjectName(MapTileData tile) {
            List<Actor> here = tile.GetAllTargetablePlusDestructibles();
            if (here == null) {
                return null;
            }

            foreach (Actor a in here) {
                if (a.GetActorType() == ActorTypes.DESTRUCTIBLE && !TerrainFeature.Is(a)) {
                    string name = GameLabelReader.Clean(a.displayName);
                    if (name != null) {
                        return name;
                    }
                }
            }

            return null;
        }

        private static bool IsGroundItemName(MapTileData tile, string text) {
            List<Item> items = tile.GetItemsInTile();
            if (items == null) {
                return false;
            }

            foreach (Item item in items) {
                if (text == GameLabelReader.Clean(item.GetNameForUI())) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A spoken terrain name. Hazard/feature location tags (lava, water, mud, ...) take
        /// priority over the coarse <c>tileType</c> because water and lava tiles are often
        /// <c>GROUND</c>-typed yet matter a great deal to a player who cannot see them.
        /// </summary>
        public static string Terrain(MapTileData tile) {
            if (tile.CheckTag(LocationTags.LAVA)) {
                return "lava";
            }
            if (tile.CheckTag(LocationTags.WATER) || tile.CheckTag(LocationTags.ISLANDSWATER)) {
                return "water";
            }
            if (tile.CheckTag(LocationTags.MUD) || tile.CheckTag(LocationTags.SUMMONEDMUD)) {
                return "mud";
            }
            if (tile.CheckTag(LocationTags.ELECTRIC)) {
                return "electrified";
            }
            if (tile.CheckTag(LocationTags.LASER)) {
                return "laser";
            }
            if (tile.CheckTag(LocationTags.TREE)) {
                return "tree";
            }
            if (tile.CheckTag(LocationTags.GRASS) || tile.CheckTag(LocationTags.GRASS2)) {
                return "grass";
            }

            return tile.tileType.ToString().ToLowerInvariant().Replace('_', ' ');
        }

        /// <summary>
        /// True for terrain a walking player should be warned about underfoot (the damaging or
        /// movement-affecting tags), so movement feedback can speak it even on a GROUND tile.
        /// </summary>
        public static bool IsHazard(MapTileData tile) {
            return tile.CheckTag(LocationTags.LAVA)
                || tile.CheckTag(LocationTags.WATER)
                || tile.CheckTag(LocationTags.ISLANDSWATER)
                || tile.CheckTag(LocationTags.MUD)
                || tile.CheckTag(LocationTags.SUMMONEDMUD)
                || tile.CheckTag(LocationTags.ELECTRIC)
                || tile.CheckTag(LocationTags.LASER);
        }

        /// <summary>
        /// True when a charging monster has telegraphed an incoming attack on this tile — the game's
        /// <c>obj_dangersquare</c> warning marker (a summoned destructible registered on the tile, the
        /// same one the game queries via <c>GetActorRef</c>). The player's own / ally charges use a
        /// separate <c>obj_friendlydangersquare</c> marker, which we deliberately ignore: this answers
        /// "a monster is about to hit here," the red square a sighted player would see underfoot.
        /// </summary>
        public static bool HasDangerSquare(MapTileData tile) {
            return tile != null && tile.HasActorByRef("obj_dangersquare");
        }

        internal static void AppendItems(MessageBuilder message, MapTileData tile) {
            List<Item> items = tile.GetItemsInTile();
            if (items == null) {
                return;
            }

            foreach (Item item in items) {
                string name = GameLabelReader.Clean(item.GetNameForUI());
                if (name != null) {
                    message.ListItem("item: " + name);
                }
            }
        }
    }
}
