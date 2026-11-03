<#
.SYNOPSIS
    FIX_SPEC S3 (#2640): when one player disables the Precursor gun at the Quarantine Enforcement Platform terminal,
    the partner's game must know the gun is disabled.

.DESCRIPTION
    Alice gets the cure goal (the terminal's own onPlayerCuredGoal key, executed with the "goal" console command),
    both players go to the terminal, Alice walks into the terminal area and uses it through the real
    PrecursorDisableGunTerminal.OnHandClick -> cinematic -> OnPlayerCinematicModeEnd -> DisableGun path.
    Then Bob's StoryGoalCustomEventHandler.gunDisabled must be true, which is what LaunchRocket.OnHandClick checks
    (gunDisabled || forcedRocketReady). Not covered: clicking a real rocket (none is built in a fresh world).

    Runs with StoryGatesEnabled=False (written into the save's server.cfg), the switch that lets one player disable the
    gun alone. With story gates on, both players must use the terminal: G1-GunAndRocketTogether.ps1 covers that.

    Starts a fresh world and two games (opens windows), unless -NoLaunch. Needs a Debug build (dual-client.ps1 -NoBuild
    is used, so build first).
#>
param([switch]$NoLaunch, [switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$terminal = 'find:PrecursorDisableGunTerminal'
# GotoConsoleCommand location "gunterminal" (TeleportCommandData asset, read through the bridge).
$terminalRoom = '372.9 -72.7 1078.4'

try {
    if (-not $NoLaunch) {
        $saveDir = Join-Path (Get-NitroxRig).SavesDir 'bridge-scenario'
        if (Test-Path $saveDir) { Remove-Item -Recurse -Force $saveDir }
        New-Item -ItemType Directory -Force $saveDir | Out-Null
        Set-Content -Path (Join-Path $saveDir 'server.cfg') -Value 'StoryGatesEnabled=False' -Encoding UTF8
    }
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch -KeepSave

    Step 'Both players go to the gun terminal' {
        Invoke-Bridge $A "teleport $terminalRoom" | Out-Null
        Invoke-Bridge $B 'teleport 370 -72.7 1076' | Out-Null
        $found = Wait-Until { (Invoke-BridgeJson $A 'find PrecursorDisableGunTerminal') -and (Invoke-BridgeJson $B 'find PrecursorDisableGunTerminal') } -TimeoutSeconds 60
        Assert-True ([bool]$found) 'the terminal is loaded in both games'
        foreach ($player in $A, $B) {
            Assert-Equal 'false' (Invoke-Bridge $player 'get StoryGoalCustomEventHandler.main.gunDisabled') "$player starts with the gun active (fresh world)"
        }
    }

    Step 'Alice is cured (terminal goal onPlayerCuredGoal)' {
        $script:cureGoal = (Invoke-Bridge $A "get $terminal PrecursorDisableGunTerminal.onPlayerCuredGoal.key").Trim('"')
        $script:gunGoal = (Invoke-Bridge $A "get $terminal PrecursorDisableGunTerminal.gunDeactivate.key").Trim('"')
        Write-Host "  cure goal $cureGoal, gun goal $gunGoal"
        Invoke-Bridge $A "console goal Story $cureGoal" | Out-Null
        $synced = Wait-Until { (Invoke-Bridge $B "call Story.StoryGoalManager.main.IsGoalComplete $cureGoal") -eq 'true' } -TimeoutSeconds 15
        Assert-True ([bool]$synced) "$B also has the cure goal"
        Assert-Equal 'true' (Invoke-Bridge $A "get $terminal PrecursorDisableGunTerminal.playerCured") "$A's terminal accepts the cure"
    }

    Step 'Alice enters the terminal area, the terminal opens' {
        $area = (Invoke-BridgeJson $A 'find PrecursorDisableGunTerminalArea')[0]
        $p = $area.position
        Invoke-Bridge $A ("teleport {0} {1} {2}" -f $p[0].ToString([Globalization.CultureInfo]::InvariantCulture), ($p[1] - 0.8).ToString([Globalization.CultureInfo]::InvariantCulture), $p[2].ToString([Globalization.CultureInfo]::InvariantCulture)) | Out-Null
        $opened = Wait-Until { (Invoke-Bridge $A "get $terminal PrecursorDisableGunTerminal.opened") -eq 'true' } -TimeoutSeconds 10
        Assert-True ([bool]$opened) "$A's terminal opened (OnTerminalAreaEnter)"
    }

    Step 'Alice uses the terminal (OnHandClick, cinematic, DisableGun)' {
        Invoke-Bridge $A "use $terminal PrecursorDisableGunTerminal" | Out-Null
        $started = Wait-Until { [int](Invoke-Bridge $A 'get PlayerCinematicController.cinematicModeCount') -gt 0 } -TimeoutSeconds 5 -IntervalSeconds 0.3
        Assert-True ([bool]$started) "$A's terminal cinematic started"
        $done = Wait-Until { (Invoke-Bridge $A 'get StoryGoalCustomEventHandler.main.gunDisabled') -eq 'true' } -TimeoutSeconds 40
        Assert-True ([bool]$done) "$A disabled the gun"
    }

    Step 'Bob sees the gun disabled' {
        $bobDisabled = Wait-Until { (Invoke-Bridge $B 'get StoryGoalCustomEventHandler.main.gunDisabled') -eq 'true' } -TimeoutSeconds 15
        Assert-True ([bool]$bobDisabled) "$B's StoryGoalCustomEventHandler.gunDisabled is true (LaunchRocket's gun check passes)"
        Assert-Equal 'true' (Invoke-Bridge $B "call Story.StoryGoalManager.main.IsGoalComplete $gunGoal") "$B has the $gunGoal goal"
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S3 GunDisableSyncs' -Keep:$Keep
