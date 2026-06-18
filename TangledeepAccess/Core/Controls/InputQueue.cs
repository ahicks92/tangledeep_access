using System;
using System.Collections.Generic;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// The buffer of recognized input events, handed from their producers (a key drainer claiming
    /// in the game's input hook, or a non-keyboard <see cref="IInputRealizer"/> source polling) to
    /// the per-frame pump (<c>Plugin.Update</c>) that drains and realizes them. Same record-in-the-
    /// producer / drain-in-the-pump discipline as <c>GameEventLog</c>: producers only record, the
    /// pump acts.
    ///
    /// <para>A queue, not a single slot — events are appended and drained in order, nothing is
    /// silently overwritten. In practice a hook call claims at most one key, so usually one event
    /// lands per frame; the queue makes that an observed outcome rather than a load-bearing
    /// assumption. Main-thread only (Unity input and the pump both run on the Unity thread), so no
    /// locking.</para>
    /// </summary>
    public static class InputQueue {
        private static readonly Queue<PendingInput> Pending = new Queue<PendingInput>();

        /// <summary>Buffer one event, tagged with the producer that will realize it.</summary>
        public static void Enqueue(IInputRealizer source, ModInputAction action) {
            Pending.Enqueue(new PendingInput { Source = source, Action = action });
        }

        /// <summary>Drain all buffered events in arrival order; empty if nothing is pending.</summary>
        public static IReadOnlyList<PendingInput> Drain() {
            if (Pending.Count == 0) {
                return Array.Empty<PendingInput>();
            }

            var drained = new List<PendingInput>(Pending.Count);
            while (Pending.Count > 0) {
                drained.Add(Pending.Dequeue());
            }

            return drained;
        }
    }
}
