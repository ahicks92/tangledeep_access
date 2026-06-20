namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// What kind of thing a scanned entity is, which selects the radar ping's voice: a per-category
    /// sample (monster, container, powerup, shop, stairs) or, for <see cref="Default"/>, the radar's
    /// triangle tone. Classified from the entity's game type at collection time and carried through the
    /// radar snapshot to the ping. Pure (Core): the enum is data; the engine maps it to samples.
    /// </summary>
    public enum RadarCategory {
        Default,
        Monster,
        Container,
        Powerup,
        Shop,
        Stairs,
    }
}
