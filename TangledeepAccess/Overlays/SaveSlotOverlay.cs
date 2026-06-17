using HarmonyLib;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TMPro;

namespace TangledeepAccess.Overlays
{
    /// <summary>
    /// Speaks the New Game / Load / Manage-Data save-slot screen. That screen does NOT use the
    /// usual uiObjectFocus/neighbor model for the slots — it has a custom cursor
    /// (TitleScreenScript.idxActiveSaveSlotInMenu) over a fixed array of panels
    /// (UIManagerScript.saveDataDisplayComponents), with cursorIsOnChangePages for the page
    /// buttons. So rather than mirror a graph, we work the array directly: each tick we read
    /// the currently selected slot's panel and speak it, which is robust and simple for a
    /// screen whose layout does not change.
    ///
    /// <para>Locale-neutral: logic keys off the slot index and each field's <c>enabled</c>
    /// state (set by the game's SetDisplayType); the spoken text is the game's own localized
    /// field text, color-stripped. The only mod token is the bare slot number for empty
    /// slots (where the name field is hidden).</para>
    /// </summary>
    internal sealed class SaveSlotOverlay : IUiOverlay
    {
        // The active slot index is a private instance field on TitleScreenScript.
        private static readonly AccessTools.FieldRef<TitleScreenScript, int> ActiveSlotIndex =
            AccessTools.FieldRefAccess<TitleScreenScript, int>("idxActiveSaveSlotInMenu");

        // localizedEver is set inside the panel's UpdateCharacterInformation, so it is true
        // only once the panel has displayed real save data (not the prefab placeholder).
        private static readonly AccessTools.FieldRef<SaveDataDisplayBlock, bool> LocalizedEver =
            AccessTools.FieldRefAccess<SaveDataDisplayBlock, bool>("localizedEver");

        public OverlayId Id => OverlayId.SaveSlot;

        /// <summary>
        /// Active only on the title-screen save-slot stage (gating on titleScreenGMS as the
        /// game itself does, so a stale CreateStage can never shadow in-game speech). While the
        /// focused slot's panel has not yet shown its real data, return Sleeping rather than
        /// read the prefab placeholder — the panel loads asynchronously after the stage opens.
        ///
        /// NOTE (semantic deviation): Sleeping properly means "this overlay cannot render
        /// anything yet" (e.g. global data unavailable), not the per-focus readiness gate we use
        /// here. This is a deliberate shortcut for these hacky focus-following layers; a real
        /// overlay should handle per-item readiness within its build, not by sleeping the whole
        /// overlay on the focused item. Revisit when this screen gets first-class input.
        /// </summary>
        public OverlayResult Handler()
        {
            bool onTitle =
                GameMasterScript.gmsSingleton != null
                && GameMasterScript.gmsSingleton.titleScreenGMS;
            if (!onTitle || TitleScreenScript.CreateStage != CreationStages.SELECTSLOT)
                return OverlayResult.Inactive;

            if (TitleScreenScript.cursorIsOnChangePages)
                return OverlayResult.Active(this); // page buttons carry their own text

            return IsReady(CurrentBlock())
                ? OverlayResult.Active(this)
                : OverlayResult.Sleeping(OverlayId.SaveSlot);
        }

        public void Build(IOverlayBuilder builder)
        {
            // The page-change buttons use the normal focus model; read the focused one.
            if (TitleScreenScript.cursorIsOnChangePages)
            {
                UIManagerScript.UIObject focus = UIManagerScript.uiObjectFocus;
                if (focus == null)
                    return;
                ControlId pageId = ControlId.ForObject(focus);
                builder.AddLabel(pageId, ctx => Speak(ctx, GameLabelReader.ReadLabel(focus)));
                builder.SetStart(pageId);
                return;
            }

            // Slots: a custom cursor over a static array (no uiObjectFocus involved). The
            // handler only reports Active when the current block is ready, so it is here.
            SaveDataDisplayBlock block = CurrentBlock();
            if (!IsReady(block))
                return;

            // The index in the structural key makes moving slots change the node => re-speak.
            ControlId slotId = ControlId.Structural("saveslot:" + block.slotIndex);
            builder.AddLabel(slotId, ctx => Speak(ctx, ReadSlot(block)));
            builder.SetStart(slotId);
        }

        /// <summary>The save panel under the slot cursor, or null if not resolvable yet.</summary>
        private static SaveDataDisplayBlock CurrentBlock()
        {
            TitleScreenScript title = TitleScreenScript.titleScreenSingleton;
            SaveDataDisplayBlock[] blocks = UIManagerScript.saveDataDisplayComponents;
            if (title == null || blocks == null)
                return null;

            int idx = ActiveSlotIndex(title);
            return idx >= 0 && idx < blocks.Length ? blocks[idx] : null;
        }

        /// <summary>A panel is ready when it has displayed real data and has no pending update.</summary>
        private static bool IsReady(SaveDataDisplayBlock block)
        {
            return block != null && LocalizedEver(block) && !block.bInfoIsDirty;
        }

        private static void Speak(OverlayCtx ctx, string text)
        {
            if (!string.IsNullOrEmpty(text))
                ctx.Message.Fragment(text);
        }

        private static string ReadSlot(SaveDataDisplayBlock block)
        {
            var message = new MessageBuilder();

            // The name field is "N. HeroName" for a populated slot; when hidden (empty / new)
            // lead with the bare slot number so the player knows which slot this is.
            string name = Field(block.txtName);
            message.ListItem(name ?? (block.slotIndex + 1).ToString());

            // Each remaining field is spoken only when the game has it enabled for this slot.
            AppendIfShown(message, block.txtJobLevelAndMode);
            AppendIfShown(message, block.txtLocation);
            AppendIfShown(message, block.txtTimePlayed);
            AppendIfShown(message, block.txtNewCharacter);
            AppendIfShown(message, block.txtHeroesLost);
            AppendIfShown(message, block.txtCampaignDifficulty);

            return message.Build();
        }

        private static void AppendIfShown(MessageBuilder message, TextMeshProUGUI field)
        {
            string text = Field(field);
            if (text != null)
                message.ListItem(text);
        }

        /// <summary>A field's cleaned text, or null when the game has it hidden/empty.</summary>
        private static string Field(TextMeshProUGUI field)
        {
            return field != null && field.enabled ? GameLabelReader.Clean(field.text) : null;
        }
    }
}
