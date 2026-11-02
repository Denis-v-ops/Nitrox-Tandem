<#
.SYNOPSIS
    FIX_SPEC S10 (#2410): the resource a scanner room scans for is shared, and survives a server restart and rejoin.

.DESCRIPTION
    Alice builds a scanner room (or, with -NoLaunch, uses the one at the site) and starts a scan through the scanner
    screen (uGUI_MapRoomScanner.OnStartScan, what the resource buttons call). Bob's room must scan the same resource and
    list the same resources, the server must store it (MapRoomMetadata), and both must still scan it after the server
    saves and everything restarts from the save. Prefers LimestoneChunk (also used by S10-MapRoomPingsClear).

    Save "s10-scan" (fresh world unless -NoLaunch). Opens game windows. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's10-scan')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'Both players at a scanner room' {
        $script:mapRoomId = Get-OrBuildMapRoom $A $B
        Write-Host "  scanner room $mapRoomId"
    }

    Step 'Alice starts a scan on the scanner screen' {
        $script:target = Start-MapRoomScan $A $mapRoomId 'LimestoneChunk'
        Write-Host "  scanning for $target"
        Assert-Equal $target (Get-TypeToScan $A $mapRoomId) "$A's room scans $target"
        $bobScans = Wait-Until { (Get-TypeToScan $B $mapRoomId) -eq $target } -TimeoutSeconds 10
        Assert-True ([bool]$bobScans) "$B's room scans $target too"
        $stored = Wait-Until { (Get-BridgeEntities 'server').Entities[$mapRoomId].Metadata -match $target } -TimeoutSeconds 10
        Assert-True ([bool]$stored) "the server stores the scan target ($((Get-BridgeEntities 'server').Entities[$mapRoomId].Metadata))"
        Start-Sleep -Seconds 2
        $aliceNodes = Get-MapRoomNodeIds $A $mapRoomId
        $bobNodes = Get-MapRoomNodeIds $B $mapRoomId
        Write-Host "  $A lists $($aliceNodes.Count) resources, $B $($bobNodes.Count)"
        Assert-True ($aliceNodes.Count -gt 0) "$A's room lists resources"
        Assert-Equal ($aliceNodes -join ',') ($bobNodes -join ',') "$B's room lists the same resources"
    }

    Step 'Server saves, everything restarts from the save' {
        # With -NoLaunch, -SaveName must be the save the running server uses
        Restart-ScenarioFromSave $A $B $SaveName
        Move-PlayersToMapRoom $A $B
        foreach ($player in $A, $B) {
            $scans = Wait-Until { (Get-TypeToScan $player $mapRoomId) -eq $target } -TimeoutSeconds 60
            Assert-True ([bool]$scans) "$player's room still scans $target after the restart (got $(Get-TypeToScan $player $mapRoomId))"
        }
    }

    Step 'Bob cancels the scan' {
        Invoke-Bridge $B "call find:uGUI_MapRoomScanner uGUI_MapRoomScanner.OnCancelScan" | Out-Null
        $cancelled = Wait-Until { (Get-TypeToScan $A $mapRoomId) -eq 'None' } -TimeoutSeconds 10
        Assert-True ([bool]$cancelled) "$A's room stopped scanning too"
    }

    Assert-NoDiff -Ids $mapRoomId 'no desync reported for the scanner room'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S10 MapRoomScanShared' -Keep:$Keep
