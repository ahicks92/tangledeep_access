using System.Collections.Generic;
using Rewired;
using TangledeepAccess.Util;
using UnityEngine;

namespace TangledeepAccess.Controls {
    /// <summary>
    /// Owns the in-game keyboard map so the mod can claim physical keys for itself. Run together
    /// whenever the game (re)builds its keyboard map:
    ///
    /// <para><b>1. Force the Default (numpad) layout.</b> Tangledeep ships two keyboard layouts
    /// (<c>KeyboardControlMaps.DEFAULT</c> = numpad, and <c>WASD</c>); the mod supports only Default.
    /// We reload it <i>unconditionally</i> via <c>Rewired.LayoutHelper.SwitchLayout</c>
    /// (gms-independent, safe even at the title screen) rather than only when a non-Default map is
    /// detected: a player who picked WASD persists that choice, and the saved map can load
    /// <i>after</i> our one startup pass — a conditional "switch only if currently non-Default" loses
    /// that race and leaves WASD live (movement broken). SwitchLayout also heals the Rewired
    /// persistence (it sets <c>PersistentPlayerSettings…layoutName = "Default"</c> and saves), but the
    /// game's <i>other</i> store — <c>PlayerOptions.defaultKeyboardMap</c> in <c>preferences.xml</c> —
    /// is independent, so we additionally force-write that back to Default (see
    /// <see cref="HealKeyboardPreference"/>); otherwise the wrong choice survives in that file.</para>
    ///
    /// <para><b>2. Evacuate claimed keys (<see cref="Table"/>).</b> The mod reads several keys raw
    /// via <c>UnityEngine.Input</c>; the game's binding on such a key must be cleared so it does not
    /// also fire. Each row is deleted outright or relocated (dragging <i>every</i> action on the
    /// source key, with axis polarity, onto a target key+modifier).</para>
    ///
    /// <para><b>3. Delete specific actions (<see cref="ActionDeletes"/>).</b> For a key that hosts
    /// several actions where we only want to drop one (e.g. Tab keeps Toggle Large Minimap but loses
    /// Jump to Searchbar).</para>
    ///
    /// <para><b>4. Mirror the numpad onto the left-hand letters (<see cref="Mirror"/>).</b> The 3x3
    /// movement block q/w/e a/s/d z/x/c is bound to the same actions as numpad 7/8/9 4/5/6 1/2/3,
    /// <i>without</i> removing the numpad bindings — both move. (Center s/5 is intentionally left
    /// alone.)</para>
    ///
    /// <para>Everything is idempotent within a pass (forcing a layout already Default is a no-op,
    /// evacuating an absent key removes nothing, the mirror skips an already-present binding).
    /// Applied at startup (the ready-poll in <c>Plugin.Update</c>) and re-applied after the game
    /// rebuilds its keyboard map via <c>GameMasterScript_SwitchControlMode_Patch</c>.</para>
    /// </summary>
    internal static class KeymapPatch {
        // Modifier conventions matching the game's own (it binds both sides, e.g. "LeftControl,
        // RightControl"), so either physical modifier key works.
        private const ModifierKeyFlags Alt = ModifierKeyFlags.LeftAlt | ModifierKeyFlags.RightAlt;
        private const ModifierKeyFlags Ctrl = ModifierKeyFlags.LeftControl | ModifierKeyFlags.RightControl;
        private const ModifierKeyFlags Shift = ModifierKeyFlags.LeftShift | ModifierKeyFlags.RightShift;

        // "Out of the way" combo for game features we keep but never expect to use — frees the bare
        // key while leaving the feature reachable behind all three modifiers.
        private const ModifierKeyFlags CtrlAltShift = Ctrl | Alt | Shift;

        /// <summary>
        /// One key the mod claims. The game's binding(s) matching (<see cref="SourceKey"/>,
        /// <see cref="SourceMod"/>) are removed; if <see cref="TargetKey"/> is set, each is recreated
        /// on the target key carrying its original action and axis polarity plus
        /// <see cref="TargetMod"/>. A null target means delete only.
        /// </summary>
        private readonly struct KeyEvac {
            public readonly KeyCode SourceKey;
            public readonly ModifierKeyFlags SourceMod;
            public readonly KeyCode? TargetKey;
            public readonly ModifierKeyFlags TargetMod;

            /// <summary>When set, relocate only this one game action (by name) and <i>own</i> the
            /// target combo; null means the legacy "move/clear every action on the source key".</summary>
            public readonly string Action;

            private KeyEvac(KeyCode sourceKey, ModifierKeyFlags sourceMod, KeyCode? targetKey, ModifierKeyFlags targetMod, string action) {
                SourceKey = sourceKey;
                SourceMod = sourceMod;
                TargetKey = targetKey;
                TargetMod = targetMod;
                Action = action;
            }

            /// <summary>Clear a key, discarding whatever the game bound to it.</summary>
            public static KeyEvac Delete(KeyCode sourceKey, ModifierKeyFlags sourceMod = ModifierKeyFlags.None) {
                return new KeyEvac(sourceKey, sourceMod, null, default, null);
            }

            /// <summary>Clear a key, relocating every action on it onto <paramref name="targetKey"/>.</summary>
            public static KeyEvac MoveTo(KeyCode sourceKey, KeyCode targetKey, ModifierKeyFlags targetMod = ModifierKeyFlags.None, ModifierKeyFlags sourceMod = ModifierKeyFlags.None) {
                return new KeyEvac(sourceKey, sourceMod, targetKey, targetMod, null);
            }

            /// <summary>
            /// Relocate a <b>single named</b> game action from <paramref name="sourceKey"/> onto
            /// <paramref name="targetKey"/>+<paramref name="targetMod"/>, and make that target combo
            /// host <i>exactly</i> that action. Use when the source key is also needed for something
            /// else (e.g. it is a movement-mirror target): only the named action leaves the source, and
            /// the target is cleared first so stray bindings that accumulated there (across sessions —
            /// Rewired persists the map) are scrubbed. Idempotent and self-healing.
            /// </summary>
            public static KeyEvac MoveAction(KeyCode sourceKey, string action, KeyCode targetKey, ModifierKeyFlags targetMod = ModifierKeyFlags.None, ModifierKeyFlags sourceMod = ModifierKeyFlags.None) {
                return new KeyEvac(sourceKey, sourceMod, targetKey, targetMod, action);
            }
        }

        /// <summary>A numpad key whose bindings are duplicated onto a letter key (numpad kept).</summary>
        private readonly struct KeyMirror {
            public readonly KeyCode Source;
            public readonly KeyCode Target;

            public KeyMirror(KeyCode source, KeyCode target) {
                Source = source;
                Target = target;
            }
        }

        /// <summary>
        /// Keys the mod claims, evacuated on top of the freshly forced Default layout. The Default
        /// layout supplies all stock bindings; this table lists only the deltas. See
        /// <c>docs/default-keymap.txt</c> for the stock layout.
        /// </summary>
        private static readonly KeyEvac[] Table = {
            // Free the right-hand mod block (u/i/j/...) — relocate game screens to Alt+digit. These
            // use MoveAction (single action, owns the target) because E/C/Q are ALSO movement-mirror
            // targets (Keypad9/3/7): a plain MoveTo would also relocate the mirrored movement onto the
            // Alt+digit combo, and re-apply on each launch (Rewired persists the map) would stack more
            // copies — so e.g. Alt+4 ends up opening the character sheet AND stepping the hero. Owning
            // the target combo scrubs that. I/J use it too for uniformity (no collision, but harmless).
            KeyEvac.MoveAction(KeyCode.I, "View Consumables", KeyCode.Alpha1, Alt),
            KeyEvac.MoveAction(KeyCode.E, "View Equipment", KeyCode.Alpha2, Alt),
            KeyEvac.MoveAction(KeyCode.J, "View Skills", KeyCode.Alpha3, Alt),
            KeyEvac.MoveAction(KeyCode.C, "View Character Info", KeyCode.Alpha4, Alt),
            KeyEvac.MoveAction(KeyCode.Q, "View Rumors", KeyCode.Q, Alt),
            KeyEvac.MoveTo(KeyCode.U, KeyCode.Semicolon),     // Healing Flask / Consumable / Unequip -> ;

            // D is freed for move-east. Both its actions are covered elsewhere: Use Stairs is
            // redundant with Confirm/Enter (TDInputHandler standing-on-stairs path), and Drop Item
            // is handled directly by the mod's inventory/equipment overlays.
            KeyEvac.Delete(KeyCode.D),

            // Kept but shoved out of the way behind Ctrl+Alt+Shift, freeing the bare letter.
            KeyEvac.MoveTo(KeyCode.M, KeyCode.M, CtrlAltShift),   // Toggle Monster Health Bars
            KeyEvac.MoveTo(KeyCode.O, KeyCode.O, CtrlAltShift),   // Toggle Pet HUD
            KeyEvac.MoveTo(KeyCode.H, KeyCode.H, CtrlAltShift),   // Hide UI; bare H freed for the monster scan

            KeyEvac.Delete(KeyCode.X),                        // Examine Mode (replaced by the mod's look cursor)
            KeyEvac.Delete(KeyCode.S),                        // View Skills duplicate (also on J)
            KeyEvac.Delete(KeyCode.PageUp),                   // mod always needs PageUp
            KeyEvac.Delete(KeyCode.PageDown),                 // mod always needs PageDown

            // Ctrl+1-8 is the mod's "fire hotbar bar 2" gesture: the patch forces the active bank to
            // 1 and lets the game's own UpdateInput fire the bare "Use Hotbar Slot N" binding (which
            // still triggers while Ctrl is held, since no-modifier Rewired bindings ignore held
            // modifiers). Any Ctrl+digit binding the game ships (default Ctrl+1-4 duplicate the F5-F8
            // weapon switches) must be cleared, or Rewired would (a) suppress the bare fire while Ctrl
            // is held and (b) double-fire that action. Delete all eight so every slot fires cleanly,
            // regardless of what the default map binds. Idempotent: deleting an absent binding no-ops.
            KeyEvac.Delete(KeyCode.Alpha1, Ctrl),
            KeyEvac.Delete(KeyCode.Alpha2, Ctrl),
            KeyEvac.Delete(KeyCode.Alpha3, Ctrl),
            KeyEvac.Delete(KeyCode.Alpha4, Ctrl),
            KeyEvac.Delete(KeyCode.Alpha5, Ctrl),
            KeyEvac.Delete(KeyCode.Alpha6, Ctrl),
            KeyEvac.Delete(KeyCode.Alpha7, Ctrl),
            KeyEvac.Delete(KeyCode.Alpha8, Ctrl),

            // Ctrl belongs to the screen reader (stop-speech). Default "Cycle Hotbars" sits here; the
            // mod removes the swap concept (both bars are directly addressable), so this just goes.
            KeyEvac.Delete(KeyCode.LeftControl),
        };

        /// <summary>Actions removed entirely (a key may keep its other actions).</summary>
        private static readonly string[] ActionDeletes = {
            "Jump to Searchbar",   // Tab keeps Toggle Large Minimap, loses search-jump
        };

        /// <summary>
        /// Numpad-to-letter movement mirror: the q/w/e a/s/d z/x/c block gets the same bindings as
        /// numpad 7/8/9 4/5/6 1/2/3, with the numpad left intact. Center (5 -> s) is omitted on
        /// purpose.
        /// </summary>
        private static readonly KeyMirror[] Mirror = {
            new KeyMirror(KeyCode.Keypad7, KeyCode.Q),
            new KeyMirror(KeyCode.Keypad8, KeyCode.W),
            new KeyMirror(KeyCode.Keypad9, KeyCode.E),
            new KeyMirror(KeyCode.Keypad4, KeyCode.A),
            new KeyMirror(KeyCode.Keypad6, KeyCode.D),
            new KeyMirror(KeyCode.Keypad1, KeyCode.Z),
            new KeyMirror(KeyCode.Keypad2, KeyCode.X),
            new KeyMirror(KeyCode.Keypad3, KeyCode.C),
        };

        /// <summary>
        /// Apply the forced layout + remaps if Rewired is ready. Returns true once it has run (so the
        /// startup poll can stop). Safe to call repeatedly.
        /// </summary>
        public static bool TryApplyWhenReady() {
            if (!ReInput.isReady) {
                return false;
            }

            Apply();
            return true;
        }

        /// <summary>Force the Default layout, then run the remap pipeline for every player.</summary>
        public static void Apply() {
            if (!ReInput.isReady) {
                return;
            }

            // Force the saved keyboard scheme back to Default before touching any map, so a player
            // who picked WASD (the only other scheme) can't have it reload from preferences.xml.
            HealKeyboardPreference();

            foreach (Player player in ReInput.players.Players) {
                ForceDefaultLayout(player);
                ApplyTable(player);
                ApplyActionDeletes(player);
                ApplyMirror(player);
            }
        }

        // Overwrite the game's stored keyboard-scheme choice (PlayerOptions, persisted to
        // preferences.xml) back to Default. This is a SEPARATE store from the Rewired persistence
        // that SwitchLayout heals: the game does not call SwitchControlMode on load, so a stale
        // defaultKeyboardMap here is what lets a player's wrong pick (WASD) resurface across launches.
        // No-op (and no disk write) once already Default.
        private static void HealKeyboardPreference() {
            if (PlayerOptions.defaultKeyboardMap == KeyboardControlMaps.DEFAULT
                && PlayerOptions.keyboardMap == KeyboardControlMaps.DEFAULT) {
                return;
            }

            Log.Info(
                "Healing saved keyboard scheme to Default (numpad); was default="
                + PlayerOptions.defaultKeyboardMap + ", current=" + PlayerOptions.keyboardMap
            );
            PlayerOptions.defaultKeyboardMap = KeyboardControlMaps.DEFAULT;
            PlayerOptions.keyboardMap = KeyboardControlMaps.DEFAULT;
            PlayerOptions.WriteOptionsToFile();
        }

        // Reload the Default (numpad) layout unconditionally. Uses LayoutHelper directly (not
        // GameMasterScript.SwitchControlMode) so it does not re-trigger the SwitchControlMode patch —
        // no recursion through our own postfix. Unconditional (not gated on detecting a non-Default
        // map) because the player's saved WASD map can load after our startup pass; SwitchLayout
        // reloads the stock Default map and persists layoutName="Default", so re-running it is
        // idempotent and self-healing.
        private static void ForceDefaultLayout(Player player) {
            LayoutHelper.SwitchLayout(player.id, ControllerType.Keyboard, 0, "Default", "Default");
        }

        private static void ApplyTable(Player player) {
            foreach (ControllerMap map in player.controllers.maps.GetMaps(ControllerType.Keyboard, 0)) {
                foreach (KeyEvac evac in Table) {
                    Evacuate(map, evac);
                }
            }
        }

        private static void ApplyActionDeletes(Player player) {
            foreach (ControllerMap map in player.controllers.maps.GetMaps(ControllerType.Keyboard, 0)) {
                foreach (string action in ActionDeletes) {
                    if (map.DeleteElementMapsWithAction(action)) {
                        Log.Info("Removed action binding: " + action);
                    }
                }
            }
        }

        private static void ApplyMirror(Player player) {
            foreach (ControllerMap map in player.controllers.maps.GetMaps(ControllerType.Keyboard, 0)) {
                foreach (KeyMirror mirror in Mirror) {
                    CopyKey(map, mirror.Source, mirror.Target);
                }
            }
        }

        // Snapshot the matching element maps first (we mutate the map below), then delete each and,
        // for a relocation, recreate it on the target carrying its action and axis polarity.
        private static void Evacuate(ControllerMap map, KeyEvac evac) {
            if (evac.Action != null) {
                EvacuateAction(map, evac);
                return;
            }

            List<ActionElementMap> matches = new List<ActionElementMap>();
            foreach (ActionElementMap ae in map.GetElementMaps()) {
                if (ae.keyCode == evac.SourceKey && ae.modifierKeyFlags == evac.SourceMod) {
                    matches.Add(ae);
                }
            }

            foreach (ActionElementMap ae in matches) {
                int actionId = ae.actionId;
                Pole pole = ae.axisContribution;
                map.DeleteElementMap(ae.id);
                if (evac.TargetKey.HasValue) {
                    map.CreateElementMap(actionId, pole, evac.TargetKey.Value, evac.TargetMod);
                }
            }

            if (matches.Count > 0) {
                string dest = evac.TargetKey.HasValue ? "to " + Describe(evac.TargetKey.Value, evac.TargetMod) : "(deleted)";
                Log.Info("Evacuated " + matches.Count + " binding(s) from " + Describe(evac.SourceKey, evac.SourceMod) + " " + dest);
            }
        }

        // Relocate a single named action and own its target combo (see KeyEvac.MoveAction). Removes
        // the action from the source key (leaving any other bindings there, e.g. a mirrored movement),
        // clears the target combo entirely (scrubbing stray/accumulated bindings), then binds the
        // action there. Self-healing across launches and idempotent within a pass.
        private static void EvacuateAction(ControllerMap map, KeyEvac evac) {
            InputAction action = ReInput.mapping.GetAction(evac.Action);
            if (action == null) {
                Log.Warn("KeymapPatch: unknown action '" + evac.Action + "', cannot relocate");
                return;
            }

            int actionId = action.id;
            KeyCode target = evac.TargetKey.Value;

            // Strip the action off the source key (the bare key is then free for movement/menu nav).
            int removedFromSource = DeleteWhere(
                map, ae => ae.keyCode == evac.SourceKey && ae.modifierKeyFlags == evac.SourceMod && ae.actionId == actionId
            );

            // Own the target combo: clear it, then bind exactly this action.
            int clearedFromTarget = DeleteWhere(
                map, ae => ae.keyCode == target && ae.modifierKeyFlags == evac.TargetMod
            );
            map.CreateElementMap(actionId, Pole.Positive, target, evac.TargetMod);

            if (removedFromSource > 0 || clearedFromTarget != 1) {
                Log.Info(
                    "Bound '" + evac.Action + "' to " + Describe(target, evac.TargetMod)
                    + " (removed " + removedFromSource + " from " + evac.SourceKey
                    + ", scrubbed " + clearedFromTarget + " stale on target)"
                );
            }
        }

        // Delete every element map matching the predicate; returns how many were removed. Snapshots
        // first because deleting mutates the map's collection.
        private static int DeleteWhere(ControllerMap map, System.Func<ActionElementMap, bool> predicate) {
            List<ActionElementMap> matches = new List<ActionElementMap>();
            foreach (ActionElementMap ae in map.GetElementMaps()) {
                if (predicate(ae)) {
                    matches.Add(ae);
                }
            }

            foreach (ActionElementMap ae in matches) {
                map.DeleteElementMap(ae.id);
            }

            return matches.Count;
        }

        // Duplicate every binding on the source key onto the target key, keeping the source.
        // Skips a binding already present on the target so a re-apply does not stack duplicates.
        private static void CopyKey(ControllerMap map, KeyCode source, KeyCode target) {
            List<ActionElementMap> sources = new List<ActionElementMap>();
            foreach (ActionElementMap ae in map.GetElementMaps()) {
                if (ae.keyCode == source) {
                    sources.Add(ae);
                }
            }

            int added = 0;
            foreach (ActionElementMap ae in sources) {
                if (HasMapping(map, ae.actionId, ae.axisContribution, target, ae.modifierKeyFlags)) {
                    continue;
                }
                if (map.CreateElementMap(ae.actionId, ae.axisContribution, target, ae.modifierKeyFlags)) {
                    added++;
                }
            }

            if (added > 0) {
                Log.Info("Mirrored " + added + " binding(s) from " + source + " to " + target);
            }
        }

        private static bool HasMapping(ControllerMap map, int actionId, Pole pole, KeyCode key, ModifierKeyFlags mod) {
            foreach (ActionElementMap ae in map.GetElementMaps()) {
                if (ae.actionId == actionId && ae.axisContribution == pole && ae.keyCode == key && ae.modifierKeyFlags == mod) {
                    return true;
                }
            }
            return false;
        }

        private static string Describe(KeyCode key, ModifierKeyFlags mod) {
            return mod == ModifierKeyFlags.None ? key.ToString() : mod + "+" + key;
        }
    }
}
