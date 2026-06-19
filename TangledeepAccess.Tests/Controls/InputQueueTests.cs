using TangledeepAccess.Controls;
using TangledeepAccess.Speech;
using Xunit;

namespace TangledeepAccess.Tests.Controls {
    // InputQueue holds process-wide static state, so these run sequentially and each drains to a
    // clean slate at the start.
    [Collection("InputQueue")]
    public class InputQueueTests {
        // A stand-in drainer: the queue only needs a reference to tag each event with, so Claim and
        // Realize are no-ops here. Two distinct instances let us assert events route back to the
        // exact drainer that produced them.
        private sealed class FakeDrainer : InputDrainer {
            public override bool Claim(bool suppressWhileHeld) => false;

            public override void Realize(ModInputAction action, PrismSpeech speech) { }
        }

        private static readonly InputDrainer A = new FakeDrainer();
        private static readonly InputDrainer B = new FakeDrainer();

        public InputQueueTests() {
            InputQueue.Drain();
        }

        [Fact]
        public void DrainOnEmptyReturnsEmpty() {
            Assert.Empty(InputQueue.Drain());
        }

        [Fact]
        public void DrainReturnsEnqueuedEventsInArrivalOrderTaggedWithTheirSource() {
            InputQueue.Enqueue(A, ModInputAction.Move(0, 1));
            InputQueue.Enqueue(B, ModInputAction.Of(ModInputKind.ReadStatus));
            InputQueue.Enqueue(A, ModInputAction.Move(-1, 0));

            var drained = InputQueue.Drain();

            Assert.Equal(3, drained.Count);
            Assert.Same(A, drained[0].Source);
            Assert.Equal(ModInputKind.Move, drained[0].Action.Kind);
            Assert.Equal(1, drained[0].Action.Dy);
            Assert.Same(B, drained[1].Source);
            Assert.Equal(ModInputKind.ReadStatus, drained[1].Action.Kind);
            Assert.Same(A, drained[2].Source);
            Assert.Equal(-1, drained[2].Action.Dx);
        }

        [Fact]
        public void DrainEmptiesTheQueue() {
            InputQueue.Enqueue(A, ModInputAction.Of(ModInputKind.ReadHere));

            Assert.Single(InputQueue.Drain());
            Assert.Empty(InputQueue.Drain());
        }
    }
}
