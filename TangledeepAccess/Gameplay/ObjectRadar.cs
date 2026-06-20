using System.Collections.Generic;
using TangledeepAccess.Audio;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Object radar (F2): a repeating sweep of everything in line of sight. Ctrl+F2 toggles it on or
    /// off (off by default); there is no Shift+F2 — it is a pure toggle, not a per-event aid. Each
    /// sweep takes a single snapshot of the visible set, sorts it by x then y, and pings one entity
    /// every <see cref="ScanCue.IntervalSeconds"/> — encoding its left-right offset as pan and its
    /// north-south offset as the reference/second-grain interval — until the snapshot is exhausted.
    /// When the sweep finishes it rests for <see cref="RestSeconds"/>, then takes a fresh snapshot and
    /// sweeps again. The snapshot is frozen for the whole sweep: entities that appear or move mid-sweep
    /// are only picked up by the next snapshot, so the order is stable and predictable.
    /// </summary>
    internal sealed class ObjectRadar : NavAid {
        // Tunable: the pause between the end of one sweep and the start of the next. The within-sweep
        // ping spacing is ScanCue.IntervalSeconds; this is the longer gap that delimits whole sweeps.
        private const double RestSeconds = 1.5;

        private readonly ObjectRadarRing _ring = new ObjectRadarRing();
        private readonly List<ObjectRadarRing.Entry> _current = new List<ObjectRadarRing.Entry>();
        private double _timer;
        private bool _resting; // between sweeps, counting RestSeconds; otherwise mid-sweep

        public ObjectRadar() : base("object radar", enabled: false) { }

        public override MessageBuilder OnCtrl() {
            MessageBuilder spoken = ToggleSpoken();
            _ring.Clear();
            if (Enabled) {
                // Arm the rest timer as already elapsed so the first sweep starts on the next tick
                // rather than after an initial RestSeconds wait.
                _resting = true;
                _timer = RestSeconds;
            } else {
                _resting = false;
                _timer = 0.0;
            }
            return spoken;
        }

        public override void Tick(double dt) {
            if (!Enabled) {
                return;
            }

            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null
                || UIManagerScript.AnyInteractableWindowOpen()) {
                return; // pause the sweep out of play or while a menu/dialog is up
            }

            _timer += dt;

            if (_resting) {
                if (_timer < RestSeconds) {
                    return; // still resting between sweeps
                }
                StartSweep(hero);
                return;
            }

            if (_timer < ScanCue.IntervalSeconds) {
                return;
            }
            _timer = 0.0;
            Ping();
        }

        // Take a fresh snapshot and ping its first entity. An empty snapshot drops straight back to
        // resting, so the radar quietly retries after RestSeconds until something is in sight.
        private void StartSweep(HeroPC hero) {
            LoadSnapshot(hero);
            _timer = 0.0;
            _resting = false;
            if (_ring.Count == 0) {
                _resting = true;
                return;
            }
            Ping();
        }

        // Ping the next entity in the current snapshot; when that was the last one, rest before the
        // next sweep (the timer is already zeroed, so RestSeconds is measured from this final ping).
        private void Ping() {
            ObjectRadarRing.Entry? next = _ring.Next();
            if (next.HasValue) {
                TonePlayer.PlayTimeline(VoicePing(next.Value));
            }
            if (_ring.SweepDone) {
                _resting = true;
            }
        }

        // The ping for one entity: its category's radar sample if one is loaded, else the default
        // triangle tone. Both share ScanCue's pan/pitch/level/envelope, so only the timbre differs.
        private static GrainTimeline VoicePing(ObjectRadarRing.Entry e) {
            if (SampleBank.TryGet(e.Category, out float[] samples, out int sampleRate)) {
                return ScanCue.BuildSample(e.X, e.Y, samples, sampleRate);
            }
            return ScanCue.Build(e.X, e.Y);
        }

        // Snapshot the visible set as hero-relative offsets and load it (sorted) as the next sweep.
        private void LoadSnapshot(HeroPC hero) {
            Vector2 hp = hero.GetPos();
            _current.Clear();
            foreach (Poi poi in Surroundings.CollectVisible(hero)) {
                int x = (int)poi.Pos.x - (int)hp.x;
                int y = (int)poi.Pos.y - (int)hp.y;
                _current.Add(new ObjectRadarRing.Entry(x, y, poi.Category));
            }
            _ring.Load(_current);
        }
    }
}
