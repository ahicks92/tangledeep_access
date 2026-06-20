using System;
using System.Collections.Generic;
using TangledeepAccess.Controls;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// The broad buckets the scanner sorts map features into — the top axis of navigation
    /// ("what kind of thing, in general?"). Two of these are not real buckets but views spanning every
    /// other bucket: <see cref="Visible"/> (only features in current line of sight) is the default
    /// selection and leads the iteration order; <see cref="All"/> (every explored feature) is the
    /// catch-all and trails it. The rest are coarse on purpose: the boundaries are a first guess pending
    /// real UX, and <see cref="Scanner.Categorize"/> is a single switch that is trivial to re-cut (e.g.
    /// splitting shops out of <see cref="Services"/> on <c>NPC.shopRef</c>). The order here is not the
    /// iteration order — that is <see cref="Scanner.Order"/> — and <see cref="Other"/> is the catch-all
    /// for actor types we do not model, surfaced so the scan misses nothing. <see cref="Terrain"/> is
    /// special: its members are not single actors but clustered regions of terrain tiles (water,
    /// mud, ...) built by <see cref="TerrainClusterer"/>.
    /// </summary>
    public enum ScanCategory {
        All,
        Visible,
        Monsters,
        Items,
        Services,
        Stairs,
        Objects,
        Terrain,
        Other,
    }

    /// <summary>
    /// One navigable feature in the scanner. The snapshot freezes membership and order (which features
    /// exist and the nearest-first sort); the name and spoken offset are recomputed live at speak time,
    /// so what you hear tracks the world even though the list does not reshuffle until the next rescan.
    /// Two concrete shapes: <see cref="ActorFeature"/> (a single actor, by stable id) and
    /// <see cref="TerrainFeatureItem"/> (a clustered terrain region). <see cref="SortKey"/> and the
    /// tie-break point are frozen at rescan; <see cref="NearestPointTo"/> is where Goto travels.
    /// </summary>
    internal abstract class ScanFeature {
        public ScanCategory Category;
        public int SortKey;  // Manhattan distance from the hero to the nearest point, at rescan
        public int TieX;     // the nearest point at rescan — the stable sort tie-break
        public int TieY;

        /// <summary>Whether the feature is still on the map (an actor not gone; terrain always is).</summary>
        public abstract bool IsPresent(HashSet<int> liveActorIds);

        /// <summary>The point Goto travels to — the live actor position, or a cluster's nearest cell.</summary>
        public abstract Vector2 NearestPointTo(Vector2 hero);

        /// <summary>Append this feature's spoken description (name + offset, or terrain region).</summary>
        public abstract void Speak(MessageBuilder message, HeroPC hero, Map map);
    }

    /// <summary>A single map actor (monster, item, NPC, stairs, non-terrain object), by stable id.</summary>
    internal sealed class ActorFeature : ScanFeature {
        public int ActorId;
        public int X; // snapshot tile position — the fallback for a gone actor
        public int Y;

        public override bool IsPresent(HashSet<int> liveActorIds) => liveActorIds.Contains(ActorId);

        public override Vector2 NearestPointTo(Vector2 hero) {
            Map map = MapMasterScript.activeMap;
            Actor a = map != null ? map.FindActorByID(ActorId) : null;
            return (a != null && !a.destroyed) ? a.GetPos() : new Vector2(X, Y);
        }

        public override void Speak(MessageBuilder message, HeroPC hero, Map map) {
            Actor a = map != null ? map.FindActorByID(ActorId) : null;
            if (a == null || a.destroyed || hero == null) {
                message.Fragment("gone"); // resolved nothing, or it died/was opened since the scan
                return;
            }

            string name = GameLabelReader.Clean(a.displayName);
            if (string.IsNullOrEmpty(name)) {
                name = a.actorRefName; // e.g. stairs carry no displayName
            }

            // Reuse the cursor's short occupant form, injecting the hero-relative offset right after
            // the name — so a monster reads "rat, 2 right 3 down, 80% hp, aggressive" and everything
            // else reads "name, <offset>".
            Vector2 p = a.GetPos();
            Vector2 hp = hero.GetPos();
            var offset = new Vector2((int)p.x - (int)hp.x, (int)p.y - (int)hp.y);
            TileDescriber.AppendShortForm(message, a, name, offset);
        }
    }

    /// <summary>
    /// A clustered region of one terrain kind (water, mud, ...). Reads as "mud, 5 by 5, &lt;offset to
    /// nearest cell&gt;, 75% filled" — the bounding-box dimensions, the nearest reachable point, and how
    /// solidly that box is filled. The cells are frozen at rescan (terrain does not move); only the
    /// hero-relative offset is live.
    /// </summary>
    internal sealed class TerrainFeatureItem : ScanFeature {
        public TerrainCluster Cluster;
        public string Name;

        public override bool IsPresent(HashSet<int> liveActorIds) => true; // terrain persists across navigation

        public override Vector2 NearestPointTo(Vector2 hero) {
            TerrainCell c = Cluster.NearestCellTo((int)hero.x, (int)hero.y);
            return new Vector2(c.X, c.Y);
        }

        public override void Speak(MessageBuilder message, HeroPC hero, Map map) {
            Vector2 hp = hero.GetPos();
            TerrainCell near = Cluster.NearestCellTo((int)hp.x, (int)hp.y);
            int pct = (int)Math.Round(Cluster.FillFraction * 100);
            message.Fragment(Name);
            message.Fragment(Cluster.Width + " by " + Cluster.Height);
            message.PushRelativeCoordinates(new Vector2(near.X - (int)hp.x, near.Y - (int)hp.y));
            message.Fragment(pct + "% filled");
        }
    }

    /// <summary>
    /// Input for the scanner, beside the state it drives — the same drainer+module split as the look
    /// cursor. Modeless: it claims only its dedicated nav keys (Page Up/Down for entries, Ctrl+Page
    /// Up/Down for categories, Home to point the cursor, Shift+Home to examine the selection, Alt+Home
    /// to toggle auto-jump, End to rescan) and passes everything else straight through, so it never
    /// fights the look cursor's arrows or the game's movement. The keys carry no payload;
    /// <see cref="Scanner"/> holds the selection and produces the speech.
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
                case ModInputKind.ScanExamine:
                    spoken = Scanner.Examine();
                    break;
                case ModInputKind.ScanAutoJumpToggle:
                    spoken = Scanner.ToggleAutoJump();
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
    /// A categorized, distance-sorted readout of the map's features — the non-visual analog of the
    /// minimap, modeled on Factorio Access's scanner. Most features are single-tile actors; terrain
    /// (water, mud, ...) is the exception, clustered into regions by <see cref="TerrainClusterer"/> so a
    /// pool reads as one feature rather than dozens of tiles.
    ///
    /// <para><b>Snapshot model.</b> The list is a snapshot — built once by walking <c>actorsInMap</c>
    /// (and clustering terrain), then held — so paging through it never reshuffles under you as monsters
    /// move. The snapshot freezes only the <em>membership and order</em> (the features, their category,
    /// and the nearest-first sort); pressing End ("rescan") rebuilds it, and it auto-rebuilds on a map
    /// change. Per the project's no-stale-speech rule, the per-feature name and offset are NOT frozen:
    /// each is re-queried live at speak time, so a moved monster's offset is current. An actor that has
    /// since died or been opened is "gone": stepping skips it rather than landing on it. Sort uses
    /// Manhattan distance from the hero to the feature's nearest point at rescan, ties broken by that
    /// point's x then y.</para>
    ///
    /// <para>Two navigation axes: category (the broad bucket, defaulting to
    /// <see cref="ScanCategory.Visible"/> — only features in current line of sight) and entry (one
    /// feature within it, nearest first). Category navigation just re-filters the held snapshot — it
    /// does not rebuild. <see cref="Goto"/> (Home) points the exploration cursor at the selected
    /// feature's nearest point and speaks the cursor's own readout; <see cref="Examine"/> (Shift+Home)
    /// reads its full tooltip; <see cref="ToggleAutoJump"/> (Alt+Home) flips a mode where navigation
    /// itself points the cursor as you go.</para>
    ///
    /// <para>Visibility follows the minimap, not line of sight: the snapshot surfaces a feature only on
    /// an <em>explored</em> tile (<see cref="Visibility.Explored"/>), and terrain is clustered only over
    /// explored tiles, so the scanner never reveals ground the player has not yet seen. The
    /// <see cref="ScanCategory.Visible"/> view narrows that explored snapshot to what the hero can see
    /// right now (<see cref="Visibility.VisibleNow"/>); <see cref="ScanCategory.All"/> spans all of
    /// it.</para>
    /// </summary>
    internal static class Scanner {
        // Iteration order for category navigation. Visible leads (the default — only what is in sight);
        // All trails as the everything-explored catch-all, with Other just ahead of it for unmodeled
        // actor types. This order is arbitrary and easy to change.
        private static readonly ScanCategory[] Order = {
            ScanCategory.Visible,
            ScanCategory.Monsters,
            ScanCategory.Stairs,
            ScanCategory.Services,
            ScanCategory.Items,
            ScanCategory.Objects,
            ScanCategory.Terrain,
            ScanCategory.Other,
            ScanCategory.All,
        };

        // The held snapshot (null = never scanned yet) and the map it was taken on, so a level change
        // forces a rebuild. A live map reference is the one sanctioned "cache" — we compare identity,
        // never read stale state from it.
        private static List<ScanFeature> _snapshot;
        private static Map _snapshotMap;

        // The default view, restored on first scan and whenever a rescan empties the current category.
        private const ScanCategory DefaultCategory = ScanCategory.Visible;

        private static ScanCategory _category = DefaultCategory;
        private static ScanFeature _selected; // the current entry, by reference within the snapshot

        // Auto-jump mode (Alt+Home toggles it): while on, every category/entry step also points the
        // exploration cursor at the selected feature, playing its tile cues — the spoken text stays the
        // scanner's own reading, the cursor just follows along.
        private static bool _autoJump;

        /// <summary>
        /// Which category a map feature belongs to — the requested classifier. Coarse and keyed only
        /// on <see cref="ActorTypes"/> for v1; the hero and terrain tiles are excluded before this is
        /// called, so it never returns a category for the player or for terrain. Never returns
        /// <see cref="ScanCategory.All"/> or <see cref="ScanCategory.Visible"/> (views spanning the
        /// others) or <see cref="ScanCategory.Terrain"/> (handled by clustering). Splitting shops out
        /// of services is a one-line change.
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
                    return ScanCategory.Objects; // a terrain destructible is pulled out before this
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
            _selected = null;
            _category = DefaultCategory;
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
                List<ScanFeature> view = View(cat, live);
                if (view.Count == 0) {
                    continue;
                }

                _category = cat;
                _selected = view[0];
                SpeakCategory(message, cat, view, 0);
                AutoJump(hero);
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

            List<ScanFeature> view = View(_category, LiveIds());
            if (view.Count == 0) {
                // The current category has no live entries left (all gone since the scan): bootstrap
                // onto the next category that does instead of saying nothing.
                return StepCategory(dir);
            }

            int cur = IndexOf(view, _selected);
            int next = cur < 0
                ? (dir > 0 ? 0 : view.Count - 1)
                : (cur + dir + view.Count) % view.Count;
            _selected = view[next];
            SpeakEntry(message, view[next], next, view.Count);
            AutoJump(hero);
            return message;
        }

        // In auto-jump mode, point the exploration cursor at the just-selected feature, playing its
        // tile cues but speaking nothing — the navigation's own scanner reading is the spoken text.
        private static void AutoJump(HeroPC hero) {
            if (_autoJump && _selected != null) {
                ExplorationCursor.JumpToSilent(_selected.NearestPointTo(hero.GetPos()));
            }
        }

        /// <summary>
        /// Point the exploration cursor at the selected feature's nearest point and speak the cursor's
        /// own readout (Home). For an actor this resolves live (falling back to its snapshot tile if
        /// gone); for terrain it is the cluster's nearest cell. Returns the cursor's readout builder.
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

            if (_selected == null || !_snapshot.Contains(_selected)) {
                return new MessageBuilder().Fragment("nothing selected");
            }

            return ExplorationCursor.JumpTo(_selected.NearestPointTo(hero.GetPos()));
        }

        /// <summary>
        /// Examine the selected feature in full (Shift+Home): the game's own tooltip at the feature's
        /// nearest point — full monster stats, or terrain plus its hazard effect — without moving the
        /// cursor. Mirrors the exploration cursor's Shift+K examine.
        /// </summary>
        public static MessageBuilder Examine() {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            if (hero == null || map == null) {
                return null;
            }

            if (!EnsureSnapshot(hero)) {
                return new MessageBuilder().Fragment("nothing in range");
            }

            if (_selected == null || !_snapshot.Contains(_selected)) {
                return new MessageBuilder().Fragment("nothing selected");
            }

            return ExplorationCursor.ExamineAt(_selected.NearestPointTo(hero.GetPos()));
        }

        /// <summary>
        /// Toggle auto-jump mode (Alt+Home): while on, every category/entry step also points the
        /// exploration cursor at the selected feature (cues only — the spoken text stays the scanner's
        /// reading). Turning it on jumps to the current selection at once.
        /// </summary>
        public static MessageBuilder ToggleAutoJump() {
            _autoJump = !_autoJump;
            var message = new MessageBuilder();
            message.Fragment(_autoJump ? "auto jump on" : "auto jump off");
            if (_autoJump) {
                HeroPC hero = GameMasterScript.heroPCActor;
                if (hero != null) {
                    AutoJump(hero);
                }
            }

            return message;
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
            List<ScanFeature> view = View(_category, LiveIds());
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
            // Keep the current category across a rescan — only fall back to the default when the rescan
            // left it with no live entries (so a refresh on the same floor doesn't yank you off
            // "Monsters" just because they moved, but does rescue you off an emptied bucket).
            List<ScanFeature> view = View(_category, LiveIds());
            if (view.Count == 0) {
                _category = DefaultCategory;
                view = View(_category, LiveIds());
            }
            _selected = view.Count > 0 ? view[0] : null;
        }

        private static List<ScanFeature> BuildSnapshot(HeroPC hero) {
            var list = new List<ScanFeature>();
            Map map = MapMasterScript.activeMap;
            if (map == null) {
                return list;
            }

            Vector2 hp = hero.GetPos();
            int hx = (int)hp.x;
            int hy = (int)hp.y;

            // Non-terrain actors: one feature each. Terrain tiles are pulled out here and clustered
            // below, so a terrain destructible never reaches Categorize — and a destructible that is
            // *not* flagged terrain stays an Object, which is how a mis-flagged terrain tile surfaces.
            foreach (Actor a in map.actorsInMap) {
                if (a == null || a == hero || a.destroyed) {
                    continue; // an opened crate / slain monster lingers in actorsInMap until cleanup
                }
                if (TerrainFeature.Is(a)) {
                    continue; // terrain — clustered below, not an individual feature
                }

                int x = (int)a.GetPos().x;
                int y = (int)a.GetPos().y;
                if (x < 0 || y < 0 || x >= map.columns || y >= map.rows) {
                    continue;
                }
                if (!Visibility.Explored(x, y)) {
                    continue; // minimap parity — never surface unexplored ground
                }

                list.Add(new ActorFeature {
                    ActorId = a.actorUniqueID,
                    Category = Categorize(a),
                    X = x,
                    Y = y,
                });
            }

            // Terrain clusters: connected regions of one kind over explored tiles only.
            foreach (TerrainCluster cluster in TerrainFeature.Cluster(map, Visibility.Explored)) {
                list.Add(new TerrainFeatureItem {
                    Cluster = cluster,
                    Category = ScanCategory.Terrain,
                    Name = TerrainFeature.Name(cluster.Kind),
                });
            }

            // Freeze the sort key (Manhattan to the nearest point) and tie-break point at rescan.
            foreach (ScanFeature f in list) {
                Vector2 np = f.NearestPointTo(hp);
                f.TieX = (int)np.x;
                f.TieY = (int)np.y;
                f.SortKey = Math.Abs(f.TieX - hx) + Math.Abs(f.TieY - hy);
            }

            list.Sort(Compare);
            return list;
        }

        // Nearest first by Manhattan distance, ties broken by the nearest point's x then y.
        private static int Compare(ScanFeature p, ScanFeature q) {
            if (p.SortKey != q.SortKey) {
                return p.SortKey - q.SortKey;
            }
            if (p.TieX != q.TieX) {
                return p.TieX - q.TieX;
            }

            return p.TieY - q.TieY;
        }

        // The navigable entries of a category, in snapshot order: those of the category (All spans
        // every category; Visible spans every category but keeps only features in current line of
        // sight) that are still present. Filtering against the live set means stepping never lands on a
        // "gone" actor. Home is the exception: it keeps its existing selection.
        private static List<ScanFeature> View(ScanCategory cat, HashSet<int> live) {
            Vector2 hero = GameMasterScript.heroPCActor != null
                ? GameMasterScript.heroPCActor.GetPos()
                : Vector2.zero;
            var view = new List<ScanFeature>();
            foreach (ScanFeature f in _snapshot) {
                if (!f.IsPresent(live)) {
                    continue;
                }
                if (cat == ScanCategory.Visible) {
                    if (!Visibility.VisibleNow(f.NearestPointTo(hero))) {
                        continue; // the explored snapshot, narrowed to what the hero can see right now
                    }
                } else if (cat != ScanCategory.All && f.Category != cat) {
                    continue;
                }

                view.Add(f);
            }

            return view;
        }

        // The actorUniqueIDs still present on the active map (resolved, not destroyed). Rebuilt each
        // navigation in one pass, so View can cheaply exclude features whose actor is gone.
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

        private static void SpeakCategory(MessageBuilder message, ScanCategory cat, List<ScanFeature> view, int index) {
            message.Fragment(Label(cat));
            message.Fragment(view.Count.ToString());
            message.ListItem();
            SpeakEntry(message, view[index], index, view.Count);
        }

        private static void SpeakEntry(MessageBuilder message, ScanFeature feature, int index, int count) {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            feature.Speak(message, hero, map);
            message.ListItem().PushFraction(index + 1, count);
        }

        private static string Label(ScanCategory cat) {
            switch (cat) {
                case ScanCategory.All:
                    return "All";
                case ScanCategory.Visible:
                    return "Visible";
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
                case ScanCategory.Terrain:
                    return "Terrain";
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

            return 0; // current category not in the iteration order — start from the top
        }

        private static int IndexOf(List<ScanFeature> entries, ScanFeature selected) {
            if (selected == null) {
                return -1;
            }

            for (int i = 0; i < entries.Count; i++) {
                if (ReferenceEquals(entries[i], selected)) {
                    return i;
                }
            }

            return -1;
        }
    }
}
