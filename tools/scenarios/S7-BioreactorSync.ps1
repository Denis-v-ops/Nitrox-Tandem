<#
.SYNOPSIS
    FIX_SPEC S7 (#2494): a bioreactor's content is the same in both games and on the server; items burned by it are gone
    everywhere and stay gone after a restart; the container never holds more than fits.

.DESCRIPTION
    Starts the fixture world tools\fixtures\s7-machine-base unless -NoLaunch. Both players stand in the room.
      1. Alice puts 2 big eggs into the bioreactor, Bob tries to put 3 more: only what fits goes in (4 big eggs in its
         4x4 grid). Server and both games agree.
      2. The burn is sped up by emptying the reactor's power source and raising BaseBioReactor._toConsume (the energy the
         reactor still has to take from its items) in both games: the producer (base lock holder) burns every egg in its
         next frame. The eggs disappear
         from the server and from both games.
      3. Bob puts one egg in, then a restart: the bioreactor holds that one egg everywhere, and the burned eggs don't
         come back.
    Without BaseBioReactor_ProducePower_Patch each game burns its own copies and the server never hears of it (#2494):
    the eggs come back after the restart.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's7-bioreactor', [string]$EggTechType = 'GasopodEgg')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'S7Helpers.ps1')
$A = 'Alice'
$B = 'Bob'
$reactor = $S7.BioReactor

function Get-NewEggs {
    param([string]$Player, [int]$Count)
    # An egg reports "<Egg>Undiscovered" until its creature is known
    $before = @((Invoke-BridgeJson $Player 'state').inventory | ForEach-Object { $_.id })
    Invoke-Bridge $Player "console item $EggTechType $Count" | Out-Null
    $eggs = Wait-Until { $now = @((Invoke-BridgeJson $Player 'state').inventory | Where-Object { $_.techType -like "$EggTechType*" -and $before -notcontains $_.id }); if ($now.Count -ge $Count) { , $now } } -TimeoutSeconds 15
    @($eggs | ForEach-Object { $_.id })
}

# Puts the item into the reactor the way the inventory UI does (IItemsContainer.AddItem checks room); true if it went in
function Add-ToReactor {
    param([string]$Player, [string]$ItemId)
    $result = Invoke-Bridge $Player "call $reactor BaseBioReactor.container.AddItem $ItemId"
    $result -ne 'null'
}

function Assert-ReactorContent {
    param([int]$Expected, [string]$Context)
    $agreed = Wait-Until { @('server', $A, $B | Where-Object { (Get-ContainerContent $_ $reactor).Count -eq $Expected }).Count -eq 3 } -TimeoutSeconds 20
    foreach ($name in 'server', $A, $B) {
        $content = Get-ContainerContent $name $reactor
        Assert-Equal $Expected $content.Count "$Context, $name has $Expected items in the bioreactor ($($content.Text))"
    }
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -Fixture $S7.Fixture -NoLaunch:$NoLaunch
    Initialize-S7Players $A, $B

    Step 'Both players in the room, bioreactor empty' {
        foreach ($player in $A, $B) {
            $found = Wait-Until { Invoke-BridgeJson $player "entity $reactor" } -TimeoutSeconds 30
            Assert-True ([bool]$found) "$player has the bioreactor loaded"
        }
        $script:producer = Get-S7Producer
        Write-Host "  base lock (producer): $producer"
        Assert-True ($producer -in $A, $B) 'one of the players holds the base lock'
        $size = Invoke-BridgeJson $A "call TechData.GetItemSize $EggTechType"
        $script:capacity = [math]::Floor(4 / $size.x) * [math]::Floor(4 / $size.y)
        Write-Host "  $EggTechType is $($size.x)x$($size.y): $capacity fit"
        Assert-True ($capacity -ge 2 -and $capacity -le 4) "$EggTechType is a big item (2 to 4 fit)"
        Assert-ReactorContent 0 'start'
    }

    Step "Alice adds 2 eggs, Bob tries to add 3 more" {
        $script:allEggs = @()
        $aliceEggs = Get-NewEggs $A 2
        foreach ($egg in $aliceEggs) { Assert-True (Add-ToReactor $A $egg) "$A put egg $egg in" }
        $script:allEggs += $aliceEggs
        Assert-ReactorContent 2 'after Alice'
        $bobEggs = Get-NewEggs $B 3
        $accepted = 0
        foreach ($egg in $bobEggs) {
            if (Add-ToReactor $B $egg) { $accepted++; $script:allEggs += $egg }
            Start-Sleep -Milliseconds 500
        }
        Assert-Equal ($capacity - 2) $accepted "$B's game took only what fits"
        Assert-ReactorContent $capacity 'full'
    }

    Step 'The producer burns the eggs, they are gone everywhere' {
        foreach ($player in $A, $B) {
            # It only burns while its power source has room (ProducePower asks for maxPower - power at most)
            Invoke-Bridge $player "set $reactor BaseBioReactor._powerSource.power 0" | Out-Null
            Invoke-Bridge $player "set $reactor BaseBioReactor._toConsume 100000" | Out-Null
        }
        Assert-ReactorContent 0 'after burning'
        $server = Get-BridgeEntities 'server'
        $left = @($allEggs | Where-Object { $server.Entities.ContainsKey($_) })
        Assert-Equal 0 $left.Count "the server has none of the burned eggs ($($left -join ', '))"
        foreach ($player in $A, $B) {
            $ghosts = @($allEggs | Where-Object { (Invoke-Bridge $player "entity $_" -AllowError) -notmatch '^ERR' })
            Assert-Equal 0 $ghosts.Count "$player has none of the burned eggs"
        }
    }

    Step 'Bob adds one egg, restart: one egg everywhere, burned eggs stay gone' {
        foreach ($player in $A, $B) { Invoke-Bridge $player "set $reactor BaseBioReactor._toConsume 0" | Out-Null }
        $egg = @(Get-NewEggs $B 1)
        Assert-True (Add-ToReactor $B $egg[0]) "$B put an egg in"
        Assert-ReactorContent 1 'one egg'
        Restart-S7World $SaveName $A, $B
        foreach ($player in $A, $B) { Wait-Until { Invoke-BridgeJson $player "entity $reactor" } -TimeoutSeconds 30 | Out-Null }
        Assert-ReactorContent 1 'after restart'
        $server = Get-BridgeEntities 'server'
        Assert-Equal 0 @($allEggs | Where-Object { $server.Entities.ContainsKey($_) }).Count 'the burned eggs did not come back on the server'
        $script:lastEgg = $egg[0]
    }

    Assert-NoDiff -Ids (@($reactor, $lastEgg) + $allEggs) 'no desync reported for the bioreactor or the eggs'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S7 BioreactorSync' -Keep:$Keep
