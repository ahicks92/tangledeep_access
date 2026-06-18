# Tangledeep minimap — what it shows, and when

How the game's minimap is built and what it reveals, as reference for a non-visual
replacement. This documents the **game** and the **sighted** behavior only; it makes no
claim about how the mod surfaces any of it. Citations are `File.cs:line` in the decompiled
game source OUTSIDE this repo at `../tangledeep-decompiled/Assembly-CSharp/`. Verified
against the decompile on 2026-06-18.

Companion docs: `targeting.md`, `ui-framework.md`.

## Where it's built

The minimap texture is generated tile-by-tile in `TileMeshGenerator.GenerateTextureForMap`
(`TileMeshGenerator.cs:441`). Each tile gets two computed indices:

- a **base / terrain** index from `GetBaseMinimapConvertedIndex` (`TileMeshGenerator.cs:190`)
- an **overlay / icon** index from `GetOverlayMinimapConvertedIndex` (`TileMeshGenerator.cs:238`)

The overlay is painted on top of the base when present; otherwise the base terrain color
shows. There are two render styles of the same texture (opaque and a blended-edge
"translucent" variant); both run identical index logic — the translucent flag is a visual
style, not a visibility tier.

## The one gate: explored, not visible

A tile is touched at all only if it passes the test at `TileMeshGenerator.cs:496`:

```
k==0 || j==0 || k==columns-1 || j==rows-1            // map border
  || MapMasterScript.activeMap.exploredTiles[k, j]   // ever explored
  || UIManagerScript.dbRevealMode                    // debug reveal
```

There is **no current-line-of-sight check anywhere in the minimap path.** This single fact
drives everything below: the minimap is keyed on *explored* (`exploredTiles`, a per-map
`bool[,]`), never on *currently visible*.

This is distinct from the fog-of-war overlay on the main game view (`FogOfWarScript`), which
*does* track current sight via the hero's `visibleTilesArray` versus ever-seen
`exploredTiles`. That dimming system is separate and does not change the minimap logic here.

## What gets drawn

**Base / terrain layer** (`GetBaseMinimapConvertedIndex`), by tag in priority order: wall,
islands-water, electric, water, lava, mud/summoned-mud, else plain ground. It also renders
player-collidable non-targetable destructibles as "wall", slime towers as ground, and
lava-like hazards as lava. An actor only contributes here if its `visibleOnMinimap` flag is
set (`TileMeshGenerator.cs:218`).

**Overlay / icon layer** (`GetOverlayMinimapConvertedIndex`) returns "nothing" if the tile
has no actors; otherwise the first match wins, in this order:

1. Hero (`heroPCActor.GetPos() == tile.pos`)
2. Shrine / fountain
3. Slime-tower state (friendly / enemy / unslimed)
4. Floor switch (active vs. destroyed)
5. Interactable NPC — shopkeeper (has `shopRef`) gets a distinct icon from a plain NPC
6. First targetable actor: ally monster, champion/boss, the special `mon_fungalcolumn`,
   else a hostile monster; or for destructibles: monster-spawner / job-trial crystal,
   treasure sparkle, swinging vine, else a generic container (chest)
7. Items on the ground (`AreItemsInTile()`)
8. Stairs — up/exit vs. down

Every one of these is read from the tile's **live** actor list at regeneration time — not a
last-seen snapshot.

## Explored but not currently visible

Because the only gate is `exploredTiles`, overlay icons are drawn for explored tiles
**regardless of current line of sight**, reading live actor state. Consequences:

- Terrain of an explored tile persists permanently.
- A monster, NPC, item, or chest standing on already-explored ground **shows on the minimap
  at its real current position even when out of the hero's sight** — effectively a minimap
  "wallhack" for explored areas. This is not a stale ghost; it tracks the actor live.
- When an actor leaves a tile the icon is erased correctly. The loop detects the overlay
  index changed (icon → none), repaints the base terrain block over the old icon
  (`TileMeshGenerator.cs:510, 514, 540`), and draws no overlay — so movement across explored
  ground is tracked, not frozen.

There is no separate "explored-dim" tier in the minimap's own data; the only such
distinction lives in the fog-of-war view noted above.

## Unexplored regions

Blank, with one exception. An unexplored interior tile fails the `:496` gate and is never
painted (the texture initializes to black); nothing speculative is drawn. The **sole
exception is the outer map border** (first/last row and column), which always paints so the
level's rectangular edge is visible from the start. No interior unexplored tile, and nothing
standing on one, ever appears.

## What is queryable to reconstruct this

- `MapMasterScript.activeMap.exploredTiles[x, y]` — the gate; "has this tile ever been seen".
- `GetBaseMinimapConvertedIndex(tile)` / `GetOverlayMinimapConvertedIndex(tile)` — the game's
  own classification of a tile, if reuse is wanted.
- The underlying actor lists per `MapTileData` (read live) plus the priority above, if a
  hand-rolled classification is preferred.

The practical model: the minimap is a queryable, **live** view of the whole *explored* map,
including actors currently out of line of sight. Unexplored tiles must be treated as unknown
(mirror the border exception only if parity with the sighted view is wanted). Per the "never
cache game state" rule, read tile/actor state at speak time rather than snapshotting.
