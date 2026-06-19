using System.Collections.Generic;
using TangledeepAccess.Controls;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// The broad buckets the scanner sorts map features into — the top axis of navigation
    /// ("what kind of thing, in general?"). <see cref="All"/> is the catch-all spanning every other
    /// bucket and is the default selection. The rest are coarse on purpose: the boundaries are a first
    /// guess pending real UX, and <see cref="Scanner.Categorize"/> is a single switch that is trivial
    /// to re-cut (e.g. splitting shops out of <see cref="Services"/> on <c>NPC.shopRef</c>). The order
    /// here is not the iteration order — that is <see cref="Scanner.Order"/> — and <see cref="Other"/>
    /// is the catch-all for actor types we do not model, surfaced so the scan misses nothing.
    /// </summary>
    public enum ScanCategory {
        All,
        Monsters,
        Items,
        Services,
        Stairs,
        Objects,
        Other,
    }

    /// <summary>
    /// One navigable feature in the scanner: a single actor, identified by its stable
    /// <c>actorUniqueID</c> so it survives the live re-query at speak time without ever caching the
    /// actor itself. <see cref="Category"/> and the snapshot position (<see cref="X"/>/<see cref="Y"/>,
    /// with <see cref="Manhattan"/> for the sort) are frozen at rescan — they fix the list's membership
    /// and order. Name and the spoken offset are recomputed live from the resolved actor, so what you
    /// hear tracks the world even though the list itself does not reshuffle until the next rescan.
    /// </summary>
    internal struct ScanEntry {
        public int ActorId;
        public ScanCategory Category;
        public int X; // snapshot tile position, the sort tie-breaker and the fallback for a gone actor
        public int Y;
        public int Manhattan; // |dx| + |dy| from the hero at rescan, the primary sort key
    }

    /// <summary>
    /// Input for the scanner, beside the state it drives — the same drainer+module split as the look
    /// cursor. Modeless: it claims only its dedicated nav keys (Page Up/Down for entries, Ctrl+Page
    /// Up/Down for categories, Home to point the cursor, End to rescan) and passes everything else
    /// straight through, so it never fights the look cursor's arrows or the game's movement. The keys
    /// carry no payload; <see cref="Scanner"/> holds the selection and produces the speech.
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
            // Each verb returns its own one-per-press builder (so Home can hand back the exploration
            // cursor's own readout builder directly — one builder, spoken once, never nested).
            MessageBuilder spoken;
            switch (action.Kind) {
                case ModInputKind.ScanNextCategory:
                    spoken = Scanner.NextCategory();
                    break;
                case ModInputKind.ScanPrevCategory:
                    spoken = Scanner.PrevCategory();
                    break;
                case ModInputKind.ScanNextEntry:
                    spoken = Scanner.NextEntry();
                    break;
                case ModInputKind.ScanPrevEntry:
                    spoken = Scanner.PrevEntry();
                    break;
                case ModInputKind.ScanGoto:
                    spoken = Scanner.Goto();
                    break;
                case ModInputKind.ScanRescan:
                    spoken = Scanner.Rescan();
                    break;
                default:
                    spoken = null;
                    break;
            }

            speech.Speak(spoken);
        }
    }

    /// <summary>
    /// A categorized, distance-sorted readout of the map's actor-features — the non-visual analog of
    /// the minimap, modeled on Factorio Access's scanner but radically simplified: Tangledeep floors
    /// are small and every feature is a single tile (no multi-tile entities), so there is no
    /// background crawling, clustering, or bounding-box machinery.
    ///
    /// <para><b>Snapshot model.</b> The list is a snapshot — built once by walking <c>actorsInMap</c>,
    /// then held — so paging through it never reshuffles under you as monsters move. The snapshot
    /// freezes only the <em>membership and order</em> (the actor ids, their category, and the
    /// nearest-first sort); pressing End ("rescan") rebuilds it, and it auto-rebuilds on a map change.
    /// Per the project's no-stale-speech rule, the per-entry name and offset are NOT frozen: each is
    /// re-queried from the live actor (resolved by id) at speak time, so a moved monster's offset is
    /// current. A snapshot member that has since died or been opened is "gone": stepping
    /// (entry/category nav) skips it rather than landing on it, so the readout only counts what is
    /// still there. Sort uses Manhattan distance from the hero at rescan, ties broken by tile x then
    /// y.</para>
    ///
    /// <para>Two navigation axes: category (the broad bucket, defaulting to <see cref="ScanCategory.All"/>)
    /// and entry (one feature within it, nearest first). Category navigation just re-filters the held
    /// snapshot — it does not rebuild. <see cref="Goto"/> (Home) points the exploration cursor at the
    /// selected feature and speaks the cursor's own readout. The third Factorio axis — grouping several
    /// actors into one "instance" — is deliberately deferred; the seam is the build step.</para>
    ///
    /// <para>Visibility follows the minimap, not line of sight: a feature is surfaced only on an
    /// <em>explored</em> tile (the single explored predicate in <see cref="BuildSnapshot"/>), so the
    /// scanner never reveals ground the player has not yet seen. That is parity with the sighted
    /// minimap, which is a live view of the whole explored map.</para>
    /// </summary>
    internal static class Scanner {
        // Iteration order for category navigation. All leads (the default); Other trails as the
        // catch-all for unmodeled actor types. This order is arbitrary and easy to change.
        private static readonly ScanCategory[] Order = {
            ScanCategory.All,
            ScanCategory.Monsters,
            ScanCategory.Stairs,
            ScanCategory.Services,
            ScanCategory.Items,
            ScanCategory.Objects,
            ScanCategory.Other,
        };

        // The held snapshot (null = never scanned yet) and the map it was taken on, so a level change
        // forces a rebuild. A live map reference is the one sanctioned "cache" — we compare identity,
        // never read stale state from it.
        private static List<ScanEntry> _snapshot;
        private static Map _snapshotMap;

        private static ScanCategory _category = ScanCategory.All;
        private static int _selectedId; // actorUniqueID of the current entry, 0 = nothing selected

        /// <summary>
        /// Which category a map feature belongs to — the requested classifier. Coarse and keyed only
        /// on <see cref="ActorTypes"/> for v1; the hero is excluded before this is called, so it never
        /// returns a category for the player. Never returns <see cref="ScanCategory.All"/> — that is a
        /// view spanning the others, not a bucket an actor lands in. Splitting shops out of services is
        /// a one-line change: add a case for <c>NPC</c> with a non-empty <c>shopRef</c>.
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
                    return ScanCategory.Other; // anything unmodeled — surfaced under the Other bucket
            }
        }

        /// <summary>Step to the next non-empty category and append its nearest entry.</summary>
        public static MessageBuilder NextCategory() {
            return StepCategory(1);
        }

        /// <summary>Step to the previous non-empty category and append its nearest entry.</summary>
        public static MessageBuilder PrevCategory() {
            return StepCategory(-1);
        }

        /// <summary>Move to the next entry within the current category, wrapping, and append it.</summary>
        public static MessageBuilder NextEntry() {
            return StepEntry(1);
        }

        /// <summary>Move to the previous entry within the current category, wrapping, and append it.</summary>
        public static MessageBuilder PrevEntry() {
            return StepEntry(-1);
        }

        /// <summary>Forget the snapshot and selection (e.g. on a level change). The next navigation
        /// rescans; a map change also auto-rescans, so this is belt-and-suspenders.</summary>
        public static void Reset() {
            _snapshot = null;
            _snapshotMap = null;
            _selectedId = 0;
            _category = ScanCategory.All;
        }

        // --- Navigation ---

        private static MessageBuilder StepCategory(int dir) {
            var message = new MessageBuilder();
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || MapMasterScript.activeMap == null) {
                return message;
            }

            if (!EnsureSnapshot(hero)) {
                message.Fragment("nothing in range");
                return message;
            }

            HashSet<int> live = LiveIds();
            int start = IndexOf(_category);
            // Skip categories with no live entries; wrap. i runs a full lap so we land back on the
            // current category only if it is the single non-empty one.
            for (int i = 1; i <= Order.Length; i++) {
                int idx = ((start + dir * i) % Order.Length + Order.Length) % Order.Length;
                ScanCategory cat = Order[idx];
                List<ScanEntry> view = View(cat, live);
                if (view.Count == 0) {
                    continue;
                }

                _category = cat;
                _selectedId = view[0].ActorId;
                SpeakCategory(message, cat, view, 0);
                return message;
            }

            message.Fragment("nothing in range");
            return message;
        }

        private static MessageBuilder StepEntry(int dir) {
            var message = new MessageBuilder();
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || MapMasterScript.activeMap == null) {
                return message;
            }

            if (!EnsureSnapshot(hero)) {
                message.Fragment("nothing in range");
                return message;
            }

            List<ScanEntry> view = View(_category, LiveIds());
            if (view.Count == 0) {
                // The current category has no live entries left (all gone since the scan): bootstrap
                // onto the next category that does instead of saying nothing.
                return StepCategory(dir);
            }

            int cur = IndexOfId(view, _selectedId);
            int next = cur < 0
                ? (dir > 0 ? 0 : view.Count - 1)
                : (cur + dir + view.Count) % view.Count;
            _selectedId = view[next].ActorId;
            SpeakEntry(message, view[next], next, view.Count);
            return message;
        }

        /// <summary>
        /// Point the exploration cursor at the selected feature and speak the cursor's own readout
        /// (Home). Resolves the actor live by id so the cursor lands on where it actually is now; if it
        /// is gone, falls back to its snapshot tile. Returns the cursor's readout builder directly.
        /// </summary>
        public static MessageBuilder Goto() {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            if (hero == null || map == null) {
                return null;
            }

            if (!EnsureSnapshot(hero)) {
                return new MessageBuilder().Fragment("nothing in range");
            }

            // The held selection is sought across the whole snapshot, not the live-filtered view, so
            // Home still works for a feature that vanished after it was selected (it reads at the last
            // tile). Navigation already keeps the selection off gone entries.
            int cur = IndexOfId(_snapshot, _selectedId);
            if (cur < 0) {
                return new MessageBuilder().Fragment("nothing selected");
            }

            ScanEntry entry = _snapshot[cur];
            Actor a = map.FindActorByID(entry.ActorId);
            Vector2 target = IsLive(a) ? a.GetPos() : new Vector2(entry.X, entry.Y);
            return ExplorationCursor.JumpTo(target);
        }

        /// <summary>
        /// Rescan (End): rebuild the snapshot from the live map, reset to <see cref="ScanCategory.All"/>,
        /// select the nearest feature, and speak the fresh readout.
        /// </summary>
        public static MessageBuilder Rescan() {
            var message = new MessageBuilder();
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || MapMasterScript.activeMap == null) {
                return message;
            }

            DoRescan(hero);
            message.Fragment("rescanned");
            List<ScanEntry> view = View(_category, LiveIds());
            if (view.Count == 0) {
                message.ListItem("nothing in range");
                return message;
            }

            message.ListItem();
            SpeakCategory(message, _category, view, 0);
            return message;
        }

        // --- Snapshot (built on rescan / first use / map change, then held) ---

        // Ensure a snapshot exists for the current map, building one if absent or stale. Returns
        // whether it has any entries.
        private static bool EnsureSnapshot(HeroPC hero) {
            if (_snapshot == null || _snapshotMap != MapMasterScript.activeMap) {
                DoRescan(hero);
            }

            return _snapshot.Count > 0;
        }

        private static void DoRescan(HeroPC hero) {
            _snapshot = BuildSnapshot(hero);
            _snapshotMap = MapMasterScript.activeMap;
            _category = ScanCategory.All;
            _selectedId = _snapshot.Count > 0 ? _snapshot[0].ActorId : 0;
        }

        private static List<ScanEntry> BuildSnapshot(HeroPC hero) {
            var list = new List<ScanEntry>();
            Map map = MapMasterScript.activeMap;
            if (map == null) {
                return list;
            }

            bool[,] explored = map.exploredTiles;
            Vector2 hp = hero.GetPos();
            int hx = (int)hp.x;
            int hy = (int)hp.y;
            foreach (Actor a in map.actorsInMap) {
                if (a == null || a == hero || a.destroyed) {
                    continue; // an opened crate / slain monster lingers in actorsInMap until cleanup
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

                list.Add(new ScanEntry {
                    ActorId = a.actorUniqueID,
                    Category = Categorize(a), // Other (unmodeled types) is surfaced, not dropped
                    X = x,
                    Y = y,
                    Manhattan = Mathf.Abs(x - hx) + Mathf.Abs(y - hy),
                });
            }

            // --- Instance-grouping seam ---
            // For now each matched actor is its own entry. When we decide what to "stack" into a
            // single instance (a monster pack, a scattered item pile), the collapse happens HERE:
            // fold members into representative entries (keeping their ids) before the sort. The
            // navigation above is already entry-based, so only this step changes.

            list.Sort(Compare);
            return list;
        }

        // Nearest first by Manhattan distance, ties broken by tile x then y (a stable total order).
        private static int Compare(ScanEntry p, ScanEntry q) {
            if (p.Manhattan != q.Manhattan) {
                return p.Manhattan - q.Manhattan;
            }
            if (p.X != q.X) {
                return p.X - q.X;
            }

            return p.Y - q.Y;
        }

        // The navigable entries of a category, in snapshot order: those of the category (All spans
        // every category) whose actor is still present. Filtering against the live set means stepping
        // never lands on a "gone" entry — a snapshot member that has since died or been opened is
        // skipped, not announced. Home is the exception: it keeps its existing selection and can still
        // point at one that vanished after it was selected (it reads via the cursor at the last tile).
        private static List<ScanEntry> View(ScanCategory cat, HashSet<int> live) {
            var view = new List<ScanEntry>();
            foreach (ScanEntry e in _snapshot) {
                if (live.Contains(e.ActorId) && (cat == ScanCategory.All || e.Category == cat)) {
                    view.Add(e);
                }
            }

            return view;
        }

        // The actorUniqueIDs still present on the active map (resolved, not destroyed). Rebuilt each
        // navigation in one pass, so View can cheaply exclude snapshot entries whose actor is gone.
        private static HashSet<int> LiveIds() {
            var ids = new HashSet<int>();
            Map map = MapMasterScript.activeMap;
            HeroPC hero = GameMasterScript.heroPCActor;
            if (map == null) {
                return ids;
            }

            foreach (Actor a in map.actorsInMap) {
                if (a != null && a != hero && !a.destroyed) {
                    ids.Add(a.actorUniqueID);
                }
            }

            return ids;
        }

        // --- Speech ---

        private static void SpeakCategory(MessageBuilder message, ScanCategory cat, List<ScanEntry> view, int index) {
            message.Fragment(Label(cat));
            message.Fragment(view.Count.ToString());
            message.ListItem();
            SpeakEntry(message, view[index], index, view.Count);
        }

        // Resolve the entry's actor live (by id) for a current name and offset; a vanished actor reads
        // "gone". The fraction (position in the list) always speaks.
        private static void SpeakEntry(MessageBuilder message, ScanEntry entry, int index, int count) {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            Actor a = map != null ? map.FindActorByID(entry.ActorId) : null;
            if (!IsLive(a) || hero == null) {
                message.Fragment("gone"); // resolved nothing, or it died/was opened since the scan
            } else {
                string name = GameLabelReader.Clean(a.displayName);
                if (string.IsNullOrEmpty(name)) {
                    name = a.actorRefName; // e.g. stairs carry no displayName
                }

                Vector2 p = a.GetPos();
                Vector2 hp = hero.GetPos();
                message.Fragment(name);
                message.PushRelativeCoordinates(new Vector2((int)p.x - (int)hp.x, (int)p.y - (int)hp.y));
            }

            message.ListItem().PushFraction(index + 1, count);
        }

        private static string Label(ScanCategory cat) {
            switch (cat) {
                case ScanCategory.All:
                    return "All";
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

        // A still-present actor: resolved and not destroyed. A destroyed actor (opened crate, slain
        // monster) lingers in actorsInMap until cleanup, so id resolution alone is not enough.
        private static bool IsLive(Actor a) {
            return a != null && !a.destroyed;
        }

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
