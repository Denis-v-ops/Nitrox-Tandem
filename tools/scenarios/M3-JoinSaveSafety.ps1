<#
.SYNOPSIS
    MULTINAUGHTICA_SPEC M3: a game that crashed rejoins at once as the same player, and every server start backs up the
    world first.

.DESCRIPTION
    Starts a new world (save m3-join-safety, replaced every run) with Alice and Bob.
    1. Alice swims to a spot; the server saves.
    2. Alice's game is killed like a crash (no disconnect message), then started again: she must join within 90 s as the
       same player (same player id, the server still has her spot). The server keeps a dead connection for 5 min in
       Debug builds; without the stale-session kick her reservation is refused ("name in use") until then, so the join
       times out.
    3. Alice's game is killed while it loads (after its reservation, before it has joined), then started again: she must
       join within 90 s. Without the kick (and without freeing the name of a reservation that never joined) she can't.
    4. Both relog (save, everything stopped, the same save started again). The server log says the world was backed up
       before loading, backups\server-starts holds a ServerStart zip with the save files, the launcher's restore list
       (backups\Backup - *.zip) doesn't include it, and both come back as the saved players. Without the start backup
       the folder is missing.
    Not covered here: a returning player joining while the world still loads can't be forced on the rig (the world of a
    scenario loads in moments); the L2 JoinSafetyTest holds the load and covers it.

    Needs a Debug build and windowless games ($env:NITROX_WINDOWLESS=1) or a rig > 0: it restarts one game by itself,
    which the launcher mode can't. -NoLaunch uses games that are already running (started the same way).
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'm3-join-safety')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$rigInfo = Get-NitroxRig
$saveDir = Join-Path $rigInfo.SavesDir $SaveName

function Get-ServerPlayer {
    param([string]$Name)
    (Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $Name } | Select-Object -First 1
}

<#
    The running game of a player that dual-client.ps1 (or this script) started: its process id, exe and arguments.
#>
function Get-GameProcess {
    param([string]$Player)
    $state = Get-Content $rigInfo.StateFile -Raw | ConvertFrom-Json
    $game = Get-CimInstance Win32_Process -Filter "Name = 'Subnautica.exe'" |
        Where-Object { $state.ProcessIds -contains $_.ProcessId -and $_.CommandLine -match "--instantlaunch `"$Player`"" } | Select-Object -First 1
    if (-not $game) { throw "No running game of $Player started by dual-client.ps1 (rig $($rigInfo.Number))" }
    $commandLine = $game.CommandLine.Trim()
    $arguments = if ($commandLine.StartsWith('"')) { $commandLine.Substring($commandLine.IndexOf('"', 1) + 1).Trim() } else { $commandLine.Substring($commandLine.IndexOf(' ') + 1).Trim() }
    [pscustomobject]@{ Id = $game.ProcessId; Exe = $game.ExecutablePath; Arguments = $arguments }
}

<#
    Kills the game like a crash (the server gets no disconnect message) and waits until it is gone.
#>
function Stop-GameHard {
    param($Game)
    Stop-Process -Id $Game.Id -Force
    Wait-Process -Id $Game.Id -Timeout 60 -ErrorAction SilentlyContinue
}

<#
    Starts the game again with the same arguments, recorded in the rig's state file so dual-client.ps1 -Stop closes it.
#>
function Start-GameAgain {
    param($Game)
    # What dual-client.ps1 gives the games (it sets them in this process when it starts them; not with -NoLaunch)
    $env:NITROX_LAUNCHER_PATH = Join-Path $script:RepoRoot 'Nitrox.Launcher\bin\Debug\net10.0'
    $env:NITROX_HOST_HOME = $env:USERPROFILE
    $env:SteamAppId = '264710'
    $env:SteamGameId = '264710'
    $process = Start-Process -FilePath $Game.Exe -ArgumentList $Game.Arguments -WorkingDirectory (Split-Path $Game.Exe) -PassThru
    $state = Get-Content $rigInfo.StateFile -Raw | ConvertFrom-Json
    $state.ProcessIds = @($state.ProcessIds) + $process.Id
    $state | ConvertTo-Json | Set-Content $rigInfo.StateFile
    $process.Id
}

function Get-ServerLogSince {
    param([hashtable]$Marks)
    $texts = Get-LogTextSince $Marks @() $SaveName
    ($texts.Keys | Where-Object { $_ -like "*_server_*" } | ForEach-Object { $texts[$_] }) -join "`n"
}

try {
    if (-not ($rigInfo.Windowless -or $rigInfo.Number -gt 0)) {
        throw 'Run windowless ($env:NITROX_WINDOWLESS=1) or on a rig > 0: this scenario restarts one game itself, which the launcher mode cannot.'
    }
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    $aliceId = (Invoke-BridgeJson $A 'state').playerId

    Step 'Alice swims to a spot, the server saves' {
        $start = [double[]](Invoke-BridgeJson $A 'state').position
        [double[]]$script:spot = @(($start[0] + 20), ($start[1] - 6), ($start[2] + 4))
        Invoke-Bridge $A ("teleport {0} {1} {2}" -f $spot[0], $spot[1], $spot[2]) | Out-Null
        $synced = Wait-Until { $p = Get-ServerPlayer $A; $p -and (Get-Distance ([double[]]$p.position) $spot) -lt 2 } -TimeoutSeconds 20
        Assert-True ([bool]$synced) "the server has Alice at $(Format-Vector $spot)"
        Invoke-Bridge 'server' 'save' | Out-Null
    }

    Step "Alice's game crashes; she rejoins at once as the same player" {
        $marks = Get-LogMarks @() $SaveName
        $game = Get-GameProcess $A
        Stop-GameHard $game
        Start-Sleep -Seconds 2
        Assert-True ([bool](Get-ServerPlayer $A).online) 'the server still holds the dead session (it times out after 5 min in Debug)'
        Start-GameAgain $game | Out-Null
        $joined = $null
        try { $joined = Wait-BridgeJoined $A -TimeoutSeconds 90 } catch { }
        Assert-True ([bool]$joined) 'Alice joined again within 90 s'
        if ($joined) {
            Assert-Equal $aliceId $joined.playerId 'she is the same player'
            $p = Get-ServerPlayer $A
            Assert-True ((Get-Distance ([double[]]$p.position) $spot) -lt 3) "the server still has her spot ($(Format-Vector ([double[]]$p.position)))"
        }
        $log = Get-ServerLogSince $marks
        Assert-True ($log.Contains('connects again from the same address')) 'the server removed the old session'
        Assert-True (-not $log.Contains('UNIQUE_PLAYER_NAME_CONSTRAINT_VIOLATED')) 'no reservation was refused for the name'
        Assert-Equal 1 @((Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $A -and $_.online }).Count 'one Alice online'
    }

    Step "Alice's game crashes while it loads; she rejoins at once" {
        $game = Get-GameProcess $A
        Stop-GameHard $game
        Start-Sleep -Seconds 2
        $marks = Get-LogMarks @() $SaveName
        $loadingId = Start-GameAgain $game
        # Kill it right after its reservation, while it loads the world (it joins about 20 s later)
        $reserved = Wait-Until { (Get-ServerLogSince $marks) -match "Reservation processed successfully: Username: $A - \[MultiplayerSessionReservation[^\]]*RESERVED" } -TimeoutSeconds 120 -IntervalSeconds 0.25
        Assert-True ([bool]$reserved) 'the restarted game got its reservation'
        Stop-GameHard ([pscustomobject]@{ Id = $loadingId })
        Assert-True (-not (Get-ServerLogSince $marks).Contains("Player $A joined successfully")) 'it was killed before it had joined'
        Start-Sleep -Seconds 2
        Start-GameAgain $game | Out-Null
        $joined = $null
        try { $joined = Wait-BridgeJoined $A -TimeoutSeconds 90 } catch { }
        Assert-True ([bool]$joined) 'Alice joined again within 90 s'
        if ($joined) { Assert-Equal $aliceId $joined.playerId 'she is the same player' }
    }

    Step 'Both relog: the server start backs up the world first' {
        $marks = Get-LogMarks @() $SaveName
        Invoke-Bridge 'server' 'save' | Out-Null
        Start-Sleep -Seconds 3
        & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop | Out-Null
        Start-Sleep -Seconds 3
        Start-ScenarioGames -Players $A, $B -SaveName $SaveName -KeepSave
        Assert-True ((Get-ServerLogSince $marks).Contains('World backed up before loading it')) 'the server log reports the start backup'
        $zips = @(Get-ChildItem (Join-Path $saveDir 'backups\server-starts') -Filter 'ServerStart - *.zip' -ErrorAction SilentlyContinue)
        Assert-True ($zips.Count -ge 1) "backups\server-starts holds a start backup ($($zips.Count))"
        if ($zips.Count -ge 1) {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = [IO.Compression.ZipFile]::OpenRead($zips[-1].FullName)
            try { $entries = @($zip.Entries | ForEach-Object { $_.FullName }) } finally { $zip.Dispose() }
            foreach ($file in 'Version.json', 'PlayerData.json', 'WorldData.json', 'GlobalRootData.json', 'EntityData.json') {
                Assert-True ($entries -contains $file) "the start backup holds $file"
            }
        }
        # The launcher lists the zips directly in backups\ whose name starts with "Backup - "
        $topLevel = @(Get-ChildItem (Join-Path $saveDir 'backups') -Filter '*.zip' -File | ForEach-Object { $_.Name })
        Assert-True (-not ($topLevel | Where-Object { $_ -like 'ServerStart*' })) "no start backup where the launcher's restore list looks ($($topLevel -join ', '))"
        Assert-Equal $aliceId (Invoke-BridgeJson $A 'state').playerId 'Alice is the saved player'
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'M3 JoinSaveSafety' -Keep:$Keep
