<#
.SYNOPSIS
    FIX_SPEC S6 (#2306, #2712): players in dry precursor rooms must walk, not swim: after a warp to a partner standing
    in one, after /back, after both relog there, and a Cyclops nearby must not flip the precursor water state.

.DESCRIPTION
    Dry precursor rooms only exist through Player.precursorOutOfWater, which trigger volumes set
    (PrecursorDoorMotorModeSetter, PrecursorMoonPoolTrigger) and OnPlayerPositionCheat clears.

    1. Alice enters the Primary Containment Facility from its moon pool and walks through the main door's
       DoorSetMotorModeCollider_Walk (the trigger's real OnTriggerEnter with her collider; a teleport skips triggers).
    2. Bob warps to her with the server's "warp Bob Alice": Bob must be out of water too.
    3. Bob uses /back (out of water again) and /warp Alice.
    4. Both relog (server save, games closed, same save started again): both still out of water, the server agrees.
    5. Both go into a Cyclops near the Quarantine Enforcement Platform. In Alice's game, the platform's moon pool trigger
       and a ForceWalk door trigger are touched by Bob's body (a child of the Cyclops there): Alice's state must not
       change, and once she leaves the Cyclops she must swim in the open water.

    Checked: precursorOutOfWater, IsUnderwater, motor mode (not Dive/Seaglide), server InPrecursor.
    Starts a fresh world (save s6-precursor) and two games (opens windows), unless -NoLaunch. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's6-precursor', [switch]$SkipCyclops)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$inv = [Globalization.CultureInfo]::InvariantCulture
# Primary Containment Facility (game coordinates read through the bridge): above the water of its moon pool, and the
# dry corridor between the moon pool room and the main door (the Walk side of DoorSetMotorModeCollider_Walk).
$moonPoolAboveWater = '318.5 -1445 -443.9'
$dryCorridor = '226,-1451.6,-280'
$moonPoolTrigger = 'find:PrecursorMoonPoolTrigger'
$walkDoorTrigger = 'find:PrecursorDoorMotorModeSetter#DoorSetMotorModeCollider_Walk'

function Get-State {
    param([string]$Player)
    Invoke-BridgeJson $Player 'state'
}

function Get-ServerInPrecursor {
    param([string]$Player)
    $id = (Get-State $Player).playerId
    if ((Invoke-Bridge 'server' "query $id") -match 'InPrecursor: (\w+)') { $Matches[1] } else { '?' }
}

function Get-WaterState {
    param([string]$Player)
    $state = Get-State $Player
    [pscustomobject]@{
        Position = Format-Vector ([double[]]$state.position)
        OutOfWater = [bool]$state.precursorOutOfWater
        Underwater = [bool]$state.isUnderwater
        Motor = (Invoke-Bridge $Player 'get player Player.motorMode').Trim('"')
        Cinematic = (Invoke-Bridge $Player 'get player Player.cinematicModeActive') -eq 'true'
    }
}

<#
    Out of water in a dry precursor room: flag set, not underwater, walking/running (not diving), server agrees.
#>
function Assert-Walking {
    param([string]$Player, [string]$When)
    $ok = Wait-Until { $s = Get-WaterState $Player; if (-not $s.Cinematic -and $s.OutOfWater -and -not $s.Underwater -and $s.Motor -notin 'Dive', 'Seaglide') { $s } } -TimeoutSeconds 15 -IntervalSeconds 0.5
    $s = Get-WaterState $Player
    Assert-True ([bool]$ok) "$When : $Player walks in the dry precursor room (at $($s.Position): precursorOutOfWater $($s.OutOfWater), underwater $($s.Underwater), motor $($s.Motor))"
    $server = Wait-Until { if ((Get-ServerInPrecursor $Player) -eq 'True') { 'True' } } -TimeoutSeconds 5 -IntervalSeconds 0.5
    Assert-True ([bool]$server) "$When : the server has InPrecursor true for $Player"
}

function Test-Near {
    param([string]$Player, [string]$Other)
    (Get-Distance ([double[]](Get-State $Player).position) ([double[]](Get-State $Other).position)) -lt 3
}

function Wait-Loaded {
    param([string]$Player, [string]$Target)
    Wait-Until { Invoke-Bridge $Player "get $Target GameObject.name" } -TimeoutSeconds 60
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'Alice walks into the Primary Containment Facility' {
        Invoke-Bridge $A "teleport $moonPoolAboveWater" | Out-Null
        Assert-True ([bool](Wait-Loaded $A $moonPoolTrigger)) 'the moon pool trigger is loaded'
        Start-Sleep -Seconds 2
        # SetPosition, not teleport: it keeps the moon pool's hold on her, like walking out of it would until the exit.
        Invoke-Bridge $A "call player Player.SetPosition $dryCorridor" | Out-Null
        # The main door is about 200 m from the moon pool and only loads near it.
        Assert-True ([bool](Wait-Loaded $A $walkDoorTrigger)) 'the main door trigger is loaded'
        # What walking would do: leave the moon pool volume, then pass the door's Walk volume.
        Invoke-Bridge $A "call $moonPoolTrigger PrecursorMoonPoolTrigger.OnTriggerExit player" | Out-Null
        Invoke-Bridge $A "call $walkDoorTrigger PrecursorDoorMotorModeSetter.OnTriggerEnter player" | Out-Null
        Assert-Walking $A 'after the door'
    }

    Step 'Bob warps to Alice (server warp)' {
        Invoke-Bridge 'server' "warp $B $A" | Out-Null
        $arrived = Wait-Until { (Test-Near $B $A) -and -not (Get-WaterState $B).Cinematic } -TimeoutSeconds 30
        Assert-True ([bool]$arrived) "$B arrived where $A stands"
        Assert-Walking $B 'after the warp'
    }

    Step 'Bob goes back (/back) and warps again (/warp Alice)' {
        Invoke-Bridge $B 'chat /back' | Out-Null
        $back = Wait-Until { -not (Test-Near $B $A) -and -not (Get-WaterState $B).OutOfWater } -TimeoutSeconds 30
        Assert-True ([bool]$back) "$B is back outside, precursorOutOfWater false"
        Assert-Equal 'False' (Get-ServerInPrecursor $B) "the server has InPrecursor false for $B after /back"
        Invoke-Bridge $B "chat /warp $A" | Out-Null
        Wait-Until { Test-Near $B $A } -TimeoutSeconds 30 | Out-Null
        Assert-Walking $B 'after /warp'
    }

    Step 'Both relog in the dry room' {
        Invoke-Bridge 'server' 'save' | Out-Null
        Start-Sleep -Seconds 3
        & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop | Out-Null
        # dual-client.ps1 refuses to start while the old launcher is still exiting.
        Wait-Until { -not (Get-Process -Name Nitrox.Launcher, Nitrox.Server.Subnautica, Subnautica -ErrorAction SilentlyContinue) } -TimeoutSeconds 30 | Out-Null
        Start-Sleep -Seconds 3
        Start-ScenarioGames -Players $A, $B -SaveName $SaveName -KeepSave
        foreach ($player in $A, $B) {
            Assert-Walking $player 'after relogging'
        }
        Start-Sleep -Seconds 5
        foreach ($player in $A, $B) {
            Assert-Walking $player '5 s after relogging'
        }
    }

    if (-not $SkipCyclops) {
        Step 'A Cyclops near the Quarantine Enforcement Platform, both inside' {
            # Open water next to the platform; its moon pool trigger and door triggers stay loaded from here.
            Invoke-Bridge $A 'teleport 430 -40 1160' | Out-Null
            Invoke-Bridge $B 'teleport 425 -40 1156' | Out-Null
            Invoke-Bridge $A 'look 0 0' | Out-Null
            Start-Sleep -Seconds 5
            $before = @((Invoke-BridgeJson $A 'find SubRoot 20') | ForEach-Object { $_.id })
            Invoke-Bridge $A 'console sub cyclops' | Out-Null
            $cyclops = Wait-Until { (Invoke-BridgeJson $A 'find SubRoot 20') | Where-Object { $_.name -like 'Cyclops*' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 30
            Assert-True ([bool]$cyclops) 'the Cyclops spawned'
            $script:cyclopsId = $cyclops.id
            Wait-Until { (Invoke-BridgeJson $B 'find SubRoot 20') | Where-Object { $_.id -eq $cyclopsId } } -TimeoutSeconds 30 | Out-Null
            Start-Sleep -Seconds 3
            foreach ($player in $B, $A) {
                $hatch = (Invoke-BridgeJson $player 'find CinematicModeTrigger 40') | Where-Object { $_.name -eq 'EntranceHatch' -and $_.id -eq "(in $cyclopsId)" } | Select-Object -First 1
                $p = [double[]]$hatch.position
                Invoke-Bridge $player ('teleport ' + (($p[0], ($p[1] - 6.9), $p[2] | ForEach-Object { $_.ToString('0.###', $inv) }) -join ' ')) | Out-Null
                Start-Sleep -Milliseconds 500
                Invoke-Bridge $player 'use find:CinematicModeTrigger#EntranceHatch' | Out-Null
                $inside = Wait-Until { $s = Get-State $player; if ($s.currentSub.id -eq $cyclopsId) { $s } } -TimeoutSeconds 15
                Assert-True ([bool]$inside) "$player is in the Cyclops"
                Wait-Until { (Invoke-Bridge $player 'get player Player.cinematicModeActive') -eq 'false' } -TimeoutSeconds 20 | Out-Null
            }
            # Drive-by fix: the pawns that carry the players' movement in the VirtualCyclops are on the Player layer now
            # (they were on TransparentFX), and Alice's pawn still stands on the floor.
            $playerLayer = Invoke-Bridge $A 'call UnityEngine.LayerMask.NameToLayer Player'
            Assert-Equal $playerLayer (Invoke-Bridge $A 'get find:CyclopsPawnIdentifier GameObject.layer') "$A's Cyclops pawn is on the Player layer"
            $grounded = Wait-Until { (Invoke-Bridge $A 'get player CyclopsMotor.grounded') -eq 'true' } -TimeoutSeconds 10
            Assert-True ([bool]$grounded) "$A's Cyclops motor is grounded (the pawn stands on the virtual floor)"
            Assert-True ([bool](Wait-Loaded $A 'find:PrecursorMoonPoolTrigger')) "$A has the platform's moon pool trigger loaded"
            $script:forceWalkDoor = (Invoke-BridgeJson $A 'find PrecursorDoorMotorModeSetter 10') | Where-Object { $_.name -like 'DoorSetMotorModeCollider*' } | ForEach-Object {
                $name = $_.name
                $mode = (Invoke-Bridge $A "get find:PrecursorDoorMotorModeSetter#$name PrecursorDoorMotorModeSetter.setToMotorModeOnEnter").Trim('"')
                if ($mode -eq 'ForceWalk') { $name }
            } | Select-Object -First 1
            Write-Host "  ForceWalk door trigger: $forceWalkDoor"
        }

        Step "Bob's body (inside the Cyclops) touches precursor triggers in Alice's game" {
            $bobId = (Get-State $B).playerId
            $before = Get-WaterState $A
            $waterPlaneBefore = Invoke-Bridge $A 'get player Player.waterPlaneOverride'
            Invoke-Bridge $A "call find:PrecursorMoonPoolTrigger PrecursorMoonPoolTrigger.OnTriggerEnter $bobId" | Out-Null
            if ($forceWalkDoor) {
                Invoke-Bridge $A "call find:PrecursorDoorMotorModeSetter#$forceWalkDoor PrecursorDoorMotorModeSetter.OnTriggerEnter $bobId" | Out-Null
            }
            Start-Sleep -Seconds 1
            $after = Get-WaterState $A
            Assert-Equal $before.OutOfWater $after.OutOfWater "$A's precursorOutOfWater didn't change"
            Assert-Equal $waterPlaneBefore (Invoke-Bridge $A 'get player Player.waterPlaneOverride') "$A didn't get the moon pool's water plane"
            Assert-True ($after.Motor -eq 'Walk' -and -not $after.Underwater) "$A still walks in the Cyclops (motor $($after.Motor), underwater $($after.Underwater))"
        }

        Step 'Alice leaves the Cyclops into open water' {
            Invoke-Bridge $A 'use find:CinematicModeTrigger#InternalDiveHatch' | Out-Null
            $out = Wait-Until { -not (Get-State $A).currentSub } -TimeoutSeconds 15
            Assert-True ([bool]$out) "$A left the Cyclops"
            $swimming = Wait-Until { $s = Get-WaterState $A; if (-not $s.Cinematic -and $s.Underwater -and -not $s.OutOfWater) { $s } } -TimeoutSeconds 10
            $s = Get-WaterState $A
            Assert-True ([bool]$swimming) "$A swims in the open water (precursorOutOfWater $($s.OutOfWater), underwater $($s.Underwater), motor $($s.Motor))"
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S6 PrecursorRejoin' -Keep:$Keep
