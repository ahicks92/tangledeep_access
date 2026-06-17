using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Ui;
using TMPro;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The "begin" screen after name entry (NAMEINPUT, name confirmed and ready to go). The game
    /// models it as an integer-indexed 3-option list (not a UIObject graph), so we own it as a
    /// vertical menu whose nodes call the game's own action directly: Begin game, Go back, World
    /// seed (read-only here; typing a seed is deferred — empty means a random seed).
    /// </summary>
    internal sealed class BeginScreenOverlay : IUiOverlay {
        private static readonly AccessTools.FieldRef<CharCreation, TextMeshProUGUI> LabelBeginGame =
            AccessTools.FieldRefAccess<CharCreation, TextMeshProUGUI>("label_begin_game");
        private static readonly AccessTools.FieldRef<CharCreation, TextMeshProUGUI> LabelGoBack =
            AccessTools.FieldRefAccess<CharCreation, TextMeshProUGUI>("label_go_back");

        public OverlayId Id => OverlayId.BeginScreen;

        public OverlayResult Handler() {
            bool onTitle = GameMasterScript.gmsSingleton != null
                && GameMasterScript.gmsSingleton.titleScreenGMS;
            return onTitle
                && TitleScreenScript.CreateStage == CreationStages.NAMEINPUT
                && CharCreation.NameEntryScreenState
                    == ENameEntryScreenState.name_confirmed_and_ready_to_go
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            CharCreation cc = CharCreation.singleton;

            builder.AddClickable(
                ControlId.Structural("begin"),
                ctx => ctx.Message.Fragment(ReadOr(LabelBeginGame(cc), "Begin game")),
                (ctx, mods) => CharCreation.singleton?.ConfirmedAndGameIsReadyToStart()
            );
            builder.AddClickable(
                ControlId.Structural("goback"),
                ctx => ctx.Message.Fragment(ReadOr(LabelGoBack(cc), "Go back")),
                (ctx, mods) => TitleScreenScript.ReturnToMenu()
            );

            string seed = cc != null && cc.worldSeedInput != null
                ? GameLabelReader.Clean(cc.worldSeedInput.text)
                : null;
            builder.AddLabel(
                ControlId.Structural("seed"),
                ctx => ctx.Message.Fragment("World seed, " + (seed ?? "random"))
            );

            builder.CaptureInput();
        }

        private static string ReadOr(TextMeshProUGUI label, string fallback) {
            string text = label != null ? GameLabelReader.Clean(label.text) : null;
            return string.IsNullOrEmpty(text) ? fallback : text;
        }
    }
}
