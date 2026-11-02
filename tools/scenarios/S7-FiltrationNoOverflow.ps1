<#
.SYNOPSIS
    FIX_SPEC S7 (#2495): a water filtration machine holds at most 2 salt + 2 water, in both games, on the server, after
    looting, while its producer is away, and after a restart.

.DESCRIPTION
    Starts the fixture world tools\fixtures\s7-machine-base (a room with two filtration machines and a bioreactor) unless
    -NoLaunch. Both players stand in the room; fast filtering is switched on per machine in both games (the field the
    filterfast console command toggles: 80x, so salt every ~5 s and water every ~11 s).
      1. Both machines fill up: exactly Salt=2, BigFilteredWater=2 each, on the server and in both games.
      2. Bob takes a salt out of machine A (ItemsContainer.AddItem into his inventory): it refills to 2, never more.
      3. The producer (base lock holder, read from the server's query) quits to the main menu: the lock moves to the
         other player, who takes a water out of machine B and sees it refill.
      4. Restart (save, close, start from the save; quitting ends the game process, so this is also how the producer
         returns): still 2 + 2 everywhere.
    Without FiltrationMachine_Spawn_Patch both games produce and the machines end up above 2 + 2 (#2495).
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's7-filtration')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'S7Helpers.ps1')
$A = 'Alice'
$B = 'Bob'
$machines = @($S7.FiltrationA, $S7.FiltrationB)

function Assert-MachinesFull {
    param([string]$Context, [string[]]$Where = @('server', $A, $B))
    foreach ($machine in $machines) {
        foreach ($name in $Where) {
            $content = Get-ContainerContent $name $machine
            Assert-Equal 'BigFilteredWater=2, Salt=2' $content.Text "$Context, $name, machine $($machine.Substring(0, 8))"
        }
    }
}

# Highest count seen in any machine on the server while waiting (catches a short overflow)
function Watch-MaxCount {
    param([int]$Seconds)
    $max = 0
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($machine in $machines) {
            $content = Get-ContainerContent 'server' $machine
            foreach ($count in $content.Counts.Values) { if ($count -gt $max) { $max = $count } }
        }
        Start-Sleep -Seconds 2
    }
    $max
}

function Enable-FastFiltering {
    param([string[]]$Players)
    foreach ($player in $Players) {
        foreach ($machine in $machines) {
            Invoke-Bridge $player "set $machine FiltrationMachine.fastFiltering true" | Out-Null
        }
    }
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -Fixture $S7.Fixture -NoLaunch:$NoLaunch
    Initialize-S7Players $A, $B

    Step 'Both players in the room, fast filtering on in both games' {
        foreach ($player in $A, $B) {
            $found = Wait-Until { (Invoke-BridgeJson $player 'find FiltrationMachine 5').Count -ge 2 } -TimeoutSeconds 30
            Assert-True ([bool]$found) "$player has both filtration machines loaded"
            Assert-Equal 'true' (Invoke-Bridge $player 'get Player.main.currentSub.isBase') "$player is in the base"
        }
        Enable-FastFiltering $A, $B
        $script:producer = Get-S7Producer
        Write-Host "  base lock (producer): $producer"
        Assert-True ($producer -in $A, $B) 'one of the players holds the base lock'
    }

    Step 'Both machines fill up to 2 salt + 2 water, not more' {
        $full = Wait-Until { @($machines | Where-Object { (Get-ContainerContent 'server' $_).Text -eq 'BigFilteredWater=2, Salt=2' }).Count -eq 2 } -TimeoutSeconds 120 -IntervalSeconds 3
        Assert-True ([bool]$full) 'the server has 2 salt + 2 water in both machines'
        $max = Watch-MaxCount 25
        Assert-True ($max -le 2) "no machine went above 2 of a kind while running on (max $max)"
        Assert-MachinesFull 'full'
    }

    Step 'Bob loots a salt, the machine refills to 2' {
        $salt = (Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.ParentId -eq $S7.FiltrationA -and $_.TechType -eq 'Salt' } | Select-Object -First 1
        Invoke-Bridge $B "call Inventory.main.container.AddItem $($salt.Id)" | Out-Null
        $looted = Wait-Until { (Get-BridgeEntities 'server').Entities[$salt.Id].ParentId -eq (Invoke-BridgeJson $B 'state').playerId } -TimeoutSeconds 15
        Assert-True ([bool]$looted) "the server has the salt in $B's inventory"
        $refilled = Wait-Until { (Get-ContainerContent 'server' $S7.FiltrationA).Text -eq 'BigFilteredWater=2, Salt=2' } -TimeoutSeconds 60 -IntervalSeconds 2
        Assert-True ([bool]$refilled) 'machine A refilled to 2 salt'
        $max = Watch-MaxCount 20
        Assert-True ($max -le 2) "no overflow after looting (max $max)"
        Assert-MachinesFull 'after looting'
    }

    Step 'The producer leaves: the other player produces, the producer rejoins' {
        $other = if ($producer -eq $A) { $B } else { $A }
        Disconnect-S7Player $producer
        $handedOver = Wait-Until { (Get-S7Producer) -eq $other } -TimeoutSeconds 60
        Assert-True ([bool]$handedOver) "the base lock moved to $other"
        $water = (Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.ParentId -eq $S7.FiltrationB -and $_.TechType -eq 'BigFilteredWater' } | Select-Object -First 1
        Invoke-Bridge $other "call Inventory.main.container.AddItem $($water.Id)" | Out-Null
        $refilled = Wait-Until { (Get-ContainerContent 'server' $S7.FiltrationB).Text -eq 'BigFilteredWater=2, Salt=2' } -TimeoutSeconds 60 -IntervalSeconds 2
        Assert-True ([bool]$refilled) "machine B refilled while only $other is in the game"
        $max = Watch-MaxCount 15
        Assert-True ($max -le 2) "no overflow while only $other is in the game (max $max)"
        Assert-MachinesFull "while only $other is in the game" -Where 'server', $other
    }

    # Quitting to the main menu ends the game process, so the producer comes back through a restart of everything
    Step 'Restart, both players back: still 2 salt + 2 water everywhere' {
        Restart-S7World $SaveName $A, $B
        Enable-FastFiltering $A, $B
        $max = Watch-MaxCount 25
        Assert-True ($max -le 2) "no overflow after the restart (max $max)"
        Assert-MachinesFull 'after restart'
    }

    $ids = @($machines) + @($machines | ForEach-Object { (Get-ContainerContent 'server' $_).Ids })
    Assert-NoDiff -Ids $ids 'no desync reported for the machines or their items'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S7 FiltrationNoOverflow' -Keep:$Keep
