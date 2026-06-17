namespace TangledeepAccess.Ui {
    /// <summary>
    /// A player navigation input fed into the dispatcher. Our own key handling translates
    /// arrows + enter into these and applies them to the active overlay's graph, replacing
    /// the game's input for the menus we drive.
    /// </summary>
    public enum NavCommand {
        Up,
        Right,
        Down,
        Left,
        Activate,
    }
}
