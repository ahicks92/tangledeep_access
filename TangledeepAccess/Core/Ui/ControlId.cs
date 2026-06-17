using System;

namespace TangledeepAccess.Ui
{
    /// <summary>
    /// The identity of a control (graph node). Replaces Factorio Access's plain string
    /// node key with a two-tier identity so focus can be followed across rebuilds even
    /// when the world shifts under us.
    ///
    /// <para><b>Reference</b> (optional) is the game/domain object a node was derived from
    /// (e.g. a <c>UIManagerScript.UIObject</c> or an <c>Item</c>), compared by reference
    /// identity. <b>StructuralKey</b> (always present) is a value-equatable key — a string,
    /// or a composite such as <c>(slotKind, index)</c> or a content hash.</para>
    ///
    /// <para>Two controls are "the same" when their references are identical (tier 1, a
    /// perfect match) OR their structural keys are equal (tier 2). Tier 1 lets us follow an
    /// object that moved (its slot-based key changed); tier 2 lets us follow a logical
    /// control whose backing object was rebuilt (new instance, same identity).</para>
    ///
    /// <para>Equality/hashing of this type is defined on <see cref="StructuralKey"/> alone,
    /// so it is a stable dictionary key (the graph stores nodes and traversal order by it).
    /// The reference tier is metadata, applied explicitly during focus reconciliation and
    /// game-focus sync via <see cref="ReferenceMatches"/>.</para>
    /// </summary>
    public sealed class ControlId : IEquatable<ControlId>
    {
        /// <summary>The originating game/domain object, or null. Matched by reference identity.</summary>
        public object Reference { get; }

        /// <summary>The value-equatable structural identity. Never null.</summary>
        public object StructuralKey { get; }

        private ControlId(object reference, object structuralKey)
        {
            if (structuralKey == null)
                throw new ArgumentNullException(nameof(structuralKey));
            Reference = reference;
            StructuralKey = structuralKey;
        }

        /// <summary>A control identified only by a structural key (no backing object).</summary>
        public static ControlId Structural(object structuralKey)
        {
            return new ControlId(null, structuralKey);
        }

        /// <summary>A control with both tiers: a backing object and a structural key.</summary>
        public static ControlId Referenced(object reference, object structuralKey)
        {
            return new ControlId(reference, structuralKey);
        }

        /// <summary>
        /// A control identified by a backing object only. The object doubles as the
        /// structural key (so equality collapses to identity). Use this when no better
        /// structural key is available, e.g. wrapping a raw game widget.
        /// </summary>
        public static ControlId ForObject(object reference)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            return new ControlId(reference, reference);
        }

        /// <summary>Tier-1 test: is <paramref name="obj"/> this control's backing object?</summary>
        public bool ReferenceMatches(object obj)
        {
            return Reference != null && ReferenceEquals(Reference, obj);
        }

        public bool Equals(ControlId other)
        {
            return other != null && Equals(StructuralKey, other.StructuralKey);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ControlId);
        }

        public override int GetHashCode()
        {
            return StructuralKey.GetHashCode();
        }

        public override string ToString()
        {
            return Reference == null
                ? "ControlId(" + StructuralKey + ")"
                : "ControlId(" + StructuralKey + ", ref=" + Reference + ")";
        }
    }
}
