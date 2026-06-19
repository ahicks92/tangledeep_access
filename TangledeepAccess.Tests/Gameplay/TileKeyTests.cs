using TangledeepAccess.Gameplay;
using TangledeepAccess.Speech;
using Xunit;

namespace TangledeepAccess.Tests.Gameplay {
    public class TileKeyTests {
        private static readonly TileShape Open = new TileShape(TileShapeKind.Open, Direction.None, 0);
        private static readonly TileShape Alcove = new TileShape(TileShapeKind.Alcove, Direction.North, 1);

        private static string Changes(TileKey key, TileKey? previous) {
            var b = new MessageBuilder();
            key.AppendChanges(b, previous);
            return b.Build();
        }

        [Fact]
        public void NoPreviousReadsBothFields() {
            Assert.Equal("ground north alcove", Changes(new TileKey("ground", Alcove), null));
        }

        [Fact]
        public void OpenShapeReadsOnlyTerrain() {
            // Open's Speak() is null, so it contributes no word.
            Assert.Equal("ground", Changes(new TileKey("ground", Open), null));
        }

        [Fact]
        public void SpeaksOnlyTheChangedShape() {
            var prev = new TileKey("ground", Open);
            Assert.Equal("north alcove", Changes(new TileKey("ground", Alcove), prev));
        }

        [Fact]
        public void SpeaksOnlyTheChangedTerrain() {
            var prev = new TileKey("ground", Alcove);
            Assert.Equal("water", Changes(new TileKey("water", Alcove), prev));
        }

        [Fact]
        public void SpeaksNothingWhenUnchanged() {
            var prev = new TileKey("ground", Alcove);
            Assert.Null(Changes(new TileKey("ground", Alcove), prev));
        }
    }
}
