<#
    Where a game rig keeps its things. Dot-source it: . (Join-Path $PSScriptRoot 'rig.ps1')

    Rig 0 is this PC's usual setup: %APPDATA%\Nitrox, port 11000, the Steam game install. Rigs 1 and up are isolated so
    several can run at the same time: their own Nitrox data folder (passed to the server and the games as --data-path,
    so saves, logs and bridge files stay apart), port 11000 + <rig>, and their own game folder, because every launch
    puts its NitroxPatcher.dll into the game's Managed folder.

    The rig and windowless mode default to $env:NITROX_RIG and $env:NITROX_WINDOWLESS, so the dual-client.ps1 calls
    inside scenario scripts (restarts) stay on the rig and in the mode the scenario was started with.
#>

function Get-NitroxRig {
    param([int]$Rig = [int]$env:NITROX_RIG)
    if ($Rig -lt 0 -or $Rig -gt 9) { throw "Rig must be 0 to 9, not $Rig" }
    $rigsRoot = Join-Path $env:LOCALAPPDATA 'Nitrox\rigs'
    if ($Rig -eq 0) {
        $dataDir = Join-Path $env:APPDATA 'Nitrox'
        $savesDir = (Get-ItemProperty -Path 'HKCU:\SOFTWARE\Nitrox' -Name 'SavesFolderDir' -ErrorAction SilentlyContinue).SavesFolderDir
        if (-not $savesDir) { $savesDir = Join-Path $dataDir 'saves' }
        $gameDir = $null
        $stateFile = Join-Path $env:TEMP 'nitrox-dual-client.json'
    }
    else {
        $dataDir = Join-Path $rigsRoot "rig$Rig\data"
        $savesDir = Join-Path $dataDir 'saves'
        $gameDir = Join-Path $rigsRoot "rig$Rig\game"
        $stateFile = Join-Path $env:TEMP "nitrox-dual-client-rig$Rig.json"
    }
    [pscustomobject]@{
        Number     = $Rig
        Windowless = $env:NITROX_WINDOWLESS -in @('1', 'true')
        DataDir    = $dataDir
        SavesDir   = $savesDir
        LogsDir    = Join-Path $dataDir 'logs'
        BridgeDir  = Join-Path $dataDir 'bridge'
        Port       = 11000 + $Rig
        # $null: the game install itself (Find-SubnauticaInstall).
        GameDir    = $gameDir
        StateFile  = $stateFile
    }
}

<#
    The Steam install of Subnautica, or $env:SUBNAUTICA_INSTALLATION_PATH (the same override the build uses).
#>
function Find-SubnauticaInstall {
    if ($env:SUBNAUTICA_INSTALLATION_PATH -and (Test-Path (Join-Path $env:SUBNAUTICA_INSTALLATION_PATH 'Subnautica.exe'))) {
        return $env:SUBNAUTICA_INSTALLATION_PATH
    }
    $steamPath = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name 'SteamPath' -ErrorAction SilentlyContinue).SteamPath
    if ($steamPath) {
        $libraries = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
        if (Test-Path $libraries) {
            foreach ($match in [regex]::Matches((Get-Content $libraries -Raw), '"path"\s+"([^"]+)"')) {
                $library = $match.Groups[1].Value.Replace('\\', '\')
                if (Test-Path (Join-Path $library 'steamapps\appmanifest_264710.acf')) {
                    return Join-Path $library 'steamapps\common\Subnautica'
                }
            }
        }
    }
    throw 'Subnautica not found in any Steam library. Set SUBNAUTICA_INSTALLATION_PATH to the game folder.'
}
