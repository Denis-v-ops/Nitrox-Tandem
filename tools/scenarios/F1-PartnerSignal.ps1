<#
.SYNOPSIS
    COOP_FEATURES_SPEC F1: the partner's marker is faint and hidden up close, the name tag shows only up close, the label
    carries the depth, and the client setting switches the marker off.

.DESCRIPTION
    Bob stays put; Alice is teleported to 5, 30 and 200 m from him. Each time Bob's game reads Alice's marker
    (PingInstance.minDist/range on her body) and her name tag (RemotePlayerVitals.canvas.enabled), the fields the game
    itself uses, plus the PartnerSignal component's view (distance, line of sight, rule). Then Bob's setting is put to
    Off and Always for a moment (PlayerPrefs; the key is deleted afterwards, so the default applies again).

    Starts a fresh world and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\F1-PartnerSignal.ps1
#>
param([switch]$NoLaunch, [switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$bobSpot = @(263, -60, 64)

function Read-Signal {
    $signal = [ordered]@{}
    foreach ($member in 'Distance', 'InLineOfSight', 'Rule', 'Label') { $signal[$member] = (Invoke-Bridge $B "get $aliceId PartnerSignal.$member" -AllowError).Trim('"') }
    $signal['minDist'] = [double](Invoke-Bridge $B "get $aliceId PingInstance.minDist")
    $signal['range'] = [double](Invoke-Bridge $B "get $aliceId PingInstance.range")
    $signal['nameTag'] = Invoke-Bridge $B "get $aliceId RemotePlayerVitals.canvas.enabled"
    Write-Host "  Bob sees Alice: $($signal | ConvertTo-Json -Compress)"
    $signal
}

function Move-AliceTo {
    param([double]$Distance)
    Invoke-Bridge $A ("teleport {0} {1} {2}" -f $bobSpot[0], $bobSpot[1], ($bobSpot[2] + $Distance)) | Out-Null
    # The marker is updated four times a second from the partner's position, which arrives with her movement packets
    Start-Sleep -Seconds 3
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch
    $script:aliceId = (Invoke-BridgeJson $A 'state').playerId

    Step 'Bob in open water; Alice 5 m away, in sight' {
        Invoke-Bridge $B ("teleport {0} {1} {2}" -f $bobSpot[0], $bobSpot[1], $bobSpot[2]) | Out-Null
        Move-AliceTo 5
        $s = Read-Signal
        Assert-True ($s.minDist -gt [double]$s.Distance) "Alice's marker is hidden at 5 m (minDist $($s.minDist) > distance $($s.Distance))"
        Assert-Equal 'true' $s.nameTag 'her name tag shows at 5 m'
        Assert-True ($s.Label -match '^Alice . 5\d m deep$') "the label carries her depth ($($s.Label))"
    }

    Step 'Alice 30 m away' {
        Move-AliceTo 30
        $s = Read-Signal
        Assert-Equal 12 $s.minDist 'the marker shows beyond 12 m'
        Assert-Equal 150 $s.range 'and fades in over 150 m (faint)'
        Assert-Equal 'false' $s.nameTag 'no name tag at 30 m'
    }

    Step 'Alice 200 m away' {
        Move-AliceTo 200
        $s = Read-Signal
        Assert-Equal 12 $s.minDist 'the marker still shows at 200 m'
        Assert-Equal 150 $s.range 'faint'
        Assert-Equal 'false' $s.nameTag 'no name tag at 200 m'
    }

    Step "Bob's setting: Off, then Always" {
        Move-AliceTo 30
        try {
            Invoke-Bridge $B 'set NitroxPrefs.PartnerSignal.Value 0' | Out-Null
            Start-Sleep -Seconds 1
            $off = Read-Signal
            Assert-True ($off.minDist -gt 1e30) 'Off hides the marker at 30 m'
            Invoke-Bridge $B 'set NitroxPrefs.PartnerSignal.Value 2' | Out-Null
            Start-Sleep -Seconds 1
            $always = Read-Signal
            Assert-Equal 'always' $always.Rule 'Always uses the signal prefab distances'
            Write-Host "  the signal prefab's own minDist/range: $($always.minDist) / $($always.range)"
        }
        finally {
            Invoke-Bridge $B 'call PlayerPrefs.DeleteKey Nitrox.partnerSignal' | Out-Null
            Invoke-Bridge $B 'call PlayerPrefs.Save' | Out-Null
        }
        Start-Sleep -Seconds 1
        Assert-Equal 'faint' (Invoke-Bridge $B "get $aliceId PartnerSignal.Rule").Trim('"') 'back to the default (Faint)'
    }
}
catch {
    Write-Host "Scenario aborted: $($_.Exception.Message)" -ForegroundColor Red
}

Complete-Scenario -Name 'F1-PartnerSignal' -Keep:$Keep
