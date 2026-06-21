# Gameplay & menu accessibility — architecture

How the mod makes Tangledeep audible, and why it is built this way. Companion docs:
`ui-framework.md` (the game's menu internals), `new-game-menu.md` (creation research),
`input-flow.md`. Written 2026-06-17; kept current as features land — now
covers the full menu/creation/dialog/panel overlays, the turn log, all gameplay reads (tile
read, exploration cursor, scanner, status, hotbar, repeat), ranged targeting, the navigation
aids, and the passive movement/weapon/danger/health announcements.

## The overlay stack and priority

Speech is produced by a stack of *overlays*, each a small object that can claim "I am
active on this screen" and declare the controls it wants spoken. The dispatcher picks the
topmost active overlay each frame, builds its control graph, reconciles focus, and speaks.
Priority is **reverse registration order** (last registered wins). Current stack, low to
high (`Plugin.Awake`):

1. `UnsupportedOverlay` — the fallback for any legacy `uiObjectFocus` screen without a
   bespoke overlay. A single owned node that announces "Unsupported menu" and captures input
   (navigation keys swallowed, others pass through so the player can back out).
2. `DialogOverlay` — the in-game modal dialog box (NPC dialogue, story intros, yes/no
   prompts). An owned vertical menu of the body plus one node per choice (via `OwnedChoices`).
3. Title-screen overlays, each gated to title context and to its exact stage:
   `TitleDialogOverlay` (catch-all for title narrative dialogs), `TitleMenuOverlay` (the main
   menu), `FeatSelectOverlay` (the `PERKSELECT` dialog), `JobGridOverlay` (image-only job
   buttons → full job readout), `NameEntryOverlay` and `BeginScreenOverlay` (the `NAMEINPUT`
   stages), and `SaveSlotOverlay` (the `SELECTSLOT` screen — built on a dialog box yet wanting
   the bespoke slot reader). The screen-specific ones are registered above the catch-all so
   they win on their own screens.
4. In-game panel overlays, each claiming only its own screen and running its own captured
   cursor: `InventoryOverlay` (I), `SkillSheetOverlay` (J), `EquipmentOverlay` (E),
   `ShopOverlay` (merchant/banker), `CharacterSheetOverlay` (C), `OptionsOverlay` (Esc menu).

The ordering rule that keeps this sane: **an overlay should claim only the screens it
truly specializes**, so a higher-priority overlay going inactive cleanly reveals the one
below. Each gates on a precise condition (its `CreateStage`/screen) rather than a broad flag.

## How character creation is driven

The whole new-game flow runs in **title-screen context**, where `TDInputHandler.UpdateInput`
(the in-game input chokepoint) is never called. The mod therefore runs a separate title input
pump — `InputChain.RouteTitle`, driven from a `TitleScreenScript` hook — that offers each
frame to the menu drainer, so overlays work here just as they do in game. Two patterns, by
screen:

- **Capturing overlays drive themselves.** The screens the generic/dialog readers handle
  poorly — the job grid, feat select, name entry, begin screen — declare `CaptureInput`, so
  the dispatcher hands them the frame and they run their own cursor (start node + mod nav)
  instead of chasing the game's focus.
- **Simple screens follow.** The main menu and save-slot screens are non-capturing: the game
  drives navigation, fires `ChangeUIFocus`, the mod's focus hook records it, and the
  dispatcher follows that focus and speaks.

Either way the job readout and name summary are derived from game data (`GetFullJobReadout`,
the creation labels), never from hover side effects, so they are available the instant focus
lands.

## Speaking the turn log

`GameLogScript.GameLogWrite` is the single sink every gameplay event funnels through. A
Harmony **prefix** mirrors its own write gate — skip the multiline parent call (its split
pieces arrive separately), honor the verbose-combat-log option, and suppress events sourced
from an actor outside the hero's `visibleTilesArray` line of sight — and enqueues the cleaned
line into `GameEventLog`. Per the project's **speak-from-the-pump** rule, the hook only
buffers; `Plugin.Update` drains the buffer once per frame into one space-joined utterance and
speaks it with `interrupt: false`, so a multi-event turn is a single message and does not chop
menu navigation.

## Gameplay reads (tile, cursor, scanner) and the input model

On-demand gameplay queries are mod hotkeys, chosen from keys the Default layout leaves
unbound (`Controls/InputKeys.cs`) so they never shadow a game action. The free-play queries
resolve through `GameplayReader`; the exploration cursor and the scanner have their own
modules.

- **Read here (`S`)** — the hero's tile: map name, coordinates, terrain type, ground items,
  and the walkable "exits" (the 8 neighbors that are not a wall/solid/blocked actor, via
  `MapTileData.IsCollidable`).
- **Status (`Y`)** — health/stamina/energy (current of max), level, and active effects
  (the game's own status-bar filter: `showIcon && !passiveAbility`, named by `abilityName`
  with a turn count).
- **Hotbar** — the mod drops the game's swap concept and makes both bars directly addressable.
  `1`-`8` fire **bar 1**, `Ctrl+1`-`8` fire **bar 2**; `` ` `` (backtick) speaks bar 1 and
  `` Ctrl+` `` speaks bar 2. The assign UIs (skill-sheet abilities, inventory consumables) follow
  the same scheme: `1`-`8` assign to bar 1, `Ctrl+1`-`8` to bar 2. Firing is the game's own path:
  for `Ctrl+digit` the input patch momentarily forces `indexOfActiveHotbar` to 1 and lets the
  game's `UpdateInput` fire (so all its guards + log→speech stay intact), then resets it (relies on
  the bare `"Use Hotbar Slot N"` Rewired binding still firing while Ctrl is held). The game's Ctrl
  "Cycle Hotbars" and the `Ctrl+1`-`8` weapon-switch dupes are stripped on load — see `KeymapPatch`.
- **Repeat (`'`)** — re-speaks `PrismSpeech.LastSpoken` (handled in the pump, which owns the
  speech instance).
- **Combat-log history (`Ctrl+[` / `Ctrl+]`)** — step back / forward through the spoken log.
- **Exploration cursor** — a discrete tile cursor for examining the map without moving the
  hero, always live (no toggle). The 8 keys around `K` step it (`u i o / j l / m , .` =
  NW N NE / W E / SW S SE, in the `+x`-east / `+y`-north convention); Shift turns a step into
  a skip to the next terrain/shape change or occupant. `K` reads the cursor's tile (occupant
  first, then terrain/shape, then items), Shift+K examines it in full (the game's tooltip),
  Alt+K toggles follow-the-hero mode, Ctrl+K recenters on the hero. An unexplored tile reads
  "unexplored"; an explored-but-unseen tile is tagged "blurred", so the cursor never reveals
  the unseen.
- **Scanner** — a Factorio-Access-style categorized, distance-sorted readout of the map's
  features (`Scanner`). Page Up/Down step entries, Ctrl+Page Up/Down step categories; Home
  points the cursor at the selection, Shift+Home examines it, Alt+Home toggles auto-jump, End
  rescans. `Surroundings.CollectVisible` is the shared visible-actor + ground-item collector.
- **Object radar (`F2`)** — a repeating audio sweep that pings every visible entity by
  direction (Ctrl+F2 continuous, Shift+F2 one sweep). One of the F-key navigation aids
  (wall echo is `F1`).

Tile contents are produced by `TileDescriber`, shared across read-here, the cursor, and the
scanner: it leans on the game's `HoverInfoScript.GetHoverText` for the actor/feature on a
tile (empty for bare ground) and falls back to the tile type plus ground items.

Direction math is a pure, unit-tested Core helper (`GridDirection`) in the game's
`+x`-east / `+y`-north convention (verified from `MapMasterScript.xDirections`): component
offsets ("2 north, 3 east"), an 8-way compass, and Chebyshev step counts.

**Input model.** Physical keys are read in one place (`Controls/InputKeys.cs`) and turned
into a `ModInputAction`; each context (free-play queries, the cursor, the scanner, the
hotbar, menu overlays) consults the key groups it owns. The keys are unbound in the forced
Default layout, so claiming them shadows nothing. A hook/source only enqueues the action;
the per-frame pump drains and realizes it, so all game-state reads and speech stay on the
main thread and out of Harmony hooks — the same enqueue / pump-executes split used for the
log. See `input-flow.md` for the full path.

Note: the game leaves `nameInputOpen`/`CreateStage` set after a game starts, so the
character-creation overlay is gated to `titleScreenGMS` to avoid it shadowing in-game screens.

## Ranged targeting

`PlayerInputTargetingManager.UpdateCurrentTargetingInformation(location, isGoodTile)` fires
as the aim cursor moves while targeting a ranged weapon or point/area ability. A postfix
hands the tile to `TargetingReader`, which the pump speaks: tile contents (shared
`TileDescriber`), direction/distance from the hero, and valid/invalid. Deduped by tile.

## Passive announcements (pump-polled, no input)

- **Movement** (`MovementWatcher`) — when the hero's tile changes, speaks ground items or
  non-ground terrain; plain ground is silent so walking is quiet.
- **Weapon slot** (`WeaponWatcher`) — when the active weapon-hotbar slot changes, speaks the
  weapon and slot (e.g. "sword slot 2"); suppressed when the verbose combat log is on.
- **Health** (`HealthWatcher`) — warns on crossing below half then a quarter health, each
  once, re-arming on recovery; interrupts (survival in a permadeath game).
- **Danger** (`DangerWatcher`) — an audio warning when the hero stands on a telegraphed
  attack square.
- **Combat radar** (`CombatRadar`) and the **navigation aids** (`NavAids`: wall echo on `F1`,
  object radar on `F2`) play their own audio cues per turn / per frame, independent of speech.

## Speech channels summary

Every game-state read and all speech happen on the per-frame pump (`Plugin.Update`); Harmony
hooks only set flags / enqueue. The pump drains, in order: the overlay dispatcher (menu
focus), realized input (gameplay queries, cursor, scanner, hotbar), targeting, the game log
(`interrupt: false`), then the passive watchers (weapon, movement, danger, navigation aids,
combat radar, health). Interrupting channels cut current speech for responsiveness; the log
does not, so a multi-event turn stays whole. In practice at most one input-driven channel
fires per frame.

## In-game movement for testing

The dev `/input` endpoint drives the hero over HTTP (`step <dir>`, `wait`, `stairs`,
`pickup`) by committing a `TurnData` through `GameMasterScript.TryNextTurn`, the same path a
keyboard step takes — the game resolves move/attack/NPC-interaction. This is how the town
interaction loop and dungeon combat were verified without a screen reader. See `CLAUDE.md`.

## Known gaps / next

- Tile terrain now names hazard/feature tags (lava, water, mud, electrified, laser, tree,
  grass) ahead of the coarse `tileType`; trap *objects* are actors and read via the scanner /
  hover. Remaining: localized terrain names and finer trap state.
- Inventory item *actions* (use/equip/drop from the panel) read but are not verified end to
  end. Custom name typing in creation is deferred (default + RANDOM suffice).
- Status names use the game's `abilityName`; a few exotic effects may still read tersely.
