using System.Collections.Generic;
using TangledeepAccess.Audio;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Tracks every monster's tile position across turns and, each turn, reports which
    /// currently-visible monsters changed tile since last turn — the data behind the combat radar's
    /// monster-moved pings. Keyed by <c>actorUniqueID</c> so a monster keeps identity as it moves; the
    /// snapshot is rebuilt each turn so the dead/despawned drop out. Visibility-gated: a monster that
    /// moved out of sight is not pinged; one that moved into sight is (its tracked position changed and
    /// it is now visible). Engine-side (touches game types); the ping math is pure in
    /// <see cref="MonsterMovedCue"/>.
    /// </summary>
    internal static class MonsterMoveTracker {
        private static readonly Dictionary<int, Vector2> _last = new Dictionary<int, Vector2>();

        public static void Reset() {
            _last.Clear();
        }

        /// <summary>
        /// Diff current monster positions against last turn's snapshot. When <paramref name="arming"/>
        /// (the first turn in play), records positions and returns nothing — there is no "previous" to
        /// diff against. Always refreshes the snapshot. Offsets are relative to the hero and ordered
        /// nearest-first, so the closest threat pings first.
        /// </summary>
        public static List<MonsterPing> PollMovedThisTurn(HeroPC hero, bool arming) {
            var moved = new List<MonsterPing>();
            Map map = MapMasterScript.activeMap;
            Vector2 hp = hero.GetPos();
            var snapshot = new Dictionary<int, Vector2>();

            foreach (Actor a in map.actorsInMap) {
                Monster m = a as Monster;
                if (m == null || m.destroyed) {
                    continue;
                }

                Vector2 pos = m.GetPos();
                snapshot[m.actorUniqueID] = pos;
                if (arming) {
                    continue;
                }

                if (_last.TryGetValue(m.actorUniqueID, out Vector2 prev) && prev != pos && IsVisible(hero, pos)) {
                    moved.Add(new MonsterPing((int)pos.x - (int)hp.x, (int)pos.y - (int)hp.y));
                }
            }

            _last.Clear();
            foreach (KeyValuePair<int, Vector2> kv in snapshot) {
                _last[kv.Key] = kv.Value;
            }

            moved.Sort((p, q) => GridDirection.Steps(p.Dx, p.Dy).CompareTo(GridDirection.Steps(q.Dx, q.Dy)));
            return moved;
        }

        private static bool IsVisible(HeroPC hero, Vector2 pos) {
            if (!MapMasterScript.InBounds(pos)) {
                return false;
            }

            bool[,] visible = hero.visibleTilesArray;
            return visible != null && visible[(int)pos.x, (int)pos.y];
        }
    }
}
