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
            if (body != null) {
                // Key by the text: a new/changed message re-announces; the same message,
                // re-rendered every tick, announces only once.
                builder.Announce(body, ctx => ctx.Message.Fragment(body));
            }

            if (IsTitleFlow()) {
                BuildOwnedChoices(builder);
            } else if (UIManagerScript.uiObjectFocus != null) {
                // In-game dialogue: the choices are legacy UIObjects in the neighbor graph and
                // the game drives navigation, so we passively mirror and follow its focus.
                GameMenuMirror.Build(builder, GameLabelReader.ReadLabel);
            } else if (body != null) {
                // Silent placeholder so the announcement (which needs a node) can ride along
                // when the game has not focused a button yet.
                builder.AddLabel(ControlId.Structural("dialogbody"), ctx => { });
            }
        }

        /// <summary>
        /// Title-screen dialogs (the new-game story intros, etc.) are pumped by
        /// <c>TitleScreenScript.Update</c>, not the in-game input chokepoint, so we own their
        /// input via the title hook. Build one node per actual dialog button straight from
        /// <c>dialogUIObjects</c> — never from the (briefly stale) <c>uiObjectFocus</c>, whose
        /// title-menu neighbors would otherwise leak in and get spoken — and claim input. The
        /// body rides the announcement; each button is a node, so a single-Continue dialog is
        /// one node ("body. Continue") and Enter passes the confirm through to the game.
        /// </summary>
        private static void BuildOwnedChoices(IOverlayBuilder builder) {
            System.Collections.Generic.List<UIManagerScript.UIObject> choices =
                UIManagerScript.dialogUIObjects;
            if (choices == null) {
                return;
            }

            foreach (UIManagerScript.UIObject choice in choices) {
                UIManagerScript.UIObject captured = choice;
                builder.AddItem(
                    ControlId.ForObject(choice),
                    new NodeVtable {
                        Label = ctx => {
                            string label = GameLabelReader.ReadLabel(captured);
                            if (!string.IsNullOrEmpty(label)) {
                                ctx.Message.Fragment(label);
                            }
                        },
                    }
                );
            }

            builder.CaptureInput();
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
