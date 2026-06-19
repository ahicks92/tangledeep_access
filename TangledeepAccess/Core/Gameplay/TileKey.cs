using System;
using TangledeepAccess.Speech;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// The spoken identity of a tile for the exploration cursor: its <see cref="Terrain"/> word and
    /// its <see cref="Shape"/>. Movement announces only the parts of this key that changed since the
    /// previous tile (so "ground alcove" → "hallway" when only the shape changed), which keeps
    /// stepping terse. Dynamic contents (entities, items) are NOT in the key — they are read every
    /// time, not differentially — and more fields may join the key later, so callers must not treat
    /// these two as the complete description of a tile.
    ///
    /// <para>Pure (BCL + Core only) so it lives in Core and is unit-tested off-engine; the engine
    /// layer builds it from a live <c>MapTileData</c>.</para>
    /// </summary>
    public readonly struct TileKey : IEquatable<TileKey> {
        public readonly string Terrain;
        public readonly TileShape Shape;

        public TileKey(string terrain, TileShape shape) {
            Terrain = terrain;
            Shape = shape;
        }

        /// <summary>
        /// Append only the fields that differ from <paramref name="previous"/> — terrain word and/or
        /// shape. A null <paramref name="previous"/> (no prior context) appends both, i.e. a full read.
        /// </summary>
        public void AppendChanges(MessageBuilder message, TileKey? previous) {
            bool full = previous == null;
            if (full || previous.Value.Terrain != Terrain) {
                message.Fragment(Terrain);
            }
            if (full || previous.Value.Shape != Shape) {
                message.Fragment(Shape.Speak()); // null (open) ignored by the builder
            }
        }

        /// <summary>Append the whole key (terrain + shape), no differencing.</summary>
        public void AppendFull(MessageBuilder message) {
            message.Fragment(Terrain);
            message.Fragment(Shape.Speak());
        }

        public bool Equals(TileKey other) => Terrain == other.Terrain && Shape == other.Shape;

        public override bool Equals(object obj) => obj is TileKey other && Equals(other);

        public override int GetHashCode() => unchecked(((Terrain?.GetHashCode() ?? 0) * 397) ^ Shape.GetHashCode());

        public static bool operator ==(TileKey a, TileKey b) => a.Equals(b);

        public static bool operator !=(TileKey a, TileKey b) => !a.Equals(b);
    }
}
