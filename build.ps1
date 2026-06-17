# build.ps1 - Build TangledeepAccess and deploy the plugin DLL, its Core
# dependency, and the self-contained Prism native runtime (prism.dll) into the
# game's BepInEx\plugins folder.

param(
    [switch]$NoBuild,
    [switch]$Help
)

if ($Help) {
    Write-Host "Usage: .\build.ps1 [-NoBuild] [-Help]"
    Write-Host "  -NoBuild  Skip building; just redeploy the last built DLL and native deps"
    Write-Host "  -Help     Show this help"
    exit 0
}

$ErrorActionPreference = "Stop"

# --- Locate the game install (same resolution as setup-bepinex.ps1) ---
$Game = $env:TANGLEDEEP_GAME
if (-not $Game) {
    $RegSteam = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -Name InstallPath -ErrorAction SilentlyContinue).InstallPath
    $DefaultSteam = if ($RegSteam) { $RegSteam } else { "C:\Program Files (x86)\Steam" }
    $SteamPaths = @()
    if (Test-Path "$DefaultSteam\steamapps") { $SteamPaths += $DefaultSteam }
    $LibFolders = "$DefaultSteam\steamapps\libraryfolders.vdf"
    if (Test-Path $LibFolders) {
        $content = Get-Content $LibFolders -Raw
        [regex]::Matches($content, '"path"\s+"([^"]+)"') | ForEach-Object {
            $p = $_.Groups[1].Value -replace '\\\\', '\'
            if ($p -ne $DefaultSteam -and (Test-Path "$p\steamapps")) { $SteamPaths += $p }
        }
    }
    foreach ($steam in $SteamPaths) {
        $candidate = "$steam\steamapps\common\Tangledeep"
        if (Test-Path "$candidate\Tangledeep.exe") { $Game = $candidate; break }
    }
    if (-not $Game) { $Game = "C:\Program Files (x86)\Steam\steamapps\common\Tangledeep" }
}
if (-not (Test-Path "$Game\Tangledeep.exe")) {
    Write-Host "ERROR: Tangledeep not found at: $Game" -ForegroundColor Red
    Write-Host "Set the TANGLEDEEP_GAME environment variable to the game folder." -ForegroundColor Red
    exit 1
}
$env:TANGLEDEEP_GAME = $Game

if (-not (Test-Path "$Game\BepInEx\core\BepInEx.dll")) {
    Write-Host "ERROR: BepInEx is not installed at $Game\BepInEx." -ForegroundColor Red
    Write-Host "Run setup-bepinex.ps1 first." -ForegroundColor Red
    exit 1
}

$PluginsDir  = "$Game\BepInEx\plugins"
$ProjectDir  = "$PSScriptRoot\TangledeepAccess"
# UseArtifactsOutput (Directory.Build.props) routes output here:
# artifacts\bin\<project>\<config-lowercased>\. Core.dll is copied in alongside.
$BuildDir    = "$PSScriptRoot\artifacts\bin\TangledeepAccess\release"
$BuildOutput = "$BuildDir\TangledeepAccess.dll"

# --- Build ---
if (-not $NoBuild) {
    Write-Host "Building TangledeepAccess (game: $Game)..." -ForegroundColor Cyan
    dotnet build "$ProjectDir\TangledeepAccess.csproj" -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build FAILED." -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path $BuildOutput)) {
    Write-Host "ERROR: DLL not found at $BuildOutput" -ForegroundColor Red
    exit 1
}

# --- Deploy into a dedicated subfolder so the mod's files stay grouped ---
$Dest = "$PluginsDir\TangledeepAccess"
New-Item -ItemType Directory -Path $Dest -Force | Out-Null

# The mod is a single managed assembly (Core sources compile straight in).
Copy-Item "$BuildDir\TangledeepAccess.dll" "$Dest\TangledeepAccess.dll" -Force
Write-Host "Deployed TangledeepAccess.dll to $Dest" -ForegroundColor Green

# Prism native runtime, co-located with the plugin so NativeLoader can preload it
# by full path. prism.dll is self-contained (screen-reader clients statically
# linked), so it is the only native file we ship.
Copy-Item "$PSScriptRoot\third_party\prism\prism.dll" "$Dest\prism.dll" -Force
Write-Host "Deployed Prism native runtime to $Dest" -ForegroundColor Green

# Mono.CSharp backs the dev eval server (Dev/). It only does anything when the mod
# is launched with TANGLEDEEP_DEV=1 (see run-game.ps1), but the assembly must be
# present for the plugin to load, so deploy it alongside.
$MonoCSharp = "$BuildDir\Mono.CSharp.dll"
if (Test-Path $MonoCSharp) {
    Copy-Item $MonoCSharp "$Dest\Mono.CSharp.dll" -Force
    Write-Host "Deployed Mono.CSharp.dll (dev eval) to $Dest" -ForegroundColor Green
} else {
    Write-Host "WARNING: Mono.CSharp.dll not found at $MonoCSharp (dev eval will fail to load)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done. Launch Tangledeep and listen for the startup line." -ForegroundColor Cyan
