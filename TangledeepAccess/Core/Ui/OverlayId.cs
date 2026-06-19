namespace TangledeepAccess.Ui {
    /// <summary>
    /// Identifies a logical overlay. A mod-side superset of the game's menus: the first
    /// block mirrors Tangledeep's own UIs (so a handler can claim "I am the inventory");
    /// the second block is for mod-only UIs the game has no widget for. The dispatcher
    /// keys its per-overlay focus cache by this enum — one slot per id means one live
    /// instance per id, which is what preserves focus across ticks.
    ///
    /// Only the ids needed now are defined; extend as overlays are written. Keep this the
    /// single source of cache identity (do not invent ad-hoc string ids elsewhere).
    /// </summary>
    public enum OverlayId {
        // The bottom-of-stack fallback for any legacy UIObject screen without a bespoke overlay:
        // a single owned node that announces the screen is unsupported and captures input.
        Unsupported,

        // Game menus (mirror Tangledeep). Add as their overlays are implemented.
        TitleMenu,
        SaveSlot,
        // The new-game flow, one id per screen so each overlay has its own focus cache that
        // resets cleanly when the player moves between screens.
        JobGrid,
        FeatSelect,
        NameEntry,
        BeginScreen,
        TitleDialog,
        Dialog,
        Inventory,
        Equipment,
        Skills,
        CharacterSheet,
        Shop,

        // Mod-only UIs (no game widget) go below here as they are added.
    }
}
