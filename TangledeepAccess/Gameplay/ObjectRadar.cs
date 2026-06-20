using System.Collections.Generic;
using TangledeepAccess.Audio;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Object radar (F2): a sweep of everything in line of sight. Each sweep takes a single snapshot of
    /// the visible set, sorts it by x then y, and pings one entity every <see cref="ScanCue.IntervalSeconds"/>
    /// — encoding its left-right offset as pan and its north-south offset as the reference/second-grain
    /// interval — until the snapshot is exhausted. The snapshot is frozen for the whole sweep: entities
    /// that appear or move mid-sweep are only picked up by the next snapshot, so the order is stable.
    ///
    /// <para>Two controls. <b>Ctrl+F2</b> toggles continuous mode: while on, sweeps repeat, resting
    /// <see cref="RestSeconds"/> between them. <b>Shift+F2</b> fires a single sweep now and cancels any
    /// sweep already in progress (so repeated presses always restart a clean scan); in continuous mode
    /// it just restarts the current cycle.</para>
    /// </summary>
    internal sealed class ObjectRadar : NavAid {
        // Tunable: the pause between the end of one sweep and the start of the next (continuous mode).
        // The within-sweep ping spacing is ScanCue.IntervalSeconds; this is the longer gap between sweeps.
        private const double RestSeconds = 1.5;

        private enum Phase {
            Idle,     // nothing running (continuous off, no one-shot in flight)
            Sweeping, // pinging through the current snapshot
            Resting,  // continuous mode, counting RestSeconds before the next sweep
        }

        private readonly ObjectRadarSnapshot _snapshot = new ObjectRadarSnapshot();
        private readonly List<ObjectRadarSnapshot.Entry> _current = new List<ObjectRadarSnapshot.Entry>();
        private Phase _phase = Phase.Idle;
        private double _timer;

        public ObjectRadar() : base("object radar", enabled: false) { }

        /// <summary>Ctrl+F2: toggle continuous mode.</summary>
        public override MessageBuilder OnCtrl() {
            MessageBuilder spoken = ToggleSpoken();
            _snapshot.Clear();
            if (Enabled) {
                // Arm as "rest already elapsed" so the first sweep starts on the next tick.
                _phase = Phase.Resting;
                _timer = RestSeconds;
            } else {
                _phase = Phase.Idle; // cancel any sweep/rest in flight
                _timer = 0.0;
            }
            return spoken;
        }

        /// <summary>Shift+F2: fire a single sweep now, cancelling any sweep already in progress.</summary>
        public override MessageBuilder OnShift() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null) {
                return null;
            }

            StartSweep(hero); // reloads the snapshot, so this cancels and replaces any current sweep
            return null;
        }

        public override void Tick(double dt) {
            if (_phase == Phase.Idle) {
                return;
            }

            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null
                || UIManagerScript.AnyInteractableWindowOpen()) {
                return; // pause the sweep out of play or while a menu/dialog is up
            }

            _timer += dt;

            if (_phase == Phase.Resting) {
                if (_timer < RestSeconds) {
                    return; // still resting between sweeps
                }
                StartSweep(hero);
                return;
            }

            // Sweeping.
            if (_timer < ScanCue.IntervalSeconds) {
                return;
            }
            _timer = 0.0;
            Ping();
        }

        // Take a fresh snapshot and ping its first entity. An empty snapshot ends the sweep at once
        // (which, in continuous mode, drops back to resting so the radar retries after RestSeconds).
        private void StartSweep(HeroPC hero) {
            LoadSnapshot(hero);
            _timer = 0.0;
            if (_snapshot.Count == 0) {
                EndSweep();
                return;
            }
            _phase = Phase.Sweeping;
            Ping();
        }

        // Ping the next entity in the current snapshot; when that was the last one, end the sweep (the
        // timer is already zeroed, so a continuous-mode rest is measured from this final ping).
        private void Ping() {
            ObjectRadarSnapshot.Entry? next = _snapshot.Next();
            if (next.HasValue) {
                TonePlayer.PlayTimeline(VoicePing(next.Value));
            }
            if (_snapshot.SweepDone) {
                EndSweep();
            }
        }

        // After a sweep: in continuous mode rest then sweep again; otherwise the one-shot is done.
        private void EndSweep() {
            _phase = Enabled ? Phase.Resting : Phase.Idle;
        }

        // The ping for one entity: its category's radar sample if one is loaded, else the default
        // triangle tone. Both share ScanCue's pan/pitch/level/envelope, so only the timbre differs.
        private static GrainTimeline VoicePing(ObjectRadarSnapshot.Entry e) {
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
                _current.Add(new ObjectRadarSnapshot.Entry(x, y, poi.Category));
            }
            _snapshot.Load(_current);
        }
    }
}
