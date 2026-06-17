using TangledeepAccess.Focus;
using TangledeepAccess.Ui;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The narrative title-screen dialogs (the new-game story intros, yes/no prompts). Pumped by
    /// <c>TitleScreenScript.Update</c>, not the in-game input chokepoint, so we own them: an
    /// unraveled vertical menu of the body (a fake control) plus one node per choice button, via
    /// <see cref="OwnedChoices"/>. Captures input so navigation is uniform regardless of how the
    /// game keys the specific dialog.
    ///
    /// <para>This is the catch-all for title dialogs: it claims any open dialog on the title
    /// screen, and the screen-specific title overlays (the main menu, feat select, save slots)
    /// are registered above it, so they win on their own screens and this handles what's left —
    /// the story/narrative dialogs.</para>
    /// </summary>
    internal sealed class TitleDialogOverlay : IUiOverlay {
        public OverlayId Id => OverlayId.TitleDialog;

        public OverlayResult Handler() {
            bool onTitle = GameMasterScript.gmsSingleton != null
                && GameMasterScript.gmsSingleton.titleScreenGMS;
            return onTitle && UIManagerScript.dialogBoxOpen
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            OwnedChoices.Build(builder, ReadBody());
        }

        private static string ReadBody() {
            DialogBoxScript dbs = UIManagerScript.myDialogBoxComponent;
            TMPro.TextMeshProUGUI text = dbs != null ? dbs.GetDialogText() : null;
            return text != null ? GameLabelReader.Clean(text.text) : null;
        }
    }
}
