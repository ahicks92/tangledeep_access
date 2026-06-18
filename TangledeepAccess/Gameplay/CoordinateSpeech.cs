using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Engine-side <see cref="MessageBuilder"/> extensions for speaking grid vectors. They live
    /// outside Core (and so cannot be on <see cref="MessageBuilder"/> itself) because they take
    /// Unity's <see cref="Vector2"/>, which Core's BCL-only rule forbids — extension methods are
    /// the seam that lets the pure builder still speak our engine vector type. They do no math:
    /// the caller passes the already-computed map position (absolute) or offset, e.g.
    /// <c>target - hero</c> (relative). This is the one spoken form of a coordinate, so every
    /// "where is it" reads identically. Each appends like <see cref="MessageBuilder.Fragment"/>;
    /// the caller sets any list boundary.
    /// </summary>
    public static class CoordinateSpeech {
        /// <summary>
        /// Append an absolute tile coordinate as "x, y" (e.g. "5, 12"). The vector is a map
        /// position; only its integer tile components are spoken.
        /// </summary>
        public static MessageBuilder PushAbsoluteCoordinates(this MessageBuilder message, Vector2 position) {
            return message.Fragment((int)position.x + ", " + (int)position.y);
        }

        /// <summary>
        /// Append a relative offset as a spoken, screen-relative direction and distance (e.g.
        /// "3 right, 2 up", or "here"). The vector is the offset the caller already computed
        /// (target - hero); the wording comes from <see cref="GridDirection.Offset"/>.
        /// </summary>
        public static MessageBuilder PushRelativeCoordinates(this MessageBuilder message, Vector2 offset) {
            return message.Fragment(GridDirection.Offset((int)offset.x, (int)offset.y));
        }
    }
}
