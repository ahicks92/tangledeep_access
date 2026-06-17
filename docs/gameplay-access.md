# Gameplay & menu accessibility — architecture

How the mod makes Tangledeep audible, and why it is built this way. Companion docs:
`ui-framework.md` (the game's menu internals), `new-game-menu.md` (creation research),
`input-flow.md`, `controls.md`. Written 2026-06-17; kept current as features land — now
covers the full menu/creation/dialog overlays, the turn log, all gameplay reads (tile,
scan, status, hotbar, look cursor, exits, repeat), ranged targeting, shops, and the passive
movement/health announcements.

## The overlay stack and priority

Speech is produced by a stack of *overlays*, each a small object that can claim "I am
active on this screen" and declare the controls it wants spoken. The dispatcher picks the
topmost active overlay each frame, builds its control graph, reconciles focus, and speaks.
Priority is **reverse registration order** (last registered wins). Current stack, low to
high (`Plugin.Awake`):

1. `GenericGameFocusOverlay` — the fallback. Mirrors whatever legacy `UIObject` graph the
   game currently has focus in and reads each control's raw widget text. Covers any screen
   without a bespoke overlay (including the shop and most full-screen panels' button names).
2. `DialogOverlay` — the modal dialog box (NPC dialogue, story intros, yes/no prompts).
   Above the generic mirror because a dialog is modal. Reads the body via the announcement
   channel, then the choices.
3. `CharCreationOverlay` — the title-screen creation screens the generic/dialog readers
   handle poorly: the job grid (image-only buttons → full job readout), feat select (a
   dialog, so it outranks `DialogOverlay` for the `PERKSELECT` stage and reads each feat's
   name + description + selected state), and name entry (prompt + name + job/mode/feats
   summary). Gated to `titleScreenGMS` so it never claims in-game screens.
4. `SaveSlotOverlay` — the title save-slot screen. Highest because that screen is *built on*
   a dialog box (its header is dialog text) yet wants the bespoke slot reader; it only claims
   its exact stage and cedes to the others the instant a real confirmation pops (which moves
   `CreateStage` off `SELECTSLOT`).

The ordering rule that keeps this sane: **an overlay should claim only the screens it
truly specializes**, so a higher-priority overlay going inactive cleanly reveals the one
below. `SaveSlotOverlay` and `CharCreationOverlay` both gate on precise conditions
(`CreateStage`, `creationActive` + a focused job button, `nameInputOpen`) rather than a
broad "are we in creation" flag.

## Why character creation is read-only follow-and-speak

The whole new-game flow runs in **title-screen context**, where `TDInputHandler.UpdateInput`
(the in-game input chokepoint the mod patches) is never called. So during creation the mod
does not drive navigation at all — the game handles the player's keys, fires `ChangeUIFocus`,
and the mod's focus hook records it; the dispatcher follows that focus and speaks. The job
readout and name summary are derived from game data (`GetFullJobReadout`, the creation
labels), never from the hover side effects, so they are available the instant focus lands.

## Speaking the turn log

`GameLogScript.GameLogWrite` is the single sink every gameplay event funnels through. A
Harmony **prefix** mirrors its own write gate — skip the multiline parent call (its split
pieces arrive separately), honor the verbose-combat-log option, and suppress events sourced
from an actor outside the hero's `visibleTilesArray` line of sight — and enqueues the cleaned
line into `GameEventLog`. Per the project's **speak-from-the-pump** rule, the hook only
buffers; `Plugin.Update` drains the buffer once per frame into one space-joined utterance and
speaks it with `interrupt: false`, so a multi-event turn is a single message and does not chop
menu navigation.

## Gameplay reads (tile reading, scanner) and the input model

On-demand gameplay queries are mod hotkeys, chosen from keys the Default layout leaves
unbound (`controls.md`) so they never shadow a game action. Most resolve through
`GameplayReader`:

- **Read here (`K`)** — the hero's tile: map name, coordinates, terrain type, ground items,
  and the walkable "exits" (the 8 neighbors that are not a wall/solid/blocked actor, via
  `MapTileData.IsCollidable`).
- **Scan (`L`)** — a Factorio-Access-style sweep of everything in line of sight, by
  direction and distance, hostiles first then nearest. Actors come from
  `activeMap.actorsInMap`; ground items from a visible-tile scan; both gated on
  `visibleTilesArray`. Pickups that exist as both an actor and a tile item are de-duped by
  name and tile.
- **Status (`Y`)** — health/stamina/energy (current of max), level, and active effects
  (the game's own status-bar filter: `showIcon && !passiveAbility`, named by `abilityName`
  with a turn count).
- **Hotbar (`A`)** — the active hotbar page's bound abilities/items by slot.
- **Repeat (`'`)** — re-speaks `PrismSpeech.LastSpoken` (handled in the pump, which owns the
  speech instance).
- **Look cursor (`;`)** — a discrete tile cursor for examining the map without moving the
  hero. Toggling centers it on the hero; while active the input layer captures the arrow
  keys to step it (Home re-centers), reading each tile via the shared `TileDescriber`. The
  game's native Examine Mode is a smooth analog free-cursor (an icon nudged by a delta), so
  it does not map to arrow-key tile stepping — hence the mod's own integer cursor. A visible
  tile is fully described; an out-of-sight tile reads "not visible" plus its direction, so
  the cursor never reveals the unseen. `]` / `[` jump it to the next / previous visible
  point of interest (nearest-first, wrapping) via `Surroundings.CollectVisible` — the shared
  visible-actor + ground-item collector the scanner also uses — for a quick tour without
  stepping tile by tile.

Tile contents are produced by `TileDescriber`, shared between read-here and the look
cursor: it leans on the game's `HoverInfoScript.GetHoverText` for the actor/feature on a
tile (empty for bare ground) and falls back to the tile type plus ground items.

Direction math is a pure, unit-tested Core helper (`GridDirection`) in the game's
`+x`-east / `+y`-north convention (verified from `MapMasterScript.xDirections`): component
offsets ("2 north, 3 east"), an 8-way compass, and Chebyshev step counts.

**Input model.** In gameplay the mod's input patch acts only when no menu is open, reads its
hotkeys (`K` `L` `Y` `A` `'` `;`, plus arrows/numpad/Home while the look cursor is active),
and consumes that frame. The keys are unbound in the game's Default layout (`controls.md`),
so consuming them shadows nothing. The hook only *requests* a command
(`UiRuntime.SetPendingGameplay`); the pump runs it through `GameplayReader` and speaks — the
same hook-requests / pump-executes split used for menu nav and the log, which keeps all
game-state reads and speech on the main-thread pump and out of Harmony hooks.

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
- **Health** (`HealthWatcher`) — warns on crossing below half then a quarter health, each
  once, re-arming on recovery; interrupts (survival in a permadeath game).

## Speech channels summary

Every game-state read and all speech happen on the per-frame pump (`Plugin.Update`); Harmony
hooks only set flags / enqueue. The pump drains, in order: the overlay dispatcher (menu
focus), the gameplay-query result, targeting, panel selection, the game log (`interrupt:
false`), then movement and health watchers. Interrupting channels (menu/query/targeting/
panel/health) cut current speech for responsiveness; the log does not, so a multi-event turn
stays whole. In practice at most one input-driven channel fires per frame.

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
