<#
.SYNOPSIS
    COOP_FEATURES_SPEC P2 (#2538): an egg in a containment tank hatches into exactly one creature for both players, even
    when it ripens first on the game of the player who doesn't simulate the tank. B sees the fish A puts in an aquarium.

.DESCRIPTION
    Starts the fixture world tools\fixtures\p2-containment-base unless -NoLaunch: one room with an alien containment
    (tank) and an aquarium, built through the dev bridge (Builder.BeginAsync / TryPlace / Constructable.Construct,
    nocost + fastbuild). Its EntityData only keeps the player-made entities.
      1. The server says who holds the tank's simulation lock (the holder; the first player to join). Only the holder's
         game hatches eggs and spawns creatures (CreatureEgg_Hatch_Patch, WaterParkCreature_BornAsync_Patch.CanBeBorn).
      2. The other player drops a cuddlefish egg into the tank (Inventory.InternalDropItem while inside it, what a player
         does). Server and both games have it in the tank.
      3. The holder goes 200 m away. The egg ripens on the other player's game first: the fast-hatch cheat is switched on
         in that game only (NoCostConsoleCommand.fastHatchCheat, read by CreatureEgg.GetHatchDuration every second), the
         way one game's once-a-second check can come first in play. The egg must still be in the tank on the server and
         in both games, and no creature exists yet.
      4. The holder's game ripens too: exactly one cuddlefish, in the tank, on the server and in both games, the egg gone
         everywhere.
      5. Aquarium (verify only): Alice puts a peeper in the aquarium (Inventory.AddOrSwap, what the storage UI does); Bob's
         aquarium holds it and shows its swimming model.
    Without the fix the other player's game destroys the ripe egg (CreatureEgg.Hatch) and its LiveMixin.Kill broadcasts
    EntityDestroyed (LiveMixin_Kill_Patch: eggs have destroyOnDeath), so the egg vanishes from the server and from the
    holder's game before it hatches there: no creature anywhere (step 3 fails).
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'p2-containment-eggs')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$P2 = @{
    Fixture  = Join-Path $script:RepoRoot 'tools\fixtures\p2-containment-base'
    Base     = '014fcae4-f4ea-494b-a421-57ca6b24dbb4'
    Tank     = 'e47b414e-ded9-4f4c-a891-08e4f4be6c30'
    Aquarium = '7949e7c4-60a8-482b-9655-26a480df1f2f'
    # In the room, next to the aquarium; and inside the tank (WaterPark.IsPointInside)
    InRoom   = '60 -27 204.5'
    InTank   = '60 -27.2 208.56'
    # Next to the escape pod's start, about 200 m from the base
    Away     = '-78 1.9 373'
}

function Enter-Room {
    param([string]$Player, [string]$Position = $P2.InRoom)
    Invoke-Bridge $Player "teleport $Position" | Out-Null
    Invoke-Bridge $Player "call Player.main.SetCurrentSub $($P2.Base) true" | Out-Null
}

function Get-TankLockHolder {
    if ((Invoke-Bridge 'server' "query $($P2.Tank)") -match 'Owner: (\w+) #') { $Matches[1] }
}

# Tech type/id of everything in the tank: the server's children of the tank, or a game's WaterPark.items
function Get-TankContent {
    param([string]$Name)
    if ($Name -eq 'server') {
        return @((Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.ParentId -eq $P2.Tank -and $_.TechType -and $_.TechType -ne 'None' } |
            ForEach-Object { [pscustomobject]@{ TechType = $_.TechType; Id = $_.Id } })
    }
    @(Invoke-BridgeJson $Name "get $($P2.Tank) WaterPark.items" | ForEach-Object { [pscustomobject]@{ TechType = $_.techType; Id = $_.id } })
}

function Get-EggProgress {
    param([string]$Player, [string]$EggId)
    $value = Invoke-Bridge $Player "get $EggId CreatureEgg.progress" -AllowError
    if ($value -match '^[0-9.Ee-]+$') { [double]::Parse($value, [Globalization.CultureInfo]::InvariantCulture) } else { $null }
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -Fixture $P2.Fixture -NoLaunch:$NoLaunch
    foreach ($player in $A, $B) {
        Invoke-Bridge $player 'set NoCostConsoleCommand.main.fastHatchCheat false' | Out-Null
    }

    $holder = $null
    $other = $null
    $egg = $null
    $creature = $null

    Step 'The tank has a lock holder' {
        $script:holder = Wait-Until { Get-TankLockHolder } -TimeoutSeconds 30
        Assert-True ($A, $B -contains $script:holder) "the server gives the tank's lock to a player ($script:holder)"
        $script:other = if ($script:holder -eq $A) { $B } else { $A }
        Write-Host "  holder: $script:holder, other: $script:other"
        foreach ($player in $A, $B) {
            # An empty tank's item list is "[]"; an unloaded one is an error
            $loaded = Wait-Until { (Invoke-Bridge $player "get $($P2.Tank) WaterPark.items" -AllowError) -like '`[*' } -TimeoutSeconds 30
            Assert-True ([bool]$loaded) "$player has the tank loaded"
        }
    }
    $holder = $script:holder
    $other = $script:other

    Step "$other drops a cuddlefish egg into the tank" {
        $before = @(Get-InventoryItem $other | ForEach-Object { $_.id })
        Invoke-Bridge $other 'console item CutefishEgg' | Out-Null
        $item = Wait-Until { Get-InventoryItem $other | Where-Object { $_.techType -like 'CutefishEgg*' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
        Assert-True ([bool]$item) "$other has a cuddlefish egg"
        $script:egg = $item.id
        Enter-Room $other $P2.InTank
        Start-Sleep -Seconds 1
        # Pickupable.Drop adds the item to Player.currentWaterPark, which the game sets while the player swims in the tank
        Invoke-Bridge $other "set Player.main.currentWaterPark $($P2.Tank)" | Out-Null
        Invoke-Bridge $other "call Inventory.main.InternalDropItem $script:egg true" | Out-Null
        foreach ($name in 'server', $A, $B) {
            $inTank = Wait-Until { (Get-TankContent $name) | Where-Object { $_.Id -eq $script:egg } } -TimeoutSeconds 20
            Assert-True ([bool]$inTank) "the egg is in the tank on $name"
        }
        Enter-Room $other
    }
    $egg = $script:egg

    Step "$holder goes away; the egg ripens on $other's game first" {
        Invoke-Bridge $holder "teleport $($P2.Away)" | Out-Null
        Start-Sleep -Seconds 3
        $holderProgress = Get-EggProgress $holder $egg
        Assert-True ($null -ne $holderProgress -and $holderProgress -lt 1) "$holder's game still has the egg, not ripe ($holderProgress)"
        Invoke-Bridge $other 'set NoCostConsoleCommand.main.fastHatchCheat true' | Out-Null
        # Cuddlefish eggs need 1200 s x daysBeforeHatching, 1/100 of that with the cheat
        $ripe = Wait-Until { $p = Get-EggProgress $other $egg; if ($null -eq $p -or $p -ge 1) { $true } } -TimeoutSeconds 60
        Assert-True ([bool]$ripe) "the egg ripened on $other's game"
        # A few of CreatureEgg.UpdateProgress's once-a-second checks
        Start-Sleep -Seconds 5
        Assert-Equal 1 ([double](Get-EggProgress $other $egg)) "$other's game keeps the ripe egg"
        $holderProgress = Get-EggProgress $holder $egg
        Assert-True ($null -ne $holderProgress -and $holderProgress -lt 1) "$holder's game still has the egg, still growing ($holderProgress)"
        foreach ($name in 'server', $A, $B) {
            $content = Get-TankContent $name
            Assert-True ([bool]($content | Where-Object { $_.Id -eq $egg })) "the egg is still in the tank on $name"
            Assert-Equal 0 @($content | Where-Object { $_.TechType -eq 'Cutefish' }).Count "no cuddlefish yet on $name"
        }
    }

    Step "$holder's game ripens it: exactly one cuddlefish everywhere" {
        Invoke-Bridge $holder 'set NoCostConsoleCommand.main.fastHatchCheat true' | Out-Null
        $hatched = Wait-Until {
            $content = Get-TankContent 'server'
            if (-not ($content | Where-Object { $_.Id -eq $egg }) -and ($content | Where-Object { $_.TechType -eq 'Cutefish' })) { $true }
        } -TimeoutSeconds 60
        Assert-True ([bool]$hatched) 'the server has the egg replaced by a cuddlefish'
        # Let late duplicates show up, if any
        Start-Sleep -Seconds 5
        $ids = @{}
        foreach ($name in 'server', $A, $B) {
            $content = Get-TankContent $name
            $fish = @($content | Where-Object { $_.TechType -eq 'Cutefish' })
            Assert-Equal 1 $fish.Count "exactly one cuddlefish in the tank on $name"
            Assert-Equal 0 @($content | Where-Object { $_.Id -eq $egg }).Count "the egg is gone on $name"
            if ($fish.Count -eq 1) { $ids[$name] = $fish[0].Id }
        }
        Assert-Equal 1 @($ids.Values | Sort-Object -Unique).Count "it's the same cuddlefish everywhere ($(($ids.Values | Sort-Object -Unique) -join ', '))"
        $script:creature = @($ids.Values)[0]
        foreach ($player in $A, $B) {
            Invoke-Bridge $player 'set NoCostConsoleCommand.main.fastHatchCheat false' | Out-Null
        }
    }
    $creature = $script:creature
    Assert-NoDiff -Ids (@($P2.Tank, $egg, $creature) | Where-Object { $_ }) 'no desync reported for the tank, the egg or the cuddlefish'

    Step "Aquarium: $A puts a peeper in, $B sees it" {
        Enter-Room $A
        $before = @(Get-InventoryItem $A | ForEach-Object { $_.id })
        Invoke-Bridge $A 'console item Peeper' | Out-Null
        $peeper = Wait-Until { Get-InventoryItem $A | Where-Object { $_.techType -eq 'Peeper' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
        Assert-True ([bool]$peeper) "$A has a peeper"
        Assert-Equal 'true' (Invoke-Bridge $A "call Inventory.AddOrSwap $($peeper.id) $($P2.Aquarium)") "$A moved the peeper into the aquarium"
        $serverHas = Wait-Until { (Get-BridgeEntities 'server').Entities[$peeper.id].ParentId -eq $P2.Aquarium } -TimeoutSeconds 15
        Assert-True ([bool]$serverHas) 'the server has the peeper in the aquarium'
        $bobHas = Wait-Until { (Invoke-Bridge $B "get $($P2.Aquarium) Aquarium.storageContainer.container.count") -eq '1' } -TimeoutSeconds 15
        Assert-True ([bool]$bobHas) "$B's aquarium holds one fish"
        Assert-Equal '"Peeper"' (Invoke-Bridge $B "get $($P2.Aquarium) Aquarium.tracks[0].fishType") "$B's aquarium shows a swimming peeper"
        Assert-Equal 'true' (Invoke-Bridge $B "get $($P2.Aquarium) Aquarium.tracks[0].fish.activeSelf") "$B's peeper model is active"
        $script:peeper = $peeper.id
    }
    Assert-NoDiff -Ids (@($P2.Aquarium, $script:peeper) | Where-Object { $_ }) 'no desync reported for the aquarium or its fish'
}
catch {
    Add-Failure $_.Exception.Message
}
Complete-Scenario -Name 'P2 ContainmentEggs' -Keep:$Keep
