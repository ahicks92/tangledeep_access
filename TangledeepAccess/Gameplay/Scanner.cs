using System.Collections.Generic;
using TangledeepAccess.Controls;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// The broad buckets the scanner sorts map features into — the top axis of navigation
    /// ("what kind of thing, in general?"). Coarse on purpose: the boundaries are a first guess
    /// pending real UX, and <see cref="Scanner.Categorize"/> is a single switch that is trivial to
    /// re-cut (e.g. splitting shops out of <see cref="Services"/> on <c>NPC.shopRef</c>). The order
    /// here is not the iteration order — that is <see cref="Scanner.Order"/> — and <see cref="Other"/>
    /// is the unclassified fallback, deliberately left out of iteration so it is never surfaced.
    /// </summary>
    public enum ScanCategory {
        Monsters,
        Items,
        Services,
        Stairs,
        Objects,
        Other,
    }

    /// <summary>
    /// One navigable feature in the scanner: a single actor, identified by its stable
    /// <c>actorUniqueID</c> so selection survives a rebuild without ever caching the actor. Name and
    /// offset are recomputed live at build time. Today one entry is exactly one actor; the
    /// "instance" grouping (several actors collapsed into one entry — a pack, a scattered item pile)
    /// is the deferred third axis, and the seam for it is the group step in <see cref="Scanner.Build"/>.
    /// </summary>
    internal struct ScanEntry {
        public int ActorId;
        public string Name;
        public int Dx;
        public int Dy;
        public int Steps; // king-move distance from the hero, for nearest-first ordering
    }

    /// <summary>
    /// Input for the scanner, beside the state it drives — the same drainer+module split as the look
    /// cursor. Modeless: it claims only its four dedicated nav keys (comma/period for entries, 9/0 for
    /// categories) and passes everything else straight through, so it never fights the look cursor's
    /// arrows or the game's movement. The keys carry no payload; <see cref="Scanner"/> holds the
    /// selection and produces the speech.
    /// </summary>
    public sealed class ScannerInputDrainer : InputDrainer {
        public static readonly ScannerInputDrainer Instance = new ScannerInputDrainer();

        public override bool Claim(bool suppressWhileHeld) {
            ModInputAction? action = InputKeys.ScannerNav();
            if (action.HasValue) {
                InputQueue.Enqueue(this, action.Value);
                return true;
            }

            return false; // everything else is someone else's
        }

        public override void Realize(ModInputAction action, PrismSpeech speech) {
            // This drainer owns the message: one builder per press, handed to the scanner to append
            // to, so scanner output composes like any other (no nested builders, no re-injected strings).
            var message = new MessageBuilder();
            switch (action.Kind) {
                case ModInputKind.ScanNextCategory:
                    Scanner.NextCategory(message);
                    break;
                case ModInputKind.ScanPrevCategory:
                    Scanner.PrevCategory(message);
                    break;
                case ModInputKind.ScanNextEntry:
                    Scanner.NextEntry(message);
                    break;
                case ModInputKind.ScanPrevEntry:
                    Scanner.PrevEntry(message);
                    break;
            }

            speech.Speak(message);
        }
    }

    /// <summary>
    /// A categorized, distance-sorted readout of the map's actor-features — the non-visual analog of
    /// the minimap, modeled on Factorio Access's scanner but radically simplified: Tangledeep floors
    /// are small and every feature is a single tile (no multi-tile entities), so there is no
    /// background crawling, clustering, or bounding-box machinery. The whole structure is rebuilt
    /// live on every keypress by walking <c>actorsInMap</c>; nothing is cached but the selection,
    /// which is a stable <c>actorUniqueID</c> re-resolved each time.
    ///
    /// <para>Two navigation axes for now: category (the broad bucket) and entry (one feature within
    /// it, nearest first). The third Factorio axis — grouping several actors into one "instance" — is
    /// deliberately deferred; the data model leaves the seam open (see <see cref="Build"/>).</para>
    ///
    /// <para>Visibility follows the minimap, not line of sight: a feature is surfaced only on an
    /// <em>explored</em> tile (the single explored predicate in <see cref="Build"/>), so the scanner
    /// never reveals ground the player has not yet seen. That is parity with the sighted minimap,
    /// which is a live view of the whole explored map.</para>
    /// </summary>
    internal static class Scanner {
        // Iteration order for category navigation. Other is omitted so unclassified actors never
        // surface. This order is arbitrary and easy to change.
        private static readonly ScanCategory[] Order = {
            ScanCategory.Monsters,
            ScanCategory.Stairs,
            ScanCategory.Services,
            ScanCategory.Items,
            ScanCategory.Objects,
        };

        private static ScanCategory _category = ScanCategory.Monsters;
        private static int _selectedId; // actorUniqueID of the current entry, 0 = nothing selected

        /// <summary>
        /// Which category a map feature belongs to — the requested classifier. Coarse and keyed only
        /// on <see cref="ActorTypes"/> for v1; the hero is excluded before this is called, so it never
        /// returns a category for the player. Splitting shops out of services is a one-line change:
        /// add a case for <c>NPC</c> with a non-empty <c>shopRef</c>.
        /// </summary>
        public static ScanCategory Categorize(Actor a) {
            switch (a.GetActorType()) {
                case ActorTypes.MONSTER:
                    return ScanCategory.Monsters;
                case ActorTypes.ITEM:
                    return ScanCategory.Items;
                case ActorTypes.STAIRS:
                    return ScanCategory.Stairs;
                case ActorTypes.NPC:
                    return ScanCategory.Services; // shops + service NPCs lumped for now
                case ActorTypes.DESTRUCTIBLE:
                    return ScanCategory.Objects;
                default:
                    return ScanCategory.Other; // HERO and anything unmodeled — never surfaced
            }
        }

        /// <summary>Step to the next non-empty category and append its nearest entry.</summary>
        public static void NextCategory(MessageBuilder message) {
            StepCategory(message, 1);
        }

        /// <summary>Step to the previous non-empty category and append its nearest entry.</summary>
        public static void PrevCategory(MessageBuilder message) {
            StepCategory(message, -1);
        }

        /// <summary>Move to the next entry within the current category, wrapping, and append it.</summary>
        public static void NextEntry(MessageBuilder message) {
            StepEntry(message, 1);
        }

        /// <summary>Move to the previous entry within the current category, wrapping, and append it.</summary>
        public static void PrevEntry(MessageBuilder message) {
            StepEntry(message, -1);
        }

        /// <summary>Forget the selection (e.g. on a level change). Mirrors the look cursor's reset.</summary>
        public static void Reset() {
            _selectedId = 0;
        }

        // --- Navigation ---

        private static void StepCategory(MessageBuilder message, int dir) {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || MapMasterScript.activeMap == null) {
                return;
            }

            int start = IndexOf(_category);
            // Skip empty categories; wrap. i runs a full lap so we land back on the current
            // category only if it is the single non-empty one.
            for (int i = 1; i <= Order.Length; i++) {
                int idx = ((start + dir * i) % Order.Length + Order.Length) % Order.Length;
                ScanCategory cat = Order[idx];
                List<ScanEntry> entries = Build(cat, hero);
                if (entries.Count == 0) {
                    continue;
                }

                _category = cat;
                _selectedId = entries[0].ActorId;
                SpeakCategory(message, cat, entries, 0);
                return;
            }

            message.Fragment("nothing in range");
        }

        private static void StepEntry(MessageBuilder message, int dir) {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || MapMasterScript.activeMap == null) {
                return;
            }

            List<ScanEntry> entries = Build(_category, hero);
            // First use, or the current category emptied out: bootstrap onto the first non-empty
            // category instead of saying nothing.
            if (entries.Count == 0) {
                StepCategory(message, dir);
                return;
            }

            int cur = IndexOfId(entries, _selectedId);
            int next = cur < 0
                ? (dir > 0 ? 0 : entries.Count - 1)
                : (cur + dir + entries.Count) % entries.Count;
            _selectedId = entries[next].ActorId;
            SpeakEntry(message, entries[next], next, entries.Count);
        }

        // --- Build (live, never cached) ---

        private static List<ScanEntry> Build(ScanCategory cat, HeroPC hero) {
            var list = new List<ScanEntry>();
            Map map = MapMasterScript.activeMap;
            if (map == null) {
                return list;
            }

            bool[,] explored = map.exploredTiles;
            Vector2 hp = hero.GetPos();
            foreach (Actor a in map.actorsInMap) {
                if (a == null || a == hero) {
                    continue;
                }

                int x = (int)a.GetPos().x;
                int y = (int)a.GetPos().y;
                if (x < 0 || y < 0 || x >= map.columns || y >= map.rows) {
                    continue;
                }

                // The one gate: explored, not currently visible — minimap parity. Flip this single
                // predicate to scan the whole floor regardless of exploration.
                if (explored == null || !explored[x, y]) {
                    continue;
                }

                if (Categorize(a) != cat) {
                    continue;
                }

                string name = GameLabelReader.Clean(a.displayName);
                if (string.IsNullOrEmpty(name)) {
                    name = a.actorRefName; // e.g. stairs carry no displayName
                }

                int dx = x - (int)hp.x;
                int dy = y - (int)hp.y;
                list.Add(new ScanEntry {
                    ActorId = a.actorUniqueID,
                    Name = name,
                    Dx = dx,
                    Dy = dy,
                    Steps = GridDirection.Steps(dx, dy),
                });
            }

            // --- Instance-grouping seam ---
            // For now each matched actor is its own entry. When we decide what to "stack" into a
            // single instance (a monster pack, a scattered item pile), the collapse happens HERE:
            // fold members into representative entries (keeping their ids) before the sort. The
            // navigation above is already entry-based, so only this step changes.

            list.Sort((p, q) => p.Steps - q.Steps);
            return list;
        }

        // --- Speech ---

        private static void SpeakCategory(MessageBuilder message, ScanCategory cat, List<ScanEntry> entries, int index) {
            message.Fragment(Label(cat));
            message.Fragment(entries.Count.ToString());
            message.ListItem();
            SpeakEntry(message, entries[index], index, entries.Count);
        }

        private static void SpeakEntry(MessageBuilder message, ScanEntry entry, int index, int count) {
            message.Fragment(entry.Name);
            message.PushRelativeCoordinates(new Vector2(entry.Dx, entry.Dy));
            message.ListItem().PushFraction(index + 1, count);
        }

        private static string Label(ScanCategory cat) {
            switch (cat) {
                case ScanCategory.Monsters:
                    return "Monsters";
                case ScanCategory.Items:
                    return "Items";
                case ScanCategory.Services:
                    return "Services";
                case ScanCategory.Stairs:
                    return "Stairs";
                case ScanCategory.Objects:
                    return "Objects";
                default:
                    return "Other";
            }
        }

        // --- Helpers ---

        private static int IndexOf(ScanCategory cat) {
            for (int i = 0; i < Order.Length; i++) {
                if (Order[i] == cat) {
                    return i;
                }
            }

            return 0; // current category not in the iteration order (e.g. Other) — start from the top
        }

        private static int IndexOfId(List<ScanEntry> entries, int id) {
            if (id == 0) {
                return -1;
            }

            for (int i = 0; i < entries.Count; i++) {
                if (entries[i].ActorId == id) {
                    return i;
                }
            }

            return -1;
        }
    }
}
