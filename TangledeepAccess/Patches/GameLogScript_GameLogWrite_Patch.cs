using System;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Gameplay;
using TangledeepAccess.Util;
using UnityEngine;

namespace TangledeepAccess.Patches {
    /// <summary>
    /// Captures the game's turn-by-turn log for speech. GameLogWrite is the single sink every
    /// log path funnels through (end-of-turn queue, string-ref writes, paralyze messages), so
    /// one prefix covers nearly every gameplay event as text.
    ///
    /// <para>The prefix mirrors GameLogWrite's own write gate so we speak exactly the lines the
    /// player would see: skip the multiline parent call (its split pieces arrive as their own
    /// calls), honor the verbose-combat-log option, and suppress events sourced from an actor
    /// the hero cannot currently see (the game's <c>visibleTilesArray</c> line-of-sight check).
    /// forceWrite bypasses those gates in the game, so it does here too. We only enqueue; the
    /// per-frame pump speaks.</para>
    /// </summary>
    [HarmonyPatch(typeof(GameLogScript), "GameLogWrite")]
    internal static class GameLogScript_GameLogWrite_Patch {
        private static void Prefix(string content, Actor source, TextDensity td, bool forceWrite) {
            try {
                if (string.IsNullOrEmpty(content) || content.Contains("\n")) {
                    return; // null, or the multiline parent — pieces come as separate calls
                }

                if (!forceWrite && !PassesWriteGate(source, td)) {
                    return;
                }

                GameEventLog.Enqueue(GameLabelReader.Clean(content));
            } catch (Exception e) {
                Log.Warn("game-log capture failed: " + e.Message);
            }
        }

        // Mirrors GameLogWrite's filters: verbose option, and visibility of a non-hero source.
        private static bool PassesWriteGate(Actor source, TextDensity td) {
            if (!PlayerOptions.verboseCombatLog && td == TextDensity.VERBOSE) {
                return false;
            }

            if (source != null && source != GameMasterScript.heroPCActor) {
                Vector2 pos = source.GetPos();
                if (!MapMasterScript.InBounds(pos)) {
                    return false;
                }

                bool[,] visible = GameMasterScript.heroPCActor?.visibleTilesArray;
                if (visible != null && !visible[(int)pos.x, (int)pos.y]) {
                    return false;
                }
            }

            return true;
        }
    }
}
