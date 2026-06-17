using TangledeepAccess.Focus;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// Speaks the game's modal dialog box: NPC dialogue, the new-game narrative intros,
    /// yes/no prompts, and any other text-plus-choices popup. Two halves:
    ///
    /// <para><b>Body</b> — the message text (<c>txtDialogBoxMessage</c>) appears without a
    /// focus move, so it rides the framework's one-shot <see cref="IOverlayBuilder.Announce"/>
    /// channel, keyed by the text itself: it is spoken once when the dialog opens (and again
    /// only if the text changes, e.g. a multi-page conversation), prepended to the focused
    /// choice. The typewriter reveal only limits <c>maxVisibleCharacters</c>; the TMP
    /// <c>.text</c> already holds the full string, so we read the whole message immediately.</para>
    ///
    /// <para><b>Choices</b> — the buttons are legacy <c>UIObject</c>s wired into the standard
    /// neighbor graph (<c>dialogUIObjects</c>, focus on <c>uiObjectFocus</c>), so we mirror them
    /// with <see cref="GameMenuMirror"/> exactly like the generic fallback. A single-Continue
    /// dialog is one node (input passes through to the game's own confirm); a multi-choice
    /// prompt is several nodes (we drive navigation, Enter passes the confirm through).</para>
    ///
    /// Registered above the per-screen overlays because a dialog is modal — when one is open it
    /// owns the screen, including over character creation (whose intros are themselves dialogs).
    /// </summary>
    internal sealed class DialogOverlay : IUiOverlay {
        public OverlayId Id => OverlayId.Dialog;

        public OverlayResult Handler() {
            return UIManagerScript.dialogBoxOpen ? OverlayResult.Active(this) : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            string body = ReadBody();

            if (IsTitleFlow()) {
                // Narrative title dialogs (story intros, prompts): own them as a virtual control,
                // body folded into each node's label. The main menu is a separate overlay
                // (TitleMenuOverlay) that outranks us, so we never build it here.
                OwnedChoices.Build(builder, body);
                return;
            }

            // In-game dialogue: the game drives navigation, so passively mirror and follow its
            // focus, with the body on the one-shot announcement channel.
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

        private static bool IsTitleFlow() {
            return GameMasterScript.gmsSingleton != null && GameMasterScript.gmsSingleton.titleScreenGMS;
        }

        /// <summary>The dialog's full message text, color-stripped, or null if unavailable.</summary>
        private static string ReadBody() {
            DialogBoxScript dbs = UIManagerScript.myDialogBoxComponent;
            TMPro.TextMeshProUGUI text = dbs != null ? dbs.GetDialogText() : null;
            return text != null ? GameLabelReader.Clean(text.text) : null;
        }
    }
}
