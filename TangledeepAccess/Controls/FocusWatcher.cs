namespace TangledeepAccess.Controls {
    /// <summary>
    /// The game-focus input source: a non-keyboard producer in the input framework. The game fires
    /// no event when focus goes stale (a closing dialog leaves <c>UIManagerScript.uiObjectFocus</c>
    /// dangling on a now-deactivated control), so a push hook cannot tell the whole story. Instead
    /// this is <em>pulled</em> once per frame from the pump: it reads the validated focus — the live
    /// focused <c>UIObject</c>, or none when the focus is null or its GameObject is no longer active
    /// in the hierarchy — edge-detects via <see cref="FocusTracker"/>, and on a change publishes the
    /// new value as <see cref="CurrentFocus"/> and enqueues one <see cref="ModInputKind.FocusChanged"/>
    /// event.
    ///
    /// <para>This is the single authority on "what is focused." Consumers read
    /// <see cref="CurrentFocus"/> — the dispatcher's focus follow when it sees the FocusChanged
    /// event, overlay activation as the one validated value — never the raw <c>uiObjectFocus</c>,
    /// whose staleness is exactly the trap this centralizes. Replaces the old ChangeUIFocus Harmony
    /// postfix side-channel. Engine-touching, so it lives outside Core; main-thread only.</para>
    /// </summary>
    internal static class FocusWatcher {
        private static readonly FocusTracker Tracker = new FocusTracker();

        /// <summary>The last validated focus the watcher published: the live focused UIObject, or
        /// null when there is none. Updated only on an edge, so reading it per-frame still reflects
        /// purely event-driven state.</summary>
        public static UIManagerScript.UIObject CurrentFocus { get; private set; }

        /// <summary>Poll once per frame from the pump, before the input queue is drained, so the
        /// emitted event is realized the same frame. Emits FocusChanged when the validated focus
        /// changes, including when it becomes stale (→ null).</summary>
        public static void Poll() {
            UIManagerScript.UIObject live = Live(UIManagerScript.uiObjectFocus);
            if (Tracker.Observe(live)) {
                CurrentFocus = live;
                InputQueue.Enqueue(FocusSource.Instance, ModInputAction.Of(ModInputKind.FocusChanged));
            }
        }

        /// <summary>
        /// Tell the watcher the mod itself just wrote the game's focus to <paramref name="target"/>
        /// (the menu drainer's nav/confirm push). Publishes it as the current focus and seeds the
        /// tracker baseline so the next <see cref="Poll"/> does not re-observe our own write as an
        /// external FocusChanged edge. A later change the mod did NOT cause still emits — e.g. the
        /// focus going stale when a confirm closes the dialog, since that collapses to null.
        /// </summary>
        public static void NoticeSelfWrite(UIManagerScript.UIObject target) {
            UIManagerScript.UIObject live = Live(target);
            CurrentFocus = live;
            Tracker.Accept(live);
        }

        // The validated focus: the live focused object, or null when there is none or it is no
        // longer active in the hierarchy (a stale, torn-down control the game left dangling).
        private static UIManagerScript.UIObject Live(UIManagerScript.UIObject raw) {
            return raw != null && raw.gameObj != null && raw.gameObj.activeInHierarchy ? raw : null;
        }
    }
}
