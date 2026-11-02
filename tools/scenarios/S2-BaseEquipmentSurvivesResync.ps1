<#
.SYNOPSIS
    FIX_SPEC S2 (#2259, #2696): equipment in a base (modules of a Seamoth docked in a moonpool, which the moonpool's
    upgrade console shows) survives "Resync base buildings" from outside and from inside, a player logging out, and a
    server restart with both players rejoining.

.DESCRIPTION
    Alice builds a moonpool, spawns a Seamoth, installs two modules (Inventory.AddOrSwap, what the module slots UI does)
    and docks it (VehicleDockingBay.OnTriggerEnter, what the physics trigger calls). Then:
      - Alice resyncs from outside the base, then from inside (BuildingHandler.AskForResync, the menu button);
      - Alice logs out (IngameMenu.QuitGame) while Bob stays;
      - the server saves, everything restarts from the save.
    After each step both games and the server must still have the docked Seamoth with both modules and its power cell.
    Base chargers and reactors use the same Equipment / InstalledModuleEntity path, but aren't built here: placing a
    wall module inside a base through the bridge didn't work (FIX_SPEC Log, 2026-11-02).

    Save "s2-equipment" (fresh world unless -NoLaunch). Opens game windows. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's2-equipment')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseEquipmentHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Invoke-Resync {
    param([string]$Label)
    # 10 s cooldown between requests (BuildingHandler.ResyncRequestCooldown)
    Start-Sleep -Seconds 11
    Invoke-Bridge $A 'call BuildingHandler.Main.AskForResync' | Out-Null
    Start-Sleep -Seconds 3
    $done = Wait-Until { (Invoke-Bridge $A 'get BuildingHandler.Main.Resyncing') -eq 'false' } -TimeoutSeconds 60
    Assert-True ([bool]$done) "$Label`: $A's resync finished"
    Start-Sleep -Seconds 3
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'Alice builds a moonpool with a docked Seamoth with two modules' {
        $script:base = Get-OrBuildDockedSeamoth $A $B
        Write-Host "  Seamoth $($base.SeamothId), modules $($base.ModuleIds -join ', '), power cell $($base.PowerCellId)"
        Assert-Equal 2 $base.ModuleIds.Count 'the server has two modules in the Seamoth'
        Assert-DockedSeamoth 'built' @($A, $B) $base.SeamothId $base.ModuleIds $base.PowerCellId
    }

    Step 'Alice resyncs from outside the base' {
        $s = $script:MoonpoolStand
        Invoke-Bridge $A ('teleport {0} {1} {2}' -f $s[0], $s[1], $s[2]) | Out-Null
        Invoke-Resync 'outside'
        Assert-DockedSeamoth 'resync outside' @($A, $B) $base.SeamothId $base.ModuleIds $base.PowerCellId
    }

    Step 'Alice resyncs from inside the moonpool' {
        $dock = (Invoke-BridgeJson $A "entity $($base.DockId)").position
        Invoke-Bridge $A ('teleport {0} {1} {2}' -f $dock[0], ($dock[1] + 2), ($dock[2] + 3.7)) | Out-Null
        Start-Sleep -Seconds 2
        Write-Host "  $A in $((Invoke-BridgeJson $A 'state').currentSub.name)"
        Invoke-Resync 'inside'
        Assert-DockedSeamoth 'resync inside' @($A, $B) $base.SeamothId $base.ModuleIds $base.PowerCellId
    }

    Step 'Alice logs out while Bob stays' {
        Invoke-Bridge $A 'call IngameMenu.main.QuitGame false' | Out-Null
        $left = Wait-Until { (Invoke-BridgeJson 'server' 'players' | Where-Object { $_.name -eq $A }).online -eq $false } -TimeoutSeconds 60
        Assert-True ([bool]$left) "$A left the game"
        Start-Sleep -Seconds 3
        Assert-DockedSeamoth 'Alice logged out' @($B) $base.SeamothId $base.ModuleIds $base.PowerCellId
    }

    Step 'Server saves, everything restarts from the save' {
        Restart-ScenarioFromSave $A $B $SaveName
        $s = $script:MoonpoolStand
        foreach ($player in $A, $B) { Invoke-Bridge $player ('teleport {0} {1} {2}' -f $s[0], $s[1], $s[2]) | Out-Null }
        $loaded = Wait-Until { (Invoke-Bridge $A "get $($base.SeamothId) Vehicle.docked" -AllowError) -eq 'true' -and (Invoke-Bridge $B "get $($base.SeamothId) Vehicle.docked" -AllowError) -eq 'true' } -TimeoutSeconds 60
        Assert-True ([bool]$loaded) 'both games have the docked Seamoth after the restart'
        Assert-DockedSeamoth 'after restart' @($A, $B) $base.SeamothId $base.ModuleIds $base.PowerCellId
    }

    # Not the Seamoth itself: a docked vehicle's game parent (base) and server parent (moonpool) differ by design, and its
    # LightsOn metadata goes stale when docking turns the lights off (both unrelated to S2)
    Assert-NoDiff -Ids (@($base.PowerCellId) + $base.ModuleIds) 'no desync reported for the Seamoth equipment'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S2 BaseEquipmentSurvivesResync' -Keep:$Keep
