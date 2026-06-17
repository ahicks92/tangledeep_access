using System.Collections.Generic;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Ui
{
    /// <summary>
    /// Drives the overlay system one tick at a time. Holds an ordered list of handlers
    /// (last registered = top of the stack) and, per <see cref="OverlayId"/>, an ephemeral
    /// focus cache (a <see cref="GraphState"/>). Each tick it finds the topmost active
    /// handler, builds its overlay, reconciles focus, applies any pending game-driven focus
    /// change, and returns the text to speak (or null).
    ///
    /// <para>Cache lifecycle implements the rule "when a handler that was driving a GUI goes
    /// back to inactive, clear its cache." One cache slot per id ⇒ one live instance per id
    /// ⇒ focus is preserved across ticks. <see cref="OverlayResultKind.Sleeping"/> counts as
    /// active, so a momentarily-unbuildable overlay keeps its position.</para>
    ///
    /// <para>This type is BCL-only. It invokes overlay/handler delegates that read game
    /// state, but never touches the engine itself, keeping it unit-testable off-engine.</para>
    /// </summary>
    public sealed class OverlayDispatcher
    {
        private readonly List<OverlayHandler> _handlers = new List<OverlayHandler>();
        private readonly Dictionary<OverlayId, GraphState> _cache =
            new Dictionary<OverlayId, GraphState>();

        private bool _hasActiveLast;
        private OverlayId _activeLast;
        private ControlId _lastSpoken;
        private object _pendingGameFocus;

        /// <summary>Register a handler. The last one registered sits at the top of the stack.</summary>
        public void Register(OverlayHandler handler)
        {
            _handlers.Add(handler);
        }

        /// <summary>
        /// Record that the game moved focus to <paramref name="reference"/> (e.g. a
        /// UIObject from the ChangeUIFocus hook). The next <see cref="Tick"/> tries to sync
        /// our cursor to the matching node. Recorded off the pump; applied on it.
        /// </summary>
        public void RecordGameFocus(object reference)
        {
            _pendingGameFocus = reference;
        }

        /// <summary>
        /// Run one frame. Returns the text to speak this tick, or null. The caller speaks it
        /// (keeping the speak-from-the-pump rule). Must be called on the main thread because
        /// overlay callbacks read live game state.
        /// </summary>
        public string Tick()
        {
            OverlayResult result = FindActive();

            // Determine which id (if any) is active this tick.
            bool hasActive = result != null && result.Kind != OverlayResultKind.Inactive;
            OverlayId activeId = hasActive ? result.Id : default(OverlayId);

            // Clear the cache of an id that was active last tick but is not active now (or
            // was replaced by a different id). Sleeping keeps the id active, so no clear.
            if (_hasActiveLast && (!hasActive || !activeId.Equals(_activeLast)))
            {
                _cache.Remove(_activeLast);
                _lastSpoken = null;
            }

            _hasActiveLast = hasActive;
            _activeLast = activeId;

            object gameFocus = _pendingGameFocus;
            _pendingGameFocus = null;

            if (!hasActive || result.Kind == OverlayResultKind.Sleeping)
                return null;

            return BuildAndSpeak(result.Overlay, gameFocus);
        }

        private OverlayResult FindActive()
        {
            for (int i = _handlers.Count - 1; i >= 0; i--)
            {
                OverlayResult r = _handlers[i]();
                if (r != null && r.Kind != OverlayResultKind.Inactive)
                    return r;
            }

            return OverlayResult.Inactive;
        }

        private string BuildAndSpeak(IUiOverlay overlay, object gameFocus)
        {
            GraphState state;
            if (!_cache.TryGetValue(overlay.Id, out state))
            {
                state = new GraphState();
                _cache[overlay.Id] = state;
            }

            var message = new MessageBuilder();
            var ctx = new OverlayCtx(message, Modifiers.None);
            var graph = new KeyGraph(c => BuildRender(overlay, c), state);

            // Build + reconcile focus into the new render.
            if (!graph.Rerender(ctx))
            {
                // The overlay built nothing this tick — treat as closed and drop its cache.
                _cache.Remove(overlay.Id);
                _hasActiveLast = false;
                _lastSpoken = null;
                return null;
            }

            // Sync to the game's focus if it moved (tier-1 reference match).
            if (gameFocus != null)
                graph.FocusByReference(gameFocus);

            // Speak only when the focused control actually changed (dedupe re-focus).
            ControlId cur = state.CurKey;
            if (cur == null || cur.Equals(_lastSpoken))
                return null;

            _lastSpoken = cur;

            GraphNode node;
            if (!graph.Current.Nodes.TryGetValue(cur, out node) || node.Vtable.Label == null)
                return null;

            node.Vtable.Label(ctx);
            return message.Build();
        }

        private static GraphRender BuildRender(IUiOverlay overlay, OverlayCtx ctx)
        {
            var builder = new GraphBuilder();
            overlay.Build(builder);
            return builder.Build();
        }
    }
}
