<#
.SYNOPSIS
    FIX_SPEC S5a (#2769, PR #2798): a base leak repaired by one player stays repaired for everyone after a restart.

.DESCRIPTION
    Alice builds a corridor (BaseHelpers.ps1 New-ScenarioBase). The base's lock holder crushes the hull (the game's
    own BaseHullStrength.CrushDamageUpdate with a negative strength, as below crush depth) until a cell leaks; the
    leak must reach Bob and the server (BaseLeakEntity). Then the server restarts and both players rejoin: leaks are
    now spawned from the server's records while the base is being built, which is where #2769 lost track of the
    leak's id. Bob welds every leak closed (LiveMixin.AddHealth, what Welder.Weld calls). The server must drop the
    leak entities, Alice must see the hull repaired, and after a second restart the base must still be whole for both.

    Starts a fresh world "s5-leaks" and two games (opens windows), unless -NoLaunch. Needs a Debug build.
#>
param(
    [switch]$NoLaunch,
    [switch]$Keep,
    # With -NoLaunch: the save the running server has (the restart step starts it again)
    [string]$SaveName = 's5-leaks',
    # Where it happens; another spot lets several scenarios share one world
    [string]$buildSpot = '-40 -42 360'
)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
# A few metres above the sea floor (about -47 m here)

function Assert-HullWhole {
    param([string]$When)
    foreach ($player in $A, $B) {
        $health = @(Get-CellHealth $player $baseId)
        Assert-True (($health | Where-Object { $_ -lt 100 }).Count -eq 0) "$When $player's base cells are at full health ($($health -join ', '))"
    }
    Assert-Equal 0 (Get-ServerLeaks $baseId).Count "$When the server has no leak under the base"
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'Alice builds a corridor, Bob comes along' {
        Invoke-Bridge $A "teleport $buildSpot" | Out-Null
        Invoke-Bridge $B "teleport $buildSpot" | Out-Null
        Start-Sleep -Seconds 3
        $script:baseId = New-ScenarioBase $A
        Write-Host "  base $baseId"
        $seen = Wait-Until { Invoke-BridgeJson $B "entity $baseId" } -TimeoutSeconds 30
        Assert-True ([bool]$seen) 'Bob sees the base'
        $script:owner = Wait-Until { Get-BaseLockOwner $baseId } -TimeoutSeconds 10
        Write-Host "  simulation lock: $owner"
        Assert-True ($owner -in $A, $B) 'one of the players simulates the base'
    }

    Step 'The hull is crushed until a cell leaks' {
        Assert-True (Invoke-HullCrush $owner $baseId) "$owner's base has a leaking cell"
        $synced = Wait-Until {
            $a = (Get-CellHealth $A $baseId) -join ','
            $b = (Get-CellHealth $B $baseId) -join ','
            if ($a -eq $b -and (Get-ServerLeaks $baseId).Count -gt 0) { $true }
        } -TimeoutSeconds 15
        Write-Host "  cell health: Alice $((Get-CellHealth $A $baseId) -join ', ') Bob $((Get-CellHealth $B $baseId) -join ', '); server leaks: $((Get-ServerLeaks $baseId).Values -join ' | ')"
        Assert-True ([bool]$synced) 'Bob and the server have the same leaks as Alice'
    }

    Step 'Restart: the server loads the leaks, both players rejoin' {
        $script:leaksBefore = Get-ServerLeaks $baseId
        Restart-ScenarioWorld -SaveName $SaveName
        Invoke-Bridge $A "teleport $buildSpot" | Out-Null
        Invoke-Bridge $B "teleport $buildSpot" | Out-Null
        $loaded = Wait-Until { (Invoke-BridgeJson $A "entity $baseId") -and (Invoke-BridgeJson $B "entity $baseId") } -TimeoutSeconds 60
        Assert-True ([bool]$loaded) 'both games have the base after the restart'
        Start-Sleep -Seconds 3
        Assert-Equal (($leaksBefore.Keys | Sort-Object) -join ',') (((Get-ServerLeaks $baseId).Keys | Sort-Object) -join ',') 'the server kept exactly the leaks it had'
        foreach ($player in $A, $B) {
            $health = @(Get-CellHealth $player $baseId)
            Assert-True (($health | Where-Object { $_ -lt 100 }).Count -gt 0) "$player's base still leaks after the restart ($($health -join ', '))"
        }
    }

    Step 'Bob welds every leak closed' {
        $count = [int](Invoke-Bridge $B "get $baseId BaseHullStrength.victims.Count")
        for ($i = 0; $i -lt $count; $i++) {
            if ((ConvertTo-Double (Invoke-Bridge $B "get $baseId BaseHullStrength.victims[$i].health")) -lt 100) {
                Invoke-Bridge $B "call $baseId BaseHullStrength.victims[$i].AddHealth 1000" | Out-Null
            }
        }
        $repaired = Wait-Until { (Get-ServerLeaks $baseId).Count -eq 0 -and ((Get-CellHealth $A $baseId) | Where-Object { $_ -lt 100 }).Count -eq 0 } -TimeoutSeconds 15
        Assert-True ([bool]$repaired) 'the server dropped the leaks and Alice sees them repaired'
        Assert-HullWhole 'repaired:'
    }

    Step 'Restart again: the repair is kept' {
        Restart-ScenarioWorld -SaveName $SaveName
        Invoke-Bridge $A "teleport $buildSpot" | Out-Null
        Invoke-Bridge $B "teleport $buildSpot" | Out-Null
        $loaded = Wait-Until { (Invoke-BridgeJson $A "entity $baseId") -and (Invoke-BridgeJson $B "entity $baseId") } -TimeoutSeconds 60
        Assert-True ([bool]$loaded) 'both games have the base after the second restart'
        Start-Sleep -Seconds 3
        Assert-HullWhole 'after restart:'
    }

    Assert-NoDiff -Ids @($baseId) 'no desync reported for the base'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S5 BaseLeaksPersist' -Keep:$Keep
