<#
.SYNOPSIS
    FIX_SPEC S2 (#2696, #2576): scanner room upgrades and camera drones survive a server restart and a rejoin.

.DESCRIPTION
    Alice builds a scanner room (Habitat Builder path, nocost + fastbuild), puts two range upgrades into it
    (Inventory.AddOrSwap, what the storage UI does) and takes one camera drone out of its dock (Pickupable.OnHandClick).
    Both games and the server must agree: 2 upgrades, 1 docked drone, 1 drone in Alice's inventory. Then the server
    saves, everything is stopped and started again from that save (server restart + both players rejoin), and the same
    must hold: exactly 2 upgrades, exactly 1 docked drone (not 3 or 4), the drone still in Alice's inventory.

    Save "s2-maproom" (fresh world each run). Opens game windows. Needs a Debug build.
#>
param([switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$save = 's2-maproom'

function Get-Distance {
    param([double[]]$P, [double[]]$Q)
    [Math]::Sqrt(($P[0] - $Q[0]) * ($P[0] - $Q[0]) + ($P[1] - $Q[1]) * ($P[1] - $Q[1]) + ($P[2] - $Q[2]) * ($P[2] - $Q[2]))
}

<#
    Upgrades in the room's container, and the drones in the world within 15 m of the room (docked ones sit in the room;
    drones in an inventory are inactive and not found). Uses only game types, so it also works on a build without the fix.
#>
function Get-MapRoomState {
    param([string]$Player)
    $room = Invoke-BridgeJson $Player "entity $mapRoomId"
    $roomPosition = [double[]]$room.position
    [pscustomobject]@{
        Upgrades = [int](Invoke-Bridge $Player "get $mapRoomId MapRoomFunctionality.storageContainer.container.count")
        Drones = @(@(Invoke-BridgeJson $Player 'find MapRoomCamera') |
                   Where-Object { (Get-Distance ([double[]]$_.position) $roomPosition) -lt 15 } |
                   Where-Object { -not $_.id -or (Invoke-Bridge $Player "get $($_.id) Pickupable.attached" -AllowError) -ne 'true' } |
                   ForEach-Object { if ($_.id) { "$($_.id)" } else { '(no id)' } } | Sort-Object)
    }
}

function Assert-MapRoom {
    param([string]$Label, [int]$Upgrades, [string[]]$DockedIds)
    $expectedDrones = (@($DockedIds) | Sort-Object) -join ','
    foreach ($player in $A, $B) {
        $state = Wait-Until { $s = Get-MapRoomState $player; if ($s.Upgrades -eq $Upgrades -and ($s.Drones -join ',') -eq $expectedDrones) { $s } } -TimeoutSeconds 20
        if (-not $state) { $state = Get-MapRoomState $player }
        Assert-Equal $Upgrades $state.Upgrades "$Label`: $player's scanner room has $Upgrades upgrades"
        Assert-Equal $expectedDrones ($state.Drones -join ',') "$Label`: drones at $player's scanner room"
    }
    $server = Get-BridgeEntities 'server'
    $serverUpgrades = @($server.Entities.Values | Where-Object { $_.ParentId -eq $mapRoomId -and $_.TechType -eq 'MapRoomUpgradeScanRange' })
    Assert-Equal $Upgrades $serverUpgrades.Count "$Label`: the server has $Upgrades upgrades in the scanner room"
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $save

    Step 'Alice builds a scanner room' {
        $script:mapRoomId = Get-OrBuildMapRoom $A $B
        Write-Host "  scanner room $mapRoomId"
        $script:defaultDocked = @(Wait-Until { $ids = (Get-MapRoomState $A).Drones; if ($ids.Count -eq 2) { , $ids } } -TimeoutSeconds 10)
        Write-Host "  docked drones $($defaultDocked -join ', ')"
        Assert-MapRoom 'built' 0 $defaultDocked
    }

    Step 'Alice puts two range upgrades in' {
        foreach ($i in 1..2) {
            $before = @(Get-InventoryItem $A -TechType MapRoomUpgradeScanRange | ForEach-Object { $_.id })
            Invoke-Bridge $A 'console item mapRoomUpgradeScanRange' | Out-Null
            $item = Wait-Until { Get-InventoryItem $A -TechType MapRoomUpgradeScanRange | Where-Object { $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 10
            Assert-Equal 'true' (Invoke-Bridge $A "call Inventory.AddOrSwap $($item.id) $mapRoomId") "$A moved upgrade $i into the scanner room"
        }
        Assert-MapRoom 'upgrades in' 2 $defaultDocked
    }

    Step 'Alice takes a drone out of its dock' {
        $script:takenCamera = $defaultDocked[0]
        $script:dockedCamera = $defaultDocked[1]
        if ($takenCamera -eq '(no id)') {
            # Without the fix the drones are untracked local objects (#2576); the upgrade checks still run
            Add-Failure 'the docked drones have no ids'
            $script:takenCamera = $null
            $script:dockedCamera = $defaultDocked
            return
        }
        Invoke-Bridge $A "use $takenCamera Pickupable" | Out-Null
        $inInventory = Wait-Until { Get-InventoryItem $A -Id $takenCamera } -TimeoutSeconds 10
        Assert-True ([bool]$inInventory) "drone $takenCamera is in $A's inventory"
        $server = Wait-Until { $r = (Get-BridgeEntities 'server').Entities[$takenCamera]; if ($r -and $r.ParentId -ne '-' -and $r.ParentId -ne $mapRoomId) { $r } } -TimeoutSeconds 10
        Assert-True ([bool]$server) 'the server has the drone in a player inventory'
        Assert-MapRoom 'drone taken' 2 @($dockedCamera)
        Write-Host (Invoke-Bridge 'server' "query $mapRoomId")
    }

    Step 'Alice resyncs base buildings' {
        # The room isn't rebuilt by a resync (base geometry, not a module): its upgrades must not be doubled
        Invoke-Bridge $A 'call BuildingHandler.Main.AskForResync' | Out-Null
        Start-Sleep -Seconds 5
        $done = Wait-Until { (Invoke-Bridge $A 'get BuildingHandler.Main.Resyncing') -eq 'false' } -TimeoutSeconds 30
        Assert-True ([bool]$done) "$A's resync finished"
        Assert-MapRoom 'after resync' 2 @($dockedCamera)
    }

    Step 'Server saves, everything restarts from the save' {
        Restart-ScenarioFromSave $A $B $save
        Move-PlayersToMapRoom $A $B
        $loaded = Wait-Until { (Invoke-BridgeJson $A 'find MapRoomFunctionality' | Select-Object -First 1).id -eq $mapRoomId -and (Invoke-BridgeJson $B 'find MapRoomFunctionality' | Select-Object -First 1).id -eq $mapRoomId } -TimeoutSeconds 60
        Assert-True ([bool]$loaded) 'both games have the scanner room after the restart'
        Start-Sleep -Seconds 5
        Assert-MapRoom 'after restart' 2 @($dockedCamera)
        if ($takenCamera) {
            Assert-True ([bool](Get-InventoryItem $A -Id $takenCamera)) "drone $takenCamera is still in $A's inventory"
        }
    }

    Assert-NoDiff -Ids (@($mapRoomId, $takenCamera) + @($dockedCamera) | Where-Object { $_ }) 'no desync reported for the scanner room or its drones'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S2 MapRoomUpgradesPersist' -Keep:$Keep
