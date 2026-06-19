using TangledeepAccess.Controls;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Input for the exploration cursor. The cursor is <b>always live</b> — there is no mode to enter
    /// — so this drainer simply claims its own dedicated keys every frame (the speculation ring plus
    /// read/follow/recenter); all of them are unbound in the forced Default layout, so nothing leaks
    /// to the game. Hero movement keys (arrows / numpad / the qweadzxc block) are never touched here,
    /// so the player can walk and speculate at the same time without a capture mode. Realization
    /// defers to <see cref="ExplorationCursor"/>.
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
                case ModInputKind.CursorRead:
                    spoken = ExplorationCursor.ReadCursor();
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
    /// Always live: it has a position at all times, stepped by the speculation ring and read on
    /// demand. Tangledeep's own Examine Mode (a smooth analog free-cursor) does not map cleanly to
    /// tile stepping, so the mod keeps its own integer cursor and reads each tile it lands on.
    ///
    /// <para><b>Follow mode</b> (default on): while on, the cursor snaps to the hero whenever the
    /// hero changes tile — but a stationary peek persists, since the snap only fires on an actual
    /// hero move. Only Alt+K toggles follow; speculation and the programmatic <see cref="JumpTo"/>
    /// leave follow alone (so with follow on they are transient — the cursor resnaps on the next
    /// step). This matches the free-cursor model other screen-reader mods use.</para>
    ///
    /// <para>Line of sight is respected: a visible tile is fully described; an out-of-sight tile
    /// reads "not visible" plus its direction, so the cursor never reveals what the hero cannot see.
    /// State is plain ints on the main thread — no caching of game objects.</para>
    /// </summary>
    internal static class ExplorationCursor {
        private static bool _initialized;
        private static int _x;
        private static int _y;
        private static bool _follow = true;
        private static int _lastHeroX;
        private static int _lastHeroY;
        private static Map _lastMap;

        /// <summary>The tile the cursor is on.</summary>
        public static Vector2 Position => new Vector2(_x, _y);

        /// <summary>
        /// Per-frame follow upkeep, called from the pump. Centers the cursor on the hero on the first
        /// in-play frame and after a map change, then — while follow is on — resnaps it to the hero
        /// only when the hero actually changed tile (so a stationary speculation peek stays put).
        /// Silent; reads are on demand.
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
                return;
            }

            if (_follow && (hx != _lastHeroX || hy != _lastHeroY)) {
                _x = hx;
                _y = hy;
            }
            _lastHeroX = hx;
            _lastHeroY = hy;
        }

        /// <summary>Step the cursor by (dx, dy) in tile space (+x east, +y north), then describe it.</summary>
        public static MessageBuilder Step(int dx, int dy) {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            if (hero == null || map == null) {
                return null;
            }

            int nx = Mathf.Clamp(_x + dx, 0, map.columns - 1);
            int ny = Mathf.Clamp(_y + dy, 0, map.rows - 1);
            var message = new MessageBuilder();
            if (nx == _x && ny == _y) {
                message.Fragment("Edge"); // clamped at the map border; re-read current tile
            }

            _x = nx;
            _y = ny;
            Describe(message, hero);
            return message;
        }

        /// <summary>Read (describe) the cursor's current tile — the K key.</summary>
        public static MessageBuilder ReadCursor() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null) {
                return null;
            }

            var message = new MessageBuilder();
            Describe(message, hero);
            return message;
        }

        /// <summary>Toggle follow mode. Turning it on snaps the cursor to the hero and reads there.</summary>
        public static MessageBuilder ToggleFollow() {
            HeroPC hero = GameMasterScript.heroPCActor;
            _follow = !_follow;
            var message = new MessageBuilder();
            if (_follow) {
                message.Fragment("Cursor follow on");
                if (hero != null) {
                    CenterOnHero(hero);
                    Describe(message, hero);
                }
            } else {
                message.Fragment("Cursor follow off");
            }
            return message;
        }

        /// <summary>Return the cursor to the hero now and describe that tile (Ctrl+K). Leaves follow as-is.</summary>
        public static MessageBuilder Recenter() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null) {
                return null;
            }

            CenterOnHero(hero);
            var message = new MessageBuilder();
            message.Fragment("Centered");
            Describe(message, hero);
            return message;
        }

        /// <summary>
        /// Position the cursor at <paramref name="pos"/> and return its description — the entry point
        /// for other systems that want to point the cursor at something (e.g. the scanner). Follow is
        /// left unchanged: with follow on the jump is transient (the cursor resnaps on the hero's next
        /// move), consistent with manual speculation.
        /// </summary>
        public static MessageBuilder JumpTo(Vector2 pos) {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            if (hero == null || map == null) {
                return null;
            }

            _x = Mathf.Clamp((int)pos.x, 0, map.columns - 1);
            _y = Mathf.Clamp((int)pos.y, 0, map.rows - 1);
            var message = new MessageBuilder();
            Describe(message, hero);
            return message;
        }

        private static void CenterOnHero(HeroPC hero) {
            Vector2 p = hero.GetPos();
            _x = (int)p.x;
            _y = (int)p.y;
        }

        private static void Describe(MessageBuilder message, HeroPC hero) {
            var pos = new Vector2(_x, _y);
            Vector2 hp = hero.GetPos();

            bool[,] visible = hero.visibleTilesArray;
            bool inSight = visible != null && visible[_x, _y];
            if (inSight) {
                TileDescriber.Contents(message, MapMasterScript.GetTile(pos), includeActor: true);
            } else {
                message.Fragment("not visible");
            }

            message.ListItem().PushRelativeCoordinates(pos - hp);
        }
    }
}
