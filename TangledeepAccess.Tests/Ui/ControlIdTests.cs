using System.Collections.Generic;
using TangledeepAccess.Ui;
using Xunit;

namespace TangledeepAccess.Tests.Ui
{
    public class ControlIdTests
    {
        [Fact]
        public void StructuralEqualityIgnoresReference()
        {
            var a = ControlId.Referenced(new object(), "k");
            var b = ControlId.Referenced(new object(), "k");
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void DifferentStructuralKeysAreNotEqual()
        {
            Assert.NotEqual(ControlId.Structural("a"), ControlId.Structural("b"));
        }

        [Fact]
        public void ReferenceMatchesIsIdentityOnly()
        {
            var obj = new object();
            var id = ControlId.Referenced(obj, "k");
            Assert.True(id.ReferenceMatches(obj));
            Assert.False(id.ReferenceMatches(new object()));
            Assert.False(ControlId.Structural("k").ReferenceMatches(obj));
        }

        [Fact]
        public void ForObjectUsesObjectAsBothTiers()
        {
            var obj = new object();
            var id = ControlId.ForObject(obj);
            Assert.True(id.ReferenceMatches(obj));
            Assert.Equal(id, ControlId.ForObject(obj)); // structural == identity
            Assert.NotEqual(id, ControlId.ForObject(new object()));
        }

        [Fact]
        public void UsableAsDictionaryKeyByStructuralIdentity()
        {
            var dict = new Dictionary<ControlId, int>();
            dict[ControlId.Referenced(new object(), "k")] = 1;
            // A different instance with the same structural key resolves the same slot.
            Assert.True(dict.ContainsKey(ControlId.Structural("k")));
            Assert.Equal(1, dict[ControlId.Referenced(new object(), "k")]);
        }

        [Fact]
        public void CompositeStructuralKeysWork()
        {
            var a = ControlId.Structural(("slot", 3));
            var b = ControlId.Structural(("slot", 3));
            var c = ControlId.Structural(("slot", 4));
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }
    }
}
