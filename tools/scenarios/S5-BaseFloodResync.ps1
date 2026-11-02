<#
.SYNOPSIS
    FIX_SPEC S5b (#2808): a player who rejoins sees the base's water level where it was, not restarting from zero.

.DESCRIPTION
    Alice builds a corridor (BaseHelpers.ps1 New-ScenarioBase), the base's lock holder crushes the hull until it
    leaks, and the base floods (BaseFloodSim, simulated in both games from the synced leaks). While it floods, Bob's
    water level must follow the lock holder's. Bob then welds the leaks closed, so the water starts draining slowly
    (1/60 per second). The server saves, everything restarts from the save and both players rejoin: both must start
    from the saved water level (minus what drained since), not from a dry base, and agree with each other.

    Starts a fresh world "s5-flood" and two games (opens windows), unless -NoLaunch. Needs a Debug build.
#>
param(
    [switch]$NoLaunch,
    [switch]$Keep,
    # With -NoLaunch: the save the running server has (the restart step starts it again)
    [string]$SaveName = 's5-flood',
    # Where it happens; another spot lets several scenarios share one world
    [string]$buildSpot = '-40 -42 360'
)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Format-Level {
    param([double]$Level)
    $Level.ToString('0.00', $script:Invariant)
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
        $script:other = if ($owner -eq $A) { $B } else { $A }
        Write-Host "  simulation lock: $owner"
        Assert-True ($owner -in $A, $B) 'one of the players simulates the base'
        Assert-Equal 0 (Get-FloodTotal $A $baseId) 'the new base is dry'
    }

    Step 'The hull leaks and the base floods' {
        Assert-True (Invoke-HullCrush $owner $baseId -MaxCellHealth 40) "$owner's base has a leaking cell"
        $flooded = Wait-Until { (Get-FloodTotal $owner $baseId) -ge 0.8 } -TimeoutSeconds 60
        Assert-True ([bool]$flooded) "the water in $owner's base rises to 0.8 ($(Format-Level (Get-FloodTotal $owner $baseId)))"
    }

    Step 'Bob welds the leaks closed, the water drains slowly' {
        $count = [int](Invoke-Bridge $B "get $baseId BaseHullStrength.victims.Count")
        for ($i = 0; $i -lt $count; $i++) {
            if ((ConvertTo-Double (Invoke-Bridge $B "get $baseId BaseHullStrength.victims[$i].health")) -lt 100) {
                Invoke-Bridge $B "call $baseId BaseHullStrength.victims[$i].AddHealth 1000" | Out-Null
            }
        }
        $repaired = Wait-Until { (Get-ServerLeaks $baseId).Count -eq 0 } -TimeoutSeconds 15
        Assert-True ([bool]$repaired) 'the leaks are repaired'
        # The lock holder sends at most every 2 s
        Start-Sleep -Seconds 5
        $ownerLevel = Get-FloodTotal $owner $baseId
        $otherLevel = Get-FloodTotal $other $baseId
        Write-Host "  water: $owner $(Format-Level $ownerLevel), $other $(Format-Level $otherLevel)"
        Assert-True ($ownerLevel -gt 0.5) "$owner's base is still partly flooded"
        Assert-True ([Math]::Abs($ownerLevel - $otherLevel) -le 0.1) "$other's water level follows $owner's"
        $script:serverMetadata = (Get-BridgeEntities 'server').Entities[$baseId].Metadata
        Write-Host "  server base metadata: $serverMetadata"
        Assert-True ($serverMetadata -match 'BaseFloodMetadata') 'the server stores the flood levels'
    }

    Step 'Restart: the server saves, both players rejoin' {
        $script:levelBefore = Get-FloodTotal $owner $baseId
        Restart-ScenarioWorld -SaveName $SaveName
        $script:restartedAt = Get-Date
        Invoke-Bridge $A "teleport $buildSpot" | Out-Null
        Invoke-Bridge $B "teleport $buildSpot" | Out-Null
        $loaded = Wait-Until { (Invoke-BridgeJson $A "entity $baseId") -and (Invoke-BridgeJson $B "entity $baseId") } -TimeoutSeconds 60
        Assert-True ([bool]$loaded) 'both games have the base after the restart'
    }

    Step 'Both players start from the saved water level' {
        Start-Sleep -Seconds 2
        $levelA = Get-FloodTotal $A $baseId
        $levelB = Get-FloodTotal $B $baseId
        Write-Host "  water before the restart $(Format-Level $levelBefore); after: Alice $(Format-Level $levelA), Bob $(Format-Level $levelB)"
        # Draining runs at 1/60 per second once the game is loaded again; a minute of it is a generous bound
        Assert-True ($levelA -ge $levelBefore - 1.0 / 60 * 60 -and $levelA -gt 0.1) "Alice's base didn't start dry again"
        Assert-True ($levelB -ge $levelBefore - 1.0 / 60 * 60 -and $levelB -gt 0.1) "Bob's base didn't start dry again"
        Assert-True ([Math]::Abs($levelA - $levelB) -le 0.1) 'Alice and Bob see the same water level'
    }

    Assert-NoDiff -Ids @($baseId) 'no desync reported for the base'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S5 BaseFloodResync' -Keep:$Keep
