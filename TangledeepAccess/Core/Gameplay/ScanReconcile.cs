using System.Collections.Generic;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Selection recovery for the scanner across a snapshot rebuild — the engine-agnostic core of
    /// "rescan without losing the player's place." Given the stable key the player was on, the order it
    /// lived in, and the keys now available (nearest-first), returns the index in the new keys to select.
    ///
    /// <para>Mirrors the overlay <c>KeyGraph.Reconcile</c>, collapsed to the scanner's simpler identity:
    /// one stable-key tier (an actor's <c>actorUniqueID</c> or a terrain cluster's kind + canonical
    /// cell — no separate object-reference tier, because the key is already stable across rebuilds) plus
    /// the order-walk fallback. When the selected feature has vanished, walk the previous order backward
    /// from where it sat to the nearest surviving feature (the list is distance-sorted, so backward is
    /// toward the hero); only if nothing in that order survives does it fall back to the new nearest.</para>
    /// </summary>
    public static class ScanReconcile {
        /// <summary>The index in <paramref name="newKeys"/> to select, or -1 if it is empty.</summary>
        public static int Resolve(string priorKey, IReadOnlyList<string> priorOrder, IReadOnlyList<string> newKeys) {
            if (newKeys == null || newKeys.Count == 0) {
                return -1;
            }
            if (priorKey == null) {
                return 0; // never had a selection (first scan) — start on the nearest.
            }

            // Exact: the same feature is still present.
            int exact = IndexOf(newKeys, priorKey);
            if (exact >= 0) {
                return exact;
            }

            // Vanished: nearest survivor, walking the prior order backward from where the selection sat.
            if (priorOrder != null) {
                int at = IndexOf(priorOrder, priorKey);
                for (int i = at; i >= 0; i--) {
                    int survivor = IndexOf(newKeys, priorOrder[i]);
                    if (survivor >= 0) {
                        return survivor;
                    }
                }
            }

            // Nothing recoverable: the new nearest.
            return 0;
        }

        private static int IndexOf(IReadOnlyList<string> list, string key) {
            for (int i = 0; i < list.Count; i++) {
                if (list[i] == key) {
                    return i;
                }
            }

            return -1;
        }
    }
}
