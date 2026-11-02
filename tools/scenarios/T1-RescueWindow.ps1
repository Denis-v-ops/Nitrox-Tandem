<#
.SYNOPSIS
    COOP_FEATURES_SPEC T1 (rescue window) and D3 (distress call). Alice takes lethal damage and goes down instead of
    dying; Bob revives her by holding the interact button on her body, by hand and carrying a first aid kit; the window
    shrinks on repeat downs; when it runs out she dies as in vanilla (death beacon); out of air she drowns, down or not.

.DESCRIPTION
    Alice plays without the no-damage and no-oxygen cheats (Bob keeps them, he only rescues). The server is started
    with MarkDeathPointsWithBeacon=True (written into the save's server.cfg), so a death leaves Nitrox's beacon.
    1. Both a metre apart underwater in the shallows.
    2. 500 damage: Alice is alive at 1 health, invincible, input locked, can't interact, the fade and the countdown are
       on, the server's window is 60 s; her air keeps draining. Bob's game: her body is a downed partner, it collapses
       and holds the pose (animator stopped in the Death layer), the weak pain sound plays from it, her marker turns
       into the red distress call ("Alice, down, NN s").
    3. Bob holds for 3 s: nothing. Then 6.5 s: Alice is up with about 20 health, everything unlocked; her body gets up
       and the marker is back to normal on Bob's game.
    4. Second down: the window is 30 s. Bob carries a first aid kit and holds 2.5 s: Alice gets 50 health, the kit is
       gone from Bob's inventory and from the server.
    5. Third down: no window, Alice dies at once (vanilla death: respawn with full health, the death beacon).
    6. D3 at 400 m ("rescue rest" first, as a sleep would): Alice down at 400 m, Bob at 50 m far away. Bob's game shows
       the distress call although the line is cut (D2); the marker stays put when Alice is moved and jumps to her
       every 5 s, with the ping sound. Then the 60 s run out: Alice dies, Bob's distress call ends.
    7. Out of air while up: Alice drowns without going down. Out of air while down: she drowns before the window
       ends, and Bob's game stops showing her as down.

    Windowless limits: the fade (overlay value) and the countdown are read from RescueWindow, the pose from the
    animator's state; how they look needs a windowed run.

    Negative control: run on a build without the LiveMixin.Kill prefix (or unfixed coop): step 2 fails (Alice dies).

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$SaveName = 'bridge-scenario'
$shallowSpot = @(-60, -10, 380)
$deepSpot = @(-485, -400, 1326)         # above Lifepod 29 (floor about 500 m), interference by depth (D2)
$bobFarSpot = @(263, -50, 164)          # Grassy Plateaus, 50 m

function Get-State { param([string]$Player) Invoke-BridgeJson $Player 'state' }
function Get-Value { param([string]$Player, [string]$Path) (Invoke-Bridge $Player "get $Path" -AllowError).Trim('"') }
function Get-Health { [double](Get-Value $A 'Player.main.liveMixin.health') }
function Get-RescueState { Get-Value $A 'RescueWindow.State' }
function Get-Signal { param([string]$Member) Get-Value $B "$aliceId PartnerSignal.$Member" }

function Set-AliceOxygen {
    param([double]$Value)
    $now = [double](Get-State $A).oxygen
    if ($now -gt $Value) { Invoke-Bridge $A "call player OxygenManager.RemoveOxygen $($now - $Value)" | Out-Null }
    elseif ($now -lt $Value) { Invoke-Bridge $A "call player OxygenManager.AddOxygen $($Value - $now)" | Out-Null }
}

function Move-Both {
    param([double[]]$Spot)
    Invoke-Bridge $A "teleport $($Spot[0]) $($Spot[1]) $($Spot[2])" | Out-Null
    Invoke-Bridge $B "teleport $($Spot[0] + 1) $($Spot[1]) $($Spot[2])" | Out-Null
    Start-Sleep -Seconds 4
}

# Lethal damage to Alice; returns the window her game got (0 if the kill went through), after the server answered.
function Invoke-LethalDamage {
    Invoke-Bridge $A 'call player LiveMixin.TakeDamage 500' | Out-Null
    $answer = Wait-Until {
        $state = Get-RescueState
        if ($state -eq 'DOWN') { [double](Get-Value $A 'RescueWindow.SecondsLeft') }
        elseif ($state -eq 'UP') { -1 }
    } -TimeoutSeconds 10 -IntervalSeconds 0.25
    if ($answer -eq -1) { return 0 }
    $answer
}

function Invoke-BobHold {
    param([double]$Seconds)
    Invoke-Bridge $B "hold $aliceId $Seconds" | Out-Null
    Wait-Until { $s = Invoke-BridgeJson $B 'hold status'; if (-not $s.active) { $s } } -TimeoutSeconds ([int]$Seconds + 10) -IntervalSeconds 0.5
}

function Wait-Respawned {
    # Vanilla death: OnKill, 5 s, then the respawn resets health to full
    $dead = Wait-Until { (Invoke-Bridge $A 'call Player.main.IsAlive') -eq 'false' } -TimeoutSeconds 10 -IntervalSeconds 0.25
    $respawned = Wait-Until { (Get-Health) -ge 99 -and (Get-Value $A 'Player.main.playerController.inputEnabled') -eq 'true' } -TimeoutSeconds 40
    [bool]$dead -and [bool]$respawned
}

function Get-DeathBeacons { @(Invoke-BridgeJson $A 'find DeathBeacon 5000' | Where-Object { $_ }).Count }

function Assert-AliceUp {
    param([string]$Because)
    Assert-Equal 'UP' (Get-RescueState) "Alice's game: she is up ($Because)"
    Assert-Equal 'true' (Get-Value $A 'Player.main.playerController.inputEnabled') 'her input is unlocked'
    Assert-Equal 'false' (Get-Value $A 'Player.main.liveMixin.invincible') 'she can be hurt again'
    Assert-Equal 'true' (Invoke-Bridge $A 'call Player.main.IsFreeToInteract') 'she can interact again'
}

function Assert-BobSeesAliceUp {
    $up = Wait-Until { (Get-Value $B "$aliceId PartnerHandTarget.IsDowned") -eq 'false' -and (Get-Signal 'Rule') -ne 'distress' } -TimeoutSeconds 5 -IntervalSeconds 0.25
    Assert-True ([bool]$up) "Bob's game: Alice isn't down any more (marker rule $(Get-Signal 'Rule'))"
}

try {
    if (-not $NoLaunch) {
        $saveDir = Join-Path (Get-NitroxRig).SavesDir $SaveName
        if (Test-Path $saveDir) { Remove-Item -Recurse -Force $saveDir }
        New-Item -ItemType Directory -Force $saveDir | Out-Null
        Set-Content -Path (Join-Path $saveDir 'server.cfg') -Value 'MarkDeathPointsWithBeacon=True' -Encoding UTF8
    }
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch -KeepSave
    $script:aliceId = (Get-State $A).playerId
    Write-Host "  rescue: $(Invoke-Bridge 'server' 'rescue status' -AllowError)"

    Step 'Both underwater in the shallows, a metre apart; Alice without cheats' {
        Move-Both $shallowSpot
        Invoke-Bridge $A 'call GameModeUtils.DeactivateCheat NoOxygen' | Out-Null
        Invoke-Bridge $A 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
        Set-AliceOxygen 45
        foreach ($player in $A, $B) { Invoke-Bridge $player 'sounds clear' | Out-Null }
        Assert-True ([bool](Get-State $A).isUnderwater) 'Alice is underwater'
    }

    Step 'Lethal damage: Alice goes down instead of dying' {
        Invoke-Bridge $A 'call player LiveMixin.TakeDamage 500' | Out-Null
        Start-Sleep -Milliseconds 500
        # Vanilla reads first: on an unfixed build she is dead here
        Assert-Equal 'true' (Invoke-Bridge $A 'call Player.main.IsAlive') 'Alice is alive after 500 damage'
        Assert-Equal 1 (Get-Health) 'at 1 health'
        $window = Wait-Until { $s = [double](Get-Value $A 'RescueWindow.SecondsLeft'); if ($s -gt 0) { $s } } -TimeoutSeconds 5 -IntervalSeconds 0.25
        Assert-True ($window -gt 50 -and $window -le 60) "the server's window: 60 s ($window left)"
        Assert-Equal 'DOWN' (Get-RescueState) 'she is down'
        Assert-Equal 'true' (Get-Value $A 'Player.main.liveMixin.invincible') 'invincible while down'
        Assert-Equal 'false' (Get-Value $A 'Player.main.playerController.inputEnabled') 'her input is locked'
        Assert-Equal 'true' (Get-Value $A 'MainCameraControl.main.enabled') 'her camera stays free'
        Assert-Equal 'false' (Invoke-Bridge $A 'call Player.main.IsFreeToInteract') "she can't interact"
        $fade = [double](Get-Value $A 'RescueWindow.LastFade')
        Assert-True ($fade -ge 0.35 -and $fade -le 1) "the fade is on (overlay $fade)"
        $countdown = Get-Value $A 'RescueWindow.LastCountdown'
        Assert-True ($countdown -match '\d+ s') "the countdown shows ('$countdown')"
        Invoke-Bridge $A 'call player LiveMixin.TakeDamage 50' | Out-Null
        Assert-Equal 1 (Get-Health) 'more damage does nothing while down'
        $o0 = [double](Get-State $A).oxygen
        Start-Sleep -Seconds 3
        $o1 = [double](Get-State $A).oxygen
        Assert-True ($o1 -lt $o0) "her air keeps draining ($o0 -> $o1)"

        $downed = Wait-Until { (Get-Value $B "$aliceId PartnerHandTarget.IsDowned") -eq 'true' } -TimeoutSeconds 5 -IntervalSeconds 0.25
        Assert-True ([bool]$downed) "Bob's game: Alice's body is a downed partner"
        $held = Wait-Until { (Get-Value $B "$aliceId DownedPose.Held") -eq 'true' } -TimeoutSeconds 6 -IntervalSeconds 0.25
        $anim = (Invoke-BridgeJson $B "animator $aliceId")[0]
        $death = $anim.layers | Where-Object layer -eq 'Death'
        Write-Host "  animator speed $($anim.speed), Death layer: $($death.clips -join ',') at $($death.normalizedTime)"
        Assert-True ([bool]$held -and $anim.speed -eq 0 -and @($death.clips).Count -gt 0) 'her body collapsed and holds the pose (animator stopped in the Death layer)'
        Start-Sleep -Seconds 2
        Assert-True ((Invoke-BridgeJson $B "animator $aliceId")[0].speed -eq 0) 'still lying 2 s later'
        $pain = @(Invoke-BridgeJson $B 'sounds 15 pain_no_tank_light' | Where-Object { $_ -and $_.remotePlayer })
        Assert-True ($pain.Count -ge 1) "the weak pain sound plays from her body ($($pain.Count) times)"
        Assert-Equal 'distress' (Get-Signal 'Rule') 'her marker is the distress call'
        Assert-Equal 2 (Get-Signal 'ColorIndex') 'in the red marker colour'
        $label = Get-Signal 'Label'
        Assert-True ($label -match '^Alice .+ \d+ s$') "labelled with the time left ('$label')"
    }

    Step 'Bob holds 3 s: not enough. Then 6.5 s by hand: Alice is up with about 20 health' {
        $status = Invoke-BobHold 3
        Assert-True ($status.targetedFrames -gt 0 -and $status.maxProgress -gt 0.3) "Bob's game targeted her body and showed progress ($($status.maxProgress))"
        Start-Sleep -Milliseconds 500
        Assert-Equal 'DOWN' (Get-RescueState) 'a 3 s hold revives nobody'
        $status = Invoke-BobHold 6.5
        Write-Host "  hold: $($status | ConvertTo-Json -Compress -Depth 3)"
        $up = Wait-Until { (Get-RescueState) -eq 'UP' } -TimeoutSeconds 5 -IntervalSeconds 0.25
        Assert-True ([bool]$up) 'Alice is revived'
        $health = Get-Health
        Assert-True ($health -ge 19 -and $health -le 21) "with about 20 health ($health)"
        Assert-AliceUp 'revived by hand'
        Assert-BobSeesAliceUp
        Assert-True ((Invoke-BridgeJson $B "animator $aliceId")[0].speed -eq 1) 'her body moves again'
        Assert-True ((Get-Signal 'ColorIndex') -ne '2') "the marker has its own colour again ($(Get-Signal 'ColorIndex'))"
    }

    Step 'Second down: a 30 s window. Bob carries a first aid kit and revives her in 2.5 s: 50 health, the kit is used up' {
        Invoke-Bridge $B 'console item firstaidkit' | Out-Null
        $kit = Wait-Until { (Get-State $B).inventory | Where-Object techType -eq 'FirstAidKit' | Select-Object -First 1 } -TimeoutSeconds 10
        Assert-True ([bool]$kit) "Bob has a first aid kit ($($kit.id))"
        Set-AliceOxygen 45
        $window = Invoke-LethalDamage
        Assert-True ($window -gt 25 -and $window -le 30) "the second down gets half the window ($window s)"
        $status = Invoke-BobHold 2.5
        $up = Wait-Until { (Get-RescueState) -eq 'UP' } -TimeoutSeconds 5 -IntervalSeconds 0.25
        Assert-True ([bool]$up) "a 2.5 s hold with the kit revives her (progress $($status.maxProgress))"
        $health = Get-Health
        Assert-True ($health -ge 49 -and $health -le 51) "with 50 health ($health)"
        Assert-AliceUp 'revived with a kit'
        Assert-BobSeesAliceUp
        $gone = Wait-Until { -not ((Get-State $B).inventory | Where-Object id -eq $kit.id) } -TimeoutSeconds 5
        Assert-True ([bool]$gone) "the kit is gone from Bob's inventory"
        $query = Invoke-Bridge 'server' "query $($kit.id)" -AllowError
        Assert-True ($query -notmatch 'FirstAidKit') "and from the server ($query)"
    }

    Step 'Third down: no window, Alice dies as in vanilla; the death beacon marks the spot' {
        Set-AliceOxygen 45
        $beaconsBefore = Get-DeathBeacons
        $window = Invoke-LethalDamage
        Assert-Equal 0 $window 'the third down since she rested gets no window'
        Assert-True (Wait-Respawned) 'she died and respawned with full health'
        Assert-True ((Get-DeathBeacons) -gt $beaconsBefore) "Nitrox's death beacon was placed ($beaconsBefore -> $(Get-DeathBeacons))"
        Assert-AliceUp 'after the respawn'
        Assert-Equal 'false' (Get-Value $B "$aliceId PartnerHandTarget.IsDowned") "Bob's game never showed her down"
    }

    Step 'D3: Alice down at 400 m, Bob at 50 m: the distress call gets through and pulses every 5 s' {
        Write-Host "  $(Invoke-Bridge 'server' 'rescue rest Alice')"
        Invoke-Bridge $A 'call GameModeUtils.ActivateCheat NoOxygen' | Out-Null
        Invoke-Bridge $A "teleport $($deepSpot -join ' ')" | Out-Null
        Invoke-Bridge $B "teleport $($bobFarSpot -join ' ')" | Out-Null
        Start-Sleep -Seconds 5
        $cut = Wait-Until { (Get-Signal 'LinkCut') -eq 'true' } -TimeoutSeconds 10
        Assert-True ([bool]$cut) "the line is cut (D2: Alice at 400 m), rule $(Get-Signal 'Rule')"
        foreach ($player in $A, $B) { Invoke-Bridge $player 'sounds clear' | Out-Null }
        $window = Invoke-LethalDamage
        Assert-True ($window -gt 50) "rested: the full window again ($window s)"
        $distress = Wait-Until { (Get-Signal 'Rule') -eq 'distress' } -TimeoutSeconds 5 -IntervalSeconds 0.25
        Assert-True ([bool]$distress) 'the distress call gets through the cut line'
        Assert-True ([double](Get-Signal 'MinDist') -le 3 -and [double](Get-Signal 'Range') -le 1) "at full marker alpha (minDist $(Get-Signal 'MinDist'), range $(Get-Signal 'Range'))"
        Start-Sleep -Seconds 1
        # Move Alice 30 m (the bridge teleports her body): the marker stays until the next pulse, then jumps to her
        $pulses0 = [int](Get-Signal 'DistressPulses')
        $marker0 = Invoke-BridgeJson $B "get $aliceId PartnerSignal.MarkerPosition"
        Invoke-Bridge $A "teleport $($deepSpot[0] + 30) $($deepSpot[1]) $($deepSpot[2])" | Out-Null
        $start = Get-Date
        $samples = while (((Get-Date) - $start).TotalSeconds -lt 14) {
            Start-Sleep -Milliseconds 300
            $body = Invoke-BridgeJson $B "get $aliceId Transform.position"
            $marker = Invoke-BridgeJson $B "get $aliceId PartnerSignal.MarkerPosition"
            [pscustomobject]@{ t = [Math]::Round(((Get-Date) - $start).TotalSeconds, 1); pulses = [int](Get-Signal 'DistressPulses'); markerX = [double]$marker[0]; bodyX = [double]$body[0] }
        }
        $samples | ForEach-Object { Write-Host ("  t={0,4} pulses {1} marker x {2:N1} body x {3:N1}" -f $_.t, $_.pulses, $_.markerX, $_.bodyX) }
        $bodyMoved = $samples | Where-Object { [Math]::Abs($_.bodyX - $deepSpot[0] - 30) -lt 3 } | Select-Object -First 1
        Assert-True ([bool]$bodyMoved) "Bob's game moved her body (at t=$($bodyMoved.t))"
        $stale = @($samples | Where-Object { $_.pulses -eq $pulses0 -and [Math]::Abs($_.markerX - [double]$marker0[0]) -lt 1 -and $_.t -ge $bodyMoved.t })
        Assert-True ($stale.Count -ge 1) "between pulses the marker stays where it was ($($stale.Count) samples after the move)"
        $caught = $samples | Where-Object { $_.pulses -gt $pulses0 -and [Math]::Abs($_.markerX - $_.bodyX) -lt 2 } | Select-Object -First 1
        Assert-True ([bool]$caught) "the next pulse puts it on her (t=$($caught.t))"
        # The first sample at which each new pulse was seen; samples are about 0.5 s apart
        $pulseTimes = @($samples | Group-Object pulses | Where-Object { [int]$_.Name -gt $pulses0 } | ForEach-Object { $_.Group[0].t })
        $gaps = @(for ($i = 1; $i -lt $pulseTimes.Count; $i++) { $pulseTimes[$i] - $pulseTimes[$i - 1] })
        Assert-True ($pulseTimes.Count -ge 2 -and @($gaps | Where-Object { $_ -lt 4 -or $_ -gt 6.5 }).Count -eq 0) "one pulse every 5 s (seen at $($pulseTimes -join ', ') s)"
        $pings = @(Invoke-BridgeJson $B 'sounds 20 interface/ping' | Where-Object { $_ })
        Assert-True ($pings.Count -ge 2) "each pulse plays the ping ($($pings.Count))"
        $label = Get-Signal 'Label'
        Assert-True ($label -match '^Alice .+ \d+ s$') "the label counts down ('$label')"
    }

    Step 'D3: the window runs out at 400 m: Alice dies, the distress call ends' {
        Invoke-Bridge $A 'call GameModeUtils.DeactivateCheat NoOxygen' | Out-Null
        Set-AliceOxygen 45
        Invoke-Bridge $A 'call GameModeUtils.ActivateCheat NoOxygen' | Out-Null
        $left = [double](Get-Value $A 'RescueWindow.SecondsLeft')
        Write-Host "  waiting $([int]$left) s for the window to run out"
        $beaconsBefore = Get-DeathBeacons
        $expired = Wait-Until { (Get-RescueState) -eq 'UP' } -TimeoutSeconds ([int]$left + 8)
        Assert-True ([bool]$expired) 'the window ran out'
        Assert-True (Wait-Respawned) 'she died and respawned'
        Assert-True ((Get-DeathBeacons) -gt $beaconsBefore) 'with a death beacon'
        Assert-BobSeesAliceUp
    }

    Step 'Out of air while up: Alice drowns without going down' {
        Write-Host "  $(Invoke-Bridge 'server' 'rescue rest Alice')"
        Move-Both $shallowSpot
        Invoke-Bridge $A 'call GameModeUtils.DeactivateCheat NoOxygen' | Out-Null
        Assert-True ([bool](Get-State $A).isUnderwater) 'Alice is underwater'
        Set-AliceOxygen 0
        $script:seenDown = $false
        $dead = Wait-Until {
            if ((Get-RescueState) -ne 'UP') { $script:seenDown = $true }
            (Invoke-Bridge $A 'call Player.main.IsAlive') -eq 'false'
        } -TimeoutSeconds 40 -IntervalSeconds 0.25
        Assert-True ([bool]$dead) 'she drowned'
        Assert-True (-not $script:seenDown) 'without going down'
        Assert-True (Wait-Respawned) 'and respawned'
    }

    Step 'Out of air while down: she drowns before the window ends' {
        Move-Both $shallowSpot
        Invoke-Bridge $A 'call GameModeUtils.DeactivateCheat NoOxygen' | Out-Null
        Invoke-Bridge $A 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
        Set-AliceOxygen 3
        $window = Invoke-LethalDamage
        Assert-True ($window -gt 50) "down with a full window ($window s), 3 s of air"
        $dead = Wait-Until { (Invoke-Bridge $A 'call Player.main.IsAlive') -eq 'false' } -TimeoutSeconds 40 -IntervalSeconds 0.25
        $status = Invoke-Bridge 'server' 'rescue status' -AllowError
        Assert-True ([bool]$dead) "she drowned while down (server: $status)"
        Assert-True ($status -notmatch 'Alice: down') 'the server ended her window'
        Assert-BobSeesAliceUp
        Assert-True (Wait-Respawned) 'and respawned'
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
finally {
    foreach ($player in $A, $B) {
        try { Invoke-Bridge $player 'call GameModeUtils.ActivateCheat NoOxygen' | Out-Null } catch { }
    }
}
Complete-Scenario -Name 'T1 RescueWindow' -Keep:$Keep
