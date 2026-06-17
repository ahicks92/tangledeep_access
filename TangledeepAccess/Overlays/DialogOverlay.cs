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
                // Title-flow dialogs: own them as a real virtual control (body in the label).
                BuildOwnedChoices(builder, body);
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

        /// <summary>
        /// Title-screen dialogs (the new-game story intros, etc.) are pumped by
        /// <c>TitleScreenScript.Update</c>, not the in-game input chokepoint, so we own their
        /// input via the title hook. Build one node per actual dialog button straight from
        /// <c>dialogUIObjects</c> — never from the (briefly stale) <c>uiObjectFocus</c>, whose
        /// title-menu neighbors would otherwise leak in and get spoken — and claim input.
        ///
        /// <para>Each node's label is the control's whole spoken content: the dialog body
        /// followed by that button's text. So a single-Continue intro is one node,
        /// "&lt;body&gt;. Continue", and the body is re-read whenever focus lands on it (a nav
        /// key-press, or the next page). The structural key folds in the body, so a new page is
        /// a new identity the framework re-reads; the button reference drives Enter through the
        /// game's own CursorConfirm. No announcement channel — the label is the content, which
        /// is what makes this a real virtual control rather than a side-channel.</para>
        /// </summary>
        private static void BuildOwnedChoices(IOverlayBuilder builder, string body) {
            System.Collections.Generic.List<UIManagerScript.UIObject> choices =
                UIManagerScript.dialogUIObjects;
            if (choices == null) {
                return;
            }

            for (int i = 0; i < choices.Count; i++) {
                UIManagerScript.UIObject captured = choices[i];
                // Key on (index, body): same page → same identity (read once); a new page
                // (changed body) → a new identity the framework re-reads.
                ControlId id = ControlId.Referenced(captured, "dialog:" + i + ":" + (body ?? ""));
                builder.AddItem(
                    id,
                    new NodeVtable {
                        Label = ctx => {
                            if (!string.IsNullOrEmpty(body)) {
                                ctx.Message.Fragment(body);
                            }

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
