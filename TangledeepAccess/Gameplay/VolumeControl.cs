using TangledeepAccess.Controls;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// A hacky, one-time audio tuning aid: nudges the game's music / SFX / footsteps volume and
    /// saves it to disk, because the default music volume drowns out speech and cues. Not a real
    /// settings UI — just enough to dial values in once.
    ///
    /// The game stores each volume as an int in decibels on the static <c>PlayerOptions</c>
    /// (mirrored to a 0-100 "hundredBased" value the options slider shows), persists the dB to
    /// <c>preferences.xml</c>, and applies it by setting a Unity <c>AudioMixer</c> float. The dB
    /// range is -30 (silent) to 0 (full); footsteps clamp to -1 at the top (the game forbids 0).
    /// We adjust the dB field, recompute the mirror, push the value to the live mixer, and persist.
    /// </summary>
    internal static class VolumeControl {
        // dB per press. Coarse on purpose — this is a set-once tool, not a fine fader.
        private const int StepDb = 2;

        public static MessageBuilder Adjust(ModInputAction action) {
            var message = new MessageBuilder();

            MusicManagerScript mm = MusicManagerScript.singleton;
            if (mm == null) {
                return message.Fragment("Audio not ready.");
            }

            int delta = action.Dx * StepDb; // +1 louder, -1 quieter
            string label;
            int newDb;
            switch (action.Kind) {
                case ModInputKind.VolumeMusic:
                    newDb = Mathf.Clamp(PlayerOptions.musicVolume + delta, -30, 0);
                    PlayerOptions.musicVolume = newDb;
                    mm.SetMusicVolume(newDb);
                    label = "Music";
                    break;
                case ModInputKind.VolumeSfx:
                    newDb = Mathf.Clamp(PlayerOptions.SFXVolume + delta, -30, 0);
                    PlayerOptions.SFXVolume = newDb;
                    mm.SetSFXVolume(newDb, false);
                    label = "Sound effects";
                    break;
                case ModInputKind.VolumeFootsteps:
                    // The game forbids footsteps at 0 dB (clamps to -1), so cap there.
                    newDb = Mathf.Clamp(PlayerOptions.footstepsVolume + delta, -30, -1);
                    PlayerOptions.footstepsVolume = newDb;
                    mm.SetFootstepsVolume(newDb, false);
                    label = "Footsteps";
                    break;
                default:
                    return null;
            }

            // Keep the options-slider mirror in sync, then save the dB to preferences.xml so the
            // value survives the session.
            PlayerOptions.SetHundredBasedVolumeValuesFromBaseValues();
            PlayerOptions.WriteOptionsToFile();

            // Report the 0-100 value the player sees in the options menu (the game's own mapping:
            // 0% at -30 dB, 100% at 0 dB).
            int percent = Mathf.RoundToInt(100f * (1f + newDb / 30f));
            return message.Fragment(label + " " + percent + " percent");
        }
    }
}
