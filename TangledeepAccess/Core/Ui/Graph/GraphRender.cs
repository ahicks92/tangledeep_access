using System.Collections.Generic;

namespace TangledeepAccess.Ui.Graph
{
    /// <summary>
    /// One built snapshot of a graph: the nodes (keyed by structural identity) and the
    /// control focus starts at when there is no prior position. Rebuilt every tick and
    /// thrown away — capture live state in the node callbacks, not here.
    /// </summary>
    public sealed class GraphRender
    {
        public ControlId StartKey;
        public readonly Dictionary<ControlId, GraphNode> Nodes =
            new Dictionary<ControlId, GraphNode>();
    }

    /// <summary>
    /// The persistent cursor for a graph — the only thing that survives between renders
    /// (the dispatcher caches it per <see cref="OverlayId"/>). Holds where focus is, the
    /// last computed traversal order (for closest-survivor recovery), and a one-shot
    /// move request.
    /// </summary>
    public sealed class GraphState
    {
        /// <summary>The focused control's id (carries its Reference for tier-1 recovery). Null until first render.</summary>
        public ControlId CurKey;

        /// <summary>The down-right total order from the previous render. Null on first render.</summary>
        public List<ControlId> KeyOrder;

        /// <summary>If set, focus jumps here (silently) on the next render when present.</summary>
        public ControlId NextSuggestedMove;
    }
}
