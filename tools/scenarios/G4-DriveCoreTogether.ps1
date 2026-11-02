<#
.SYNOPSIS
    COOP_FEATURES_SPEC G4 and the G0 radio lock: the Aurora's last radiation leak only seals once both players are in
    the drive core room, and the Sunbeam radio message only plays while both are online.

.DESCRIPTION
    1. Alice explodes the Aurora (/aurora explode), the leaks get their ids. Their positions are printed (they define
       the drive core room box in StoryGateRules).
    2. Both in the drive core room; Alice heals 10 of the 11 leaks (LiveMixin.AddHealth 1000, the welder's own call).
       Bob teleports out of the room.
    3. Alice "welds" the last leak (AddHealth 10 until it would seal) alone: it stays leaking on both games, the
       leaks-fixed goal isn't complete, the server's gate waits for Bob.
    4. Bob comes back into the room while Alice keeps welding: the leak seals on both games and the goal
       (LeakingRadiation.leaksFixedGoal, read at runtime) completes on both.
    5. Radio lock: RadioSunbeam4 is queued (goal radio RadioSunbeam4), Bob quits. Alice's radio doesn't play it (still
       queued, OnPlayRadioSunbeam4 not complete). Both rejoin; Alice's radio plays it, the Sunbeam countdown goal
       completes on both.

    The welder itself can't be driven windowless (Welder.Weld runs from an animation event), so the scenario calls
    LiveMixin.AddHealth on the leak like Welder.Weld does; Radio.PlayRadioMessage is called directly (what
    Radio.OnHandClick invokes 1.25 s after the click).

    Passed windowless 2026-11-02 (24 checks). Negative control the same day, with LiveMixin_AddHealth_Patch and
    Radio_PlayRadioMessage_Patch as on coop before G0: 9 checks fail (the last leak seals alone on both games and the
    goal completes, the radio plays RadioSunbeam4 with Bob offline).

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build
    (dual-client.ps1 -NoBuild is used, so build first).
#>
param([switch]$NoLaunch, [switch]$Keep, [switch]$SkipRadio)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$SaveName = 'bridge-scenario'

function Get-State { param([string]$Player) Invoke-BridgeJson $Player 'state' }

function Get-Leaks {
    param([string]$Player)
    @(Invoke-BridgeJson $Player 'find RadiationLeak 20')
}

function Get-LeakHealth {
    param([string]$Player, [string]$Id)
    [double](Invoke-Bridge $Player "get $Id LiveMixin.health")
}

function Test-Goal {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call Story.StoryGoalManager.main.IsGoalComplete $Key") -eq 'true'
}

function Get-PendingRadio {
    param([string]$Player)
    @(Invoke-BridgeJson $Player 'get Story.StoryGoalManager.main.pendingRadioMessages')
}

function Get-GateStatus { (Invoke-Bridge 'server' 'storygate status') -join "`n" }

function Move-To {
    param([string]$Player, [double[]]$Spot)
    Invoke-Bridge $Player ("teleport {0} {1} {2}" -f $Spot[0], $Spot[1], $Spot[2]) | Out-Null
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch

    Step 'Alice explodes the Aurora; the 11 leaks are loaded with ids on both games' {
        Invoke-Bridge $A 'chat /aurora explode' | Out-Null
        $exploded = Wait-Until { (Invoke-Bridge $A 'call CrashedShipExploder.main.IsExploded') -eq 'true' -and (Invoke-Bridge $B 'call CrashedShipExploder.main.IsExploded') -eq 'true' } -TimeoutSeconds 60
        Assert-True ([bool]$exploded) 'the Aurora exploded on both games'
        $script:leaks = Wait-Until { $l = Get-Leaks $A; if ($l.Count -ge 11 -and @($l | Where-Object { $_.id }).Count -ge 11) { $l } } -TimeoutSeconds 30
        Assert-Equal 11 @($script:leaks).Count 'Alice sees 11 radiation leaks with ids'
        foreach ($leak in $script:leaks) { Write-Host ("  leak {0} at {1}, health {2}" -f $leak.id, (Format-Vector ([double[]]$leak.position)), (Get-LeakHealth $A $leak.id)) }
        $xs = $script:leaks | ForEach-Object { [double]$_.position[0] }; $ys = $script:leaks | ForEach-Object { [double]$_.position[1] }; $zs = $script:leaks | ForEach-Object { [double]$_.position[2] }
        Write-Host ("  leak bounds: x {0:0.0}..{1:0.0}, y {2:0.0}..{3:0.0}, z {4:0.0}..{5:0.0}" -f ($xs | Measure-Object -Minimum).Minimum, ($xs | Measure-Object -Maximum).Maximum, ($ys | Measure-Object -Minimum).Minimum, ($ys | Measure-Object -Maximum).Maximum, ($zs | Measure-Object -Minimum).Minimum, ($zs | Measure-Object -Maximum).Maximum)
        $script:goalKey = Invoke-Bridge $A 'get LeakingRadiation.main.leaksFixedGoal.key'
        Write-Host "  leaks fixed goal: $script:goalKey"
        Assert-True (-not [string]::IsNullOrEmpty($script:goalKey)) 'the leaks-fixed goal key is read from the game'
        # Open water in the middle of the room (StoryGateRules.DriveCoreRoom is the leaks' box plus 6 m)
        $script:room = @(865.0, -5.0, 5.0)
    }

    Step 'Both in the drive core room; Alice heals 10 of the 11 leaks; Bob leaves the room' {
        Move-To $A $script:room
        Move-To $B $script:room
        Start-Sleep -Seconds 3
        $script:lastLeak = $script:leaks[10].id
        foreach ($leak in $script:leaks[0..9]) { Invoke-Bridge $A "call $($leak.id) LiveMixin.AddHealth 1000" | Out-Null }
        $synced = Wait-Until { @($script:leaks[0..9] | Where-Object { (Get-LeakHealth $B $_.id) -lt 100 }).Count -eq 0 } -TimeoutSeconds 15
        Assert-True ([bool]$synced) 'the 10 healed leaks are sealed on Bob''s game too'
        Assert-True ((Get-LeakHealth $A $script:lastLeak) -lt 100) "the last leak still leaks ($(Get-LeakHealth $A $script:lastLeak))"
        Assert-True (-not (Test-Goal $A $script:goalKey)) 'the goal isn''t complete yet'
        Move-To $B @(800.0, -20.0, 100.0)
        Start-Sleep -Seconds 3
    }

    Step 'Alice welds the last leak alone: held on both games, the server waits for Bob' {
        for ($i = 0; $i -lt 14; $i++) {
            Invoke-Bridge $A "call $script:lastLeak LiveMixin.AddHealth 10" | Out-Null
            Start-Sleep -Milliseconds 300
        }
        Start-Sleep -Seconds 2
        $health = Get-LeakHealth $A $script:lastLeak
        Assert-True ($health -lt 100) "the last leak didn't seal on Alice's game ($health)"
        Assert-True ((Get-LeakHealth $B $script:lastLeak) -lt 100) "nor on Bob's ($(Get-LeakHealth $B $script:lastLeak))"
        Assert-True (-not (Test-Goal $A $script:goalKey)) 'the goal isn''t complete on Alice''s game'
        Assert-True (-not (Test-Goal $B $script:goalKey)) 'nor on Bob''s'
        $status = try { Get-GateStatus } catch { "$_" }
        Write-Host "  $($status -replace "`n", "`n  ")"
        Assert-True ($status -match 'DRIVE_CORE[^\n]*WAITING[^\n]*waiting for Bob') 'the server''s drive core gate waits for Bob'
    }

    Step 'Bob comes into the room while Alice keeps welding: the leak seals for both, the goal completes on both' {
        Move-To $B $script:room
        Start-Sleep -Seconds 2
        $sealed = Wait-Until {
            Invoke-Bridge $A "call $script:lastLeak LiveMixin.AddHealth 10" | Out-Null
            (Get-LeakHealth $A $script:lastLeak) -ge 100
        } -TimeoutSeconds 20 -IntervalSeconds 0.5
        Assert-True ([bool]$sealed) "the last leak sealed on Alice's game ($(Get-LeakHealth $A $script:lastLeak))"
        $both = Wait-Until { (Get-LeakHealth $B $script:lastLeak) -ge 100 -and (Test-Goal $B $script:goalKey) } -TimeoutSeconds 15
        Assert-True ([bool]$both) "the leak sealed on Bob's game and his goal completed ($(Get-LeakHealth $B $script:lastLeak))"
        Assert-True (Test-Goal $A $script:goalKey) "the goal $script:goalKey completed on Alice's game"
    }
    # Not Assert-NoDiff: the leak's FixRealTime is extracted live (RadiationMetadataExtractor gives "now" for a sealed
    # leak), so every sealed leak shows as a METADATA difference in the diff. Check the server's record instead.
    Step 'The server stores the last leak as sealed' {
        $query = (Invoke-Bridge 'server' "query $script:lastLeak") -join "`n"
        $metadata = [regex]::Match($query, 'RadiationMetadata[^\]]*').Value
        Assert-True ($metadata -match 'Health: 100') "the server's record of the last leak is at full health ($metadata)"
    }

    if (-not $SkipRadio) {
        Step 'Radio lock: RadioSunbeam4 is queued, Bob quits, Alice''s radio keeps it' {
            Move-To $A @(-60, -6, 380)
            Invoke-Bridge $A 'console goal radio RadioSunbeam4' | Out-Null
            $queued = Wait-Until { (Get-PendingRadio $A) -contains 'RadioSunbeam4' -and (Get-PendingRadio $B) -contains 'RadioSunbeam4' } -TimeoutSeconds 15
            Assert-True ([bool]$queued) 'RadioSunbeam4 is queued on both games'
            Invoke-Bridge 'server' 'save' | Out-Null
            Invoke-Bridge $B 'call IngameMenu.main.QuitGame false' | Out-Null
            $left = Wait-Until { -not ((Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $B -and $_.online }) } -TimeoutSeconds 60
            Assert-True ([bool]$left) "$B left the game"
            Invoke-Bridge $A 'call find:Radio Radio.PlayRadioMessage' | Out-Null
            Start-Sleep -Seconds 3
            Assert-True ((Get-PendingRadio $A) -contains 'RadioSunbeam4') "the message is still queued on Alice's game ($((Get-PendingRadio $A) -join ', '))"
            Assert-True (-not (Test-Goal $A 'OnPlayRadioSunbeam4')) 'the Sunbeam countdown didn''t start'
            $status = try { Get-GateStatus } catch { "$_" }
            Write-Host "  $($status -replace "`n", "`n  ")"
            Assert-True ($status -match 'SUNBEAM_RADIO[^\n]*LOCKED[^\n]*waiting for Bob') 'the server''s radio gate is locked, naming Bob'
        }

        Step 'Both back online: Alice''s radio plays it, the countdown goal completes on both' {
            Invoke-Bridge 'server' 'save' | Out-Null
            Start-Sleep -Seconds 3
            & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop | Out-Null
            Start-Sleep -Seconds 5
            Start-ScenarioGames -Players $A, $B -SaveName $SaveName -KeepSave
            Start-Sleep -Seconds 5
            $queued = Wait-Until { (Get-PendingRadio $A) -contains 'RadioSunbeam4' } -TimeoutSeconds 30
            Assert-True ([bool]$queued) 'the message survived in the queue'
            Invoke-Bridge $A 'call find:Radio Radio.PlayRadioMessage' | Out-Null
            $played = Wait-Until { (Test-Goal $A 'OnPlayRadioSunbeam4') -and (Test-Goal $B 'OnPlayRadioSunbeam4') } -TimeoutSeconds 15
            Assert-True ([bool]$played) 'OnPlayRadioSunbeam4 completed on both games'
            Assert-True (-not ((Get-PendingRadio $A) -contains 'RadioSunbeam4')) 'the message left Alice''s queue'
            Assert-True (-not ((Get-PendingRadio $B) -contains 'RadioSunbeam4')) 'and Bob''s'
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
    Write-Host $_.ScriptStackTrace
}
Complete-Scenario -Name 'G4 DriveCoreTogether' -Keep:$Keep
