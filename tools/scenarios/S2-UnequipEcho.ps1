<#
.SYNOPSIS
    FIX_SPEC S2, root cause B (echo): an item one player takes out of an equipment slot while the other player is nearby
    must not be deleted by an EntityDestroyed echo from the other player's game.

.DESCRIPTION
    Uses the moonpool with a docked Seamoth of S2-BaseEquipmentSurvivesResync (built if missing). With Bob next to the
    Seamoth, Alice moves a module from the Seamoth into her inventory (Inventory.AddOrSwap, what dragging it does).
    The server must record the module in Alice's inventory (not delete it), Bob's Seamoth must lose it, and after the
    server saves and everything restarts Alice must still have it.

    Save "s2-unequip" (fresh world unless -NoLaunch). Opens game windows. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's2-unequip')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseEquipmentHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    $script:alicePlayerId = (Invoke-BridgeJson $A 'state').playerId

    Step 'A docked Seamoth with modules, both players next to it' {
        $script:base = Get-OrBuildDockedSeamoth $A $B
        Assert-True ($base.ModuleIds.Count -ge 1) "the Seamoth has modules ($($base.ModuleIds -join ', '))"
        $script:module = $base.ModuleIds[-1]
        $script:remaining = @($base.ModuleIds | Where-Object { $_ -ne $module })
        Write-Host "  Seamoth $($base.SeamothId), Alice takes $module"
    }

    Step 'Alice takes a module out while Bob is nearby' {
        Assert-Equal 'true' (Invoke-Bridge $A "call Inventory.AddOrSwap $module player") "$A moved the module to her inventory"
        $inInventory = Wait-Until { Get-InventoryItem $A -Id $module } -TimeoutSeconds 10
        Assert-True ([bool]$inInventory) "the module is in $A's inventory"
        $server = Wait-Until { $r = (Get-BridgeEntities 'server').Entities[$module]; if ($r -and $r.ParentId -eq $alicePlayerId) { $r } } -TimeoutSeconds 10
        Assert-True ([bool]$server) "the server has the module in $A's inventory"
        Start-Sleep -Seconds 3
        Assert-True ([bool](Get-BridgeEntities 'server').Entities[$module]) 'the server still has the module a few seconds later (no echoed EntityDestroyed)'
        Assert-DockedSeamoth 'module taken' @($A, $B) $base.SeamothId $remaining $base.PowerCellId
    }

    Step 'Server saves, everything restarts from the save' {
        $saveName = $SaveName
        Restart-ScenarioFromSave $A $B $saveName
        $s = $script:MoonpoolStand
        foreach ($player in $A, $B) { Invoke-Bridge $player ('teleport {0} {1} {2}' -f $s[0], $s[1], $s[2]) | Out-Null }
        Assert-True ([bool](Wait-Until { Get-InventoryItem $A -Id $module } -TimeoutSeconds 30)) "$A still has the module after the restart"
        $loaded = Wait-Until { (Invoke-Bridge $B "get $($base.SeamothId) Vehicle.docked" -AllowError) -eq 'true' } -TimeoutSeconds 60
        Assert-True ([bool]$loaded) "$B has the docked Seamoth after the restart"
        Assert-DockedSeamoth 'after restart' @($A, $B) $base.SeamothId $remaining $base.PowerCellId
    }

    Assert-NoDiff -Ids (@($module, $base.PowerCellId) + $remaining) 'no desync reported for the Seamoth equipment'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S2 UnequipEcho' -Keep:$Keep
