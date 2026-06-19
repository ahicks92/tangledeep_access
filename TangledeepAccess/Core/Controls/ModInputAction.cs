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
        /// focus (orthogonal only); with the look cursor it steps the cursor one tile (8-way).</summary>
        Move,

        /// <summary>Confirm / activate the focused control (menus).</summary>
        Confirm,

        /// <summary>Read detailed info about the focused control (menus) — the equivalent of the
        /// game's hover tooltip. Distinct from Confirm, which is the primary action (use/equip).</summary>
        ReadInfo,

        /// <summary>Toggle the focused control's "favorite" mark (menus). A toggle, not a one-way
        /// set — blind players expect the key to flip the state both ways.</summary>
        MarkFavorite,

        /// <summary>Toggle the focused control's "trash" mark (menus). A toggle, like MarkFavorite.</summary>
        MarkTrash,

        /// <summary>The game's UI focus changed — a non-keyboard event source, not a key press. A
        /// payload-free ping: the new focus lives in the focus watcher's published current value,
        /// read by the realizer when this fires. Emitted once per focus edge, stale→none included.</summary>
        FocusChanged,

        // Free-play spatial queries.
        ReadHere,
        Scan,
        ReadStatus,
        ReadHotbar,
        Help,
        RepeatLast,

        // Audio volume nudges (free play). Dx carries the direction: +1 louder, -1 quieter.
        // A hacky one-time tuning aid — music drowns out speech/cues by default.
        VolumeMusic,
        VolumeSfx,
        VolumeFootsteps,

        // Navigation aids (free play): a framework of audio cues on F-key slots, distinct from the
        // spoken queries. Dx carries the aid index (F1 = 0, F2 = 1, …). Shift+Fn toggles an aid on
        // or off; Ctrl+Fn fires it once without moving.
        NavAidToggle,
        NavAidTrigger,

        // Look-cursor control.
        LookToggle,
        LookRecenter,
        LookNextPoi,
        LookPrevPoi,

        // Scanner: navigating the categorized, distance-sorted map readout. Two axes —
        // category (the broad bucket) and entry (one feature within it). Modeless: the
        // scanner keeps its selection between presses, no toggle.
        ScanNextCategory,
        ScanPrevCategory,
        ScanNextEntry,
        ScanPrevEntry,
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

        /// <summary>A payload-free intent (everything but <see cref="ModInputKind.Move"/>).</summary>
        public static ModInputAction Of(ModInputKind kind) {
            return new ModInputAction { Kind = kind };
        }
    }
}
