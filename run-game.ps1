# run-game.ps1 - Launch Tangledeep for iteration and BLOCK until it exits.
#
# Run this as a background task: the blocking wait means the task completes the
# instant the game crashes or quits, which is the wake-up signal. There is no
# separate restart verb - relaunching kills any leftover instance first, so
# "restart" is just "cancel the background task and run this again".

param(
    [switch]$Help
)

if ($Help) {
    Write-Host "Usage: .\run-game.ps1 [-Help]"
    Write-Host "  Launches Tangledeep and blocks until it exits. Run as a background task."
    exit 0
}

$ErrorActionPreference = "Stop"

# --- Locate the game install (same resolution as build.ps1 / setup-bepinex.ps1) ---
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
$Exe = "$Game\Tangledeep.exe"
if (-not (Test-Path $Exe)) {
    Write-Host "ERROR: Tangledeep not found at: $Game" -ForegroundColor Red
    Write-Host "Set the TANGLEDEEP_GAME environment variable to the game folder." -ForegroundColor Red
    exit 1
}

# --- Self-cleaning restart: kill any leftover instance (e.g. orphaned by a
#     cancelled background task) so this launch always starts fresh. ---
Get-Process -Name Tangledeep -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping existing Tangledeep (PID $($_.Id))..." -ForegroundColor Yellow
    $_ | Stop-Process -Force
}

# Enable the in-process dev server (eval + speech tap) for this launch. Inherited
# by the child process; absent in a normal play launch so no socket is opened.
$env:TANGLEDEEP_DEV = "1"

$proc = Start-Process -FilePath $Exe -WorkingDirectory $Game -PassThru
Write-Host "Launched Tangledeep (PID $($proc.Id)), dev server on http://127.0.0.1:8770. Blocking until it exits..." -ForegroundColor Cyan

try {
    $proc.WaitForExit()
} finally {
    # Graceful stop of this launcher: take the game down with us. (A hard kill of the
    # launcher skips this; the next launch's kill-existing step covers that case.)
    if (-not $proc.HasExited) {
        Write-Host "Launcher stopping; killing game (PID $($proc.Id))..." -ForegroundColor Yellow
        $proc.Kill()
    }
}

Write-Host "Tangledeep exited with code $($proc.ExitCode)." -ForegroundColor Cyan
exit $proc.ExitCode
