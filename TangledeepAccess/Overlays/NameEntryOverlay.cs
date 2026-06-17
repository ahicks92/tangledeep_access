using TangledeepAccess.Focus;
using TangledeepAccess.Ui;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The character-creation name-entry screen (NAMEINPUT, while still deciding on the name).
    /// The focused text field and the buttons are not a navigable UIObject graph, so we model it
    /// as an owned vertical menu: Name (reads the current field value), Random name (rolls a new
    /// one and reads it), Confirm name (commits → the begin screen). Owns input via the title
    /// hook. Custom typing is deferred — RANDOM and the default name complete the screen — but
    /// the value is always read.
    /// </summary>
    internal sealed class NameEntryOverlay : IUiOverlay {
        public OverlayId Id => OverlayId.NameEntry;

        public OverlayResult Handler() {
            bool onTitle = GameMasterScript.gmsSingleton != null
                && GameMasterScript.gmsSingleton.titleScreenGMS;
            return onTitle
                && TitleScreenScript.CreateStage == CreationStages.NAMEINPUT
                && CharCreation.NameEntryScreenState == ENameEntryScreenState.deciding_on_name
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            string name = NameValue();
            builder.AddLabel(
                ControlId.Structural("name"),
                ctx => ctx.Message.Fragment("Name, " + (name ?? "blank"))
            );
            builder.AddClickable(
                ControlId.Structural("random"),
                ctx => ctx.Message.Fragment("Random name"),
                (ctx, mods) => {
                    CharCreation.singleton?.GenerateRandomNameAndFillField();
                    ctx.Message.Fragment("Random name");
                    string fresh = NameValue();
                    if (fresh != null) {
                        ctx.Message.Fragment(fresh);
                    }
                }
            );
            builder.AddClickable(
                ControlId.Structural("confirm"),
                ctx => ctx.Message.Fragment("Confirm name"),
                (ctx, mods) => CharCreation.singleton?.OnNameEntryBoxConfirm()
            );
            builder.CaptureInput();
        }

        private static string NameValue() {
            return CharCreation.nameInputTextBox != null
                ? GameLabelReader.Clean(CharCreation.nameInputTextBox.text)
                : null;
        }
    }
}
