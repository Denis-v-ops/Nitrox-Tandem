<#
.SYNOPSIS
    COOP_FEATURES_SPEC R2d: the partner's arm aims a held tool where the partner looks, not where this game's camera looks.

.DESCRIPTION
    A tool such as the scanner turns on the arm's aim IK (ArmsController.Reconfigure: rightAim.shouldAim from the tool's
    ikAimRightArm). Its target is ArmsController.lookTargetTransform. On the partner's body, which is a copy of the local
    body, that pointed at the local player's own LookTarget (4 m in front of the local camera, outside the copied
    hierarchy), so the partner's arm turned with the viewer's camera and bent behind their head when the viewer looked
    at them.

    Alice floats still with the scanner drawn; Bob floats 2.5 m in front of her. Then:
    - Bob only turns his camera (5 directions): Alice's right hand on Bob's game stays where it is (< 5 cm; 40 cm before
      the fix), and the arm's aim target is a child of Alice's body, not of Bob's player;
    - Alice looks straight ahead, down and up: on Bob's game the aim target is 4 m along her view (within 0.5 m) and her
      hand follows it (lower when she looks down than when she looks up);
    - Alice puts the scanner away: the arm stops aiming (aim weight 0).
    Windowless, an animator off camera is CullUpdateTransforms, so the scenario sets Alice's body animator on Bob's game to
    AlwaysAnimate, as a windowed game does while the body is on screen.

    Starts a fresh world and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\R2d-PartnerArmAim.ps1
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'r2d-arm-aim')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$aliceSpot = @(-45, -8, 345)
# The local LookTarget's distance in front of the camera (measured on the local player, see RemotePlayer.LOOK_TARGET_DISTANCE)
$lookDistance = 4

function Get-Vector {
    param([string]$Player, [string]$Path)
    [double[]](Invoke-BridgeJson $Player "get $Path")
}

function Get-Distance {
    param([double[]]$P, [double[]]$Q)
    [Math]::Sqrt([Math]::Pow($P[0] - $Q[0], 2) + [Math]::Pow($P[1] - $Q[1], 2) + [Math]::Pow($P[2] - $Q[2], 2))
}

function Get-AliceHand { Get-Vector $B "$script:aliceId ArmsController.rightHand.position" }

function Get-AimTarget { Get-Vector $B "$script:aliceId ArmsController.rightAim.aimer.solver.IKPosition" }

function Set-Look {
    param([string]$Player, [double]$Yaw, [double]$Pitch)
    Invoke-Bridge $Player "look $Yaw $Pitch" | Out-Null
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    $script:aliceId = (Invoke-BridgeJson $A 'state').playerId

    Step 'Alice floats with the scanner drawn, Bob 2.5 m in front of her looking at her' {
        Invoke-Bridge $A ('teleport ' + ($aliceSpot -join ' ')) | Out-Null
        Invoke-Bridge $B ("teleport {0} {1} {2}" -f $aliceSpot[0], $aliceSpot[1], ($aliceSpot[2] + 2.5)) | Out-Null
        Set-Look $A 0 0
        Set-Look $B 180 0
        Invoke-Bridge $A 'console item scanner' | Out-Null
        Assert-True ([bool](Wait-Until { Get-InventoryItem $A 'Scanner' } -TimeoutSeconds 30)) 'Alice has a scanner'
        $id = (Get-InventoryItem $A 'Scanner' | Select-Object -First 1).id
        $slot = [int](Invoke-Bridge $A "call Inventory.main.quickSlots.GetSlotByItem $id")
        Invoke-Bridge $A "call Inventory.main.quickSlots.SelectImmediate $slot" | Out-Null
        $aiming = Wait-Until { (Invoke-Bridge $B "get $script:aliceId ArmsController.rightAim.shouldAim") -eq 'true' } -TimeoutSeconds 15
        Assert-True ([bool]$aiming) "Bob's copy of Alice aims the scanner arm (rightAim.shouldAim)"
        Invoke-Bridge $B "set $script:aliceId Animator.cullingMode AlwaysAnimate" | Out-Null
        $weight = Wait-Until { [double](Invoke-Bridge $B "get $script:aliceId ArmsController.rightAim.aimer.solver.IKPositionWeight") -gt 0.99 } -TimeoutSeconds 10
        Assert-True ([bool]$weight) "the aim IK is fully on (weight $(Invoke-Bridge $B "get $script:aliceId ArmsController.rightAim.aimer.solver.IKPositionWeight"))"
        $owner = Invoke-Bridge $B "get $script:aliceId ArmsController.rightAim.aimer.solver.target.root.name"
        $alice = Invoke-Bridge $B "get $script:aliceId GameObject.name"
        Assert-Equal $alice $owner "the right arm's aim target belongs to Alice's body on Bob's game"
        $leftOwner = Invoke-Bridge $B "get $script:aliceId ArmsController.leftAim.aimer.solver.target.root.name"
        Assert-Equal $alice $leftOwner "the left arm's aim target belongs to Alice's body on Bob's game"
        $lookOwner = Invoke-Bridge $B "get $script:aliceId ArmsController.lookTargetTransform.root.name"
        Assert-Equal $alice $lookOwner "ArmsController.lookTargetTransform belongs to Alice's body on Bob's game"
    }

    Step "Bob turns his camera; Alice's hand stays where it is" {
        $hands = @()
        foreach ($view in @(@(180, 0), @(160, 0), @(200, 0), @(180, -30), @(180, 30))) {
            Set-Look $B $view[0] $view[1]
            Start-Sleep -Milliseconds 1200
            $hand = Get-AliceHand
            $hands += , $hand
            Write-Host ("  Bob looks {0},{1}: Alice's hand {2}, aim target {3}" -f $view[0], $view[1], (Format-Vector $hand), (Format-Vector (Get-AimTarget)))
        }
        $spread = 0
        foreach ($p in $hands) { foreach ($q in $hands) { $spread = [Math]::Max($spread, (Get-Distance $p $q)) } }
        Assert-True ($spread -lt 0.05) ("Alice's hand moves {0:0.000} m while only Bob's camera turns (< 0.05)" -f $spread)
        Set-Look $B 180 0
    }

    Step "Alice looks ahead, down and up; her arm follows her view" {
        $results = @{}
        foreach ($view in @(@('ahead', 0), @('down', -40), @('up', 40))) {
            Set-Look $A 0 $view[1]
            Start-Sleep -Milliseconds 1500
            $forward = Get-Vector $A 'MainCamera.camera.transform.forward'
            $body = Get-Vector $B "$script:aliceId Transform.position"
            $expected = @(($body[0] + $forward[0] * $lookDistance), ($body[1] + $forward[1] * $lookDistance), ($body[2] + $forward[2] * $lookDistance))
            $target = Get-AimTarget
            $hand = Get-AliceHand
            $results[$view[0]] = $hand
            $off = Get-Distance $target $expected
            Assert-True ($off -lt 0.5) ("Alice looks {0}: the aim target on Bob's game is {1:0.00} m from 4 m along her view (< 0.5; target {2}, expected {3})" -f $view[0], $off, (Format-Vector $target), (Format-Vector $expected))
        }
        $drop = $results['up'][1] - $results['down'][1]
        Assert-True ($drop -gt 0.1) ("Alice's hand is {0:0.00} m lower when she looks down than when she looks up (> 0.1)" -f $drop)
        Set-Look $A 0 0
    }

    Step 'Alice puts the scanner away; the arm stops aiming' {
        Invoke-Bridge $A 'call Inventory.main.quickSlots.DeselectImmediate' | Out-Null
        $off = Wait-Until { [double](Invoke-Bridge $B "get $script:aliceId ArmsController.rightAim.aimer.solver.IKPositionWeight") -lt 0.01 } -TimeoutSeconds 10
        Assert-True ([bool]$off) "the aim IK on Bob's copy is off without a tool"
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'R2d PartnerArmAim' -Keep:$Keep
