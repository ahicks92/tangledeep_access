using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Describes the tile under the ranged-targeting cursor while the player aims a ranged
    /// weapon or a point/area ability. Fed by the hook on
    /// <c>PlayerInputTargetingManager.UpdateCurrentTargetingInformation</c> (which the game
    /// calls as the targeting cursor moves), it speaks what is on the target tile, its
    /// direction and distance from the hero, and whether it is a valid target — the core of the
    /// "ranged targeting is a visual problem" the project flagged.
    ///
    /// <para>Hook records, pump speaks (interrupting — it is active cursor movement). Deduped by
    /// tile so a repeated update for the same location does not repeat.</para>
    /// </summary>
    internal static class TargetingReader {
        private static MessageBuilder _pending;
        private static int _lastX = int.MinValue;
        private static int _lastY = int.MinValue;

        /// <summary>Record the targeted tile. Call from the targeting hook (main thread).</summary>
        public static void Aim(Vector2 location, bool isGoodTile) {
            int x = (int)location.x;
            int y = (int)location.y;
            if (x == _lastX && y == _lastY) {
                return;
            }

            _lastX = x;
            _lastY = y;

            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || MapMasterScript.activeMap == null) {
                return;
            }

            var message = new MessageBuilder();
            MapTileData tile = MapMasterScript.GetTile(location);
            TileDescriber.Contents(message, tile, includeActor: true);

            Vector2 hp = hero.GetPos();
            message.ListItem().PushRelativeCoordinates(location - hp);
            message.ListItem(isGoodTile ? "valid target" : "invalid target");
            _pending = message;
        }

        /// <summary>Take the pending targeting description (or null), clearing it.</summary>
        public static MessageBuilder Consume() {
            MessageBuilder p = _pending;
            _pending = null;
            return p;
        }

        /// <summary>Forget the last tile so the next aim always speaks (call when targeting starts/ends).</summary>
        public static void Reset() {
            _lastX = int.MinValue;
            _lastY = int.MinValue;
        }
    }
}
