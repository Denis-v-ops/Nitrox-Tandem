<#
.SYNOPSIS
    COOP_FEATURES_SPEC T3: hand items over. Alice presses the interact button on Bob's body, which opens the transfer
    screen with Bob's inventory as the other side, and moves items over; the server moves them, both games agree, and a
    restart keeps them with Bob. Nothing equipped can be given and nothing can be taken from Bob's side.

.DESCRIPTION
    1. Both players in the shallows a metre apart. Alice gets titanium and a flashlight (with its battery) and wears a
       tank (console "item ...").
    2. Alice holds the interact button on Bob's body (bridge hold): the PDA opens with her copy of Bob's inventory as
       the used storage.
    3. Alice clicks the titanium (Inventory.ExecuteItemAction, button 0: the game's own "move to the other side") and
       drags the flashlight over (Inventory.AddOrSwap onto Bob's container, what the drop calls). Both end up in Bob's
       inventory on Bob's game, in Bob's copy on Alice's game and under Bob on the server; the flashlight keeps its id
       and its battery; no desync for them.
    4. Alice drags her equipped tank onto Bob's side: refused, the tank stays on Alice everywhere. Alice drags the
       titanium back from Bob's side into her inventory: refused (nothing can be taken), it stays with Bob.
    5. Restart (save, stop, start, both rejoin): Bob still has both, the battery is still in the flashlight, no desync.

    Negative control (2026-11-02): without GiveItemAction and ItemGiveRequestProcessor (the unfixed coop), the hold
    opens nothing and the items stay with Alice: step 2 and 3 fail.

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$SaveName = 'bridge-scenario'

function Get-State { param([string]$Player) Invoke-BridgeJson $Player 'state' }

function Get-ServerParent {
    param([string]$Id)
    $record = (Get-BridgeEntities 'server').Entities[$Id]
    if ($record) { $record.ParentId }
}

function Get-ServerChildren {
    param([string]$Id)
    @((Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.ParentId -eq $Id } | ForEach-Object { $_.Id })
}

function Add-Item {
    param([string]$Player, [string]$Command, [string]$TechType)
    Invoke-Bridge $Player "console item $Command" | Out-Null
    $item = Wait-Until { Get-InventoryItem $Player -TechType $TechType | Select-Object -First 1 } -TimeoutSeconds 10
    Assert-True ([bool]$item) "$Player has a $TechType"
    $onServer = Wait-Until { Get-ServerParent $item.id } -TimeoutSeconds 10
    Assert-True ([bool]$onServer) "the server has $Player's $TechType ($($item.id))"
    $item
}

# Both games and the server agree that $Id is with Bob, and nothing of it is left with Alice.
function Assert-WithBob {
    param([string]$Id, [string]$TechType)
    $withBob = Wait-Until { Get-InventoryItem $B -Id $Id } -TimeoutSeconds 10
    Assert-True ([bool]$withBob) "Bob's game has the $TechType ($Id) in his inventory"
    Assert-True (-not (Get-InventoryItem $A -Id $Id)) "Alice's game no longer has the $TechType in her inventory"
    Assert-Equal $bobId (Get-ServerParent $Id) "the server has the $TechType under Bob"
    $inCopy = Invoke-Bridge $A "call Inventory.main.IsUsingStorage $bobId"
    $copyHasIt = Invoke-Bridge $A "get $Id Pickupable.inventoryItem.container.tr.parent.name"
    Assert-Equal $B $copyHasIt "Alice's game has the $TechType in its copy of Bob's inventory (screen open: $inCopy)"
}

function Restart-World {
    Write-Host (Invoke-Bridge 'server' 'save')
    Start-Sleep -Seconds 3
    & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop
    if ($LASTEXITCODE -ne 0) { throw 'The games, server or launcher are still running after dual-client.ps1 -Stop' }
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -KeepSave
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    $script:aliceId = (Get-State $A).playerId
    $script:bobId = (Get-State $B).playerId

    Step 'Both players in the shallows a metre apart; Alice has titanium, a flashlight and wears a tank' {
        Invoke-Bridge $A 'teleport -60 -10 380' | Out-Null
        Invoke-Bridge $B 'teleport -59 -10 380' | Out-Null
        Start-Sleep -Seconds 4
        $script:titanium = Add-Item $A 'titanium' 'Titanium'
        $script:flashlight = Add-Item $A 'flashlight' 'Flashlight'
        Assert-True ([bool]$flashlight.battery) "the flashlight has a battery ($($flashlight.battery.id))"
        $script:batteryId = $flashlight.battery.id
        Invoke-Bridge $A 'console item tank' | Out-Null   # equips itself into the free tank slot
        $script:tank = Wait-Until { (Get-State $A).equipment.Tank } -TimeoutSeconds 10
        Assert-True ([bool]$tank) "Alice wears a tank ($($tank.id))"
        Assert-Equal $aliceId (Wait-Until { Get-ServerParent $tank.id } -TimeoutSeconds 10) 'the server has the tank under Alice'
        # Bob's game spawned Alice's items into its copy of her inventory
        $copied = Wait-Until { (Invoke-Bridge $B "get $($titanium.id) Pickupable.inventoryItem.container.tr.parent.name") -eq $A } -TimeoutSeconds 10
        Assert-True ([bool]$copied) "Bob's game has Alice's titanium in its copy of her inventory"
    }

    Step 'Alice presses interact on Bob: the transfer screen opens with his inventory' {
        Invoke-Bridge $A "hold $bobId 0.5" | Out-Null
        $status = Wait-Until { $s = Invoke-BridgeJson $A 'hold status'; if (-not $s.active) { $s } } -TimeoutSeconds 10 -IntervalSeconds 0.5
        Write-Host "  hold: $($status | ConvertTo-Json -Compress -Depth 3)"
        Assert-True ($status.targetedFrames -gt 0) "GUIHand targeted Bob's body ($($status.targetedFrames) of $($status.frames) frames)"
        $open = Wait-Until { (Invoke-Bridge $A 'get Player.main.pda.isInUse') -eq 'true' } -TimeoutSeconds 5
        Assert-True ([bool]$open) "Alice's PDA is open"
        Assert-Equal 'true' (Invoke-Bridge $A "call Inventory.main.IsUsingStorage $bobId") "the other side is Alice's copy of Bob's inventory"
    }

    Step 'Alice clicks the titanium and drags the flashlight over: both are Bob''s on every game' {
        Invoke-Bridge $A "call Inventory.main.ExecuteItemAction $($titanium.id) 0" | Out-Null
        Invoke-Bridge $A "call Inventory.AddOrSwap $($flashlight.id) $bobId" | Out-Null
        Assert-WithBob $titanium.id 'titanium'
        Assert-WithBob $flashlight.id 'flashlight'
        $bobsFlashlight = Get-InventoryItem $B -Id $flashlight.id
        Assert-Equal $batteryId $bobsFlashlight.battery.id "the flashlight keeps its battery on Bob's game"
        $children = Get-ServerChildren $flashlight.id
        Assert-True ($children.Count -eq 1 -and $children[0] -eq $batteryId) "the server has the same single battery under the flashlight ($($children -join ', '))"
        Assert-NoDiff -Ids $titanium.id, $flashlight.id, $batteryId 'no desync for the given items'
    }

    Step 'Refused: the equipped tank, and taking the titanium back from Bob''s side' {
        Invoke-Bridge $A "call Inventory.AddOrSwap $($tank.id) $bobId" | Out-Null
        Invoke-Bridge $A "call Inventory.AddOrSwap $($titanium.id) $aliceId" | Out-Null
        Start-Sleep -Seconds 2
        Assert-True ([bool](Get-State $A).equipment.Tank) 'Alice still wears the tank'
        Assert-Equal $aliceId (Get-ServerParent $tank.id) 'the server still has the tank under Alice'
        Assert-True (-not (Get-InventoryItem $B -Id $tank.id)) "Bob's game didn't get the tank"
        Assert-True ([bool](Get-InventoryItem $B -Id $titanium.id)) "Bob still has the titanium"
        Assert-True (-not (Get-InventoryItem $A -Id $titanium.id)) "Alice couldn't take it back"
        Assert-Equal $bobId (Get-ServerParent $titanium.id) 'the server still has the titanium under Bob'
        Assert-NoDiff -Ids $titanium.id, $tank.id 'no desync for the tank and the titanium'
        Invoke-Bridge $A 'call Player.main.pda.Close' | Out-Null
    }

    Step 'Restart: Bob still has both, the flashlight its battery' {
        Restart-World
        Assert-True ([bool](Wait-Until { Get-InventoryItem $B -Id $titanium.id } -TimeoutSeconds 20)) "Bob has the titanium after the restart"
        $bobsFlashlight = Get-InventoryItem $B -Id $flashlight.id
        Assert-True ([bool]$bobsFlashlight) "Bob has the flashlight after the restart"
        Assert-Equal $batteryId $bobsFlashlight.battery.id 'with the same battery'
        Assert-True (-not (Get-InventoryItem $A -Id $titanium.id) -and -not (Get-InventoryItem $A -Id $flashlight.id)) 'Alice has neither'
        Assert-Equal $bobId (Get-ServerParent $titanium.id) 'the server has the titanium under Bob'
        Assert-Equal $bobId (Get-ServerParent $flashlight.id) 'the server has the flashlight under Bob'
        Assert-True ([bool](Get-State $A).equipment.Tank) 'Alice still wears the tank'
        Assert-NoDiff -Ids $titanium.id, $flashlight.id, $batteryId, $tank.id 'no desync after the restart'
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'T3 HandItemsOver' -Keep:$Keep
