# Builds the plugin and produces dist\NormalGolfMultiplayer-v<version>.zip: BepInEx + the mod, ready for a
# player to extract into the folder that contains "Normal Golf Game.exe" (...\Normal Golf Game\Normal\).
# The BepInEx files are copied from your own installed copy, so install BepInEx 5 in the game first.
param([string]$GameDir = "")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
. (Join-Path $root "find-game.ps1")

& (Join-Path $root "build.ps1") -GameDir $GameDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$version = (Select-Xml -Path (Join-Path $root "NormalGolfMultiplayer.csproj") -XPath "//Version").Node.InnerText
$gameDir = Get-GameDirOrExit $GameDir $root -RequireBepInEx
$stage = Join-Path $root "dist\stage"
$pluginStage = Join-Path $stage "BepInEx\plugins\NormalGolfMultiplayer"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $pluginStage | Out-Null

# BepInEx loader + core, taken from the installed copy (no logs, config or cache).
foreach ($f in "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt") {
    Copy-Item (Join-Path $gameDir $f) $stage
}
Copy-Item (Join-Path $gameDir "BepInEx\core") (Join-Path $stage "BepInEx\core") -Recurse

# The mod.
Copy-Item (Join-Path $gameDir "BepInEx\plugins\NormalGolfMultiplayer\*.dll") $pluginStage
Copy-Item (Join-Path $root "PLAYER-README.txt") (Join-Path $pluginStage "README.txt")
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.txt") $pluginStage
Copy-Item (Join-Path $root "PLAYER-README.txt") (Join-Path $stage "NormalGolfMultiplayer-README.txt")

$zip = Join-Path $root "dist\NormalGolfMultiplayer-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
# Written entry by entry: Windows PowerShell's ZipFile.CreateFromDirectory stores "\" separators,
# which some extractors turn into literal file names.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
$archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $stageFull = (Resolve-Path $stage).Path.TrimEnd('\')
    Get-ChildItem $stageFull -Recurse -File -Force | ForEach-Object {
        $entry = $_.FullName.Substring($stageFull.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry) | Out-Null
    }
}
finally {
    $archive.Dispose()
    $stream.Dispose()
}
Remove-Item $stage -Recurse -Force

Write-Host "Created $zip"
