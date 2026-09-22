<#
.SYNOPSIS
    Builds the plugin and copies it into the game's BepInEx\plugins folder.

.DESCRIPTION
    Finds the game automatically (every Steam library on this PC, then this repo's parent folder).
    Override with -GameDir, the NGMP_GAME_DIR environment variable, or a game-path.txt file next to
    this script containing the path to the folder that holds Normal Golf Game.exe.
    Close the game before building: it locks the plugin DLL.
#>
param(
    [string]$Configuration = "Release",
    [string]$GameDir = ""
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "find-game.ps1")

$resolved = Get-GameDirOrExit $GameDir $PSScriptRoot -RequireBepInEx
Write-Host "Game: $resolved"

# Prefer a user-local .NET SDK if one is installed, otherwise whatever dotnet is on PATH.
$localDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }
if ($dotnet -ne "dotnet") { $env:DOTNET_ROOT = Split-Path $dotnet }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
# The SDK's background workload-manifest check has been seen to hang builds; no workloads are needed here.
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"

# No trailing backslash: Windows argument parsing would read it as escaping the closing quote.
& $dotnet build (Join-Path $PSScriptRoot "NormalGolfMultiplayer.csproj") -c $Configuration --nologo "-p:GameDir=$resolved"
exit $LASTEXITCODE
