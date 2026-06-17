#!/usr/bin/env bash
# Dev helper: drive the new-game flow from the title screen to the name-entry
# screen via the dev HTTP endpoints, so an overlay change can be re-verified
# without manual clicking. Stops at NAMEINPUT. Prints the speech log at the end.
# Not a player feature; requires TANGLEDEEP_DEV=1 (run-game.ps1 sets it).
set -e
H=http://127.0.0.1:8770

post() { curl -s -X POST "$H/input" -d "$1" >/dev/null; }
evalc() { curl -s -X POST "$H/eval" -d "$1"; }
stage() { curl -s -X POST "$H/eval" -d 'System.Console.Write(TitleScreenScript.CreateStage.ToString());'; }

echo "title -> new game"
post confirm; sleep 1.2
echo "select slot 1"
evalc 'TitleScreenScript.titleScreenSingleton.OnSelectSlotConfirmPressed(-1);' >/dev/null; sleep 2

echo "continue through intros / mode / campaign until JOBSELECT"
for i in $(seq 1 12); do
  [ "$(stage)" = "JOBSELECT" ] && break
  post confirm; sleep 0.9
done

echo "select focused job, then SELECT JOB confirm"
post confirm; sleep 1.2   # pick job -> confirm sub-screen
post confirm; sleep 1.2   # SELECT JOB -> feat select

echo "feats: navigate to PICK FOR ME and confirm"
# from the feat list, go down to the bottom row then up to PICK FOR ME
for i in $(seq 1 9); do post down; sleep 0.3; done
post up; sleep 0.4         # land on PICK FOR ME
post confirm; sleep 1.2

echo "stage now: $(stage)"
curl -s "$H/speech?since=0"
