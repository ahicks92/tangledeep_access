using System;
using System.Collections.Generic;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Ui;
using TMPro;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// Speaks the character-creation screens whose controls the generic fallback cannot read.
    /// Two are handled today:
    ///
    /// <para><b>Job grid</b> — the job buttons are image-only (an <c>Animatable</c> walk sprite,
    /// no TMP text), so each label is derived the way the game would: <c>jobEnumOrder[i]</c> →
    /// job enum → <c>GetFullJobReadout</c> (pure synchronous string assembly), or the locked-job
    /// string when not yet unlocked. The button graph is mirrored exactly like the generic
    /// fallback; only the label provider differs.</para>
    ///
    /// <para><b>Name entry</b> — the prompt, the current name value, and the job/mode/feats
    /// summary are on-screen labels with no focusable control, so they ride the one-shot
    /// announcement channel (keyed by the name, so RANDOM re-announces the new name). The
    /// CONFIRM / RANDOM buttons are mirrored like any other menu. Custom typing into the field
    /// is a later enhancement — the default name plus RANDOM make the screen completable now.</para>
    ///
    /// Other creation stages (feat select, difficulty mods) stay on the generic mirror / dialog
    /// overlay until each gets its own handling. See docs/new-game-menu.md.
    /// </summary>
    internal sealed class CharCreationOverlay : IUiOverlay {
        // jobEnumOrder maps a button slot to a CharacterJobs enum value; it is private static.
        private static readonly AccessTools.FieldRef<int[]> JobEnumOrder =
            AccessTools.StaticFieldRefAccess<int[]>(AccessTools.Field(typeof(CharCreation), "jobEnumOrder"));

        // Name-entry summary labels: private instance fields on CharCreation.
        private static readonly AccessTools.FieldRef<CharCreation, TextMeshProUGUI> LabelTitle =
            AccessTools.FieldRefAccess<CharCreation, TextMeshProUGUI>("label_title");
        private static readonly AccessTools.FieldRef<CharCreation, TextMeshProUGUI> LabelJobName =
            AccessTools.FieldRefAccess<CharCreation, TextMeshProUGUI>("label_job_name");
        private static readonly AccessTools.FieldRef<CharCreation, TextMeshProUGUI> LabelDifficulty =
            AccessTools.FieldRefAccess<CharCreation, TextMeshProUGUI>("label_difficulty");
        private static readonly AccessTools.FieldRef<CharCreation, List<TextMeshProUGUI>> LabelFeats =
            AccessTools.FieldRefAccess<CharCreation, List<TextMeshProUGUI>>("label_feats");

        public OverlayId Id => OverlayId.CharCreation;

        /// <summary>
        /// Active on the two screens we specialize: the job grid (creation live and the focused
        /// control is a job button) and name entry (<c>nameInputOpen</c>). Keying the job case
        /// off the focused button scopes us to the grid and cedes to the dialog overlay when an
        /// intro/prompt dialog is up.
        /// </summary>
        public OverlayResult Handler() {
            bool jobGrid = CharCreation.creationActive && FocusedJobIndex() >= 0;
            // Title-screen feat select only, so a stale CreateStage can never shadow an in-game
            // dialog (the same guard SaveSlotOverlay uses).
            bool onTitle = GameMasterScript.gmsSingleton != null
                && GameMasterScript.gmsSingleton.titleScreenGMS;
            bool featSelect = onTitle
                && TitleScreenScript.CreateStage == CreationStages.PERKSELECT
                && UIManagerScript.dialogBoxOpen;
            return jobGrid || UIManagerScript.nameInputOpen || featSelect
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            if (UIManagerScript.nameInputOpen) {
                BuildNameEntry(builder);
                return;
            }

            if (TitleScreenScript.CreateStage == CreationStages.PERKSELECT) {
                BuildFeatSelect(builder);
                return;
            }

            UIManagerScript.UIObject[] buttons = CharCreation.jobButtons;
            GameMenuMirror.Build(builder, uo => JobLabel(uo, buttons));
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
            TMPro.TextMeshProUGUI text = dbs != null ? dbs.GetDialogText() : null;
            return text != null ? GameLabelReader.Clean(text.text) : null;
        }

        // --- Name entry ---

        private static void BuildNameEntry(IOverlayBuilder builder) {
            string name = CharCreation.nameInputTextBox != null
                ? GameLabelReader.Clean(CharCreation.nameInputTextBox.text)
                : null;

            // Prompt + name + summary appear without a focus move, so announce them; key by the
            // name so picking a RANDOM name re-reads the screen with the new name.
            CharCreation cc = CharCreation.singleton;
            builder.Announce(name ?? "", ctx => {
                ctx.Message.Fragment(Read(LabelTitle(cc))); // "What is our heroine's name?"
                ctx.Message.ListItem(name);
                ctx.Message.ListItem(Read(LabelJobName(cc)));
                ctx.Message.ListItem(Read(LabelDifficulty(cc)));
                AppendFeats(ctx.Message, cc);
            });

            // The CONFIRM / RANDOM buttons use the normal focus model; mirror them.
            if (UIManagerScript.uiObjectFocus != null) {
                GameMenuMirror.Build(builder, GameLabelReader.ReadLabel);
            } else if (name != null) {
                builder.AddLabel(ControlId.Structural("nameentry"), ctx => { });
            }
        }

        private static void AppendFeats(Speech.MessageBuilder message, CharCreation cc) {
            List<TextMeshProUGUI> feats = cc != null ? LabelFeats(cc) : null;
            if (feats == null) {
                return;
            }

            foreach (TextMeshProUGUI feat in feats) {
                string text = Read(feat);
                if (text != null) {
                    message.ListItem(text);
                }
            }
        }

        private static string Read(TextMeshProUGUI label) {
            return label != null ? GameLabelReader.Clean(label.text) : null;
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
    }
}
