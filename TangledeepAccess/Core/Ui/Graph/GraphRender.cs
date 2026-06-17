using System.Collections.Generic;

namespace TangledeepAccess.Ui.Graph {
    /// <summary>
    /// One built snapshot of a graph: the nodes (keyed by structural identity) and the
    /// control focus starts at when there is no prior position. Rebuilt every tick and
    /// thrown away — capture live state in the node callbacks, not here.
    /// </summary>
    public sealed class GraphRender {
        public ControlId StartKey;
        public readonly Dictionary<ControlId, GraphNode> Nodes =
            new Dictionary<ControlId, GraphNode>();

        /// <summary>
        /// Optional one-shot announcement identity. When this value-equatable key changes
        /// between ticks, the dispatcher speaks <see cref="Announce"/> once, prepended to
        /// (and independent of) the focus label — so text that just appeared on screen
        /// (dialog body, tutorial popup, level-up prompt) is read even when focus did not
        /// move. Null means "no announcement this build."
        /// </summary>
        public object AnnounceKey;

        /// <summary>Appends the announcement text to the message. Paired with <see cref="AnnounceKey"/>.</summary>
        public System.Action<OverlayCtx> Announce;

        /// <summary>
        /// The overlay explicitly wants to own keyboard input, even with a single node. The
        /// dispatcher's default rule captures input only for a multi-node tree (a degenerate
        /// single node is assumed unrepresentable and left to the game); a modal control we
        /// deliberately model as one node (e.g. a Continue dialog) sets this to claim input
        /// anyway. Surfaced separately as the dispatcher's <c>CapturesInputExplicitly</c> so a
        /// context-specific input hook can engage only for opted-in overlays.
        /// </summary>
        public bool ForceCapture;
    }

    /// <summary>
    /// The persistent cursor for a graph — the only thing that survives between renders
    /// (the dispatcher caches it per <see cref="OverlayId"/>). Holds where focus is, the
    /// last computed traversal order (for closest-survivor recovery), and a one-shot
    /// move request.
    /// </summary>
    public sealed class GraphState {
        /// <summary>The focused control's id (carries its Reference for tier-1 recovery). Null until first render.</summary>
        public ControlId CurKey;

        /// <summary>The down-right total order from the previous render. Null on first render.</summary>
        public List<ControlId> KeyOrder;

        /// <summary>If set, focus jumps here (silently) on the next render when present.</summary>
        public ControlId NextSuggestedMove;
    }
}
