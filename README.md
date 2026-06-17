# Tangledeep Access

A screen-reader accessibility mod for **Tangledeep** (Impact Gameworks), aiming
to make the turn-based roguelike fully playable without vision. Speech is the
primary interface, via [Prism](https://github.com/ethindp/prism) (a unified
screen-reader/TTS abstraction) through a hand-written P/Invoke binding.

Status: **early gameplay.** The full new-game flow is playable by ear — title menu,
save-slot selection, the story intros, job selection, feat selection, and name entry
all speak — and the mod drops you into the game and reads turn events, tiles, and a
line-of-sight scan. See **Features** and **Controls** below; architecture notes are in
`docs/`.

## Features

- **Spoken menus and dialogs.** A small overlay framework mirrors the game's menus and
  speaks the focused control as the game moves focus. Modal dialog boxes (NPC dialogue,
  the new-game story intros, yes/no prompts) read their full body text via a one-shot
  "announcement" channel, then the choices.
- **Character creation.** Save-slot panels, the image-only job buttons (each job's full
  readout — description, difficulty, passive bonuses — is derived and spoken), and the
  name-entry screen (prompt, current name, job/mode/feats summary; RANDOM re-reads the
  new name) are all readable.
- **Turn-by-turn game log.** Combat, status changes, pickups, and NPC barks are spoken as
  they happen, filtered by the game's own line-of-sight and verbose-log settings.
- **Movement feedback.** Stepping onto a tile with an item or non-ground terrain announces
  it automatically; plain empty ground stays silent, so walking is not chatty.
- **Health warnings.** Crossing below half health, then a quarter, is announced (and re-armed
  on recovery) — important in a permadeath game.
- **Full-screen panels.** Inventory, equipment, skills, and the character sheet speak the
  selected item/ability — its name as you navigate, then its full tooltip (cost, cooldown,
  effects). Navigate with the game's own keys.
- **Ranged targeting.** While aiming a ranged weapon or a point/area ability, the target tile
  is read as the cursor moves — its contents, direction and distance from the hero, and
  whether it is a valid target.
- **Shops & NPCs.** Talking to an NPC reads the dialogue and choices; in a shop, each item's
  details and gold cost are read as you browse.
- **Tile reading and a scanner.** On demand, read the hero's tile or sweep everything in
  line of sight by direction and distance (see Controls).

## Controls

Mod controls are chosen from keys the game's Default layout leaves unbound, so they do
not shadow any game action. Menus are navigated with the game's own keys; the mod just
speaks them.

- `K` — **Read here**: the hero's tile — map, coordinates, terrain, and any items on it.
- `L` — **Scan**: everything in line of sight, by direction and distance (hostiles first,
  then nearest).
- `Y` — **Status**: the hero's health, stamina, energy, level, and active (temporary) effects.
- `A` — **Hotbar**: the abilities/items bound to the active hotbar page, by slot number.
- `'` (apostrophe) — **Repeat**: re-speak the last phrase (e.g. a combat line you missed).
- `;` — **Look cursor**: toggle a tile cursor for examining the map without moving. While
  it is on, the **arrow keys** (and the **numpad**, including diagonals 7/9/1/3) step the
  cursor — each tile is read, respecting line of sight — and **Home** re-centers it on the
  hero; press `;` again to turn it off.

(More gameplay controls — targeting support, a Factorio-style rescan refinement — are planned.)

## Layout

The mod compiles to a **single managed DLL** (plus the native `prism.dll`).

- `TangledeepAccess/` — the BepInEx plugin (net472), the only product assembly.
  Engine/native glue at the root; engine-agnostic logic (Prism binding, speech wrapper,
  native loader, logging) under `Core/`, compiled straight in.
- `TangledeepAccess.Tests/` — offline xUnit tests (net8). Links the plugin's `Core/`
  sources directly (no product-DLL reference); no game launch.
- `third_party/prism/` — vendored Prism x64 runtime (`prism.dll`), header, and
  license. **Committed** for reproducible builds.
- `third_party/bepinex/` — vendored BepInEx 5.4.23.5 win-x64 (Unity Mono). **Committed.**
- `artifacts/` — all build output lands here (gitignored), not in per-project `bin`/`obj`.

The decompiled game source lives **outside** this repo (`../tangledeep-decompiled`)
and is never committed.

## Environment (verified)

- Tangledeep is **Unity 2020.3.37f1, Mono, x64**, full .NET 4.x BCL → plugin targets `net472`.
- Loader: **BepInEx 5.4.23.5 (x64)** + HarmonyX. No entrypoint tweak needed (unlike Unity 5.x).
- Speech: **Prism v0.16.6** (`prism.dll`, self-contained), cdecl, UTF-8 strings.
- Requires the .NET SDK (8 or 9) to build; a running screen reader (e.g. NVDA) to hear output.

## Build, install, run

```powershell
# One-time: install BepInEx into the game folder.
.\setup-bepinex.ps1

# Build the plugin and deploy it + the Prism runtime into BepInEx\plugins.
.\build.ps1

# Run the offline tests.
.\test.ps1

# Format all C# to one-true-brace style (Roslyn, driven by .editorconfig).
dotnet format TangledeepAccess.sln
```

All three scripts auto-locate the Steam install of Tangledeep; override with the
`TANGLEDEEP_GAME` environment variable.

Then launch Tangledeep. With a screen reader running you should hear
"Tangledeep Access \<version\> loaded. Hello world." within a couple seconds.

## Logs

- BepInEx: `<game>\BepInEx\LogOutput.log` (mod lines via the BepInEx logger).
- Unity player log: `%USERPROFILE%\AppData\LocalLow\ImpactGameworks\Tangledeep\Player.log`.
