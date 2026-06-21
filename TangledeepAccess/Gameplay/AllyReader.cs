using System.Collections.Generic;
using TangledeepAccess.Focus;
using TangledeepAccess.Speech;
using UnityEngine;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// The current-ally selection behind the Y family of free-play keys. The mod keeps one remembered
    /// selection (by <c>actorUniqueID</c>) over the hero's commandable creatures — the summoned
    /// monsters, which include the active pet — so the player can step through them, hear each one's
    /// status, and open its command menu without spatially cursoring onto it (the game only surfaces
    /// that menu from examine mode, which the mod removes).
    ///
    /// <para>State is just the selected id; everything else is re-queried live each call. The
    /// selection self-heals: if the remembered ally is gone (died, dismissed, list changed) it falls
    /// back to the active pet, else the first summon. Stale ids from a previous floor simply never
    /// match and re-default.</para>
    /// </summary>
    internal static class AllyReader {
        // The selected ally's actorUniqueID, or -1 for "none chosen yet / re-default".
        private static int _activeId = -1;

        /// <summary>Step the selection by <paramref name="dir"/> (+1 next, -1 previous) and speak the
        /// landing ally's status, or "no allies".</summary>
        public static void Cycle(MessageBuilder message, HeroPC hero, int dir) {
            List<Monster> allies = Allies(hero);
            if (allies.Count == 0) {
                message.Fragment(ModStrings.NoAllies);
                return;
            }

            Monster current = Resolve(hero, allies);
            int idx = allies.IndexOf(current);
            idx = ((idx + dir) % allies.Count + allies.Count) % allies.Count;
            _activeId = allies[idx].actorUniqueID;
            AppendStatus(message, hero, allies[idx]);
        }

        /// <summary>Speak the current ally's status (Shift+Y), or "no allies".</summary>
        public static void ReadActive(MessageBuilder message, HeroPC hero) {
            List<Monster> allies = Allies(hero);
            if (allies.Count == 0) {
                message.Fragment(ModStrings.NoAllies);
                return;
            }

            AppendStatus(message, hero, Resolve(hero, allies));
        }

        /// <summary>
        /// Open the current ally's command conversation (Alt+Y) via the game's own pet-behaviour menu,
        /// which the dialog overlay then voices. Returns a "no allies" message when there is nothing to
        /// command (the dialog carries the spoken result otherwise, so this returns null).
        /// </summary>
        public static MessageBuilder OpenActiveMenu() {
            HeroPC hero = GameMasterScript.heroPCActor;
            if (hero == null) {
                return null;
            }

            List<Monster> allies = Allies(hero);
            if (allies.Count == 0) {
                var message = new MessageBuilder();
                message.Fragment(ModStrings.NoAllies);
                return message;
            }

            PetPartyUIScript.StartPetBehaviorConversationFromRef(Resolve(hero, allies));
            return null;
        }

        // The hero's commandable creatures: every live summoned monster (the active pet is one of them).
        private static List<Monster> Allies(HeroPC hero) {
            var list = new List<Monster>();
            foreach (Actor a in hero.summonedActors) {
                if (a is Monster m && !ActorPresence.IsGone(m)) {
                    list.Add(m);
                }
            }

            return list;
        }

        // The remembered ally if it is still present, else the active pet, else the first summon.
        // Updates _activeId so a re-default sticks for the next call.
        private static Monster Resolve(HeroPC hero, List<Monster> allies) {
            foreach (Monster m in allies) {
                if (m.actorUniqueID == _activeId) {
                    return m;
                }
            }

            int petId = hero.GetMonsterPetID();
            foreach (Monster m in allies) {
                if (m.actorUniqueID == petId) {
                    _activeId = petId;
                    return m;
                }
            }

            _activeId = allies[0].actorUniqueID;
            return allies[0];
        }

        // Name, then the relative direction to it, then its status bar: HP, level, status effects, and
        // (for a temporary summon) the turns until it vanishes. Matches the "name, relative direction,
        // status bar" shape requested for the ally read.
        private static void AppendStatus(MessageBuilder message, HeroPC hero, Monster ally) {
            message.Fragment(GameLabelReader.Clean(ally.displayName) ?? ally.actorRefName);
            message.PushRelativeCoordinates(ally.GetPos() - hero.GetPos());

            StatBlock stats = ally.myStats;
            message.ListItem();
            message.PushFraction(
                (int)stats.GetStat(StatTypes.HEALTH, StatDataTypes.CUR),
                (int)stats.GetStat(StatTypes.HEALTH, StatDataTypes.MAX),
                "health"
            );
            message.ListItem("Level " + stats.GetLevel());
            GameplayReader.AppendStatuses(message, stats);

            if (ally.turnsToDisappear > 0) {
                message.ListItem(ModStrings.TurnsLeft(ally.turnsToDisappear));
            }
        }
    }
}
