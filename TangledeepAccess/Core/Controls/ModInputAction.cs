namespace TangledeepAccess.Controls {
    /// <summary>
    /// The metaphorical intent a key press expresses — independent of the physical key that
    /// produced it and of how the active context will carry it out. The engine-side keymap
    /// (<c>InputKeys</c>) turns keys into these, one shared vocabulary across menus, the look
    /// cursor, and free play; the per-context realizer (the <see cref="Ui.OverlayDispatcher"/> for
    /// menus, <c>GameplayReader</c> for free play) cashes each one out into a concrete effect. The
    /// same intent legitimately means different things per context (e.g. <see cref="ModInputKind.Move"/>
    /// steps menu focus, or steps the look cursor). Carrying the direction as a field rather than
    /// as MoveNorth/MoveUp variants lets one handler branch on direction.
    /// </summary>
    public enum ModInputKind {
        /// <summary>Directional. Carries Dx/Dy in {-1,0,1} (+x east, +y north). In a menu it steps
        /// focus (orthogonal only); with the exploration cursor it steps the cursor one tile (8-way).</summary>
        Move,

        /// <summary>Directional, like <see cref="Move"/>, but in a menu it skips focus as far as
        /// possible in the direction — the last reachable control in that row/column (Shift+direction).
        /// Carries Dx/Dy in {-1,0,1} the same way; orthogonal only.</summary>
        MoveToEdge,

        /// <summary>Confirm / activate the focused control (menus).</summary>
        Confirm,

        /// <summary>Confirm a destructive / confirmation-required action — Ctrl+Enter. Activates the
        /// focused control like <see cref="Confirm"/>, but the dispatcher invokes the control with
        /// <see cref="Ui.Modifiers.Control"/> set, so a node can gate an action that first warns on a
        /// plain Enter (e.g. selling a favorited item) and only proceeds on this.</summary>
        DangerousConfirm,

        /// <summary>Cancel / back out without acting (Escape). Dismisses an auxiliary overlay (e.g. a
        /// quantity prompt) back to its parent. Claimed only while an aux owns input, so a normal
        /// screen's Escape still passes through to the game to close that screen.</summary>
        Cancel,

        /// <summary>Read detailed info about the focused control (menus) — the equivalent of the
        /// game's hover tooltip. Distinct from Confirm, which is the primary action (use/equip).</summary>
        ReadInfo,

        /// <summary>Read a control's <i>secondary</i> info (menus) — a second read channel beside
        /// <see cref="ReadInfo"/>, on Ctrl+K. Its meaning is per-control: the equipment sheet uses it
        /// to read an item's stat comparison against the equipped gear, cycling the compared slot on
        /// repeat. Controls with no secondary read just re-read their label.</summary>
        ReadSecondary,

        /// <summary>Toggle the focused control's "favorite" mark (menus). A toggle, not a one-way
        /// set — blind players expect the key to flip the state both ways.</summary>
        MarkFavorite,

        /// <summary>Toggle the focused control's "trash" mark (menus). A toggle, like MarkFavorite.</summary>
        MarkTrash,

        /// <summary>Assign the focused control to a hotbar slot (menus). Dx carries the slot, 1-8.
        /// The skill sheet binds its active abilities this way; overlays with no handler re-read.</summary>
        AssignHotbar,

        /// <summary>The game's UI focus changed — a non-keyboard event source, not a key press. A
        /// payload-free ping: the new focus lives in the focus watcher's published current value,
        /// read by the realizer when this fires. Emitted once per focus edge, stale→none included.</summary>
        FocusChanged,

        // Free-play spatial queries. ReadHere reads the player's own tile (S).
        ReadHere,
        ReadStatus,
        ReadHotbar,

        /// <summary>Cycle to the next hotbar page and read it. The mod owns this on backtick because
        /// the game's own "Cycle Hotbars" defaults to Ctrl, which the screen reader claims.</summary>
        CycleHotbar,
        RepeatLast,

        // Combat-log history scrollback (free play). Step a browse cursor through the captured
        // game-log lines: Prev is older (Ctrl+[), Next is newer (Ctrl+]). A new logged line snaps
        // the cursor back to the latest.
        LogHistoryPrev,
        LogHistoryNext,

        // Navigation aids (free play): a framework of audio cues on F-key slots, distinct from the
        // spoken queries. Dx carries the aid index (F1 = 0, F2 = 1, …). Shift+Fn toggles an aid on
        // or off; Ctrl+Fn fires it once without moving.
        NavAidToggle,
        NavAidTrigger,

        // Exploration cursor control (free play). Move steps the cursor (the speculation ring),
        // CursorSkip (Shift+ring) skips to the next terrain/shape change or occupant; the rest are
        // its read/follow/recenter verbs.
        CursorRead,
        CursorExamine,
        CursorSkip,
        CursorFollowToggle,
        CursorRecenter,

        // Scanner: navigating the categorized, distance-sorted map readout. Two axes —
        // category (the broad bucket, with "visible" and "all" buckets spanning every feature)
        // and entry (one feature within it). Modeless: the scanner keeps its selection between
        // presses, no toggle. The list is a snapshot rebuilt only on ScanRescan; ScanGoto points
        // the exploration cursor at the selected feature, ScanExamine reads its full tooltip, and
        // ScanAutoJumpToggle flips a mode where navigation auto-points the cursor (cues only).
        ScanNextCategory,
        ScanPrevCategory,
        ScanNextEntry,
        ScanPrevEntry,
        ScanGoto,
        ScanExamine,
        ScanAutoJumpToggle,
        ScanRescan,
    }

    /// <summary>
    /// A recognized intent plus its optional directional payload. A value type, so the one-slot
    /// pending input (<c>UiRuntime</c>) carries it without allocating.
    /// </summary>
    public struct ModInputAction {
        public ModInputKind Kind;
        public int Dx;
        public int Dy;

        /// <summary>A directional intent: Dx/Dy in {-1,0,1}, +x east, +y north.</summary>
        public static ModInputAction Move(int dx, int dy) {
            return new ModInputAction { Kind = ModInputKind.Move, Dx = dx, Dy = dy };
        }

        /// <summary>A directional skip-to-edge intent: same Dx/Dy convention as <see cref="Move"/>.</summary>
        public static ModInputAction MoveToEdge(int dx, int dy) {
            return new ModInputAction { Kind = ModInputKind.MoveToEdge, Dx = dx, Dy = dy };
        }

        /// <summary>A payload-free intent (everything but <see cref="ModInputKind.Move"/>).</summary>
        public static ModInputAction Of(ModInputKind kind) {
            return new ModInputAction { Kind = kind };
        }
    }
}
