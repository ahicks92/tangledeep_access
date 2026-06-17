using System;
using System.Collections.Generic;

namespace TangledeepAccess.Ui.Graph
{
    /// <summary>
    /// The flagship: a directed graph of controls (nodes) with up/down/left/right
    /// transitions, ported from Factorio Access's key-graph.lua. Everything is rebuilt
    /// from a render callback on each operation; focus persists via an external
    /// <see cref="GraphState"/>. Two invariants from the original carry over:
    ///
    /// <para><b>Down-right total order</b> (<see cref="ComputeOrder"/>): starting at the
    /// start node, go right until stuck, queueing each down; this visits a planar UI in
    /// reading order. The constraint is that down+right must reach every node (up/left may
    /// do anything). This order drives both focus recovery and (later) search.</para>
    ///
    /// <para><b>Focus recovery on rebuild</b> (<see cref="Reconcile"/>): if the focused
    /// control vanished, land the player on the nearest survivor rather than jumping to the
    /// start. The recovery is upgraded from string keys to <see cref="ControlId"/>'s two
    /// tiers, so it follows an object that moved (tier 1) or a logical control whose backing
    /// object was rebuilt (tier 2) before falling back to order-closeness.</para>
    ///
    /// One <see cref="Graph"/> wraps one render callback + state for a single use-session.
    /// The dispatcher constructs a fresh one each tick (state lives in its cache); tests
    /// drive one directly.
    /// </summary>
    public sealed class KeyGraph
    {
        private readonly Func<OverlayCtx, GraphRender> _renderCallback;
        private readonly GraphState _state;
        private readonly Controller _controller;
        private GraphRender _current;

        public KeyGraph(Func<OverlayCtx, GraphRender> renderCallback, GraphState state)
        {
            _renderCallback = renderCallback;
            _state = state;
            _controller = new Controller(state);
        }

        public GraphState State => _state;

        /// <summary>The most recently built render, or null if not yet rendered / closed.</summary>
        public GraphRender Current => _current;

        /// <summary>True if a callback asked the graph to close.</summary>
        public bool Closed => _controller.Closed;

        private sealed class Controller : IOverlayController
        {
            private readonly GraphState _state;
            public bool Closed;

            public Controller(GraphState state)
            {
                _state = state;
            }

            public void Close() => Closed = true;

            public void SuggestMove(ControlId key) => _state.NextSuggestedMove = key;
        }

        /// <summary>
        /// Rebuild the render and reconcile focus into it. Returns false if the callback
        /// closed the graph or produced nothing (the caller should treat that as closed).
        /// The render callback only declares controls; it must not append to the message.
        /// </summary>
        public bool Rerender(OverlayCtx ctx)
        {
            ctx.Controller = _controller;
            _current = _renderCallback(ctx);
            if (_controller.Closed || _current == null || _current.Nodes.Count == 0)
            {
                _current = null;
                return false;
            }

            Reconcile(_current, _state);
            return true;
        }

        /// <summary>
        /// Move focus from the cached <see cref="GraphState.CurKey"/> to a valid control in
        /// <paramref name="render"/>, then recompute the traversal order. Mirrors
        /// key-graph.lua's _rerender focus logic, with ControlId-aware recovery.
        /// </summary>
        public static void Reconcile(GraphRender render, GraphState state)
        {
            // Honor a pending suggested move first, if its target still exists.
            if (state.NextSuggestedMove != null)
            {
                GraphNode suggested;
                if (render.Nodes.TryGetValue(state.NextSuggestedMove, out suggested))
                    state.CurKey = suggested.Id;
                state.NextSuggestedMove = null;
            }

            ControlId old = state.CurKey;
            ControlId resolved = null;

            if (old != null)
            {
                // Tier 1: the same backing object, even if its structural key changed.
                if (old.Reference != null)
                {
                    foreach (KeyValuePair<ControlId, GraphNode> kv in render.Nodes)
                    {
                        if (kv.Value.Id.ReferenceMatches(old.Reference))
                        {
                            resolved = kv.Value.Id;
                            break;
                        }
                    }
                }

                // Tier 2: the same structural key, even if the backing object was rebuilt.
                if (resolved == null)
                {
                    GraphNode structural;
                    if (render.Nodes.TryGetValue(old, out structural))
                        resolved = structural.Id;
                }

                // Fallback: nearest survivor walking the previous order backward.
                if (resolved == null && state.KeyOrder != null)
                {
                    int oldIndex = IndexOf(state.KeyOrder, old);
                    if (oldIndex >= 0)
                    {
                        for (int i = oldIndex; i >= 0; i--)
                        {
                            GraphNode survivor;
                            if (render.Nodes.TryGetValue(state.KeyOrder[i], out survivor))
                            {
                                resolved = survivor.Id;
                                break;
                            }
                        }
                    }
                }
            }

            // Nothing matched (or first render): start at the start node.
            if (resolved == null)
            {
                GraphNode start;
                if (render.Nodes.TryGetValue(render.StartKey, out start))
                    resolved = start.Id;
                else
                    resolved = render.StartKey;
            }

            state.CurKey = resolved;
            state.KeyOrder = ComputeOrder(render);
        }

        /// <summary>
        /// The down-right total order: go right until stuck (recording each node), queue
        /// every down for a later pass, repeat. Ported from key-graph.lua:276-318.
        /// </summary>
        public static List<ControlId> ComputeOrder(GraphRender render)
        {
            var order = new List<ControlId>();
            var seen = new HashSet<ControlId>();
            var downFringe = new List<ControlId> { render.StartKey };

            int i = 0;
            while (i < downFringe.Count)
            {
                ControlId k = downFringe[i];
                while (!seen.Contains(k))
                {
                    seen.Add(k);
                    order.Add(k);

                    GraphNode n;
                    if (!render.Nodes.TryGetValue(k, out n))
                        break;

                    Transition d,
                        t;
                    if (n.Transitions.TryGetValue(GraphDir.Down, out d) && d != null)
                        downFringe.Add(d.Destination);

                    if (!n.Transitions.TryGetValue(GraphDir.Right, out t) || t == null)
                        break;

                    k = t.Destination;
                }

                i++;
            }

            return order;
        }

        private static int IndexOf(List<ControlId> order, ControlId key)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i].Equals(key))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Tier-1 focus sync from the game: if a node's backing object is
        /// <paramref name="reference"/>, move focus there. Returns true if focus moved.
        /// Speaks nothing — the caller decides whether to read the new focus.
        /// </summary>
        public bool FocusByReference(object reference)
        {
            if (reference == null || _current == null)
                return false;

            foreach (KeyValuePair<ControlId, GraphNode> kv in _current.Nodes)
            {
                if (kv.Value.Id.ReferenceMatches(reference))
                {
                    bool changed = _state.CurKey == null || !_state.CurKey.Equals(kv.Value.Id);
                    _state.CurKey = kv.Value.Id;
                    return changed;
                }
            }

            return false;
        }

        /// <summary>Append the focused control's label to the message (re-render first).</summary>
        public void ReadCurrentLabel(OverlayCtx ctx)
        {
            if (!Rerender(ctx))
                return;
            ReadLabelOf(_state.CurKey, ctx);
        }

        private void ReadLabelOf(ControlId key, OverlayCtx ctx)
        {
            GraphNode node;
            if (
                key != null
                && _current.Nodes.TryGetValue(key, out node)
                && node.Vtable.Label != null
            )
                node.Vtable.Label(ctx);
        }

        /// <summary>
        /// Move one step in <paramref name="dir"/>. On a real move, speaks the transition
        /// label (if any) then the destination label; at an edge, re-reads the current
        /// label. Mirrors key-graph.lua:_do_move.
        /// </summary>
        public void Move(OverlayCtx ctx, GraphDir dir)
        {
            if (!Rerender(ctx))
                return;

            GraphNode node;
            if (!_current.Nodes.TryGetValue(_state.CurKey, out node))
                return;

            Transition t;
            node.Transitions.TryGetValue(dir, out t);
            GraphNode newNode = node;
            if (t != null)
                _current.Nodes.TryGetValue(t.Destination, out newNode);

            if (newNode == null || newNode == node)
            {
                // Edge: nothing to move to. Re-read the current label.
                ReadLabelOf(_state.CurKey, ctx);
                return;
            }

            t.Label?.Invoke(ctx);
            t.PlaySound?.Invoke(ctx);
            if (newNode.Vtable.Label != null)
                newNode.Vtable.Label(ctx);
            _state.CurKey = newNode.Id;
        }

        /// <summary>
        /// Move as far as possible in <paramref name="dir"/> (home/end within the row or
        /// column), speaking the landing control. Mirrors key-graph.lua:_do_move_to_edge.
        /// </summary>
        public void MoveToEdge(OverlayCtx ctx, GraphDir dir)
        {
            if (!Rerender(ctx))
                return;

            ControlId current = _state.CurKey;
            bool moved = false;

            while (true)
            {
                GraphNode node;
                if (!_current.Nodes.TryGetValue(current, out node))
                    break;

                Transition t;
                if (!node.Transitions.TryGetValue(dir, out t) || t == null)
                    break;

                if (!_current.Nodes.ContainsKey(t.Destination))
                    break;

                current = t.Destination;
                moved = true;
            }

            if (moved)
                _state.CurKey = current;
            ReadLabelOf(_state.CurKey, ctx);
        }

        /// <summary>Activate the focused control (re-render first).</summary>
        public void Click(OverlayCtx ctx, Modifiers modifiers)
        {
            if (!Rerender(ctx))
                return;

            GraphNode node;
            if (!_current.Nodes.TryGetValue(_state.CurKey, out node))
                return;

            if (node.Vtable.OnClick != null)
                node.Vtable.OnClick(ctx, modifiers);
            else if (node.Vtable.Label != null)
                node.Vtable.Label(ctx); // default: re-read the label
        }
    }
}
