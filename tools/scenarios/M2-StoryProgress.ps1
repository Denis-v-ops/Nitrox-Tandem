<#
.SYNOPSIS
    MULTINAUGHTICA_SPEC M2: story goals completed outside StoryGoal.Execute, teleporter pairs, the gun-disable leftovers,
    the respawn in the pod, Sea Emperor babies, a double fragment scan and story goals that arrive while a game loads.

.DESCRIPTION
    Starts a new world (save m2-story-progress, replaced every run) with Alice and Bob. Windowless ($env:NITROX_WINDOWLESS=1)
    or a rig > 0: the last step restarts one game by itself, which the launcher mode can't (-SkipLoading leaves it out).
    1. A goal completed with StoryGoalManager.OnGoalComplete directly (what PrecursorGlobalKeyActivator.ToggleDoor does
       with a key's doorActivationKey) reaches Bob. Fails without StoryGoalManager_OnGoalComplete_Patch: nothing is sent.
    2. Alice activates a teleporter pair (TeleporterManager.ActivateTeleporter): Bob has its Nitrox_TeleporterActive goal
       and the pair is active in his game. Fails without TeleporterManager_ActivateTeleporter_Patch / TeleporterPairs.
    3. At the gun terminal: Bob stands in the terminal area (open), Alice queues RadioSunbeam4 and disables the gun
       (console goal). Bob's terminal closes (fails without the NotifyGoalComplete Postfix: it stays open).
    4. Sea Emperor: Alice spawns a baby near the Emperor; in both games it swims to the mother (a swim target). Fails
       without the Start patch change: the spawner parented it to the cell root, and the old patch skipped every baby
       with a parent. Alice's baby reaches the teleporter (SeaEmperorBaby.Teleport): the server forgets it. Fails
       without SeaEmperorBaby_Teleport_Patch.
    5. Bob gets the same fragment scan twice (StoryManager.ScanCompleted): no exception. Fails with PDAScanner.fragments.Add.
    6. Alice respawns in the pod (Player.MovePlayerToRespawnPoint): the server keeps her in the pod (SubRootId, parent).
       Fails without PlayerParentRules: the last packet ("no sub root") cleared it. Last before the relog: a teleport
       takes her out of the pod.
    7. Both relog: Bob still has the door goal and the teleporter goal (the server stored them), the server's radio
       queue no longer brings back RadioSunbeam4 (fails without StoryManager.RemoveRadioMessagesCancelledBy), and Alice
       is still in the pod on the server.
    8. Bob's game is restarted; while it loads, Alice completes goals every half second. Bob joins with all of them and
       his log has no "Error trying to process packet" for StoryGoalExecuted (without InitialSyncDeferral the goals
       that arrive in the menu or while loading throw: StoryGoalScheduler.main is null).
    Not covered here (no fixture with the story that far): radiation leaks after the explosion, the incubator, a rocket,
    the lifepod beacons' case-insensitive keys (asset data). The L2 StoryProgressTest and unit tests cover the server
    side of radiation, rockets, the pod and the radio queue.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'm2-story-progress', [switch]$SkipLoading)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$rigInfo = Get-NitroxRig
$doorGoal = 'M2TestDoorKey'
$pairId = 'm2test'
$pairGoal = "Nitrox_TeleporterActive:$pairId"
$terminal = 'find:PrecursorDisableGunTerminal'
# GotoConsoleCommand location "gunterminal" (as in S3-GunDisableSyncs.ps1)
$terminalRoom = '372.9 -72.7 1078.4'
# SeaEmperorBaby.teleporterPosition (decompiled default): the aquarium's exit teleporter, inside the Primary Containment Facility
$aquariumTeleporter = '243.4 -1580 -307.5'

function Test-Goal {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call Story.StoryGoalManager.main.IsGoalComplete $Key") -eq 'true'
}

function Get-ServerQuery {
    param([string]$Id)
    Invoke-Bridge 'server' "query $Id"
}

function Get-ServerSubRoot {
    param([string]$PlayerId)
    if ((Get-ServerQuery $PlayerId) -match 'SubRootId: (\S+)') { $Matches[1] } else { '?' }
}

# The next three come from M3-JoinSaveSafety.ps1: a game that dual-client.ps1 started, killed and started again.
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

function Stop-GameHard {
    param($Game)
    Stop-Process -Id $Game.Id -Force
    Wait-Process -Id $Game.Id -Timeout 60 -ErrorAction SilentlyContinue
}

function Start-GameAgain {
    param($Game)
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

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    $aliceId = (Invoke-BridgeJson $A 'state').playerId
    $podId = Get-ServerSubRoot $aliceId
    Write-Host "  Alice $aliceId starts in the pod $podId"

    Step 'Fresh world: the test goals are not complete (the Nitrox pair prefix is no game goal)' {
        foreach ($player in $A, $B) {
            Assert-True (-not (Test-Goal $player $doorGoal)) "$player doesn't have $doorGoal"
            Assert-True (-not (Test-Goal $player $pairGoal)) "$player doesn't have $pairGoal"
        }
    }

    Step 'A goal completed outside StoryGoal.Execute reaches the partner (Precursor door key)' {
        Invoke-Bridge $A "call Story.StoryGoalManager.main.OnGoalComplete $doorGoal" | Out-Null
        $synced = Wait-Until { Test-Goal $B $doorGoal } -TimeoutSeconds 15
        Assert-True ([bool]$synced) "$B has $doorGoal (StoryGoalManager_OnGoalComplete_Patch)"
    }

    Step 'A teleporter pair Alice activates is active for Bob' {
        Invoke-Bridge $A "call TeleporterManager.ActivateTeleporter $pairId" | Out-Null
        $synced = Wait-Until { Test-Goal $B $pairGoal } -TimeoutSeconds 15
        Assert-True ([bool]$synced) "$B has $pairGoal"
        Assert-Equal 'true' (Invoke-Bridge $B "call TeleporterManager.GetTeleporterActive $pairId") "the pair is active in $B's game"
    }

    Step "Bob's open gun terminal closes when Alice disables the gun" {
        Invoke-Bridge $A "teleport $terminalRoom" | Out-Null
        Invoke-Bridge $B 'teleport 370 -72.7 1076' | Out-Null
        $found = Wait-Until { (Invoke-BridgeJson $A 'find PrecursorDisableGunTerminalArea') -and (Invoke-BridgeJson $B 'find PrecursorDisableGunTerminalArea') } -TimeoutSeconds 60
        Assert-True ([bool]$found) 'the terminal is loaded in both games'
        $area = (Invoke-BridgeJson $B 'find PrecursorDisableGunTerminalArea')[0]
        $p = $area.position
        Invoke-Bridge $B ('teleport {0} {1} {2}' -f $p[0], ($p[1] - 0.8), $p[2]) | Out-Null
        $opened = Wait-Until { (Invoke-Bridge $B "get $terminal PrecursorDisableGunTerminal.opened") -eq 'true' } -TimeoutSeconds 10
        Assert-True ([bool]$opened) "$B's terminal opened (he stands in its area)"

        Invoke-Bridge $A 'console goal Radio RadioSunbeam4' | Out-Null
        $queued = Wait-Until { (Invoke-Bridge $B 'get Story.StoryGoalManager.main.pendingRadioMessages') -match 'RadioSunbeam4' } -TimeoutSeconds 15
        Assert-True ([bool]$queued) "RadioSunbeam4 is queued (relayed to $B)"
        Invoke-Bridge $A 'console goal Story Goal_Disable_Gun' | Out-Null
        $disabled = Wait-Until { (Invoke-Bridge $B 'get StoryGoalCustomEventHandler.main.gunDisabled') -eq 'true' } -TimeoutSeconds 15
        Assert-True ([bool]$disabled) "$B has the gun disabled"
        $closed = Wait-Until { (Invoke-Bridge $B "get $terminal PrecursorDisableGunTerminal.opened") -eq 'false' } -TimeoutSeconds 5
        Assert-True ([bool]$closed) "$B's terminal closed (FIX_SPEC S3 step 2)"
        Assert-True (-not ((Invoke-Bridge $B 'get Story.StoryGoalManager.main.pendingRadioMessages') -match 'RadioSunbeam4')) "$B's radio queue dropped RadioSunbeam4 (vanilla)"
    }

    Step 'A Sea Emperor baby of the server swims to the mother; one that reaches the teleporter is gone on the server' {
        Invoke-Bridge $A "teleport $aquariumTeleporter" | Out-Null
        Invoke-Bridge $B "teleport $aquariumTeleporter" | Out-Null
        $loaded = Wait-Until { (Invoke-BridgeJson $A 'find SeaEmperor') -and (Invoke-BridgeJson $B 'find SeaEmperor') } -TimeoutSeconds 90
        Assert-True ([bool]$loaded) 'the Sea Emperor is loaded in both games'
        $emperor = (Invoke-BridgeJson $A 'find SeaEmperor')[0].position
        foreach ($player in $A, $B) {
            Invoke-Bridge $player ('teleport {0} {1} {2}' -f $emperor[0], ($emperor[1] + 25), ($emperor[2] + 25)) | Out-Null
        }
        Start-Sleep -Seconds 3
        Invoke-Bridge $A 'console spawn seaemperorbaby' | Out-Null
        $baby = Wait-Until { $b = @(Invoke-BridgeJson $A 'find SeaEmperorBaby') | Where-Object { $_.id } | Select-Object -First 1; $b } -TimeoutSeconds 15
        Assert-True ([bool]$baby) "$A spawned a baby"
        if ($baby) {
            $babyId = $baby.id
            $onBob = Wait-Until { (Invoke-Bridge $B "entity $babyId") -match $babyId } -TimeoutSeconds 30
            Assert-True ([bool]$onBob) "$B got the baby from the server"
            foreach ($player in $A, $B) {
                $swimming = Wait-Until { (Invoke-Bridge $player "get $babyId SeaEmperorBaby.swimToTarget.target") -ne 'null' } -TimeoutSeconds 10
                Assert-True ([bool]$swimming) "$player's baby swims to the mother (it has a swim target)"
            }
            Invoke-Bridge $A "call $babyId SeaEmperorBaby.Teleport" | Out-Null
            $forgotten = Wait-Until { (Get-ServerQuery $babyId) -match 'not found' } -TimeoutSeconds 10
            Assert-True ([bool]$forgotten) 'the server forgot the teleported baby'
        }
    }

    Step "Bob gets the same fragment scan twice: no exception" {
        # Any loaded entity serves as the fragment: the escape pod is a global root, loaded wherever Bob is
        $first = Invoke-Bridge $B "call NitroxClient.GameLogic.StoryManager.ScanCompleted $podId false"
        $second = $null
        try { $second = Invoke-Bridge $B "call NitroxClient.GameLogic.StoryManager.ScanCompleted $podId false" } catch { $second = "threw: $($_.Exception.Message)" }
        Assert-True (-not ("$first$second" -match 'threw|exception|already been added')) "the second scan of one fragment doesn't throw ('$second')"
    }

    Step 'Alice respawns in the pod: the server keeps her in it' {
        Assert-True ($podId -ne '<null>' -and $podId -ne '?') "the server had Alice in a pod at the start ($podId)"
        Invoke-Bridge $A 'teleport 30 -10 30' | Out-Null
        $left = Wait-Until { (Get-ServerSubRoot $aliceId) -eq '<null>' } -TimeoutSeconds 10
        Assert-True ([bool]$left) 'the teleport took her out of the pod on the server'
        Invoke-Bridge $A 'call Player.main.MovePlayerToRespawnPoint' | Out-Null
        Start-Sleep -Seconds 3
        Assert-Equal $podId (Get-ServerSubRoot $aliceId) 'the server has Alice in the pod after the respawn'
        Assert-True ((Get-ServerQuery $aliceId) -match "ParentId: $podId") "her entity's parent is the pod"
    }

    Step 'Both relog: the goals came back from the server, the Sunbeam radio message did not' {
        Invoke-Bridge 'server' 'save' | Out-Null
        Start-Sleep -Seconds 3
        & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop | Out-Null
        Start-Sleep -Seconds 3
        Start-ScenarioGames -Players $A, $B -SaveName $SaveName -KeepSave
        Assert-True (Test-Goal $B $doorGoal) "$B still has $doorGoal"
        Assert-True (Test-Goal $B $pairGoal) "$B still has $pairGoal"
        Assert-True (-not ((Invoke-Bridge $B 'get Story.StoryGoalManager.main.pendingRadioMessages') -match 'RadioSunbeam4')) "$B's radio queue from the server has no RadioSunbeam4"
        Assert-Equal $podId (Get-ServerSubRoot $aliceId) 'Alice is still in the pod on the server'
    }

    if ($SkipLoading -or -not ($rigInfo.Windowless -or $rigInfo.Number -gt 0)) {
        Write-Host 'Skipping the loading step (needs windowless games or a rig > 0, or -SkipLoading was given)' -ForegroundColor Yellow
    }
    else {
        Step "Goals that come while Bob's game loads are applied after its sync" {
            $marks = Get-LogMarks @($B) $SaveName
            $game = Get-GameProcess $B
            Stop-GameHard $game
            Start-Sleep -Seconds 2
            Start-GameAgain $game | Out-Null
            $sent = @()
            $whileLoading = 0
            $deadline = (Get-Date).AddSeconds(120)
            while ((Get-Date) -lt $deadline) {
                $synced = $false
                try { $synced = [bool](Invoke-BridgeJson $B 'state').initialSyncCompleted } catch { }
                if ($synced) { break }
                $key = "M2Loading$($sent.Count)"
                Invoke-Bridge $A "call Story.StoryGoalManager.main.OnGoalComplete $key" | Out-Null
                $sent += $key
                $whileLoading++
                Start-Sleep -Milliseconds 500
            }
            Assert-True ($whileLoading -gt 0) "$A completed $whileLoading goals while $B loaded"
            $joined = $null
            try { $joined = Wait-BridgeJoined $B -TimeoutSeconds 90 } catch { }
            Assert-True ([bool]$joined) "$B joined again"
            Start-Sleep -Seconds 2
            $missing = @($sent | Where-Object { -not (Test-Goal $B $_) })
            Assert-True ($missing.Count -eq 0) "$B has every goal completed while he loaded (missing: $($missing -join ', '))"
            $log = ((Get-LogTextSince $marks @($B) $SaveName).Values) -join "`n"
            Assert-True (-not ($log -match 'Error trying to process packet.*StoryGoalExecuted')) "$B's log has no StoryGoalExecuted processing error"
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'M2 StoryProgress' -Keep:$Keep
