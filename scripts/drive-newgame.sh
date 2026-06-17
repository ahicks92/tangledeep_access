#!/usr/bin/env bash
# Dev helper: drive a fresh launch from the title screen all the way into gameplay
# via the dev HTTP endpoints, so a gameplay change can be re-verified without
# manually clicking through character creation. Not a player feature; requires
# TANGLEDEEP_DEV=1 (run-game.ps1 sets it). Picks job index 0 and random feats.
set -e
H=http://127.0.0.1:8770

post()  { curl -s -X POST "$H/input" -d "$1" >/dev/null; }
ev()    { curl -s -X POST "$H/eval" -d "$1"; }
field() { ev "System.Console.Write($1);"; }

# Poll until an eval boolean expression prints True (timeout ~15s).
wait_true() {
  for _ in $(seq 1 30); do
    [ "$(field "$1")" = "True" ] && return 0
    sleep 0.5
  done
  echo "  TIMEOUT waiting for: $1" >&2; return 1
}
wait_stage() {
  for _ in $(seq 1 30); do
    [ "$(field "TitleScreenScript.CreateStage")" = "$1" ] && return 0
    sleep 0.5
  done
  echo "  TIMEOUT waiting for stage $1 (now $(field TitleScreenScript.CreateStage))" >&2; return 1
}

curl -s --retry 90 --retry-connrefused --retry-delay 1 "$H/health" >/dev/null
echo "title: waiting for ready"; wait_true "GameMasterScript.gmsSingleton != null && GameMasterScript.gmsSingleton.titleScreenGMS && UIManagerScript.uiObjectFocus != null"

echo "new game"; post confirm; wait_stage SELECTSLOT; sleep 1
echo "select slot 0"; ev 'TitleScreenScript.titleScreenSingleton.OnSelectSlotConfirmPressed(0);' >/dev/null

echo "intros -> JOBSELECT"
for _ in $(seq 1 14); do
  [ "$(field TitleScreenScript.CreateStage)" = "JOBSELECT" ] && break
  post confirm; sleep 0.9
done
wait_stage JOBSELECT

echo "pick job 0, confirm"; post confirm; sleep 1.2; post confirm; wait_stage PERKSELECT
# Pick two feats and advance deterministically (the "randomfeats" button's own path),
# rather than navigating the feat dialog, whose cursor position varies by timing.
echo "feats: random + advance"
ev 'GameStartData.ClearFeats();
var f = GameStartData.allFeats;
GameStartData.AddFeat(f[0]); GameStartData.AddFeat(f[1]);
GameStartData.newGame = true;
UIManagerScript.singletonUIMS.StartCharacterCreation_NameInput();' >/dev/null
wait_stage NAMEINPUT

echo "begin game"; ev 'CharCreation.singleton.ConfirmedAndGameIsReadyToStart();' >/dev/null
wait_true "GameMasterScript.actualGameStarted" && echo "IN GAME" || echo "FAILED to enter game"
field '"level loaded; hero at "+GameMasterScript.heroPCActor.GetPos()'
