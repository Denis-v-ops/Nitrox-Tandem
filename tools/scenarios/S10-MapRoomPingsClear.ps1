<#
.SYNOPSIS
    FIX_SPEC S10 (#2046, #2151): a resource broken by one player disappears from the other player's scanner room,
    whether or not the other player has its cell loaded.

.DESCRIPTION
    Alice's scanner room scans for outcrops (LimestoneChunk if listed). Bob breaks one of the listed outcrops
    (BreakableResource.BreakIntoResources, where a hit ends) while both players stand next to it: it must leave Alice's
    room and her ResourceTrackerDatabase. Then Alice goes far away (her cells around the room unload, the database keeps
    their outcrops), Bob breaks a second one: it must leave her database too.
    Also checks that the database ids are NitroxIds (ResourceTracker.uniqueId), which the unloaded case relies on.

    Save "s10-pings" (fresh world unless -NoLaunch). Opens game windows. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's10-pings')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
# Far enough for Alice's cells around the scanner room to unload
$farAway = '-30 -4 1100'

function Test-InDatabase {
    param([string]$Player, [string]$TechType, [string]$Id)
    @(@(Invoke-BridgeJson $Player "call ResourceTrackerDatabase.GetNodes $TechType") | ForEach-Object { $_.uniqueId }) -contains $Id
}

<#
    A resource Alice's room lists that Bob has loaded and that is a breakable outcrop.
#>
function Select-Outcrop {
    param([string[]]$Exclude = @())
    foreach ($id in (Get-MapRoomNodeIds $A $mapRoomId)) {
        if ($Exclude -contains $id) { continue }
        $onBob = Invoke-Bridge $B "get $id BreakableResource.broken" -AllowError
        if ($onBob -eq 'false') { return $id }
    }
    $null
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'Alice scans for outcrops' {
        $script:mapRoomId = Get-OrBuildMapRoom $A $B
        $script:target = Start-MapRoomScan $A $mapRoomId 'LimestoneChunk'
        Write-Host "  scanning for $target"
        $script:first = Wait-Until { Select-Outcrop } -TimeoutSeconds 30
        Assert-True ([bool]$first) "$A's room lists a $target that $B has loaded"
        Assert-Equal $first ((Invoke-Bridge $A "get $first ResourceTracker.uniqueId").Trim('"')) "$A's resource tracker uses the NitroxId"
        Assert-True (Test-InDatabase $A $target $first) "$first is in $A's resource database"
    }

    Step 'Bob breaks a listed outcrop next to Alice' {
        Invoke-Bridge $B "call $first BreakableResource.BreakIntoResources" | Out-Null
        $gone = Wait-Until { -not (Test-InDatabase $A $target $first) } -TimeoutSeconds 10
        Assert-True ([bool]$gone) "$first left $A's resource database"
        Assert-True ((Get-MapRoomNodeIds $A $mapRoomId) -notcontains $first) "$first left $A's scanner room"
    }

    Step 'Bob breaks another one while Alice is far away' {
        $script:second = Select-Outcrop -Exclude $first
        Assert-True ([bool]$second) "$A's room lists a second $target"
        Invoke-Bridge $A "teleport $farAway" | Out-Null
        $unloaded = Wait-Until { (Invoke-Bridge $A "entity $second" -AllowError).StartsWith('ERR') } -TimeoutSeconds 60
        Assert-True ([bool]$unloaded) "$second is unloaded in $A's game"
        Assert-True (Test-InDatabase $A $target $second) "$second is still in $A's resource database"
        Invoke-Bridge $B "call $second BreakableResource.BreakIntoResources" | Out-Null
        $gone = Wait-Until { -not (Test-InDatabase $A $target $second) } -TimeoutSeconds 10
        Assert-True ([bool]$gone) "$second left $A's resource database while unloaded"
        Move-PlayersToMapRoom $A $B
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S10 MapRoomPingsClear' -Keep:$Keep
