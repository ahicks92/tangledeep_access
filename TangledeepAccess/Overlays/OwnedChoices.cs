using TangledeepAccess.Focus;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// Unravels a title-screen dialog box into an owned vertical menu and captures input, so we
    /// navigate it the same way regardless of how the game keys that particular dialog.
    ///
    /// <para>The dialog's <paramref name="body"/> text (if any) becomes its own <b>fake control</b>
    /// — a synthetic, read-only node with no game backing — placed first, rather than being folded
    /// into every choice's label. Each real button (from <c>dialogUIObjects</c>, never the
    /// briefly-stale <c>uiObjectFocus</c>) follows as its own node carrying its <c>UIObject</c>
    /// reference, so Enter drives the game's own CursorConfirm on the right choice. The result is a
    /// clean list — "story text", then "Continue"; or "Delete save?", then "Yes", "No" — with no
    /// instruction repeated on each item. The body node is read on appearance (it is the start)
    /// and re-readable by navigating to it.</para>
    ///
    /// Used by the title menu (no body → buttons only) and the narrative title dialogs (body +
    /// choices).
    /// </summary>
    internal static class OwnedChoices {
        public static void Build(IOverlayBuilder builder, string body) {
            if (!string.IsNullOrEmpty(body)) {
                // Fake read-only control: navigable and re-readable, but a no-op on activate so it
                // never confirms a default (important for a yes/no prompt). Keyed on the text so a
                // new page is a new node.
                builder.AddClickable(
                    ControlId.Structural("dlgbody:" + body),
                    ctx => ctx.Message.Fragment(body),
                    (ctx, mods) => { }
                );
            }

            System.Collections.Generic.List<UIManagerScript.UIObject> choices =
                UIManagerScript.dialogUIObjects;
            if (choices != null) {
                for (int i = 0; i < choices.Count; i++) {
                    UIManagerScript.UIObject captured = choices[i];
                    builder.AddItem(
                        ControlId.Referenced(captured, "dlgchoice:" + i),
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
            }

            builder.CaptureInput();
        }
    }
}
