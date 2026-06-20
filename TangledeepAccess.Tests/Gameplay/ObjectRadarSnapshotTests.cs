using System.Collections.Generic;
using TangledeepAccess.Gameplay;
using Xunit;

namespace TangledeepAccess.Tests.Gameplay {
    public class ObjectRadarSnapshotTests {
        private static IList<ObjectRadarSnapshot.Entry> Set(params (int x, int y)[] items) {
            var list = new List<ObjectRadarSnapshot.Entry>();
            foreach (var it in items) {
                list.Add(new ObjectRadarSnapshot.Entry(it.x, it.y));
            }
            return list;
        }

        // Drain a loaded ring into the (x, y) sequence it pings.
        private static List<(int x, int y)> Sweep(ObjectRadarSnapshot ring) {
            var order = new List<(int, int)>();
            ObjectRadarSnapshot.Entry? e;
            while ((e = ring.Next()).HasValue) {
                order.Add((e.Value.X, e.Value.Y));
            }
            return order;
        }

        [Fact]
        public void EmptySnapshotIsDoneAndYieldsNull() {
            var ring = new ObjectRadarSnapshot();
            Assert.True(ring.SweepDone);
            Assert.Null(ring.Next());
        }

        [Fact]
        public void LoadSortsByXThenYImmediately() {
            var ring = new ObjectRadarSnapshot();
            ring.Load(Set((2, 5), (1, 9), (1, 3)));
            // Column-major: x ascending, then y ascending -> (1,3), (1,9), (2,5).
            Assert.Equal(new List<(int, int)> { (1, 3), (1, 9), (2, 5) }, Sweep(ring));
        }

        [Fact]
        public void NextReturnsNullAndSweepDoneAfterExhaustion() {
            var ring = new ObjectRadarSnapshot();
            ring.Load(Set((0, 0), (1, 0)));
            Assert.False(ring.SweepDone);
            ring.Next();
            Assert.False(ring.SweepDone); // one left
            ring.Next();
            Assert.True(ring.SweepDone); // exhausted
            Assert.Null(ring.Next());
        }

        [Fact]
        public void LoadReplacesTheSnapshotAndRewinds() {
            var ring = new ObjectRadarSnapshot();
            ring.Load(Set((5, 0), (6, 0)));
            ring.Next(); // consume one

            // A fresh snapshot fully replaces the old one and starts from the top.
            ring.Load(Set((3, 1), (2, 2)));
            Assert.Equal(2, ring.Count);
            Assert.False(ring.SweepDone);
            Assert.Equal(new List<(int, int)> { (2, 2), (3, 1) }, Sweep(ring));
        }

        [Fact]
        public void DoesNotWrap() {
            // Unlike a cycling ring, the snapshot is one-shot: after the last entry Next stays null
            // until the next Load.
            var ring = new ObjectRadarSnapshot();
            ring.Load(Set((0, 0)));
            Assert.NotNull(ring.Next()); // the single entry
            Assert.Null(ring.Next());    // no wrap back to the start
            Assert.Null(ring.Next());
        }

        [Fact]
        public void ClearEmptiesAndMarksDone() {
            var ring = new ObjectRadarSnapshot();
            ring.Load(Set((0, 0), (1, 1)));
            ring.Clear();
            Assert.Equal(0, ring.Count);
            Assert.True(ring.SweepDone);
            Assert.Null(ring.Next());
        }

        [Fact]
        public void CarriesCategoryThroughTheSnapshot() {
            var ring = new ObjectRadarSnapshot();
            ring.Load(new List<ObjectRadarSnapshot.Entry> {
                new ObjectRadarSnapshot.Entry(0, 0, RadarCategory.Monster),
            });
            Assert.Equal(RadarCategory.Monster, ring.Next().Value.Category);
        }
    }
}
