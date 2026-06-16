# Tangledeep input flow (mechanical)

How player input is registered and flows through the game, for planning Harmony
hooks. Citations are `File.cs:line` in the decompiled game source, which lives
OUTSIDE this repo at `../tangledeep-decompiled/Assembly-CSharp/` (game) and
`../tangledeep-decompiled/Rewired_Core/` (Rewired). Verified against the decompile
on 2026-06-16.

Companion docs: `controls.md` (the binding listing), `ui-framework.md` (menus).

## Summary

- All real input is Rewired, read as named actions off `player` where
  `player = ReInput.players.GetPlayer(0)` (`GameMasterScript.cs:4357`, cached into
  `TDInputHandler.player` at `TDInputHandler.cs:236`). Reads look like
  `player.GetButtonDown("Wait Turn")`, `player.GetAxis("Move Horizontal")`.
- The legacy `InputMapper` / `InputControls` / `TDControl` KeyCode path is DEAD code:
  `TDControl` hardcodes `keyMap1 = keyMap2 = KeyCode.None` (`TDControl.cs:14`), nothing
  assigns them, and `InputMapper.GetControlDown` is never called by game logic. Ignore it.
- Mouse buttons are read through `TDTouchControls.GetMouseButton*` (a wrapper over
  `UnityEngine.Input` + touch), NOT Rewired.

## 1. The pump and the chokepoint

`TDInputHandler` is declared a `MonoBehaviour` (`TDInputHandler.cs:10`) but Unity never
ticks it: it has no `Update()`, and it is almost entirely static. It is initialized once
via `TDInputHandler.Initialize()` (`TDInputHandler.cs:226`).

The actual per-frame pump is `GameMasterScript.Update()` (`GameMasterScript.cs:945`),
the live `MonoBehaviour`, which calls:

```
TDInputHandler.UpdateInput();   // GameMasterScript.cs:1125, gated by `if (actualGameStarted)`
```

So the chain is: Unity → `GameMasterScript.Update()` → `TDInputHandler.UpdateInput()`
(`TDInputHandler.cs:250`, `public static void UpdateInput()`) → the many
`player.GetButtonDown(...)` reads.

`TDInputHandler.UpdateInput()` is THE single chokepoint for in-game input. A Harmony
prefix runs before the game reads anything that frame; a postfix runs after. Caveats:

- It only runs once gameplay has started (`actualGameStarted` gate at the call site),
  NOT on the title screen.
- The title screen, campaign select, and credits have their OWN separate input loops:
  `TitleScreenScript.GetDirectionalInput(...)`, `UI_CampaignSelect.UpdateInput()`
  (`UI_CampaignSelect.cs:184`, called from `GameMasterScript.cs:990`),
  `CreditRollScript.UpdateInput()` (`CreditRollScript.cs:71`). Covering those needs
  separate hooks.
- It is one long procedure of edge-triggered `GetButtonDown` checks. Postfixing tells you
  the frame was processed, not which action fired; for "what did the player do" use the
  action sink in section 6.

## 2. What UpdateInput does, top to bottom

`UpdateInput()` (`TDInputHandler.cs:250`-808) is a linear procedure with many early
`return`s. Order:

1. Hard pre-gates that abort the frame: mid-save; `!initialized`; control remapper open
   (`cMapper.isOpen`, `:267`); `DebugConsole.IsOpen` (`:278`); Steam popup shown (`:290`);
   and `CheckForConditionsThatHaltInput()` (`:304`, body `965`) which aborts on
   `disableAllInput`, animations pausing the next turn, camera animation, or lost window
   focus (`!gms.tdHasFocus`).
2. Captures the Confirm press once: `bool buttonDown = player.GetButtonDown("Confirm");`
   (`:295`), threaded through everything.
3. Computes direction once: `Directions directions = GetDirectionalInput();` (`:361`),
   with hold-to-repeat buffering (`:362`-405, see section 3).
4. Dispatch, each able to `return` and consume the frame:
   - Game-over input (`:406`); radial menu `Switch_RadialMenu.HandleInput` (`:415`);
     hotbar cycle (`:419`).
   - `if (HandleAbilityTargetingInput() || HandleExamineModeInput(...) || (HandleHotbarInput(...) && !flag)) return;` (`:425`)
   - `if (HandleInteractableWindowInput(directions, flag, buttonDown)) return;` (`:435`)
     — the menu dispatcher (body `995`), see `ui-framework.md`.
   - Log scroll via `"Scroll UI Boxes Vertical"` axis (`:439`).
   - Gameplay gate: `if (GameMasterScript.playerDied || !GameMasterScript.actualGameStarted || UIManagerScript.dialogBoxOpen) return;` (`:443`).
   - UI shortcuts, Hide UI, options open/close (`:452`-491).
   - Window-open gate: `if ((UIManagerScript.AnyInteractableWindowOpen() || UIManagerScript.GetWindowState(UITabs.CHARACTER)) && !uims.CheckHotbarNavigating()) return;` (`:494`).
   - Targeting / NPC / turn-executing gate: `if (gms.turnExecuting || GameMasterScript.IsNextTurnPausedByAnimations() || CheckForExamineModeInput() || CheckForTargetingInput(...) || CheckForNPCInteractionsOnConfirm(...)) return;` (`:519`).
   - Move-rate clamp: `if (Time.time - timeSinceLastActionInput <= gms.playerMoveSpeed - 0.01f) return;` (`:524`).
   - Mouse click handling (`:528`-604), then keyboard action shortcuts (healing flask,
     planks, shovel, mallet, town portal) (`:605`).
   - Movement to turn (`:617`-679, see section 3).
   - Other gameplay actions: `"Fire Ranged Weapon"` (`:680`), `"Wait Turn"` (`:706`),
     cycle/switch weapons (`:721`-752), and the hotbar-slot loop `"Use Hotbar Slot " + n`
     (`:755`-803).

Key gates to know (these decide menu vs world): `UIManagerScript.dialogBoxOpen`
(`UIManagerScript.cs:1274`), `UIManagerScript.AnyInteractableWindowOpen()`
(`UIManagerScript.cs:14631`), `gms.turnExecuting`, `actualGameStarted`.

## 3. Movement becomes a turn

Direction acquisition: `GetDirectionalInput()` (`TDInputHandler.cs:2466`) returns a
`Directions` enum value (8-way compass + `NEUTRAL`).

- Keyboard / D-pad: `CheckForDiscreteDirectionalInput(...)` (`:810`) reads the explicit
  diagonal actions `"Move Up+Left"` etc. (`:819`-834) then the orthogonals `"Move Left/
  Right/Up/Down"` (`:839`-842).
- Joystick: when the last active controller is a `Joystick` and no discrete direction was
  found, reads `new Vector2(player.GetAxis("Move Horizontal"), player.GetAxis("Move
  Vertical"))` (`:2483`), dead-zone `PlayerOptions.buttonDeadZone/100f`, quantized to 8
  directions by `Mathf.Round(angle/45f) % 8` (`:2496`).
- `"Diagonal Move Only"` (force-diagonal modifier) suppresses orthogonals while held.

Hold-to-repeat (`:362`-405): `framesSinceNeutral` resets on `NEUTRAL`; the first press
starts a buffer (`bufferingInput`, `inputBufferCount`) and movement is suppressed until
`inputBufferCount >= num` where `num` is `gms.movementInputDelayTime` (world) or
`gms.movementInputOptionsTime` (menu) or `0.15f` (targeting).

STEP_MOVE vs STANDARD (`PlayerOptions.joystickControlStyle`, enum `JoystickControlStyles`):
the move block (`:617`-679) builds a `vControllerInput` from the direction. For joystick
STEP_MOVE it gates on `HandleJoystickIndividualStepMovement(...)` (`:1510`): the stick aims,
a Confirm short/double-press commits exactly one tile (with auto-slash at an adjacent
enemy). STANDARD skips that gate, so a held stick auto-repeats moves via the buffer timer.
Keyboard always uses the discrete path plus the buffer timer.

The commit (`:662`-678): the direction yields a target tile `pos`; if walkable, the game
builds a `TurnData`, sets `TurnTypes.MOVE` and `newPosition`, records
`timeSinceLastActionInput = Time.time`, and calls `gms.TryNextTurn(turnData3, newTurn:
true)`.

## 4. The action sink: TryNextTurn

Every committed player action (MOVE, ATTACK, ABILITY, PASS, ...) funnels through

```
public void TryNextTurn(TurnData tData, bool newTurn, int iThreadIndex = 0)   // GameMasterScript.cs:1501
```

with a populated `TurnData` (`TurnTypes`, `newPosition`, `actorThatInitiatedTurn`, target
data). Called from at least `TDInputHandler.cs:548, 576, 675, 713, 1995, 2202, 2228, 2268,
2298, 2348, 2399, 2416`. This is the cleanest SEMANTIC chokepoint for "the player just did
X" (the per-frame `UpdateInput` postfix cannot tell you that). Read the body before
patching; the turn/energy/speed model lives here and in the actor scheduler, and because
the game is variable-speed (haste/slow), enemy turns are interleaved, not strictly
alternating.

## 5. Mouse

Two pathfinding helpers, both invoked at `TDInputHandler.cs:601`:
`HandleMouseInputForPathfinding()` (`:2236`, left-click A* click-to-move) and
`HandleMouseInputPostPathfinding()` (`:2165`, straight-line move). Click-to-attack /
click-to-target is inline at `:528`-591 (converts `Input.mousePosition` via
`Camera.main.ScreenToWorldPoint`, builds a `TurnTypes.ATTACK` turn if a hostile is in
range, else hands off to pathfinding).

Gating: the option flag is `PlayerOptions.disableMouseMovement` (checked in both
guards, `:2168`, `:2239`); mouse is only honored when `uims.IsMouseInGameWorld()` and no
window is open; `PlayerOptions.disableMouseOnKeyJoystick` hides the cursor when the last
input was not the mouse. Last input device is `ReInput.controllers.GetLastActiveControllerType()`
(`ControllerType.Mouse/Keyboard/Joystick`). The `InputMethod` enum exists but the live
gating uses `ControllerType` + the `PlayerOptions` booleans.

## 6. Mod hook points

Observing actions:
- Whole-frame: postfix `TDInputHandler.UpdateInput()` (`TDInputHandler.cs:250`). Runs
  every gameplay frame; good for polling state, not for "which action fired."
- Semantic player action (recommended): patch `GameMasterScript.TryNextTurn(TurnData,
  bool, int)` (`GameMasterScript.cs:1501`); inspect the `TurnData`.
- Per-button (granular, high frequency): postfix Rewired `Player.GetButtonDown(string)` /
  `GetButton` / `GetAxis` in `Rewired_Core`. Usually too noisy; prefer the semantic layer.

Injecting NEW mod controls:
- Do NOT add Rewired actions at runtime. `AddAction` exists only on design-time data
  (`Rewired.Data.UserData.AddAction`, `Rewired_Core/Rewired.Data/UserData.cs:3708`); there
  is no clean runtime player-action API, and mutating it collides with the control-remapper
  UI and the `controlNames` table.
- Instead read raw keys yourself: `UnityEngine.Input.GetKeyDown(KeyCode.X)` (or BepInEx
  config-bound keys) in a `MonoBehaviour.Update()` you own, or in a prefix on
  `UpdateInput()`. The game already mixes raw `UnityEngine.Input` with Rewired (e.g.
  `Input.GetKeyDown(KeyCode.Escape)` at `TDInputHandler.cs:269`), so this is consistent.
  Pick KeyCodes the default bindings do not use to avoid double-firing.

Suppressing / consuming input:
- Built-in freeze: `TDInputHandler.DisableInput()` (`:1648`) sets `disableAllInput = true`;
  `EnableInput()` (`:1653`) clears it; `IsInputDisabled()` (`:2426`). This is the intended
  "freeze the game's input" switch while a mod modal is active.
- Per-frame: a Harmony PREFIX on `UpdateInput()` returning `false` skips the game's entire
  input handling that frame (read your own keys first, then return false for exclusivity).
- Mouse-only: `IgnoreNextMouseAction()` (`:1643`), `DelayMouseInput(float)` (`:245`).
- You cannot easily "eat" one specific edge-triggered Rewired button after the fact; prefer
  an unbound key or the prefix-suppress approach.

Clean single-method chokepoints:
- `TDInputHandler.UpdateInput()` (`TDInputHandler.cs:250`) — observe or suppress the frame.
- `GameMasterScript.TryNextTurn(TurnData, bool, int)` (`GameMasterScript.cs:1501`) — player
  committed an action.
- `TDInputHandler.DisableInput()` / `EnableInput()` — built-in freeze switch.

## Open items to confirm before relying on them

- Full body and energy semantics of `TryNextTurn` (read before patching).
- WASD vs arrows is handled purely by Rewired binding the same action names to different
  keys; no runtime `KeyboardControlMaps` branch was found in the movement path. Re-check if
  layout-specific behavior ever matters.
