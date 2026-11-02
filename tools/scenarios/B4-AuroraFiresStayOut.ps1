<#
.SYNOPSIS
    COOP_FEATURES_SPEC B4: an Aurora fire one player puts out is out for both players, and stays out after a restart.

.DESCRIPTION
    Alice explodes the Aurora (server command aurora explode, from a player). Both players go to the cargo room, where
    the fires are world entities from the batch (Aurora_ExtinguishableFire_*, the Fire is their child). Alice douses the
    nearest fire as the fire extinguisher does: a few small Fire.Douse calls (one per frame while spraying), then one that
    puts it out. Checks: Bob's copy goes out and is removed, the server drops the record. Then the server restarts and
    both players rejoin the cargo room: the fire is gone for both, and the server still has no record of it, while the
    fires nobody touched are back for both.

    Starts a fresh world "b4-aurora-fires" and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\B4-AuroraFiresStayOut.ps1
#>
param(
    [switch]$NoLaunch,
    [switch]$Keep,
    # With -NoLaunch: the save the running server has (the restart step starts it again)
    [string]$SaveName = 'b4-aurora-fires'
)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
# Aurora cargo room (A0 probe: 33 fires here after the explosion)
$cargoRoom = '940 14 18'

# Ids of the loaded fires near the player: the NitroxId of the fire's parent (the Aurora_ExtinguishableFire_* entity)
function Get-FireIds {
    param([Parameter(Mandatory)][string]$Player)
    @(Invoke-BridgeJson $Player 'find Fire 200') | Where-Object { $_.distance -lt 60 -and $_.id -match '^\(in (.+)\)$' } |
        ForEach-Object { $_.id -replace '^\(in (.+)\)$', '$1' } | Sort-Object -Unique
}

function Test-ServerHas {
    param([Parameter(Mandatory)][string]$Id)
    -not ((Invoke-Bridge 'server' "query $Id") -match 'not found')
}

function Enter-CargoRoom {
    foreach ($player in $A, $B) { Invoke-Bridge $player "teleport $cargoRoom" | Out-Null }
    $loaded = Wait-Until { (Get-FireIds $A).Count -gt 5 -and (Get-FireIds $B).Count -gt 5 } -TimeoutSeconds 60
    Assert-True ([bool]$loaded) 'both games have the cargo room fires loaded'
    Start-Sleep -Seconds 3
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'Alice explodes the Aurora, both go to the cargo room' {
        # The aurora command must come from a player (RequiresOrigin PLAYER)
        Invoke-Bridge $A 'chat /aurora explode' | Out-Null
        Start-Sleep -Seconds 20
        Enter-CargoRoom
        $script:firesBefore = @(Get-FireIds $A)
        $bobFires = @(Get-FireIds $B)
        Write-Host "  fires: Alice $($firesBefore.Count), Bob $($bobFires.Count)"
        $script:fireId = @(Invoke-BridgeJson $A 'find Fire 200' | Where-Object { $_.id -match '^\(in (.+)\)$' -and $bobFires -contains ($_.id -replace '^\(in (.+)\)$', '$1') }) |
            Select-Object -First 1 | ForEach-Object { $_.id -replace '^\(in (.+)\)$', '$1' }
        Assert-True ([bool]$fireId) 'Alice and Bob have the same fire loaded'
        Write-Host "  fire $fireId ($((Invoke-BridgeJson $A "entity $fireId").name))"
        Assert-True (Test-ServerHas $fireId) 'the server has the fire'
        Assert-True ((ConvertTo-Double (Invoke-Bridge $B "get $fireId Fire.livemixin.health")) -gt 0) "the fire burns in Bob's game"
    }

    Step 'Alice douses the fire like the extinguisher, until it is out' {
        # FireExtinguisher.UseExtinguisher calls Fire.Douse(20 * deltaTime) every frame it sprays; each bridge call is a frame.
        for ($i = 0; $i -lt 5; $i++) { Invoke-Bridge $A "call $fireId Fire.Douse 0.5" | Out-Null }
        Invoke-Bridge $A "call $fireId Fire.Douse 1000" | Out-Null
        # Fire.Extinguished destroys the fire's parent 4 s later
        $aliceOut = Wait-Until { -not (Get-FireIds $A | Where-Object { $_ -eq $fireId }) } -TimeoutSeconds 15
        Assert-True ([bool]$aliceOut) "the fire is gone from Alice's game"
        $bobOut = Wait-Until { -not (Get-FireIds $B | Where-Object { $_ -eq $fireId }) } -TimeoutSeconds 15
        Assert-True ([bool]$bobOut) "the fire is gone from Bob's game"
        Assert-True (-not (Test-ServerHas $fireId)) 'the server dropped the fire'
    }

    Step 'Restart: the fire stays out for both, the others still burn' {
        Restart-ScenarioWorld -SaveName $SaveName
        Enter-CargoRoom
        $aliceFires = @(Get-FireIds $A)
        $bobFires = @(Get-FireIds $B)
        Write-Host "  fires: Alice $($aliceFires.Count), Bob $($bobFires.Count) (before: $($firesBefore.Count))"
        Assert-True (-not ($aliceFires -contains $fireId)) "the doused fire isn't back in Alice's game"
        Assert-True (-not ($bobFires -contains $fireId)) "the doused fire isn't back in Bob's game"
        Assert-True (-not (Test-ServerHas $fireId)) 'the server has no record of the doused fire'
        $untouched = @($firesBefore | Where-Object { $_ -ne $fireId })
        Assert-True (@($untouched | Where-Object { $aliceFires -contains $_ -and $bobFires -contains $_ }).Count -gt 0) 'the fires nobody touched burn again for both'
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'B4 AuroraFiresStayOut' -Keep:$Keep
