using System.Collections.Generic;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// The object radar's per-sweep snapshot: the fixed, sorted list of entities to ping, one per
    /// cadence tick. <see cref="Load"/> takes a snapshot of the currently visible set and sorts it by
    /// x then y (column-major, left-to-right, near-to-far on each column), so every sweep — including
    /// the first — plays in that spatial order. <see cref="Next"/> walks the snapshot once and reports
    /// completion via <see cref="SweepDone"/>; the radar then rests and reloads a fresh snapshot for
    /// the next sweep. Entities discovered mid-sweep do not barge in — they appear in the next
    /// snapshot. Pure (Core): payload is integer tile offsets, so it tests without the engine.
    /// </summary>
    public sealed class ObjectRadarRing {
        public struct Entry {
            public int X;
            public int Y;
            public RadarCategory Category; // selects the ping voice; carried, not used by ordering

            public Entry(int x, int y, RadarCategory category = RadarCategory.Default) {
                X = x;
                Y = y;
                Category = category;
            }
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int _cursor; // index Next() will return

        public int Count => _entries.Count;

        /// <summary>True once the current snapshot is exhausted (or empty) — the sweep is complete.</summary>
        public bool SweepDone => _cursor >= _entries.Count;

        /// <summary>
        /// Replace the snapshot with <paramref name="current"/>, sorted by x then y, and rewind to the
        /// start. This is the only way entities enter the sweep, so membership and order are frozen for
        /// the whole sweep.
        /// </summary>
        public void Load(IList<Entry> current) {
            _entries.Clear();
            _entries.AddRange(current);
            _entries.Sort(CompareXThenY);
            _cursor = 0;
        }

        /// <summary>The next entity to ping, advancing the cursor; null once the sweep is done.</summary>
        public Entry? Next() {
            if (_cursor >= _entries.Count) {
                return null;
            }

            return _entries[_cursor++];
        }

        public void Clear() {
            _entries.Clear();
            _cursor = 0;
        }

        // Spatial sweep order: column-major, x ascending then y ascending.
        private static int CompareXThenY(Entry p, Entry q) {
            int c = p.X.CompareTo(q.X);
            return c != 0 ? c : p.Y.CompareTo(q.Y);
        }
    }
}
