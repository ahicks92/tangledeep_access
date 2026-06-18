namespace TangledeepAccess.Controls {
    /// <summary>
    /// Edge detector for the game's UI focus. The game fires no event when focus goes <em>stale</em>
    /// — a closing dialog leaves <c>uiObjectFocus</c> dangling on a now-deactivated control — so the
    /// focus source cannot rely on a push hook for the full story. Instead it reads the
    /// <em>validated</em> focus each frame (the live focused object, or null when there is none or it
    /// went stale) and hands that here. <see cref="Observe"/> returns true only when the validated
    /// value actually changed since the last call, so one event is emitted per edge — including the
    /// became-stale → null edge — rather than every frame.
    ///
    /// <para>Reference identity (the focused object instance persists while focused). The initial
    /// state is "no focus" (null), so the first observe of a real focus is an edge but a startup run
    /// of nulls is not. BCL-only and testable off-engine; main-thread only (polled from the pump),
    /// so no locking.</para>
    /// </summary>
    public sealed class FocusTracker {
        private object _last; // null = "no focus", the assumed initial state

        /// <summary>Record the current validated focus; return true iff it differs from the last
        /// observed value — the edge an event should be emitted on.</summary>
        public bool Observe(object current) {
            if (ReferenceEquals(current, _last)) {
                return false;
            }
            _last = current;
            return true;
        }

        /// <summary>Adopt <paramref name="current"/> as the baseline without signaling an edge, so a
        /// subsequent <see cref="Observe"/> of that same value reports no change. Used to cancel the
        /// echo when the mod itself wrote the game's focus — that write is not external news, but a
        /// later change the mod did NOT cause (e.g. focus going stale) still reads as an edge.</summary>
        public void Accept(object current) {
            _last = current;
        }
    }
}
