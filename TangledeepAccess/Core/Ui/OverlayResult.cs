namespace TangledeepAccess.Ui {
    public enum OverlayResultKind {
        /// <summary>This handler is not driving a GUI right now.</summary>
        Inactive,

        /// <summary>"I am awake but cannot build my info right now." Keeps the overlay's
        /// id active (cache preserved) but renders and speaks nothing this tick.</summary>
        Sleeping,

        /// <summary>This handler is driving a GUI; build and reconcile the overlay.</summary>
        Active,
    }

    /// <summary>
    /// A handler's per-tick verdict. The dispatcher walks handlers top-down and the first
    /// non-<see cref="Inactive"/> result wins. The <see cref="Sleeping"/> sentinel is the
    /// crucial middle state: an overlay that is conceptually open but momentarily unable to
    /// build (data not ready) returns Sleeping so its focus cache is preserved — returning
    /// Inactive instead would clear it and lose the player's position.
    /// </summary>
    public sealed class OverlayResult {
        public OverlayResultKind Kind { get; private set; }

        /// <summary>Valid for Sleeping and Active.</summary>
        public OverlayId Id { get; private set; }

        /// <summary>Valid for Active only.</summary>
        public IUiOverlay Overlay { get; private set; }

        /// <summary>Shared inactive sentinel; handlers may also return null to mean inactive.</summary>
        public static readonly OverlayResult Inactive = new OverlayResult {
            Kind = OverlayResultKind.Inactive,
        };

        public static OverlayResult Sleeping(OverlayId id) {
            return new OverlayResult { Kind = OverlayResultKind.Sleeping, Id = id };
        }

        public static OverlayResult Active(IUiOverlay overlay) {
            return new OverlayResult {
                Kind = OverlayResultKind.Active,
                Id = overlay.Id,
                Overlay = overlay,
            };
        }
    }

    /// <summary>A registered overlay source, polled every tick. May return null (== Inactive).</summary>
    public delegate OverlayResult OverlayHandler();
}
