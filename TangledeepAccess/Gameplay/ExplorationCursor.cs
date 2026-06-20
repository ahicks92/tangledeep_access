using TangledeepAccess.Audio;
using TangledeepAccess.Controls;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Input for the exploration cursor. The cursor is always live, so this drainer claims its own
    /// dedicated keys every frame: the speculation ring (step), Shift+ring (skip), K (read), Alt+K
    /// (follow toggle), Ctrl+K (recenter). All are unbound in the forced Default layout, so the
    /// cursor and hero movement coexist with no capture mode. Realization defers to
    /// <see cref="ExplorationCursor"/>.
    /// </summary>
    public sealed class ExplorationCursorInputDrainer : InputDrainer {
        public static readonly ExplorationCursorInputDrainer Instance = new ExplorationCursorInputDrainer();

        public override bool Claim(bool suppressWhileHeld) {
            ModInputAction? action = InputKeys.CursorKeys();
            if (action.HasValue) {
                InputQueue.Enqueue(this, action.Value);
                return true;
            }

            return false; // not our key — let the chain fall through to free play / the game
        }

        public override void Realize(ModInputAction action, PrismSpeech speech) {
            MessageBuilder spoken;
            switch (action.Kind) {
                case ModInputKind.Move:
                    spoken = ExplorationCursor.Step(action.Dx, action.Dy);
                    break;
                case ModInputKind.CursorSkip:
                    spoken = ExplorationCursor.Skip(action.Dx, action.Dy);
                    break;
                case ModInputKind.CursorRead:
                    spoken = ExplorationCursor.ReadCursor();
                    break;
                case ModInputKind.CursorExamine:
                    spoken = ExplorationCursor.ExamineCursor();
                    break;
                case ModInputKind.CursorFollowToggle:
                    spoken = ExplorationCursor.ToggleFollow();
                    break;
                case ModInputKind.CursorRecenter:
                    spoken = ExplorationCursor.Recenter();
                    break;
                default:
                    spoken = null;
                    break;
            }

            speech.Speak(spoken);
        }
    }

    /// <summary>
    /// A discrete tile cursor for examining the map without moving the hero — "the cursor" in docs.
    /// Always live, with a position at all times. Two channels report a tile:
    ///
    /// <list type="bullet">
    /// <item><b>Speech is differential</b> for verbosity: a step speaks only the parts of the tile's
    /// <see cref="TileKey"/> (terrain, shape) that changed since the last tile, plus any occupant and
    /// items (those are always read, not differenced). Coordinates are spoken only on the explicit
    /// read (K).</item>
    /// <item><b>Sound always plays</b> on a step/skip/read, even when speech says nothing: the
    /// passability cue (ground or impassable) plus the entity cue when an occupant is present — they
    /// stack. See <see cref="CursorSounds"/>.</item>
    /// </list>
    ///
    /// <para><b>Follow mode</b> (default on): the cursor snaps to the hero whenever the hero changes
    /// tile, but a stationary peek persists (the snap fires only on a real hero move). Only Alt+K
    /// toggles follow; speculation, skip, and the programmatic <see cref="JumpTo"/> leave it alone.</para>
    ///
    /// <para>Visibility follows the minimap, not line of sight: the cursor reads any <em>explored</em>
    /// tile (terrain, shape, and live occupants — minimap parity), and an explored tile that is not
    /// currently in sight is tagged "blurred" (tracked differentially, announced once on the boundary).
    /// Only a truly <em>unexplored</em> tile reads "unexplored" and plays no cue. State is plain ints
    /// on the main thread.</para>
    /// </summary>
    internal static class ExplorationCursor {
        private static bool _initialized;
        private static int _x;
        private static int _y;
        private static bool _follow = true;
        private static int _lastHeroX;
        private static int _lastHeroY;
        private static Map _lastMap;

        // The last tile key we spoke, the baseline for differential speech. Null forces a full read
        // (start, after a not-visible tile, after a map change).
        private static TileKey? _lastKey;

        /// <summary>The tile the cursor is on.</summary>
        public static Vector2 Position => new Vector2(_x, _y);

        /// <summary>
        /// Per-frame follow upkeep, called from the pump. Centers on the hero on the first in-play
        /// frame and after a map change, then — while follow is on — resnaps to the hero only when the
        /// hero actually changed tile. Silent (no speech, no cue); reads are on demand.
        /// </summary>
        public static void SyncFollow() {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            if (hero == null || map == null) {
                _initialized = false; // out of play; re-center on re-entry
                return;
            }

            Vector2 p = hero.GetPos();
            int hx = (int)p.x;
            int hy = (int)p.y;

            if (!_initialized || map != _lastMap) {
                _x = hx;
                _y = hy;
                _initialized = true;
                _lastMap = map;
                _lastHeroX = hx;
                _lastHeroY = hy;
                SetBaselineToHero(hero);
                return;
            }

            if (_follow && (hx != _lastHeroX || hy != _lastHeroY)) {
                _x = hx;
                _y = hy;
                SetBaselineToHero(hero);
            }
            _lastHeroX = hx;
            _lastHeroY = hy;
        }

        /// <summary>Step one tile (+x east, +y north): differential read, no coordinates.</summary>
        public static MessageBuilder Step(int dx, int dy) {
            Map map = MapMasterScript.activeMap;
            if (GameMasterScript.heroPCActor == null || map == null) {
                return null;
            }

            int nx = Mathf.Clamp(_x + dx, 0, map.columns - 1);
            int ny = Mathf.Clamp(_y + dy, 0, map.rows - 1);
            bool edge = nx == _x && ny == _y;
            _x = nx;
            _y = ny;

            var message = new MessageBuilder();
            if (edge) {
                message.Fragment("edge");
            }
            AppendRead(message, differential: true, withCoords: false, playCues: true);
            return message;
        }

        /// <summary>
        /// Skip in a direction (Shift+ring): keep stepping while the tile key matches the start and no
        /// occupant is hit, stopping on the first change/occupant (landing on it) or at the map edge /
        /// fog boundary. Announces "skipped N tiles, &lt;landing read&gt;" with a forced comma.
        /// </summary>
        public static MessageBuilder Skip(int dx, int dy) {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            if (hero == null || map == null) {
                return null;
            }

            // A run is defined relative to the start tile; if that is unexplored, just do a single step.
            if (!Visibility.Explored(_x, _y)) {
                return Step(dx, dy);
            }

            TileKey refKey = KeyAt(new Vector2(_x, _y), MapMasterScript.GetTile(new Vector2(_x, _y)));
            int cx = _x;
            int cy = _y;
            int count = 0;
            while (true) {
                int tx = cx + dx;
                int ty = cy + dy;
                if (tx < 0 || tx >= map.columns || ty < 0 || ty >= map.rows) {
                    break; // map edge
                }
                if (!Visibility.Explored(tx, ty)) {
                    break; // don't skip into the unexplored unknown
                }

                var tp = new Vector2(tx, ty);
                MapTileData tt = MapMasterScript.GetTile(tp);
                bool stop = KeyAt(tp, tt) != refKey || TileDescriber.Occupant(tt) != null;
                cx = tx;
                cy = ty;
                count++;
                if (stop) {
                    break; // landed on the change / occupant
                }
            }

            _x = cx;
            _y = cy;

            var message = new MessageBuilder();
            if (count == 0) {
                message.Fragment("edge");
                AppendRead(message, differential: false, withCoords: false, playCues: true);
                return message;
            }

            CursorSounds.PlaySkipped();
            message.Fragment("skipped").Fragment(count + (count == 1 ? " tile" : " tiles"));
            message.ListItemForcedComma();
            AppendRead(message, differential: false, withCoords: false, playCues: true);
            return message;
        }

        /// <summary>Read the cursor's tile in full, with coordinates — the K key.</summary>
        public static MessageBuilder ReadCursor() {
            if (GameMasterScript.heroPCActor == null) {
                return null;
            }

            var message = new MessageBuilder();
            AppendRead(message, differential: false, withCoords: true, playCues: false);
            return message;
        }

        /// <summary>
        /// Examine the cursor's tile in full — the game's own tooltip (Shift+K): full monster stats, or
        /// terrain plus its hazard effect ("mud, chance to root on step"). Coordinates lead; a blurred
        /// tile is tagged; an unexplored tile reads "unexplored".
        /// </summary>
        public static MessageBuilder ExamineCursor() {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            if (hero == null || map == null) {
                return null;
            }

            var message = new MessageBuilder();
            var pos = new Vector2(_x, _y);
            message.PushRelativeCoordinates(pos - hero.GetPos());
            message.ListItem();

            if (!Visibility.Explored(_x, _y)) {
                message.Fragment("unexplored");
                return message;
            }
            if (Visibility.Blurred(_x, _y)) {
                message.Fragment("blurred");
            }

            MapTileData tile = MapMasterScript.GetTile(pos);
            string full = TileDescriber.Examine(tile);
            message.Fragment(string.IsNullOrEmpty(full) ? TileDescriber.Terrain(tile) : full);
            TileDescriber.AppendItems(message, tile);
            return message;
        }

        /// <summary>Toggle follow mode (Alt+K). Turning it on snaps to the hero and reads there.</summary>
        public static MessageBuilder ToggleFollow() {
            HeroPC hero = GameMasterScript.heroPCActor;
            _follow = !_follow;
            var message = new MessageBuilder();
            if (_follow) {
                message.Fragment("cursor follow on");
                if (hero != null) {
                    CenterOnHero(hero);
                    AppendRead(message, differential: false, withCoords: false, playCues: true);
                }
            } else {
                message.Fragment("cursor follow off");
            }
            return message;
        }

        /// <summary>Return the cursor to the hero now and read that tile (Ctrl+K). Leaves follow as-is.</summary>
        public static MessageBuilder Recenter() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null) {
                return null;
            }

            CenterOnHero(hero);
            var message = new MessageBuilder();
            message.Fragment("centered");
            AppendRead(message, differential: false, withCoords: false, playCues: true);
            return message;
        }

        /// <summary>
        /// Position the cursor at <paramref name="target"/> and read it in full — the entry point for
        /// other systems that want to point the cursor at something. Follow is left unchanged.
        /// </summary>
        public static MessageBuilder JumpTo(Vector2 target) {
            Map map = MapMasterScript.activeMap;
            if (GameMasterScript.heroPCActor == null || map == null) {
                return null;
            }

            _x = Mathf.Clamp((int)target.x, 0, map.columns - 1);
            _y = Mathf.Clamp((int)target.y, 0, map.rows - 1);
            var message = new MessageBuilder();
            AppendRead(message, differential: false, withCoords: false, playCues: true);
            return message;
        }

        // Append the current tile's read into the message, and play its cues unless suppressed.
        // Coordinates (when asked) lead. Out of sight: "not visible", no cue, baseline reset.
        // Visible: differential or full terrain/shape, then the always-read occupant and items.
        private static void AppendRead(MessageBuilder message, bool differential, bool withCoords, bool playCues) {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null) {
                return;
            }

            var pos = new Vector2(_x, _y);
            if (withCoords) {
                message.PushRelativeCoordinates(pos - hero.GetPos());
                message.ListItem(); // subsequent content is a new, comma-separated item
            }

            if (!Visibility.Explored(_x, _y)) {
                message.Fragment("unexplored");
                _lastKey = null;
                return;
            }

            MapTileData tile = MapMasterScript.GetTile(pos);
            if (playCues) {
                PlayTileCues(pos, tile);
            }

            TileKey key = KeyAt(pos, tile);
            if (differential) {
                key.AppendChanges(message, _lastKey);
            } else {
                key.AppendFull(message);
            }
            _lastKey = key;

            string occupant = TileDescriber.Occupant(tile);
            if (occupant != null) {
                message.ListItem(occupant);
            }
            TileDescriber.AppendItems(message, tile);
        }

        private static void PlayTileCues(Vector2 pos, MapTileData tile) {
            if (Impassable(pos, tile)) {
                CursorSounds.PlayImpassable();
            } else {
                CursorSounds.PlayGround();
            }

            if (TileDescriber.Occupant(tile) != null) {
                CursorSounds.PlayEntity();
            }

            // A telegraphed attack square stacks on top of the passability/entity cues: the tile is
            // still walkable ground, but a monster is about to strike it.
            if (TileDescriber.HasDangerSquare(tile)) {
                CursorSounds.PlayDangerous();
            }
        }

        // A wall has no meaningful local shape, so the shape is suppressed (Open speaks nothing) on
        // any impassable tile; only walkable tiles carry a shape.
        private static TileKey KeyAt(Vector2 pos, MapTileData tile) {
            TileShape shape = Impassable(pos, tile)
                ? new TileShape(TileShapeKind.Open, Direction.None, 0)
                : ShapeAt(pos);
            bool blurred = Visibility.Blurred((int)pos.x, (int)pos.y);
            return new TileKey(TileDescriber.Terrain(tile), shape, blurred);
        }

        private static bool Impassable(Vector2 pos, MapTileData tile) {
            return TerrainQuery.IsImpassableWall(pos) || (tile != null && tile.CheckTag(LocationTags.TREE));
        }

        private static TileShape ShapeAt(Vector2 pos) {
            var passable = new bool[8];
            for (int i = 0; i < TileShapes.DirectionsCW.Length; i++) {
                (int dx, int dy) = TileShapes.DirectionsCW[i];
                passable[i] = !TerrainQuery.IsImpassableWall(new Vector2(pos.x + dx, pos.y + dy));
            }
            return TileShapes.Describe(passable);
        }

        private static void SetBaselineToHero(HeroPC hero) {
            Vector2 pos = hero.GetPos();
            int x = (int)pos.x;
            int y = (int)pos.y;
            _lastKey = Visibility.Explored(x, y) ? KeyAt(pos, MapMasterScript.GetTile(pos)) : (TileKey?)null;
        }

        private static void CenterOnHero(HeroPC hero) {
            Vector2 p = hero.GetPos();
            _x = (int)p.x;
            _y = (int)p.y;
        }
    }
}
