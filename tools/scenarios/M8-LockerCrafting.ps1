<#
.SYNOPSIS
    MULTINAUGHTICA_SPEC M8: a fabricator in a base or a Cyclops also uses the items in that base's or Cyclops' lockers,
    and the partner's locker loses them.

.DESCRIPTION
    Starts the fixture world tools\fixtures\s7-machine-base (one room, see S7Helpers.ps1) unless -NoLaunch.
      Base: Alice builds a Fabricator and a Locker in the room (nocost + fastbuild, then the cheats go off again). Bob puts
      one titanium into the locker and keeps the locker open. Alice has every other knife ingredient and no titanium.
        1. With the fabricator's menu open in Alice's game, the knife lights up
           (CrafterLogic.IsCraftRecipeFulfilled is true: CrafterLogic_IsCraftRecipeFulfilled_Patch + LockerCrafting).
        2. Alice crafts a knife (GhostCrafter.Craft, what the menu's click calls). The claim takes the titanium: it is
           gone from the locker on the server and in both games (Bob's open locker included), Alice gets the knife, her
           other ingredients are spent, and diff is clean for the locker and the knife.
      Cyclops: Bob spawns a Cyclops in open water, both stand in it, and the same craft uses titanium from one of the
      Cyclops' own lockers with its fabricator.
    -OptionOff (negative control for the switch): the world starts with CraftFromLockers=False in server.cfg. The knife
    stays dark, the craft makes nothing and the titanium stays in the locker everywhere.

    Without the fix (no LockerCrafting patches) the scenario fails by construction: the menu counts only the inventory
    (step 1 false), and the vanilla craft refuses ("DontHaveNeededIngredients"), so no knife and the locker keeps the
    titanium.

    Windowless is enough ($env:NITROX_WINDOWLESS = 1): menu state and inventories are read through the bridge, nothing
    has to be seen. Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [switch]$OptionOff, [switch]$SkipCyclops, [string]$SaveName = 'm8-locker-crafting',
    # Over the Grassy Plateaus, open water (S13, T4)
    [double[]]$CyclopsArea = @(263, -60, 64))

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'S7Helpers.ps1')
. (Join-Path $PSScriptRoot 'BaseEquipmentHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$expectCraft = -not $OptionOff

function Format-Position {
    param([double[]]$P)
    ($P | ForEach-Object { Format-BridgeNumber $_ }) -join ' '
}

function Get-Count {
    param([string]$Player, [string]$TechType)
    @(Get-InventoryItem $Player -TechType $TechType).Count
}

function Get-ServerIds {
    param([string]$TechType)
    @((Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.TechType -eq $TechType } | ForEach-Object { $_.Id })
}

<#
    Places a module in the S7 room the way the Habitat Builder does, trying a few view directions from the room's middle
    (the machines take some walls), then constructs it. Returns its id. The player is put back into the base after the
    teleport: modules that must be inside are only placeable while the player is in the base.
#>
function New-RoomModule {
    param([string]$Player, [string]$TechType, [double[]]$Pitches)
    foreach ($yaw in 0, 90, 180, 270, 45, 135, 225, 315) {
        foreach ($pitch in $Pitches) {
            Enter-S7Base $Player
            Invoke-Bridge $Player ('look {0} {1}' -f (Format-BridgeNumber $yaw), (Format-BridgeNumber $pitch)) | Out-Null
            Start-Sleep -Milliseconds 500
            $before = @((Invoke-BridgeJson $Player 'find Constructable 15') | ForEach-Object { $_.id })
            Invoke-Bridge $Player 'call Builder.End' | Out-Null
            Invoke-Bridge $Player "call Builder.BeginAsync $TechType" | Out-Null
            $canPlace = Wait-Until { (Invoke-Bridge $Player 'get Builder.canPlace') -eq 'true' } -TimeoutSeconds 4 -IntervalSeconds 0.5
            if (-not $canPlace) { continue }
            Assert-Equal 'true' (Invoke-Bridge $Player 'call Builder.TryPlace') "$Player placed $TechType (look $yaw/$pitch)"
            Invoke-Bridge $Player 'call Builder.End' | Out-Null
            $ghost = Wait-Until {
                @(Invoke-BridgeJson $Player 'find Constructable 15') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1
            } -TimeoutSeconds 10 -IntervalSeconds 0.5
            if (-not $ghost) { throw "No new Constructable with an id after placing $TechType" }
            for ($i = 0; $i -lt 300; $i++) {
                if ((Invoke-Bridge $Player "get $($ghost.id) Constructable.constructed" -AllowError) -ne 'false') { break }
                Invoke-Bridge $Player "call $($ghost.id) Constructable.Construct" -AllowError | Out-Null
            }
            Write-Host "  built $TechType ($($ghost.id)) looking $yaw/$pitch"
            return $ghost.id
        }
    }
    Invoke-Bridge $Player 'call Builder.End' | Out-Null
    throw "$Player can't place $TechType anywhere in the room"
}

<#
    The knife's ingredients from the game's own data (TechData.GetIngredients), as tech type -> amount.
#>
function Get-KnifeRecipe {
    $recipe = [ordered]@{}
    foreach ($line in @(Invoke-BridgeJson $A 'call TechData.GetIngredients Knife')) {
        $type = if ($line.techType) { $line.techType } else { $line._techType }
        $amount = if ($line.amount) { $line.amount } else { $line._amount }
        if ($type) { $recipe["$type"] = [int]$amount }
    }
    $recipe
}

<#
    One craft from a locker: Bob stores one titanium in $LockerId and keeps the locker open, Alice (with every other
    ingredient, no titanium) opens the fabricator's menu, checks the knife lights up, closes it and crafts. $StandAt: where
    Alice stands, within the fabricator's 3 m (GhostCrafter.closeDistance), so the open menu isn't closed again.
#>
function Test-LockerCraft {
    param([string]$Where, [string]$FabricatorId, [string]$LockerId, [double[]]$StandAt, [scriptblock]$EnterAlice)

    Step "$Where`: Bob stores one titanium in the locker and keeps it open; Alice has the rest of the recipe" {
        Invoke-Bridge $B 'console item titanium 1' | Out-Null
        $script:titaniumId = (Wait-Until { (Get-InventoryItem $B -TechType 'Titanium' | Select-Object -Last 1).id } -TimeoutSeconds 10)
        if (-not $titaniumId) { throw "$B didn't get a titanium" }
        Assert-True ((Invoke-Bridge $B "call $LockerId StorageContainer.container.AddItem $titaniumId") -ne 'null') "$B put titanium $titaniumId into the locker"
        $bobId = (Invoke-BridgeJson $B 'state').playerId
        $stored = Wait-Until { (Get-BridgeEntities 'server').Entities[$titaniumId] | Where-Object { $_.ParentId -ne '-' -and $_.ParentId -ne $bobId } } -TimeoutSeconds 15
        Assert-True ([bool]$stored) 'the server has the titanium in the locker'
        $script:lockerOwner = $stored.ParentId
        foreach ($player in $A, $B) {
            $seen = Wait-Until { (Get-BridgeEntities $player).Entities[$titaniumId] | Where-Object { $_.ParentId -eq $lockerOwner } } -TimeoutSeconds 15
            Assert-True ([bool]$seen) "$player's game has the titanium in the locker ($lockerOwner)"
        }
        Invoke-Bridge $B "use $LockerId StorageContainer" | Out-Null

        # Alice: no titanium of her own, everything else the knife needs
        for ($i = 0; $i -lt 30 -and (Get-Count $A 'Titanium') -gt 0; $i++) { Invoke-Bridge $A 'call Inventory.main.DestroyItem Titanium false' | Out-Null }
        foreach ($type in $recipe.Keys) {
            if ($type -ne 'Titanium') { Invoke-Bridge $A "console item $type $($recipe[$type])" | Out-Null }
        }
        Start-Sleep -Seconds 2
        Assert-Equal 0 (Get-Count $A 'Titanium') "$A has no titanium"
        $script:before = @{ Knife = (Get-Count $A 'Knife'); ServerKnives = (Get-ServerIds 'Knife') }
        foreach ($type in $recipe.Keys) { $script:before[$type] = Get-Count $A $type }
    }

    Step "$Where`: with the fabricator's menu open the knife $(if ($expectCraft) { 'lights up' } else { 'stays dark (option off)' })" {
        & $EnterAlice
        Invoke-Bridge $A ('teleport ' + (Format-Position $StandAt)) | Out-Null
        Start-Sleep -Seconds 1
        Invoke-Bridge $A "use $FabricatorId GhostCrafter" | Out-Null
        $open = Wait-Until { (Invoke-Bridge $A 'get uGUI.main.craftingMenu.client' -AllowError) -notmatch '^(null|ERR)' } -TimeoutSeconds 5
        Assert-True ([bool]$open) "$A's crafting menu is open on the fabricator"
        Assert-Equal ($(if ($expectCraft) { 'true' } else { 'false' })) (Invoke-Bridge $A 'call CrafterLogic.IsCraftRecipeFulfilled Knife') 'the knife counts the locker titanium'
        Invoke-Bridge $A 'call uGUI.main.craftingMenu.Deselect' | Out-Null
    }

    Step "$Where`: Alice crafts the knife" {
        Invoke-Bridge $A "call $FabricatorId GhostCrafter.Craft Knife 1" | Out-Null
        if ($expectCraft) {
            $done = Wait-Until { (Invoke-Bridge $A "get $FabricatorId GhostCrafter.logic.craftingTechType") -match 'Knife' -and (Invoke-Bridge $A "get $FabricatorId GhostCrafter.logic.inProgress") -eq 'false' } -TimeoutSeconds 30
            $taken = $done -or (Get-Count $A 'Knife') -gt $before.Knife
            Assert-True ([bool]$taken) "$A's fabricator made the knife"
            if ((Get-Count $A 'Knife') -eq $before.Knife) { Invoke-Bridge $A "use $FabricatorId GhostCrafter" | Out-Null }
            Assert-True ([bool](Wait-Until { (Get-Count $A 'Knife') -eq $before.Knife + 1 } -TimeoutSeconds 10)) "$A got one knife"
        }
        else {
            Start-Sleep -Seconds 8
            Assert-Equal $before.Knife (Get-Count $A 'Knife') "$A got no knife"
            Assert-True ((Invoke-Bridge $A "get $FabricatorId GhostCrafter.logic.craftingTechType") -match 'None') "$A's fabricator didn't start"
        }
    }

    Step "$Where`: the titanium is $(if ($expectCraft) { 'gone from the locker everywhere' } else { 'still in the locker everywhere' })" {
        foreach ($name in 'server', $A, $B) {
            if ($expectCraft) {
                $gone = Wait-Until { -not (Get-BridgeEntities $name).Entities.ContainsKey($titaniumId) } -TimeoutSeconds 15
                Assert-True ([bool]$gone) "$name no longer has the titanium $titaniumId"
            }
            else {
                $entity = (Get-BridgeEntities $name).Entities[$titaniumId]
                Assert-Equal $lockerOwner $entity.ParentId "$name still has the titanium in the locker"
            }
        }
        foreach ($player in $A, $B) {
            Assert-Equal ($(if ($expectCraft) { '0' } else { '1' })) (Invoke-Bridge $player "call $LockerId StorageContainer.container.GetCount Titanium") "$player's locker container counts it"
        }
        Assert-Equal 0 (Get-Count $A 'Titanium') "$A has no titanium (the knife didn't take hers)"
        foreach ($type in $recipe.Keys) {
            if ($type -eq 'Titanium') { continue }
            $spent = if ($expectCraft) { $recipe[$type] } else { 0 }
            Assert-Equal ($before[$type] - $spent) (Get-Count $A $type) "$A's $type was $(if ($expectCraft) { 'spent' } else { 'kept' })"
        }
        $newKnives = @(Get-ServerIds 'Knife' | Where-Object { $before.ServerKnives -notcontains $_ })
        Assert-Equal ($(if ($expectCraft) { 1 } else { 0 })) $newKnives.Count "the server has $(if ($expectCraft) { 'one new knife' } else { 'no new knife' })"
        # Bob closes the locker again (StorageContainer.Open opened his PDA)
        Invoke-Bridge $B 'call Player.main.pda.Close' -AllowError | Out-Null
        $script:checkIds = @($lockerOwner) + $newKnives
    }
    Assert-NoDiff -Ids $checkIds "$Where`: no desync reported for the locker and the knife"
}

try {
    $fixture = $S7.Fixture
    if ($OptionOff -and -not $NoLaunch) {
        $fixture = Join-Path ([IO.Path]::GetTempPath()) 'nitrox-m8-option-off'
        if (Test-Path $fixture) { Remove-Item -Recurse -Force $fixture }
        Copy-Item $S7.Fixture $fixture -Recurse
        Add-Content -Path (Join-Path $fixture 'server.cfg') -Value 'CraftFromLockers=False'
    }
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -Fixture $fixture -NoLaunch:$NoLaunch
    Initialize-S7Players $A, $B
    $script:recipe = Get-KnifeRecipe
    if ($recipe.Count -eq 0) {
        Write-Host '  TechData.GetIngredients gave nothing readable; using the known recipe (1 Silicone, 1 Titanium)' -ForegroundColor Yellow
        $script:recipe = [ordered]@{ Silicone = 1; Titanium = 1 }
    }
    Write-Host "  knife recipe: $(($recipe.Keys | ForEach-Object { "$_ x$($recipe[$_])" }) -join ', ')"
    Assert-Equal 1 $recipe['Titanium'] 'the knife needs one titanium'

    Step 'Alice builds a Fabricator and a Locker in the room; real costs again afterwards' {
        Enable-BuildCheats $A
        $script:baseFabricator = New-RoomModule $A 'Fabricator' @(0, -10, 10)
        $script:baseLocker = New-RoomModule $A 'Locker' @(-50, -35, -65)
        Invoke-Bridge $A 'call GameModeUtils.DeactivateCheat NoCost' | Out-Null
        if ((Invoke-Bridge $A 'get NoCostConsoleCommand.main.fastBuildCheat') -eq 'true') { Invoke-Bridge $A 'console fastbuild' | Out-Null }
        foreach ($player in $A, $B) {
            Assert-Equal 'true' (Invoke-Bridge $player 'call GameModeUtils.RequiresIngredients') "$player's game charges ingredients"
            foreach ($id in $baseFabricator, $baseLocker) {
                Assert-True ([bool](Wait-Until { -not (Invoke-Bridge $player "entity $id" -AllowError).StartsWith('ERR ') } -TimeoutSeconds 20)) "$player has $id"
            }
        }
    }

    $fabricatorPosition = [double[]]((Invoke-BridgeJson $A "entity $baseFabricator").position)
    $room = [double[]]($S7.Inside -split ' ')
    # 1.5 m from the fabricator toward the middle of the room
    $toRoom = @(($room[0] - $fabricatorPosition[0]), 0, ($room[2] - $fabricatorPosition[2]))
    $length = [math]::Max(0.01, [math]::Sqrt($toRoom[0] * $toRoom[0] + $toRoom[2] * $toRoom[2]))
    $baseStand = @(($fabricatorPosition[0] + 1.5 * $toRoom[0] / $length), $room[1], ($fabricatorPosition[2] + 1.5 * $toRoom[2] / $length))
    Test-LockerCraft -Where 'Base' -FabricatorId $baseFabricator -LockerId $baseLocker -StandAt $baseStand -EnterAlice { Enter-S7Base $A }

    if (-not $SkipCyclops) {
        Step 'Bob spawns a Cyclops in open water; both games see it' {
            Invoke-Bridge $B ('teleport ' + (Format-Position $CyclopsArea)) | Out-Null
            Invoke-Bridge $A ('teleport ' + (Format-Position @(($CyclopsArea[0] - 5), $CyclopsArea[1], ($CyclopsArea[2] - 4)))) | Out-Null
            Invoke-Bridge $B 'look 0 0' | Out-Null
            Start-Sleep -Seconds 3
            $before = @((Invoke-BridgeJson $B 'find SubRoot 20') | ForEach-Object { $_.id })
            Invoke-Bridge $B 'console sub cyclops' | Out-Null
            $cyclops = Wait-Until { (Invoke-BridgeJson $B 'find SubRoot 20') | Where-Object { $_.name -like 'Cyclops*' -and $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 30
            if (-not $cyclops) { throw 'the Cyclops did not spawn' }
            $script:cyclopsId = $cyclops.id
            Assert-True ([bool](Wait-Until { (Invoke-BridgeJson $A 'find SubRoot 20') | Where-Object { $_.id -eq $cyclopsId } } -TimeoutSeconds 30)) "$A sees the Cyclops $cyclopsId"
            Start-Sleep -Seconds 3
            $script:cyclopsFabricator = (Wait-Until { @(Invoke-BridgeJson $B 'find Fabricator 40') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $_.id -ne $baseFabricator } | Select-Object -First 1 } -TimeoutSeconds 20)
            $script:cyclopsLocker = (Wait-Until { @(Invoke-BridgeJson $B 'find CyclopsLocker 40') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' } | Select-Object -First 1 } -TimeoutSeconds 20)
            if (-not $cyclopsFabricator -or -not $cyclopsLocker) { throw 'no Cyclops fabricator or locker with an id' }
            Write-Host "  Cyclops fabricator $($cyclopsFabricator.id), locker $($cyclopsLocker.id)"
            foreach ($id in $cyclopsFabricator.id, $cyclopsLocker.id) {
                Assert-True ((Invoke-Bridge $A "entity $id" -AllowError) -notmatch '^ERR ') "$A's game has $id"
            }
        }

        $p = [double[]]$cyclopsFabricator.position
        $cyclopsPosition = [double[]](@(Invoke-BridgeJson $A 'find SubRoot 40') | Where-Object { $_.id -eq $cyclopsId } | Select-Object -First 1).position
        # 1.2 m from the fabricator toward the Cyclops' middle line (it hangs on a wall)
        $toMiddle = @(($cyclopsPosition[0] - $p[0]), 0, ($cyclopsPosition[2] - $p[2]))
        $length = [math]::Max(0.01, [math]::Sqrt($toMiddle[0] * $toMiddle[0] + $toMiddle[2] * $toMiddle[2]))
        $cyclopsStand = @(($p[0] + 1.2 * $toMiddle[0] / $length), $p[1], ($p[2] + 1.2 * $toMiddle[2] / $length))
        # Teleporting inside a Cyclops clears currentSub (TESTING.md); the craft doesn't need it, only the 3 m range
        Test-LockerCraft -Where 'Cyclops' -FabricatorId $cyclopsFabricator.id -LockerId $cyclopsLocker.id -StandAt $cyclopsStand -EnterAlice { }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name "M8 LockerCrafting$(if ($OptionOff) { ' (option off)' })" -Keep:$Keep
