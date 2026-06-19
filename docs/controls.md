# Tangledeep controls

Authoritative control listing for both the game and this mod, in one place.

The **game** bindings are extracted from the game's own live Rewired data, not
hand-transcribed. The mod ships a `ControlDumper` that, on reaching the title screen,
writes `tangledeep-controls.txt` into `BepInEx/plugins/TangledeepAccess/`; this file is
the human-formatted version of that dump. To regenerate after a game update or a rebind,
relaunch to the title and re-read that file. (Source data generated 2026-06-16; keyboard
bindings re-verified against live Rewired data on 2026-06-17 — which corrected the WASD
"Mark Favorite Item" entry, previously listed as unbound.)

The **mod** bindings are read straight from the plugin source
(`TangledeepAccess/Patches/TDInputHandler_UpdateInput_Patch.cs`), not Rewired — see the
mod section for why that distinction matters under the WASD layout.

All real game input flows through Rewired as named actions; the legacy `InputMapper` /
`TDControl` KeyCode path in the game code is vestigial.

## How to read the keyboard listing

The game ships two keyboard layouts: **Default** (arrows / numpad) and **WASD**
(selectable in Options). To avoid comparing two separate sections, every keyboard line
below lists **both** layouts at once, in this format:

> `Default-layout key(s), WASD-layout key(s): purpose`

- A `/` within one layout means either key works (e.g. `Up Arrow / Keypad 8`).
- `(unbound)` means that layout has **no default key** for the action; it would have to
  be rebound in Options. (Several actions are unbound under WASD — see the unbound
  entries inline.)
- `(hold)` means the key is held, not tapped.
- Mouse and gamepad are excluded from this listing and kept in a separate section at the
  end.

## Diagonal movement (no-numpad keyboards)

The **only dedicated diagonal keys are the numpad** — Keypad 7/9/1/3 — in *both*
layouts. There is no letter or arrow key that, by itself, steps diagonally. With arrows
(Default) or WASD, you move diagonally one of these ways:

1. **Press two adjacent direction keys together** (e.g. Up + Right = Northeast,
   Down + Left = Southwest). This is the normal no-numpad method.
2. **Hold Left Shift ("Diagonal Move Only") + two adjacent keys** — forces a diagonal and
   guarantees no accidental cardinal step.
3. **Keypad 7/9/1/3** — one keystroke per diagonal, instant, identical in both layouts.

Why method 1 works, and the timing wrinkle, from
`TDInputHandler.CheckForDiscreteDirectionalInput` (decompiled `TDInputHandler.cs:810`,
fed by `GetDirectionalInput` at `:2466`):

- The dedicated diagonal actions ("Move Up+Left", etc.) are bound *only* to the numpad,
  so with arrows/WASD that path never fires; the handler instead reads the two cardinal
  axes (left/right and up/down). Opposing keys resolve left-over-right and up-over-down.
- **Without Left Shift:** a 3-frame debounce buffer (`CheckMoveAgainstFrameBuffer` /
  `UpdateFrameBuffer`, `:905`) holds a new input until the same `(x, y)` has persisted for
  3 consecutive frames before committing. That settle window is exactly what lets two keys
  pressed a frame or two apart coalesce — pressing Up then Right never commits a pure
  North; once both are held and stable it commits Northeast. A single key, held, just
  resolves to that cardinal after the same brief settle.
- **With Left Shift held:** the cardinal-assignment block *and* the debounce are skipped.
  A single cardinal key produces **no movement at all**; only a genuine two-axis press
  yields a diagonal, immediately. So Shift is a "lock to diagonals" modifier.

Practical note for non-visual play: method 1 depends on frame timing and is fiddly
without visual feedback; the **numpad diagonals are the deterministic single-keystroke
option** and behave identically in both layouts. The mod's Look cursor (below) uses the
same numpad diagonals.

## Keyboard — game actions (both layouts per line)

### Movement and turns

- Up Arrow / Keypad 8, W / Keypad 8: move North
- Down Arrow / Keypad 2, S / Keypad 2: move South
- Left Arrow / Keypad 4, A / Keypad 4: move West
- Right Arrow / Keypad 6, D / Keypad 6: move East
- Keypad 7, Keypad 7: move Northwest
- Keypad 9, Keypad 9: move Northeast
- Keypad 1, Keypad 1: move Southwest
- Keypad 3, Keypad 3: move Southeast
- Left Shift (hold), Left Shift (hold): Diagonal Move Only (force diagonal)
- Keypad 5 / Space, Space: wait / pass turn
- D, Return / Keypad Enter: use stairs (travel)

### Combat and weapons

- F, F: fire ranged weapon
- R, R: rotate targeting shape
- F5 / Ctrl + 1, (unbound): switch to weapon 1
- F6 / Ctrl + 2, (unbound): switch to weapon 2
- F7 / Ctrl + 3, (unbound): switch to weapon 3
- F8 / Ctrl + 4, (unbound): switch to weapon 4
- [, Q: cycle weapons left
- ], E: cycle weapons right

### Hotbar

There are **2 hotbars (pages) of 8 slots each** (`UIManagerScript.MAX_HOTBARS = 2`,
`HotbarHelper.SLOTS_PER_HOTBAR = 8`; the backing array is 16 bindables). Keys `1`–`8` act
on whichever page is active; Left Control flips between the two. There is no slot 9 or 10,
so number-row `9` and `0` are unbound in both layouts.

- 1 through 8, 1 through 8: use hotbar slot 1–8 (on the active page)
- Left Control, Left Control: cycle hotbars (switch between the 2 pages)

### Items and interaction

- G, (unbound): pick up item
- U, (unbound): use healing flask
- P, P: use town portal
- U, U: use consumable (from menu)
- U, U: unequip item
- D, (unbound): drop item (consumable or equipment)
- F, F: mark favorite item
- Minus (-), (unbound): mark as trash
- V, V: use shovel
- T, T: use monster mallet

### Menus and panels

- Keypad Enter / Return, Keypad Enter / Return: confirm / interact
- ESC, ESC: cancel
- ESC, ESC: open game menu (Default action "Options Menu"; WASD action "Toggle Menu Select")
- I, I: inventory (view consumables)
- E, (unbound): equipment
- C, C: character info
- J / S, J: skills
- Q, (unbound): rumors
- F1, F1: help
- F1, F1: UI page left
- F2, F2: UI page right
- Page Up, Page Up: list page up
- Page Down, Page Down: list page down
- Tab, (unbound): jump to searchbar

### Map, HUD, and display

- Tab, Tab: toggle large minimap
- Equals (=), (unbound): cycle minimap
- X, (unbound): examine mode
- H, H: hide UI
- B, B: toggle player health bar
- M, M: toggle monster health bars
- O, O: toggle pet HUD
- Left Shift (hold), Left Shift (hold): compare alternate (in item comparisons)

### Notes on shared and overloaded keys

The game multiplexes some keys by context:

- Default `D` is use stairs *and* drop item; under WASD `D` is move East, so stairs move
  to Enter and drop has no default key.
- `U` is use healing flask, use consumable, and unequip item (all on `U` under Default;
  under WASD only the consumable/unequip uses remain).
- `F` is fire ranged weapon and mark favorite item.
- `F1` is help and UI page left; `Tab` is toggle large minimap and jump to searchbar.
- `ESC` is cancel and open-menu.
- Left Shift is both "Diagonal Move Only" and "Compare Alternate" (contexts don't overlap).

The WASD layout binds a **smaller default set**: switch-to-weapon 1–4, pick up item,
examine mode, drop item, view equipment, view rumors, use healing flask, mark as trash,
and cycle minimap all have no default key and would need rebinding. (View skills is `J`
only — no `S`, which is now move South.)

## Keyboard — mod actions (TangledeepAccess)

The mod reads these as **raw Unity `KeyCode`s**, *not* through Rewired, so they are
**identical regardless of which game layout is selected** and do not appear in the Rewired
dump. They were chosen from keys the **Default** layout leaves unbound, so under Default
they shadow nothing. Source: `TDInputHandler_UpdateInput_Patch.cs`.

In free play (no menu/dialog open):

- K: read here — the hero's tile (map, coordinates, terrain, items on it, open exits)
- L: scan everything in line of sight (hostiles first, then nearest)
- Y: status (health, stamina, energy, level, active temporary effects)
- A: hotbar readout (abilities/items on the active page)
- ` (backtick): cycle to the next hotbar page and read it. Replaces the game's Ctrl "Cycle
  Hotbars" — Ctrl is the screen reader's stop-speech key, so the mod strips that binding on
  load (see `KeymapPatch`) and owns the cycle itself.
- ' (apostrophe): repeat the last spoken phrase
- / (slash): help — speak the list of mod commands
- ; (semicolon): toggle the Look cursor (examine the map without moving)

In menus and overlays (inventory, skill sheet, dialogs — the active overlay owns input):

- Arrow keys: move between controls (rows are up/down, items within a row left/right)
- Enter: confirm — the control's primary action (use / eat, equip, learn, switch mode)
- K: read full info — the focused control's detailed tooltip
- F / − (minus): toggle favorite / trash (inventory items)
- 1 through 8: in the skill sheet's slot view, assign the focused active ability to that
  hotbar slot, on the active page (cycle pages with backtick first to reach slots 9–16)
- ` (backtick) and A: cycle / read the hotbar — these work even with an overlay open, so you
  can pick the page before assigning

While the Look cursor is active (it owns these keys and suppresses game input):

- Arrow keys: step the cursor orthogonally
- Keypad 8 / 2 / 4 / 6: step the cursor orthogonally
- Keypad 7 / 9 / 1 / 3: step the cursor diagonally
- ] : jump to the next point of interest (monster, item, stairs)
- [ : jump to the previous point of interest
- Home: re-center the cursor on the hero
- ; : turn the Look cursor off

### WASD-layout caveat

Because the mod keys bypass Rewired, they do **not** move when you switch the game to the
WASD layout — and one collides:

- **`A` (mod: hotbar readout) sits on top of `A` = move West under WASD.** In free play the
  mod consumes the frame, so pressing `A` speaks the hotbar instead of stepping. This is
  the one genuine conflict. (`K`, `L`, `Y`, `'`, `/`, `;` stay clear in both layouts.)
- `[` and `]` are Cycle Weapons Left/Right under Default, but the mod only claims them
  *while the Look cursor is active*, and the Look cursor already suppresses game input, so
  there is no conflict there.

The mod's hotkeys therefore assume the **Default** layout. If you play WASD, expect the
`A` clash above (rebind the game's move-West, or the mod hotkey, to resolve it).

## Mouse

Captured from the same Rewired dump (listed here only for completeness):

- Right Mouse Button: cancel
- Mouse Button 3: mark as hostile

## Gamepad

Not captured: no controller was connected when the dump was generated. Connect a gamepad
and regenerate (relaunch to the title screen) to capture the controller bindings; the mod
reads them from the same Rewired data via the joystick maps.
