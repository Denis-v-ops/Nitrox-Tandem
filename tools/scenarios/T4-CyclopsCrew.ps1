<#
.SYNOPSIS
    COOP_FEATURES_SPEC T4 (Cyclops crew roles): a player standing in the Cyclops runs the helm screen's abilities while
    the partner pilots. Engine, motor mode and horn stay the pilot's; the power the abilities use is paid once.

.DESCRIPTION
    Bob spawns a Cyclops, both board through the entrance hatch. Bob takes the helm and leaves it again, so he stands
    behind the chair with his currentSub set (a teleport inside the sub would clear it), then Alice takes the helm: she
    holds the exclusive lock and simulates the sub, Bob's game replicates it. Bob installs the sonar, shield and fire
    suppression modules and loads a decoy (Inventory.AddOrSwap, what the slot UI does).

    Bob clicks the helm screen's buttons as a player does: the bridge's "hold" turns his camera onto the button (the
    game's UI raycast hovers it: EventTrigger PointerEnter registers its LeftHandButton), then presses the interact
    button (LeftHandButton.ManagedUpdate sees GameInput's LeftHand down and invokes the button's OnClick).

    Checked:
    - Bob's helm screen is interactable (CanvasGroup.interactable, what Nitrox's CyclopsHelmHUDManager patch set false
      for everyone but the pilot);
    - Bob can't start the engine, change the motor mode or sound the horn (the horn button is hidden for him, so its
      handler is called directly);
    - Bob turns silent running, the shield and the sonar on and off: Alice's game follows each time, and the power the
      sub loses while each runs is what one game's drain costs (SubRoot's cost per 5 s iteration, measured in Alice's
      game, the simulating one);
    - Bob launches a decoy and starts the fire suppression: both games get the decoy and the suppression;
    - Bob looks through the external cameras and back;
    - Alice still can: she starts the engine and changes the motor mode, Bob's game follows (her handlers are called:
      the bridge can't aim a pilot's camera).
    The horn (hidden for Bob) and the fire suppression (its own panel, a uGUI Button that needs a real mouse click) are
    called directly.

    Starts a fresh world (save t4-cyclops-crew) and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\T4-CyclopsCrew.ps1
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 't4-cyclops-crew',
    # Over the Grassy Plateaus, open water (S13).
    [double[]]$Area = @(263, -60, 64),
    # Seconds each ability runs while its power drain is measured.
    [int]$DrainSeconds = 22)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$inv = [Globalization.CultureInfo]::InvariantCulture

function Format-Position {
    param([double[]]$P)
    ($P | ForEach-Object { $_.ToString('0.###', $inv) }) -join ' '
}

function Get-State { param([string]$Player) Invoke-BridgeJson $Player 'state' }

function Wait-NoCinematic {
    param([string]$Player)
    Wait-Until { [int](Invoke-Bridge $Player 'get PlayerCinematicController.cinematicModeCount') -eq 0 -and (Invoke-Bridge $Player 'get player Player.cinematicModeActive') -eq 'false' } -TimeoutSeconds 20 -IntervalSeconds 0.3
}

function Enter-Cyclops {
    param([string]$Player)
    $hatch = (Invoke-BridgeJson $Player 'find CinematicModeTrigger 40') | Where-Object { $_.name -eq 'EntranceHatch' -and $_.id -eq "(in $script:cyclopsId)" } | Select-Object -First 1
    if (-not $hatch) { throw "$Player doesn't see the Cyclops' entrance hatch" }
    Invoke-Bridge $Player ('teleport ' + (Format-Position @($hatch.position[0], ($hatch.position[1] - 6.9), $hatch.position[2]))) | Out-Null
    Start-Sleep -Milliseconds 500
    Invoke-Bridge $Player 'use find:CinematicModeTrigger#EntranceHatch' | Out-Null
    $inside = Wait-Until { (Get-State $Player).currentSub.id -eq $script:cyclopsId } -TimeoutSeconds 15 -IntervalSeconds 0.3
    Wait-NoCinematic $Player | Out-Null
    [bool]$inside
}

function Get-Mode {
    param([string]$Player)
    (Invoke-Bridge $Player 'get player Player.mode').Trim('"')
}

function Enter-Helm {
    param([string]$Player)
    Invoke-Bridge $Player 'use find:PilotingChair' | Out-Null
    $piloting = Wait-Until { (Get-Mode $Player) -eq 'Piloting' } -TimeoutSeconds 15 -IntervalSeconds 0.3
    Wait-NoCinematic $Player | Out-Null
    [bool]$piloting
}

function Exit-Helm {
    param([string]$Player)
    Invoke-Bridge $Player 'call player Player.TryEject' | Out-Null
    $left = Wait-Until { (Get-Mode $Player) -eq 'Normal' } -TimeoutSeconds 15 -IntervalSeconds 0.3
    Wait-NoCinematic $Player | Out-Null
    [bool]$left
}

function Get-LockOwner {
    if ((Invoke-Bridge 'server' "query $script:cyclopsId") -match 'Owner: (\w+)') { $Matches[1] } else { '<none>' }
}

# A member of a Cyclops component: the bridge looks for the component on the sub and its children (inactive ones too).
function Get-Sub {
    param([string]$Player, [string]$Member)
    (Invoke-Bridge $Player "get $script:cyclopsId $Member" -AllowError).Trim('"')
}

function Invoke-Sub {
    param([string]$Player, [string]$Method)
    Invoke-Bridge $Player "call $script:cyclopsId $Method" -AllowError
}

function Get-Power {
    param([string]$Player)
    [double]::Parse((Invoke-Bridge $Player "call $script:cyclopsId PowerRelay.GetPower"), $inv)
}

<#
    Clicks a helm screen button as a player does: aim at it (the UI raycast hovers it), then press the interact button.
    $Target is a bridge target such as find:CyclopsSonarButton or find:CyclopsMotorModeButton#Speed_03.
#>
function Invoke-Click {
    param([string]$Player, [string]$Target)
    Invoke-Bridge $Player "hold $Target 1 None" | Out-Null
    Start-Sleep -Milliseconds 1300
    Invoke-Bridge $Player "hold $Target 0.2" | Out-Null
    Start-Sleep -Milliseconds 600
}

function Install-Item {
    param([string]$Player, [string]$Item, [string]$TechType, [string]$Owner, [string]$Slot)
    Invoke-Bridge $Player "console item $Item" | Out-Null
    $id = Wait-Until { (Get-InventoryItem $Player -TechType $TechType).id } -TimeoutSeconds 10
    if (-not $id) { throw "$Player didn't get $Item" }
    $result = Invoke-Bridge $Player "call Inventory.AddOrSwap $id $Owner $Slot"
    Assert-Equal 'true' $result "$Player put $Item in $Slot"
}

function Get-OtherMotorModeButton {
    param([string]$Player)
    $mode = Get-Sub $A 'CyclopsMotorMode.cyclopsMotorMode'
    foreach ($name in @((Invoke-BridgeJson $Player 'find CyclopsMotorModeButton 10') | Select-Object -ExpandProperty name -Unique)) {
        $index = (Invoke-Bridge $Player "get find:CyclopsMotorModeButton#$name CyclopsMotorModeButton.motorModeIndex").Trim('"')
        if ($index -ne $mode) { return [pscustomobject]@{ Name = $name; Mode = $index; Current = $mode } }
    }
    throw "$Player sees no motor mode button other than $mode"
}

<#
    Bob turns an ability on; Alice's game must follow; the power the sub loses in $DrainSeconds (Alice's game) must be
    what one game's drain costs ($Cost per 5 s iteration, with one iteration of slack); then Bob turns it off.
#>
function Test-Ability {
    param([string]$Name, [string]$Button, [string]$StateMember, [double]$Cost)
    Invoke-Click $B "find:$Button"
    $seen = Wait-Until { (Get-Sub $A $StateMember) -eq 'true' } -TimeoutSeconds 10 -IntervalSeconds 0.3
    Assert-True ([bool]$seen) "$A's game turned $Name on after $B clicked it"
    Assert-Equal 'true' (Get-Sub $B $StateMember) "$Name is on in $B's game"
    $before = Get-Power $A
    Start-Sleep -Seconds $DrainSeconds
    $after = Get-Power $A
    $drain = $before - $after - $script:baselinePerSecond * $DrainSeconds
    $iterations = [math]::Floor($DrainSeconds / 5) + 1
    $single = $Cost * $iterations
    Write-Host ("  {0} for {1} s: the sub lost {2:0.0} (one game's drain: about {3:0}, both games' about {4:0})" -f $Name, $DrainSeconds, $drain, $single, (2 * $single))
    Assert-True ($drain -le $single + $Cost) ("$Name drains the power once ({0:0.0} <= {1:0})" -f $drain, ($single + $Cost))
    Assert-True ($drain -ge $single - 2 * $Cost) ("$Name drains the power at all ({0:0.0} >= {1:0})" -f $drain, ($single - 2 * $Cost))
    Invoke-Click $B "find:$Button"
    $off = Wait-Until { (Get-Sub $A $StateMember) -eq 'false' } -TimeoutSeconds 10 -IntervalSeconds 0.3
    Assert-True ([bool]$off) "$A's game turned $Name off after $B clicked it again"
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'Bob spawns a Cyclops; both games see it' {
        Invoke-Bridge $B ('teleport ' + (Format-Position $Area)) | Out-Null
        Invoke-Bridge $A ('teleport ' + (Format-Position @(($Area[0] - 5), $Area[1], ($Area[2] - 4)))) | Out-Null
        Invoke-Bridge $B 'look 0 0' | Out-Null
        Start-Sleep -Seconds 3
        $before = @((Invoke-BridgeJson $B 'find SubRoot 20') | ForEach-Object { $_.id })
        Invoke-Bridge $B 'console sub cyclops' | Out-Null
        $cyclops = Wait-Until { (Invoke-BridgeJson $B 'find SubRoot 20') | Where-Object { $_.name -like 'Cyclops*' -and $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 30
        if (-not $cyclops) { throw 'the Cyclops did not spawn' }
        $script:cyclopsId = $cyclops.id
        Write-Host "  Cyclops $cyclopsId"
        Assert-True ([bool](Wait-Until { (Invoke-BridgeJson $A 'find SubRoot 20') | Where-Object { $_.id -eq $cyclopsId } } -TimeoutSeconds 30)) "$A sees the Cyclops"
        Start-Sleep -Seconds 3
    }

    Step 'Both board; Bob takes the helm and leaves it; Alice takes the helm' {
        Assert-True (Enter-Cyclops $B) "$B is in the Cyclops"
        Assert-True (Enter-Cyclops $A) "$A is in the Cyclops"
        Assert-True (Enter-Helm $B) "$B is piloting"
        Assert-True (Exit-Helm $B) "$B left the helm"
        Assert-True (Enter-Helm $A) "$A is piloting"
        Start-Sleep -Seconds 2
        Assert-Equal $A (Get-LockOwner) 'Alice (the pilot) simulates the Cyclops'
        Assert-Equal $cyclopsId (Get-State $B).currentSub.id "$B is still in the Cyclops"
        Assert-Equal 'Normal' (Get-Mode $B) "$B stands (not piloting)"
        $hud = (Invoke-BridgeJson $B 'find CyclopsHelmHUDManager 10') | Select-Object -First 1
        Write-Host ("  $B stands {0:0.0} m from the helm screen" -f $hud.distance)
    }

    Step 'Bob installs the sonar, shield and fire suppression modules and loads a decoy' {
        Install-Item $B 'cyclopsshieldmodule' 'CyclopsShieldModule' 'find:UpgradeConsole' 'Module1'
        Install-Item $B 'cyclopssonarmodule' 'CyclopsSonarModule' 'find:UpgradeConsole' 'Module2'
        Install-Item $B 'cyclopsfiresuppressionmodule' 'CyclopsFireSuppressionModule' 'find:UpgradeConsole' 'Module3'
        foreach ($player in $A, $B) {
            $ready = Wait-Until { (Get-Sub $player 'SubRoot.shieldUpgrade') -eq 'true' -and (Get-Sub $player 'SubRoot.sonarUpgrade') -eq 'true' } -TimeoutSeconds 15
            Assert-True ([bool]$ready) "$player's game has the shield and sonar upgrades"
        }
        Install-Item $B 'cyclopsdecoy' 'CyclopsDecoy' 'find:CyclopsDecoyLoadingTube' 'DecoySlot1'
        foreach ($player in $A, $B) {
            $loaded = Wait-Until { (Get-Sub $player 'CyclopsDecoyManager.decoyCount') -eq '1' } -TimeoutSeconds 15
            Assert-True ([bool]$loaded) "$player's game has one decoy in the tube"
        }
    }

    Step 'Bob can use the helm screen' {
        Wait-Until { (Get-Sub $B 'CyclopsHelmHUDManager.canvasGroup.interactable') -eq 'true' } -TimeoutSeconds 5 | Out-Null
        Assert-Equal 'true' (Get-Sub $B 'CyclopsHelmHUDManager.canvasGroup.interactable') "$B's helm screen is interactable"
        Assert-Equal 'true' (Get-Sub $A 'CyclopsHelmHUDManager.canvasGroup.interactable') "$A's helm screen is interactable"
    }

    Step "Engine and horn stay the pilot's" {
        Assert-Equal 'false' (Get-Sub $A 'CyclopsMotorMode.engineOn') 'the new Cyclops starts with the engine off'
        Invoke-Click $B 'find:CyclopsEngineChangeState'
        Start-Sleep -Seconds 8
        foreach ($player in $A, $B) {
            Assert-Equal 'false' (Get-Sub $player 'CyclopsMotorMode.engineOn') "the engine is still off in $player's game after $B clicked it"
        }

        # The horn button is only shown to the pilot; call its handler, as a click on it would
        Invoke-Bridge $B 'sounds clear' | Out-Null
        $press = Invoke-Sub $B 'CyclopsHornButton.OnPress'
        Start-Sleep -Seconds 1
        $horn = @(Invoke-BridgeJson $B 'sounds 3 horn')
        Write-Host "  $B pressed the horn ($press): $($horn.Count) horn sounds"
        Assert-Equal 0 $horn.Count "$B's horn press played no horn"
    }

    Step 'The pilot starts the engine; the motor mode stays hers' {
        # The pilot's camera can't be aimed through the bridge; her clicks call the handler (as S13 does)
        Invoke-Sub $A 'CyclopsEngineChangeState.OnClick' | Out-Null
        foreach ($player in $A, $B) {
            $on = Wait-Until { (Get-Sub $player 'CyclopsMotorMode.engineOn') -eq 'true' } -TimeoutSeconds 15 -IntervalSeconds 0.5
            Assert-True ([bool]$on) "the engine is on in $player's game after $A clicked it"
        }
        Start-Sleep -Seconds 1
        $other = Get-OtherMotorModeButton $B
        Write-Host "  motor mode $($other.Current); $B clicks $($other.Name) ($($other.Mode))"
        Invoke-Click $B "find:CyclopsMotorModeButton#$($other.Name)"
        Start-Sleep -Seconds 2
        foreach ($player in $A, $B) {
            Assert-Equal $other.Current (Get-Sub $player 'CyclopsMotorMode.cyclopsMotorMode') "the motor mode is unchanged in $player's game after $B clicked $($other.Name)"
        }
        $other = Get-OtherMotorModeButton $A
        Invoke-Bridge $A "call find:CyclopsMotorModeButton#$($other.Name) CyclopsMotorModeButton.OnClick" | Out-Null
        foreach ($player in $A, $B) {
            $changed = Wait-Until { (Get-Sub $player 'CyclopsMotorMode.cyclopsMotorMode') -eq $other.Mode } -TimeoutSeconds 10 -IntervalSeconds 0.5
            Assert-True ([bool]$changed) "the motor mode is $($other.Mode) in $player's game after $A clicked $($other.Name)"
        }
    }

    Step 'Power use with no ability on (baseline)' {
        $before = Get-Power $A
        Start-Sleep -Seconds 10
        $after = Get-Power $A
        $script:baselinePerSecond = [math]::Max(0, ($before - $after) / 10)
        Write-Host ("  power {0:0.0} -> {1:0.0} in 10 s; {2:0.00} per second" -f $before, $after, $script:baselinePerSecond)
        Assert-True ($script:baselinePerSecond -lt 0.5) 'the idle sub uses (almost) no power'
    }

    Step 'Bob runs silent running, the shield and the sonar; Alice sees them; the power drains once' {
        Test-Ability 'silent running' 'CyclopsSilentRunningAbilityButton' 'CyclopsSilentRunningAbilityButton.active' ([double](Get-Sub $A 'SubRoot.silentRunningPowerCost'))
        Test-Ability 'the shield' 'CyclopsShieldButton' 'CyclopsShieldButton.active' ([double](Get-Sub $A 'SubRoot.shieldPowerCost'))
        Test-Ability 'the sonar' 'CyclopsSonarButton' 'CyclopsSonarButton._sonarActive' ([double](Get-Sub $A 'SubRoot.sonarPowerCost'))
    }

    Step 'Bob launches a decoy and starts the fire suppression' {
        $decoysBefore = @{}
        foreach ($player in $A, $B) { $decoysBefore[$player] = @(Invoke-BridgeJson $player 'find CyclopsDecoy 500').Count }
        Invoke-Click $B 'find:CyclopsDecoyLaunchButton'
        foreach ($player in $A, $B) {
            $launched = Wait-Until { @(Invoke-BridgeJson $player 'find CyclopsDecoy 500').Count -gt $decoysBefore[$player] } -TimeoutSeconds 10
            Assert-True ([bool]$launched) "a decoy left the Cyclops in $player's game"
            Assert-Equal '0' (Get-Sub $player 'CyclopsDecoyManager.decoyCount') "the tube is empty in $player's game"
        }

        # Not on the helm screen: the fire suppression panel is a uGUI Button (a mouse click the bridge can't make), so call
        # its handler
        Invoke-Sub $B 'CyclopsFireSuppressionSystemButton.OnClick' | Out-Null
        foreach ($player in $A, $B) {
            $suppressing = Wait-Until { (Get-Sub $player 'SubRoot.fireSuppressionState') -eq 'true' } -TimeoutSeconds 10 -IntervalSeconds 0.5
            Assert-True ([bool]$suppressing) "the fire suppression runs in $player's game"
        }
    }

    Step 'Bob looks through the external cameras and back' {
        Invoke-Click $B 'find:CyclopsExternalCamsButton'
        Assert-True ([bool](Wait-Until { (Get-Sub $B 'CyclopsExternalCams.active') -eq 'true' } -TimeoutSeconds 5)) "$B looks through the external cameras"
        Invoke-Sub $B 'CyclopsExternalCams.SetActive false' | Out-Null
        Assert-True ([bool](Wait-Until { (Get-Sub $B 'CyclopsExternalCams.active') -eq 'false' } -TimeoutSeconds 5)) "$B is back from the cameras"
        Assert-Equal $cyclopsId (Get-State $B).currentSub.id "$B is still in the Cyclops"
        Assert-Equal 'Piloting' (Get-Mode $A) "$A is still piloting"
        Assert-NoDiff @($cyclopsId) 'no desync for the Cyclops'
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure "scenario aborted: $($_.Exception.Message)" }
}

Complete-Scenario -Name 'T4-CyclopsCrew' -Keep:$Keep
