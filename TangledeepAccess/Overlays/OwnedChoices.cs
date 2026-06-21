using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;
using UnityEngine;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// Unravels the game's single shared dialog box into an owned vertical menu and captures input,
    /// so we navigate it uniformly regardless of how the game keys that particular dialog. Shared by
    /// the in-game (<see cref="DialogOverlay"/>) and title (<see cref="TitleDialogOverlay"/>) dialog
    /// overlays — the only difference between them is the scope/pump that owns the dialog.
    ///
    /// <para>The box is one reused widget driven by a branching conversation model, so its content
    /// changes in place as the player advances. We do NOT key controls to game objects — ids are
    /// plain structural strings — and instead detect content changes via <see cref="SubIdentity"/>:
    /// when the generation string changes, the dispatcher re-announces as if freshly opened. Each
    /// choice carries its own click handler that focuses that specific button and drives the game's
    /// own keyboard confirm (<see cref="UIManagerScript.DialogCursorConfirm()"/>) on it, so we never
    /// depend on the game's focus already being synced to us. That confirm honors the typewriter — it
    /// early-returns while text is still revealing (<c>dialogInteractableDelayed</c>) — so we cannot
    /// fast-forward a typing dialog; we wait it out exactly like the keyboard does.</para>
    ///
    /// <para>The vertical list, in order: the body text (read-only), then the value slider / text
    /// box / image if present, then one node per response button. Convenience: a dialog with exactly
    /// one body and one button makes the body activate that button too, so the player can click
    /// straight through narrative pages.</para>
    /// </summary>
    internal static class OwnedChoices {
        // The dialog value slider's parent GameObject (instance field on the singleton) and the
        // portrait layout parent (static field) are private on UIManagerScript; read their active
        // state by reflection to tell whether the slider / image is showing.
        private static readonly AccessTools.FieldRef<UIManagerScript, GameObject> SliderParentRef =
            AccessTools.FieldRefAccess<UIManagerScript, GameObject>("dialogValueSliderParent");

        private static readonly AccessTools.FieldRef<GameObject> ImageParentRef =
            AccessTools.StaticFieldRefAccess<GameObject>(
                AccessTools.Field(typeof(UIManagerScript), "dialogBoxImageLayoutParent")
            );

        public static void Build(IOverlayBuilder builder) {
            builder.CaptureInput();

            string body = ReadBody();
            List<UIManagerScript.UIObject> choices = UIManagerScript.dialogUIObjects;
            int choiceCount = choices?.Count ?? 0;

            bool sliderOn = SliderActive();
            bool textboxOn = TextboxActive();
            bool imageOn = ImageActive();

            // Click-through: a lone body with a single button (and no slider/text entry to resolve
            // first) lets the body itself activate that button — Enter on the text advances the page.
            bool clickThrough = !string.IsNullOrEmpty(body) && choiceCount == 1 && !sliderOn && !textboxOn;

            if (!string.IsNullOrEmpty(body)) {
                UIManagerScript.UIObject only = clickThrough ? choices[0] : null;
                builder.AddItem(
                    ControlId.Structural("dlgbody"),
                    new NodeVtable {
                        Label = ctx => ctx.Message.Fragment(body),
                        // With several choices the body is inert (a no-op, not a default-confirm);
                        // with exactly one it drives that one button.
                        OnClick = clickThrough
                            ? (ctx, mods) => Activate(only)
                            : (Action<OverlayCtx, Modifiers>)((ctx, mods) => { }),
                    }
                );
            }

            if (sliderOn) {
                builder.AddItem(
                    ControlId.Structural("dlgslider"),
                    new NodeVtable {
                        Label = ctx => ctx.Message.Fragment(ModStrings.SliderAmount(UIManagerScript.GetSliderValueInt())),
                        OnHorizontalAdjust = AdjustSlider,
                    }
                );
            }

            if (textboxOn) {
                builder.AddClickable(
                    ControlId.Structural("dlgtextbox"),
                    ctx => ctx.Message.Fragment(ModStrings.TextBox),
                    (ctx, mods) => ctx.Message.Fragment(ModStrings.TextBoxUnsupported)
                );
            }

            if (imageOn) {
                builder.AddLabel(
                    ControlId.Structural("dlgimage"),
                    ctx => ctx.Message.Fragment(ModStrings.Image)
                );
            }

            AddChoices(builder, choices);
        }

        /// <summary>
        /// The title main menu's build: capture input and present only the response buttons. The
        /// menu reuses the same shared dialog box, but its "body" is a placeholder ("(ok)"), not
        /// content, so it is omitted — as are the slider/text/image extras, which never apply there.
        /// </summary>
        public static void BuildButtonsOnly(IOverlayBuilder builder) {
            builder.CaptureInput();
            AddChoices(builder, UIManagerScript.dialogUIObjects);
        }

        // One node per response button. Each carries its own click handler that drives the game's
        // confirm on that specific button, so navigation never depends on game focus being synced.
        private static void AddChoices(IOverlayBuilder builder, List<UIManagerScript.UIObject> choices) {
            if (choices == null) {
                return;
            }

            for (int i = 0; i < choices.Count; i++) {
                UIManagerScript.UIObject captured = choices[i];
                builder.AddItem(
                    ControlId.Structural("dlgchoice:" + i),
                    new NodeVtable {
                        Label = ctx => {
                            string label = GameLabelReader.ReadLabel(captured);
                            if (!string.IsNullOrEmpty(label)) {
                                ctx.Message.Fragment(label);
                            }
                        },
                        OnClick = (ctx, mods) => Activate(captured),
                    }
                );
            }
        }

        /// <summary>
        /// The content generation: every piece that identifies what the dialog is currently showing,
        /// in order. A change means the conversation advanced (or a different dialog opened) and the
        /// overlay should re-announce. Deliberately excludes the slider's live <i>value</i> — that
        /// changes on every nudge and must not re-fire the open behaviour.
        /// </summary>
        public static string SubIdentity() {
            var sb = new StringBuilder();
            sb.Append((int)UIManagerScript.dialogBoxType).Append('\n');
            sb.Append(UIManagerScript.currentConversation?.refName).Append('\n');
            sb.Append(UIManagerScript.currentTextBranch?.branchRefName).Append('\n');
            sb.Append(ReadBody()).Append('\n');

            List<UIManagerScript.UIObject> choices = UIManagerScript.dialogUIObjects;
            if (choices != null) {
                sb.Append(choices.Count);
                foreach (UIManagerScript.UIObject c in choices) {
                    sb.Append('\0').Append(GameLabelReader.ReadLabel(c));
                }
            }

            sb.Append('\n');
            sb.Append(SliderActive() ? '1' : '0');
            sb.Append(TextboxActive() ? '1' : '0');
            sb.Append(ImageActive() ? '1' : '0');
            return sb.ToString();
        }

        private static string ReadBody() {
            DialogBoxScript dbs = UIManagerScript.myDialogBoxComponent;
            TMPro.TextMeshProUGUI text = dbs != null ? dbs.GetDialogText() : null;
            return text != null ? GameLabelReader.Clean(text.text) : null;
        }

        // Activate a specific choice by driving the game's own dialog confirm on it: focus the button
        // (so the game's confirm targets it), then call DialogCursorConfirm — the same keyboard
        // confirm path the game runs, which executes the button's dialogEventScript and advances the
        // branch / closes. It early-returns while the typewriter is still revealing text
        // (dialogInteractableDelayed), so this no-ops mid-reveal rather than skipping to the end. Our
        // next tick sees the new generation and re-announces.
        private static void Activate(UIManagerScript.UIObject choice) {
            if (choice == null) {
                return;
            }

            UIManagerScript.ChangeUIFocusAndAlignCursor(choice);
            UIManagerScript.DialogCursorConfirm();
        }

        // Mirror the game's own slider keys: a small step is its MoveCursor (1% of range, min 1), a
        // large step its ScrollPages (10%). Both clamp, update the slider readout, and play the tick
        // sound; we then speak the new value (just the number, for rapid adjustment).
        private static void AdjustSlider(OverlayCtx ctx, int sign, bool large) {
            if (large) {
                UIManagerScript.ScrollPages(sign > 0);
            } else {
                UIManagerScript.MoveCursor(sign < 0 ? Directions.WEST : Directions.EAST);
            }

            ctx.Message.Fragment(UIManagerScript.GetSliderValueInt().ToString());
        }

        private static bool SliderActive() {
            UIManagerScript ums = UIManagerScript.singletonUIMS;
            GameObject go = ums != null ? SliderParentRef(ums) : null;
            return go != null && go.activeSelf;
        }

        private static bool TextboxActive() {
            return UIManagerScript.genericTextInputField != null
                && UIManagerScript.genericTextInputField.IsActive();
        }

        private static bool ImageActive() {
            GameObject go = ImageParentRef();
            return go != null && go.activeSelf;
        }
    }
}
