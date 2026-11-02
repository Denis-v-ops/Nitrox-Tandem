<#
.SYNOPSIS
    FIX_SPEC S9 (#2898): on a brand-new world, both players start inside their escape pod, and the initial sync finds
    the pod (no "Game object required from id" / OptionalEmptyException).

.DESCRIPTION
    Starts a new world (save s9-fresh, replaced every run) with Alice and Bob. Once both joined:
    - Player.currentEscapePod is set and Player.escapePod (the "inside a pod" flag) is true for both, right after the
      join and again 10 s later (not dropped out of the pod by physics or a late position update);
    - each player stands within 5 m of an escape pod;
    - the server has an EscapePod entity, and the Nitrox, Unity and server logs written during this run contain no
      OptionalEmptyException and no "Game object required from id".

    -NoLaunch can't check a fresh world; it is only for debugging the checks against running games.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's9-fresh')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Test-InPod {
    param([string]$Player, [string]$When)
    $pod = Invoke-Bridge $Player 'get player Player.currentEscapePod'
    $inside = Invoke-Bridge $Player 'get player Player.escapePod.value'
    $state = Invoke-BridgeJson $Player 'state'
    $nearest = (Invoke-BridgeJson $Player 'find EscapePod 1') | Select-Object -First 1
    Assert-True ($pod -ne 'null') "$When : $Player's currentEscapePod is set"
    Assert-Equal 'true' $inside "$When : $Player is inside the pod (Player.escapePod)"
    Assert-True ($nearest -and $nearest.distance -lt 5) "$When : $Player is within 5 m of an escape pod (at $(Format-Vector ([double[]]$state.position)), pod $($nearest.distance) m away)"
}

try {
    $marks = Get-LogMarks @($A, $B) $SaveName
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'Both players start in the escape pod' {
        foreach ($player in $A, $B) {
            Test-InPod $player 'right after joining'
        }
        $serverPods = @((Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.TechType -eq 'EscapePod' })
        Assert-True ($serverPods.Count -ge 1) "the server has $($serverPods.Count) escape pod(s)"
    }

    Step 'Still in the pod 10 s later' {
        Start-Sleep -Seconds 10
        foreach ($player in $A, $B) {
            Test-InPod $player '10 s later'
        }
    }

    Step 'No missing-object errors in the logs of this run' {
        $texts = Get-LogTextSince $marks @($A, $B) $SaveName
        Assert-True ($texts.Count -ge 3) "read $($texts.Count) log files of this run ($((($texts.Keys | ForEach-Object { Split-Path -Leaf $_ }) -join ', ')))"
        foreach ($file in $texts.Keys) {
            $hits = @(($texts[$file] -split "`n") | Where-Object { $_ -match 'OptionalEmptyException|Game object required from id' })
            foreach ($hit in $hits | Select-Object -First 3) { Write-Host "    $(Split-Path -Leaf $file): $($hit.Trim())" -ForegroundColor Yellow }
            Assert-Equal 0 $hits.Count "$(Split-Path -Leaf $file) has no OptionalEmptyException / 'Game object required from id'"
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S9 FreshWorldSpawnsInPod' -Keep:$Keep
