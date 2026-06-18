using System.Collections.Generic;
using TangledeepAccess.Controls;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Input for the look cursor, beside the cursor it drives — and the sole owner of the cursor's
    /// whole lifecycle. While off it claims exactly one key, the toggle that turns it on, and passes
    /// everything else through to free play. While on it owns the toggle (to turn off) plus the
    /// cursor-movement set (step, recenter, jump-to-POI); every other key (our query hotkeys, the
    /// game's own keys) still passes through, so they keep working while looking. The one subtlety:
    /// a <em>held</em> directional has no key-down on its repeat frames, so we swallow it anyway, or
    /// the repeat would leak to the game and walk the hero alongside the cursor. (Reading the cursor
    /// tile with K is a free-play query that is already cursor-aware, so it lives in
    /// <see cref="GameplayReader"/>, not here.)
    /// </summary>
    public sealed class LookInputDrainer : InputDrainer {
        public static readonly LookInputDrainer Instance = new LookInputDrainer();

        public override bool Claim(bool suppressWhileHeld) {
            if (!LookCursor.Active) {
                // Off: the only key we own is the one that turns us on.
                ModInputAction? toggle = InputKeys.LookToggle();
                if (toggle.HasValue) {
                    InputQueue.Enqueue(this, toggle.Value);
                    return true;
                }

                return false; // pass everything else to the chain below
            }

            // On: the toggle (to turn off) and our movement set; everything else passes through.
            ModInputAction? action = InputKeys.LookToggle() ?? InputKeys.LookMove();
            if (action.HasValue) {
                InputQueue.Enqueue(this, action.Value);
                return true;
            }

            // Swallow held directionals (no key-down this frame) so their repeat can't walk the
            // hero; let anything else fall through to free play and the game.
            return InputKeys.AnyLookDirectionalHeld();
        }

        public override void Realize(ModInputAction action, PrismSpeech speech) {
            MessageBuilder spoken;
            switch (action.Kind) {
                case ModInputKind.LookToggle:
                    spoken = LookCursor.Toggle();
                    break;
                case ModInputKind.Move:
                    spoken = LookCursor.Move(action.Dx, action.Dy);
                    break;
                case ModInputKind.LookRecenter:
                    spoken = LookCursor.Recenter();
                    break;
                case ModInputKind.LookNextPoi:
                    spoken = LookCursor.JumpToPoi(1);
                    break;
                case ModInputKind.LookPrevPoi:
                    spoken = LookCursor.JumpToPoi(-1);
                    break;
                default:
                    spoken = null;
                    break;
            }

            speech.Speak(spoken);
        }
    }

    /// <summary>
    /// A discrete tile cursor for examining the map without moving the hero. Tangledeep's own
    /// Examine Mode is a smooth analog free-cursor (an icon nudged by a delta), which does not
    /// map cleanly to arrow-key tile stepping, so the mod keeps its own integer cursor and
    /// reads each tile it lands on. The input layer captures the arrow keys while the cursor is
    /// active (suppressing hero movement); the pump moves the cursor and speaks.
    ///
    /// <para>Line of sight is respected: a tile the hero can currently see is fully described
    /// (actor/feature, terrain, items); a tile out of sight reads only "not visible" plus its
    /// direction, so the cursor never reveals what the hero cannot see. State is plain ints on
    /// the main thread — no caching of game objects.</para>
    /// </summary>
    internal static class LookCursor {
        public static bool Active { get; private set; }
        private static int _x;
        private static int _y;

        /// <summary>The tile the cursor is on. Only meaningful while <see cref="Active"/>.</summary>
        public static Vector2 Position => new Vector2(_x, _y);

        /// <summary>
        /// Append the focused tile's line-of-sight-gated description (visible contents, or "not
        /// visible") plus its offset from the hero. Lets the read-here command reuse the cursor's
        /// own LOS rule instead of re-reading a remote tile and leaking what the hero can't see.
        /// </summary>
        public static void Read(MessageBuilder message, HeroPC hero) {
            Describe(message, hero);
        }

        /// <summary>Toggle the cursor on (centered on the hero) or off. Returns what to speak.</summary>
        public static MessageBuilder Toggle() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (Active || hero == null) {
                Active = false;
                return new MessageBuilder().Fragment("Look cursor off");
            }

            Active = true;
            CenterOnHero(hero);
            var message = new MessageBuilder();
            message.Fragment("Look cursor");
            Describe(message, hero);
            return message;
        }

        /// <summary>Re-center the cursor on the hero and describe that tile.</summary>
        public static MessageBuilder Recenter() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (!Active || hero == null) {
                return null;
            }

            CenterOnHero(hero);
            var message = new MessageBuilder();
            message.Fragment("Centered");
            Describe(message, hero);
            return message;
        }

        /// <summary>Step the cursor by (dx, dy) in tile space (+x east, +y north), then describe it.</summary>
        public static MessageBuilder Move(int dx, int dy) {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            if (!Active || hero == null || map == null) {
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

        /// <summary>
        /// Jump the cursor to the next (<paramref name="dir"/> +1) or previous (-1) point of
        /// interest in line of sight, nearest-first, wrapping. Lets the player tour visible
        /// actors and items without stepping tile by tile (the Factorio-Access cursor model).
        /// </summary>
        public static MessageBuilder JumpToPoi(int dir) {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (!Active || hero == null || MapMasterScript.activeMap == null) {
                return null;
            }

            List<Poi> pois = Surroundings.CollectVisible(hero);
            if (pois.Count == 0) {
                return new MessageBuilder().Fragment("nothing in view");
            }

            pois.Sort((a, b) => a.Steps - b.Steps);

            // Where is the cursor now in that order? -1 if not on a POI.
            int current = -1;
            for (int i = 0; i < pois.Count; i++) {
                if ((int)pois[i].Pos.x == _x && (int)pois[i].Pos.y == _y) {
                    current = i;
                    break;
                }
            }

            // From "nowhere", forward starts at the nearest, backward at the farthest.
            int next = current < 0 ? (dir > 0 ? 0 : pois.Count - 1) : (current + dir + pois.Count) % pois.Count;
            _x = (int)pois[next].Pos.x;
            _y = (int)pois[next].Pos.y;

            var message = new MessageBuilder();
            Describe(message, hero);
            return message;
        }

        /// <summary>Drop the cursor if the hero/level went away (e.g. on level change).</summary>
        public static void Reset() {
            Active = false;
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
