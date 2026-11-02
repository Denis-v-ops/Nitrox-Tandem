<#
.SYNOPSIS
    COOP_FEATURES_SPEC D6: a flare Alice throws burns in Bob's game too, near and far.

.DESCRIPTION
    Alice throws a flare 20 m from Bob, then another 120 m from him. Windowless games don't fire animation events, so
    the throw is started with Flare.OnToolUseAnim (what the throw animation calls); the game's own Throw then drops it.
    At 20 m Bob's copy must be lit and out of Alice's hands. At 120 m the flare's cell isn't loaded in Bob's game, so he
    gets no spawn for it; his copy must still leave Alice's inventory (before the fix it stayed there, unlit, A0 probe
    2026-11-02). When Bob swims over, the flare must load lit.

    Starts a fresh world and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\D6-ThrownFlare.ps1
#>
param([switch]$NoLaunch, [switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$bobSpot = @(263, -60, 64)

<#
    An unlit flare of Alice's that sits in a quick slot, so it can be drawn (only some flares of a stack are bound).
#>
function Get-NewFlare {
    function Find-BoundFlare {
        Get-InventoryItem $A -TechType 'Flare' | ForEach-Object { $_.id } | Where-Object {
            [int](Invoke-Bridge $A "call Inventory.main.quickSlots.GetSlotByItem $_") -ge 0 -and (Invoke-Bridge $A "get $_ Flare.flareActiveState") -eq 'false'
        } | Select-Object -First 1
    }
    $flare = Find-BoundFlare
    if ($flare) { return $flare }
    Invoke-Bridge $A 'console item flare' | Out-Null
    Wait-Until { Find-BoundFlare } -TimeoutSeconds 10
}

function Throw-Flare {
    param([string]$Id)
    if ((Invoke-BridgeJson $A 'state').held -ne $Id) {
        $slot = Invoke-Bridge $A "call Inventory.main.quickSlots.GetSlotByItem $Id"
        Invoke-Bridge $A "call Inventory.main.quickSlots.SelectImmediate $slot" | Out-Null
        Start-Sleep -Seconds 2
    }
    Assert-Equal $Id (Invoke-BridgeJson $A 'state').held 'Alice holds the flare'
    Invoke-Bridge $A "call $Id Flare.OnToolUseAnim player" | Out-Null
    # Flare.Throw runs 0.5 s later and drops it
    $thrown = Wait-Until { (Invoke-BridgeJson $A "entity $Id").parentId -ne $aliceId } -TimeoutSeconds 5 -IntervalSeconds 0.3
    Assert-True ([bool]$thrown) "Alice's game threw it"
}

function Read-Flare {
    param([string]$Player, [string]$Id)
    $entity = Invoke-Bridge $Player "entity $Id" -AllowError
    if ($entity.StartsWith('ERR ')) {
        return [ordered]@{ exists = $false }
    }
    $entity = $entity | ConvertFrom-Json
    [ordered]@{
        exists   = $true
        active   = Invoke-Bridge $Player "get $Id Flare.flareActiveState"
        light    = Invoke-Bridge $Player "get $Id Light.enabled"
        parentId = $entity.parentId
        position = $entity.position
    }
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch
    $script:aliceId = (Invoke-BridgeJson $A 'state').playerId

    Step 'Alice throws a flare 20 m from Bob' {
        Invoke-Bridge $B ("teleport {0} {1} {2}" -f $bobSpot[0], $bobSpot[1], $bobSpot[2]) | Out-Null
        Invoke-Bridge $A ("teleport {0} {1} {2}" -f $bobSpot[0], $bobSpot[1], ($bobSpot[2] + 20)) | Out-Null
        Start-Sleep -Seconds 3
        $script:nearFlare = Get-NewFlare
        Assert-True ([bool]$nearFlare) 'Alice has a flare'
        Throw-Flare $nearFlare
        $lit = Wait-Until { $f = Read-Flare $B $nearFlare; if ($f.exists -and $f.active -eq 'true') { $f } } -TimeoutSeconds 10
        $alice = Read-Flare $A $nearFlare
        Write-Host "  Alice: $($alice | ConvertTo-Json -Compress) | Bob: $((Read-Flare $B $nearFlare) | ConvertTo-Json -Compress)"
        Assert-Equal 'true' $alice.active "the flare burns in Alice's game"
        Assert-True ([bool]$lit) "the flare burns in Bob's game"
        Assert-True ($lit.light -eq 'true') "its light is on in Bob's game"
        Assert-True ($lit.parentId -ne $aliceId) "it left Alice's hands in Bob's game"
    }

    Step 'Alice throws a flare 120 m from Bob' {
        Invoke-Bridge $A ("teleport {0} {1} {2}" -f $bobSpot[0], $bobSpot[1], ($bobSpot[2] + 120)) | Out-Null
        Start-Sleep -Seconds 3
        $script:farFlare = Get-NewFlare
        Assert-True ([bool]$farFlare) 'Alice has a second flare'
        Start-Sleep -Seconds 2
        $before = Read-Flare $B $farFlare
        Assert-True ($before.exists -and $before.parentId -eq $aliceId) "before the throw, Bob's game has it in Alice's inventory"
        Throw-Flare $farFlare
        Start-Sleep -Seconds 5
        $after = Read-Flare $B $farFlare
        Write-Host "  Alice: $((Read-Flare $A $farFlare) | ConvertTo-Json -Compress) | Bob: $($after | ConvertTo-Json -Compress)"
        Assert-True (-not $after.exists -or $after.parentId -ne $aliceId) "it left Alice's inventory in Bob's game"
    }

    Step 'Bob swims over to it' {
        $flarePosition = (Invoke-BridgeJson $A "entity $farFlare").position
        Invoke-Bridge $B ("teleport {0} {1} {2}" -f $flarePosition[0], ($flarePosition[1] + 2), ($flarePosition[2] - 8)) | Out-Null
        $lit = Wait-Until { $f = Read-Flare $B $farFlare; if ($f.exists -and $f.active -eq 'true') { $f } } -TimeoutSeconds 20
        Write-Host "  Bob: $((Read-Flare $B $farFlare) | ConvertTo-Json -Compress)"
        Assert-True ([bool]$lit) "the far flare loads lit in Bob's game"
        Assert-True ($lit.parentId -ne $aliceId) 'in the world, not in her inventory'
    }
}
catch {
    Write-Host "Scenario aborted: $($_.Exception.Message)" -ForegroundColor Red
}

Complete-Scenario -Name 'D6-ThrownFlare' -Keep:$Keep
