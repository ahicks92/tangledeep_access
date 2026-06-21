namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// The single answer to "is this actor really still here?" for navigation. Two ways an actor stops
    /// being a real feature while still sitting in <c>actorsInMap</c>:
    ///
    /// <list type="bullet">
    /// <item><c>destroyed</c> — Unity-level destroyed, awaiting cleanup.</item>
    /// <item>A <see cref="Destructible"/> with <c>isDestroyed</c> — opened/broken (e.g. a looted crate).
    /// The game sets this on removal and then makes the husk non-collidable, but the actor lingers in
    /// <c>actorsInMap</c> with <c>destroyed == false</c>. Filtering only on <c>destroyed</c> leaves it as
    /// a phantom object in the scanner and the F2 radar.</item>
    /// </list>
    ///
    /// <para>This is the one place that rule lives, so the scanner (<see cref="Scanner"/>) and the radar
    /// (<see cref="Surroundings"/>) agree. It mirrors the same <c>!destroyed &amp;&amp; !isDestroyed</c>
    /// pair <see cref="TerrainFeature.Is"/> already uses for terrain.</para>
    /// </summary>
    internal static class ActorPresence {
        /// <summary>True if the actor is null, Unity-destroyed, or a broken/opened destructible husk —
        /// i.e. not a real present feature. The hero is a separate, caller-specific exclusion.</summary>
        public static bool IsGone(Actor a) {
            return a == null || a.destroyed || (a is Destructible d && d.isDestroyed);
        }
    }
}
