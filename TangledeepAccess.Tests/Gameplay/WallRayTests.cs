using System.Collections.Generic;
using TangledeepAccess.Gameplay;
using Xunit;

namespace TangledeepAccess.Tests.Gameplay {
    public class WallRayTests {
        // A tiny map model: a set of wall tiles and a set of currently-visible tiles, both keyed
        // "x,y". Everything in [0,100) is in bounds.
        private static int? Cast(
            int ox, int oy, int dx, int dy, int maxTiles,
            HashSet<string> walls, HashSet<string> visible) {
            return WallRay.DistanceToWall(
                ox, oy, dx, dy, maxTiles,
                (x, y) => x >= 0 && x < 100 && y >= 0 && y < 100,
                (x, y) => walls.Contains(x + "," + y),
                (x, y) => visible.Contains(x + "," + y));
        }

        private static HashSet<string> Set(params string[] keys) => new HashSet<string>(keys);

        [Fact]
        public void PingsVisibleWallEvenWhenAnIntermediateTileIsNotVisible() {
            // The live bug: hero (10,6), wall (10,0) is visible, but the tile (10,3) between them
            // is passable and NOT in the visible set. The ray must still reach and ping the wall.
            var walls = Set("10,0");
            var visible = Set("10,5", "10,4", "10,2", "10,1", "10,0"); // 10,3 deliberately missing
            Assert.Equal(6, Cast(10, 6, 0, -1, 8, walls, visible));
        }

        [Fact]
        public void NoWallInRangeIsNull() {
            // Far from any boundary so the map edge is out of range too.
            Assert.Null(Cast(10, 20, 0, -1, 8, Set(), Set()));
        }

        [Fact]
        public void WallOutOfRangeIsNull() {
            // Wall 10 tiles south but range is 8; origin kept clear of the map edge.
            var walls = Set("10,10"); // d=10 from y=20, out of range
            Assert.Null(Cast(10, 20, 0, -1, 8, walls, Set("10,10")));
        }

        [Fact]
        public void InvisibleWallIsNotPinged() {
            // Wall in range but not currently visible (dark/unexplored) — no ping.
            var walls = Set("10,2");
            Assert.Null(Cast(10, 6, 0, -1, 8, walls, Set()));
        }

        [Fact]
        public void NearestWallWins() {
            var walls = Set("10,4", "10,1");
            var visible = Set("10,4", "10,1");
            Assert.Equal(2, Cast(10, 6, 0, -1, 8, walls, visible));
        }

        [Fact]
        public void PingsMapEdgeAsWall() {
            // Heading south from y=2 with no wall: leaves bounds at y=-1 (d=3). The map edge is
            // itself a wall and must ping at that distance — the live bug was it returning null.
            Assert.Equal(3, Cast(10, 2, 0, -1, 8, Set(), Set()));
        }

        [Fact]
        public void MapEdgePingIsNotVisibilityGated() {
            // No tiles visible at all, but the world boundary is always known — still pings.
            Assert.Equal(3, Cast(10, 2, 0, -1, 8, Set(), Set()));
        }

        [Fact]
        public void InBoundsWallBeatsFartherMapEdge() {
            // A visible wall before the edge wins over the edge behind it.
            var walls = Set("10,1");
            Assert.Equal(1, Cast(10, 2, 0, -1, 8, walls, Set("10,1")));
        }
    }
}
