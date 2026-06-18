using System.Collections.Generic;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Announces notable contents of a tile the hero steps onto, so the player learns there is
    /// an item to grab or that they have entered hazardous terrain without polling read-here
    /// every step. Polled once per frame from the pump; it compares the hero's tile to the
    /// last and speaks only when the tile changed AND has something worth saying (ground items,
    /// or terrain other than plain ground). Plain empty ground is silent, keeping walking quiet.
    ///
    /// <para>Deliberately conservative: combat and events come through the game log; this only
    /// covers "what am I standing on." No game-object caching — just the last integer tile.</para>
    /// </summary>
    internal static class MovementWatcher {
        private static bool _have;
        private static int _x;
        private static int _y;

        /// <summary>The announcement for a just-entered notable tile, or null. Call every frame.</summary>
        public static MessageBuilder PollOnMove() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null) {
                _have = false; // dropped out of play; re-arm so re-entry doesn't fire on spawn
                return null;
            }

            Vector2 pos = hero.GetPos();
            int x = (int)pos.x;
            int y = (int)pos.y;
            if (_have && x == _x && y == _y) {
                return null;
            }

            bool first = !_have;
            _have = true;
            _x = x;
            _y = y;
            if (first) {
                return null; // don't announce the spawn tile
            }

            MapTileData tile = MapMasterScript.GetTile(pos);
            if (tile == null) {
                return null;
            }

            var message = new MessageBuilder();
            bool notable = false;

            // Terrain when it is not plain ground, OR a hazard tag on a ground-typed tile
            // (water/lava/mud/electric/laser often ride a GROUND tile yet matter a lot).
            if (tile.tileType != TileTypes.GROUND || TileDescriber.IsHazard(tile)) {
                message.Fragment(TileDescriber.Terrain(tile));
                notable = true;
            }

            List<Item> items = tile.GetItemsInTile();
            if (items != null) {
                foreach (Item item in items) {
                    string name = GameLabelReader.Clean(item.GetNameForUI());
                    if (name != null) {
                        message.ListItem("item: " + name);
                        notable = true;
                    }
                }
            }

            return notable ? message : null;
        }
    }
}
