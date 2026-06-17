namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// An on-demand spatial query the player triggered with a mod hotkey during gameplay. The
    /// input hook records one of these; the per-frame pump runs it through
    /// <see cref="GameplayReader"/> and speaks the answer. Distinct from <c>NavCommand</c>,
    /// which drives menu overlays.
    /// </summary>
    internal enum GameplayCommand {
        /// <summary>Describe the hero's current tile (position, terrain, items).</summary>
        ReadHere,

        /// <summary>Sweep everything in line of sight by direction and distance.</summary>
        Scan,

        /// <summary>Read the hero's vitals: health, stamina, energy, level, active effects.</summary>
        ReadStatus,

        /// <summary>Read the active hotbar page's bound abilities/items by slot.</summary>
        ReadHotbar,

        /// <summary>Repeat the last spoken phrase (handled in the pump, which holds the speech).</summary>
        RepeatLast,

        /// <summary>Toggle the look cursor (examine tiles without moving the hero).</summary>
        LookToggle,

        /// <summary>Re-center the look cursor on the hero.</summary>
        LookRecenter,

        // Step the look cursor one tile (only while it is active). +x east, +y north.
        LookNorth,
        LookSouth,
        LookEast,
        LookWest,
        LookNortheast,
        LookNorthwest,
        LookSoutheast,
        LookSouthwest,
    }
}
