using TangledeepAccess.Speech;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Warns the player when the hero's health crosses below a danger threshold — important in
    /// a permadeath game where a missed combat-log line could mean death. Polled once per frame
    /// from the pump; it fires once each time health drops past the threshold and re-arms only
    /// when health recovers back above it, so it does not nag every frame while low.
    ///
    /// <para>Two tiers (low, then critical); each is independent, so dropping straight to
    /// critical announces critical. Re-queries live HP every poll — no caching.</para>
    /// </summary>
    internal static class HealthWatcher {
        private const float LowFraction = 0.5f;
        private const float CriticalFraction = 0.25f;

        private static bool _belowLow;
        private static bool _belowCritical;

        /// <summary>A health warning to speak this frame, or null. Call every frame in play.</summary>
        public static string Poll() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted) {
                _belowLow = false;
                _belowCritical = false;
                return null;
            }

            int cur = (int)hero.myStats.GetStat(StatTypes.HEALTH, StatDataTypes.CUR);
            int max = (int)hero.myStats.GetStat(StatTypes.HEALTH, StatDataTypes.MAX);
            if (max <= 0) {
                return null;
            }

            float frac = (float)cur / max;
            string warning = null;

            // Critical takes priority over low when both newly cross.
            if (frac <= CriticalFraction) {
                if (!_belowCritical) {
                    warning = "Warning, health critical, " + cur + " of " + max;
                }
                _belowCritical = true;
                _belowLow = true;
            } else if (frac <= LowFraction) {
                if (!_belowLow) {
                    warning = "Health low, " + cur + " of " + max;
                }
                _belowLow = true;
                _belowCritical = false;
            } else {
                _belowLow = false;
                _belowCritical = false;
            }

            return warning;
        }
    }
}
