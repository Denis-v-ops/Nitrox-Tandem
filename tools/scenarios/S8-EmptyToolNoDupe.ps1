<#
.SYNOPSIS
    FIX_SPEC S8 (#2668): a battery-powered tool whose battery was removed must not get a new battery when another
    player picks it up.

.DESCRIPTION
    Alice spawns a flashlight (console "item flashlight", it comes with a battery), takes the battery out
    (EnergyMixin.Select(null), what the battery slot UI calls), drops the empty flashlight
    (Inventory.InternalDropItem, what the inventory's drop action calls), and Bob picks it up (Pickupable.OnHandClick).
    Then Bob's flashlight has no battery, the server has no battery under it, and the server has as many batteries as
    before the pickup.

    Starts a fresh world and two games (opens windows), unless -NoLaunch. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Get-ServerBatteryCount {
    @((Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.TechType -eq 'Battery' }).Count
}

function Get-ServerRecord {
    param([string]$Id)
    $server = Get-BridgeEntities 'server'
    [pscustomobject]@{
        Record = $server.Entities[$Id]
        Children = @($server.Entities.Values | Where-Object { $_.ParentId -eq $Id })
    }
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch
    $script:alicePlayerId = (Invoke-BridgeJson $A 'state').playerId
    $script:bobPlayerId = (Invoke-BridgeJson $B 'state').playerId

    Step 'Both players in shallow water next to each other' {
        Invoke-Bridge $A 'teleport -60 -6 380' | Out-Null
        Invoke-Bridge $B 'teleport -58 -6 382' | Out-Null
        Start-Sleep -Seconds 3
        Assert-True ((Invoke-BridgeJson $A 'state').isUnderwater) "$A is in the water (items can be dropped)"
    }

    Step 'Alice spawns a flashlight with a battery' {
        Invoke-Bridge $A 'console item flashlight' | Out-Null
        $script:flashlight = Wait-Until { Get-InventoryItem $A -TechType 'Flashlight' | Where-Object { $_.battery } | Select-Object -First 1 } -TimeoutSeconds 10
        Assert-True ([bool]$flashlight) "$A has a flashlight with a battery"
        $script:flashlightId = $flashlight.id
        $script:batteryId = $flashlight.battery.id
        Write-Host "  flashlight $flashlightId, battery $batteryId"
        $server = Wait-Until { $r = Get-ServerRecord $flashlightId; if ($r.Record -and $r.Children.Count -eq 1) { $r } } -TimeoutSeconds 10
        Assert-True ([bool]$server) 'the server has the flashlight with its battery'
    }

    Step 'Alice takes the battery out' {
        Invoke-Bridge $A "call $flashlightId EnergyMixin.Select null" | Out-Null
        $inInventory = Wait-Until { Get-InventoryItem $A -Id $batteryId } -TimeoutSeconds 10
        Assert-True ([bool]$inInventory) "the battery is in $A's inventory"
        Assert-Equal 'null' (Invoke-Bridge $A "get $flashlightId EnergyMixin.batterySlot.storedItem") "$A's flashlight has no battery"
        $server = Wait-Until { $r = Get-ServerRecord $batteryId; if ($r.Record.ParentId -eq $alicePlayerId) { $r } } -TimeoutSeconds 10
        Assert-True ([bool]$server) "the server has the battery in $A's inventory"
        Assert-Equal 0 (Get-ServerRecord $flashlightId).Children.Count 'the server has nothing under the flashlight'
        $script:batteriesBefore = Get-ServerBatteryCount
        Write-Host "  server battery count: $batteriesBefore"
    }

    Step 'Alice drops the empty flashlight' {
        Invoke-Bridge $A "call Inventory.main.InternalDropItem $flashlightId true" | Out-Null
        $dropped = Wait-Until { $r = Get-ServerRecord $flashlightId; if ($r.Record.ParentId -eq '-') { $r } } -TimeoutSeconds 10
        Assert-True ([bool]$dropped) 'the server has the flashlight in the world'
        $seen = Wait-Until { Invoke-BridgeJson $B "entity $flashlightId" } -TimeoutSeconds 10
        Assert-True ([bool]$seen) "$B sees the dropped flashlight"
        Assert-Equal 'null' (Invoke-Bridge $B "get $flashlightId EnergyMixin.batterySlot.storedItem") "$B's copy of the dropped flashlight has no battery"
    }

    Step 'Bob picks the flashlight up' {
        Invoke-Bridge $B "use $flashlightId Pickupable" | Out-Null
        $picked = Wait-Until { Get-InventoryItem $B -Id $flashlightId } -TimeoutSeconds 10
        Assert-True ([bool]$picked) "the flashlight is in $B's inventory"
        $server = Wait-Until { $r = Get-ServerRecord $flashlightId; if ($r.Record.ParentId -eq $bobPlayerId) { $r } } -TimeoutSeconds 10
        Assert-True ([bool]$server) "the server has the flashlight in $B's inventory"
        # A duplicated battery is spawned after the pickup, so check once the server has caught up, not right away.
        Start-Sleep -Seconds 2
        $flashlightNow = Get-InventoryItem $B -Id $flashlightId
        Assert-True (-not $flashlightNow.battery) "$B's flashlight has no battery (inventory: $($flashlightNow.battery | ConvertTo-Json -Compress))"
        Assert-Equal 'null' (Invoke-Bridge $B "get $flashlightId EnergyMixin.batterySlot.storedItem") "$B's flashlight battery slot is empty"
        Assert-Equal 0 (Get-ServerRecord $flashlightId).Children.Count 'the server has no battery under the flashlight'
        Assert-Equal $batteriesBefore (Get-ServerBatteryCount) 'the server has as many batteries as before the pickup'
        Write-Host (Invoke-Bridge 'server' "query $flashlightId")
    }

    Assert-NoDiff -Ids $flashlightId, $batteryId 'no desync reported for the flashlight or the battery'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S8 EmptyToolNoDupe' -Keep:$Keep
