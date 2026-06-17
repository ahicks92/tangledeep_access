using TangledeepAccess.Focus;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// Builds an owned virtual control for a title-screen dialog box from its own buttons
    /// (<c>UIManagerScript.dialogUIObjects</c>) — never from the briefly-stale
    /// <c>uiObjectFocus</c>, whose neighbors would leak in. One node per button; the optional
    /// <paramref name="body"/> (a narrative dialog's text) is folded into each label so the
    /// label is the control's whole spoken content and the framework re-reads it on navigation.
    /// The node carries the button reference so Enter drives the game's own CursorConfirm, and
    /// the overlay claims input (the title hook then owns navigation). Shared by every owned
    /// title dialog (the main menu reads buttons only; narrative dialogs fold in the body).
    /// </summary>
    internal static class OwnedChoices {
        public static void Build(IOverlayBuilder builder, string body) {
            System.Collections.Generic.List<UIManagerScript.UIObject> choices =
                UIManagerScript.dialogUIObjects;
            if (choices == null) {
                return;
            }

            for (int i = 0; i < choices.Count; i++) {
                UIManagerScript.UIObject captured = choices[i];
                // Key on (index, body): same content → same identity (read once); a new page
                // (changed body) → a new identity the framework re-reads. body is "" for the menu.
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
    }
}
