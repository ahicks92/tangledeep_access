namespace TangledeepAccess.Controls {
    /// <summary>
    /// One frame's recognized input, handed from its producer (a key drainer's hook claim, or a
    /// non-keyboard source's poll) to the pump that realizes it. It carries the
    /// <see cref="IInputRealizer"/> that produced it, so the pump dispatches straight back to that
    /// producer's <see cref="IInputRealizer.Realize"/> — the producing decision stays authoritative
    /// and is never re-derived.
    /// </summary>
    public struct PendingInput {
        public IInputRealizer Source;
        public ModInputAction Action;
    }
}
