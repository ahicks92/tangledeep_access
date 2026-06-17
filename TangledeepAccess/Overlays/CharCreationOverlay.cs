using System;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Ui;
using TMPro;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// Speaks the character-creation screens the generic fallback cannot read. Each is a small
    /// overlay; the title-input hook lets the owned ones drive their own navigation.
    ///
    /// <para><b>Job grid</b> — image-only buttons, so each label is derived like the game would
    /// (<c>jobEnumOrder[i]</c> → <c>GetFullJobReadout</c>); the button graph is mirrored, only the
    /// label provider differs. Passive (the game drives grid nav).</para>
    ///
    /// <para><b>Feat select</b> — dialog buttons; mirrored with name + description from the
    /// ButtonCombo and the toggled state. Passive.</para>
    ///
    /// <para><b>Name entry</b> and <b>the ready/begin screen</b> — these are NOT in the UIObject
    /// focus graph (a focused text field and an integer-indexed option list), which is why the
    /// generic mirror reads nothing. We model them as owned virtual controls: real nodes whose
    /// labels read the live field value / option text and whose <c>OnClick</c> calls the game's
    /// own action directly (the Core dispatcher just invokes the delegate, so a node can drive
    /// game code). Custom name/seed typing is deferred — RANDOM and the default name complete the
    /// screen — but the value is always read.</para>
    ///
    /// See docs/new-game-menu.md.
    /// </summary>
    internal sealed class CharCreationOverlay : IUiOverlay {
        // jobEnumOrder maps a button slot to a CharacterJobs enum value; it is private static.
        private static readonly AccessTools.FieldRef<int[]> JobEnumOrder =
            AccessTools.StaticFieldRefAccess<int[]>(AccessTools.Field(typeof(CharCreation), "jobEnumOrder"));

        // Ready-screen option labels: private instance fields on CharCreation.
        private static readonly AccessTools.FieldRef<CharCreation, TextMeshProUGUI> LabelBeginGame =
            AccessTools.FieldRefAccess<CharCreation, TextMeshProUGUI>("label_begin_game");
        private static readonly AccessTools.FieldRef<CharCreation, TextMeshProUGUI> LabelGoBack =
            AccessTools.FieldRefAccess<CharCreation, TextMeshProUGUI>("label_go_back");

        public OverlayId Id => OverlayId.CharCreation;

        /// <summary>
        /// Active on the screens we specialize: the job grid (creation live, a job button
        /// focused), feat select (PERKSELECT dialog), and the name/ready screens (NAMEINPUT).
        /// Gated to the title-screen flow — the game leaves nameInputOpen/CreateStage set after a
        /// game starts, so without this the name branch would shadow in-game screens.
        /// </summary>
        public OverlayResult Handler() {
            bool onTitle = GameMasterScript.gmsSingleton != null
                && GameMasterScript.gmsSingleton.titleScreenGMS;
            if (!onTitle) {
                return OverlayResult.Inactive;
            }

            bool jobGrid = CharCreation.creationActive && FocusedJobIndex() >= 0;
            bool featSelect = TitleScreenScript.CreateStage == CreationStages.PERKSELECT
                && UIManagerScript.dialogBoxOpen;
            return jobGrid || IsNameFlow() || featSelect
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            if (IsNameFlow()) {
                if (CharCreation.NameEntryScreenState
                    == ENameEntryScreenState.name_confirmed_and_ready_to_go) {
                    BuildReadyScreen(builder);
                } else {
                    BuildNameEntry(builder);
                }

                return;
            }

            if (TitleScreenScript.CreateStage == CreationStages.PERKSELECT) {
                BuildFeatSelect(builder);
                return;
            }

            UIManagerScript.UIObject[] buttons = CharCreation.jobButtons;
            GameMenuMirror.Build(builder, uo => JobLabel(uo, buttons));
        }

        // The name and ready screens both live in the NAMEINPUT stage (distinguished by
        // NameEntryScreenState); nameInputOpen alone can lag, so accept either signal.
        private static bool IsNameFlow() {
            return UIManagerScript.nameInputOpen
                || TitleScreenScript.CreateStage == CreationStages.NAMEINPUT;
        }

        // --- Name entry (owned virtual control) ---

        private static void BuildNameEntry(IOverlayBuilder builder) {
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

        // --- Ready / begin screen (owned virtual control) ---

        // The 3-option selector (iSelectedConfirmCharacterOption 0..2): Begin / Go back / seed.
        // Not UIObject-backed, so we drive each option's game action straight from OnClick.
        private static void BuildReadyScreen(IOverlayBuilder builder) {
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

            // World seed: read-only here (typing a seed is deferred; empty means a random seed).
            string seed = cc != null && cc.worldSeedInput != null
                ? GameLabelReader.Clean(cc.worldSeedInput.text)
                : null;
            builder.AddLabel(
                ControlId.Structural("seed"),
                ctx => ctx.Message.Fragment("World seed, " + (seed ?? "random"))
            );

            builder.CaptureInput();
        }

        private static string NameValue() {
            return CharCreation.nameInputTextBox != null
                ? GameLabelReader.Clean(CharCreation.nameInputTextBox.text)
                : null;
        }

        // --- Feat select ---

        private static void BuildFeatSelect(IOverlayBuilder builder) {
            // Announce the instruction ("Select two feats...") once; it is dialog body text
            // with no focusable control, like the other dialog bodies.
            string body = DialogBody();
            if (body != null) {
                builder.Announce(body, ctx => ctx.Message.Fragment(body));
            }

            // The feat buttons are dialog UIObjects in the normal neighbor graph. Mirror them
            // like the generic fallback, but read name + description from the ButtonCombo (the
            // generic reader sees only the header/name), plus the toggled selection state.
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
            // A feat already chosen reads as selected so the player can track their two picks.
            return button.toggled ? "selected, " + text : text;
        }

        private static string DialogBody() {
            DialogBoxScript dbs = UIManagerScript.myDialogBoxComponent;
            TextMeshProUGUI text = dbs != null ? dbs.GetDialogText() : null;
            return text != null ? GameLabelReader.Clean(text.text) : null;
        }

        // --- Job grid ---

        /// <summary>The spoken text for one job button: its full readout, or the locked string.</summary>
        private static string JobLabel(UIManagerScript.UIObject uo, UIManagerScript.UIObject[] buttons) {
            int idx = Array.IndexOf(buttons, uo);
            if (idx < 0) {
                // Not a job button (some other control reachable in the graph) — read it plainly.
                return GameLabelReader.ReadLabel(uo);
            }

            int[] order = JobEnumOrder();
            if (order == null || idx >= order.Length) {
                return null;
            }

            int jobEnum = order[idx];
            if (!SharedBank.CheckIfJobIsUnlocked((CharacterJobs)jobEnum)) {
                return GameLabelReader.Clean(StringManager.GetString("ui_job_locked"));
            }

            CharacterJobData data = CharacterJobData.GetJobDataByEnum(jobEnum);
            // Null while masterJobList is still loading; a later tick re-speaks once it is ready.
            return data != null ? GameLabelReader.Clean(data.GetFullJobReadout("")) : null;
        }

        /// <summary>Index of the focused job button in <c>jobButtons</c>, or -1.</summary>
        private static int FocusedJobIndex() {
            UIManagerScript.UIObject[] buttons = CharCreation.jobButtons;
            UIManagerScript.UIObject focus = UIManagerScript.uiObjectFocus;
            return buttons != null && focus != null ? Array.IndexOf(buttons, focus) : -1;
        }

        private static string ReadOr(TextMeshProUGUI label, string fallback) {
            string text = label != null ? GameLabelReader.Clean(label.text) : null;
            return string.IsNullOrEmpty(text) ? fallback : text;
        }
    }
}
