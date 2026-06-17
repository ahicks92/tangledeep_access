using System;
using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Ui;

namespace TangledeepAccess.Overlays {
    /// <summary>
    /// The character-creation job grid. The job buttons are image-only (a walk sprite, no TMP
    /// text), so each label is derived the way the game would: <c>jobEnumOrder[i]</c> → job enum
    /// → <c>GetFullJobReadout</c>, or the locked-job string. Passive: the game drives grid
    /// navigation; we mirror its button graph and follow focus, swapping in the derived label.
    /// Scoped to the focused control being a job button (so it claims only the grid).
    /// </summary>
    internal sealed class JobGridOverlay : IUiOverlay {
        // jobEnumOrder maps a button slot to a CharacterJobs enum value; it is private static.
        private static readonly AccessTools.FieldRef<int[]> JobEnumOrder =
            AccessTools.StaticFieldRefAccess<int[]>(AccessTools.Field(typeof(CharCreation), "jobEnumOrder"));

        public OverlayId Id => OverlayId.JobGrid;

        public OverlayResult Handler() {
            bool onTitle = GameMasterScript.gmsSingleton != null
                && GameMasterScript.gmsSingleton.titleScreenGMS;
            return onTitle && CharCreation.creationActive && FocusedJobIndex() >= 0
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder) {
            UIManagerScript.UIObject[] buttons = CharCreation.jobButtons;
            GameMenuMirror.Build(builder, uo => JobLabel(uo, buttons));
        }

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

        private static int FocusedJobIndex() {
            UIManagerScript.UIObject[] buttons = CharCreation.jobButtons;
            UIManagerScript.UIObject focus = UIManagerScript.uiObjectFocus;
            return buttons != null && focus != null ? Array.IndexOf(buttons, focus) : -1;
        }
    }
}
