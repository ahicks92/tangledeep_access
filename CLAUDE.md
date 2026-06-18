# TangledeepAccess - Claude Code Instructions

TangledeepAccess makes **Tangledeep** (Impact Gameworks) playable by blind users.
Speech is the primary interface; there is no visual fallback. If something fails
silently, speaks stale data, or omits information, the player has no way to know.
A logged failure is actionable; a silent one is invisible.

This project is greenfield and new. That means that committing to `main` is acceptable.

## Game & environment (verified by decompile + binary inspection)

- Engine: **Unity 2020.3.37f1, Mono, x64.** Full .NET 4.x BCL (mscorlib present,
  netstandard absent), so the plugin targets **net472**. The game is turn-based,
  8-direction tile movement, top-down camera following the hero. No real-time pressure.
- Loader: **BepInEx 5.4.23.5 (x64)** + HarmonyX (`0Harmony.dll`). Vendored in
  `third_party/bepinex/`; `setup-bepinex.ps1` installs it. Unlike Unity 5.x games,
  **no entrypoint tweak is needed** — the upstream default config works.
- Game code: `<game>\Tangledeep_Data\Managed\Assembly-CSharp.dll` (+ `-firstpass`).
  Input is **Rewired** (`Rewired_Core.dll`), text is **TextMeshPro**.
- Decompiled game source for reference: `../tangledeep-decompiled/` (OUTSIDE the repo,
  never committed). Browse `Assembly-CSharp/` there. **Look up any game
  type/method/field signature there before guessing.**
- Logs: BepInEx writes `<game>\BepInEx\LogOutput.log`; Unity writes
  `%USERPROFILE%\AppData\LocalLow\ImpactGameworks\Tangledeep\Player.log`. Mod lines go
  through the BepInEx logger.

## Speech: Prism (not Tolk directly)

- Speech backend is **Prism** (https://github.com/ethindp/prism), a unified
  screen-reader/TTS abstraction (and itself a Tolk replacement). v0.16.6 vendored at
  `third_party/prism/` (`prism.dll` + `prism.h` + license). `prism.dll` is
  self-contained: its screen-reader clients including the NVDA controller are
  statically linked (verified against its import table), so it has no `tolk.dll` or
  `nvdaControllerClient.dll` dependency. On this machine Prism selects **NVDA**.
- The P/Invoke binding is hand-written in Core (`Speech/PrismNative.cs`) from the
  vendored header — existing third-party C# Prism bindings are often incomplete/buggy.
  ABI invariants, **audit `prism.h` after any Prism upgrade**:
  - Calling convention **`__cdecl`** (`CallingConvention.Cdecl`) on every import.
  - All boundary strings are **UTF-8**. net472 lacks `LPUTF8Str` /
    `Marshal.PtrToStringUTF8`, so strings are marshaled by hand: inbound as
    NUL-terminated `byte[]` (`PrismNative.ToUtf8`), outbound `const char*` via
    `PrismNative.FromUtf8`. Never switch these to `LPStr`/`LPWStr`.
  - C `bool` is one byte → `UnmanagedType.I1`. `size_t` → `UIntPtr`. Handles → `IntPtr`.
  - `PrismContext*`/`PrismBackend*` are opaque. `create_best` returns an **owned**
    backend (free with `prism_backend_free`); `acquire_best` is non-owning (do not free).
  - Use `prism_backend_output` (screen-reader path: speech + braille) over
    `prism_backend_speak` (TTS only).
- Native loading: BepInEx loads the managed plugin from `BepInEx\plugins\...`, which
  is not on the OS DLL search path. `NativeLoader` `LoadLibrary`s `prism.dll` by full
  path before any P/Invoke, so the by-name imports bind to the already-loaded module.
  `build.ps1` co-locates `prism.dll` with the plugin.

## Build, deploy, test

- `setup-bepinex.ps1` — install vendored BepInEx into the game (once per install /
  after a game update wipes it).
- `build.ps1` — build the plugin and deploy the single managed `TangledeepAccess.dll`
  plus the native `prism.dll` into `<game>\BepInEx\plugins\TangledeepAccess\`.
- `test.ps1` — offline xUnit suite (`dotnet test`), no game/Unity.
- `run-game.ps1` — launch the game for iteration and **block until it exits** (run it as a
  background task: a crash/quit then wakes you with the exit code). Sets `TANGLEDEEP_DEV=1`
  to enable the dev driver (below). Relaunch to restart (it kills any leftover instance
  first). **Kill the game before `build.ps1`** — a running game locks the deployed plugin DLL.
  **Prism/NVDA is OFF by default** (`TANGLEDEEP_NO_SPEECH=1`) so headless/overnight runs don't
  depend on a screen reader; spoken text is still captured for `/speech`. Pass `-Speech` to
  voice through NVDA. **`-SaveSlot N`** takes you from cold launch to in-game in one command:
  once the dev server answers it calls the `/loadsave` endpoint for slot N (retrying until the
  title's UI is ready), so you skip the new-game flow entirely (e.g. `run-game.ps1 -SaveSlot 0`).
  - **Restarting the game (do this right — it bit us):** to restart, **properly cancel the
    running `run-game.ps1` background task** (its `finally` kills the game and releases the
    lock), confirm teardown, *then* relaunch. **Never start a second launcher while the first
    background task is still alive.** Two launchers race the dev-server port (8770): the new
    game can't bind it and exits with code 1, while the second launcher externally kills the
    first's game so that task reports a spurious failure (exit -1/255). To enforce this,
    `run-game.ps1` takes a single-instance lock (`%TEMP%\tangledeep-run-game.lock`, holds the
    launcher PID) and **refuses** to start if a live launcher holds it; a stale lock (dead
    holder, e.g. a hard-killed launcher) is cleared automatically. The launcher also kills any
    orphaned game and **waits for process exit + port 8770 to free** (≤15s) before starting —
    `Stop-Process` returns before teardown, so a naive kill-then-start races the socket.
- All scripts auto-locate the Steam install; override with `TANGLEDEEP_GAME`.
- `<Version>` lives in `Directory.Build.props` (single source of truth; the plugin's
  `BepInPlugin` literal is generated from it). `LangVersion` 7.3 (safe for Unity Mono).
- **Build output** for every project goes to a single repo-root `artifacts/` folder
  (`UseArtifactsOutput` in `Directory.Build.props`), NOT per-project `bin`/`obj`. The
  plugin DLL lands at `artifacts/bin/TangledeepAccess/release/TangledeepAccess.dll`.
- **Formatting:** Roslyn's `dotnet format` (ships with the SDK), driven by
  `.editorconfig`. Style is **one-true-brace (1TBS)**: every opening brace cuddles onto
  its construct's line, `else`/`catch`/`finally` cuddle onto the closing brace, and
  single-statement blocks always get braces (IDE0011). 4-space indent. CSharpier was
  dropped — it is Allman-only and cannot emit 1TBS. Note `dotnet format` does NOT reflow
  long lines to a column width the way CSharpier did.
  - Format the whole tree: `dotnet format TangledeepAccess.sln`. This is the normal
    command and is safe — verified idempotent on the converted tree, and adding new
    braceless code then re-formatting solution-wide applies braces cleanly.
  - Rare escape hatch: the `Core/**` sources are `<Compile Include>`-linked into both
    the net472 plugin and the net8 test project. `dotnet format` formats a linked file
    once per project and, *if the two project contexts ever compute different output for
    it*, writes git conflict markers (`>>>>>>> After`) into the file instead of
    overwriting. This bit once during the initial cold Allman→1TBS bulk conversion and
    was not reproducible afterward. If you ever see those markers after a format,
    `git checkout` the `.cs` files and format **per project** instead:
    `dotnet format TangledeepAccess/TangledeepAccess.csproj` (covers all plugin + Core
    files once), then
    `dotnet format TangledeepAccess.Tests/TangledeepAccess.Tests.csproj --include TangledeepAccess.Tests/`
    (test-own files only, leaving the already-formatted linked Core files untouched).

## Dev driver (in-process HTTP server) — for iteration, not a player feature

A dev-only HTTP server is **baked into the mod** (`TangledeepAccess/Dev/`), gated behind
`TANGLEDEEP_DEV=1` (set by `run-game.ps1`); inert in a normal launch. It lets an agent
introspect and drive the live game over `http://127.0.0.1:8770`. **It is intentionally
part of the mod assembly — do NOT isolate it into a separate package/plugin; that is
settled and out of scope. Don't raise it.** Its dep `Mono.CSharp.dll` (NuGet 4.0.0.143)
is deployed beside the plugin by `build.ps1`. Speech is tapped at the `PrismSpeech.Speak`
chokepoint via `PrismSpeech.Observer` (Core).

Bring-up: launch via `run-game.ps1` (background task), then poll
`curl -s --retry 60 --retry-connrefused --retry-delay 1 http://127.0.0.1:8770/health`.
The server starts in `Awake`, answers within ~2s, and works even at the title screen. The
game keeps simulating while unfocused (`Application.runInBackground` is true and the dev
server re-forces it), so you can drive it while your terminal has focus — proven: eval
runs with `isFocused=False`.

Endpoints (loopback; drive with `curl`):
- `POST /eval` — body is C# source, compiled by Mono.CSharp and run on the Unity main
  thread against the live game. REPL **state persists across calls**. Returns captured
  `Console` output, compile diagnostics, exceptions (caught — eval errors never crash the
  game), and the trailing expression value (`=> ...`). Two gotchas: (1) use
  **fully-qualified type names** — a `using X;` followed by statements in one body trips
  Mono.CSharp; (2) eval'd code is its own dynamic assembly, so it sees only **public**
  members of the mod/game — reach `internal`/`private` via reflection
  (`typeof(TangledeepAccess.Plugin).Assembly.GetType("...")` then `GetField(..., NonPublic)`).
- `GET /speech?since=N` — strings the mod has spoken, with a monotonic cursor; poll
  incrementally. This is how you observe the TTS you can't hear. The tap is upstream of the
  Prism backend, so it works even with speech disabled (the overnight default).
- `GET /gui/game` — **raw** structural dump of the active UI hierarchy (full paths, every
  component type, raw widget text) + key `UIManagerScript` state. Deliberately NOT the
  mod's cleaned-label view: it surfaces structure the cleaned view hides (e.g. the
  SaveSlot screen's data lives in `SaveDataDisplayBlock`s, not any focus label), so you
  can reverse-engineer a screen, then `eval` into what it reveals. Caveat: lists
  GameObject-*active* objects, including ones hidden via `CanvasGroup` alpha — use
  `/screenshot` for the visibility truth.
- `GET /gui/mod` — **interpreted** view: the active overlay's graph (node labels, current
  cursor `>`, directional links) via `OverlayDispatcher.Describe`. Diff against `/gui/game`
  to find where the mod is losing information.
- `GET /screenshot` — captures the game framebuffer to a PNG and returns its path; `Read`
  that path to view it. Captures the game render (not the desktop) and works unfocused.
- `POST /input` — body is a verb. Drives the game via its **own** handlers, **not** OS
  synthetic keys (which can't reach an unfocused Rewired game). Injecting trips the focus
  hook / game log, so results get spoken — read them back via `/speech`. Verbs:
  - **Menu** (the `uiObjectFocus` model — title, dialogs, shops): `up|down|left|right`
    walk the focused `UIObject.neighbors` compass (orthogonals at slots 0/2/4/6) via
    `ChangeUIFocusAndAlignCursor`; `confirm` calls `CursorConfirm()`.
  - **In-game hero actions** (gameplay): `step <dir>` where dir is `n|s|e|w|ne|nw|se|sw`
    commits a one-tile `TurnData(MOVE)` through `GameMasterScript.TryNextTurn` — the game
    resolves it as move / attack / NPC interaction, exactly like a keyboard step (so
    stepping into a shopkeeper opens the shop, into a monster attacks). `wait` passes the
    turn (`TurnTypes.PASS`); `stairs` calls `TravelManager.TryTravelStairs()`; `pickup`
    calls `TileInteractions.TryPickupItemsInHeroTile()`. This is how you drive gameplay
    (combat, shops, descent) over HTTP for testing.
  - Save-slot selection has no menu verb — to skip straight into a save use `/loadsave`
    (below), **not** `OnSelectSlotConfirmPressed`, which no-ops if called cold (it needs the
    CONTINUE slot window already set up).
- `POST /loadsave` — body is a save slot index (default `0`). From the title screen, loads
  that slot and **blocks until the gameplay scene is interactive**, then returns
  `loaded slot N: hero=… map=… focus=…`. This is the fast path to a real in-game state for
  iterating on gameplay/GUI without walking the new-game flow. It drives the game's own load
  path (set slot, `GameStartData.newGame=false`, stash the `LOADGAME` response that the
  gameplay-scene init reads, run `UIManagerScript.FadeOutThenLoadGame`), then polls
  `GameMasterScript.gameLoadSequenceCompleted` (the load coroutine's final line; forced false
  first so a second in-session load can't see a stale true). **Focus fix baked in:** the load
  force-sets `GameMasterScript.tdHasFocus=true` regardless of real OS focus, and
  `TDInputHandler` gates physical-key processing on that flag — so a background load would make
  the game eat stray keystrokes. On completion the endpoint sets `tdHasFocus` to whether our
  process actually owns the OS foreground window (a Win32 `GetForegroundWindow` check —
  `Application.isFocused` is unreliable: it inits true and only flips on a focus-*loss* event,
  so a never-focused background launch reports true forever). Injected `/input` verbs are
  unaffected (they bypass the input gate via `TryNextTurn`).

Iteration loop: edit → **kill the game** (cancel the `run-game.ps1` background task) →
`build.ps1` → `run-game.ps1 -SaveSlot 0` (background; lands in-game on its own) → poll
`/health` → `curl`. (Drop `-SaveSlot` to stop at the title; load later with `POST /loadsave`.)

## Architecture

The mod ships as **one managed assembly**. Two projects:
- **`TangledeepAccess`** — the BepInEx plugin (net472), the only product assembly.
  Engine/native glue (`Plugin`, `LogBepInExBackend`) at the root; engine-agnostic code
  (Prism binding + speech wrapper, native loader, `Log` seam) under **`Core/`**, which
  compiles straight in. References game/BepInEx assemblies.
- **`TangledeepAccess.Tests`** — net8 + xUnit. References no product DLL: it **links the
  plugin's `Core/**` sources directly** (`<Compile Include>`) and tests them off-engine.

**The `Core/` rule:** anything under `TangledeepAccess/Core/` must compile with only the
BCL — no Unity, no BepInEx, no Harmony. The test build enforces this (those files are
compiled on net8). Engine-touching code lives outside `Core/`. This is what keeps the
single shipped DLL unit-testable without a second assembly. Internal types under `Core/`
(e.g. `PrismNative`) are visible to tests because the sources compile into the test
assembly — no `InternalsVisibleTo` needed. Put testable logic under `Core/`.

**Plugin lifecycle.** `Awake` does only non-Unity setup (logging, native preload,
Prism init — all pure native, no Unity state). The spoken startup line is deferred to
`Update` after a short frame countdown. `Update` is where per-frame announcement
pumping will live. Never speak from a Harmony hook — hooks set state/flags; speak once
per frame from the pump.

## Conventions (carried from hand-of-fate-access; apply as the mod grows)

- **No silent failures.** Every catch in a Harmony patch / reflection path logs via
  `Log.Warn`/`Log.Error`. A swallowed exception silently kills a feature the blind
  player can't see fail.
- **Never cache game state.** Re-query the game at speak time; stale speech is worse
  than none. The only acceptable "cache" is a live reference to a game object whose
  properties you read on demand.
- **Reuse the game's own strings.** Tangledeep centralizes text in `StringManager`
  (`GetString(refName)`) and has pre-built description builders
  (`BuildHoverTextFromMonster`, `GetInformationForTooltip`, ...). Speak those; don't
  hardcode. Mod-authored strings go in one central place (for future translation), not
  inline literals.
- **One MessageBuilder per spoken message — never nest.** Overlay control callbacks
  receive the framework's builder as `ctx.Message`; append directly to it
  (`ctx.Message.Fragment(...)`, `.ListItem(...)`). A helper that assembles several pieces
  takes that same `MessageBuilder` as a parameter and appends to it. Do NOT `new
  MessageBuilder()` inside a callback, `.Build()` it, and re-inject the string as a single
  `Fragment` — that flattens the fragment/list-item separation discipline into one opaque
  fragment and duplicates the builder lifecycle. The only code that constructs a builder
  and calls `.Build()` is the dispatcher that owns the message (`OverlayDispatcher`); the
  only other `new MessageBuilder()` is in tests.
- **High-value hook target:** `GameLogScript` (`EnqueueEndOfTurnLogMessage`) is the
  centralized turn-by-turn event log — nearly every gameplay event flows through it as
  text. Piping it to speech is the biggest early win.
- The two genuinely visual problems to design around: **ranged ability targeting**
  (`PlayerInputTargetingManager`, `TargetingLineScript`) and **spatial awareness** of
  the surrounding grid (`MapMasterScript`, `MapTileData`, `FogOfWarScript`). Both are
  solvable — the underlying data is all queryable in code.
- **Combat is variable-speed** (a `SPEED` stat, haste/slow), not strict you-then-them.
  Announce each enemy action as it happens; don't assume lockstep turns.
- Don't over-null-check — let it crash where null isn't expected (a crash is visible).
  Comments describe current state, not change history.
