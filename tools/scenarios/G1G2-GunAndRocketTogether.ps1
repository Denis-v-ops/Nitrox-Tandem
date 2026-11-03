<#
.SYNOPSIS
    COOP_FEATURES_SPEC G1 and G2: the gun is disabled together (both players use the terminal, both cinematics start at
    the same moment, both end with the gun disabled), and the rocket launches together (both press launch in the
    cockpit, both games launch at the same moment; a player who isn't aboard isn't left behind by the other's click).

.DESCRIPTION
    G1 (as in S3, but both players use the terminal):
    1. Both at the gun terminal, Alice gets the cure goal (synced to Bob, both terminals accept it).
    2. Alice enters the terminal area and clicks: held (no cinematic, gun active on both, the server waits for Bob).
       She leaves the area: her click is withdrawn (the server no longer waits). She comes back and clicks again.
    3. Bob enters the area and clicks: both cinematics start within 2 s of each other; both end with gunDisabled and
       the gun goal.
    G2:
    4. Alice spawns a rocket platform (spawn rocketbase, synced as a vehicle) and builds the four stages through the
       real RocketConstructor.StartRocketConstruction (nocost, the synced path); Bob's game follows through the
       rocket's metadata. forcerocketready on both games (the preflight checks aren't driven). The cockpit's position
       relative to the rocket is printed (StoryGateRules.RocketCockpitOffset).
    5. Alice in the cockpit presses launch (LaunchRocket.OnHandClick) while Bob stands on the platform below: held
       on both games (launchStarted false), the server waits for Bob.
    6. Bob comes aboard and presses launch: both games start the launch within 2 s.

    Passed windowless 2026-11-03 (29 checks; both cinematics and both launches 0.2-0.3 s after the second click).
    Negative control the same day, with the two PrecursorDisableGunTerminal patches, the LaunchRocket Prefix and the
    server's RocketLaunchProcessor as on coop: 6 checks fail (Alice's cinematic and her launch start alone, the server
    waits for nobody). The final "both started" checks pass there too (each player's own click runs the game's code).

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build
    (dual-client.ps1 -NoBuild is used, so build first). Stop the games soon after: the launch ends in the credits.
#>
param([switch]$NoLaunch, [switch]$Keep, [switch]$SkipGun, [switch]$SkipRocket)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$terminal = 'find:PrecursorDisableGunTerminal'
# GotoConsoleCommand location "gunterminal" (TeleportCommandData asset, read through the bridge), as in S3.
$terminalRoom = '372.9 -72.7 1078.4'
$outsideTerminalArea = '370 -72.7 1076'
# Open water at the surface of the Safe Shallows, for the rocket.
$rocketSpot = @(-60.0, 3.0, 380.0)

function Get-GateStatus { (Invoke-Bridge 'server' 'storygate status') -join "`n" }

function Get-CinematicCount { param([string]$Player) [int](Invoke-Bridge $Player 'get PlayerCinematicController.cinematicModeCount') }

function Test-Goal {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call Story.StoryGoalManager.main.IsGoalComplete $Key") -eq 'true'
}

function Move-To {
    param([string]$Player, [double[]]$Spot)
    Invoke-Bridge $Player ("teleport {0} {1} {2}" -f $Spot[0], $Spot[1], $Spot[2]) | Out-Null
}

<# Waits until the check is true on both games; returns the seconds after $Since at which each first was. #>
function Wait-Both {
    param([scriptblock]$Check, [datetime]$Since, [int]$TimeoutSeconds = 10)
    $seen = @{}
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($seen.Count -lt 2 -and (Get-Date) -lt $deadline) {
        foreach ($player in $A, $B) {
            if (-not $seen.ContainsKey($player)) {
                $ok = try { & $Check $player } catch { $false }
                if ($ok) { $seen[$player] = ((Get-Date) - $Since).TotalSeconds }
            }
        }
        Start-Sleep -Milliseconds 200
    }
    $seen
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch

    if (-not $SkipGun) {
        Step 'G1: both at the gun terminal, Alice is cured (synced to Bob)' {
            Invoke-Bridge $A "teleport $terminalRoom" | Out-Null
            Invoke-Bridge $B "teleport $outsideTerminalArea" | Out-Null
            $found = Wait-Until { (Invoke-BridgeJson $A 'find PrecursorDisableGunTerminal') -and (Invoke-BridgeJson $B 'find PrecursorDisableGunTerminal') } -TimeoutSeconds 60
            Assert-True ([bool]$found) 'the terminal is loaded in both games'
            $script:cureGoal = (Invoke-Bridge $A "get $terminal PrecursorDisableGunTerminal.onPlayerCuredGoal.key").Trim('"')
            $script:gunGoal = (Invoke-Bridge $A "get $terminal PrecursorDisableGunTerminal.gunDeactivate.key").Trim('"')
            Invoke-Bridge $A "console goal Story $cureGoal" | Out-Null
            $cured = Wait-Until { (Invoke-Bridge $B "get $terminal PrecursorDisableGunTerminal.playerCured") -eq 'true' -and (Invoke-Bridge $A "get $terminal PrecursorDisableGunTerminal.playerCured") -eq 'true' } -TimeoutSeconds 15
            Assert-True ([bool]$cured) 'both terminals accept the cure'
            $area = (Invoke-BridgeJson $A 'find PrecursorDisableGunTerminalArea')[0]
            $script:areaSpot = @([double]$area.position[0], ([double]$area.position[1] - 0.8), [double]$area.position[2])
            Write-Host "  terminal area at $(Format-Vector ([double[]]$area.position))"
        }

        Step 'G1: Alice clicks alone: held; she walks away and her click is withdrawn' {
            Move-To $A $script:areaSpot
            $opened = Wait-Until { (Invoke-Bridge $A "get $terminal PrecursorDisableGunTerminal.opened") -eq 'true' } -TimeoutSeconds 10
            Assert-True ([bool]$opened) "$A's terminal opened"
            Invoke-Bridge $A "use $terminal PrecursorDisableGunTerminal" | Out-Null
            Start-Sleep -Seconds 3
            Assert-Equal 0 (Get-CinematicCount $A) "$A's cinematic didn't start"
            Assert-Equal 'false' (Invoke-Bridge $A 'get StoryGoalCustomEventHandler.main.gunDisabled') "the gun is still active on $A's game"
            $status = Get-GateStatus
            Write-Host "  $($status -replace "`n", "`n  ")"
            Assert-True ($status -match 'DISABLE_GUN[^\n]*WAITING[^\n]*waiting for Bob') 'the server waits for Bob at the terminal'
            Invoke-Bridge $A "teleport $outsideTerminalArea" | Out-Null
            $withdrawn = Wait-Until { -not ((Get-GateStatus) -match 'DISABLE_GUN[^\n]*WAITING') } -TimeoutSeconds 10
            Assert-True ([bool]$withdrawn) "leaving the terminal area withdrew $A's click"
            Move-To $A $script:areaSpot
            $opened = Wait-Until { (Invoke-Bridge $A "get $terminal PrecursorDisableGunTerminal.opened") -eq 'true' } -TimeoutSeconds 10
            Assert-True ([bool]$opened) "$A's terminal opened again"
            Invoke-Bridge $A "use $terminal PrecursorDisableGunTerminal" | Out-Null
            $waiting = Wait-Until { (Get-GateStatus) -match 'DISABLE_GUN[^\n]*WAITING[^\n]*waiting for Bob' } -TimeoutSeconds 10
            Assert-True ([bool]$waiting) "$A's second click waits for Bob"
            Assert-Equal 0 (Get-CinematicCount $A) "$A's cinematic still didn't start"
        }

        Step 'G1: Bob clicks too: both cinematics start together, both disable the gun' {
            Move-To $B $script:areaSpot
            $opened = Wait-Until { (Invoke-Bridge $B "get $terminal PrecursorDisableGunTerminal.opened") -eq 'true' } -TimeoutSeconds 10
            Assert-True ([bool]$opened) "$B's terminal opened (the gun is still active on his game)"
            $clicked = Get-Date
            Invoke-Bridge $B "use $terminal PrecursorDisableGunTerminal" | Out-Null
            $started = Wait-Both -Since $clicked -Check { param($p) (Get-CinematicCount $p) -gt 0 }
            Write-Host ("  cinematic started: Alice {0:0.0} s, Bob {1:0.0} s after Bob's click" -f $started[$A], $started[$B])
            Assert-True ($started.ContainsKey($A)) "$A's cinematic started"
            Assert-True ($started.ContainsKey($B)) "$B's cinematic started"
            if ($started.Count -eq 2) { Assert-True ([math]::Abs($started[$A] - $started[$B]) -lt 2) 'both started within 2 s' }
            $done = Wait-Until { (Invoke-Bridge $A 'get StoryGoalCustomEventHandler.main.gunDisabled') -eq 'true' -and (Invoke-Bridge $B 'get StoryGoalCustomEventHandler.main.gunDisabled') -eq 'true' -and (Get-CinematicCount $A) -eq 0 -and (Get-CinematicCount $B) -eq 0 } -TimeoutSeconds 40
            Assert-True ([bool]$done) 'both cinematics ended with the gun disabled on both games'
            Assert-True ((Test-Goal $A $script:gunGoal) -and (Test-Goal $B $script:gunGoal)) "both have the $script:gunGoal goal"
        }
    }

    if (-not $SkipRocket) {
        Step 'G2: Alice spawns a rocket platform and builds the four stages' {
            foreach ($player in $A, $B) { Move-To $player $rocketSpot }
            Invoke-Bridge $A 'look 0 0' | Out-Null
            Start-Sleep -Seconds 2
            Invoke-Bridge $A 'console nocost' | Out-Null
            Invoke-Bridge $A 'console spawn rocketbase 1 25' | Out-Null
            $rocket = Wait-Until { (Invoke-BridgeJson $A 'find Rocket') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' } | Select-Object -First 1 } -TimeoutSeconds 30
            if (-not $rocket) { throw 'the rocket platform did not spawn' }
            $script:rocketId = $rocket.id
            $seen = Wait-Until { (Invoke-BridgeJson $B 'find Rocket') | Where-Object { $_.id -eq $script:rocketId } } -TimeoutSeconds 30
            Assert-True ([bool]$seen) "$B has the rocket $script:rocketId"
            # Shorter stage construction (VFXConstructing takes Rocket.timeToConstruct) on the builder's game.
            Invoke-Bridge $A "set $script:rocketId Rocket.timeToConstruct 2" | Out-Null
            $platformDone = Wait-Until { [int](Invoke-Bridge $A "get $script:rocketId Rocket.currentRocketStage") -ge 1 -and (Invoke-Bridge $A "call $script:rocketId Rocket.IsRocketReady") -eq 'true' } -TimeoutSeconds 90
            Assert-True ([bool]$platformDone) "the platform finished on $A's game"
            for ($stage = 1; $stage -lt 5; $stage++) {
                $ready = Wait-Until { (Invoke-Bridge $A "call $script:rocketId Rocket.IsRocketReady") -eq 'true' -and (Invoke-Bridge $A "get find:RocketConstructor RocketConstructor.crafterLogic.inProgress" -AllowError) -eq 'false' } -TimeoutSeconds 60
                Invoke-Bridge $A "call find:RocketConstructor RocketConstructor.StartRocketConstruction" | Out-Null
                $next = $stage + 1
                $built = Wait-Until { [int](Invoke-Bridge $A "get $script:rocketId Rocket.currentRocketStage") -ge $next } -TimeoutSeconds 30
                Write-Host "  stage $next on Alice's game: $([bool]$built)"
            }
            $finished = Wait-Until { (Invoke-Bridge $A "call $script:rocketId Rocket.IsFinished") -eq 'true' -and (Invoke-Bridge $B "call $script:rocketId Rocket.IsFinished") -eq 'true' } -TimeoutSeconds 90
            Assert-True ([bool]$finished) 'the rocket is finished on both games'
            $launch = Wait-Until { (Invoke-BridgeJson $A 'find LaunchRocket') -and (Invoke-BridgeJson $B 'find LaunchRocket') } -TimeoutSeconds 60
            Assert-True ([bool]$launch) 'both games have the launch lever'
            foreach ($player in $A, $B) { Invoke-Bridge $player 'console forcerocketready' | Out-Null }
        }

        Step 'G2: where the cockpit is (StoryGateRules.RocketCockpitOffset)' {
            $rocket = (Invoke-BridgeJson $A 'find Rocket') | Where-Object { $_.id -eq $script:rocketId } | Select-Object -First 1
            $script:atmosphere = (Invoke-BridgeJson $A 'find AtmosphereVolume 200') | Where-Object { $_.name -eq 'AtmosphereVolume' -and "$($_.id)" -match [regex]::Escape($script:rocketId) } | Select-Object -First 1
            $lever = (Invoke-BridgeJson $A 'find LaunchRocket')[0]
            $rotation = Invoke-Bridge $A "get $script:rocketId Rocket.transform.eulerAngles" -AllowError
            $server = (Invoke-Bridge 'server' "query $script:rocketId") -join "`n"
            Write-Host "  rocket at $(Format-Vector ([double[]]$rocket.position)), rotation $rotation"
            Write-Host "  cockpit AtmosphereVolume at $(Format-Vector ([double[]]$script:atmosphere.position)), lever at $(Format-Vector ([double[]]$lever.position))"
            Write-Host ("  offset {0}" -f (Format-Vector @(([double]$script:atmosphere.position[0] - $rocket.position[0]), ([double]$script:atmosphere.position[1] - $rocket.position[1]), ([double]$script:atmosphere.position[2] - $rocket.position[2]))))
            Write-Host "  server: $([regex]::Match($server, 'Position: \[[^\]]*\]').Value)"
            Assert-True ([bool]$script:atmosphere) 'the cockpit''s AtmosphereVolume was found'
        }

        Step 'G2: Alice presses launch aboard while Bob is below: held on both games' {
            $c = [double[]]$script:atmosphere.position
            $script:cockpit = @($c[0], $c[1], $c[2])
            Move-To $A $script:cockpit
            $rocket = (Invoke-BridgeJson $A 'find Rocket') | Where-Object { $_.id -eq $script:rocketId } | Select-Object -First 1
            Move-To $B @([double]$rocket.position[0], ([double]$rocket.position[1] + 4), [double]$rocket.position[2])
            Start-Sleep -Seconds 3
            Write-Host "  Alice: $(Invoke-Bridge $A 'call Player.main.IsUnderwater') underwater, Bob at $(Format-Vector ([double[]](Invoke-BridgeJson $B 'state').position))"
            Invoke-Bridge $A 'use find:LaunchRocket LaunchRocket' | Out-Null
            Start-Sleep -Seconds 3
            foreach ($player in $A, $B) { Assert-Equal 'false' (Invoke-Bridge $player 'get LaunchRocket.launchStarted') "the launch didn't start on $player's game" }
            $status = Get-GateStatus
            Write-Host "  $($status -replace "`n", "`n  ")"
            Assert-True ($status -match 'ROCKET_LAUNCH[^\n]*rocket cockpit[^\n]*WAITING[^\n]*waiting for Bob') 'the server waits for Bob in the rocket cockpit'
        }

        Step 'G2: Bob comes aboard and presses launch: both games launch together' {
            Move-To $B @(($script:cockpit[0] + 0.5), $script:cockpit[1], ($script:cockpit[2] + 0.5))
            Start-Sleep -Seconds 2
            $clicked = Get-Date
            Invoke-Bridge $B 'use find:LaunchRocket LaunchRocket' | Out-Null
            $started = Wait-Both -Since $clicked -Check { param($p) (Invoke-Bridge $p 'get LaunchRocket.launchStarted') -eq 'true' }
            Write-Host ("  launch started: Alice {0:0.0} s, Bob {1:0.0} s after Bob's click" -f $started[$A], $started[$B])
            Assert-True ($started.ContainsKey($A)) "$A's launch started"
            Assert-True ($started.ContainsKey($B)) "$B's launch started"
            if ($started.Count -eq 2) { Assert-True ([math]::Abs($started[$A] - $started[$B]) -lt 2) 'both started within 2 s' }
            foreach ($player in $A, $B) { Assert-True ((Get-CinematicCount $player) -gt 0) "$player is in the launch cinematic" }
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
    Write-Host $_.ScriptStackTrace
}
Complete-Scenario -Name 'G1G2 GunAndRocketTogether' -Keep:$Keep
