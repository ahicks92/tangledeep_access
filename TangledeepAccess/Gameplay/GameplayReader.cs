using System.Collections.Generic;
using TangledeepAccess.Controls;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Input for free play, the floor of the priority chain: our query hotkeys overlay the game's
    /// own controls. We claim only our query keys and pass everything else (movement, the game's
    /// hotkeys) straight through to the game. Realization defers to <see cref="GameplayReader"/>,
    /// except repeat-last, which is handled here because it needs the speech instance's history.
    /// </summary>
    public sealed class GameplayInputDrainer : InputDrainer {
        public static readonly GameplayInputDrainer Instance = new GameplayInputDrainer();

        public override bool Claim(bool suppressWhileHeld) {
            ModInputAction? action = InputKeys.Query() ?? InputKeys.Volume() ?? InputKeys.NavAids();
            if (action.HasValue) {
                InputQueue.Enqueue(this, action.Value);
                return true;
            }

            return false; // movement and game hotkeys are the game's
        }

        public override void Realize(ModInputAction action, PrismSpeech speech) {
            if (action.Kind == ModInputKind.RepeatLast) {
                speech.RepeatLast();
                return;
            }

            // Volume nudges apply whenever the audio manager exists (even pre-run), so they bypass
            // GameplayReader's in-play gate.
            if (action.Kind == ModInputKind.VolumeMusic
                || action.Kind == ModInputKind.VolumeSfx
                || action.Kind == ModInputKind.VolumeFootsteps) {
                speech.Speak(VolumeControl.Adjust(action));
                return;
            }

            // Navigation aids. Shift+Fn and Ctrl+Fn each route to the aid's own hook, which decides
            // what to do (toggle, fire once, …) and returns any line to speak.
            if (action.Kind == ModInputKind.NavAidToggle) {
                speech.Speak(NavAids.OnShiftKey(action.Dx));
                return;
            }
            if (action.Kind == ModInputKind.NavAidTrigger) {
                speech.Speak(NavAids.OnCtrlKey(action.Dx));
                return;
            }

            speech.Speak(GameplayReader.Execute(action));
        }
    }

    /// <summary>
    /// Computes spoken answers to the player's on-demand spatial queries during gameplay:
    /// "read here" (the hero's tile) and "scan" (a Factorio-Access-style sweep of everything in
    /// view, by direction and distance). All reads re-query live game state at call time — no
    /// caching — and respect the hero's line of sight via <c>visibleTilesArray</c>. Runs on the
    /// Unity main thread from the per-frame pump; the input hook only requests the command.
    /// </summary>
    internal static class GameplayReader {
        /// <summary>
        /// Compute the spoken answer for a free-play query, or null if not in play. The look cursor's
        /// own keys — its toggle and movement — never reach here; <see cref="LookInputDrainer"/>
        /// realizes those. This handles only the query hotkeys. Repeat-last is realized in
        /// <see cref="GameplayInputDrainer"/>.
        /// </summary>
        public static MessageBuilder Execute(ModInputAction action) {
            var message = new MessageBuilder();

            // Help is static text and useful even mid-transition, so answer it before the
            // in-play gate. It is one authored paragraph, so it is a single fragment.
            if (action.Kind == ModInputKind.Help) {
                return message.Fragment(
                    "Tangledeep Access commands. K, read here and surroundings. "
                    + "L, scan in view. Y, status. A, hotbar; backtick, cycle hotbar. Semicolon, look cursor; "
                    + "then arrows or numpad to move it, brackets to jump between things in view, "
                    + "Home to recenter. Page up and page down, step scanner entries; control plus "
                    + "page up or down, step scanner categories. "
                    + "Navigation aids sit on F keys. F1 is wall echo: shift F1 toggles it on or off "
                    + "(on by default), control F1 fires it once. F2 is the entity scanner: control F2 "
                    + "toggles a continuous sweep of things in view, panned by side-to-side position "
                    + "and pitched by how far north or south. "
                    + "F8, F9, F10, lower music, sound, footsteps volume; hold shift to raise. "
                    + "Apostrophe, repeat. Slash, this help.");
            }

            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null) {
                return null;
            }

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
            }

            return message;
        }

        // --- Status ---

        private static void ReadStatus(MessageBuilder message, HeroPC hero) {
            StatBlock stats = hero.myStats;
            Bar(message.ListItem(), stats, StatTypes.HEALTH, "health");
            Bar(message.ListItem(), stats, StatTypes.STAMINA, "stamina");
            Bar(message.ListItem(), stats, StatTypes.ENERGY, "energy");
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

        // Append a stat's current/max as a fraction with the stat name as its unit, onto the
        // list item the caller opened, e.g. "5 of 20 health".
        private static void Bar(MessageBuilder message, StatBlock stats, StatTypes stat, string unit) {
            int cur = (int)stats.GetStat(stat, StatDataTypes.CUR);
            int max = (int)stats.GetStat(stat, StatDataTypes.MAX);
            message.PushFraction(cur, max, unit);
        }

        // --- Read here ---

        private static void ReadHere(MessageBuilder message, HeroPC hero) {
            // Read-here follows the look cursor when it is active, so K examines wherever the
            // player has parked the cursor; otherwise it reads the hero's own tile.
            bool atCursor = LookCursor.Active;
            Vector2 pos = atCursor ? LookCursor.Position : hero.GetPos();

            message.Fragment(MapMasterScript.activeMap.GetName());
            message.ListItem().PushAbsoluteCoordinates(pos);
            message.ListItem();
            if (atCursor) {
                // Remote tile: defer to the cursor's own LOS-gated read (contents + offset).
                LookCursor.Read(message, hero);
            } else {
                // The hero's own tile: terrain + items, not the hero actor.
                TileDescriber.Contents(message, MapMasterScript.GetTile(pos), includeActor: false);
            }

            AppendShape(message, pos);
        }

        /// <summary>
        /// Append the shape of the surrounding walls — "north alcove", "vertical hallway", or, for
        /// an unrecognized pattern, the wall directions — as a list item. Silent when every side
        /// is open. The single place tile-surroundings are announced: classification lives in the
        /// pure <see cref="TileShapes"/>; this only feeds it the eight passabilities.
        /// </summary>
        private static void AppendShape(MessageBuilder message, Vector2 pos) {
            var passable = new bool[8];
            for (int i = 0; i < TileShapes.DirectionsCW.Length; i++) {
                (int dx, int dy) = TileShapes.DirectionsCW[i];
                passable[i] = !IsWallForShape(new Vector2(pos.x + dx, pos.y + dy));
            }

            string phrase = TileShapes.Describe(passable).Speak();
            if (phrase != null) {
                message.ListItem(phrase);
            }
        }

        // The shape is the static wall geometry around the hero, so a cell counts as a wall only
        // when the *terrain* is impassable — the map edge, a wall/void tile, or solid terrain.
        // Deliberately not actor-aware (a monster or NPC standing beside you must not reshape the
        // room; those are reported by scan and the log) and not visibility-gated: we read the true
        // geometry, so an adjacent tile hidden by the diagonal line-of-sight pinch still reads as
        // the wall it is rather than a phantom exit. Diagonal pinches that survive map generation
        // are walkable via corner-cutting, so an open diagonal is always a real exit.
        private static bool IsWallForShape(Vector2 p) {
            return TerrainQuery.IsImpassableWall(p);
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
                message.PushRelativeCoordinates(p.Pos - hp);
            }
        }
    }
}
