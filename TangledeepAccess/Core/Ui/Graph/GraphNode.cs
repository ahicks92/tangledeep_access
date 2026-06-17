using System;
using System.Collections.Generic;

namespace TangledeepAccess.Ui.Graph
{
    /// <summary>The four navigable directions between graph nodes.</summary>
    public enum GraphDir
    {
        Up,
        Right,
        Down,
        Left,
    }

    /// <summary>
    /// The behaviors of a control. <see cref="Label"/> is required (it produces the spoken
    /// description); the rest are optional actions. Only the handlers needed now are
    /// present — the set grows as behaviors are defined (read-info, tooltips, etc.).
    /// </summary>
    public sealed class NodeVtable
    {
        /// <summary>Required. Append this control's spoken description to the message.</summary>
        public Action<OverlayCtx> Label;

        /// <summary>Optional. Primary activation; defaults to re-reading the label.</summary>
        public Action<OverlayCtx, Modifiers> OnClick;

        /// <summary>Optional. Secondary activation.</summary>
        public Action<OverlayCtx, Modifiers> OnRightClick;

        /// <summary>Optional. Read detailed info / tooltip about the control.</summary>
        public Action<OverlayCtx> OnReadInfo;

        /// <summary>Optional. Read positional / coordinate info.</summary>
        public Action<OverlayCtx> OnReadCoords;

        /// <summary>If true, the control is skipped by search.</summary>
        public bool ExcludeFromSearch;
    }

    /// <summary>A directed edge to another node, with optional transition speech/sound.</summary>
    public sealed class Transition
    {
        public ControlId Destination;

        /// <summary>Optional. Spoken only while crossing this edge (e.g. lane changes).</summary>
        public Action<OverlayCtx> Label;

        /// <summary>Optional. A sound to play on this edge.</summary>
        public Action<OverlayCtx> PlaySound;
    }

    /// <summary>A control: its identity, behaviors, and up to four directional transitions.</summary>
    public sealed class GraphNode
    {
        public ControlId Id;
        public NodeVtable Vtable;
        public readonly Dictionary<GraphDir, Transition> Transitions =
            new Dictionary<GraphDir, Transition>();
    }
}
