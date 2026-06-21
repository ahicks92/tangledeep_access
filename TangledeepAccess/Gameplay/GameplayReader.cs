using System;
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
            ModInputAction? action = InputKeys.Query() ?? InputKeys.NavAids();
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

            // Navigation aids. Shift+Fn and Ctrl+Fn each route to the aid's own hook, which decides
            // what to do (toggle, fire once, …) and returns any line to speak.
            if (action.Kind == ModInputKind.NavAidShift) {
                speech.Speak(NavAids.OnShiftKey(action.Dx));
                return;
            }
            if (action.Kind == ModInputKind.NavAidCtrl) {
                speech.Speak(NavAids.OnCtrlKey(action.Dx));
                return;
            }

            // Alt+Y opens the current ally's command conversation (a side effect, like the nav aids),
            // so it lives here rather than in the pure read path. The dialog overlay then voices the
            // menu; we only speak when there is no ally to command.
            if (action.Kind == ModInputKind.OpenActiveAllyMenu) {
                speech.Speak(AllyReader.OpenActiveMenu());
                return;
            }

            speech.Speak(GameplayReader.Execute(action));
        }
    }

    /// <summary>
    /// Computes spoken answers to the player's on-demand free-play queries: "read here" (the hero's
    /// own tile and wall-shape, on S) and status. All reads re-query live game state at call time —
    /// no caching — and respect the hero's line of sight via <c>visibleTilesArray</c>. Runs on the
    /// Unity main thread from the per-frame pump; the input hook only requests the command. The
    /// exploration cursor's reads are separate (<see cref="ExplorationCursor"/>).
    /// </summary>
    internal static class GameplayReader {
        /// <summary>
        /// Compute the spoken answer for a free-play query, or null if not in play. The exploration
        /// cursor's own keys never reach here; <see cref="ExplorationCursorInputDrainer"/> realizes
        /// those. This handles only the query hotkeys. Repeat-last is realized in
        /// <see cref="GameplayInputDrainer"/>.
        /// </summary>
        public static MessageBuilder Execute(ModInputAction action) {
            var message = new MessageBuilder();

            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null) {
                return null;
            }

            switch (action.Kind) {
                case ModInputKind.ReadHere:
                    ReadHere(message, hero);
                    break;
                case ModInputKind.ReadStatus:
                    ReadStatus(message, hero);
                    break;
                case ModInputKind.CycleAllyNext:
                    AllyReader.Cycle(message, hero, 1);
                    break;
                case ModInputKind.CycleAllyPrev:
                    AllyReader.Cycle(message, hero, -1);
                    break;
                case ModInputKind.ReadActiveAlly:
                    AllyReader.ReadActive(message, hero);
                    break;
                case ModInputKind.ReadMonsters:
                    ReadMonsters(message, hero);
                    break;
                case ModInputKind.ReadTreasure:
                    ReadTreasure(message, hero);
                    break;
                case ModInputKind.ReadTerrain:
                    ReadTerrain(message, hero);
                    break;
                case ModInputKind.LogHistoryPrev:
                    GameEventLog.AppendOlder(message);
                    break;
                case ModInputKind.LogHistoryNext:
                    GameEventLog.AppendNewer(message);
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
            AppendStatuses(message, stats);
        }

        /// <summary>
        /// Append the HUD-visible, non-passive status effects on <paramref name="stats"/>, each as its
        /// own list item ("bad: " prefix for negatives, a turn count for temporary ones). Shared by the
        /// hero's own status read and the ally read so both speak effects identically. The filter
        /// mirrors the game's status bar: permanent job/feat passives are excluded.
        /// </summary>
        internal static void AppendStatuses(MessageBuilder message, StatBlock stats) {
            foreach (StatusEffect status in stats.GetAllStatuses()) {
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

        // --- In-sight reads (the H family) ---

        /// <summary>
        /// Every monster the hero can currently see, nearest first by Manhattan distance, each spoken as
        /// its short name and screen-relative offset ("slime 3 right, 2 up"), comma-separated. Name +
        /// distance only — no HP or attitude; that detail is the cursor's and scanner's job. Re-queries
        /// live and respects line of sight via <see cref="Visibility.VisibleNow"/>.
        /// </summary>
        private static void ReadMonsters(MessageBuilder message, HeroPC hero) {
            Vector2 hp = hero.GetPos();
            int hx = (int)hp.x;
            int hy = (int)hp.y;

            var found = new List<(string Name, int Dx, int Dy, int Dist)>();
            foreach (Actor a in MapMasterScript.activeMap.actorsInMap) {
                if (a == hero || ActorPresence.IsGone(a) || a.GetActorType() != ActorTypes.MONSTER) {
                    continue;
                }

                Vector2 p = a.GetPos();
                if (!Visibility.VisibleNow(p)) {
                    continue; // only what the hero can actually see right now
                }

                int dx = (int)p.x - hx;
                int dy = (int)p.y - hy;
                string name = GameLabelReader.Clean(a.displayName) ?? a.actorRefName;
                found.Add((name, dx, dy, Math.Abs(dx) + Math.Abs(dy)));
            }

            Emit(message, found, "no monsters in sight");
        }

        /// <summary>
        /// All treasure in line of sight (Ctrl+H) — powerups, ground items, gold piles, and breakable
        /// containers — in the same nearest-first name-and-offset form as <see cref="ReadMonsters"/>.
        /// Reuses the radar's <see cref="Surroundings.CollectVisible"/> snapshot (which already dedupes
        /// and classifies), keeping only the pickup (powerup/gold/item) and container voices.
        /// </summary>
        private static void ReadTreasure(MessageBuilder message, HeroPC hero) {
            Vector2 hp = hero.GetPos();
            int hx = (int)hp.x;
            int hy = (int)hp.y;

            var found = new List<(string Name, int Dx, int Dy, int Dist)>();
            foreach (Poi poi in Surroundings.CollectVisible(hero)) {
                if (poi.Category != RadarCategory.Powerup && poi.Category != RadarCategory.Container) {
                    continue;
                }

                int dx = (int)poi.Pos.x - hx;
                int dy = (int)poi.Pos.y - hy;
                found.Add((poi.Name, dx, dy, Math.Abs(dx) + Math.Abs(dy)));
            }

            Emit(message, found, "no treasure in sight");
        }

        /// <summary>
        /// Every terrain feature in line of sight (Alt+H), clustered into one entry per pool at its
        /// nearest cell (the same clustering the radar and scanner use), in the nearest-first
        /// name-and-offset form of <see cref="ReadMonsters"/>.
        /// </summary>
        private static void ReadTerrain(MessageBuilder message, HeroPC hero) {
            Vector2 hp = hero.GetPos();
            int hx = (int)hp.x;
            int hy = (int)hp.y;

            var found = new List<(string Name, int Dx, int Dy, int Dist)>();
            foreach (TerrainCluster cluster in TerrainFeature.Cluster(MapMasterScript.activeMap, Visibility.VisibleNow)) {
                TerrainCell near = cluster.NearestCellTo(hx, hy);
                int dx = near.X - hx;
                int dy = near.Y - hy;
                found.Add((TerrainFeature.Name(cluster.Kind), dx, dy, Math.Abs(dx) + Math.Abs(dy)));
            }

            Emit(message, found, "no terrain in sight");
        }

        // Speak an in-sight list nearest-first (Manhattan distance, ties broken by offset for a stable,
        // deterministic read), each entry as its name plus screen-relative offset; the empty phrase when
        // nothing matched. The shared tail of every H-family read.
        private static void Emit(MessageBuilder message, List<(string Name, int Dx, int Dy, int Dist)> found, string emptyPhrase) {
            if (found.Count == 0) {
                message.Fragment(emptyPhrase);
                return;
            }

            found.Sort((l, r) => l.Dist != r.Dist ? l.Dist - r.Dist : l.Dx != r.Dx ? l.Dx - r.Dx : l.Dy - r.Dy);

            foreach ((string name, int dx, int dy, int _) in found) {
                message.ListItem(name);
                message.PushRelativeCoordinates(new Vector2(dx, dy));
            }
        }

        // --- Read here ---

        private static void ReadHere(MessageBuilder message, HeroPC hero) {
            // The player's own tile (S). The exploration cursor is read separately via K — see
            // ExplorationCursor — so this always reads where the hero stands.
            Vector2 pos = hero.GetPos();

            message.Fragment(MapMasterScript.activeMap.GetName());
            message.ListItem().PushAbsoluteCoordinates(pos);
            message.ListItem();
            // The hero's own tile: terrain + items, not the hero actor.
            TileDescriber.Contents(message, MapMasterScript.GetTile(pos), includeActor: false);

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
        // room; those are reported by the scanner and the log) and not visibility-gated: we read the
        // true geometry, so an adjacent tile hidden by the diagonal line-of-sight pinch still reads
        // as the wall it is rather than a phantom exit. Diagonal pinches that survive map generation
        // are walkable via corner-cutting, so an open diagonal is always a real exit.
        private static bool IsWallForShape(Vector2 p) {
            return TerrainQuery.IsImpassableWall(p);
        }
    }
}
