# Overnight session log — 2026-06-16/17

A running summary for the morning review, so you can see what changed and how to try it
without reading every commit. Architecture detail lives in `gameplay-access.md`,
`ui-framework.md`, `new-game-menu.md`.

## What now works

Starting from a mod that only spoke a startup line, the game is playable by ear from the
title screen into gameplay:

- **Title / save-slot / character creation** all speak. The new-game story intros, the
  image-only job buttons (full job readout derived and spoken), feats (name + description +
  which two are selected), and the name-entry screen (prompt + current name + job/mode/feats;
  RANDOM re-reads) are audible.
- **Modal dialogs** read their full body text, then choices — via a new one-shot
  "announcement" channel in the overlay framework (reusable for tutorials, level-ups).
- **In gameplay** the turn-by-turn log is spoken (combat, status, pickups, NPC barks),
  filtered by the game's own line-of-sight and verbose settings.
- **Spatial awareness** controls (keys chosen to not collide with the game):
  - `K` read-here, `L` scan (LOS sweep by direction/distance), `Y` status (HP/stamina/
    energy/level/effects), `;` look cursor (8-directional via arrows + numpad, Home
    re-centers, LOS-respecting), `'` repeat last phrase.
  - Stepping onto an item or hazardous terrain is announced automatically (plain ground is
    silent).
- **Full-screen panels** (inventory/equipment/skills/character sheet) speak the selected
  item/ability name + full tooltip, via a hook on the ImpactUI column model.
- **Ranged targeting** reads the target tile (contents, direction/distance, valid/invalid)
  as the aim cursor moves.

## How to try it

1. `./build.ps1` then launch via `./run-game.ps1` (a background task; it sets
   `TANGLEDEEP_DEV=1` and is silent unless `-Speech` is passed — turn NVDA on and pass
   `-Speech` for a real listen).
2. Drive the whole new-game flow with the game's own keys; everything should speak. Or, to
   jump straight into a game for testing, run `bash scripts/drive-newgame.sh` (uses the dev
   HTTP endpoints).
3. In gameplay, press `K`, `L`, `Y`, `;` + arrows. With speech off, read what *would* have
   been spoken via `curl -s http://127.0.0.1:8770/speech?since=0`.

The dev endpoints (`/eval`, `/speech`, `/gui/*`, `/screenshot`, `/input`) are unchanged
this session and documented in `CLAUDE.md`.

## Known gaps / next (in rough priority)

- **Shops / NPC interaction** not yet verified end-to-end (talk → buy). NPC dialogue should
  read via DialogOverlay; shop item lists may need a hook. Blocked on driving in-game
  movement over HTTP (the dev `/input` only covers menu nav), so it could not be exercised
  in the safe town — verifying this is the top morning task.
- **Custom name typing** in creation is deferred (default + RANDOM suffice).
- Terrain is the coarse tile type ("ground"/"water"/"wall").

Done since the first checkpoint: feat descriptions + selection state, 8-directional look
cursor, movement auto-announce, full-screen panel reading (inventory/equipment/skills/char
sheet via the ImpactUI column hook), a title-screen gating fix for the creation overlay,
ranged-targeting readout, and a repeat-last-phrase key.

## Notes / decisions worth your eye

- Character creation runs in title-screen context, where the in-game input hook never
  fires, so the mod is pure follow-and-speak there (the game drives navigation). That is
  why driving it over HTTP uses the game's own methods (e.g. `OnSelectSlotConfirmPressed`,
  `ConfirmedAndGameIsReadyToStart`) rather than synthetic input.
- All game-state reads and speech happen on the per-frame pump; Harmony hooks only set
  flags / enqueue, per the project rule. New mod control keys are `K L Y ;` + arrows/Home
  while the look cursor is active — all unbound in the game's Default layout.
