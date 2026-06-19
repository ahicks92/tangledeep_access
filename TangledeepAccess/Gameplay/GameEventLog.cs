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
    ///
    /// <para>Every captured line is also retained in a capped <see cref="History"/> the player can
    /// scroll: <see cref="AppendOlder"/> / <see cref="AppendNewer"/> (Ctrl+[ / Ctrl+]) step a browse
    /// cursor through it. Each new line snaps the cursor back to the latest, so scrolling back is
    /// always relative to "now" — you never have to scroll forward through a backlog after a flurry
    /// of combat.</para>
    /// </summary>
    internal static class GameEventLog {
        private static readonly Queue<string> Pending = new Queue<string>();

        // Browsable history of captured lines (oldest first), capped so a long session does not grow
        // unbounded, and the cursor the player scrolls through it with. -1 = empty.
        private const int HistoryCap = 100;
        private static readonly List<string> History = new List<string>();
        private static int _cursor = -1;

        /// <summary>Buffer one already-cleaned log line for speech and record it in history. Empty
        /// lines are ignored. Recording snaps the browse cursor to this newest line.</summary>
        public static void Enqueue(string line) {
            if (string.IsNullOrEmpty(line)) {
                return;
            }

            Pending.Enqueue(line);
            History.Add(line);
            if (History.Count > HistoryCap) {
                History.RemoveAt(0);
            }

            _cursor = History.Count - 1; // a new message jumps the browse cursor to the latest
        }

        /// <summary>
        /// Drain all buffered lines into one spoken message (space-joined sentences), or null if
        /// nothing is pending.
        /// </summary>
        public static MessageBuilder DrainToMessage() {
            if (Pending.Count == 0) {
                return null;
            }

            var message = new MessageBuilder();
            while (Pending.Count > 0) {
                message.Fragment(Pending.Dequeue());
            }

            return message;
        }

        /// <summary>Step the browse cursor to the older (previous) history line and append it (Ctrl+[).</summary>
        public static void AppendOlder(MessageBuilder message) {
            Step(message, -1);
        }

        /// <summary>Step the browse cursor to the newer (next) history line and append it (Ctrl+]).</summary>
        public static void AppendNewer(MessageBuilder message) {
            Step(message, 1);
        }

        // Move the cursor one line in the given direction, clamped. At the boundary it stays put and
        // re-reads the edge line, prefixed with "oldest"/"latest" so the dead end is audible.
        private static void Step(MessageBuilder message, int dir) {
            if (History.Count == 0) {
                message.Fragment("no log history");
                return;
            }

            int next = _cursor + dir;
            if (next < 0) {
                next = 0;
            } else if (next > History.Count - 1) {
                next = History.Count - 1;
            }

            if (next == _cursor) {
                message.Fragment(dir < 0 ? "oldest" : "latest").ListItem();
            }

            _cursor = next;
            message.Fragment(History[_cursor]);
        }
    }
}
