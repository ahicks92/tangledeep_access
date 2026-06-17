using System.Collections.Generic;
using System.Text;
using TangledeepAccess.Focus;
using TangledeepAccess.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TangledeepAccess.Dev {
    /// <summary>
    /// Dev-introspection dumps of UI state. Two deliberately different views:
    ///
    ///  - <see cref="DumpGameUi"/> is RAW and structural: the live active UI hierarchy with
    ///    every component type and raw widget text, plus key UIManagerScript state. It is NOT
    ///    the mod's cleaned label view — the whole point is to surface what the cleaned view
    ///    hides (e.g. the SaveSlot screen's real data lives in SaveDataDisplayBlock components,
    ///    not in any focus label), so the structure can be reverse-engineered. It gives breadth;
    ///    the eval endpoint then gives depth on whatever it reveals.
    ///
    ///  - <see cref="DumpModUi"/> is the INTERPRETED view: what the mod's overlay currently
    ///    surfaces. Diffing the two shows where the mod is losing information.
    ///
    /// Main-thread only (reads live scene + game state).
    /// </summary>
    internal static class GuiInspector {
        private const int MaxObjects = 400;

        public static string DumpGameUi() {
            var sb = new StringBuilder();

            sb.Append("== UIManagerScript state ==\n");
            UIManagerScript.UIObject focus = UIManagerScript.uiObjectFocus;
            sb.Append("uiObjectFocus: ");
            if (focus == null) {
                sb.Append("null\n");
            } else {
                sb.Append(PathOf(focus.gameObj)).Append(" \"").Append(Clean(GameLabelReader.ReadLabel(focus))).Append("\"\n");
            }
            UIManagerScript ums = UIManagerScript.singletonUIMS;
            if (ums != null) {
                sb.Append("currentFullScreenUI: ")
                    .Append(ums.currentFullScreenUI == null ? "null" : ums.currentFullScreenUI.GetType().Name).Append('\n');
            }

            sb.Append("\n== active UI hierarchy (full paths; {components} \"text\") ==\n");
            int count = 0;
            foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>()) {
                if (!canvas.isActiveAndEnabled) {
                    continue;
                }
                count = Walk(canvas.transform, sb, count);
                if (count >= MaxObjects) {
                    sb.Append("... (truncated at ").Append(MaxObjects).Append(" objects)\n");
                    break;
                }
            }
            return sb.ToString();
        }

        public static string DumpModUi() {
            OverlayDispatcher dispatcher = UiRuntime.Dispatcher;
            return dispatcher == null ? "no dispatcher (mod not initialized)\n" : dispatcher.Describe();
        }

        private static int Walk(Transform t, StringBuilder sb, int count) {
            if (count >= MaxObjects) {
                return count;
            }
            GameObject go = t.gameObject;
            if (!go.activeInHierarchy) {
                return count; // only what's actually on screen
            }

            var types = new List<string>();
            string text = null;
            foreach (Component c in go.GetComponents<Component>()) {
                if (c == null) {
                    continue; // missing script
                }
                string tn = c.GetType().Name;
                if (tn == "Transform" || tn == "RectTransform" || tn == "CanvasRenderer") {
                    continue; // structural noise
                }
                types.Add(tn);
                if (text == null) {
                    var tmp = c as TMP_Text;
                    if (tmp != null) {
                        text = tmp.text;
                    } else {
                        var uitext = c as Text;
                        if (uitext != null) {
                            text = uitext.text;
                        }
                    }
                }
            }

            sb.Append(PathOf(go));
            if (types.Count > 0) {
                sb.Append(" {").Append(string.Join(", ", types.ToArray())).Append('}');
            }
            if (!string.IsNullOrEmpty(text)) {
                sb.Append(" \"").Append(Clean(text)).Append('"');
            }
            sb.Append('\n');
            count++;

            for (int i = 0; i < t.childCount; i++) {
                count = Walk(t.GetChild(i), sb, count);
                if (count >= MaxObjects) {
                    break;
                }
            }
            return count;
        }

        private static string PathOf(GameObject go) {
            if (go == null) {
                return "null";
            }
            var parts = new List<string>();
            for (Transform t = go.transform; t != null; t = t.parent) {
                parts.Add(t.name);
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        // Strip TMP color/markup, collapse newlines, cap length - for readability only.
        private static string Clean(string raw) {
            if (string.IsNullOrEmpty(raw)) {
                return "";
            }
            string s = GameLabelReader.Clean(raw) ?? raw;
            s = s.Replace("\r", "").Replace("\n", "\\n");
            return s.Length > 80 ? s.Substring(0, 80) + "..." : s;
        }
    }
}
