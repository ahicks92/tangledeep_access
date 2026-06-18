using TangledeepAccess.Gameplay;
using Xunit;

namespace TangledeepAccess.Tests.Gameplay {
    public class GridDirectionTests {
        [Fact]
        public void OffsetZeroIsHere() {
            Assert.Equal("here", GridDirection.Offset(0, 0));
        }

        [Fact]
        public void OffsetAxesAreScreenRelative() {
            // +x east => "right", +y north => "up".
            Assert.Equal("3 up", GridDirection.Offset(0, 3));
            Assert.Equal("2 down", GridDirection.Offset(0, -2));
            Assert.Equal("5 right", GridDirection.Offset(5, 0));
            Assert.Equal("4 left", GridDirection.Offset(-4, 0));
        }

        [Fact]
        public void OffsetSpeaksXBeforeY() {
            Assert.Equal("3 right, 2 up", GridDirection.Offset(3, 2));
            Assert.Equal("4 left, 1 down", GridDirection.Offset(-4, -1));
        }

        [Fact]
        public void CompassEightWay() {
            Assert.Equal("north", GridDirection.Compass(0, 1));
            Assert.Equal("south", GridDirection.Compass(0, -1));
            Assert.Equal("east", GridDirection.Compass(1, 0));
            Assert.Equal("west", GridDirection.Compass(-1, 0));
            Assert.Equal("northeast", GridDirection.Compass(1, 1));
            Assert.Equal("northwest", GridDirection.Compass(-1, 1));
            Assert.Equal("southeast", GridDirection.Compass(1, -1));
            Assert.Equal("southwest", GridDirection.Compass(-1, -1));
            Assert.Equal("here", GridDirection.Compass(0, 0));
        }

        [Fact]
        public void StepsIsChebyshev() {
            Assert.Equal(3, GridDirection.Steps(3, 2));
            Assert.Equal(4, GridDirection.Steps(-1, -4));
            Assert.Equal(0, GridDirection.Steps(0, 0));
        }
    }
}
