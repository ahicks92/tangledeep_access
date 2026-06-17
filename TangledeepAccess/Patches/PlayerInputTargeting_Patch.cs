using System;
using HarmonyLib;
using TangledeepAccess.Gameplay;
using TangledeepAccess.Util;
using UnityEngine;

namespace TangledeepAccess.Patches {
    /// <summary>
    /// Speaks the ranged-targeting cursor. The game calls
    /// <c>PlayerInputTargetingManager.UpdateCurrentTargetingInformation(location, isGoodTile)</c>
    /// each time the target cursor moves while aiming a ranged weapon or a point/area ability;
    /// this postfix hands the targeted tile to <see cref="TargetingReader"/> for the pump to
    /// speak (tile contents, direction/distance, valid/invalid). The method is only called while
    /// targeting is active, so the postfix needs no extra gate; the reader dedupes by tile.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInputTargetingManager), "UpdateCurrentTargetingInformation")]
    internal static class PlayerInputTargeting_Patch {
        private static void Postfix(Vector2 location, bool isGoodTile) {
            try {
                TargetingReader.Aim(location, isGoodTile);
            } catch (Exception e) {
                Log.Warn("targeting capture failed: " + e.Message);
            }
        }
    }
}
