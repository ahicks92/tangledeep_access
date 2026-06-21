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

        /// <summary>
        /// A stable identity across rescans, so a rebuilt snapshot can put the player back on the same
        /// feature by key rather than by (now-dead) object reference. An actor keys on its game-stable
        /// <c>actorUniqueID</c>; a terrain cluster on its kind plus a canonical member cell. The two
        /// shapes carry distinct prefixes, so their key spaces never collide.
        /// </summary>
        public abstract string Key { get; }

        /// <summary>Whether the feature is still on the map (an actor not gone; terrain always is).</summary>
        public abstract bool IsPresent(HashSet<int> liveActorIds);

        /// <summary>The nearest point overall, visibility-agnostic: the frozen sort point and the
        /// fallback Goto target. Live for actors, the cluster's nearest cell for terrain.</summary>
        public abstract Vector2 NearestPointTo(Vector2 hero);

        /// <summary>
        /// The point representing this feature in the current view, and whether it qualifies for it. In
        /// the Visible view (<paramref name="visibleView"/>) that is the nearest <em>currently visible</em>
        /// point — an actor's tile if it is in sight, a terrain cluster's nearest visible cell — and the
        /// returned bool reports whether any such point exists, so a pool only partly in view is
        /// represented (and included) by the part in sight. Outside the Visible view the point is
        /// <see cref="NearestPointTo"/> and the bool is always true. The out point is always set (the
        /// nearest overall when nothing is visible), so callers that ignore the bool still get a target.
        /// </summary>
        public abstract bool ReferencePoint(Vector2 hero, bool visibleView, out Vector2 point);

        /// <summary>Append this feature's spoken description (name + offset, or terrain region). In the
        /// Visible view the offset is to the nearest visible point, matching what put it in the view.</summary>
        public abstract void Speak(MessageBuilder message, HeroPC hero, Map map, bool visibleView);
    }

    /// <summary>A single map actor (monster, item, NPC, stairs, non-terrain object), by stable id.</summary>
    internal sealed class ActorFeature : ScanFeature {
        public int ActorId;
        public int X; // snapshot tile position — the fallback for a gone actor
        public int Y;

        public override string Key => "actor:" + ActorId;

        public override bool IsPresent(HashSet<int> liveActorIds) => liveActorIds.Contains(ActorId);

        public override Vector2 NearestPointTo(Vector2 hero) {
            Map map = MapMasterScript.activeMap;
            Actor a = map != null ? map.FindActorByID(ActorId) : null;
            return (a != null && !a.destroyed) ? a.GetPos() : new Vector2(X, Y);
        }

        public override bool ReferencePoint(Vector2 hero, bool visibleView, out Vector2 point) {
            point = NearestPointTo(hero);
            return !visibleView || Visibility.VisibleNow(point);
        }

        public override void Speak(MessageBuilder message, HeroPC hero, Map map, bool visibleView) {
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

        public override string Key {
            get {
                TerrainCell c = Cluster.CanonicalCell;
                return "terrain:" + Cluster.Kind + ":" + c.X + "," + c.Y;
            }
        }

        public override bool IsPresent(HashSet<int> liveActorIds) => true; // terrain persists across navigation

        public override Vector2 NearestPointTo(Vector2 hero) {
            TerrainCell c = Cluster.NearestCellTo((int)hero.x, (int)hero.y);
            return new Vector2(c.X, c.Y);
        }

        public override bool ReferencePoint(Vector2 hero, bool visibleView, out Vector2 point) {
            if (visibleView) {
                TerrainCell v = Cluster.NearestVisibleCellTo((int)hero.x, (int)hero.y, Visibility.VisibleNow, out bool any);
                if (any) {
                    point = new Vector2(v.X, v.Y);
                    return true;
                }
            }

            point = NearestPointTo(hero); // a non-visible view, or no member in sight: the nearest overall
            return !visibleView;
        }

        public override void Speak(MessageBuilder message, HeroPC hero, Map map, bool visibleView) {
            Vector2 hp = hero.GetPos();
            ReferencePoint(hp, visibleView, out Vector2 near);
            int pct = (int)Math.Round(Cluster.FillFraction * 100);
            message.Fragment(Name);
            message.Fragment(Cluster.Width + " by " + Cluster.Height);
            message.PushRelativeCoordinates(new Vector2((int)near.x - (int)hp.x, (int)near.y - (int)hp.y));
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
    /// <para><b>Snapshot model.</b> The list is a snapshot — built by walking <c>actorsInMap</c> (and
    /// clustering terrain), then held — so paging through it never reshuffles under you as monsters move.
    /// The snapshot freezes only the <em>membership and order</em> (the features, their category, and the
    /// nearest-first sort). It rebuilds on a map change, on End ("rescan"), and — the auto-rescan — once
    /// a game turn has elapsed, rebuilt lazily on the next scanner key. Because turns advance only when
    /// the player acts, the rebuild never lands mid-paging: between turns the world is static, so the
    /// list is stable while you read it, and it refreshes exactly when the world changed. A rebuild does
    /// not cost you your place: the selection is reconciled by each feature's stable
    /// <see cref="ScanFeature.Key"/> (see <see cref="ScanReconcile"/>), so you stay on the same feature,
    /// or fall back to the nearest survivor if it vanished. Per the project's no-stale-speech rule, the
    /// per-feature name and offset are NOT frozen: each is re-queried live at speak time, so a moved
    /// monster's offset is current. An actor that has since died or been opened is "gone": stepping skips
    /// it rather than landing on it. Sort uses Manhattan distance from the hero to the feature's nearest
    /// point at rescan, ties broken by that point's x then y.</para>
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
        private static int _snapshotTurn; // GameMasterScript.turnNumber the snapshot was built on

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
            SpeakEntry(message, view[next], next, view.Count, _category);
            AutoJump(hero);
            return message;
        }

        // In auto-jump mode, point the exploration cursor at the just-selected feature, playing its
        // tile cues but speaking nothing — the navigation's own scanner reading is the spoken text.
        private static void AutoJump(HeroPC hero) {
            if (_autoJump && _selected != null) {
                _selected.ReferencePoint(hero.GetPos(), _category == ScanCategory.Visible, out Vector2 p);
                ExplorationCursor.JumpToSilent(p);
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

            _selected.ReferencePoint(hero.GetPos(), _category == ScanCategory.Visible, out Vector2 target);
            return ExplorationCursor.JumpTo(target);
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

            _selected.ReferencePoint(hero.GetPos(), _category == ScanCategory.Visible, out Vector2 at);
            return ExplorationCursor.ExamineAt(at);
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
        /// Rescan (End): force a rebuild from the live map now, keeping the current category and — via
        /// key reconcile — the selected feature (or the nearest survivor if it is gone), then speak it.
        /// The snapshot also auto-rebuilds each turn, so this is the explicit "refresh now" affordance
        /// rather than the only way to refresh.
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
            int idx = Math.Max(0, IndexOf(view, _selected));
            SpeakCategory(message, _category, view, idx);
            return message;
        }

        // --- Snapshot (built on rescan / first use / map change, then held) ---

        // Ensure a snapshot exists for the current map, building one if absent or stale. Returns
        // whether it has any entries.
        private static bool EnsureSnapshot(HeroPC hero) {
            // Rebuild on first use, a map change, or — the auto-rescan — once a game turn has elapsed.
            // The rebuild is lazy (it happens only when a scanner key is next pressed), so it costs
            // nothing per frame; and turns advance only when the player acts, so the list never
            // reshuffles while they are paging it between turns. DoRescan reconciles the selection by
            // key, so a fresh snapshot keeps the player on the same feature (or the nearest survivor if
            // it vanished) rather than snapping back to the top.
            if (_snapshot == null
                || _snapshotMap != MapMasterScript.activeMap
                || _snapshotTurn != GameMasterScript.turnNumber) {
                DoRescan(hero);
            }

            return _snapshot.Count > 0;
        }

        private static void DoRescan(HeroPC hero) {
            // Remember the feature we were on and the order it lived in, so the rebuilt snapshot can put
            // us back on it by stable key — or, if it vanished, the nearest survivor (mirroring the
            // overlay KeyGraph's focus recovery). Capture the prior order before _snapshot is replaced.
            HashSet<int> live = LiveIds();
            string priorKey = _selected?.Key;
            // Only when a snapshot already exists — the first scan (and a post-Reset rebuild) has none,
            // and View walks _snapshot, so reading it here would dereference null.
            List<string> priorOrder = _snapshot != null ? KeysOf(View(_category, live)) : null;

            _snapshot = BuildSnapshot(hero);
            _snapshotMap = MapMasterScript.activeMap;
            _snapshotTurn = GameMasterScript.turnNumber;

            // Keep the current category across a rescan — only fall back to the default when the rescan
            // left it with no live entries (so a refresh on the same floor doesn't yank you off
            // "Monsters" just because they moved, but does rescue you off an emptied bucket).
            List<ScanFeature> view = View(_category, live);
            if (view.Count == 0) {
                _category = DefaultCategory;
                view = View(_category, live);
            }

            int idx = ScanReconcile.Resolve(priorKey, priorOrder, KeysOf(view));
            _selected = idx >= 0 ? view[idx] : null;
        }

        // The features' stable keys in view order — the input to reconcile (the prior order to walk and
        // the new view to land in).
        private static List<string> KeysOf(List<ScanFeature> view) {
            var keys = new List<string>(view.Count);
            foreach (ScanFeature f in view) {
                keys.Add(f.Key);
            }

            return keys;
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
                if (a == hero || ActorPresence.IsGone(a)) {
                    continue; // an opened crate (isDestroyed husk) / slain monster lingers until cleanup
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
                    if (!f.ReferencePoint(hero, true, out _)) {
                        // The explored snapshot, narrowed to what the hero can see right now. A pool only
                        // partly in sight still qualifies — on its nearest visible cell — so the scanner's
                        // Visible view matches the F2 radar, which clusters over the in-sight set.
                        continue;
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
                if (a != hero && !ActorPresence.IsGone(a)) {
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
            SpeakEntry(message, view[index], index, view.Count, cat);
        }

        private static void SpeakEntry(MessageBuilder message, ScanFeature feature, int index, int count, ScanCategory cat) {
            HeroPC hero = GameMasterScript.heroPCActor;
            Map map = MapMasterScript.activeMap;
            feature.Speak(message, hero, map, cat == ScanCategory.Visible);
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
