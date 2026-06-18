using TangledeepAccess.Speech;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// Realizes the focus source's events. A focus change owns no key and never suppresses game
    /// input, so this is a pure <see cref="IInputRealizer"/> — the realize seam without the
    /// drainer's claim/suppress half. When the pump drains a <see cref="ModInputKind.FocusChanged"/>
    /// event it hands the dispatcher the freshly-published <see cref="FocusWatcher.CurrentFocus"/>;
    /// the per-frame menu idle-tick then reconciles and speaks the new focus. This replaces the old
    /// ChangeUIFocus Harmony postfix that fed <c>RecordGameFocus</c> directly.
    /// </summary>
    internal sealed class FocusSource : IInputRealizer {
        public static readonly FocusSource Instance = new FocusSource();

        public void Realize(ModInputAction action, PrismSpeech speech) {
            UiRuntime.Dispatcher?.RecordGameFocus(FocusWatcher.CurrentFocus);
        }
    }
}
