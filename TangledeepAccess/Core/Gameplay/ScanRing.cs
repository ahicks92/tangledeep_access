using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// The scanner's rotating list of entities to ping, one per cadence tick. Entities are
    /// identified by reference (the underlying game object), so a thing that merely moves keeps its
    /// slot with refreshed coordinates, while a genuinely new thing is inserted at the cursor — the
    /// next to play — so it pings right away instead of waiting at the back of the queue. No sorting:
    /// reconcile preserves order and only adds/removes/refreshes. Pure (Core): identity is by
    /// reference, payload is integer tile offsets, so it tests without the engine.
    /// </summary>
    public sealed class ScanRing {
        public struct Entry {
            public object Id;
            public int X;
            public int Y;

            public Entry(object id, int x, int y) {
                Id = id;
                X = x;
                Y = y;
            }
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int _cursor; // index Next() will return

        public int Count => _entries.Count;

        /// <summary>
        /// Update to the current set of visible entities: refresh survivors' coordinates, drop the
        /// gone (keeping the cursor pointing at the same upcoming entity), and insert newcomers at
        /// the cursor so they play next.
        /// </summary>
        public void Reconcile(IList<Entry> current) {
            var byId = new Dictionary<object, Entry>(ReferenceComparer.Instance);
            for (int i = 0; i < current.Count; i++) {
                byId[current[i].Id] = current[i];
            }

            // Refresh survivors / remove the gone. Backwards so removals don't shift unseen indices,
            // and so the cursor can track removals below it. Track which ids survived.
            var survived = new HashSet<object>(ReferenceComparer.Instance);
            for (int i = _entries.Count - 1; i >= 0; i--) {
                Entry updated;
                if (byId.TryGetValue(_entries[i].Id, out updated)) {
                    _entries[i] = updated; // same entity, refreshed coordinates
                    survived.Add(_entries[i].Id);
                } else {
                    _entries.RemoveAt(i);
                    if (i < _cursor) {
                        _cursor--;
                    }
                }
            }

            if (_cursor >= _entries.Count) {
                _cursor = 0;
            }

            // Newcomers (in current order, not dictionary order) go in at the cursor as a block, so
            // they play next and keep their relative order. The cursor stays on the first newcomer.
            int insertAt = _cursor;
            for (int i = 0; i < current.Count; i++) {
                if (!survived.Contains(current[i].Id)) {
                    _entries.Insert(insertAt, current[i]);
                    insertAt++;
                }
            }
        }

        /// <summary>The next entity to ping, advancing the cursor; null if empty.</summary>
        public Entry? Next() {
            if (_entries.Count == 0) {
                return null;
            }
            if (_cursor >= _entries.Count) {
                _cursor = 0;
            }

            Entry e = _entries[_cursor];
            _cursor = (_cursor + 1) % _entries.Count;
            return e;
        }

        public void Clear() {
            _entries.Clear();
            _cursor = 0;
        }

        // Reference identity regardless of any Equals/== overrides (UnityEngine.Object overrides ==).
        private sealed class ReferenceComparer : IEqualityComparer<object> {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            bool IEqualityComparer<object>.Equals(object a, object b) {
                return ReferenceEquals(a, b);
            }

            int IEqualityComparer<object>.GetHashCode(object obj) {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
