using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// One navigation aid on an F-key slot. The base defines four optional hooks so each aid picks
    /// its own control scheme: <see cref="OnShift"/> (Shift+Fn) and <see cref="OnCtrl"/> (Ctrl+Fn)
    /// return a line to speak (or null); <see cref="OnMove"/> fires on each hero step; <see cref="Tick"/>
    /// runs every frame. <see cref="ToggleSpoken"/> is the common "flip Enabled and announce it" helper.
    /// </summary>
    internal abstract class NavAid {
        public string Name { get; }
        public bool Enabled;

        protected NavAid(string name, bool enabled) {
            Name = name;
            Enabled = enabled;
        }

        public virtual MessageBuilder OnShift() => null;
        public virtual MessageBuilder OnCtrl() => null;
        public virtual void OnMove() { }
        public virtual void Tick(double dt) { }

        protected MessageBuilder ToggleSpoken() {
            Enabled = !Enabled;
            return new MessageBuilder().Fragment(Name).Fragment(Enabled ? "on" : "off");
        }
    }

    /// <summary>
    /// Wall echo (F1): Ctrl+F1 toggles auto-on-move (<see cref="NavAid.Enabled"/>, on by default),
    /// Shift+F1 fires it once. The auto-on-move play itself lives in <see cref="CombatRadar"/>, which
    /// reads <see cref="Enabled"/> (via <see cref="NavAids.WallEchoAuto"/>) and combines the wall tones
    /// with monster-moved pings on one shared timeline — so this aid only owns the keys and the toggle.
    /// </summary>
    internal sealed class WallEchoAid : NavAid {
        public WallEchoAid() : base("wall echo", enabled: true) { }

        public override MessageBuilder OnCtrl() => ToggleSpoken();

        public override MessageBuilder OnShift() {
            WallEcho.Play();
            return null;
        }
    }

    /// <summary>
    /// The navigation-aid framework. A registry of aids, each on an F-key slot (F1 = index 0, …).
    /// Shift+Fn routes to the aid's <see cref="NavAid.OnShift"/>, Ctrl+Fn to <see cref="NavAid.OnCtrl"/>;
    /// both speak whatever the hook returns. <see cref="PollOnMove"/> edge-detects the hero tile once
    /// for all aids and calls their <see cref="NavAid.OnMove"/>; <see cref="Tick"/> drives the
    /// continuous aids every frame. Adding an aid is one entry in <see cref="Aids"/>.
    /// </summary>
    internal static class NavAids {
        private static readonly WallEchoAid Wall = new WallEchoAid();
        private static readonly NavAid[] Aids = {
            Wall,             // F1
            new ObjectRadar(), // F2
        };

        /// <summary>Whether wall echo's auto-on-move is enabled (Ctrl+F1). Read by <see cref="CombatRadar"/>.</summary>
        public static bool WallEchoAuto => Wall.Enabled;

        // Last hero tile, shared across all aids for the on-move hook.
        private static bool _have;
        private static int _lastX;
        private static int _lastY;

        public static MessageBuilder OnShiftKey(int index) {
            return (index >= 0 && index < Aids.Length) ? Aids[index].OnShift() : null;
        }

        public static MessageBuilder OnCtrlKey(int index) {
            return (index >= 0 && index < Aids.Length) ? Aids[index].OnCtrl() : null;
        }

        public static void Tick(double dt) {
            foreach (NavAid aid in Aids) {
                aid.Tick(dt);
            }
        }

        public static void PollOnMove() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null || !GameMasterScript.actualGameStarted || MapMasterScript.activeMap == null) {
                _have = false;
                return;
            }

            Vector2 pos = hero.GetPos();
            int x = (int)pos.x;
            int y = (int)pos.y;
            if (_have && x == _lastX && y == _lastY) {
                return;
            }

            bool first = !_have;
            _have = true;
            _lastX = x;
            _lastY = y;
            if (first) {
                return; // don't fire on the arrival tile
            }

            foreach (NavAid aid in Aids) {
                aid.OnMove();
            }
        }
    }
}
