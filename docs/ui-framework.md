# Tangledeep UI / menu framework

How the game's menu and UI system works, for making it speak. Citations are
`File.cs:line` in the decompiled game source OUTSIDE this repo at
`../tangledeep-decompiled/Assembly-CSharp/`. The load-bearing chokepoints
(`ChangeUIFocus`, `uiObjectFocus`, `UITabs`, `ISelectableUIObject`) were verified
against the decompile on 2026-06-16; surfaces flagged "unconfirmed" below were
reported by source survey but not line-verified.

Companion docs: `input-flow.md` (how input reaches these menus), `controls.md`.

## Big picture

There are TWO parallel menu architectures sharing ONE global focus pointer:

- Legacy graph: `UIManagerScript.UIObject` nodes wired into an 8-direction neighbor
  graph. Used by dialogs, title screen, shop, hotbar, options, character creation,
  monster corral, casino.
- Newer `ImpactUI_Base` / `Switch_*` family: full-screen panels hosting scrolling
  button columns. Used by inventory, equipment, skills, character sheet.

Both ultimately set the same field `UIManagerScript.uiObjectFocus`
(`UIManagerScript.cs:2014`, `public static UIObject uiObjectFocus`) through
`ChangeUIFocus`. That shared pointer is why one focus hook can cover almost everything,
but the "selection changed" EVENT is emitted from two different places (section 3).

Input does NOT switch components when a menu opens: `TDInputHandler.UpdateInput()` keeps
running and internally routes directional/confirm/cancel to the open menu (see
`input-flow.md`).

## 1. Toolkit and text

Unity uGUI (`UnityEngine.UI`: Canvas/Image/Button/Slider) plus TextMeshPro labels. No
NGUI, no tk2d (74 files `using TMPro;`). On-screen text is `TMPro.TextMeshProUGUI`.

The element wrapper `UIManagerScript.UIObject` (`UIManagerScript.cs:49`) carries what you
need to describe a focused element:
- `gameObj` (`:51`), `subObjectTMPro` (TextMeshProUGUI, `:55`), `subObjectImage` (`:53`)
- `button` (`:59`, a `ButtonCombo`)
- `mySubmitFunction : Action<int>` (`:61`, confirm), `myOnSelectAction : Action<int>`
  (`:67`, fires on focus/hover), `myOnExitAction` (`:69`)
- `neighbors : UIObject[]` (`:57`, the 8-direction nav graph)

Localization: `StringManager.GetString(string refName, bool forceParseButtonAssignments =
false, bool backupLookup = false)` (`StringManager.cs:777`) resolves a key through
per-language dictionaries with rich-text/button-assignment parsing, falling back to English
then the raw key. Caution: many user-visible strings are assembled inline from a
`displayName` plus color tags rather than via `GetString`, so the mod should prefer reading
the resolved `.text` or the description builders (section 5) over re-deriving from keys.

## 2. UIManagerScript and the window system

Central singleton: `public static UIManagerScript singletonUIMS` (set in `Awake`,
`UIManagerScript.cs:3659`). ~19,400 lines; owns dialogs, info bars, the cursor, the
hotbar, targeting overlay, and the full-screen-UI host.

Window tabs, `UITabs` enum (`UITabs.cs`): `CHARACTER, EQUIPMENT, INVENTORY, SKILLS,
RUMORS, OPTIONS, SHOP, COOKING, CRAFTING, NONE, COUNT`. State is a bool array:
- `SetWindowState(UITabs, bool)` (`UIManagerScript.cs:3646`)
- `GetWindowState(UITabs)` (`:3652`)
- `OpenFullScreenUI(UITabs)` (`:3075`), `ToggleUITab(UITabs, bool)` (`:6033`),
  `GetCurrentUITab()` (`:5672`), `lastUITabOpened` (`:2151`).

"Is anything open" predicates:
- `AnyInteractableWindowOpen()` (`:14631`) = `AnyInteractableWindowOpenExceptDialog() ||
  dialogBoxOpen`.
- `AnyInteractableWindowOpenExceptDialog()` (`:14571`) ORs the EQUIPMENT/INVENTORY/SKILLS/
  OPTIONS/RUMORS/COOKING/CHARACTER tabs plus `jobSheetOpen`,
  `ShopUIScript.CheckShopInterfaceState()`, casino/corral/item-world flags,
  `CharCreation.creationActive`, and `currentFullScreenUI != null`.
- `dialogBoxOpen` (`:1274`); the active new-style panel is `currentFullScreenUI`
  (`ImpactUI_Base`, `:638`).

## 3. The selection / focus model (the key to everything)

One field holds focus: `uiObjectFocus` (`UIManagerScript.cs:2014`). The focus setter both
paths funnel through is:

```
public static void ChangeUIFocus(UIObject obj, bool processEvent = true)   // UIManagerScript.cs:6639
```

(plus the common wrapper `ChangeUIFocusAndAlignCursor(UIObject, float, float)` at `:6630`
which also moves the visual cursor). `ChangeUIFocus` sets `uiObjectFocus = obj` and, when
`processEvent`, notifies the title screen and the uGUI EventSystem.

Important nuance: `ChangeUIFocus` itself does NOT run the per-element `myOnSelectAction`.
That callback is fired by the NAVIGATION methods:
- Legacy path: `MoveCursorToNeighbor(int dir)` (`:507`) and `MoveCursorToUIObject`
  (`:6656`, mouse hover) end by calling `ChangeUIFocus` then
  `uiObjectFocus.myOnSelectAction(...)`. Directional input enters via `MoveCursor(Directions,
  ...)` (`:6675`).
- New column path: `ImpactUI_Base.OnColumnUpdateFocus(Switch_UIButtonColumn)`
  (`ImpactUI_Base.cs:591`) and `FocusAndBounceButton(Switch_InvItemButton)`
  (`ImpactUI_Base.cs:508`) call `ChangeUIFocus(btn.myUIObject, processEvent: false)` and
  `DisplayItemInfo(...)`. The column raises this via its `onCursorPositionInListUpdated`
  callback (`Switch_UIButtonColumn.cs:270`); the selected button is
  `column.GetSelectedButtonInList()` (`Switch_UIButtonColumn.cs:197`).

Selectable items expose a uniform describe-yourself interface
(`ISelectableUIObject.cs`): `Sprite GetSpriteForUI()`, `string GetNameForUI()`,
`string GetInformationForTooltip()`. Implemented by `Item`, `AbilityScript`, `JobAbility`,
`Equipment`. This is the universal "name + description of the focused thing."

## 4. Key surfaces

For each: the class, how items are represented, how selection is tracked.

- Title / main menu: `TitleScreenScript`. Options are legacy `UIObject`s; focus =
  `uiObjectFocus`; focus-change notify `OnChangedUIFocus(UIObject)`
  (`TitleScreenScript.cs:529`, nearly a no-op).
- Character creation: `CharCreation`. `jobButtons : UIObject[]` (`:16`),
  `indexOfSelectedJob`/`indexOfHoveringJob`; selection-change `HoverJobInfo(int)` (`:729`,
  writes `jobDescText.text`); confirm `SelectJob(int)` (`:582`). Name entry uses a
  `TMP_InputField NameInputTextBox` and a state enum.
- Inventory / consumables: `Switch_UIInventoryScreen : ImpactUI_WithItemColumn`. Items are
  `Switch_InvItemButton` in a `Switch_UIButtonColumn`; selection via
  `GetSelectedButtonInList()`; change fires `OnColumnUpdateFocus` -> `DisplayItemInfo`.
- Equipment: `Switch_UIEquipmentScreen`. Browse column plus `equippedGearButtons`; equipped
  hover `SetTooltipViaEquipmentButtonByID(...)`.
- Character sheet: `Switch_UICharacterSheet`. Info lines `CharacterSheetInfoPoint`
  (label/value); tab `ECharacterSheetTab` via `ActivateTab(...)`; per-line hover
  `OnHoverCharacterSheetInfoLine(int)` -> `SetTooltipForCharacterSheetOption(...)`.
- Skills / abilities: `Switch_UISkillSheet`. Two columns (active/passive); abilities are
  `AbilityScript` / `JobAbility` in `Switch_InvItemButton`; hover -> `SetTooltipViaButtonByID`
  -> `DisplayItemInfo`.
- Hotbar: `HotbarBindable` holds an ability or consumable; active index
  `UIManagerScript.indexOfActiveHotbar` (`:745`); per-slot focus glow compares
  `uiObjectFocus`. Hotbar focus still flows through `ChangeUIFocus`.
- Dialog / confirmation / NPC: `DialogBoxScript` (text `txtDialogBoxMessage`). Text written
  via `DialogBoxWrite(string)` (`UIManagerScript.cs:13488`) ->
  `BeginTypewriterText(string, TextMeshProUGUI)` (`:7565`); choices are `ButtonCombo`s
  rendered as legacy `UIObject`s in `dialogUIObjects`, navigated by the legacy path.
- Shop: `ShopUIScript`. `shopItemButtonList : UIObject[]`; per-button
  `myOnSelectAction = ShowItemInfo(int)` (`:309`).
- Level-up / stat / job allocation: appears to run through the dialog/conversation system
  (`DialogBoxWrite` + `ButtonCombo`), with job change reusing `CharCreation.SelectJob`.
  Unconfirmed; verify when implementing in case there is a special prompt.
- Ranged targeting: `PlayerInputTargetingManager` (static). Cursor tile
  `UIManagerScript.virtualCursorPosition`; the loop `UpdateCursorTargetingTiles()`
  (`UIManagerScript.cs:19072`) calls
  `PlayerInputTargetingManager.UpdateCurrentTargetingInformation(Vector2, bool isGoodTile)`
  (`PlayerInputTargetingManager.cs:96`) on every cursor move. Payload `TargetData`:
  `targetTiles : List<Vector2>`, `targetActors : List<Actor>`, `clickedPosition`,
  `whichAbility`. Resolve the actor on a tile via `MapMasterScript.GetTile(location)`.

## 5. Description / tooltip builders to reuse for speech

- Monster: `HoverInfoScript.BuildHoverTextFromMonster(Monster)` (`HoverInfoScript.cs:149`);
  tile dispatcher `GetHoverText(MapTileData, bool)` (`:300`).
- Item / equipment: `Item.GetInformationForTooltip()` (`Item.cs:169`) ->
  `GetItemInformationNoName(bool)` (`Item.cs:2186`); name `GetNameForUI()` (`Item.cs:164`).
- Ability: `AbilityScript.GetInformationForTooltip()` (`:226`) -> `GetAbilityInformation()`
  (`:509`); name `GetNameForUI()` (`:212`).
- Dispatch into the panel: `DisplayItemInfo(Item, GameObject, bool)`
  (`UIManagerScript.cs:10563`); info-bar sink `SetInfoText(string)` (`:5498`).
- Because every selectable implements `ISelectableUIObject`, the universal call is
  `obj.GetNameForUI()` + `obj.GetInformationForTooltip()`.

## 6. Recommended speech hook plan

- A. Universal focus announcer: postfix `ChangeUIFocus(UIObject, bool)`
  (`UIManagerScript.cs:6639`). Covers title, dialog choices, shop, hotbar, options, char
  creation, and the inventory/equipment/skills/char-sheet columns. Read `obj.subObjectTMPro
  .text`, or if the object maps to an `ISelectableUIObject`, `GetNameForUI()`. It fires
  frequently (including programmatic re-focus on open), so dedupe consecutive identical
  objects. Postfix runs regardless of the `processEvent` flag, so the column path
  (`processEvent: false`) is still covered.
- B. Rich descriptions for columns: postfix `ImpactUI_Base.OnColumnUpdateFocus(...)`
  (`ImpactUI_Base.cs:591`) or `FocusAndBounceButton(...)` (`:508`) to speak the full
  tooltip (`column.GetSelectedButtonInList().GetContainedData().GetInformationForTooltip()`).
- C. Dialog / any typewriter text: postfix `BeginTypewriterText(string, TextMeshProUGUI)`
  (`UIManagerScript.cs:7565`); plus `DialogBoxWrite` (`:13488`) to enumerate choices. Covers
  NPC dialogue and (apparently) level-up prompts.
- D. Window context: postfix `SetWindowState(UITabs, bool)` (`:3646`) and
  `OpenFullScreenUI(UITabs)` (`:3075`) to announce "Inventory opened/closed."
- E. Targeting: postfix `PlayerInputTargetingManager.UpdateCurrentTargetingInformation
  (Vector2, bool)` (`PlayerInputTargetingManager.cs:96`); speak the tile/actor, use
  `isGoodTile` for valid/invalid.
- F. Radial / ring menu: WEAK spot. `Switch_RadialMenuButton.OnSelect()` is confirm, not
  hover, and there is no persistent "hovered slice" field; announcing the highlighted slice
  as the stick moves likely needs patching the highlight computation inside
  `Switch_RadialMenu.HandleInput_Internal` (`:194`). Flag for hands-on work.

Biggest single win is hook A (`ChangeUIFocus`): one patch covers most menu focus. Layer B
for descriptions, C for text/dialogue, D for window context, E for targeting, and treat F
(radial) as a special case. Main risks: A's call frequency (dedupe), and the radial menu's
missing hover state.
