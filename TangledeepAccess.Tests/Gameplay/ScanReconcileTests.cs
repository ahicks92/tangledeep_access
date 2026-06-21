using System.Collections.Generic;
using TangledeepAccess.Gameplay;
using Xunit;

namespace TangledeepAccess.Tests.Gameplay {
    public class ScanReconcileTests {
        private static List<string> Keys(params string[] keys) => new List<string>(keys);

        [Fact]
        public void EmptyNewViewYieldsNoSelection() {
            Assert.Equal(-1, ScanReconcile.Resolve("a", Keys("a", "b"), Keys()));
        }

        [Fact]
        public void NoPriorSelectionStartsAtNearest() {
            // First scan ever: land on index 0 (the nearest).
            Assert.Equal(0, ScanReconcile.Resolve(null, null, Keys("a", "b", "c")));
        }

        [Fact]
        public void StaysOnTheSameFeatureByKey() {
            // The selected key is still present, even at a new index after a re-sort.
            int idx = ScanReconcile.Resolve("c", Keys("a", "b", "c"), Keys("c", "a", "b"));
            Assert.Equal(0, idx);
        }

        [Fact]
        public void VanishedSelectionFallsBackToNearestSurvivorBackward() {
            // We were on "c" (index 2) and it vanished. Walk the prior order backward: "b" survives.
            var prior = Keys("a", "b", "c", "d");
            var now = Keys("a", "b", "d"); // c gone
            int idx = ScanReconcile.Resolve("c", prior, now);
            Assert.Equal("b", now[idx]);
        }

        [Fact]
        public void WalksPastMultipleVanishedToTheNearestSurvivor() {
            // "c" and "b" both vanished; the backward walk reaches "a".
            var prior = Keys("a", "b", "c", "d");
            var now = Keys("a", "d");
            int idx = ScanReconcile.Resolve("c", prior, now);
            Assert.Equal("a", now[idx]);
        }

        [Fact]
        public void FallsBackToNearestWhenNothingInPriorOrderSurvives() {
            // The whole front of the prior order is gone; nothing to walk back to → new nearest (index 0).
            var prior = Keys("a", "b", "c");
            var now = Keys("x", "y");
            int idx = ScanReconcile.Resolve("c", prior, now);
            Assert.Equal(0, idx);
        }

        [Fact]
        public void FallsBackToNearestWhenPriorKeyAbsentFromPriorOrder() {
            // The selection isn't even in the captured order (defensive) and isn't in the new view:
            // there is nothing to walk, so land on the nearest.
            int idx = ScanReconcile.Resolve("z", Keys("a", "b"), Keys("a", "b"));
            Assert.Equal(0, idx);
        }

        [Fact]
        public void NullPriorOrderWithVanishedKeyFallsBackToNearest() {
            int idx = ScanReconcile.Resolve("gone", null, Keys("a", "b"));
            Assert.Equal(0, idx);
        }
    }
}
