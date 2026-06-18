using TangledeepAccess.Ui;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The bottom-of-stack fallback for any legacy <c>uiObjectFocus</c> screen we have not written
    /// a bespoke overlay for. It deliberately does NOT mirror the game's focus graph — that rarely
    /// produced a usable reading and was the only thing in the framework that followed game focus
    /// and captured input per-tick. Instead it is a single owned node that announces the screen is
    /// unsupported and captures input like every other owned overlay, so navigation keys are
    /// swallowed deterministically while non-navigation keys (Escape, hotkeys) still pass through
    /// to the game (so the player can at least back out). Screens worth supporting get their own
    /// overlay, registered above this one.
    /// </summary>
    internal sealed class UnsupportedOverlay : IUiOverlay {
        public OverlayId Id => OverlayId.Unsupported;

        /// <summary>
        /// Active whenever there is a live focused UI element no overlay above claimed. Reads the
        /// focus watcher's published <see cref="Controls.FocusWatcher.CurrentFocus"/> — the single,
        /// edge-detected, validated focus — never the raw <c>uiObjectFocus</c>. The game does not
        /// null <c>uiObjectFocus</c> when a dialog closes (it leaves the reference dangling on the
        /// now-deactivated control), so reading it raw would latch this fallback on after every
        /// closed dialog and capture input during open gameplay; the watcher collapses that stale
        /// focus to null for us.
        /// </summary>
        public OverlayResult Handler() {
            return Controls.FocusWatcher.CurrentFocus != null
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            // One owned node: no graph to mirror, just an honest "not supported", and a no-op on
            // Enter so we never confirm a default on a screen we do not understand.
            builder.AddClickable(
                ControlId.Structural("unsupported"),
                ctx => ctx.Message.Fragment("Unsupported menu"),
                (ctx, mods) => { }
            );
            builder.CaptureInput();
        }
    }
}
