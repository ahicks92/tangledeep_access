namespace TangledeepAccess.Controls {
    /// <summary>
    /// One frame's recognized input, handed from the hook (which knows the active context) to the
    /// pump (which realizes it). Pairing the action with its context makes the hook's routing
    /// decision authoritative, so the pump never re-derives context — which could race a mid-frame
    /// state change (e.g. the look cursor toggling off as the action is realized).
    /// </summary>
    public struct PendingInput {
        public InputContext Context;
        public ModInputAction Action;
    }
}
