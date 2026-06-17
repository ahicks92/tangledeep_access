using System.Collections.Generic;
using TangledeepAccess.Speech;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Buffers turn-by-turn game-log lines captured from <c>GameLogScript.GameLogWrite</c> (the
    /// game's centralized event sink: combat, status, pickups, NPC barks, the hero's intro
    /// monologue) for the per-frame pump to speak. Following the mod's discipline, the Harmony
    /// hook only enqueues; <see cref="DrainToMessage"/> is called once per frame from
    /// <c>Plugin.Update</c> and the result spoken there — never from the hook.
    ///
    /// <para>Lines accumulated within a frame are joined into one utterance so a multi-line turn
    /// (you hit, it dies, you gain XP) is a single spoken message rather than a stutter of
    /// interrupts. Main-thread only (the game writes the log and the pump drains it both on the
    /// Unity thread), so no locking.</para>
    /// </summary>
    internal static class GameEventLog {
        private static readonly Queue<string> Pending = new Queue<string>();

        /// <summary>Buffer one already-cleaned log line. Empty lines are ignored.</summary>
        public static void Enqueue(string line) {
            if (!string.IsNullOrEmpty(line)) {
                Pending.Enqueue(line);
            }
        }

        /// <summary>
        /// Drain all buffered lines into one spoken message (space-joined sentences), or null if
        /// nothing is pending.
        /// </summary>
        public static string DrainToMessage() {
            if (Pending.Count == 0) {
                return null;
            }

            var message = new MessageBuilder();
            while (Pending.Count > 0) {
                message.Fragment(Pending.Dequeue());
            }

            return message.Build();
        }
    }
}
