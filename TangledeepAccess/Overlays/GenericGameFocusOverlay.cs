using TangledeepAccess.Focus;
using TangledeepAccess.Ui;

namespace TangledeepAccess.Overlays
{
    /// <summary>
    /// The bottom-of-stack fallback overlay. It mirrors the game's current legacy UIObject
    /// graph as a tree (via <see cref="GameMenuMirror"/>) and reads each control's label from
    /// its raw widget text (<see cref="GameLabelReader"/>) — reproducing the old FocusAnnouncer
    /// behavior through the new framework, for any screen we have not written a bespoke overlay
    /// for. Higher-priority overlays registered later override it for specific screens.
    ///
    /// <para>This is also what keeps the abstraction honest: it is just the lowest, dumbest
    /// handler. Nodes need not map to a game control, and the game's input loop never stops
    /// moving focus through ChangeUIFocus, so following that focus is permanent infrastructure.</para>
    /// </summary>
    internal sealed class GenericGameFocusOverlay : IUiOverlay
    {
        public OverlayId Id => OverlayId.GenericGameFocus;

        /// <summary>Active whenever the game reports a focused UI element.</summary>
        public OverlayResult Handler()
        {
            return UIManagerScript.uiObjectFocus != null
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder)
        {
            GameMenuMirror.Build(builder, GameLabelReader.ReadLabel);
        }
    }
}
