# setup-bepinex.ps1 - Install the vendored BepInEx (third_party/bepinex) into the
# Tangledeep folder. Idempotent: safe to re-run (e.g. after a game update wipes it).
# Unity 2020.3 Mono needs no entrypoint tweak, so the upstream default config is used
# as-is. After this, run build.ps1 to deploy the mod.

$ErrorActionPreference = "Stop"

# --- Locate the game install ---
# TANGLEDEEP_GAME env var wins; otherwise auto-detect from Steam library folders;
# otherwise fall back to the default location.
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

$Vendor = "$PSScriptRoot\third_party\bepinex"
if (-not (Test-Path "$Vendor\winhttp.dll")) {
    Write-Host "ERROR: vendored BepInEx not found at $Vendor" -ForegroundColor Red
    exit 1
}

Write-Host "Installing BepInEx into $Game ..." -ForegroundColor Cyan

# --- Copy the loader proxy + doorstop config + core ---
foreach ($f in @("winhttp.dll", "doorstop_config.ini", "changelog.txt", ".doorstop_version")) {
    if (Test-Path "$Vendor\$f") { Copy-Item "$Vendor\$f" "$Game\$f" -Force }
}
$CoreDest = "$Game\BepInEx\core"
New-Item -ItemType Directory -Path $CoreDest -Force | Out-Null
Copy-Item "$Vendor\BepInEx\core\*" $CoreDest -Force
New-Item -ItemType Directory -Path "$Game\BepInEx\plugins" -Force | Out-Null

Write-Host ""
Write-Host "BepInEx installed. Now run build.ps1 to deploy the mod." -ForegroundColor Cyan
Write-Host "(First game launch after install generates BepInEx\config\BepInEx.cfg and LogOutput.log.)" -ForegroundColor DarkGray
