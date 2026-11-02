<#
.SYNOPSIS
    FIX_SPEC S6 (#2712): a player inside the Cyclops must stay "inside" (currentSub set, not underwater, oxygen not
    draining), however often the partner goes in and out, while the sub moves and vehicles dock.

.DESCRIPTION
    Alice spawns a Cyclops (console "sub cyclops", powered). Bob enters it through the entrance hatch. Then Alice goes in
    and out through the real hatches (CinematicModeTrigger EntranceHatch / InternalDiveHatch, OnHandClick) -Cycles times
    while the simulating player keeps the Cyclops moving. Every -DockEvery cycles Alice instead spawns a Seamoth, pilots
    it into the Cyclops docking bay (VehicleDockingBay.Update docks it and calls SetCurrentSub(cyclops) non-forced, the
    one non-forced way into a Cyclops) and undocks it again from inside (DockedVehicleHandTarget).

    Checked after every entry: Alice's and Bob's currentSub is the Cyclops, neither is underwater, the server's SubRootId
    for both is the Cyclops, oxygen isn't draining. After every exit: Alice's sub is null in game and on the server.
    The NoOxygen cheat is turned off (oxygen must be real); no-damage stays on.

    Starts a fresh world (save s6-interior) and two games (opens windows), unless -NoLaunch. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [int]$Cycles = 50, [int]$DockEvery = 10, [string]$SaveName = 's6-interior',
    # Open water over the Grassy Plateaus, deep enough for a Cyclops (about 35 m above the floor).
    [double[]]$Area = @(263, -55, 64))

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$inv = [Globalization.CultureInfo]::InvariantCulture

function Format-Position {
    param([double[]]$P)
    ($P | ForEach-Object { $_.ToString('0.###', $inv) }) -join ' '
}

function Get-State {
    param([string]$Player)
    Invoke-BridgeJson $Player 'state'
}

function Get-ServerSubRootId {
    param([string]$Player)
    $entry = (Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $Player }
    "$($entry.subRootId)"
}

function Test-InCyclops {
    param($State)
    $State.currentSub -and $State.currentSub.id -eq $script:cyclopsId
}

function Wait-NoCinematic {
    param([string]$Player)
    Wait-Until { [int](Invoke-Bridge $Player 'get PlayerCinematicController.cinematicModeCount') -eq 0 -and (Invoke-Bridge $Player 'get player Player.cinematicModeActive') -eq 'false' } -TimeoutSeconds 20 -IntervalSeconds 0.3
}

<#
    The Cyclops' hatch trigger with that GameObject name, as this player's game sees it (world position).
#>
function Get-HatchPosition {
    param([string]$Player, [string]$Name)
    $hatch = (Invoke-BridgeJson $Player 'find CinematicModeTrigger 40') | Where-Object { $_.name -eq $Name -and $_.id -eq "(in $script:cyclopsId)" } | Select-Object -First 1
    if (-not $hatch) { throw "$Player doesn't see the Cyclops' $Name" }
    [double[]]$hatch.position
}

<#
    From the water under the Cyclops through the entrance hatch (onCinematicStart -> EnterExitHelper.CinematicEnter).
#>
function Enter-Cyclops {
    param([string]$Player)
    $hatch = Get-HatchPosition $Player 'EntranceHatch'
    Invoke-Bridge $Player ('teleport ' + (Format-Position @($hatch[0], ($hatch[1] - 6.9), $hatch[2]))) | Out-Null
    Start-Sleep -Milliseconds 500
    Invoke-Bridge $Player 'use find:CinematicModeTrigger#EntranceHatch' | Out-Null
    $inside = Wait-Until { Test-InCyclops (Get-State $Player) } -TimeoutSeconds 15 -IntervalSeconds 0.3
    Wait-NoCinematic $Player | Out-Null
    [bool]$inside
}

<#
    Out through the internal dive hatch (onCinematicEnd -> EnterExitHelper.CinematicExit -> SetCurrentSub(null)).
#>
function Exit-Cyclops {
    param([string]$Player)
    Invoke-Bridge $Player 'use find:CinematicModeTrigger#InternalDiveHatch' | Out-Null
    $outside = Wait-Until { -not (Get-State $Player).currentSub } -TimeoutSeconds 15 -IntervalSeconds 0.3
    Wait-NoCinematic $Player | Out-Null
    [bool]$outside
}

<#
    The simulating player pushes the Cyclops along z (direction alternates so it stays in the area).
#>
function Get-CyclopsOwner {
    if ((Invoke-Bridge 'server' "query $script:cyclopsId") -match 'Owner: (\w+)') { $Matches[1] } else { $A }
}

function Move-Cyclops {
    param([int]$Direction)
    Invoke-Bridge (Get-CyclopsOwner) ("set $script:cyclopsId Rigidbody.velocity 0,0," + (4 * $Direction).ToString($inv)) | Out-Null
}

$script:problems = New-Object System.Collections.ArrayList
function Add-Problem {
    param([string]$Message)
    [void]$script:problems.Add($Message)
    Write-Host "    ! $Message" -ForegroundColor Yellow
}

<#
    Both inside the Cyclops, not underwater, the server agrees, and oxygen isn't lower than it was.
#>
function Test-BothInside {
    param([string]$When)
    foreach ($player in $A, $B) {
        $state = Get-State $player
        if (-not (Test-InCyclops $state)) { Add-Problem "$When : $player's currentSub is '$($state.currentSub.name)' (position $(Format-Vector ([double[]]$state.position)))" }
        if ($state.isUnderwater) { Add-Problem "$When : $player is underwater inside the Cyclops" }
        $motor = (Invoke-Bridge $player 'get player Player.motorMode').Trim('"')
        if ($motor -ne 'Walk') { Add-Problem "$When : $player's motor mode is $motor inside the Cyclops (swimming)" }
        if ($script:lastOxygen[$player] -and $state.oxygen -lt $script:lastOxygen[$player] - 1) { Add-Problem "$When : $player's oxygen dropped from $($script:lastOxygen[$player]) to $($state.oxygen)" }
        $script:lastOxygen[$player] = $state.oxygen
        $serverSub = Wait-Until { $id = Get-ServerSubRootId $player; if ($id -eq $script:cyclopsId) { $id } } -TimeoutSeconds 5 -IntervalSeconds 0.5
        if (-not $serverSub) { Add-Problem "$When : the server's SubRootId for $player is '$(Get-ServerSubRootId $player)', not the Cyclops" }
    }
}

<#
    Alice spawns a Seamoth outside, pilots it into the docking bay, waits for the dock (she ends up in the Cyclops),
    then undocks it from inside and leaves the Seamoth outside.
#>
function Get-DockingBay {
    # The Cyclops' bay has its own NitroxId; the nearest bay with an id is the Cyclops' (the VirtualCyclops copy has none).
    $bay = (Invoke-BridgeJson $A 'find VehicleDockingBay 5') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' } | Select-Object -First 1
    if (-not $bay) { throw 'no docking bay found' }
    $bay
}

function Get-DockingBayPosition {
    [double[]](Get-DockingBay).position
}

function Format-Vector3Arg {
    param([double[]]$P)
    ($P | ForEach-Object { $_.ToString('0.###', $inv) }) -join ','
}

<#
    The bay doors' animator (on the Cyclops, not under the bay): culling mode, state and clips. Docking needs the state
    "opened" (VehicleDockingBay.requiresAnimationStateForDocking, read in game).
#>
$script:openedHash = -1386862757    # Animator.StringToHash("opened")
function Get-BayAnimator {
    (Invoke-BridgeJson $A "animator $script:cyclopsId all") | Where-Object { $_.object -eq 'cyclops_launch_bay_launch_cin' } | Select-Object -First 1
}

<#
    What VehicleDockingBay.OnTriggerEnter checks, as Alice's game sees it, for a docking try that didn't dock.
#>
function Write-DockDiagnostics {
    param([string]$BayId)
    $values = [ordered]@{}
    foreach ($member in 'interpolatingVehicle', '_dockedVehicle', 'nearbyVehicle') {
        $values["bay.$member set"] = (Invoke-Bridge $A -AllowError "get $BayId VehicleDockingBay.$member") -ne 'null'
    }
    foreach ($member in '_docked', 'timeUndocked') {
        $values["seamoth.$member"] = Invoke-Bridge $A -AllowError "get $seamothId Vehicle.$member"
    }
    $values['Time.time'] = Invoke-Bridge $A -AllowError 'get UnityEngine.Time.time'
    $values['seamoth/bay position'] = "$(Invoke-Bridge $A -AllowError "get $seamothId Transform.position") / $(Format-Vector (Get-DockingBayPosition))"
    $values['server'] = ((Invoke-Bridge 'server' -AllowError "query $seamothId") -split "`n" | Where-Object { $_ -match 'Owner' }) -join ' '
    try {
        $animator = Get-BayAnimator
        $values['bay animator'] = "$($animator.cullingMode), state $($animator.layers[0].stateHash) (opened: $script:openedHash), $($animator.layers[0].clips -join ', ')"
    }
    catch { $values['bay animator'] = $_.Exception.Message }
    $values.GetEnumerator() | ForEach-Object { Write-Host "    $($_.Key) = $($_.Value)" }
}

function Invoke-DockCycle {
    param([int]$Cycle)
    $p = Get-DockingBayPosition
    Invoke-Bridge $A ('teleport ' + (Format-Position @(($p[0] + 20), ($p[1] - 8), $p[2]))) | Out-Null
    Invoke-Bridge $A 'look 270 0' | Out-Null
    Start-Sleep -Seconds 1
    if (-not $script:seamothId) {
        $before = @((Invoke-BridgeJson $A 'find SeaMoth 20') | ForEach-Object { $_.id })
        Invoke-Bridge $A 'console spawn seamoth' | Out-Null
        $seamoth = Wait-Until { (Invoke-BridgeJson $A 'find SeaMoth 20') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
        if (-not $seamoth) { Add-Problem "cycle $Cycle : the Seamoth didn't spawn"; return }
        $script:seamothId = $seamoth.id
        Write-Host "  Seamoth $seamothId"
        Start-Sleep -Seconds 2
    }
    else {
        Invoke-Bridge $A ("set $seamothId Transform.position " + (Format-Vector3Arg @(($p[0] + 12), ($p[1] - 8), $p[2]))) | Out-Null
        Start-Sleep -Seconds 1
    }
    Invoke-Bridge $A "use $seamothId SeaMoth" | Out-Null
    $piloting = Wait-Until { (Get-State $A).vehicle } -TimeoutSeconds 10 -IntervalSeconds 0.3
    if (-not $piloting) { Add-Problem "cycle $Cycle : Alice couldn't enter the Seamoth"; return }
    # The bay only docks a vehicle that enters its trigger (VehicleDockingBay.OnTriggerEnter) while its door animation
    # is in the "opened" state, and opens the doors when a vehicle comes near (LaunchbayAreaEnter). So: stop the sub,
    # come from below, wait for the doors, then into the trigger; retry from further away if docking doesn't start.
    $bayId = (Get-DockingBay).id
    Invoke-Bridge (Get-CyclopsOwner) "set $script:cyclopsId Rigidbody.velocity 0,0,0" | Out-Null
    $docked = $null
    for ($try = 1; $try -le 3 -and -not $docked; $try++) {
        $p = Get-DockingBayPosition
        Invoke-Bridge $A ("set $seamothId Transform.position " + (Format-Vector3Arg @($p[0], ($p[1] - 25), $p[2]))) | Out-Null
        Start-Sleep -Seconds 2
        $p = Get-DockingBayPosition
        Invoke-Bridge $A ("set $seamothId Transform.position " + (Format-Vector3Arg @($p[0], ($p[1] - 8), $p[2]))) | Out-Null
        Wait-Until { (Invoke-Bridge $A "get $bayId VehicleDockingBay.nearbyVehicle") -ne 'null' } -TimeoutSeconds 8 | Out-Null
        $opened = Wait-Until { (Get-BayAnimator).layers[0].stateHash -eq $script:openedHash } -TimeoutSeconds 8 -IntervalSeconds 0.5
        if (-not $opened) { Write-Host "  docking try $try : the bay doors didn't open" }
        Start-Sleep -Seconds 1
        $p = Get-DockingBayPosition
        Invoke-Bridge $A ("set $seamothId Transform.position " + (Format-Vector3Arg @($p[0], ($p[1] - 0.4), $p[2]))) | Out-Null
        $docked = Wait-Until { (Test-InCyclops (Get-State $A)) -and -not (Get-State $A).vehicle } -TimeoutSeconds 15 -IntervalSeconds 0.5
        if (-not $docked) {
            Write-Host "  docking try $try didn't dock"
            Write-DockDiagnostics $bayId
        }
    }
    Wait-NoCinematic $A | Out-Null
    if (-not $docked) { Add-Problem "cycle $Cycle : docking didn't put Alice in the Cyclops (state $((Get-State $A) | ConvertTo-Json -Compress -Depth 3))"; return }
    $script:docks++
    $script:lastOxygen[$A] = $null
    Start-Sleep -Seconds 1
    Test-BothInside "cycle $Cycle (docked)"
    # Undock from inside: DockedVehicleHandTarget -> cinematic -> Vehicle.EnterVehicle (sub null) -> undock.
    Invoke-Bridge $A 'use find:DockedVehicleHandTarget' | Out-Null
    $undocked = Wait-Until { $s = Get-State $A; -not $s.currentSub -and $s.vehicle } -TimeoutSeconds 30 -IntervalSeconds 0.5
    if (-not $undocked) { Add-Problem "cycle $Cycle : undocking didn't put Alice in the Seamoth outside"; return }
    $script:undocks++
    Start-Sleep -Seconds 3
    $serverSub = Wait-Until { $id = Get-ServerSubRootId $A; if (-not $id) { 'none' } } -TimeoutSeconds 5 -IntervalSeconds 0.5
    if (-not $serverSub) { Add-Problem "cycle $Cycle : after undocking the server's SubRootId for Alice is still '$(Get-ServerSubRootId $A)'" }
    # Leave the Seamoth (what the exit key ends in).
    Invoke-Bridge $A 'call player Player.ToNormalMode true' | Out-Null
    $left = Wait-Until { -not (Get-State $A).vehicle } -TimeoutSeconds 10 -IntervalSeconds 0.3
    if (-not $left) { Add-Problem "cycle $Cycle : Alice couldn't leave the Seamoth" }
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    foreach ($player in $A, $B) {
        Invoke-Bridge $player 'call GameModeUtils.DeactivateCheat NoOxygen' | Out-Null
    }
    $script:lastOxygen = @{}
    $script:docks = 0
    $script:undocks = 0
    $script:seamothId = $null

    Step 'Alice spawns a Cyclops, Bob goes in' {
        Invoke-Bridge $A ('teleport ' + (Format-Position $area)) | Out-Null
        Invoke-Bridge $B ('teleport ' + (Format-Position @(($area[0] - 5), $area[1], ($area[2] - 4)))) | Out-Null
        Invoke-Bridge $A 'look 0 0' | Out-Null
        Start-Sleep -Seconds 3
        $before = @((Invoke-BridgeJson $A 'find SubRoot 20') | ForEach-Object { $_.id })
        Invoke-Bridge $A 'console sub cyclops' | Out-Null
        $cyclops = Wait-Until { (Invoke-BridgeJson $A 'find SubRoot 20') | Where-Object { $_.name -like 'Cyclops*' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 30
        Assert-True ([bool]$cyclops) 'the Cyclops spawned'
        $script:cyclopsId = $cyclops.id
        Write-Host "  Cyclops $cyclopsId"
        $seen = Wait-Until { (Invoke-BridgeJson $B 'find SubRoot 20') | Where-Object { $_.id -eq $cyclopsId } } -TimeoutSeconds 30
        Assert-True ([bool]$seen) "$B sees the Cyclops"
        Start-Sleep -Seconds 3
        Assert-True (Enter-Cyclops $B) "$B is in the Cyclops after using the entrance hatch"
        Assert-True (Enter-Cyclops $A) "$A is in the Cyclops after using the entrance hatch"
        Test-BothInside 'start'
        Assert-True (Exit-Cyclops $A) "$A is out after using the internal dive hatch"
        $script:lastOxygen[$A] = $null
    }

    Step "Alice goes in and out $Cycles times while Bob stays inside and the Cyclops moves" {
        $entries = 0
        $exits = 0
        for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
            Move-Cyclops -Direction $(if ($cycle % 2) { 1 } else { -1 })
            if ($DockEvery -gt 0 -and $cycle % $DockEvery -eq 0) {
                Write-Host "  cycle $cycle : docking"
                Invoke-DockCycle $cycle
                continue
            }
            if (-not (Enter-Cyclops $A)) { Add-Problem "cycle $cycle : $A's entry didn't set her sub (state $((Get-State $A) | ConvertTo-Json -Compress -Depth 3))"; continue }
            $entries++
            Test-BothInside "cycle $cycle (entered)"
            Start-Sleep -Seconds 2
            Test-BothInside "cycle $cycle (2 s later)"
            if (-not (Exit-Cyclops $A)) { Add-Problem "cycle $cycle : $A's exit didn't clear her sub"; continue }
            $exits++
            # Alice breathes water outside: her oxygen is only compared while she stays inside.
            $script:lastOxygen[$A] = $null
            $serverSub = Wait-Until { $id = Get-ServerSubRootId $A; if (-not $id) { 'none' } } -TimeoutSeconds 5 -IntervalSeconds 0.5
            if (-not $serverSub) { Add-Problem "cycle $cycle : after the exit the server's SubRootId for $A is still '$(Get-ServerSubRootId $A)'" }
            $bob = Get-State $B
            if (-not (Test-InCyclops $bob) -or $bob.isUnderwater) { Add-Problem "cycle $cycle (Alice left): $B's sub '$($bob.currentSub.name)', underwater $($bob.isUnderwater)" }
            if ($cycle % 10 -eq 0) { Write-Host "  cycle $cycle : $($script:problems.Count) problems so far" }
        }
        Write-Host "  $entries hatch entries, $exits exits, $docks docks, $undocks undocks"
        $expectedDocks = if ($DockEvery -gt 0) { [math]::Floor($Cycles / $DockEvery) } else { 0 }
        Assert-Equal ($Cycles - $expectedDocks) $entries "$A got in through the hatch every time"
        Assert-Equal ($Cycles - $expectedDocks) $exits "$A got out through the hatch every time"
        Assert-Equal $expectedDocks $docks "$A docked a Seamoth into the Cyclops every time"
        Assert-Equal $expectedDocks $undocks "$A undocked it every time"
        Assert-Equal 0 $script:problems.Count "no interior-state problem (sub, underwater, oxygen, server SubRootId) in any cycle"
    }

    Step 'Bob is still inside' {
        $bob = Get-State $B
        Assert-True (Test-InCyclops $bob) "$B's currentSub is still the Cyclops"
        Assert-True (-not $bob.isUnderwater) "$B isn't underwater"
        Assert-Equal $cyclopsId (Get-ServerSubRootId $B) "the server has $B in the Cyclops"
    }
    Assert-NoDiff -Ids $cyclopsId 'no desync reported for the Cyclops'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S6 InteriorStateHolds' -Keep:$Keep
