using TangledeepAccess.Audio;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// The per-turn "combat radar": at the start of each turn it builds ONE shared timeline carrying
    /// the wall-echo tones (when wall echo is on and the hero moved — the current F1 behavior) plus a
    /// monster-moved ping for every visible monster that moved this turn, and plays it as a single
    /// buffer. Sharing one timeline keeps a turn's spatial feedback to a single render/voice instead of
    /// stacking separate buffers.
    ///
    /// <para>Because monster moves can land on a turn the hero stood still (wait/attack-in-place), the
    /// radar now plays on more turns than wall echo alone did — that is the intended "the monster-move
    /// tracker joins the wall-tones radar" behavior. Edge-triggered on the game's turn counter
    /// (<c>GameMasterScript.turnNumber</c>); polled once per frame from the pump. Pure audio, no speech.</para>
    /// </summary>
    internal static class CombatRadar {
        private static bool _have;
        private static int _lastTurn;
        private static int _lastHeroX;
        private static int _lastHeroY;

        public static void PollTurn() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null) {
                _have = false; // out of play; re-arm so re-entry's first turn does not fire
                MonsterMoveTracker.Reset();
                return;
            }

            int turn = GameMasterScript.turnNumber;
            if (_have && turn == _lastTurn) {
                return; // same turn — already handled
            }

            bool first = !_have;
            _have = true;
            _lastTurn = turn;

            Vector2 p = hero.GetPos();
            int hx = (int)p.x;
            int hy = (int)p.y;
            bool heroMoved = !first && (hx != _lastHeroX || hy != _lastHeroY);
            _lastHeroX = hx;
            _lastHeroY = hy;

            var moved = MonsterMoveTracker.PollMovedThisTurn(hero, first);
            if (first) {
                return; // arm only: positions recorded, nothing to play on the first observed turn
            }

            var timeline = new GrainTimeline();
            bool any = false;

            // Wall tones keep their behavior: only when auto wall echo is on (Shift+F1) and the hero
            // actually moved this turn.
            if (NavAids.WallEchoAuto && heroMoved) {
                any |= WallEcho.AddTo(timeline);
            }

            // Monster-move pings join the same timeline regardless of the wall-echo toggle.
            any |= MonsterMovedSound.AddPings(timeline, moved);

            if (any) {
                TonePlayer.PlayTimeline(timeline);
            }
        }
    }
}
