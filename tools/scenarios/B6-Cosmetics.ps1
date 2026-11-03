<#
.SYNOPSIS
    COOP_FEATURES_SPEC B6: the partner sees the propulsion cannon's beam and the cannons' shots, and a repulsion shot
    pushes an object in both games. After a propulsion shot the partner can grab the shot object.

.DESCRIPTION
    Bob drops two pieces of scrap metal (his game takes their simulation lock, as for any dropped item). Alice holds a
    propulsion cannon, later a repulsion cannon; Bob holds a propulsion cannon too.
    1. Alice grabs scrap 1 (PropulsionCannon.GrabObject through the bridge, which runs the lock request of
       PropulsionCannon_GrabObject_Patch): Bob's copy of her cannon shows the beam (fxBeam and the glow on), its electric
       lines end at scrap 1 in Bob's game, and the copy itself grabs nothing (grabbedObject stays null).
    2. Alice lets go (ReleaseGrabbedObject): the beam goes off on Bob's copy.
    3. Alice grabs scrap 1 again and shoots it (OnShoot): the beam goes off and Bob's game plays the shot on his copy
       (log line). Then Bob grabs scrap 1 with his own cannon: it works (before, Alice's game kept the exclusive lock
       after a shot, so Bob's grab was refused) and Alice's copy of Bob's cannon shows his beam.
    4. Alice draws the repulsion cannon, faces scrap 2 from 4 m and fires (RepulsionCannon.OnToolUseAnim through the bridge:
       it's an animation event, which doesn't fire windowless). Scrap 2 flies off in Alice's game and in Bob's, and both
       games end with it at the same place; Bob's game plays the shot on his copy of her cannon (log line).
    Sound isn't checked (windowless games are muted).

    Starts a fresh world and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\B6-Cosmetics.ps1
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'b6-cosmetics')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$spot = @(-60, -15, 380)

function Get-Position {
    param([string]$Player, [string]$Id)
    [double[]](Invoke-BridgeJson $Player "entity $Id").position
}

function Get-Bool {
    param([string]$Player, [string]$Query)
    (Invoke-Bridge $Player "get $Query") -eq 'true'
}

# The id of what the cannon has grabbed in this game ('' when nothing)
function Get-Grabbed {
    param([string]$Player, [string]$CannonId)
    $grabbed = Invoke-BridgeJson $Player "get $CannonId PropulsionCannon.grabbedObject"
    if ($grabbed) { "$($grabbed.id)" } else { '' }
}

function Select-Tool {
    param([string]$Player, [string]$Id)
    $slot = [int](Invoke-Bridge $Player "call Inventory.main.quickSlots.GetSlotByItem $Id")
    Invoke-Bridge $Player "call Inventory.main.quickSlots.SelectImmediate $slot" | Out-Null
}

function Add-Tool {
    param([string]$Player, [string]$TechType)
    Invoke-Bridge $Player "console item $TechType" | Out-Null
    $item = Wait-Until { Get-InventoryItem $Player -TechType $TechType | Select-Object -First 1 } -TimeoutSeconds 10
    Assert-True ([bool]$item) "$Player has a $TechType"
    $charge = [double](Invoke-Bridge $Player "get $($item.id) EnergyMixin.charge")
    Assert-True ($charge -gt 4) "$Player's $TechType is charged ($charge)"
    $item.id
}

# Bob drops a piece of scrap metal in front of him; returns its id once Alice's game has it too
function Add-Scrap {
    $before = @(Get-InventoryItem $B -TechType 'ScrapMetal' | ForEach-Object { $_.id })
    Invoke-Bridge $B 'console item scrapmetal' | Out-Null
    $scrap = Wait-Until { Get-InventoryItem $B -TechType 'ScrapMetal' | Where-Object { $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 10
    if (-not $scrap) { throw 'Bob got no scrap metal' }
    Invoke-Bridge $B "call Inventory.main.InternalDropItem $($scrap.id) true" | Out-Null
    $inAlice = Wait-Until { Get-Position $A $scrap.id } -TimeoutSeconds 15
    Assert-True ([bool]$inAlice) "Bob dropped scrap metal $($scrap.id) and Alice's game has it"
    $scrap.id
}

# Bob's game played this cannon effect on its copy of Alice's cannon since the marks
function Test-EffectLogged {
    param([hashtable]$Marks, [string]$Player, [string]$Effect, [string]$CannonId)
    $texts = Get-LogTextSince $Marks @($A, $B) $SaveName
    $file = $texts.Keys | Where-Object { (Split-Path -Leaf $_) -like "game*$Player*" }
    [bool]($file | Where-Object { $texts[$_] -match "Played $Effect on the copy of $CannonId" })
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'Setup: both players 6 m apart underwater; cannons; Bob drops two pieces of scrap metal' {
        Invoke-Bridge $A ('teleport ' + ($spot -join ' ')) | Out-Null
        Invoke-Bridge $B ("teleport {0} {1} {2}" -f ($spot[0] + 6), $spot[1], $spot[2]) | Out-Null
        Start-Sleep -Seconds 4
        $script:alicePropulsion = Add-Tool $A 'PropulsionCannon'
        $script:aliceRepulsion = Add-Tool $A 'RepulsionCannon'
        $script:bobPropulsion = Add-Tool $B 'PropulsionCannon'
        $script:scrap1 = Add-Scrap
        $script:scrap2 = Add-Scrap
        Start-Sleep -Seconds 2
    }

    Step 'Alice grabs scrap 1: Bob sees the beam on her cannon' {
        Select-Tool $A $alicePropulsion
        $drawn = Wait-Until { Get-Bool $B "$alicePropulsion GameObject.activeSelf" } -TimeoutSeconds 10
        Assert-True ([bool]$drawn) "Bob's copy of Alice's propulsion cannon is out"
        Assert-True (-not (Get-Bool $B "$alicePropulsion PropulsionCannon.fxBeam.activeSelf")) "Bob's copy shows no beam before the grab"

        Invoke-Bridge $A "call $alicePropulsion PropulsionCannon.GrabObject $scrap1" | Out-Null
        $grabbed = Wait-Until { (Get-Grabbed $A $alicePropulsion) -eq $scrap1 } -TimeoutSeconds 10 -IntervalSeconds 0.25
        Assert-True ([bool]$grabbed) "Alice's cannon holds scrap 1 (the lock was granted)"
        $beam = Wait-Until { Get-Bool $B "$alicePropulsion PropulsionCannon.fxBeam.activeSelf" } -TimeoutSeconds 8 -IntervalSeconds 0.25
        Assert-True ([bool]$beam) "Bob's copy of Alice's cannon shows the beam"
        Assert-True (Get-Bool $B "$alicePropulsion PropulsionCannon.grabbedEffect.activeSelf") "Bob's copy shows the glow on the grabbed object"
        Start-Sleep -Seconds 1
        $lineEnd = [double[]](Invoke-BridgeJson $B "get $alicePropulsion VFXElectricLine.target")
        $scrapAtBob = Get-Position $B $scrap1
        $gap = Get-Distance $lineEnd $scrapAtBob
        Assert-True ($gap -lt 2) ("the beam ends at scrap 1 in Bob's game ({0:0.00} m from it)" -f $gap)
        Assert-Equal '' (Get-Grabbed $B $alicePropulsion) "Bob's copy of the cannon grabs nothing itself"
    }

    Step 'Alice lets go: the beam goes off for Bob' {
        Invoke-Bridge $A "call $alicePropulsion PropulsionCannon.ReleaseGrabbedObject" | Out-Null
        $off = Wait-Until { -not (Get-Bool $B "$alicePropulsion PropulsionCannon.fxBeam.activeSelf") } -TimeoutSeconds 8 -IntervalSeconds 0.25
        Assert-True ([bool]$off) "Bob's copy of Alice's cannon shows no beam after she lets go"
    }

    Step 'Alice grabs scrap 1 again and shoots it; then Bob grabs it' {
        Invoke-Bridge $A "call $alicePropulsion PropulsionCannon.GrabObject $scrap1" | Out-Null
        $grabbed = Wait-Until { (Get-Grabbed $A $alicePropulsion) -eq $scrap1 } -TimeoutSeconds 10 -IntervalSeconds 0.25
        Assert-True ([bool]$grabbed) 'Alice grabs scrap 1 again'
        Wait-Until { Get-Bool $B "$alicePropulsion PropulsionCannon.fxBeam.activeSelf" } -TimeoutSeconds 8 -IntervalSeconds 0.25 | Out-Null
        $marks = Get-LogMarks @($A, $B) $SaveName
        Invoke-Bridge $A "call $alicePropulsion PropulsionCannon.OnShoot" | Out-Null
        Assert-Equal '' (Get-Grabbed $A $alicePropulsion) "Alice's cannon let go of scrap 1 with the shot"
        $off = Wait-Until { -not (Get-Bool $B "$alicePropulsion PropulsionCannon.fxBeam.activeSelf") } -TimeoutSeconds 8 -IntervalSeconds 0.25
        Assert-True ([bool]$off) "Bob's copy shows no beam after the shot"
        $shot = Wait-Until { Test-EffectLogged $marks $B 'PROPULSION_SHOT' $alicePropulsion } -TimeoutSeconds 8
        Assert-True ([bool]$shot) "Bob's game played the shot on its copy of Alice's cannon"

        Start-Sleep -Seconds 3
        Invoke-Bridge $A 'call Inventory.main.quickSlots.DeselectImmediate' | Out-Null
        Select-Tool $B $bobPropulsion
        Wait-Until { Get-Bool $A "$bobPropulsion GameObject.activeSelf" } -TimeoutSeconds 10 | Out-Null
        Invoke-Bridge $B "call $bobPropulsion PropulsionCannon.GrabObject $scrap1" | Out-Null
        $bobGrabbed = Wait-Until { (Get-Grabbed $B $bobPropulsion) -eq $scrap1 } -TimeoutSeconds 8 -IntervalSeconds 0.25
        Assert-True ([bool]$bobGrabbed) 'Bob can grab the scrap Alice shot (her game gave up the exclusive lock)'
        $bobBeam = Wait-Until { Get-Bool $A "$bobPropulsion PropulsionCannon.fxBeam.activeSelf" } -TimeoutSeconds 8 -IntervalSeconds 0.25
        Assert-True ([bool]$bobBeam) "Alice's copy of Bob's cannon shows his beam"
        Invoke-Bridge $B "call $bobPropulsion PropulsionCannon.ReleaseGrabbedObject" | Out-Null
        Invoke-Bridge $B 'call Inventory.main.quickSlots.DeselectImmediate' | Out-Null
        $bobOff = Wait-Until { -not (Get-Bool $A "$bobPropulsion PropulsionCannon.fxBeam.activeSelf") } -TimeoutSeconds 8 -IntervalSeconds 0.25
        Assert-True ([bool]$bobOff) "Alice's copy of Bob's cannon shows no beam after he lets go"
    }

    Step 'Alice fires the repulsion cannon at scrap 2: it flies off in both games' {
        Select-Tool $A $aliceRepulsion
        Wait-Until { Get-Bool $B "$aliceRepulsion GameObject.activeSelf" } -TimeoutSeconds 10 | Out-Null
        # Face scrap 2 from 4 m: look straight ahead, then put the camera 4 m behind the scrap along that direction
        Invoke-Bridge $A 'look 0 0' | Out-Null
        Start-Sleep -Milliseconds 500
        $forward = [double[]](Invoke-BridgeJson $A 'get MainCamera.camera.transform.forward')
        for ($i = 0; $i -lt 2; $i++) {
            $target = Get-Position $A $scrap2
            $camera = [double[]](Invoke-BridgeJson $A 'get MainCamera.camera.transform.position')
            $body = [double[]](Invoke-BridgeJson $A 'state').position
            $p = 0..2 | ForEach-Object { $target[$_] - 4 * $forward[$_] - ($camera[$_] - $body[$_]) }
            Invoke-Bridge $A ('teleport {0:0.00} {1:0.00} {2:0.00}' -f $p[0], $p[1], $p[2]) | Out-Null
            Start-Sleep -Milliseconds 700
        }
        $camera = [double[]](Invoke-BridgeJson $A 'get MainCamera.camera.transform.position')
        $beforeA = Get-Position $A $scrap2
        $beforeB = Get-Position $B $scrap2
        Write-Host ("  camera {0}, scrap 2 at Alice {1}, at Bob {2}" -f (Format-Vector $camera), (Format-Vector $beforeA), (Format-Vector $beforeB))

        $marks = Get-LogMarks @($A, $B) $SaveName
        Invoke-Bridge $A "call $aliceRepulsion RepulsionCannon.OnToolUseAnim player" | Out-Null
        $movedA = Wait-Until { (Get-Distance (Get-Position $A $scrap2) $beforeA) -gt 3 } -TimeoutSeconds 6 -IntervalSeconds 0.25
        Assert-True ([bool]$movedA) ("scrap 2 flies off in Alice's game ({0:0.0} m)" -f (Get-Distance (Get-Position $A $scrap2) $beforeA))
        $movedB = Wait-Until { (Get-Distance (Get-Position $B $scrap2) $beforeB) -gt 3 } -TimeoutSeconds 6 -IntervalSeconds 0.25
        Assert-True ([bool]$movedB) ("scrap 2 flies off in Bob's game ({0:0.0} m)" -f (Get-Distance (Get-Position $B $scrap2) $beforeB))
        Start-Sleep -Seconds 5
        $agree = Wait-Until { (Get-Distance (Get-Position $A $scrap2) (Get-Position $B $scrap2)) -lt 1.5 } -TimeoutSeconds 10 -IntervalSeconds 0.5
        Assert-True ([bool]$agree) ("both games end with scrap 2 at the same place (Alice {0}, Bob {1})" -f (Format-Vector (Get-Position $A $scrap2)), (Format-Vector (Get-Position $B $scrap2)))
        $shot = Wait-Until { Test-EffectLogged $marks $B 'REPULSION_SHOT' $aliceRepulsion } -TimeoutSeconds 8
        Assert-True ([bool]$shot) "Bob's game played the repulsion shot on its copy of Alice's cannon"
    }

    Step 'No desync for the scrap' {
        Assert-NoDiff @($scrap1, $scrap2) 'diff reports nothing for the two pieces of scrap'
    }
}
catch {
    Write-Host "Scenario aborted: $($_.Exception.Message)" -ForegroundColor Red
}

Complete-Scenario -Name 'B6 cosmetics (cannon beams and shots)' -Keep:$Keep
