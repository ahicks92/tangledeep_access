using TangledeepAccess.Controls;
using Xunit;

namespace TangledeepAccess.Tests.Controls {
    public class FocusTrackerTests {
        // Stand-in focus references; identity is all the tracker compares.
        private static readonly object A = new object();
        private static readonly object B = new object();

        [Fact]
        public void InitialNullIsNotAnEdge() {
            var t = new FocusTracker();
            Assert.False(t.Observe(null)); // assumed initial state is "no focus"; a startup null is no change
        }

        [Fact]
        public void FirstRealFocusIsAnEdge() {
            var t = new FocusTracker();
            Assert.True(t.Observe(A));
        }

        [Fact]
        public void SameFocusIsNotAnEdge() {
            var t = new FocusTracker();
            Assert.True(t.Observe(A));
            Assert.False(t.Observe(A)); // unchanged => no event every frame
        }

        [Fact]
        public void ChangingFocusIsAnEdge() {
            var t = new FocusTracker();
            Assert.True(t.Observe(A));
            Assert.True(t.Observe(B));
        }

        [Fact]
        public void BecomingStaleIsAnEdge() {
            var t = new FocusTracker();
            Assert.True(t.Observe(A));
            Assert.True(t.Observe(null)); // focus went away (stale collapsed to null) => one edge
            Assert.False(t.Observe(null)); // still gone => no repeat
        }

        [Fact]
        public void RefocusAfterStaleIsAnEdge() {
            var t = new FocusTracker();
            t.Observe(A);
            t.Observe(null);
            Assert.True(t.Observe(A)); // same object refocused after going away is still a fresh edge
        }
    }
}
