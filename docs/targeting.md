# Tangledeep targeting — the sighted flow

How the game's ranged/ability targeting works end to end, and what a sighted player sees and
does, as reference for a future non-visual replacement. This documents the **game** and the
**sighted** flow only; it makes no claim about how the mod handles any of it. Citations are
`File.cs:line` in the decompiled game source OUTSIDE this repo at
`../tangledeep-decompiled/Assembly-CSharp/`. Verified against the decompile and a live
`/eval` enumeration on 2026-06-18.

Companion docs: `minimap.md`, `ui-framework.md`, `input-flow.md`.

## Entry and state

Using an ability with the `TARGETED` tag opens targeting via
`UIManagerScript.EnterTargeting(abil, prevDir)` (`UIManagerScript.cs:18718`). It sets
`abilityTargeting = true` and stores the active ability in `abilityInTargeting`. `EnterTargeting`
auto-picks a sensible first target on entry (`:18846`–`:18999`): the direction held when
targeting opened, else last-attacked enemy, else last attacker, else nearest hostile, else
nearest valid tile. So a useful target/orientation is already selected the instant targeting
begins. `UIManagerScript.CheckTargeting()` (`:5058`) reports whether targeting is active.

The visual "brains" are in `UIManagerScript`, not in `PlayerInputTargetingManager` /
`TargetingLineScript` (those only draw the line). Input during targeting is handled by
`TDInputHandler.CheckForTargetingInput` (`TDInputHandler.cs:1714`).

## The core split: shape-on-cursor vs. shape-from-hero

Every targeted ability uses one virtual cursor (`virtualCursorPosition`) and one stored 8-way
orientation (`lineDir`, `UIManagerScript.cs:1064`). The decisive fork is the **`CURSORTARGET`
tag**, which decides where the effect shape is anchored:

- **`CURSORTARGET`** → the shape is anchored **on the cursor tile**. The player drives a cursor
  and the blast/area is drawn wherever the cursor sits. Direction input *moves the cursor*. On
  fire, the origin is the cursor (`clickedPosition = virtualCursorPosition`). This is "point at
  a tile/enemy."
- **non-`CURSORTARGET`** → the shape is anchored **on the hero** and projected outward along
  `lineDir`. Direction input *rotates the shape* (via `TryRotateTargetingShape`). On fire the
  origin is the hero (`clickedPosition = heroPCActor.GetPos()`); the cursor position is largely
  irrelevant to the result. This is "aim a direction."

So a directional (line/cone) ability is not "a cursor that shoots toward the tile" — the thing
being manipulated is the 8-way `lineDir`, not a destination tile.

## Aiming a direction: lineDir, CANROTATE, snapping

There are **two** `TryRotateTargetingShape` overloads with different gates:

- `TryRotateTargetingShape(Directions dir)` (`UIManagerScript.cs:19269`) — gated on
  **`CANROTATE` only**. Sets `lineDir = dir`. This is what directional input (arrow/stick) and
  the mouse-angle path call (`TDInputHandler.cs:1759`–`1823`, `:1746`–`1757`).
- `TryRotateTargetingShape(bool clockwise)` (`UIManagerScript.cs:19292`) — gated on `CANROTATE`
  **and** the shape being in an inline flex list (`:19299`: FLEXLINE, POINT, FLEXCROSS,
  FLEXCONE, FLEXRECT, SEMICIRCLE, CLAW). This is what the **mouse wheel** and the gamepad
  **"Rotate Targeting Shape"** axis call (`TDInputHandler.cs:1719`–`1740`).

Whether an ability is aimable at all is governed by the **`CANROTATE` tag**. Without it,
`lineDir` is fixed at whatever `EnterTargeting` initialized; pressing a direction moves the
cursor but does not re-aim the shape.

`SnapNEWS` (`UIManagerScript.cs:19131`) snaps diagonal aims to a cardinal for shapes in
`rotatableTargetShapes` (`:2168`: includes both CONE and FLEXCONE) / `snappableTargetShapes`
(`:2159`). `SnapLine` (`:19109`) maps a diagonal to one of its two flanking cardinals.

## How a direction becomes tiles

`CreateShapeTileList(shape, abil, baseTile, localLineDir, range, playerUser)`
(`UIManagerScript.cs:17902`) expands a shape into the set of affected tiles. `GetFlexShape`
(`:19021`) first resolves a flex shape against the current direction:

- `FLEXLINE` → HLINE / VLINE / DLINE_NE / DLINE_SE depending on cardinal vs. diagonal.
- `FLEXCROSS` → CROSS (cardinal) or XCROSS (diagonal).
- `FLEXCONE` is left as-is and handled directly in the cone case.

Cones (`CONE` / `FLEXCONE` / `CLAW`, `:18158`) fan out one tile wider per range step. **The
cone expansion switch only handles N/E/S/W** (`:18170`–`:18184`); a diagonal `lineDir` falls
through to North, and `SnapNEWS` forces cone aims onto a cardinal — so **cones are effectively
4-way**, whereas FLEXLINE is genuinely 8-way via its DLINE diagonals. `CLAW` is the cone code
with a filter (`:18167`) that keeps only the two outer edges and the center spine.

The `CENTERED` tag changes the origin: non-centered shapes start at the hero and extend
outward; centered shapes extend symmetrically through the hero.

### CONE vs. FLEXCONE

Their tile footprint is computed by **identical** code (shared case at `:18158`); given the
same `lineDir`/range/`CENTERED`/offsets they hit the same tiles. The only behavioral
difference is that FLEXCONE can also be rotated by the dedicated rotate control (mouse wheel /
gamepad rotate axis) and plain CONE cannot — both respond identically to arrow/stick aiming
when `CANROTATE` is set. For describing *effect*, treat them as one wedge.

## Confirm, cancel, fire

Confirm assembles a `TargetData` (target tiles, target actors, clicked position, the ability),
handles multi-target abilities (`numMultiTargets`), calls `ExitTargeting()`
(`UIManagerScript.cs:19376`, clears `abilityTargeting`, hides the line), then fires via
`GameMasterScript.TryNextTurn` with an ATTACK/ABILITY `TurnData`
(`TDInputHandler.cs:1887`–`1996`). Cancel calls `ExitTargeting()` and clears the item-in-use.

## Player-facing taxonomy

Five experiences, distinguished by tags + which shape field is used (`targetShape` for cursor
abilities, `boundsShape` for directional ones):

1. **Pick a single enemy/tile** — `CURSORTARGET` + POINT. Move cursor, confirm. (Multi-target
   variant keeps the session open for several picks.)
2. **Drop an AoE on a chosen tile** — `CURSORTARGET` + an area `targetShape`
   (CIRCLE/RECT/BURST/CROSS/CIRCLECORNERS/CHECKERBOARD/RANDOM). Slide cursor, blast follows it.
3. **Aim a line/beam (8-way)** — non-`CURSORTARGET` + a line `boundsShape`
   (FLEXLINE/HLINE/VLINE/DIRECTLINE_THICK), usually `CANROTATE`.
4. **Cone/wedge from the hero (4-way)** — non-`CURSORTARGET` + CONE/FLEXCONE/CLAW/SEMICIRCLE,
   usually `CANROTATE`.
5. **Self / no targeting** — no `TARGETED` tag; pressing the ability just fires it.

Branch a non-visual scheme on **`CANROTATE` (aimable or fixed)** and **direction granularity
(4-way cone vs. 8-way line vs. cursor-placed AoE)** — never on cone-vs-flexcone.

## Shapes actually in use (live enumeration, 2026-06-18)

Reading `GameMasterScript.masterAbilityList` (765 abilities), the player jobs
(`masterJobList` → `CharacterJobData.JobAbilities`), and monsters
(`masterMonsterList` → `MonsterTemplateData.monsterPowers`):

- **Player**, 74 distinct targeted abilities across 15 jobs. `boundsShape`: RECT 50, then POINT,
  FLEXCONE, FLEXLINE (each ~4–5), CLAW 3, BURST 2, and one each of CIRCLECORNERS, CONE, FLEXRECT,
  FLEXCROSS, SEMICIRCLE. `targetShape`: POINT 66, RECT 4, FLEXLINE 3, FLEXCROSS 1.
- **Monsters**, 246 distinct abilities (173 targeted). `boundsShape`: RECT 217, FLEXCONE 7,
  BURST 6, FLEXLINE 4, CIRCLE 3, CLAW 2, FLEXCROSS 2, RANDOM 2, POINT 2, DIRECTLINE_THICK 1.
  `targetShape`: overwhelmingly POINT, plus RECT, FLEXLINE, CIRCLE, HLINE, FLEXCROSS, VLINE.

The dominant pattern (~90% of both sides) is `targetShape=POINT` + `boundsShape=RECT`: "aim at
a single tile/enemy", the RECT being a square sized by range (range 0 = the one tile). The
directional/aimable shapes are a clear minority.

Of the 24 `TargetShapes` values, 16 are authored on some targeted ability. **Never authored:**
XCROSS, DLINE_NE, DLINE_SE, BIGDIPPER, DIRECTLINE. Caveat: XCROSS and DLINE_NE/DLINE_SE are
still *produced at runtime* by `GetFlexShape` resolving a diagonally-aimed FLEXCROSS/FLEXLINE,
so footprint reproduction must handle them; only BIGDIPPER and DIRECTLINE appear genuinely
unused.

## Runtime shape swaps — the shape is not static

The enumerated shapes are a **baseline**. Several abilities have their `boundsShape`/`targetShape`
(and range/tags/offsets) swapped at use time on a per-cast/per-equip working copy, while the
authored data is never rewritten. Read the shape off the live ability at targeting time, not
from a static table.

- **Spell Shaper augments** (`AbilityScript.cs:900`–`994`, gated on `spellshift` + a
  `SpellShaperEffect`): `ESpellShape.BURST` → boundsShape BURST (range 2); `CONE` → FLEXCONE
  (range 3, +CANROTATE); `SQUARE` → targetShape RECT (+CANROTATE); `RAY` → boundsShape FLEXLINE
  (range 2, −CENTERED, +CANROTATE); `LINE` → targetShape FLEXLINE (+CANROTATE). This is why the
  SPELLSHAPER job reads as plain RECT statically yet can produce cones/lines/bursts.
- **Gear/status conditional swaps** (`GameMasterScript.cs` ability-setup path): `skill_icemissile`
  → boundsShape CLAW (+CENTERED) with the `blizzardgearbonus2` status (`:15659`); `skill_qistrike`
  → boundsShape FLEXCROSS (+CENTERED, range 3) with `qiwaveset` (`:15713`). The same path also
  bumps range/offsets on other skills by gear/status.

## Queryable data and naming

- `UIManagerScript.abilityInTargeting` — the active ability (read `range`, `targetRange`,
  `targetShape`, `boundsShape`, and tags via `AbilityScript.CheckAbilityTag(AbilityTags.X)`).
- `UIManagerScript.virtualCursorPosition` and `lineDir` — current aim.
- `TargetingMeshScript.goodTiles` / `badTiles` on the targeting meshes — the validated tile sets.
- `CreateShapeTileList(...)` — to preview the footprint of any candidate origin/direction.
- `AbilityScript.GetBoundsShapeText()` (`AbilityScript.cs:1202`) / `GetTargetShapeText()`
  (`:1207`) — localized shape names via key `misc_shape_<shape>`. Prefer these over inventing
  shape vocabulary. (The exact `misc_shape_*` strings live in the compressed localization bundle
  and were not extracted here; read them live if needed.)
