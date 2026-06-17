using System;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Ui {
    /// <summary>
    /// A spoken overlay over some game (or mod) UI. <see cref="Build"/> declares the
    /// overlay's controls into the builder every tick; the framework reconciles focus and
    /// throws the tree away. Capture live state in the control callbacks — never cache the
    /// built tree. An overlay's <see cref="Id"/> keys its focus cache.
    /// </summary>
    public interface IUiOverlay {
        OverlayId Id { get; }

        void Build(IOverlayBuilder builder);
    }

    /// <summary>
    /// What an overlay declares its controls into. Backed by the graph builder, it offers
    /// two construction styles (do not mix them in one build):
    ///
    /// <para><b>Raw graph</b> (<see cref="AddNode"/>/<see cref="Connect"/>/<see cref="SetStart"/>)
    /// — the "assembly language": arbitrary nodes and directional edges. Use this to mirror
    /// a graph the game already defines (e.g. its UIObject neighbor graph).</para>
    ///
    /// <para><b>Menu sugar</b> (<see cref="StartRow"/>/<see cref="AddItem"/>/…) — rows give
    /// 2-D navigation (items within a row are left/right, rows are up/down); pass the same
    /// <paramref name="rowKey"/> to two rows for column navigation (up/down preserves the
    /// position within the row). This is sugar that the builder lowers to the raw graph.</para>
    /// </summary>
    public interface IOverlayBuilder {
        // --- Raw graph API ---

        /// <summary>Add a control node with a full vtable.</summary>
        IOverlayBuilder AddNode(ControlId id, NodeVtable vtable);

        /// <summary>Add a directional edge between two declared nodes.</summary>
        IOverlayBuilder Connect(ControlId from, GraphDir dir, ControlId to);

        /// <summary>Set the start node (focus lands here with no prior position). Defaults to the first node added.</summary>
        IOverlayBuilder SetStart(ControlId id);

        // --- Menu sugar ---

        /// <summary>Begin an explicit row. Optional key enables column nav between same-keyed rows.</summary>
        IOverlayBuilder StartRow(object rowKey = null);

        /// <summary>End the current explicit row.</summary>
        IOverlayBuilder EndRow();

        /// <summary>Add a control to the current/implicit row with a full vtable.</summary>
        IOverlayBuilder AddItem(ControlId id, NodeVtable vtable);

        /// <summary>A read-only control that just speaks <paramref name="label"/>.</summary>
        IOverlayBuilder AddLabel(ControlId id, Action<OverlayCtx> label);

        /// <summary>A control that speaks a label and runs <paramref name="onClick"/> on activation.</summary>
        IOverlayBuilder AddClickable(
            ControlId id,
            Action<OverlayCtx> label,
            Action<OverlayCtx, Modifiers> onClick
        );
    }
}
