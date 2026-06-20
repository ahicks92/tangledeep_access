using System.Collections.Generic;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UIObject = UIManagerScript.UIObject;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The options menu (<c>OptionsMenu</c>, opened with Esc / View Options). Like the shop it lives
    /// under the PlayerHUD, not a <c>currentFullScreenUI</c>; liveness is the OPTIONS window state. We
    /// flatten its four sections (Audio / Control / Visual / Gameplay) plus the stray "Lock Camera to
    /// Player" into one owned vertical scroll with a section anchor before each block.
    ///
    /// <para>Every control is a live <see cref="UIObject"/> in <c>allUIObjects</c>; we re-present each
    /// and drive the game's own apply path:
    /// <list type="bullet">
    /// <item><b>Sliders</b> adjust inline on left/right via <c>UIObject.ChangeSliderValue</c> (after
    /// pointing <c>uiObjectFocus</c> at the slider and setting <c>movingSliderViaKeyboard</c>), which
    /// applies the setting and updates the slider's value text — spoken back.</item>
    /// <item><b>Toggles</b> flip on confirm: flip the Unity <c>Toggle.isOn</c> then invoke the
    /// UIObject's <c>mySubmitFunction</c> (which reads the new state and applies it).</item>
    /// <item><b>Buttons</b> activate on confirm via <c>mySubmitFunction</c> — except the keyboard
    /// rebinding / layout buttons, which are gated: the mod owns the keymap by design.</item>
    /// </list>
    /// The world seed is a read-only line; there are no text-entry boxes here.</para>
    /// </summary>
    internal sealed class OptionsOverlay : IUiOverlay {
        // Action buttons (by GameObject name) we lift to the top of the list for quick access.
        private static readonly HashSet<string> ActionButtons = new HashSet<string> {
            "Save and Quit",
            "Save and Title",
            "View Help",
        };

        public OverlayId Id => OverlayId.Options;

        public OverlayResult Handler() {
            // Yield to the dialog overlay for the Save-and-Quit style confirm dialogs.
            if (UIManagerScript.dialogBoxOpen) {
                return OverlayResult.Inactive;
            }

            return UIManagerScript.GetWindowState(UITabs.OPTIONS)
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            builder.CaptureInput();

            Transform menu = FindOptionsMenu();
            if (menu == null) {
                return;
            }

            // Map each control's GameObject to its live UIObject so we can drive the game's handlers.
            var byGo = new Dictionary<GameObject, UIObject>();
            if (UIManagerScript.allUIObjects != null) {
                foreach (UIObject o in UIManagerScript.allUIObjects) {
                    if (o != null && o.gameObj != null) {
                        byGo[o.gameObj] = o;
                    }
                }
            }

            // Action buttons first (Save / quit / help), for quick access — no header, the labels
            // speak for themselves.
            AddNamedButton(builder, menu, byGo, "Save and Quit");
            AddNamedButton(builder, menu, byGo, "Save and Title");
            AddNamedButton(builder, menu, byGo, "View Help");

            foreach (Transform child in menu) {
                string n = child.name;

                // The control/input section is owned by the mod (forced keymap, own input) — drop it.
                if (n == "OptionsControlBlockHeader" || n == "OptionsControlBlock") {
                    continue;
                }

                // Lock Camera is a stray trailing toggle; relocate it into the Visual block below.
                if (n == "Lock Camera to Player") {
                    continue;
                }

                if (n.Contains("Header")) {
                    AddSectionLabel(builder, child.gameObject);
                } else if (n.Contains("Block")) {
                    foreach (Transform c in child) {
                        if (ActionButtons.Contains(c.name)) {
                            continue; // already emitted at the top
                        }

                        TryAddControl(builder, c.gameObject, byGo);
                    }

                    // Camera lock belongs with the other camera/visual settings.
                    if (n == "OptionsVisualBlock") {
                        Transform lockCam = FindChild(menu, "Lock Camera to Player");
                        if (lockCam != null) {
                            TryAddControl(builder, lockCam.gameObject, byGo);
                        }
                    }
                } else {
                    // Other direct-child controls (if any).
                    TryAddControl(builder, child.gameObject, byGo);
                }
            }
        }

        // Find a descendant GameObject by name (the action buttons live a level down, in a block).
        private static Transform FindChild(Transform root, string name) {
            foreach (Transform child in root) {
                if (child.name == name) {
                    return child;
                }

                Transform found = FindChild(child, name);
                if (found != null) {
                    return found;
                }
            }

            return null;
        }

        private static void AddNamedButton(IOverlayBuilder builder, Transform menu, Dictionary<GameObject, UIObject> byGo, string goName) {
            Transform t = FindChild(menu, goName);
            if (t != null) {
                AddButton(builder, t.gameObject, Lookup(byGo, t.gameObject));
            }
        }

        // Walk up from a known options control to the OptionsMenu root (active only while open).
        private static Transform FindOptionsMenu() {
            GameObject anchor = UIManagerScript.optionsMusicVolumeContainer;
            Transform t = anchor != null ? anchor.transform : null;
            while (t != null && t.name != "OptionsMenu") {
                t = t.parent;
            }

            return t;
        }

        private static void AddSectionLabel(IOverlayBuilder builder, GameObject header) {
            string text = PrimaryText(header);
            builder.AddLabel(
                ControlId.Structural("opt:sec:" + header.name),
                ctx => ctx.Message.Fragment(text)
            );
        }

        // Classify a GameObject as a toggle / button / slider / read-only line and add a node. Returns
        // false (adds nothing) for non-controls (spacers, the dialog cursor, the blocks themselves).
        private static bool TryAddControl(IOverlayBuilder builder, GameObject go, Dictionary<GameObject, UIObject> byGo) {
            Toggle toggle = go.GetComponent<Toggle>();
            if (toggle != null) {
                AddToggle(builder, go, toggle, Lookup(byGo, go));
                return true;
            }

            Button button = go.GetComponent<Button>();
            if (button != null) {
                AddButton(builder, go, Lookup(byGo, go));
                return true;
            }

            Slider slider = go.GetComponentInChildren<Slider>();
            if (slider != null) {
                AddSlider(builder, go, slider, Lookup(byGo, slider.gameObject));
                return true;
            }

            // A bare text line inside a block (the world seed). Read-only.
            if (go.GetComponentInChildren<TextMeshProUGUI>() != null) {
                string key = go.name;
                builder.AddLabel(
                    ControlId.Structural("opt:text:" + key),
                    ctx => ctx.Message.Fragment(PrimaryText(go))
                );
                return true;
            }

            return false;
        }

        private static UIObject Lookup(Dictionary<GameObject, UIObject> byGo, GameObject go) {
            UIObject o;
            return byGo.TryGetValue(go, out o) ? o : null;
        }

        // --- Sliders ---------------------------------------------------------------------------

        private static void AddSlider(IOverlayBuilder builder, GameObject container, Slider slider, UIObject uiObject) {
            TextMeshProUGUI valueText = container.GetComponentInChildren<TextMeshProUGUI>();

            builder.AddItem(
                ControlId.Structural("opt:slider:" + slider.gameObject.name),
                new NodeVtable {
                    Label = ctx => ctx.Message.Fragment(LabelText(valueText, slider)),
                    OnHorizontalAdjust = uiObject == null
                        ? (System.Action<OverlayCtx, int, bool>)null
                        : (ctx, sign, large) => Adjust(ctx, uiObject, valueText, slider, sign, large),
                }
            );
        }

        private static void Adjust(OverlayCtx ctx, UIObject uiObject, TextMeshProUGUI valueText, Slider slider, int sign, bool large) {
            // Point the game at this slider and use its own keyboard-adjust path (applies + updates
            // the value text). A coarse Shift step nudges several units.
            UIManagerScript.uiObjectFocus = uiObject;
            UIManagerScript.movingSliderViaKeyboard = true;
            uiObject.ChangeSliderValue(sign * (large ? 5 : 1));

            ctx.Message.Fragment(ValueOnly(valueText, slider));
        }

        // The focus label: the full value text ("Music Volume: 23%"), or the bare slider value when the
        // text carries no value (the zoom slider just reads "Zoom Level").
        private static string LabelText(TextMeshProUGUI valueText, Slider slider) {
            string clean = valueText != null ? GameLabelReader.Clean(valueText.text) : null;
            if (string.IsNullOrEmpty(clean)) {
                return ((int)slider.value).ToString();
            }

            return clean.Contains(": ") ? clean : clean + " " + (int)slider.value;
        }

        // The spoken value after an adjust: just the value part, for rapid stepping.
        private static string ValueOnly(TextMeshProUGUI valueText, Slider slider) {
            string clean = valueText != null ? GameLabelReader.Clean(valueText.text) : null;
            if (string.IsNullOrEmpty(clean)) {
                return ((int)slider.value).ToString();
            }

            int idx = clean.LastIndexOf(": ", System.StringComparison.Ordinal);
            return idx >= 0 ? clean.Substring(idx + 2) : clean + " " + (int)slider.value;
        }

        // --- Toggles ---------------------------------------------------------------------------

        private static void AddToggle(IOverlayBuilder builder, GameObject go, Toggle toggle, UIObject uiObject) {
            string label = PrimaryText(go);

            builder.AddItem(
                ControlId.Structural("opt:toggle:" + go.name),
                new NodeVtable {
                    Label = ctx => {
                        ctx.Message.Fragment(label);
                        ctx.Message.Fragment(toggle.isOn ? ModStrings.On : ModStrings.Off);
                    },
                    OnClick = uiObject == null
                        ? (System.Action<OverlayCtx, Modifiers>)null
                        : (ctx, mods) => {
                            UIManagerScript.uiObjectFocus = uiObject;
                            toggle.isOn = !toggle.isOn;
                            uiObject.mySubmitFunction?.Invoke(uiObject.onSubmitValue);
                            ctx.Message.Fragment(label);
                            ctx.Message.Fragment(toggle.isOn ? ModStrings.On : ModStrings.Off);
                        },
                }
            );
        }

        // --- Buttons ---------------------------------------------------------------------------

        private static void AddButton(IOverlayBuilder builder, GameObject go, UIObject uiObject) {
            string label = PrimaryText(go);

            builder.AddItem(
                ControlId.Structural("opt:button:" + go.name),
                new NodeVtable {
                    Label = ctx => ctx.Message.Fragment(label),
                    OnClick = uiObject == null
                        ? (System.Action<OverlayCtx, Modifiers>)null
                        : (ctx, mods) => {
                            UIManagerScript.uiObjectFocus = uiObject;
                            uiObject.mySubmitFunction?.Invoke(uiObject.onSubmitValue);
                        },
                }
            );
        }

        // The control's own label text (the first text under it: a slider/section header's text, a
        // toggle's Label child, a button's caption).
        private static string PrimaryText(GameObject go) {
            TextMeshProUGUI tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            return tmp != null ? GameLabelReader.Clean(tmp.text) : go.name;
        }
    }
}
