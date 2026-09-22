# Shared game-folder lookup, dot-sourced by build.ps1 and package.ps1.
# "Game folder" always means the folder containing Normal Golf Game.exe
# (inside the Steam install that is ...\Normal Golf Game\Normal).

function Test-GameDir([string]$dir) {
    return $dir -and (Test-Path (Join-Path $dir "Normal Golf Game_Data\Managed\Assembly-CSharp.dll"))
}

function Resolve-GameDir([string]$explicit, [string]$repoRoot) {
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($explicit) { $candidates.Add($explicit) }
    if ($env:NGMP_GAME_DIR) { $candidates.Add($env:NGMP_GAME_DIR) }

    $pathFile = Join-Path $repoRoot "game-path.txt"
    if (Test-Path $pathFile) {
        $line = Get-Content $pathFile | Where-Object { $_.Trim() -and -not $_.Trim().StartsWith("#") } | Select-Object -First 1
        if ($line) { $candidates.Add($line.Trim()) }
    }

    # Every Steam library on this PC, read from Steam's own library list.
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $steam = $steam -replace '/', '\'
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $lib = $m.Groups[1].Value -replace '\\\\', '\'
                $candidates.Add((Join-Path $lib "steamapps\common\Normal Golf Game\Normal"))
            }
        }
        $candidates.Add((Join-Path $steam "steamapps\common\Normal Golf Game\Normal"))
    }

    # Repo sitting next to the game (the layout this mod was developed in).
    $candidates.Add((Join-Path $repoRoot "..\Normal"))

    foreach ($c in $candidates) {
        if (Test-GameDir $c) { return (Resolve-Path $c).Path }
    }
    return $null
}

function Get-GameDirOrExit([string]$explicit, [string]$repoRoot, [switch]$RequireBepInEx) {
    $resolved = Resolve-GameDir $explicit $repoRoot
    if (-not $resolved) {
        Write-Host "Could not find Normal Golf Game." -ForegroundColor Red
        Write-Host "Point the scripts at it in any of these ways:"
        Write-Host '  .\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Normal Golf Game\Normal"'
        Write-Host '  $env:NGMP_GAME_DIR = "...\Normal Golf Game\Normal"'
        Write-Host '  Set-Content game-path.txt "...\Normal Golf Game\Normal"'
        Write-Host "That is the folder containing Normal Golf Game.exe."
        exit 1
    }
    if ($RequireBepInEx -and -not (Test-Path (Join-Path $resolved "BepInEx\core\BepInEx.dll"))) {
        Write-Host "BepInEx is not installed in $resolved" -ForegroundColor Red
        Write-Host "Install BepInEx 5 (win x64) there first: https://github.com/BepInEx/BepInEx/releases"
        exit 1
    }
    return $resolved
}
