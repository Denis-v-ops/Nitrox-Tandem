<#
.SYNOPSIS
    COOP_FEATURES_SPEC R1b (upstream #2859): each player's game shows the other sitting on a bench, lying in a bed and
    climbing out of the escape pod, and neither gets stuck afterwards.

.DESCRIPTION
    Starts from the fixture tools\fixtures\s7-machine-base (a room at about (-100, -31, 251)); Alice builds a bench and a
    bed in it (Builder.BeginAsync / TryPlace / Constructable.Construct, as BaseHelpers.ps1 does for base pieces). Then:
    - Bench, -SitCycles times: Alice sits down (OnHandClick) and stands up again (Bench.ExitSittingMode, what the Exit key
      calls). Bob's copy of Alice must show the sitting pose (cinematics_enabled, bench_sit) at the seat and be held in
      the cinematic (RemotePlayer.InCinematic), then let go; Alice must leave cinematic mode every time (#2287: benches
      broke and players got stuck after the animations). Afterwards Bob's copy must follow Alice's movement again.
    - Bed: the Aurora is exploded first (Bed.GetCanSleep needs it). Alice lies down on one side; Bob's copy shows the
      lie-down pose and holds it. Bob tries the same side: he must not end up on it (the side lock stays Alice's until she
      gets up). Bob lies down on the other side, Alice's copy of Bob shows it, both sleep (server SleepManager), both get
      up and both copies let go. Alice's game must not have started a local time skip (DayNightCycle.skipTimeMode: the
      sleeper's dayNightSpeed would read 79x for thermal plants and filtration machines).
    - Escape pod: both stand in the pod, Alice climbs out of the top hatch and Bob clicks it right after: he must be
      turned away while Alice holds the pod's lock (not both on one ladder); Bob's copy of Alice shows the cinematic. Then
      Bob climbs out and Alice's copy of him shows it. The pod's hatch use is kept per player name on the server.
    Windowless games fire no animation events (TESTING.md), so a cinematic that hasn't ended after a few seconds is ended
    the way its animation event would (PlayerCinematicController.OnPlayerCinematicModeEnd).
    Before R1b (coop without #2859), Bob's copy shows none of this except the bench pose from BenchChanged.

    Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\R1b-SeeEachOtherSit.ps1
#>
param([switch]$NoLaunch, [switch]$Keep, [int]$SitCycles = 10, [string]$SaveName = 'r1b-cinematics')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'S7Helpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Get-Vector {
    param([string]$Text)
    [double[]]@([regex]::Matches($Text, '-?\d+(\.\d+)?(E[-+]?\d+)?') | Select-Object -First 3 | ForEach-Object { [double]$_.Value })
}

function Get-Distance {
    param([double[]]$P, [double[]]$Q)
    [math]::Sqrt([math]::Pow($P[0] - $Q[0], 2) + [math]::Pow($P[1] - $Q[1], 2) + [math]::Pow($P[2] - $Q[2], 2))
}

# Where a player's own body is (their game) and where the partner's game shows it
function Get-OwnPosition { param([string]$Player) Get-Vector (Invoke-Bridge $Player 'get player Transform.position') }
function Get-CopyPosition { param([string]$Viewer, [string]$PlayerId) Get-Vector (Invoke-Bridge $Viewer "get $PlayerId Transform.position") }

function Get-CopyAnim {
    param([string]$Viewer, [string]$PlayerId, [string]$Parameter)
    (Invoke-BridgeJson $Viewer "animator $PlayerId")[0].parameters.$Parameter
}

function Test-CopyInCinematic {
    param([string]$Viewer, [string]$PlayerId)
    (Invoke-Bridge $Viewer "get $PlayerId RemotePlayerIdentifier.RemotePlayer.InCinematic") -eq 'true'
}

function Test-InCinematicMode { param([string]$Player) (Invoke-Bridge $Player 'get player Player.cinematicModeActive') -eq 'true' }

# A string or enum value from "get", without the JSON quotes
function Get-Text { param([string]$Player, [string]$Path) (Invoke-Bridge $Player "get $Path").Trim('"') }

function Assert-Copy {
    param([string]$Viewer, [string]$PlayerId, [string]$Parameter, [bool]$Expected, [string]$Message, [int]$TimeoutSeconds = 8)
    $matched = Wait-Until { (Get-CopyAnim $Viewer $PlayerId $Parameter) -eq $Expected } -TimeoutSeconds $TimeoutSeconds -IntervalSeconds 0.25
    Assert-True ([bool]$matched) "$Message ($Parameter = $(Get-CopyAnim $Viewer $PlayerId $Parameter))"
}

function Assert-CopyHeld {
    param([string]$Viewer, [string]$PlayerId, [bool]$Expected, [string]$Message, [int]$TimeoutSeconds = 8)
    $matched = Wait-Until { (Test-CopyInCinematic $Viewer $PlayerId) -eq $Expected } -TimeoutSeconds $TimeoutSeconds -IntervalSeconds 0.25
    Assert-True ([bool]$matched) "$Message (InCinematic = $(Test-CopyInCinematic $Viewer $PlayerId))"
}

<#
    Lets the local cinematic of $ControllerPath (a bridge "call" path to a PlayerCinematicController) end: waits, then calls
    what its animation event would. Returns $true once the player is out of cinematic mode, or still in it but only
    because of a mode the cinematic started (sitting, sleeping) when -Stays.
#>
function Complete-Cinematic {
    param([string]$Player, [string]$ControllerPath, [int]$WaitSeconds = 4)
    $ended = Wait-Until { (Invoke-Bridge $Player "get $ControllerPath.cinematicModeActive") -eq 'false' } -TimeoutSeconds $WaitSeconds -IntervalSeconds 0.25
    if (-not $ended) {
        Invoke-Bridge $Player "call $ControllerPath.OnPlayerCinematicModeEnd" | Out-Null
        $ended = Wait-Until { (Invoke-Bridge $Player "get $ControllerPath.cinematicModeActive") -eq 'false' } -TimeoutSeconds 5 -IntervalSeconds 0.25
    }
    [bool]$ended
}

<#
    Alice builds a piece of furniture in the room and returns its NitroxId (like New-ScenarioBase in BaseHelpers.ps1).
#>
function New-Furniture {
    param([string]$Player, [string]$TechType, [double[]]$Spot, [int[]]$Yaws = @(0, 90, 180, 270), [double]$Rotation = 0)
    # Reuse one already built (-NoLaunch runs)
    $done = @(Invoke-BridgeJson $Player 'find Constructable 40' | Where-Object { $_.techType -eq $TechType -and $_.id -match '^[0-9a-f-]{36}$' -and (Invoke-Bridge $Player "get $($_.id) Constructable.constructed") -eq 'true' }) | Select-Object -First 1
    if ($done) { return $done.id }
    Invoke-Bridge $Player ('teleport ' + ($Spot -join ' ')) | Out-Null
    Invoke-Bridge $Player "call Player.main.SetCurrentSub $($S7.Base) true" | Out-Null
    $existing = @(Invoke-BridgeJson $Player 'find Constructable 40' | ForEach-Object { $_.id })
    Invoke-Bridge $Player "call Builder.BeginAsync $TechType" | Out-Null
    # The ghost's turn (the mouse wheel in game), in degrees
    Invoke-Bridge $Player "set Builder.additiveRotation $Rotation" | Out-Null
    $placeable = $false
    foreach ($yaw in $Yaws) {
        foreach ($pitch in -45, -50, -35, -65, -25) {
            Invoke-Bridge $Player "look $yaw $pitch" | Out-Null
            $placeable = Wait-Until { (Invoke-Bridge $Player 'get Builder.canPlace') -eq 'true' } -TimeoutSeconds 3 -IntervalSeconds 0.5
            if ($placeable) { break }
        }
        if ($placeable) { break }
    }
    if (-not $placeable) {
        Invoke-Bridge $Player 'call Builder.End' | Out-Null
        throw "$Player can't place $TechType in the room (Builder.canPlace stays false)"
    }
    Assert-Equal 'true' (Invoke-Bridge $Player 'call Builder.TryPlace') "$Player placed the $TechType ghost"
    $built = Wait-Until {
        $new = @(Invoke-BridgeJson $Player 'find Constructable 40' | Where-Object { $_.techType -eq $TechType -and $_.id -match '^[0-9a-f-]{36}$' -and $existing -notcontains $_.id }) | Select-Object -First 1
        if ($new) {
            Invoke-Bridge $Player "call $($new.id) Constructable.Construct" -AllowError | Out-Null
            if ((Invoke-Bridge $Player "get $($new.id) Constructable.constructed") -eq 'true') { $new.id }
        }
    } -TimeoutSeconds 40 -IntervalSeconds 0.3
    if (-not $built) { throw "$Player's $TechType wasn't built" }
    $built
}

# A spot $Distance m from the furniture along its local axis $Axis (right/forward), at the room's floor height
function Get-SpotBeside {
    param([string]$Player, [string]$Id, [string]$Axis, [double]$Distance)
    $position = Get-Vector (Invoke-Bridge $Player "get $Id Transform.position")
    $direction = Get-Vector (Invoke-Bridge $Player "get $Id Transform.$Axis")
    @(($position[0] + $direction[0] * $Distance), ($position[1] + 0.9), ($position[2] + $direction[2] * $Distance))
}

<#
    Bed.GetSide gives a side only if 0.6 m beside it is free, and the fixture's room is narrow (a bioreactor in the middle).
    Shifts the bed sideways in both games, a few centimetres at a time, until the game offers each player the side they
    stand on. Only the scenario's view of the room changes (the server keeps the built position).
#>
function Set-BedWithTwoSides {
    param([string]$BedId)
    $origin = Get-Vector (Invoke-Bridge $A "get $BedId Transform.position")
    $right = Get-Vector (Invoke-Bridge $A "get $BedId Transform.right")
    foreach ($shift in 0, 0.05, -0.05, 0.1, -0.1, 0.15, -0.15, 0.2, -0.2, 0.25, -0.25, 0.3, -0.3, 0.35, -0.35, 0.4, -0.4, 0.5, -0.5) {
        $position = @(($origin[0] + $right[0] * $shift), $origin[1], ($origin[2] + $right[2] * $shift))
        foreach ($player in $A, $B) { Invoke-Bridge $player "set $BedId Transform.position $($position -join ',')" | Out-Null }
        $sides = foreach ($side in -1.4, 1.4) {
            Enter-Room $A @(($position[0] + $right[0] * $side), ($position[1] + 1.7), ($position[2] + $right[2] * $side))
            Start-Sleep -Milliseconds 300
            (Invoke-Bridge $A "call $BedId Bed.GetSide player").Trim('"')
        }
        if ($sides[0] -ne $sides[1] -and $sides -notcontains 'None') {
            Write-Host "  bed shifted $shift m sideways: both sides free"
            return $true
        }
    }
    $false
}

function Enter-Room {
    param([string]$Player, [double[]]$Spot)
    Invoke-Bridge $Player ('teleport ' + ($Spot -join ' ')) | Out-Null
    Invoke-Bridge $Player "call Player.main.SetCurrentSub $($S7.Base) true" | Out-Null
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch -Fixture $S7.Fixture
    $script:aliceId = (Invoke-BridgeJson $A 'state').playerId
    $script:bobId = (Invoke-BridgeJson $B 'state').playerId
    $inside = Get-Vector $S7.Inside

    Step 'Alice builds a bench and a bed in the room' {
        Initialize-S7Players @($A, $B)
        # No cost, fast building (a toggle: once per game)
        if ((Invoke-Bridge $A 'get NoCostConsoleCommand.main.fastBuildCheat') -ne 'true') { Invoke-Bridge $A 'console bobthebuilder' | Out-Null }
        $bench = New-Furniture $A 'Bench' @(($inside[0] - 2.5), $inside[1], ($inside[2] - 2.5))
        $script:bedId = New-Furniture $A 'Bed1' @(($inside[0] + 2.5), $inside[1], ($inside[2] + 2.3)) -Yaws 180, 0, 90, 270        # A bench has three seats (MultiplayerBench, each an entity of its own); Alice uses the left one
        $benchPosition = Get-Vector (Invoke-Bridge $A "get $bench Transform.position")
        $script:benchId = (@(Invoke-BridgeJson $A 'find Bench 40' | Where-Object { $_.name -eq 'BenchPlaceLeft' -and (Get-Distance ([double[]]$_.position) $benchPosition) -lt 2 }) | Select-Object -First 1).id
        Assert-True ([bool](Wait-Until { (Invoke-Bridge $B "get $script:bedId Constructable.constructed") -eq 'true' -and (Invoke-Bridge $B "get $script:benchId Bench.enabled") -eq 'true' } -TimeoutSeconds 20)) "Bob's game has the bench and the bed"
        Write-Host "  bench seat $script:benchId, bed $script:bedId"
    }

    Step "Alice sits down and stands up $SitCycles times; Bob sees each" {
        $front = Get-SpotBeside $A $script:benchId 'forward' 0.9
        Enter-Room $B (Get-SpotBeside $A $script:benchId 'forward' 3)
        $stuck = 0; $notSeenSitting = 0; $notSeenAtSeat = 0; $notReleased = 0; $farthest = 0.0
        for ($cycle = 1; $cycle -le $SitCycles; $cycle++) {
            Enter-Room $A $front
            Start-Sleep -Milliseconds 400
            Invoke-Bridge $A "use $script:benchId Bench" | Out-Null
            $sitting = Wait-Until { (Invoke-Bridge $A "get $script:benchId Bench.cinematicController.cinematicModeActive") -eq 'true' -or (Invoke-Bridge $A "get $script:benchId Bench.isSitting") -eq 'true' } -TimeoutSeconds 5 -IntervalSeconds 0.2
            if (-not $sitting) { $stuck++; Write-Host "    cycle $cycle`: Alice didn't sit down" -ForegroundColor Yellow; continue }
            $seen = Wait-Until { (Test-CopyInCinematic $B $script:aliceId) -and (Get-CopyAnim $B $script:aliceId 'cinematics_enabled') -eq $true -and (Get-CopyAnim $B $script:aliceId 'bench_sit') -eq $true } -TimeoutSeconds 5 -IntervalSeconds 0.25
            if (-not $seen) { $notSeenSitting++ }
            Complete-Cinematic $A "$script:benchId Bench.cinematicController" | Out-Null
            $distance = 99.0
            Wait-Until { $script:d = Get-Distance (Get-OwnPosition $A) (Get-CopyPosition $B $script:aliceId); $script:d -lt 0.3 } -TimeoutSeconds 3 -IntervalSeconds 0.3 | Out-Null
            $distance = $script:d
            if ($distance -gt $farthest) { $farthest = $distance }
            if ($distance -gt 0.5) { $notSeenAtSeat++ }
            Invoke-Bridge $A "call $script:benchId Bench.ExitSittingMode player false" | Out-Null
            Complete-Cinematic $A "$script:benchId Bench.standUpCinematicController" | Out-Null
            if (-not (Wait-Until { -not (Test-InCinematicMode $A) } -TimeoutSeconds 5 -IntervalSeconds 0.25)) { $stuck++; Write-Host "    cycle $cycle`: Alice stays in cinematic mode" -ForegroundColor Yellow }
            if (-not (Wait-Until { -not (Test-CopyInCinematic $B $script:aliceId) -and (Get-CopyAnim $B $script:aliceId 'cinematics_enabled') -eq $false } -TimeoutSeconds 5 -IntervalSeconds 0.25)) { $notReleased++ }
        }
        Write-Host "  farthest Bob's copy was from Alice while seated: $([math]::Round($farthest, 2)) m"
        Assert-Equal 0 $stuck "Alice sat down and got up again every time ($SitCycles cycles)"
        Assert-Equal 0 $notSeenSitting "Bob saw Alice sitting every time (held in the cinematic, cinematics_enabled, bench_sit)"
        Assert-Equal 0 $notSeenAtSeat "Bob saw Alice on the seat every time (within 0.5 m of where she sits)"
        Assert-Equal 0 $notReleased "Bob's copy of Alice let go of the pose every time she got up"
        $walk = Get-SpotBeside $A $script:benchId 'forward' 2.5
        Enter-Room $A $walk
        $follows = Wait-Until { (Get-Distance (Get-OwnPosition $A) (Get-CopyPosition $B $script:aliceId)) -lt 1 } -TimeoutSeconds 8 -IntervalSeconds 0.5
        Assert-True ([bool]$follows) "Bob's copy of Alice follows her again after the last stand-up"
    }

    Step 'Alice lies down in the bed; Bob sees it and can only take the other side' {
        # The aurora command must come from a player (RequiresOrigin PLAYER)
        if ((Invoke-Bridge $A 'call CrashedShipExploder.main.IsExploded') -ne 'true') { Invoke-Bridge $A 'chat /aurora explode' | Out-Null }
        $exploded = Wait-Until { (Invoke-Bridge $A 'call CrashedShipExploder.main.IsExploded') -eq 'true' -and (Invoke-Bridge $B 'call CrashedShipExploder.main.IsExploded') -eq 'true' } -TimeoutSeconds 60 -IntervalSeconds 1
        Assert-True ([bool]$exploded) 'the Aurora has exploded in both games (beds need it)'
        Assert-True (Set-BedWithTwoSides $script:bedId) 'the bed has room on both sides'
        $script:leftSpot = Get-SpotBeside $A $script:bedId 'right' -1.4
        $script:rightSpot = Get-SpotBeside $A $script:bedId 'right' 1.4
        Enter-Room $A $script:leftSpot
        Enter-Room $B $script:rightSpot
        Start-Sleep -Milliseconds 500
        Invoke-Bridge $A "use $script:bedId Bed" | Out-Null
        $lying = Wait-Until { (Invoke-Bridge $A "get $script:bedId Bed.cinematicController.cinematicModeActive") -eq 'true' } -TimeoutSeconds 5 -IntervalSeconds 0.25
        Assert-True ([bool]$lying) 'Alice starts lying down'
        $script:aliceSide = Get-Text $A "$script:bedId Bed.cinematicController.playerViewAnimationName"
        $key = $script:aliceSide
        Assert-CopyHeld $B $script:aliceId $true "Bob's copy of Alice is held in the bed's cinematic"
        Assert-Copy $B $script:aliceId 'cinematics_enabled' $true "Bob's copy of Alice leaves the walking poses"
        Assert-Copy $B $script:aliceId $key $true "Bob sees Alice lie down"
        Complete-Cinematic $A "$script:bedId Bed.cinematicController" | Out-Null
        Assert-Equal 'Sleeping' (Get-Text $A "$script:bedId Bed.inUseMode") 'Alice lies in the bed'
        Assert-Equal 'false' (Invoke-Bridge $A 'get DayNightCycle.main.skipTimeMode') "Alice's game started no local time skip"
        Assert-CopyHeld $B $script:aliceId $true "Bob's copy of Alice still lies there once she is asleep"
        $copyDistance = Get-Distance (Get-OwnPosition $A) (Get-CopyPosition $B $script:aliceId)
        Assert-True ($copyDistance -lt 0.6) "Bob's copy of Alice lies where she lies ($([math]::Round($copyDistance, 2)) m apart)"

        # Bob on Alice's side: the game picks the side by where he stands and which side is clear
        Enter-Room $B $script:leftSpot
        Start-Sleep -Milliseconds 500
        Invoke-Bridge $B "use $script:bedId Bed" | Out-Null
        Start-Sleep -Seconds 2
        $bobLying = (Invoke-Bridge $B "get $script:bedId Bed.cinematicController.cinematicModeActive") -eq 'true'
        $bobSide = Get-Text $B "$script:bedId Bed.cinematicController.playerViewAnimationName"
        Write-Host "  Bob on Alice's side: lying down $bobLying, side $bobSide"
        Assert-True (-not ($bobLying -and $bobSide -eq $script:aliceSide)) "Bob doesn't lie down on Alice's side ($script:aliceSide)"
        if ($bobLying) {
            Complete-Cinematic $B "$script:bedId Bed.cinematicController" | Out-Null
        }
        else {
            Enter-Room $B $script:rightSpot
            Start-Sleep -Milliseconds 500
            Invoke-Bridge $B "use $script:bedId Bed" | Out-Null
            Assert-True ([bool](Wait-Until { (Invoke-Bridge $B "get $script:bedId Bed.cinematicController.cinematicModeActive") -eq 'true' } -TimeoutSeconds 5 -IntervalSeconds 0.25)) 'Bob lies down on the other side'
            Complete-Cinematic $B "$script:bedId Bed.cinematicController" | Out-Null
        }
        $bobSide = Get-Text $B "$script:bedId Bed.cinematicController.playerViewAnimationName"
        Assert-True ($bobSide -ne $script:aliceSide) "Bob lies on the other side ($bobSide)"
        Assert-CopyHeld $A $script:bobId $true "Alice's copy of Bob is held in the bed's cinematic"
        Assert-Copy $A $script:bobId 'cinematics_enabled' $true "Alice sees Bob lie down"
        Assert-CopyHeld $B $script:aliceId $true "Bob's copy of Alice still lies on her side"
    }

    Step 'Both sleep; both get up and both copies let go' {
        $up = Wait-Until { (Get-Text $A "$script:bedId Bed.inUseMode") -eq 'None' -and (Get-Text $B "$script:bedId Bed.inUseMode") -eq 'None' } -TimeoutSeconds 30 -IntervalSeconds 0.5
        Assert-True ([bool]$up) 'the sleep ended for both (server SleepManager, SleepComplete)'
        foreach ($player in $A, $B) {
            Complete-Cinematic $player "$script:bedId Bed.currentStandUpCinematicController" | Out-Null
            Assert-True ([bool](Wait-Until { -not (Test-InCinematicMode $player) } -TimeoutSeconds 6 -IntervalSeconds 0.25)) "$player is out of cinematic mode after getting up"
        }
        Assert-CopyHeld $B $script:aliceId $false "Bob's copy of Alice got up"
        Assert-CopyHeld $A $script:bobId $false "Alice's copy of Bob got up"
        Assert-Copy $B $script:aliceId 'cinematics_enabled' $false "Bob's copy of Alice is back in the walking poses"
        Enter-Room $A $script:rightSpot
        $follows = Wait-Until { (Get-Distance (Get-OwnPosition $A) (Get-CopyPosition $B $script:aliceId)) -lt 1 } -TimeoutSeconds 8 -IntervalSeconds 0.5
        Assert-True ([bool]$follows) "Bob's copy of Alice follows her again after the bed"
    }

    Step 'Escape pod: Alice climbs out of the top hatch, Bob is turned away until she is through, then climbs out' {
        $podId = (Invoke-BridgeJson $A 'find EscapePod 1')[0].id
        $script:podId = $podId
        $triggers = @(Invoke-BridgeJson $A 'find CinematicModeTrigger 20' | Where-Object { $_.id -eq "(in $podId)" })
        Write-Host ('  pod triggers: ' + (($triggers | ForEach-Object { $_.name }) -join ', '))
        $podPosition = Get-Vector (Invoke-Bridge $A "get $podId Transform.position")
        foreach ($player in $A, $B) {
            Invoke-Bridge $player ('teleport ' + (@($podPosition[0], ($podPosition[1] + 0.5), $podPosition[2]) -join ' ')) | Out-Null
            Invoke-Bridge $player "call Player.main.SetCurrentSub null true" -AllowError | Out-Null
        }
        Start-Sleep -Seconds 1
        $top = "$podId EscapePodFirstUseCinematicsController.topFirstUseCinematicTarget"
        if ((Invoke-Bridge $A "get $top.gameObject.activeSelf") -ne 'true') { $top = "$podId EscapePodFirstUseCinematicsController.topCinematicTarget" }
        $topName = Get-Text $A "$top.gameObject.name"
        Invoke-Bridge $A "use find:CinematicModeTrigger#$topName" | Out-Null
        Invoke-Bridge $B "use find:CinematicModeTrigger#$topName" | Out-Null
        $aliceClimbs = Wait-Until { Test-InCinematicMode $A } -TimeoutSeconds 5 -IntervalSeconds 0.2
        Assert-True ([bool]$aliceClimbs) "Alice climbs the ladder ($topName)"
        Start-Sleep -Seconds 1
        Assert-True (-not (Test-InCinematicMode $B)) 'Bob is turned away while Alice is on the ladder'
        Assert-CopyHeld $B $script:aliceId $true "Bob's copy of Alice is in the hatch cinematic"
        Assert-Copy $B $script:aliceId 'cinematics_enabled' $true "Bob sees Alice climb"
        Complete-Cinematic $A "$top.cinematicController" | Out-Null
        Assert-True ([bool](Wait-Until { -not (Test-InCinematicMode $A) } -TimeoutSeconds 6 -IntervalSeconds 0.25)) 'Alice is out of cinematic mode on the roof'
        Assert-CopyHeld $B $script:aliceId $false "Bob's copy of Alice lets go after the ladder"

        # Bob's own pod still has his first use ahead of him if Alice's was the first use (per player)
        $bobTop = "$podId EscapePodFirstUseCinematicsController.topFirstUseCinematicTarget"
        if ((Invoke-Bridge $B "get $bobTop.gameObject.activeSelf") -ne 'true') { $bobTop = "$podId EscapePodFirstUseCinematicsController.topCinematicTarget" }
        $bobTopName = Get-Text $B "$bobTop.gameObject.name"
        Start-Sleep -Milliseconds 500
        Invoke-Bridge $B "use find:CinematicModeTrigger#$bobTopName" | Out-Null
        Assert-True ([bool](Wait-Until { Test-InCinematicMode $B } -TimeoutSeconds 5 -IntervalSeconds 0.2)) "Bob climbs the ladder once Alice is through ($bobTopName)"
        Assert-CopyHeld $A $script:bobId $true "Alice's copy of Bob is in the hatch cinematic"
        Complete-Cinematic $B "$bobTop.cinematicController" | Out-Null
        Assert-True ([bool](Wait-Until { -not (Test-InCinematicMode $B) } -TimeoutSeconds 6 -IntervalSeconds 0.25)) 'Bob is out of cinematic mode on the roof'
        Assert-CopyHeld $A $script:bobId $false "Alice's copy of Bob lets go after the ladder"
        $query = Wait-Until { $q = Invoke-Bridge 'server' "query $podId"; if ($q -match 'PlayersWithTopHatchUsed') { $q } } -TimeoutSeconds 10
        Write-Host "  server: $(($query -split "`n" | Where-Object { $_ -match 'HatchUsed|EscapePodMetadata' }) -join ' | ')"
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'R1b SeeEachOtherSit' -Keep:$Keep
