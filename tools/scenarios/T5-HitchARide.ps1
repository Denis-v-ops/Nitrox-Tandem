<#
.SYNOPSIS
    COOP_FEATURES_SPEC T5: Seamoth hitch-a-ride. Alice pilots a Seamoth; Bob holds on to its hull and rides along outside,
    still a swimmer whose air drains, and lets go by key, when he goes down, when Alice gets out, when it docks or is
    destroyed.

.DESCRIPTION
    1. Alice spawns a Seamoth in open water and gets in; Bob swims to its left side. His reticle on the hull offers "hold
       on"; a click (bridge hold on the Seamoth, GUIHand's own targeting) asks the server, and Bob holds on: Normal mode,
       his motor off, no interaction (no tools). In Alice's game Bob's body hangs on the hitch handle.
    2. Bob's no-oxygen cheat off. Alice drives about 200 m (movement input, real Seamoth physics; teleport steps if the
       input doesn't move it). Sampled every second: Bob stays on the handle in his own game and in Alice's, both games
       agree where he is. Afterwards: Bob still underwater, his air went down.
    3. Exit key (bridge hold none Exit): Bob lets go where he is, his motor is back; Alice's game takes the body off the
       handle and puts it where Bob is.
    4. Hold on again, Alice gets out (Exit key): Bob lets go in both games.
    5. Hold on again, Alice docks the Seamoth in a Cyclops: Bob lets go before the bay takes it, and isn't in the Cyclops.
    6. Hold on again, the Seamoth is destroyed (LiveMixin.Kill in Alice's game, which simulates it): Bob lets go, his
       player and his body in Alice's game survive.
    7. Hold on again, Bob takes lethal damage (no-damage cheat off): he goes down (T1) and lets go; Alice's game too.

    Negative control: on a build without T5 there is no hitch, so step 1 fails (no "hold on", Bob never holds on) and
    the rest can't run.

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep,
    # Open water over the Grassy Plateaus (as S6), deep enough for a Cyclops.
    [double[]]$Area = @(263, -45, 64),
    [double]$DriveMeters = 200)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$inv = [Globalization.CultureInfo]::InvariantCulture

function Get-State { param([string]$Player) Invoke-BridgeJson $Player 'state' }
function Get-Value { param([string]$Player, [string]$Path) (Invoke-Bridge $Player "get $Path" -AllowError).Trim('"') }
function Get-EntityPosition { param([string]$Player, [string]$Id) [double[]](Invoke-BridgeJson $Player "entity $Id").position }
function Get-Distance { param([double[]]$P, [double[]]$Q) [Math]::Sqrt([Math]::Pow($P[0] - $Q[0], 2) + [Math]::Pow($P[1] - $Q[1], 2) + [Math]::Pow($P[2] - $Q[2], 2)) }
function Format-Position { param([double[]]$P) ($P | ForEach-Object { $_.ToString('0.###', $inv) }) -join ' ' }
function Format-Vector3Arg { param([double[]]$P) ($P | ForEach-Object { $_.ToString('0.###', $inv) }) -join ',' }

function Get-BobHitchedIn { param([string]$Player) if ($Player -eq $B) { Get-Value $B 'SeamothHitch.LocalVehicleId' } else { Get-Value $Player "$bobId RemotePlayerIdentifier.RemotePlayer.IsHitched" } }

<#
    The hitch handle's world position in that game (it exists once someone held on there).
#>
function Get-HandlePosition {
    param([string]$Player)
    # The handle has no id: compute it from the Seamoth's pose instead (same constant as SeamothHitch.HandleLocalPosition)
    $world = Invoke-Bridge $Player "call $script:seamothId Transform.TransformPoint $(Format-Vector3Arg $script:handleLocal)"
    if ($world -match '\(?\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)') { return [double[]]@([double]$Matches[1], [double]$Matches[2], [double]$Matches[3]) }
    throw "can't read the handle position in $Player's game ($world)"
}

function Get-BobPosition {
    param([string]$Player)
    if ($Player -eq $B) { [double[]](Get-State $B).position } else { Get-EntityPosition $A $bobId }
}

<#
    Alice spawns a Seamoth near the area and gets in. Bob swims to its left side.
#>
function New-PilotedSeamoth {
    Invoke-Bridge $A "teleport $(Format-Position $Area)" | Out-Null
    Invoke-Bridge $A 'look 0 0' | Out-Null
    Start-Sleep -Seconds 1
    $before = @((Invoke-BridgeJson $A 'find SeaMoth 200') | ForEach-Object { $_.id })
    Invoke-Bridge $A 'console spawn seamoth 1 6' | Out-Null
    $seamoth = Wait-Until { (Invoke-BridgeJson $A 'find SeaMoth 200') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
    if (-not $seamoth) { throw "the Seamoth didn't spawn" }
    $script:seamothId = $seamoth.id
    Write-Host "  Seamoth $seamothId"
    $p = Get-EntityPosition $A $seamothId
    Invoke-Bridge $A "teleport $(Format-Position @($p[0], ($p[1] + 2), $p[2]))" | Out-Null
    Start-Sleep -Milliseconds 500
    Invoke-Bridge $A "use $seamothId" | Out-Null
    $inside = Wait-Until { (Get-State $A).vehicle } -TimeoutSeconds 10 -IntervalSeconds 0.3
    Assert-True ([bool]$inside) "$A pilots the Seamoth"
    $seen = Wait-Until { Get-EntityPosition $B $seamothId } -TimeoutSeconds 20
    Assert-True ([bool]$seen) "$B's game has the Seamoth"
}

<#
    Bob next to the Seamoth's left side, then a click on its hull (GUIHand targets it, OnHandClick runs). Returns once
    he holds on in his own game and in Alice's.
#>
function Invoke-HoldOn {
    param([string]$When)
    $handle = Get-HandlePosition $B
    Invoke-Bridge $B "teleport $(Format-Position @(($handle[0]), ($handle[1] + 0.3), ($handle[2])))" | Out-Null
    Start-Sleep -Seconds 1
    Invoke-Bridge $B "hold $seamothId 0.3" | Out-Null
    $held = Wait-Until { $v = Get-BobHitchedIn $B; if ($v -and $v -ne 'null' -and $v -ne '') { $v } } -TimeoutSeconds 8 -IntervalSeconds 0.3
    $status = Invoke-BridgeJson $B 'hold status'
    if (-not $held) {
        # The aim may miss the hull from where he floats; the click itself (as the bridge's use) is the same entry point
        Write-Host "  ($When) the click didn't reach the hull: $($status | ConvertTo-Json -Compress -Depth 3); using OnHandClick"
        Invoke-Bridge $B "use $seamothId" | Out-Null
        $held = Wait-Until { $v = Get-BobHitchedIn $B; if ($v -and $v -ne 'null' -and $v -ne '') { $v } } -TimeoutSeconds 8 -IntervalSeconds 0.3
    }
    Assert-True ($held -eq $seamothId) "$When : $B holds on to the Seamoth ($held)"
    $inAlice = Wait-Until { if ((Get-BobHitchedIn $A) -eq 'true') { 'yes' } } -TimeoutSeconds 8 -IntervalSeconds 0.3
    Assert-True ([bool]$inAlice) "$When : in $A's game $B's body hangs on the Seamoth"
    $status
}

<#
    Bob doesn't hold on in either game; returns his position in his game.
#>
function Assert-LetGo {
    param([string]$When, [string]$Reason)
    $free = Wait-Until { $v = Get-BobHitchedIn $B; if ($v -eq 'null' -or $v -eq '') { 'yes' } } -TimeoutSeconds 10 -IntervalSeconds 0.3
    Assert-True ([bool]$free) "$When : $B let go in his game"
    if ($Reason) {
        $actual = Get-Value $B 'SeamothHitch.LastReleaseReason'
        Assert-True ($actual -like "*$Reason*") "$When : $B's game says why ('$actual')"
    }
    $freeInA = Wait-Until { if ((Get-BobHitchedIn $A) -eq 'false') { 'yes' } } -TimeoutSeconds 10 -IntervalSeconds 0.3
    Assert-True ([bool]$freeInA) "$When : $A's game took $B's body off the Seamoth"
}

function Assert-BobFreeToSwim {
    param([string]$When)
    Assert-Equal 'Normal' (Get-Value $B 'Player.main.mode') "$When : $B is in Normal mode"
    Assert-Equal 'true' (Get-Value $B 'Player.main.playerController.enabled') "$When : $B's motor is back"
    Assert-Equal 'true' (Invoke-Bridge $B 'call Player.main.IsFreeToInteract') "$When : $B can interact again"
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch
    $script:bobId = (Get-State $B).playerId
    $script:handleLocal = @(-1.9, 0.1, -0.3)

    Step 'Alice pilots a Seamoth; Bob sees "hold on" on its hull and holds on' {
        New-PilotedSeamoth
        $p = Get-EntityPosition $B $seamothId
        Invoke-Bridge $B "teleport $(Format-Position @(($p[0] - 3), $p[1], $p[2]))" | Out-Null
        Start-Sleep -Seconds 1
        # Aim at the hull without clicking: the reticle offers "hold on" (the vanilla "enter" would be refused anyway)
        $before = [double](Get-Value $B 'SeamothHitch.LastOfferTime')
        Invoke-Bridge $B "hold $seamothId 0.5 None" | Out-Null
        Start-Sleep -Seconds 1
        $after = [double](Get-Value $B 'SeamothHitch.LastOfferTime')
        $status = Invoke-BridgeJson $B 'hold status'
        Write-Host "  hover: $($status | ConvertTo-Json -Compress -Depth 3)"
        Assert-True ($after -gt $before) "$B's reticle on the hull offers to hold on ($before -> $after)"

        $status = Invoke-HoldOn 'first'
        Assert-Equal 'Normal' (Get-Value $B 'Player.main.mode') "$B stays a swimmer (Normal mode, not the pilot's locked mode)"
        Assert-Equal 'false' (Get-Value $B 'Player.main.playerController.enabled') "$B's motor is off"
        Assert-Equal 'false' (Invoke-Bridge $B 'call Player.main.IsFreeToInteract') "$B can't use tools or interact while holding on"
        Assert-Equal 'true' ([string][bool](Get-State $B).isUnderwater).ToLower() "$B is underwater"
        Start-Sleep -Seconds 1
        $bobAtHandle = Get-Distance (Get-BobPosition $B) (Get-HandlePosition $B)
        Assert-True ($bobAtHandle -lt 0.2) ("in $B's game he is on the handle ({0:N2} m)" -f $bobAtHandle)
        $bodyAtHandle = Get-Distance (Get-BobPosition $A) (Get-HandlePosition $A)
        Assert-True ($bodyAtHandle -lt 0.2) ("in $A's game his body is on the handle ({0:N2} m)" -f $bodyAtHandle)
        $hull = Get-Distance (Get-BobPosition $A) (Get-EntityPosition $A $seamothId)
        Write-Host ("  body {0:N2} m from the Seamoth's origin" -f $hull)
    }

    Step "Alice drives about $DriveMeters m; Bob rides along in both games and his air drains" {
        Invoke-Bridge $B 'call GameModeUtils.DeactivateCheat NoOxygen' | Out-Null
        try {
            $o0 = [double](Get-State $B).oxygen
            $t0 = Get-Date
            $start = Get-EntityPosition $A $seamothId
            $bobStart = Get-BobPosition $B
            $samples = New-Object System.Collections.ArrayList
            Invoke-Bridge $A 'input move 0 0 1' | Out-Null
            $usedSteps = $false
            $moved = 0
            for ($i = 0; $i -lt 60 -and $moved -lt $DriveMeters; $i++) {
                Start-Sleep -Seconds 1
                $now = Get-EntityPosition $A $seamothId
                $moved = Get-Distance $start $now
                if ($i -eq 4 -and $moved -lt 20) {
                    # The input didn't drive it (windowless input or no power): move it in steps as the pilot's game
                    Write-Host ("  movement input moved the Seamoth only {0:N1} m in 5 s: driving it in 4 m steps instead" -f $moved)
                    Invoke-Bridge $A 'input off' | Out-Null
                    $usedSteps = $true
                }
                if ($usedSteps) {
                    for ($k = 0; $k -lt 4; $k++) {
                        $now = Get-EntityPosition $A $seamothId
                        Invoke-Bridge $A "set $seamothId Transform.position $(Format-Vector3Arg @(($now[0] + 0), ($now[1]), ($now[2] + 4)))" | Out-Null
                        Start-Sleep -Milliseconds 200
                    }
                    $now = Get-EntityPosition $A $seamothId
                    $moved = Get-Distance $start $now
                }
                $sample = [ordered]@{
                    moved   = [Math]::Round($moved, 1)
                    bobOwn  = [Math]::Round((Get-Distance (Get-BobPosition $B) (Get-HandlePosition $B)), 2)
                    bodyA   = [Math]::Round((Get-Distance (Get-BobPosition $A) (Get-HandlePosition $A)), 2)
                    games   = [Math]::Round((Get-Distance (Get-BobPosition $A) (Get-BobPosition $B)), 2)
                }
                [void]$samples.Add($sample)
            }
            Invoke-Bridge $A 'input off' | Out-Null
            Start-Sleep -Seconds 2
            $t1 = Get-Date
            $o1 = [double](Get-State $B).oxygen
            $bobEnd = Get-BobPosition $B
            $samples | ForEach-Object { Write-Host "    $($_ | ConvertTo-Json -Compress)" }
            $travel = Get-Distance $bobStart $bobEnd
            Assert-True ($moved -ge $DriveMeters * 0.9) ("the Seamoth went {0:N0} m in {1}" -f $moved, $(if ($usedSteps) { 'steps' } else { 'its own physics' }))
            Assert-True ($travel -ge $DriveMeters * 0.9) ("$B went along in his game ({0:N0} m)" -f $travel)
            $worstOwn = ($samples | Measure-Object -Property bobOwn -Maximum).Maximum
            $worstA = ($samples | Measure-Object -Property bodyA -Maximum).Maximum
            $worstGames = ($samples | Measure-Object -Property games -Maximum).Maximum
            Assert-True ($worstOwn -lt 0.5) "in $B's game he stayed on the handle the whole way (worst $worstOwn m)"
            Assert-True ($worstA -lt 0.5) "in $A's game his body stayed on the handle the whole way (worst $worstA m)"
            Write-Host "  where the two games had Bob, worst gap while moving: $worstGames m (the Seamoth's own replication delay)"
            $settled = Get-Distance (Get-BobPosition $A) (Get-BobPosition $B)
            Assert-True ($settled -lt 1.5) ("once the Seamoth stopped, both games have $B in the same place ({0:N2} m)" -f $settled)
            Assert-Equal 'true' ([string][bool](Get-State $B).isUnderwater).ToLower() "$B is still underwater"
            $seconds = ($t1 - $t0).TotalSeconds
            Write-Host ("  {0}'s air {1} -> {2} in {3:N0} s" -f $B, $o0, $o1, $seconds)
            Assert-True ($o1 -lt $o0 - $seconds * 0.5) ("$B's air drained while holding on ({0} -> {1} in {2:N0} s)" -f $o0, $o1, $seconds)
            $serverBob = (Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $B }
            Write-Host "  server: $($serverBob | ConvertTo-Json -Compress -Depth 3)"
        }
        finally {
            Invoke-Bridge $A 'input off' -AllowError | Out-Null
            Invoke-Bridge $B 'call GameModeUtils.ActivateCheat NoOxygen' | Out-Null
        }
        Assert-NoDiff -Ids @($seamothId) 'no desync for the Seamoth'
    }

    Step 'Bob lets go with the Exit key' {
        Invoke-Bridge $B 'hold none 0.2 Exit' | Out-Null
        Assert-LetGo 'Exit key' 'let go'
        Assert-BobFreeToSwim 'Exit key'
        Start-Sleep -Seconds 2
        $gap = Get-Distance (Get-BobPosition $A) (Get-BobPosition $B)
        Assert-True ($gap -lt 2) ("$A's game has $B where he let go ({0:N2} m)" -f $gap)
    }

    Step 'Bob holds on again; Alice gets out: Bob lets go' {
        Invoke-HoldOn 'pilot out' | Out-Null
        Invoke-Bridge $A 'hold none 0.2 Exit' | Out-Null
        $out = Wait-Until { -not (Get-State $A).vehicle } -TimeoutSeconds 8 -IntervalSeconds 0.3
        if (-not $out) {
            Write-Host "  the Exit key didn't get Alice out; ToNormalMode instead"
            Invoke-Bridge $A 'call player Player.ToNormalMode true' | Out-Null
            $out = Wait-Until { -not (Get-State $A).vehicle } -TimeoutSeconds 8 -IntervalSeconds 0.3
        }
        Assert-True ([bool]$out) "$A got out of the Seamoth"
        Assert-LetGo 'pilot out' ''
        Write-Host "  reason: $(Get-Value $B 'SeamothHitch.LastReleaseReason')"
        Assert-BobFreeToSwim 'pilot out'
    }

    Step 'Bob holds on; Alice docks the Seamoth in a Cyclops: Bob lets go outside the bay' {
        # The Cyclops near the area, then Alice back in the Seamoth
        $p = Get-EntityPosition $A $seamothId
        Invoke-Bridge $A "teleport $(Format-Position @(($p[0] - 30), ($p[1] - 5), $p[2]))" | Out-Null
        Start-Sleep -Seconds 1
        $before = @((Invoke-BridgeJson $A 'find SubRoot 50') | ForEach-Object { $_.id })
        Invoke-Bridge $A 'console sub cyclops' | Out-Null
        $cyclops = Wait-Until { (Invoke-BridgeJson $A 'find SubRoot 50') | Where-Object { $_.name -like 'Cyclops*' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 30
        Assert-True ([bool]$cyclops) 'a Cyclops spawned'
        $script:cyclopsId = $cyclops.id
        Start-Sleep -Seconds 3
        $bay = (Invoke-BridgeJson $A 'find VehicleDockingBay 60') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' } | Select-Object -First 1
        $bayId = $bay.id
        $b = [double[]]$bay.position
        Invoke-Bridge $A "set $seamothId Transform.position $(Format-Vector3Arg @(($b[0] + 12), ($b[1] - 8), $b[2]))" | Out-Null
        Start-Sleep -Seconds 1
        $p = Get-EntityPosition $A $seamothId
        Invoke-Bridge $A "teleport $(Format-Position @($p[0], ($p[1] + 2), $p[2]))" | Out-Null
        Start-Sleep -Milliseconds 500
        Invoke-Bridge $A "use $seamothId" | Out-Null
        Assert-True ([bool](Wait-Until { (Get-State $A).vehicle } -TimeoutSeconds 10 -IntervalSeconds 0.3)) "$A is back in the Seamoth"
        Start-Sleep -Seconds 2
        Invoke-HoldOn 'dock' | Out-Null

        $openedHash = -1386862757    # Animator.StringToHash("opened")
        $docking = $null
        for ($try = 1; $try -le 3 -and -not $docking; $try++) {
            $b = [double[]]((Invoke-BridgeJson $A 'find VehicleDockingBay 60') | Where-Object { $_.id -eq $bayId }).position
            Invoke-Bridge $A "set $seamothId Transform.position $(Format-Vector3Arg @($b[0], ($b[1] - 25), $b[2]))" | Out-Null
            Start-Sleep -Seconds 2
            Invoke-Bridge $A "set $seamothId Transform.position $(Format-Vector3Arg @($b[0], ($b[1] - 8), $b[2]))" | Out-Null
            Wait-Until { (Invoke-Bridge $A "get $bayId VehicleDockingBay.nearbyVehicle") -ne 'null' } -TimeoutSeconds 8 | Out-Null
            Wait-Until { ((Invoke-BridgeJson $A "animator $cyclopsId all") | Where-Object { $_.object -eq 'cyclops_launch_bay_launch_cin' } | Select-Object -First 1).layers[0].stateHash -eq $openedHash } -TimeoutSeconds 8 -IntervalSeconds 0.5 | Out-Null
            Start-Sleep -Seconds 1
            Invoke-Bridge $A "set $seamothId Transform.position $(Format-Vector3Arg @($b[0], ($b[1] - 0.4), $b[2]))" | Out-Null
            $docking = Wait-Until { if ((Invoke-Bridge $A "get $bayId VehicleDockingBay.interpolatingVehicle") -ne 'null' -or (Get-Value $A "$seamothId Vehicle.docked") -eq 'true') { 'yes' } } -TimeoutSeconds 10 -IntervalSeconds 0.3
            if (-not $docking) { Write-Host "  docking try $try didn't start" }
        }
        Assert-True ([bool]$docking) 'the Seamoth docks in the Cyclops'
        Assert-LetGo 'dock' 'dock'
        Assert-BobFreeToSwim 'dock'
        Start-Sleep -Seconds 4
        $bob = Get-State $B
        Assert-True (-not $bob.currentSub) "$B isn't taken into the Cyclops (currentSub '$($bob.currentSub.name)')"
        $docked = Get-Value $A "$seamothId Vehicle.docked"
        Write-Host "  Seamoth docked: $docked; Bob at $(Format-Vector ([double[]]$bob.position)), the bay at $(Format-Vector $b)"
        # Undock for the next steps: Alice from inside
        Invoke-Bridge $A 'use find:DockedVehicleHandTarget' | Out-Null
        $undocked = Wait-Until { $s = Get-State $A; -not $s.currentSub -and $s.vehicle } -TimeoutSeconds 30 -IntervalSeconds 0.5
        Assert-True ([bool]$undocked) "$A undocked and pilots the Seamoth again"
        Start-Sleep -Seconds 3
    }

    Step 'Bob holds on; the Seamoth is destroyed: Bob lets go, his player and his body survive' {
        if (-not (Get-State $A).vehicle) {
            $p = Get-EntityPosition $A $seamothId
            Invoke-Bridge $A "teleport $(Format-Position @($p[0], ($p[1] + 2), $p[2]))" | Out-Null
            Invoke-Bridge $A "use $seamothId" | Out-Null
            Wait-Until { (Get-State $A).vehicle } -TimeoutSeconds 10 -IntervalSeconds 0.3 | Out-Null
        }
        # Away from the Cyclops, so the Seamoth doesn't dock again
        $p = Get-EntityPosition $A $seamothId
        Invoke-Bridge $A "set $seamothId Transform.position $(Format-Vector3Arg @(($p[0] + 40), ($p[1] - 5), $p[2]))" | Out-Null
        Start-Sleep -Seconds 2
        Invoke-HoldOn 'destroyed' | Out-Null
        Invoke-Bridge $A "call $seamothId LiveMixin.Kill" | Out-Null
        $gone = Wait-Until { -not ((Invoke-BridgeJson $B 'find SeaMoth 200') | Where-Object { $_.id -eq $seamothId }) } -TimeoutSeconds 10 -IntervalSeconds 0.5
        Assert-True ([bool]$gone) "the Seamoth is gone in $B's game"
        Assert-LetGo 'destroyed' ''
        Write-Host "  reason: $(Get-Value $B 'SeamothHitch.LastReleaseReason')"
        Assert-BobFreeToSwim 'destroyed'
        Assert-True ([bool](Get-State $B).position) "$B's player is still there"
        $body = Wait-Until { Get-EntityPosition $A $bobId } -TimeoutSeconds 5
        Assert-True ([bool]$body) "$B's body still exists in $A's game"
        Assert-Equal 'true' (Get-Value $A 'Player.main.liveMixin.IsAlive') "$A survived her Seamoth"
    }

    Step 'Bob holds on to a new Seamoth and goes down: he lets go' {
        New-PilotedSeamoth
        Invoke-HoldOn 'downed' | Out-Null
        Invoke-Bridge $B 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
        try {
            Invoke-Bridge $B 'call player LiveMixin.TakeDamage 500' | Out-Null
            $down = Wait-Until { $s = Get-Value $B 'RescueWindow.State'; if ($s -eq 'DOWN') { $s } } -TimeoutSeconds 10 -IntervalSeconds 0.3
            Assert-True ([bool]$down) "$B is down (T1)"
            Assert-LetGo 'downed' 'down'
            Assert-Equal 'false' (Get-Value $B 'Player.main.playerController.enabled') "$B's motor stays off while down (T1's lock, not the hitch's)"
            Assert-Equal 'true' (Get-Value $B 'Player.main.liveMixin.IsAlive') "$B is alive (down)"
        }
        finally {
            Invoke-Bridge $B 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'T5 HitchARide' -Keep:$Keep
