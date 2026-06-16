# Tangledeep Access

A screen-reader accessibility mod for **Tangledeep** (Impact Gameworks), aiming
to make the turn-based roguelike fully playable without vision. Speech is the
primary interface, via [Prism](https://github.com/ethindp/prism) (a unified
screen-reader/TTS abstraction) through a hand-written P/Invoke binding.

Status: **bootstrap / hello-world.** The mod loads under BepInEx, brings up Prism,
and speaks a startup line. Accessibility features are not built yet.

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
# One-time: restore the pinned local tools (CSharpier).
dotnet tool restore

# One-time: install BepInEx into the game folder.
.\setup-bepinex.ps1

# Build the plugin and deploy it + the Prism runtime into BepInEx\plugins.
.\build.ps1

# Run the offline tests.
.\test.ps1

# Format all C# (and csproj/props) before committing.
dotnet csharpier format .
```

All three scripts auto-locate the Steam install of Tangledeep; override with the
`TANGLEDEEP_GAME` environment variable.

Then launch Tangledeep. With a screen reader running you should hear
"Tangledeep Access \<version\> loaded. Hello world." within a couple seconds.

## Logs

- BepInEx: `<game>\BepInEx\LogOutput.log` (mod lines via the BepInEx logger).
- Unity player log: `%USERPROFILE%\AppData\LocalLow\ImpactGameworks\Tangledeep\Player.log`.
