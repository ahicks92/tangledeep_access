using TangledeepAccess.Focus;
using TangledeepAccess.Ui;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The character-creation feat-select screen (the <c>PERKSELECT</c> dialog). Passive: the
    /// game drives navigation. The instruction ("Select two feats...") is dialog body text with
    /// no focusable control, so it rides the one-shot announcement channel; the feat buttons are
    /// mirrored, reading name + description from the <c>ButtonCombo</c> (the generic reader sees
    /// only the header) plus the toggled selection state so the player can track their two picks.
    /// </summary>
    internal sealed class FeatSelectOverlay : IUiOverlay {
        public OverlayId Id => OverlayId.FeatSelect;

        public OverlayResult Handler() {
            bool onTitle = GameMasterScript.gmsSingleton != null
                && GameMasterScript.gmsSingleton.titleScreenGMS;
            return onTitle
                && TitleScreenScript.CreateStage == CreationStages.PERKSELECT
                && UIManagerScript.dialogBoxOpen
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            string body = DialogBody();
            if (body != null) {
                builder.Announce(body, ctx => ctx.Message.Fragment(body));
            }

            GameMenuMirror.Build(builder, FeatLabel);
        }

        private static string FeatLabel(UIManagerScript.UIObject uo) {
            ButtonCombo button = uo.button;
            if (button == null) {
                return GameLabelReader.ReadLabel(uo);
            }

            string name = GameLabelReader.Clean(button.headerText);
            string desc = GameLabelReader.Clean(button.buttonText);
            string text = name != null && desc != null ? name + ". " + desc : name ?? desc;
            return button.toggled ? "selected, " + text : text;
        }

        private static string DialogBody() {
            DialogBoxScript dbs = UIManagerScript.myDialogBoxComponent;
            TMPro.TextMeshProUGUI text = dbs != null ? dbs.GetDialogText() : null;
            return text != null ? GameLabelReader.Clean(text.text) : null;
        }
    }
}
