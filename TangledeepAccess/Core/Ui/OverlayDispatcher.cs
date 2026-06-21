using System;
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
        // Menu keys that map to a read-only-style NodeVtable action on the focused control, each
        // resolving the vtable slot to invoke. The single registry for non-nav, non-confirm keys.
        private static readonly Dictionary<ModInputKind, Func<NodeVtable, Action<OverlayCtx>>> NodeActions =
            new Dictionary<ModInputKind, Func<NodeVtable, Action<OverlayCtx>>> {
                { ModInputKind.ReadInfo, vt => vt.OnReadInfo },
                { ModInputKind.ReadSecondary, vt => vt.OnReadSecondary },
                { ModInputKind.MarkFavorite, vt => vt.OnMarkFavorite },
                { ModInputKind.MarkTrash, vt => vt.OnMarkTrash },
                { ModInputKind.AssignHotbar, vt => vt.OnAssignHotbar },
            };

        private readonly List<OverlayHandler> _handlers = new List<OverlayHandler>();
        private readonly Dictionary<OverlayId, GraphState> _cache =
            new Dictionary<OverlayId, GraphState>();

        // Per-id last reported subidentity (for overlays implementing ISubIdentified). A change
        // while the id stays active is an in-place content swap (e.g. a dialog branch switch) and is
        // treated as a fresh open. Removed alongside the focus cache when the id goes inactive.
        private readonly Dictionary<OverlayId, string> _subId =
            new Dictionary<OverlayId, string>();

        private bool _hasActiveLast;
        private OverlayId _activeLast;
        private ControlId _lastSpoken;
        private object _pendingGameFocus;

        // The active auxiliary overlay session, or null. While set, ticks route to the aux (anchored
        // to a node of the main overlay) instead of the normal handler stack; see TickAux.
        private AuxSession _aux;

        // A live auxiliary overlay: the modal sub-overlay, plus the main overlay it is anchored to
        // (by id, for its focus cache) and the anchor node's key (whose OnAuxCommit fires on commit).
        private sealed class AuxSession {
            public IUiOverlay Main;
            public OverlayId MainId;
            public ControlId Anchor;
            public IUiOverlay Aux;
        }

        /// <summary>
        /// True when the active overlay declared that it owns keyboard input, via
        /// <see cref="IOverlayBuilder.CaptureInput"/>. A static property of the overlay, decided
        /// at build time — not inferred from node count. Both input hooks (title and in-game)
        /// read it to decide whether to route keys to us or leave them to the game. Updated each
        /// <see cref="Tick"/>; one frame stale is fine for a persistent menu.
        /// </summary>
        public bool CapturesInput { get; private set; }

        /// <summary>True while an auxiliary overlay (opened via <see cref="IOverlayController.OpenAuxiliary"/>)
        /// owns the tick. The input layer reads this to claim Escape as an aux-cancel only then,
        /// leaving a normal screen's Escape to the game.</summary>
        public bool AuxActive => _aux != null;

        /// <summary>Register a handler. The last one registered sits at the top of the stack.</summary>
        public void Register(OverlayHandler handler) {
            _handlers.Add(handler);
        }

        /// <summary>
        /// Record that the game's focus moved to <paramref name="reference"/> (a UIObject, or null
        /// when focus went away). Fed by the focus event source's realize — a validated, edge-
        /// detected signal — not by polling the raw game field. The next <see cref="Tick"/> with no
        /// nav command syncs our cursor to the matching node. Recorded off the pump; applied on it.
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
            // An open auxiliary overlay owns the tick: it drives its own focus while the main
            // overlay's cache is preserved underneath. See TickAux.
            if (_aux != null) {
                return TickAux(command);
            }

            OverlayResult result = FindActive();

            bool hasActive = result != null && result.Kind != OverlayResultKind.Inactive;
            OverlayId activeId = hasActive ? result.Id : default(OverlayId);

            // Clear the cache of an id that was active last tick but is not active now (or was
            // replaced). Sleeping keeps the id active, so no clear.
            if (_hasActiveLast && (!hasActive || !activeId.Equals(_activeLast))) {
                _cache.Remove(_activeLast);
                _subId.Remove(_activeLast);
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
            // An open auxiliary overlay owns the screen; describe it (with a banner naming the main
            // overlay it is anchored to) rather than the handler stack underneath.
            IUiOverlay overlay;
            OverlayId id;
            string banner = null;
            if (_aux != null) {
                overlay = _aux.Aux;
                id = _aux.Aux.Id;
                banner = "auxiliary over " + _aux.MainId + "\n";
            } else {
                OverlayResult result = FindActive();
                if (result == null || result.Kind == OverlayResultKind.Inactive) {
                    return "overlay: none\n";
                }
                if (result.Kind == OverlayResultKind.Sleeping) {
                    return "overlay: " + result.Id + " (sleeping - not rendering yet)\n";
                }

                overlay = result.Overlay;
                id = result.Id;
            }

            var ctx = new OverlayCtx(new MessageBuilder(), Modifiers.None);
            GraphRender render = BuildRender(overlay, ctx);

            GraphState state;
            _cache.TryGetValue(id, out state);
            ControlId current = state != null && state.CurKey != null ? state.CurKey : render.StartKey;

            var labels = new Dictionary<ControlId, string>();
            foreach (KeyValuePair<ControlId, GraphNode> kv in render.Nodes) {
                labels[kv.Key] = NodeLabel(kv.Value);
            }

            var sb = new StringBuilder();
            if (banner != null) {
                sb.Append(banner);
            }

            sb.Append("overlay: ").Append(id)
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

            // Generation check: an overlay whose content can change in place (a dialog as its
            // conversation advances) reports a subidentity. When it changes while the id stays active,
            // behave as a fresh open — reset focus to the start node and re-announce, ignoring any nav
            // command this frame so the new content wins over a same-frame keypress.
            if (overlay is ISubIdentified sub) {
                string now = sub.SubIdentity();
                string prev;
                bool had = _subId.TryGetValue(overlay.Id, out prev);
                _subId[overlay.Id] = now;
                if (had && !string.Equals(prev, now)) {
                    state.CurKey = null;
                    _lastSpoken = null;
                    command = null;
                }
            }

            var message = new MessageBuilder();
            var ctx = new OverlayCtx(message, Modifiers.None);
            var graph = new KeyGraph(c => BuildRender(overlay, c), state);

            if (!graph.Rerender(ctx)) {
                // The overlay built nothing this tick — treat as closed and drop its cache.
                _cache.Remove(overlay.Id);
                _subId.Remove(overlay.Id);
                _hasActiveLast = false;
                _lastSpoken = null;
                CapturesInput = false;
                return TickResult.Empty;
            }

            // Input ownership is what the overlay declared, not a function of node count.
            CapturesInput = graph.Current.ForceCapture;

            if (command.HasValue) {
                TickResult navResult = ApplyNav(graph, state, ctx, message, command.Value);

                // A node action may have opened an auxiliary overlay (OpenAuxiliary). Capture the
                // session, anchored to the node that opened it; subsequent ticks route to TickAux.
                if (graph.PendingAux != null) {
                    _aux = new AuxSession {
                        Main = overlay,
                        MainId = overlay.Id,
                        Anchor = graph.PendingAuxAnchor,
                        Aux = graph.PendingAux,
                    };
                    CapturesInput = true;
                }

                return navResult;
            }

            // An overlay that captures input (CaptureInput, i.e. ForceCapture) drives its own
            // focus — its start node, then our nav — so it must not chase the game's focus, or a
            // freshly-opened owned screen jumps off its start (e.g. a dialog's body node) onto
            // whatever button the game had focused. Non-capturing overlays (the save-slot screen)
            // keep following the game's focus, since the game drives their navigation.
            return Follow(graph, state, ctx, message, graph.Current.ForceCapture ? null : gameFocus);
        }

        // Drive one tick while an auxiliary overlay is open. The aux owns input and focus; the main
        // overlay underneath is rebuilt only to confirm it (and the anchor node) still exist.
        private TickResult TickAux(ModInputAction? command) {
            // Game focus is irrelevant while the (capturing) aux owns input; drop any pending edge.
            _pendingGameFocus = null;

            AuxSession aux = _aux;

            // 1. The main overlay must still be live and still contain the anchor node, or the aux is
            // orphaned: tear it down and fall back to normal handling.
            GraphState mainState;
            if (!_cache.TryGetValue(aux.MainId, out mainState)) {
                _aux = null;
                return Tick(command);
            }

            var probe = new KeyGraph(c => BuildRender(aux.Main, c), mainState);
            if (!probe.Rerender(new OverlayCtx(new MessageBuilder(), Modifiers.None))
                || !probe.Current.Nodes.ContainsKey(aux.Anchor)) {
                _aux = null;
                return Tick(command);
            }

            // 2. Build + process the aux overlay against its own cache slot.
            GraphState auxState;
            if (!_cache.TryGetValue(aux.Aux.Id, out auxState)) {
                auxState = new GraphState();
                _cache[aux.Aux.Id] = auxState;
            }

            var message = new MessageBuilder();
            var ctx = new OverlayCtx(message, Modifiers.None);
            var auxGraph = new KeyGraph(c => BuildRender(aux.Aux, c), auxState);
            if (!auxGraph.Rerender(ctx)) {
                // The aux built nothing this tick — treat as a cancel-close.
                return CloseAux(aux, mainState, null);
            }

            CapturesInput = auxGraph.Current.ForceCapture;
            _hasActiveLast = true;
            _activeLast = aux.Aux.Id;

            // Escape cancels the aux (no commit) and returns to the parent's anchor.
            if (command.HasValue && command.Value.Kind == ModInputKind.Cancel) {
                return CloseAux(aux, mainState, null);
            }

            TickResult result = command.HasValue
                ? ApplyNav(auxGraph, auxState, ctx, message, command.Value)
                : Follow(auxGraph, auxState, ctx, message, null);

            // A commit delivers a result to the anchor's OnAuxCommit; a plain close is a cancel.
            if (auxGraph.AuxCommitRequested) {
                return CloseAux(aux, mainState, auxGraph.AuxResult);
            }
            if (auxGraph.Closed) {
                return CloseAux(aux, mainState, null);
            }

            return result;
        }

        // Tear down the aux session and resume the main overlay focused on the anchor, silently. On a
        // commit, run the anchor node's OnAuxCommit against a fresh rebuild of the main (live state)
        // with the committed scalar in ctx.Arg; the transaction's own game-log line carries the
        // spoken result, so OnAuxCommit usually appends nothing.
        private TickResult CloseAux(AuxSession aux, GraphState mainState, int? commitResult) {
            _aux = null;
            _cache.Remove(aux.Aux.Id);
            _subId.Remove(aux.Aux.Id);

            var result = new TickResult();

            if (commitResult.HasValue) {
                var message = new MessageBuilder();
                var ctx = new OverlayCtx(message, Modifiers.None) { Arg = commitResult.Value };
                var mainGraph = new KeyGraph(c => BuildRender(aux.Main, c), mainState);
                if (mainGraph.Rerender(ctx)) {
                    GraphNode anchor;
                    if (mainGraph.Current.Nodes.TryGetValue(aux.Anchor, out anchor)
                        && anchor.Vtable.OnAuxCommit != null) {
                        anchor.Vtable.OnAuxCommit(ctx);
                    }
                }

                result.Message = message;
            }

            // Resume the main overlay on the anchor next tick, without re-announcing it.
            mainState.NextSuggestedMove = aux.Anchor;
            _hasActiveLast = true;
            _activeLast = aux.MainId;
            _lastSpoken = aux.Anchor;
            CapturesInput = true;
            return result;
        }

        private TickResult ApplyNav(
            KeyGraph graph,
            GraphState state,
            OverlayCtx ctx,
            MessageBuilder message,
            ModInputAction command
        ) {
            var result = new TickResult();

            // Cancel is meaningful only to an open auxiliary overlay (handled in TickAux); reaching
            // here means no aux is up, so it is a no-op rather than falling through to a stray move.
            if (command.Kind == ModInputKind.Cancel) {
                return result;
            }

            // Carry the command's integer payloads (e.g. the AssignHotbar slot in Dx and bank in Dy)
            // to the node action.
            ctx.Arg = command.Dx;
            ctx.Bank = command.Dy;

            // Non-nav, non-confirm keys map to a NodeVtable action on the focused control (read
            // info, mark favorite/trash, assign-to-hotbar, …). One generic path: invoke the selected
            // action, never move or activate. Adding a future action key is a ModInputKind + vtable
            // slot + one entry here + a keymap line.
            Func<NodeVtable, Action<OverlayCtx>> selector;
            if (NodeActions.TryGetValue(command.Kind, out selector)) {
                graph.InvokeNodeAction(ctx, selector);
                result.Message = message;
                return result;
            }

            if (command.Kind == ModInputKind.Confirm || command.Kind == ModInputKind.DangerousConfirm) {
                ControlId cur = state.CurKey;
                GraphNode node = null;
                if (cur != null) {
                    graph.Current.Nodes.TryGetValue(cur, out node);
                }

                bool hasModHandler = node != null && node.Vtable.OnClick != null;

                if (hasModHandler) {
                    // Mod-side control: run its handler; the game is not involved. Ctrl+Enter
                    // (DangerousConfirm) was genuinely held with Ctrl, so report Modifiers.Control —
                    // a node gates a confirmation-required action on it.
                    Modifiers mods = command.Kind == ModInputKind.DangerousConfirm
                        ? new Modifiers { Control = true }
                        : Modifiers.None;
                    graph.Click(ctx, mods);
                    result.Message = message;
                } else {
                    // Game-backed pass-through: let the caller confirm it through the game.
                    result.Activated = true;
                    result.FocusReference = cur?.Reference;
                }

                return result;
            }

            // A value control (a slider) intercepts horizontal input to adjust its value instead of
            // moving focus: left/right nudge, Shift (MoveToEdge) takes a coarse step. Vertical input
            // always navigates. The handler plays its own sound and speaks the new value, so this is
            // not reported as a move.
            if (command.Dx != 0 && command.Dy == 0
                && graph.TryHorizontalAdjust(ctx, command.Dx, command.Kind == ModInputKind.MoveToEdge)) {
                result.Message = message;
                return result;
            }

            // The only kinds left are the two directional ones: Move steps one control, MoveToEdge
            // skips as far as the row/column reaches. Both speak the landing label into the message
            // and report a real move the same way.
            ControlId prev = state.CurKey;
            GraphDir dir = ToDir(command.Dx, command.Dy);
            if (command.Kind == ModInputKind.MoveToEdge) {
                graph.MoveToEdge(ctx, dir);
            } else {
                graph.Move(ctx, dir);
            }
            ControlId now = state.CurKey;

            result.Moved = prev == null || !prev.Equals(now);
            result.FocusReference = now?.Reference;
            result.Message = message;
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

            return new TickResult { Message = message };
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
