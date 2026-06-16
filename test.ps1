# test.ps1 - Run the offline unit tests (Core only; no Unity, no BepInEx, no game).
$ErrorActionPreference = "Stop"
dotnet test "$PSScriptRoot\TangledeepAccess.Tests\TangledeepAccess.Tests.csproj" -c Release
