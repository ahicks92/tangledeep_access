# Gameplay & menu accessibility — architecture

How the mod makes Tangledeep audible, and why it is built this way. Companion docs:
`ui-framework.md` (the game's menu internals), `new-game-menu.md` (creation research),
`input-flow.md`, `controls.md`. Written 2026-06-17; covers the overlay-framework
additions and the first gameplay reads.

## The overlay stack and priority

Speech is produced by a stack of *overlays*, each a small object that can claim "I am
active on this screen" and declare the controls it wants spoken. The dispatcher picks the
topmost active overlay each frame, builds its control graph, reconciles focus, and speaks.
Priority is **reverse registration order** (last registered wins). Current stack, low to
high (`Plugin.Awake`):

1. `GenericGameFocusOverlay` — the fallback. Mirrors whatever legacy `UIObject` graph the
   game currently has focus in and reads each control's raw widget text. Covers any screen
   without a bespoke overlay.
2. `CharCreationOverlay` — the job grid and name-entry screen (controls the generic reader
   cannot read: image-only job buttons; label-only name summary).
3. `DialogOverlay` — the modal dialog box. Above the per-screen overlays because a dialog
   is modal: when one is open it owns the screen, including over character creation (whose
   intros are themselves dialogs).
4. `SaveSlotOverlay` — the title save-slot screen. Above `DialogOverlay` because that
   screen is *built on* a dialog box (its header is dialog text) yet wants the bespoke slot
   reader; it only claims its exact stage and cedes to `DialogOverlay` the instant a real
   confirmation pops (which moves `CreateStage` off `SELECTSLOT`).

The ordering rule that keeps this sane: **an overlay should claim only the screens it
truly specializes**, so a higher-priority overlay going inactive cleanly reveals the one
below. `SaveSlotOverlay` and `CharCreationOverlay` both gate on precise conditions
(`CreateStage`, `creationActive` + a focused job button, `nameInputOpen`) rather than a
broad "are we in creation" flag.

## The one-shot announcement channel

The dispatcher's normal job is to speak the *focused* control and dedupe repeats. But much
game text appears with **no focus move**: a dialog's body, a tutorial popup, a level-up
prompt, the new-game story. To speak those, an overlay can declare an *announcement* on its
build: `builder.Announce(key, text)`. The dispatcher speaks the text once each time the
value-equatable `key` changes between frames, prepended to the focus label and independent
of the focus dedupe.

Keying by the **content itself** gives exactly-once behavior for free: a build that
re-renders every frame with the same text announces once; a changed message (the next
dialog page, a new random name) re-announces. `DialogOverlay` keys on the body text;
`CharCreationOverlay`'s name screen keys on the current name. This is a general primitive,
not a dialog hack — tutorials and level-ups will reuse it. Implemented in `GraphRender`
(carries `AnnounceKey`/`Announce`), `GraphBuilder`/`IOverlayBuilder` (the `Announce` API),
and `OverlayDispatcher` (tracks the last key, emits once, resets when the overlay closes).

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

The mod's first gameplay controls are on-demand **spatial queries** (`GameplayReader`):

- **Read here (`K`)** — the hero's tile: map name, coordinates, terrain type, ground items.
- **Scan (`L`)** — a Factorio-Access-style sweep of everything in line of sight, by
  direction and distance, hostiles first then nearest. Actors come from
  `activeMap.actorsInMap`; ground items from a visible-tile scan; both gated on
  `visibleTilesArray`. Pickups that exist as both an actor and a tile item are de-duped by
  name and tile.
- **Look cursor (`;`)** — a discrete tile cursor for examining the map without moving the
  hero. Toggling centers it on the hero; while active the input layer captures the arrow
  keys to step it (Home re-centers), reading each tile via the shared `TileDescriber`. The
  game's native Examine Mode is a smooth analog free-cursor (an icon nudged by a delta), so
  it does not map to arrow-key tile stepping — hence the mod's own integer cursor. A visible
  tile is fully described; an out-of-sight tile reads "not visible" plus its direction, so
  the cursor never reveals the unseen.

Tile contents are produced by `TileDescriber`, shared between read-here and the look
cursor: it leans on the game's `HoverInfoScript.GetHoverText` for the actor/feature on a
tile (empty for bare ground) and falls back to the tile type plus ground items.

Direction math is a pure, unit-tested Core helper (`GridDirection`) in the game's
`+x`-east / `+y`-north convention (verified from `MapMasterScript.xDirections`): component
offsets ("2 north, 3 east"), an 8-way compass, and Chebyshev step counts.

**Input model.** In gameplay the mod's input patch acts only when no menu is open, reads
its hotkeys (`K`/`L`), and consumes that frame. The keys are unbound in the game's Default
layout (`controls.md`), so consuming them shadows nothing. The hook only *requests* a
command (`UiRuntime.SetPendingGameplay`); the pump runs it through `GameplayReader` and
speaks — the same hook-requests / pump-executes split used for menu nav and the log, which
keeps all game-state reads and speech on the main-thread pump and out of Harmony hooks.

## Known gaps / next

- Tile terrain is the coarse `tileType` ("ground", "water", "wall"); a friendlier localized
  name can replace it.
- Feat descriptions on the PERKSELECT screen are not yet read (only feat names) — see the
  task list. Feats are a dialog, so this needs `CharCreation` to handle PERKSELECT and
  outrank `DialogOverlay` for that stage.
- The look cursor is 4-directional (orthogonal arrows); numpad diagonals are a natural
  add. No targeting support yet (ranged abilities); planned, will reuse `GridDirection`
  and the gameplay input layer.
- Custom name typing is deferred; the default name plus RANDOM make the screen completable.
