using TangledeepAccess.Ui;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The title-screen main menu (NEW GAME / CONTINUE / QUIT / COMMUNITY / MANAGE DATA). It is
    /// a dialog box like the narrative dialogs, but it is its own screen, so it gets its own
    /// overlay rather than a heuristic inside <see cref="DialogOverlay"/> guessing menu-vs-dialog.
    /// Identified concretely by the creation stage being <c>TITLESCREEN</c> (the menu's stage;
    /// the narrative/creation dialogs run at other stages). Registered above
    /// <see cref="DialogOverlay"/> so it wins while both are nominally active on the menu.
    ///
    /// <para>Reads the menu buttons only — the menu's dialog "body" is a placeholder ("(ok)"),
    /// not content — and owns input, so navigation reads each button and Enter confirms it
    /// through the game (the title hook drives it, with held-key suppression so a held arrow
    /// cannot double-step).</para>
    /// </summary>
    internal sealed class TitleMenuOverlay : IUiOverlay {
        public OverlayId Id => OverlayId.TitleMenu;

        public OverlayResult Handler() {
            bool onTitle = GameMasterScript.gmsSingleton != null
                && GameMasterScript.gmsSingleton.titleScreenGMS;
            return onTitle
                && TitleScreenScript.CreateStage == CreationStages.TITLESCREEN
                && UIManagerScript.dialogBoxOpen
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            OwnedChoices.Build(builder, null);
        }
    }
}
