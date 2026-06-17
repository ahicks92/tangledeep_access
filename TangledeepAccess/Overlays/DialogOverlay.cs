using TangledeepAccess.Focus;
using TangledeepAccess.Ui;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// In-game modal dialogue (NPC conversation, yes/no prompts during play). The game drives
    /// navigation here through its own input, so this overlay is <b>passive</b>: it speaks the
    /// body once via the one-shot announcement channel (keyed by the text, so a new page
    /// re-announces) and mirrors the choices, following the game's focus.
    ///
    /// <para>The title-screen narrative dialogs are a different beast — pumped by a different
    /// input path and owned by us — so they live in <see cref="TitleDialogOverlay"/>. This
    /// overlay scopes itself to non-title dialogs.</para>
    /// </summary>
    internal sealed class DialogOverlay : IUiOverlay {
        public OverlayId Id => OverlayId.Dialog;

        public OverlayResult Handler() {
            bool inGame = GameMasterScript.gmsSingleton == null
                || !GameMasterScript.gmsSingleton.titleScreenGMS;
            return inGame && UIManagerScript.dialogBoxOpen
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            string body = ReadBody();
            if (body != null) {
                // Key by the text: a new/changed message re-announces; the same message,
                // re-rendered every tick, announces only once.
                builder.Announce(body, ctx => ctx.Message.Fragment(body));
            }

            if (UIManagerScript.uiObjectFocus != null) {
                GameMenuMirror.Build(builder, GameLabelReader.ReadLabel);
            } else if (body != null) {
                // Silent placeholder so the announcement (which needs a node) can ride along
                // when the game has not focused a button yet.
                builder.AddLabel(ControlId.Structural("dialogbody"), ctx => { });
            }
        }

        private static string ReadBody() {
            DialogBoxScript dbs = UIManagerScript.myDialogBoxComponent;
            TMPro.TextMeshProUGUI text = dbs != null ? dbs.GetDialogText() : null;
            return text != null ? GameLabelReader.Clean(text.text) : null;
        }
    }
}
