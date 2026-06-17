using System;
using System.Collections.Generic;

namespace TangledeepAccess.Ui.Graph {
    /// <summary>
    /// Builds a <see cref="GraphRender"/> from rows of controls, wiring the directional
    /// transitions. Ported from Factorio Access's menu.lua MenuBuilder. Items added without
    /// an explicit row become single-item rows (a plain vertical menu); explicit rows give
    /// horizontal groups. Two rows sharing a non-null key get column navigation: up/down
    /// preserves the position within the row instead of snapping to the first item.
    ///
    /// This is also the <see cref="IOverlayBuilder"/> an overlay's Build writes into.
    /// </summary>
    public sealed class GraphBuilder : IOverlayBuilder {
        private sealed class Entry {
            public ControlId Id;
            public NodeVtable Vtable;
        }

        private sealed class Row {
            public readonly List<Entry> Items = new List<Entry>();
            public object Key;
        }

        private sealed class RawEdge {
            public ControlId From;
            public GraphDir Dir;
            public ControlId To;
        }

        // Menu mode.
        private readonly List<Row> _rows = new List<Row>();
        private Row _currentRow;

        // Raw graph mode (mutually exclusive with menu mode).
        private readonly List<ControlId> _rawNodeOrder = new List<ControlId>();
        private readonly Dictionary<ControlId, NodeVtable> _rawNodes =
            new Dictionary<ControlId, NodeVtable>();
        private readonly List<RawEdge> _rawEdges = new List<RawEdge>();
        private ControlId _rawStart;

        // Explicit input ownership, independent of node count (orthogonal to construction style).
        private bool _forceCapture;

        public IOverlayBuilder AddNode(ControlId id, NodeVtable vtable) {
            if (id == null) {
                throw new ArgumentNullException(nameof(id));
            }

            if (vtable == null || vtable.Label == null) {
                throw new ArgumentException("A control must have a Label", nameof(vtable));
            }

            if (_rawNodes.ContainsKey(id)) {
                throw new InvalidOperationException("Duplicate control id: " + id);
            }

            _rawNodeOrder.Add(id);
            _rawNodes.Add(id, vtable);
            return this;
        }

        public IOverlayBuilder Connect(ControlId from, GraphDir dir, ControlId to) {
            if (from == null || to == null) {
                throw new ArgumentNullException(from == null ? nameof(from) : nameof(to));
            }

            _rawEdges.Add(
                new RawEdge {
                    From = from,
                    Dir = dir,
                    To = to,
                }
            );
            return this;
        }

        public IOverlayBuilder SetStart(ControlId id) {
            _rawStart = id;
            return this;
        }

        public IOverlayBuilder StartRow(object rowKey = null) {
            if (_currentRow != null) {
                throw new InvalidOperationException("Cannot start a row while another is open");
            }

            _currentRow = new Row { Key = rowKey };
            return this;
        }

        public IOverlayBuilder EndRow() {
            if (_currentRow == null) {
                throw new InvalidOperationException("No row to end");
            }

            if (_currentRow.Items.Count == 0) {
                throw new InvalidOperationException("Row cannot be empty");
            }

            _rows.Add(_currentRow);
            _currentRow = null;
            return this;
        }

        public IOverlayBuilder AddItem(ControlId id, NodeVtable vtable) {
            if (id == null) {
                throw new ArgumentNullException(nameof(id));
            }

            if (vtable == null || vtable.Label == null) {
                throw new ArgumentException("A control must have a Label", nameof(vtable));
            }

            var entry = new Entry { Id = id, Vtable = vtable };
            if (_currentRow != null) {
                _currentRow.Items.Add(entry);
            } else {
                var row = new Row();
                row.Items.Add(entry);
                _rows.Add(row);
            }

            return this;
        }

        public IOverlayBuilder AddLabel(ControlId id, Action<OverlayCtx> label) {
            return AddItem(id, new NodeVtable { Label = label });
        }

        public IOverlayBuilder CaptureInput() {
            _forceCapture = true;
            return this;
        }

        public IOverlayBuilder AddClickable(
            ControlId id,
            Action<OverlayCtx> label,
            Action<OverlayCtx, Modifiers> onClick
        ) {
            return AddItem(id, new NodeVtable { Label = label, OnClick = onClick });
        }

        /// <summary>
        /// Finalize into a render. Uses whichever construction style was used (raw graph or
        /// menu rows; mixing the two is an error). Returns null if empty so the engine treats
        /// a contentless build as "closed".
        /// </summary>
        public GraphRender Build() {
            bool hasRaw = _rawNodes.Count > 0;
            bool hasMenu = _rows.Count > 0 || _currentRow != null;
            if (hasRaw && hasMenu) {
                throw new InvalidOperationException(
                    "Cannot mix raw graph (AddNode/Connect) with menu rows in one build"
                );
            }

            GraphRender render = hasRaw ? BuildRaw() : BuildMenu();
            if (render != null) {
                render.ForceCapture = _forceCapture;
            }

            return render;
        }

        private GraphRender BuildRaw() {
            var render = new GraphRender();
            foreach (ControlId id in _rawNodeOrder) {
                render.Nodes.Add(id, new GraphNode { Id = id, Vtable = _rawNodes[id] });
            }

            foreach (RawEdge e in _rawEdges) {
                // Skip edges to/from controls that were never declared.
                if (render.Nodes.ContainsKey(e.From) && render.Nodes.ContainsKey(e.To)) {
                    render.Nodes[e.From].Transitions[e.Dir] = new Transition { Destination = e.To };
                }
            }

            render.StartKey = _rawStart ?? _rawNodeOrder[0];
            return render;
        }

        private GraphRender BuildMenu() {
            if (_currentRow != null) {
                throw new InvalidOperationException("Unclosed row - call EndRow()");
            }

            if (_rows.Count == 0) {
                return null;
            }

            var render = new GraphRender();

            // Create nodes; reject duplicate structural ids (they would collide in the map).
            foreach (Row row in _rows) {
                foreach (Entry item in row.Items) {
                    var node = new GraphNode { Id = item.Id, Vtable = item.Vtable };
                    if (render.Nodes.ContainsKey(item.Id)) {
                        throw new InvalidOperationException("Duplicate control id: " + item.Id);
                    }

                    render.Nodes.Add(item.Id, node);
                }
            }

            render.StartKey = _rows[0].Items[0].Id;

            for (int rowIdx = 0; rowIdx < _rows.Count; rowIdx++) {
                Row row = _rows[rowIdx];
                for (int pos = 0; pos < row.Items.Count; pos++) {
                    GraphNode node = render.Nodes[row.Items[pos].Id];

                    if (rowIdx > 0) {
                        node.Transitions[GraphDir.Up] = Edge(
                            VerticalTarget(row, _rows[rowIdx - 1], pos)
                        );
                    }

                    if (rowIdx < _rows.Count - 1) {
                        node.Transitions[GraphDir.Down] = Edge(
                            VerticalTarget(row, _rows[rowIdx + 1], pos)
                        );
                    }

                    if (pos > 0) {
                        node.Transitions[GraphDir.Left] = Edge(row.Items[pos - 1].Id);
                    }

                    if (pos < row.Items.Count - 1) {
                        node.Transitions[GraphDir.Right] = Edge(row.Items[pos + 1].Id);
                    }
                }
            }

            return render;
        }

        private static Transition Edge(ControlId destination) {
            return new Transition { Destination = destination };
        }

        /// <summary>
        /// Where vertical navigation from position <paramref name="pos"/> in
        /// <paramref name="from"/> lands in <paramref name="to"/>: the same position when
        /// the rows share a key (column nav), else the first item.
        /// </summary>
        private static ControlId VerticalTarget(Row from, Row to, int pos) {
            if (
                from.Key != null
                && to.Key != null
                && Equals(from.Key, to.Key)
                && pos < to.Items.Count
            ) {
                return to.Items[pos].Id;
            }

            return to.Items[0].Id;
        }
    }
}
