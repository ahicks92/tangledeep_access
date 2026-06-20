using TangledeepAccess.Audio;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Plays a warning cue at the start of any turn the hero begins standing on a telegraphed danger
    /// tile — a charging monster's incoming-attack square (<see cref="TileDescriber.HasDangerSquare"/>).
    /// The game gives no audible warning that you are about to be hit where you stand; this is the
    /// blind-player substitute for seeing the red warning square under your feet, and a prompt to move.
    ///
    /// <para>Edge-triggered on the game's turn counter (<c>GameMasterScript.turnNumber</c>, bumped once
    /// per hero turn), so the cue fires once per turn rather than every frame, and fires whether you
    /// stepped onto the square or a monster began charging it while you stood still. The first
    /// observation after entering play only arms the edge detector (no cue on spawn / load). Polled
    /// once per frame from the pump; pure audio, no speech.</para>
    /// </summary>
    internal static class DangerWatcher {
        private static bool _have;
        private static int _lastTurn;

        public static void PollTurn() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null) {
                _have = false; // out of play; re-arm so re-entry doesn't fire on the first turn back
                return;
            }

            int turn = GameMasterScript.turnNumber;
            if (_have && turn == _lastTurn) {
                return; // same turn — already handled
            }

            bool first = !_have;
            _have = true;
            _lastTurn = turn;
            if (first) {
                return; // arm only; don't fire on the first frame in play
            }

            MapTileData tile = MapMasterScript.GetTile(hero.GetPos());
            if (TileDescriber.HasDangerSquare(tile)) {
                CursorSounds.PlayDangerous();
            }
        }
    }
}
