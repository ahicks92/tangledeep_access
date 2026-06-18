using System.Collections.Generic;
using System.Text;
using TangledeepAccess.Controls;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Ui {
    /// <summary>
    /// Drives the overlay system one tick at a time. Holds an ordered list of handlers
    /// (last registered = top of the stack) and, per <see cref="OverlayId"/>, an ephemeral
    /// focus cache (a <see cref="GraphState"/>). Each tick it finds the topmost active
    /// handler, builds its overlay, reconciles focus, and either applies a player navigation
    /// command (our own key handling) or follows the game's focus.
    ///
    /// <para>Cache lifecycle implements the rule "when a handler that was driving a GUI goes
    /// back to inactive, clear its cache." One cache slot per id ⇒ one live instance per id
    /// ⇒ focus is preserved across ticks. <see cref="OverlayResultKind.Sleeping"/> counts as
    /// active, so a momentarily-unbuildable overlay keeps its position.</para>
    ///
    /// <para>This type is BCL-only. It invokes overlay/handler delegates that read game
    /// state, but never touches the engine itself, keeping it unit-testable off-engine.</para>
    /// </summary>
    public sealed class OverlayDispatcher {
        private readonly List<OverlayHandler> _handlers = new List<OverlayHandler>();
        private readonly Dictionary<OverlayId, GraphState> _cache =
            new Dictionary<OverlayId, GraphState>();

        private bool _hasActiveLast;
        private OverlayId _activeLast;
        private ControlId _lastSpoken;
        private object _pendingGameFocus;

        /// <summary>
        /// True when the active overlay declared that it owns keyboard input, via
        /// <see cref="IOverlayBuilder.CaptureInput"/>. A static property of the overlay, decided
        /// at build time — not inferred from node count. Both input hooks (title and in-game)
        /// read it to decide whether to route keys to us or leave them to the game. Updated each
        /// <see cref="Tick"/>; one frame stale is fine for a persistent menu.
        /// </summary>
        public bool CapturesInput { get; private set; }

        /// <summary>Register a handler. The last one registered sits at the top of the stack.</summary>
        public void Register(OverlayHandler handler) {
            _handlers.Add(handler);
        }

        /// <summary>
        /// Record that the game moved focus to <paramref name="reference"/> (e.g. a
        /// UIObject from the ChangeUIFocus hook). The next <see cref="Tick"/> with no nav
        /// command syncs our cursor to the matching node. Recorded off the pump; applied on it.
        /// </summary>
        public void RecordGameFocus(object reference) {
            _pendingGameFocus = reference;
        }

        /// <summary>
        /// Run one frame, optionally applying a player navigation command. Returns what the
        /// caller should speak / sound / focus this tick. Must be called on the main thread
        /// because overlay callbacks read live game state.
        /// </summary>
        public TickResult Tick(ModInputAction? command = null) {
            OverlayResult result = FindActive();

            bool hasActive = result != null && result.Kind != OverlayResultKind.Inactive;
            OverlayId activeId = hasActive ? result.Id : default(OverlayId);

            // Clear the cache of an id that was active last tick but is not active now (or was
            // replaced). Sleeping keeps the id active, so no clear.
            if (_hasActiveLast && (!hasActive || !activeId.Equals(_activeLast))) {
                _cache.Remove(_activeLast);
                _lastSpoken = null;
            }

            _hasActiveLast = hasActive;
            _activeLast = activeId;

            object gameFocus = _pendingGameFocus;
            _pendingGameFocus = null;

            if (!hasActive || result.Kind == OverlayResultKind.Sleeping) {
                CapturesInput = false;
                return TickResult.Empty;
            }

            return BuildAndProcess(result.Overlay, gameFocus, command);
        }

        /// <summary>
        /// Dev-introspection: describe the currently active overlay as the mod sees it — the
        /// built graph's nodes with their spoken labels, the current cursor, and the directional
        /// links. Read-only (builds a throwaway render; does not disturb the live cache). Must run
        /// on the main thread, since node labels read live game state.
        /// </summary>
        internal string Describe() {
            OverlayResult result = FindActive();
            if (result == null || result.Kind == OverlayResultKind.Inactive) {
                return "overlay: none\n";
            }
            if (result.Kind == OverlayResultKind.Sleeping) {
                return "overlay: " + result.Id + " (sleeping - not rendering yet)\n";
            }

            var ctx = new OverlayCtx(new MessageBuilder(), Modifiers.None);
            GraphRender render = BuildRender(result.Overlay, ctx);

            GraphState state;
            _cache.TryGetValue(result.Overlay.Id, out state);
            ControlId current = state != null && state.CurKey != null ? state.CurKey : render.StartKey;

            var labels = new Dictionary<ControlId, string>();
            foreach (KeyValuePair<ControlId, GraphNode> kv in render.Nodes) {
                labels[kv.Key] = NodeLabel(kv.Value);
            }

            var sb = new StringBuilder();
            sb.Append("overlay: ").Append(result.Id)
                .Append(" (nodes=").Append(render.Nodes.Count)
                .Append(", capturesInput=").Append(render.ForceCapture).Append(")\n");
            foreach (KeyValuePair<ControlId, GraphNode> kv in render.Nodes) {
                bool isCurrent = current != null && current.Equals(kv.Key);
                sb.Append(isCurrent ? "> " : "  ").Append('"').Append(labels[kv.Key] ?? "").Append('"');
                foreach (KeyValuePair<GraphDir, Transition> tr in kv.Value.Transitions) {
                    string destLabel;
                    labels.TryGetValue(tr.Value.Destination, out destLabel);
                    sb.Append("  ").Append(tr.Key).Append("->\"").Append(destLabel ?? "?").Append('"');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string NodeLabel(GraphNode node) {
            if (node == null || node.Vtable == null || node.Vtable.Label == null) {
                return null;
            }
            var message = new MessageBuilder();
            node.Vtable.Label(new OverlayCtx(message, Modifiers.None));
            return message.Build();
        }

        private OverlayResult FindActive() {
            for (int i = _handlers.Count - 1; i >= 0; i--) {
                OverlayResult r = _handlers[i]();
                if (r != null && r.Kind != OverlayResultKind.Inactive) {
                    return r;
                }
            }

            return OverlayResult.Inactive;
        }

        private TickResult BuildAndProcess(
            IUiOverlay overlay,
            object gameFocus,
            ModInputAction? command
        ) {
            GraphState state;
            if (!_cache.TryGetValue(overlay.Id, out state)) {
                state = new GraphState();
                _cache[overlay.Id] = state;
            }

            var message = new MessageBuilder();
            var ctx = new OverlayCtx(message, Modifiers.None);
            var graph = new KeyGraph(c => BuildRender(overlay, c), state);

            if (!graph.Rerender(ctx)) {
                // The overlay built nothing this tick — treat as closed and drop its cache.
                _cache.Remove(overlay.Id);
                _hasActiveLast = false;
                _lastSpoken = null;
                CapturesInput = false;
                return TickResult.Empty;
            }

            // Input ownership is what the overlay declared, not a function of node count.
            CapturesInput = graph.Current.ForceCapture;

            if (command.HasValue) {
                return ApplyNav(graph, state, ctx, message, command.Value);
            }

            // An overlay that captures input (CaptureInput, i.e. ForceCapture) drives its own
            // focus — its start node, then our nav — so it must not chase the game's focus, or a
            // freshly-opened owned screen jumps off its start (e.g. a dialog's body node) onto
            // whatever button the game had focused. Non-capturing overlays (the save-slot screen)
            // keep following the game's focus, since the game drives their navigation.
            return Follow(graph, state, ctx, message, graph.Current.ForceCapture ? null : gameFocus);
        }

        private TickResult ApplyNav(
            KeyGraph graph,
            GraphState state,
            OverlayCtx ctx,
            MessageBuilder message,
            ModInputAction command
        ) {
            var result = new TickResult();

            if (command.Kind == ModInputKind.Confirm) {
                ControlId cur = state.CurKey;
                GraphNode node = null;
                if (cur != null) {
                    graph.Current.Nodes.TryGetValue(cur, out node);
                }

                bool hasModHandler = node != null && node.Vtable.OnClick != null;

                if (hasModHandler) {
                    // Mod-side control: run its handler; the game is not involved.
                    graph.Click(ctx, Modifiers.None);
                    result.Speak = message.Build();
                } else {
                    // Game-backed pass-through: let the caller confirm it through the game.
                    result.Activated = true;
                    result.FocusReference = cur?.Reference;
                }

                return result;
            }

            ControlId prev = state.CurKey;
            graph.Move(ctx, ToDir(command.Dx, command.Dy));
            ControlId now = state.CurKey;

            result.Moved = prev == null || !prev.Equals(now);
            result.FocusReference = now?.Reference;
            result.Speak = message.Build();
            _lastSpoken = now;
            return result;
        }

        private TickResult Follow(
            KeyGraph graph,
            GraphState state,
            OverlayCtx ctx,
            MessageBuilder message,
            object gameFocus
        ) {
            // Sync to the game's focus if it moved (tier-1 reference match).
            if (gameFocus != null) {
                graph.FocusByReference(gameFocus);
            }

            // Speak the focus label only when focus changed.
            ControlId cur = state.CurKey;
            if (cur == null || cur.Equals(_lastSpoken)) {
                return TickResult.Empty;
            }

            _lastSpoken = cur;
            GraphNode node;
            if (graph.Current.Nodes.TryGetValue(cur, out node) && node.Vtable.Label != null) {
                node.Vtable.Label(ctx);
            }

            return new TickResult { Speak = message.Build() };
        }

        // A Move's (dx, dy) — +x east, +y north — to a menu graph direction. Menu nav only ever
        // sends orthogonals, so the diagonal case never arises; north/south win the tie defensively.
        private static GraphDir ToDir(int dx, int dy) {
            if (dy > 0) {
                return GraphDir.Up;
            }
            if (dy < 0) {
                return GraphDir.Down;
            }
            if (dx > 0) {
                return GraphDir.Right;
            }

            return GraphDir.Left;
        }

        private static GraphRender BuildRender(IUiOverlay overlay, OverlayCtx ctx) {
            var builder = new GraphBuilder();
            overlay.Build(builder);
            return builder.Build();
        }
    }
}
