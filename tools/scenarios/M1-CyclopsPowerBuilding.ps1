<#
.SYNOPSIS
    MULTINAUGHTICA_SPEC M1 (Cyclops, power and building): undocking, battery charge, power-cell swap, building resync,
    a deconstructed loose constructable, and a Cyclops destroyed by damage with the partner aboard.

.DESCRIPTION
    Each step fails without its fix by construction:
      1. Moonpool with a docked Seamoth (BaseEquipmentHelpers). Alice clicks the docked Seamoth while another cinematic
         counts as running (PlayerCinematicController.cinematicModeCount = 1, what CinematicModeTriggerBase.StartCinematicMode
         refuses). Bob's bay must stay docked (before: VehicleUndocking start was sent at the lock grant, Bob's bay opened),
         and Bob must then be able to undock it himself (before: Alice kept the exclusive lock, Bob was refused).
      2. Bob (the Seamoth's lock holder) drains its power cell to 0.4 and then to 0 (EnergyMixin.ConsumeEnergy, what the
         game's consumers call). The server must store 0 (before: the patch sent the charge before the change, so it kept
         the value above the last whole number).
      3. Alice swaps the Seamoth's power cell for a new one (EnergyMixin.Select, what the slot UI calls). Bob's Seamoth must
         hold the new cell and have power (before: the new cell arrived while the old one filled the slot and was refused,
         then the old cell's pickup emptied the slot). Needs the power cell prefab cached on Bob's game (the sync spawn
         path), which the Seamoth's first cell does.
      4. Bob's copy of the moonpool base is destroyed (Object.Destroy), Bob asks for a building resync. The base must come
         back (before: the resync paired it with no component, the overwrite threw and the entity was dropped).
      5. Alice deconstructs a loose constructable (a world object with a Constructable that isn't a base piece or module,
         searched near $LooseSpot) with Constructable.DeconstructAsync, what the builder tool runs. Bob then loads that
         cell. The server must not log "Removing stale entry of <id>" (before: only the registry entry was removed, the
         cell entry stayed until S7's GetEntities clean-up found it), and diff must show nothing for it.
      6. A Cyclops with both players aboard; its lock holder takes the hits (three steps: SubRoot.OnTakeDamage only starts
         its 18 s countdown when neither the "hull low" nor the "hull critical" line fires in that hit). 5 s after the
         killing blow the partner's Cyclops must not have exploded and the server must store IsDestroyed False (before: the
         extractor sent IsDestroyed at health 0, the partner exploded it at once). 30 s after, both games and the server
         have it destroyed, and the server got one beacon and one set of loot (15 scrap metal).
      7. A second Cyclops: the holder takes the killing blow and quits at once. The partner's game must destroy it once
         the countdown should have ended (CyclopsDestructionWatchdog; before: it stayed at 0 HP for good).
    Not covered: vehicle lights and the thermal reactor running once (they need a long drain at a vent to show), the
    Mobile Vehicle Bay's build bots (only a NullReferenceException in the log), the beacon when the lock moves during the
    countdown.

    Starts a fresh world (save m1-cyclops-power) and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\M1-CyclopsPowerBuilding.ps1
    Written 2026-11-02, not run yet (MULTINAUGHTICA_SPEC "Testing": game-rig runs deferred to one final pass).
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'm1-cyclops-power',
    # Open water over the Grassy Plateaus (T4, S13)
    [double[]]$CyclopsArea = @(263, -60, 64),
    # Where a loose constructable is searched: the Aurora's cargo room (A0 probes). Unverified that one is there.
    [double[]]$LooseSpot = @(940, 14, 18))

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseEquipmentHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$inv = [Globalization.CultureInfo]::InvariantCulture

function Format-Position {
    param([double[]]$P)
    ($P | ForEach-Object { $_.ToString('0.###', $inv) }) -join ' '
}

function Get-Number {
    param([string]$Player, [string]$Path)
    [double]::Parse((Invoke-Bridge $Player "get $Path").Trim('"'), $inv)
}

function Get-Mode {
    param([string]$Player)
    (Invoke-Bridge $Player 'get player Player.mode').Trim('"')
}

function Wait-NoCinematic {
    param([string]$Player)
    Wait-Until { [int](Invoke-Bridge $Player 'get PlayerCinematicController.cinematicModeCount') -eq 0 -and (Invoke-Bridge $Player 'get player Player.cinematicModeActive') -eq 'false' } -TimeoutSeconds 20 -IntervalSeconds 0.3
}

function Get-LockOwner {
    param([string]$Id)
    if ((Invoke-Bridge 'server' "query $Id") -match 'Owner: (\w+)') { $Matches[1] } else { '<none>' }
}

function Get-ServerText {
    param([string]$Id)
    Invoke-Bridge 'server' "query $Id" -AllowError
}

function Get-ServerCount {
    param([string]$TechType)
    @((Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.TechType -eq $TechType }).Count
}

function Get-ServerLogMark {
    $mark = @{}
    Get-ChildItem (Get-NitroxRig).LogsDir -Filter '*_server_*.log' | ForEach-Object { $mark[$_.FullName] = @(Get-Content $_.FullName).Count }
    $mark
}

function Get-ServerLogLines {
    param([hashtable]$Mark, [string]$Pattern)
    Get-ChildItem (Get-NitroxRig).LogsDir -Filter '*_server_*.log' | ForEach-Object {
        $skip = if ($Mark.ContainsKey($_.FullName)) { $Mark[$_.FullName] } else { 0 }
        Get-Content $_.FullName | Select-Object -Skip $skip
    } | Where-Object { $_ -match $Pattern }
}

function Set-NoEnergy {
    param([string]$Player, [bool]$On)
    $requiresPower = (Invoke-Bridge $Player 'call GameModeUtils.RequiresPower') -eq 'true'
    if ($requiresPower -eq $On) { Invoke-Bridge $Player 'console noenergy' | Out-Null }
    Assert-Equal (-not $On).ToString().ToLower() (Invoke-Bridge $Player 'call GameModeUtils.RequiresPower') "$Player's noenergy cheat is $(if ($On) { 'on' } else { 'off' })"
}

function New-Cyclops {
    param([double[]]$At)
    Invoke-Bridge $A ('teleport ' + (Format-Position $At)) | Out-Null
    Invoke-Bridge $A 'look 0 0' | Out-Null
    Start-Sleep -Seconds 3
    $before = @(@(Invoke-BridgeJson $A 'find SubRoot') | ForEach-Object { $_.id })
    Invoke-Bridge $A 'console sub cyclops' | Out-Null
    $sub = Wait-Until { @(Invoke-BridgeJson $A 'find SubRoot') | Where-Object { $_.name -like 'Cyclops*' -and $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 60
    if (-not $sub) { throw 'no Cyclops spawned' }
    Invoke-Bridge $B ('teleport ' + (Format-Position @($At[0] + 5, $At[1], $At[2]))) | Out-Null
    if (-not (Wait-Until { Invoke-BridgeJson $B "entity $($sub.id)" } -TimeoutSeconds 60)) { throw "Bob doesn't have Cyclops $($sub.id)" }
    Start-Sleep -Seconds 3
    $sub.id
}

function Enter-Cyclops {
    param([string]$Player, [string]$CyclopsId)
    $hatch = (Invoke-BridgeJson $Player 'find CinematicModeTrigger 40') | Where-Object { $_.name -eq 'EntranceHatch' -and $_.id -eq "(in $CyclopsId)" } | Select-Object -First 1
    if (-not $hatch) { throw "$Player doesn't see the Cyclops' entrance hatch" }
    Invoke-Bridge $Player ('teleport ' + (Format-Position @($hatch.position[0], ($hatch.position[1] - 6.9), $hatch.position[2]))) | Out-Null
    Start-Sleep -Milliseconds 500
    Invoke-Bridge $Player 'use find:CinematicModeTrigger#EntranceHatch' | Out-Null
    $inside = Wait-Until { (Invoke-BridgeJson $Player 'state').currentSub.id -eq $CyclopsId } -TimeoutSeconds 15 -IntervalSeconds 0.3
    Wait-NoCinematic $Player | Out-Null
    [bool]$inside
}

<#
    The lock holder's game hits the Cyclops down to 40 %, 20 %, then 0 (each hit through LiveMixin.TakeDamage, the game's
    damage entry). Returns the time of the killing blow.
#>
function Invoke-KillingBlow {
    param([string]$Holder, [string]$CyclopsId)
    $max = Get-Number $Holder "$CyclopsId LiveMixin.maxHealth"
    # The scenario's no-damage cheat zeroes all damage the cheating game deals (DamageSystem.CalculateDamage)
    Invoke-Bridge $Holder 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
    foreach ($fraction in 0.4, 0.2) {
        $health = Get-Number $Holder "$CyclopsId LiveMixin.health"
        Invoke-Bridge $Holder ("call $CyclopsId LiveMixin.TakeDamage " + ($health - $fraction * $max).ToString($inv)) | Out-Null
        Start-Sleep -Seconds 1
    }
    Invoke-Bridge $Holder ("call $CyclopsId LiveMixin.TakeDamage " + $max.ToString($inv)) | Out-Null
    $blow = Get-Date
    Invoke-Bridge $Holder 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
    Assert-Equal 0 (Get-Number $Holder "$CyclopsId LiveMixin.health") "the holder ($Holder) has the Cyclops at 0 HP"
    $blow
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'A moonpool with a docked Seamoth' {
        $script:base = Get-OrBuildDockedSeamoth $A $B
        Write-Host "  base $($base.BaseId), bay $($base.DockId), Seamoth $($base.SeamothId), power cell $($base.PowerCellId)"
        Assert-True ([bool]$base.PowerCellId) 'the server has the Seamoth with a power cell'
    }

    Step 'Alice clicks the docked Seamoth, the game refuses (another cinematic): nothing undocks, the lock is released' {
        Invoke-Bridge $A 'set PlayerCinematicController.cinematicModeCount 1' | Out-Null
        Assert-Equal 1 (Invoke-Bridge $A 'get PlayerCinematicController.cinematicModeCount') "Alice's game counts a cinematic as running"
        Invoke-Bridge $A 'use find:DockedVehicleHandTarget' | Out-Null
        Start-Sleep -Seconds 5
        Invoke-Bridge $A 'set PlayerCinematicController.cinematicModeCount 0' | Out-Null
        Assert-Equal 'Normal' (Get-Mode $A) 'Alice is not in the Seamoth'
        foreach ($player in $A, $B) {
            Assert-Equal 'true' (Invoke-Bridge $player "get $($base.DockId) VehicleDockingBay.vehicle_docked_param") "$player's bay still holds the Seamoth (no undocking announced)"
        }
        Write-Host "  Seamoth lock: $(Get-LockOwner $base.SeamothId)"
    }

    Step 'Bob undocks the Seamoth himself (the lock Alice was granted is free again)' {
        Invoke-Bridge $B 'use find:DockedVehicleHandTarget' | Out-Null
        $piloting = Wait-Until { (Get-Mode $B) -eq 'Piloting' } -TimeoutSeconds 20 -IntervalSeconds 0.5
        Assert-True ([bool]$piloting) 'Bob pilots the Seamoth out of the bay'
        $opened = Wait-Until { (Invoke-Bridge $A "get $($base.DockId) VehicleDockingBay.vehicle_docked_param") -eq 'false' } -TimeoutSeconds 10
        Assert-True ([bool]$opened) "Alice's game replays Bob's undocking"
        Wait-NoCinematic $B | Out-Null
        Start-Sleep -Seconds 8
        Assert-Equal $B (Get-LockOwner $base.SeamothId) 'Bob holds the Seamoth'
    }

    Step 'Bob drains the power cell to 0.4, then to 0: the server stores 0' {
        foreach ($player in $A, $B) { Set-NoEnergy $player $false }
        $charge = Get-Number $B "$($base.SeamothId) EnergyMixin.charge"
        Write-Host "  charge $charge"
        Invoke-Bridge $B ("call $($base.SeamothId) EnergyMixin.ConsumeEnergy " + ($charge - 0.4).ToString($inv)) | Out-Null
        Start-Sleep -Seconds 1
        Invoke-Bridge $B "call $($base.SeamothId) EnergyMixin.ConsumeEnergy 1" | Out-Null
        Assert-Equal 0 (Get-Number $B "$($base.SeamothId) EnergyMixin.charge") "Bob's power cell is empty"
        $stored = Wait-Until { if ((Get-ServerText $base.PowerCellId) -match 'Charge: ([-0-9.E]+)') { $c = [double]::Parse($Matches[1], $inv); if ($c -le 0) { "$c" } } } -TimeoutSeconds 10
        Assert-True ($null -ne $stored) "the server stores the empty power cell ($(if ((Get-ServerText $base.PowerCellId) -match 'Charge: ([^\]]+)') { $Matches[1] }))"
        Assert-Equal 0 (Get-Number $A "$($base.SeamothId) EnergyMixin.charge") "Alice's copy of the power cell is empty too"
    }

    Step 'Alice swaps the empty power cell for a new one: Bob has the new one and power' {
        $inventoryBefore = @(Get-InventoryItem $A | ForEach-Object { $_.id })
        Invoke-Bridge $A 'console item powercell' | Out-Null
        $cell = Wait-Until { Get-InventoryItem $A -TechType 'PowerCell' | Where-Object { $inventoryBefore -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 10
        Assert-True ([bool]$cell) 'Alice has a new power cell'
        $script:newCellId = $cell.id
        Invoke-Bridge $A "call $($base.SeamothId) EnergyMixin.Select $newCellId" | Out-Null
        Start-Sleep -Seconds 3
        foreach ($player in $A, $B) {
            $stored = Invoke-BridgeJson $player "get $($base.SeamothId) EnergyMixin.batterySlot.storedItem.item.gameObject"
            Assert-Equal $newCellId $stored.id "$player's Seamoth holds the new power cell"
            Assert-True ((Get-Number $player "$($base.SeamothId) EnergyMixin.charge") -gt 0) "$player's Seamoth has power"
        }
        $server = Get-BridgeEntities 'server'
        Assert-Equal $base.SeamothId $server.Entities[$newCellId].ParentId 'the server has the new cell in the Seamoth'
        Assert-True ($server.Entities[$base.PowerCellId].ParentId -ne $base.SeamothId) 'the server has the old cell out of the Seamoth'
    }
    Assert-NoDiff -Ids @($base.SeamothId, $newCellId, $base.PowerCellId) 'no desync for the Seamoth and its power cells'

    Step "Bob's copy of the base is lost; a building resync brings it back" {
        Invoke-Bridge $B "call UnityEngine.Object.Destroy $($base.BaseId)" | Out-Null
        $gone = Wait-Until { -not (@(Invoke-BridgeJson $B 'find Base') | Where-Object { $_.id -eq $base.BaseId }) } -TimeoutSeconds 10
        Assert-True ([bool]$gone) "Bob's game no longer has the base"
        # 10 s cooldown between requests (BuildingHandler.ResyncRequestCooldown)
        Start-Sleep -Seconds 11
        Invoke-Bridge $B 'call BuildingHandler.Main.AskForResync' | Out-Null
        Start-Sleep -Seconds 3
        $done = Wait-Until { (Invoke-Bridge $B 'get BuildingHandler.Main.Resyncing') -eq 'false' } -TimeoutSeconds 60
        Assert-True ([bool]$done) "Bob's resync finished"
        $back = Wait-Until { @(Invoke-BridgeJson $B 'find Base') | Where-Object { $_.id -eq $base.BaseId } } -TimeoutSeconds 20
        Assert-True ([bool]$back) "the resync spawned the base again on Bob's game"
    }
    Assert-NoDiff -Ids @($base.BaseId) 'no desync for the base after the resync'

    Step 'Alice deconstructs a loose constructable; Bob loading its cell gets no stale entry' {
        Enable-BuildCheats $A
        Invoke-Bridge $A ('teleport ' + (Format-Position $LooseSpot)) | Out-Null
        Start-Sleep -Seconds 8
        $server = Get-BridgeEntities 'server'
        $loose = @(Invoke-BridgeJson $A 'find Constructable 60') | Where-Object {
            $_.id -match '^[0-9a-f-]{36}$' -and $server.Entities[$_.id] -and $server.Entities[$_.id].Kind -notmatch 'Ghost|Module|Build|Base' -and
            (Invoke-Bridge $A "get $($_.id) Constructable.constructed" -AllowError) -eq 'true' -and
            (Invoke-Bridge $A "get $($_.id) ConstructableBase" -AllowError) -like 'ERR*'
        } | Select-Object -First 1
        if (-not $loose) { throw "no loose constructable with an id near $(Format-Position $LooseSpot): pick another -LooseSpot" }
        $script:looseId = $loose.id
        Write-Host "  $($loose.name) $looseId ($($server.Entities[$looseId].TechType), $($server.Entities[$looseId].Kind)) at $($loose.distance) m"
        Invoke-Bridge $A "call $looseId Constructable.SetState false false" | Out-Null
        for ($i = 0; $i -lt 200; $i++) {
            if ((Invoke-Bridge $A "entity $looseId" -AllowError) -like 'ERR*') { break }
            Invoke-Bridge $A "call $looseId Constructable.DeconstructAsync _ _" -AllowError | Out-Null
            Start-Sleep -Milliseconds 100
        }
        Assert-True ((Invoke-Bridge $A "entity $looseId" -AllowError) -like 'ERR*') "Alice's game deconstructed it"
        $removed = Wait-Until { -not (Get-BridgeEntities 'server').Entities[$looseId] } -TimeoutSeconds 10
        Assert-True ([bool]$removed) 'the server removed it'
        $mark = Get-ServerLogMark
        Invoke-Bridge $B ('teleport ' + (Format-Position @($LooseSpot[0] + 3, $LooseSpot[1], $LooseSpot[2]))) | Out-Null
        Start-Sleep -Seconds 10
        $stale = @(Get-ServerLogLines $mark "Removing stale entry of $looseId")
        Assert-Equal 0 $stale.Count "the server had no stale cell entry left for it when Bob loaded the cell"
        Assert-True ((Invoke-Bridge $B "entity $looseId" -AllowError) -like 'ERR*') "Bob's game doesn't spawn it"
    }
    Assert-NoDiff -Ids @($looseId) 'no desync for the deconstructed constructable'

    Step 'A Cyclops with both players aboard' {
        $script:cyclopsId = New-Cyclops $CyclopsArea
        Write-Host "  Cyclops $cyclopsId"
        foreach ($player in $A, $B) { Assert-True (Enter-Cyclops $player $cyclopsId) "$player is aboard" }
        $script:holder = Get-LockOwner $cyclopsId
        Assert-True ($holder -in $A, $B) "a player holds the Cyclops ($holder)"
        $script:partner = if ($holder -eq $A) { $B } else { $A }
        $script:beaconsBefore = Get-ServerCount 'Beacon'
        $script:scrapBefore = Get-ServerCount 'ScrapMetal'
    }

    Step 'The holder takes the killing blow: 5 s later the partner is still aboard an intact Cyclops' {
        $script:blow = Invoke-KillingBlow $holder $cyclopsId
        Start-Sleep -Seconds 5
        Assert-Equal 'false' (Invoke-Bridge $partner "get $cyclopsId SubRoot.subDestroyed") "the partner's ($partner) Cyclops hasn't exploded"
        Assert-Equal 'false' (Invoke-Bridge $holder "get $cyclopsId SubRoot.subDestroyed") "the holder's Cyclops hasn't exploded yet"
        Assert-Equal 0 (Get-Number $partner "$cyclopsId LiveMixin.health") "the partner's Cyclops is at 0 HP"
        Assert-True ((Get-Number $partner 'player LiveMixin.health') -gt 0) 'the partner is alive'
        Assert-Equal $cyclopsId (Invoke-BridgeJson $partner 'state').currentSub.id 'the partner is still aboard'
        Assert-True ((Get-ServerText $cyclopsId) -match 'IsDestroyed: False') 'the server stores the Cyclops as not destroyed'
    }

    Step 'After the countdown both games and the server have it destroyed, with one beacon and one set of loot' {
        $wait = 30 - ((Get-Date) - $blow).TotalSeconds
        if ($wait -gt 0) { Start-Sleep -Seconds ([int][Math]::Ceiling($wait)) }
        foreach ($player in $A, $B) {
            Assert-Equal 'true' (Invoke-Bridge $player "get $cyclopsId SubRoot.subDestroyed" -AllowError) "$player's Cyclops exploded"
        }
        Assert-True ((Get-ServerText $cyclopsId) -match 'IsDestroyed: True') 'the server stores the Cyclops as destroyed'
        Assert-Equal ($beaconsBefore + 1) (Get-ServerCount 'Beacon') 'the server got one beacon'
        Assert-Equal ($scrapBefore + 15) (Get-ServerCount 'ScrapMetal') 'the server got one set of loot (15 scrap metal)'
    }

    Step "A second Cyclops: the holder takes the killing blow and leaves, the partner's game ends the countdown" {
        # The explosion may have killed whoever was still aboard (game:CyclopsDestructionEvent.Update): wait for the respawn
        foreach ($player in $A, $B) {
            if ((Get-Mode $player) -ne 'Normal') { Invoke-Bridge $player 'call player Player.TryEject' -AllowError | Out-Null }
            $alive = Wait-Until { (Get-Number $player 'player LiveMixin.health') -gt 0 -and (Invoke-Bridge $player 'get player Player.cinematicModeActive') -eq 'false' } -TimeoutSeconds 60
            Assert-True ([bool]$alive) "$player is alive and free to move"
        }
        $script:secondId = New-Cyclops @($CyclopsArea[0] + 120, $CyclopsArea[1], $CyclopsArea[2])
        $owner = Get-LockOwner $secondId
        Assert-True ($owner -in $A, $B) "a player holds the second Cyclops ($owner)"
        $stayer = if ($owner -eq $A) { $B } else { $A }
        $blow = Invoke-KillingBlow $owner $secondId
        Invoke-Bridge $owner 'call IngameMenu.main.QuitGame false' | Out-Null
        $left = Wait-Until { -not ((Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $owner -and $_.online }) } -TimeoutSeconds 60
        $leftAfter = ((Get-Date) - $blow).TotalSeconds
        Assert-True ($left -and $leftAfter -lt 16) "$owner left before the countdown ended ($([int]$leftAfter) s after the blow)"
        $taken = Wait-Until { (Get-LockOwner $secondId) -eq $stayer } -TimeoutSeconds 15
        Assert-True ([bool]$taken) "$stayer took over the Cyclops"
        $destroyed = Wait-Until { (Invoke-Bridge $stayer "get $secondId SubRoot.subDestroyed" -AllowError) -eq 'true' } -TimeoutSeconds 45
        Assert-True ([bool]$destroyed) "$stayer's game destroyed the Cyclops once the countdown should have ended"
        Assert-True ((Get-ServerText $secondId) -match 'IsDestroyed: True') 'the server stores it as destroyed'
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'M1 CyclopsPowerBuilding' -Keep:$Keep
