# New-game / character-creation menu

How Tangledeep's "New Game" flow (character creation) is built, and what a future
spoken overlay needs to read it. Citations are `File.cs:line` in the decompiled
game source OUTSIDE this repo at `../tangledeep-decompiled/Assembly-CSharp/`.
Surveyed 2026-06-16. Companion docs: `ui-framework.md`, `input-flow.md`.

This is research for the overlay that will sit above the generic focus fallback
(`OverlayId.CharCreation`); the overlay itself is not built yet. The takeaway:
character creation rides the legacy `UIObject` graph, so the focus-sync stopgap
already moves through it — but its controls are image-only, so the generic
fallback speaks nothing useful and a dedicated overlay is required.

## Class and lifecycle

`CharCreation : MonoBehaviour` (`CharCreation.cs:10`), singleton `CharCreation.singleton`
(`:36`). `creationActive` (`:38`) gates the whole flow; the overlay's handler should
return `Active` while it is true and `Inactive` otherwise. Reached from the title
screen's New Game path (the confirm button's `mySubmitFunction =
StartCharacterCreation4FromButton`, `:162`).

The flow is a sequence of screens, each of which rebuilds the active legacy UIObject
set via `UIManagerScript.ClearUIObjects()` + `AddUIObject(...)`:

1. Job selection — `BeginCharCreation_JobSelection()` (`:809`).
2. Feat selection — `StartCharacterCreation_FeatSelect()` (`:883`).
3. Game mods (difficulty + world seed) — `StartCharacterCreation_SelectGameMods()` (`:976`).
4. Name entry — `PrepareNameEntryPage()` (`:1112`), a state machine over
   `ENameEntryScreenState` (`ENameEntryScreenState.cs`): `deciding_on_name`,
   `name_confirmed_and_ready_to_go`, `game_loading_stop_updating`, `max`.

Because each screen swaps the UIObject set, the overlay should re-derive its controls
from the current screen on every build (which the framework already does — build is
per-tick). Detecting the current screen: the simplest signal is which controls are
active / which `CanvasGroup`s are interactable; confirm during implementation.

## Navigation goes through ChangeUIFocus (stopgap already reaches it)

Job buttons are wired into the legacy 8-direction neighbor graph
(`jobButtons[i].neighbors[...]`, `:345`+) and selection fires through the standard
path: `jobButtons[i].myOnSelectAction = HoverJobInfo` (`:316`, runs on focus/hover)
and `mySubmitFunction = SelectJob` (`:318`, confirm). Directional input →
`MoveCursorToNeighbor` → `ChangeUIFocus` → `myOnSelectAction`, so our
`ChangeUIFocus` postfix records each focused job button without extra hooks.

## The controls are not simply labeled

Reading the focused control's widget text (what the generic fallback does) yields
little here, which is the whole reason for a dedicated overlay:

- Job buttons (`jobButtons : UIManagerScript.UIObject[]`, `:16`) are image-only
  (`subObjectImage`, an `Animatable` walk sprite) — there is **no TMP text on the
  button**, so the generic fallback reads nothing and jobs are silent. The description
  lives in a separate label `jobDescText` (`:20`), which the game fills in as a side
  effect of `HoverJobInfo(int)` (`:729`) from
  `CharacterJobData.GetJobDataByEnum(jobEnumOrder[i]).GetFullJobReadout(extraText)`
  (`:768`). Locked jobs are masked by `SetMysteryJob()` (`:718`, gated on
  `SharedBank.CheckIfJobIsUnlocked`). Job order is indirected through `jobEnumOrder`
  (`:108`); `NUM_JOBS = 14` (`:110`).
  → The overlay's job-node `Label` should call `GetFullJobReadout` **itself** — it is
    pure synchronous string assembly (`CharacterJobData.cs:238`), available the instant
    focus lands. Do NOT read `jobDescText` instead: that couples us to the game's hover
    having already run (a render/order dependency we don't need). There is no tooltip
    render to wait for.

- Feats: `img_feats` / `label_feats` lists (`:59`, `:62`) — read the paired label.
- Difficulty / seed: `label_difficulty` (`:65`) and the `TMP_InputField worldSeedInput`
  (`:53`) with placeholder `worldSeedPlaceholder` (`:56`); `SetWorldSeed()` (`:268`).
- Name entry: `TMP_InputField NameInputTextBox` (`:50`), random-name helpers
  `GenerateRandomNameAndFillField` (`:1060`) / `HoverOverRandomName` (`:1074`),
  confirm `OnNameEntryBoxConfirm` (`:1274`).

Where a focused thing implements `ISelectableUIObject` (job data does not directly,
but items/abilities do), prefer the uniform `GetNameForUI()` +
`GetInformationForTooltip()` (see `ui-framework.md` §5).

## Implications for the overlay (next session)

- `OverlayId.CharCreation` overlay, registered above `GenericGameFocus`.
- Handler: `Active` while `CharCreation.creationActive`. The only not-ready condition
  is a data-load gate, NOT a render gate: `GetJobDataByEnum` returns null while
  `GameMasterScript.masterJobList` is null (a narrow early/transition window; this is
  why `HoverJobInfo` defers via `WaitAndTryHoverJobInfo`, `:742`). Because the tree
  rebuilds every tick, a job node whose `Label` no-ops when the list is null simply
  speaks correctly on the next navigation once data is present — mirroring the game's
  coroutine is unnecessary. Use `Sleeping` only if we want to suppress the whole overlay
  during that window.
- Control nodes: derive each job node's `ControlId` from its `jobButtons[i]`
  (`ControlId.ForObject(jobButtons[i])`) so the `ChangeUIFocus` stopgap tier-1 syncs
  our cursor; give the node a `Label` that calls the job readout. Same pattern for
  feats / difficulty / name controls per screen.
- Build the per-screen node set from the active screen; let the per-tick rebuild +
  focus reconciliation handle screen transitions.
