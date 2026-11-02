<#
    Building helpers for scenarios (dot-source after ScenarioHelpers.ps1). They go through the game's own building code
    (Builder.BeginAsync -> BuildModeInputHandler -> Builder.TryPlace -> Constructable.Construct), so the Nitrox building
    patches run as they do for a player with the Habitat Builder.

    Enable-BuildCheats once per game (nocost + fastbuild), then New-Construction for each piece: teleport to where the
    player would stand, look at the spot, place and construct.
#>

$script:Invariant = [Globalization.CultureInfo]::InvariantCulture

function Format-BridgeNumber {
    param([double]$Value)
    $Value.ToString($script:Invariant)
}

function Enable-BuildCheats {
    param([Parameter(Mandatory)][string]$Player)
    if ((Invoke-Bridge $Player 'call GameModeUtils.RequiresIngredients') -eq 'true') {
        Invoke-Bridge $Player 'console nocost' | Out-Null
    }
    if ((Invoke-Bridge $Player 'get NoCostConsoleCommand.main.fastBuildCheat') -ne 'true') {
        Invoke-Bridge $Player 'console fastbuild' | Out-Null
    }
    Assert-Equal 'false' (Invoke-Bridge $Player 'call GameModeUtils.RequiresIngredients') "$Player builds without ingredients"
    Assert-Equal 'true' (Invoke-Bridge $Player 'get NoCostConsoleCommand.main.fastBuildCheat') "$Player builds fast"
}

<#
    Places a piece (base piece or module) the way the Habitat Builder does and constructs it completely. Returns the
    id of the new ghost/constructable (for base pieces Nitrox moves this id to the built piece or base).
#>
function New-Construction {
    param(
        [Parameter(Mandatory)][string]$Player,
        [Parameter(Mandatory)][string]$TechType,
        [Parameter(Mandatory)][double[]]$StandAt,
        [double]$Yaw = 0,
        # Negative is down
        [double]$Pitch = -30,
        [string]$ConstructableType = 'Constructable'
    )
    Invoke-Bridge $Player ('teleport {0} {1} {2}' -f (Format-BridgeNumber $StandAt[0]), (Format-BridgeNumber $StandAt[1]), (Format-BridgeNumber $StandAt[2])) | Out-Null
    Invoke-Bridge $Player ('look {0} {1}' -f (Format-BridgeNumber $Yaw), (Format-BridgeNumber $Pitch)) | Out-Null
    Start-Sleep -Seconds 2
    $before = @((Invoke-BridgeJson $Player "find $ConstructableType") | ForEach-Object { $_.id })
    Invoke-Bridge $Player 'call Builder.End' | Out-Null
    Invoke-Bridge $Player "call Builder.BeginAsync $TechType" | Out-Null
    $canPlace = Wait-Until { (Invoke-Bridge $Player 'get Builder.canPlace') -eq 'true' } -TimeoutSeconds 15 -IntervalSeconds 0.5
    if (-not $canPlace) {
        Invoke-Bridge $Player 'call Builder.End' | Out-Null
        throw "$Player can't place $TechType from $($StandAt -join ', ') looking $Yaw/$Pitch"
    }
    Assert-Equal 'true' (Invoke-Bridge $Player 'call Builder.TryPlace') "$Player placed $TechType"
    # A click would make BuildModeInputHandler leave the input stack; after a direct TryPlace it stays (and blocks the next
    # Builder.BeginAsync) unless the builder is ended
    Invoke-Bridge $Player 'call Builder.End' | Out-Null
    $ghost = Wait-Until {
        @(Invoke-BridgeJson $Player "find $ConstructableType") | Where-Object { $_.id -and $_.id -notlike '(in*' -and $before -notcontains $_.id } | Select-Object -First 1
    } -TimeoutSeconds 10 -IntervalSeconds 0.5
    if (-not $ghost) { throw "No new $ConstructableType with an id after placing $TechType" }
    $id = $ghost.id
    # A finished base piece loses its ghost (the id moves to the base or module), so stop when it's gone or constructed.
    for ($i = 0; $i -lt 300; $i++) {
        $constructed = Invoke-Bridge $Player "get $id Constructable.constructed" -AllowError
        if ($constructed -ne 'false') { break }
        Invoke-Bridge $Player "call $id Constructable.Construct" -AllowError | Out-Null
    }
    Write-Host "  built $TechType ($id)"
    $id
}

# ---- Scanner room (FIX_SPEC S2/S10) ----

# Where the builder stands to place the scenarios' scanner room (safe shallows, no stalkers within 100 m: they carry drones
# off. Looking north and 30 degrees down puts it on the sand at about (60, -26, 207); found in game 2026-11-02)
$script:MapRoomStand = @(60.0, -22.0, 200.0)

<#
    Both players next to the scanner room site, with the noenergy cheat on (a scanner room's screen, uGUI_MapRoomScanner,
    is only active while the room has power).
#>
function Move-PlayersToMapRoom {
    param([string]$Builder, [string]$Partner)
    $s = $script:MapRoomStand
    Invoke-Bridge $Builder ('teleport {0} {1} {2}' -f (Format-BridgeNumber $s[0]), (Format-BridgeNumber $s[1]), (Format-BridgeNumber $s[2])) | Out-Null
    Invoke-Bridge $Partner ('teleport {0} {1} {2}' -f (Format-BridgeNumber ($s[0] + 3)), (Format-BridgeNumber $s[1]), (Format-BridgeNumber ($s[2] - 1))) | Out-Null
    foreach ($player in $Builder, $Partner) {
        if ((Invoke-Bridge $player 'call GameModeUtils.RequiresPower') -eq 'true') {
            Invoke-Bridge $player 'console noenergy' | Out-Null
        }
    }
}

<#
    Id of the scanner room at the site, built by $Builder if there is none yet. Both games must have it with the same id.
#>
function Get-OrBuildMapRoom {
    param([string]$Builder, [string]$Partner)
    Move-PlayersToMapRoom $Builder $Partner
    Start-Sleep -Seconds 3
    $existing = @(Invoke-BridgeJson $Builder 'find MapRoomFunctionality') | Where-Object { $_.distance -lt 30 } | Select-Object -First 1
    if (-not $existing) {
        Enable-BuildCheats $Builder
        New-Construction -Player $Builder -TechType BaseMapRoom -StandAt $script:MapRoomStand -Yaw 0 -Pitch -30 -ConstructableType ConstructableBase | Out-Null
        $existing = Wait-Until { @(Invoke-BridgeJson $Builder 'find MapRoomFunctionality') | Where-Object { $_.distance -lt 30 -and $_.id } | Select-Object -First 1 } -TimeoutSeconds 20
    }
    $id = $existing.id
    Assert-True ([bool]$id -and $id -notlike '(in*') "$Builder's scanner room has an id ($id)"
    $seen = Wait-Until { @(Invoke-BridgeJson $Partner 'find MapRoomFunctionality') | Where-Object { $_.id -eq $id } } -TimeoutSeconds 30
    Assert-True ([bool]$seen) "$Partner's scanner room has the same id"
    $id
}

<#
    Starts a scan the way the scanner screen's resource buttons do (uGUI_MapRoomScanner.OnStartScan(index on the page)).
    Returns the tech type scanned for: $Preferred if the room lists it, else the first listed one.
#>
function Start-MapRoomScan {
    param([string]$Player, [string]$MapRoomId, [string]$Preferred)
    $ui = 'find:uGUI_MapRoomScanner uGUI_MapRoomScanner'
    $types = @(Wait-Until { $t = @(Invoke-BridgeJson $Player "get $ui.sortedTechTypes"); if ($t.Count -gt 0) { , $t } } -TimeoutSeconds 20)
    if ($types.Count -eq 0) { throw "$Player's scanner screen lists no resources" }
    $index = [Array]::IndexOf([string[]]$types, $Preferred)
    if ($index -lt 0) { $index = 0 }
    $pageSize = @(Invoke-BridgeJson $Player "get $ui.resourceList").Count
    Invoke-Bridge $Player "set $ui.currentPage $([Math]::Floor($index / $pageSize))" | Out-Null
    Invoke-Bridge $Player "call $ui.OnStartScan $($index % $pageSize)" | Out-Null
    $types[$index]
}

function Get-TypeToScan {
    param([string]$Player, [string]$MapRoomId)
    (Invoke-Bridge $Player "get $MapRoomId MapRoomFunctionality.typeToScan" -AllowError).Trim('"')
}

function Get-MapRoomNodeIds {
    param([string]$Player, [string]$MapRoomId)
    @(@(Invoke-BridgeJson $Player "call $MapRoomId MapRoomFunctionality.GetNodes") | ForEach-Object { $_.uniqueId } | Sort-Object)
}

# ---- Moonpool with a docked Seamoth (FIX_SPEC S2) ----

# Where the builder stands to place the moonpool (safe shallows; looking north and 30 degrees down puts it on the sand,
# its docking bay at about (-55, -15, 325.8); found in game 2026-11-02)
$script:MoonpoolStand = @(-55.0, -4.0, 312.0)

<#
    A moonpool at the site with a Seamoth docked in it that has $Modules installed (console item names), built by
    $Builder if there is none yet. Returns @{ BaseId; DockId; SeamothId; ModuleIds; PowerCellId }.
#>
function Get-OrBuildDockedSeamoth {
    param([string]$Builder, [string]$Partner, [string[]]$Modules = @('vehiclehullmodule1', 'seamothsolarcharge'))
    $s = $script:MoonpoolStand
    Invoke-Bridge $Builder ('teleport {0} {1} {2}' -f (Format-BridgeNumber $s[0]), (Format-BridgeNumber $s[1]), (Format-BridgeNumber $s[2])) | Out-Null
    Invoke-Bridge $Partner ('teleport {0} {1} {2}' -f (Format-BridgeNumber ($s[0] + 3)), (Format-BridgeNumber $s[1]), (Format-BridgeNumber ($s[2] - 1))) | Out-Null
    foreach ($player in $Builder, $Partner) {
        if ((Invoke-Bridge $player 'call GameModeUtils.RequiresPower') -eq 'true') { Invoke-Bridge $player 'console noenergy' | Out-Null }
    }
    Start-Sleep -Seconds 3
    $dock = @(Invoke-BridgeJson $Builder 'find VehicleDockingBay') | Where-Object { $_.distance -lt 40 -and $_.id } | Select-Object -First 1
    if (-not $dock) {
        Enable-BuildCheats $Builder
        New-Construction -Player $Builder -TechType BaseMoonpool -StandAt $s -Yaw 0 -Pitch -30 -ConstructableType ConstructableBase | Out-Null
        $dock = Wait-Until { @(Invoke-BridgeJson $Builder 'find VehicleDockingBay') | Where-Object { $_.distance -lt 40 -and $_.id } | Select-Object -First 1 } -TimeoutSeconds 20
    }
    $dockId = $dock.id
    $seamoth = (Invoke-Bridge $Builder "get $dockId VehicleDockingBay.dockedVehicle.gameObject" -AllowError)
    $seamoth = if ($seamoth.StartsWith('{')) { $seamoth | ConvertFrom-Json } else { $null }
    if (-not $seamoth) {
        $before = @(@(Invoke-BridgeJson $Builder 'find SeaMoth') | ForEach-Object { $_.id })
        Invoke-Bridge $Builder 'console spawn seamoth' | Out-Null
        $new = Wait-Until { @(Invoke-BridgeJson $Builder 'find SeaMoth') | Where-Object { $_.id -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
        $seamothId = $new.id
        $slot = 1
        foreach ($module in $Modules) {
            $inventoryBefore = @(Get-InventoryItem $Builder | ForEach-Object { $_.id })
            Invoke-Bridge $Builder "console item $module" | Out-Null
            $item = Wait-Until { Get-InventoryItem $Builder | Where-Object { $inventoryBefore -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 10
            Assert-Equal 'true' (Invoke-Bridge $Builder "call Inventory.AddOrSwap $($item.id) $seamothId SeamothModule$slot") "$Builder installed $module"
            $slot++
        }
        # Into the docking trigger, then the trigger call the physics step would make (needs the vehicle's simulation lock)
        $p = $dock.position
        Invoke-Bridge $Builder ('set {0} Transform.position {1},{2},{3}' -f $seamothId, (Format-BridgeNumber $p[0]), (Format-BridgeNumber ($p[1] + 3)), (Format-BridgeNumber $p[2])) | Out-Null
        Start-Sleep -Seconds 1
        Invoke-Bridge $Builder "call $dockId VehicleDockingBay.OnTriggerEnter $seamothId" | Out-Null
    }
    else {
        $seamothId = $seamoth.id
    }
    foreach ($player in $Builder, $Partner) {
        $docked = Wait-Until { (Invoke-Bridge $player "get $seamothId Vehicle.docked" -AllowError) -eq 'true' } -TimeoutSeconds 20
        Assert-True ([bool]$docked) "$player has the Seamoth docked"
    }
    $children = @((Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.ParentId -eq $seamothId })
    [pscustomobject]@{
        BaseId = (Invoke-BridgeJson $Builder "entity $dockId").parentId
        DockId = $dockId
        SeamothId = $seamothId
        ModuleIds = @($children | Where-Object { $_.Kind -eq 'InstalledModuleEntity' } | ForEach-Object { $_.Id } | Sort-Object)
        PowerCellId = ($children | Where-Object { $_.TechType -eq 'PowerCell' } | Select-Object -First 1).Id
    }
}

function Get-SeamothModuleIds {
    param([string]$Player, [string]$SeamothId)
    @(((Invoke-BridgeJson $Player "get $SeamothId Vehicle.modules.equipment").PSObject.Properties | Where-Object { $_.Value } | ForEach-Object { $_.Value.item.id }) | Sort-Object)
}

<#
    Both games have the Seamoth docked with exactly $ModuleIds, and the server has those modules and $PowerCellId under it.
#>
function Assert-DockedSeamoth {
    param([string]$Label, [string[]]$Players, [string]$SeamothId, [string[]]$ModuleIds, [string]$PowerCellId)
    $expected = (@($ModuleIds) | Sort-Object) -join ','
    foreach ($player in $Players) {
        $modules = Wait-Until { $m = (Get-SeamothModuleIds $player $SeamothId) -join ','; if ($m -eq $expected) { $m } } -TimeoutSeconds 20
        if (-not $modules) { $modules = (Get-SeamothModuleIds $player $SeamothId) -join ',' }
        Assert-Equal $expected $modules "$Label`: $player's Seamoth modules"
        Assert-Equal 'true' (Invoke-Bridge $player "get $SeamothId Vehicle.docked" -AllowError) "$Label`: $player's Seamoth is docked"
    }
    $server = Get-BridgeEntities 'server'
    $serverModules = (@($server.Entities.Values | Where-Object { $_.ParentId -eq $SeamothId -and $_.Kind -eq 'InstalledModuleEntity' } | ForEach-Object { $_.Id }) | Sort-Object) -join ','
    Assert-Equal $expected $serverModules "$Label`: the server's Seamoth modules"
    if ($PowerCellId) {
        Assert-Equal $SeamothId $server.Entities[$PowerCellId].ParentId "$Label`: the server still has the power cell in the Seamoth"
    }
}

<#
    Server saves, the server and both games are stopped and started again from that save (a server restart in which both
    players rejoin).
#>
function Restart-ScenarioFromSave {
    param([string]$PlayerA, [string]$PlayerB, [string]$SaveName)
    Write-Host (Invoke-Bridge 'server' 'save')
    Start-Sleep -Seconds 3
    # -Stop waits until they are gone.
    & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop | Out-Null
    Start-ScenarioGames -Players $PlayerA, $PlayerB -SaveName $SaveName -KeepSave
}
