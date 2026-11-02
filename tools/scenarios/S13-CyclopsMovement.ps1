<#
.SYNOPSIS
    FIX_SPEC S13: Cyclops movement and sync between two players, measured. One player pilots, the other rides along,
    then they swap; both games record the sub and both players every frame and the tracks are compared by wall clock.

.DESCRIPTION
    Bob spawns a Cyclops (he holds its transient lock), both board through the entrance hatch, and Alice takes the helm
    (PilotingChair.OnHandClick -> exclusive lock). The pilot drives with the game's own movement input (bridge
    "input move", read by SubControl.Update in DirectInput mode): straight, then turning. Meanwhile Bob stands, then
    walks around inside. Alice leaves the helm with her forward input still held (#2379), the sub coasts, Bob takes
    the helm (handover) and drives, Alice rides, Bob leaves the helm and the sub coasts again.

    Measured (S13Helpers.ps1): for the non-simulating game, the time shift between its track of the sub and the
    simulating game's (lag), what's left after the shift (residual) and per-frame stutter (stall/jump/reverse frames);
    the replicated Cyclops' rigidbody velocity; passengers' local positions in the sub (stay put while standing,
    stay inside the hull while walking, partner's view matches); the pilot's body during the exit animation.
    Raw recordings are saved to -OutDir.

    Starts a fresh world (save s13-cyclops) and two games (opens windows), unless -NoLaunch. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's13-cyclops',
    # Open water over the Grassy Plateaus (as S6), raised: the sub's top must stay under water to have thrust.
    [double[]]$Area = @(263, -40, 64),
    [string]$OutDir = (Join-Path $env:TEMP 's13-recordings'),
    # Thresholds (metres / count) for the asserts.
    [double]$MaxResidual = 0.5, [double]$MaxStandingDrift = 0.3, [double]$MaxPartnerViewError = 1.0, [double]$MaxStallShare = 0.1,
    [double]$MaxCoastDesync = 0.5,
    # Leave out the docked-Seamoth part (it needs a few docking attempts).
    [switch]$SkipDocking)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'S13Helpers.ps1')
$A = 'Alice'
$B = 'Bob'
$inv = [Globalization.CultureInfo]::InvariantCulture
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Format-Position {
    param([double[]]$P)
    ($P | ForEach-Object { $_.ToString('0.###', $inv) }) -join ' '
}

function Get-State {
    param([string]$Player)
    Invoke-BridgeJson $Player 'state'
}

function Now-Ms { [DateTime]::UtcNow.Ticks / 10000.0 }

function Test-InCyclops {
    param($State)
    $State.currentSub -and $State.currentSub.id -eq $script:cyclopsId
}

function Wait-NoCinematic {
    param([string]$Player)
    Wait-Until { [int](Invoke-Bridge $Player 'get PlayerCinematicController.cinematicModeCount') -eq 0 -and (Invoke-Bridge $Player 'get player Player.cinematicModeActive') -eq 'false' } -TimeoutSeconds 20 -IntervalSeconds 0.3
}

function Get-HatchPosition {
    param([string]$Player, [string]$Name)
    $hatch = (Invoke-BridgeJson $Player 'find CinematicModeTrigger 40') | Where-Object { $_.name -eq $Name -and $_.id -eq "(in $script:cyclopsId)" } | Select-Object -First 1
    if (-not $hatch) { throw "$Player doesn't see the Cyclops' $Name" }
    [double[]]$hatch.position
}

function Enter-Cyclops {
    param([string]$Player)
    $hatch = Get-HatchPosition $Player 'EntranceHatch'
    Invoke-Bridge $Player ('teleport ' + (Format-Position @($hatch[0], ($hatch[1] - 6.9), $hatch[2]))) | Out-Null
    Start-Sleep -Milliseconds 500
    Invoke-Bridge $Player 'use find:CinematicModeTrigger#EntranceHatch' | Out-Null
    $inside = Wait-Until { Test-InCyclops (Get-State $Player) } -TimeoutSeconds 15 -IntervalSeconds 0.3
    Wait-NoCinematic $Player | Out-Null
    [bool]$inside
}

function Exit-Cyclops {
    param([string]$Player)
    Invoke-Bridge $Player 'use find:CinematicModeTrigger#InternalDiveHatch' | Out-Null
    $outside = Wait-Until { -not (Get-State $Player).currentSub } -TimeoutSeconds 15 -IntervalSeconds 0.3
    Wait-NoCinematic $Player | Out-Null
    [bool]$outside
}

function Format-Vector3Arg {
    param([double[]]$P)
    ($P | ForEach-Object { $_.ToString('0.###', $inv) }) -join ','
}

function Get-DockingBay {
    # The Cyclops' bay has its own NitroxId; the VirtualCyclops copy has none.
    $bay = (Invoke-BridgeJson $A 'find VehicleDockingBay 5') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' } | Select-Object -First 1
    if (-not $bay) { throw 'no docking bay found' }
    $bay
}

<#
    As S6: Alice spawns a Seamoth beside the Cyclops, pilots it under the bay and into its trigger while the doors are
    open (VehicleDockingBay.OnTriggerEnter). Returns the Seamoth's id once she is docked and in the sub, or $null.
#>
function Invoke-DockSeamoth {
    $p = [double[]](Get-DockingBay).position
    Invoke-Bridge $A ('teleport ' + (Format-Position @(($p[0] + 20), ($p[1] - 8), $p[2]))) | Out-Null
    Invoke-Bridge $A 'look 270 0' | Out-Null
    Start-Sleep -Seconds 1
    $before = @((Invoke-BridgeJson $A 'find SeaMoth 20') | ForEach-Object { $_.id })
    Invoke-Bridge $A 'console spawn seamoth' | Out-Null
    $seamoth = Wait-Until { (Invoke-BridgeJson $A 'find SeaMoth 20') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
    if (-not $seamoth) { Write-Host '  the Seamoth did not spawn'; return $null }
    $id = $seamoth.id
    Write-Host "  Seamoth $id"
    Start-Sleep -Seconds 2
    Invoke-Bridge $A "use $id SeaMoth" | Out-Null
    if (-not (Wait-Until { (Get-State $A).vehicle } -TimeoutSeconds 10 -IntervalSeconds 0.3)) { Write-Host '  Alice could not enter the Seamoth'; return $null }
    $bayId = (Get-DockingBay).id
    for ($try = 1; $try -le 3; $try++) {
        $p = [double[]](Get-DockingBay).position
        Invoke-Bridge $A ("set $id Transform.position " + (Format-Vector3Arg @($p[0], ($p[1] - 25), $p[2]))) | Out-Null
        Start-Sleep -Seconds 2
        $p = [double[]](Get-DockingBay).position
        Invoke-Bridge $A ("set $id Transform.position " + (Format-Vector3Arg @($p[0], ($p[1] - 8), $p[2]))) | Out-Null
        Wait-Until { (Invoke-Bridge $A "get $bayId VehicleDockingBay.nearbyVehicle") -ne 'null' } -TimeoutSeconds 8 | Out-Null
        Start-Sleep -Seconds 3
        $p = [double[]](Get-DockingBay).position
        Invoke-Bridge $A ("set $id Transform.position " + (Format-Vector3Arg @($p[0], ($p[1] - 0.4), $p[2]))) | Out-Null
        $docked = Wait-Until { $s = Get-State $A; (Test-InCyclops $s) -and -not $s.vehicle } -TimeoutSeconds 15 -IntervalSeconds 0.5
        if ($docked) {
            Wait-NoCinematic $A | Out-Null
            Start-Sleep -Seconds 3
            return $id
        }
        Write-Host "  docking try $try didn't dock"
    }
    $null
}

function Get-Mode {
    param([string]$Player)
    (Invoke-Bridge $Player 'get player Player.mode').Trim('"')
}

function Enter-Helm {
    param([string]$Player)
    Invoke-Bridge $Player 'use find:PilotingChair' | Out-Null
    $piloting = Wait-Until { (Get-Mode $Player) -eq 'Piloting' } -TimeoutSeconds 15 -IntervalSeconds 0.3
    Wait-NoCinematic $Player | Out-Null
    [bool]$piloting
}

function Exit-Helm {
    param([string]$Player)
    Invoke-Bridge $Player 'call player Player.TryEject' | Out-Null
    $left = Wait-Until { (Get-Mode $Player) -eq 'Normal' } -TimeoutSeconds 15 -IntervalSeconds 0.3
    [bool]$left
}

function Get-LockOwner {
    if ((Invoke-Bridge 'server' "query $script:cyclopsId") -match 'Owner: (\w+)') { $Matches[1] } else { '<none>' }
}

$script:windows = [ordered]@{}
function Mark {
    param([string]$Name, [double]$From, [double]$To)
    $script:windows[$Name] = @($From, $To)
    Write-Host ("  window {0}: {1:0.0} s" -f $Name, (($To - $From) / 1000))
}

$script:recordings = @(
    @{ Player = $A; Name = 'aCyc'; Target = 'cyclops' },
    @{ Player = $A; Name = 'aSelf'; Target = 'player'; RelativeTo = 'cyclops' },
    @{ Player = $A; Name = 'aBob'; Target = 'bob'; RelativeTo = 'cyclops' },
    @{ Player = $B; Name = 'bCyc'; Target = 'cyclops' },
    @{ Player = $B; Name = 'bSelf'; Target = 'player'; RelativeTo = 'cyclops' },
    @{ Player = $B; Name = 'bAlice'; Target = 'alice'; RelativeTo = 'cyclops' }
)

function Resolve-RecordingTarget {
    param([string]$Token)
    switch ($Token) {
        'cyclops' { $script:cyclopsId }
        'bob' { $script:bobId }
        'alice' { $script:aliceId }
        'seamoth' { $script:seamothId }
        default { $Token }
    }
}

function Start-AllRecordings {
    param([string]$Suffix)
    foreach ($r in $script:recordings) {
        $relativeTo = if ($r.RelativeTo) { Resolve-RecordingTarget $r.RelativeTo } else { $null }
        Start-Recording $r.Player "$($r.Name)$Suffix" (Resolve-RecordingTarget $r.Target) $relativeTo
    }
}

function Stop-AllRecordings {
    param([string]$Suffix)
    $tracks = @{}
    foreach ($r in $script:recordings) {
        $name = "$($r.Name)$Suffix"
        $tracks[$r.Name] = Stop-Recording $r.Player $name -SaveTo (Join-Path $OutDir "$name.json")
        Write-Host "  $name : $($tracks[$r.Name].Count) samples"
    }
    $tracks
}

function Show-Compare {
    param([string]$Label, [S13Track]$Owner, [S13Track]$Observer, [string]$Window)
    $w = $script:windows[$Window]
    $result = [S13Analysis]::Compare($Owner, $Observer, $w[0], $w[1], 1500)
    Write-Host "  $Label [$Window]: $(Format-Result $result)"
    Write-Host "    owner frames: $(Format-Result ([S13Analysis]::Frames($Owner, $w[0], $w[1]))) | own smoothness: $(Format-Result ([S13Analysis]::Smoothness($Owner, 'px', $w[0], $w[1])))"
    Write-Host "    observer frames: $(Format-Result ([S13Analysis]::Frames($Observer, $w[0], $w[1]))) | smoothness: $(Format-Result ([S13Analysis]::Smoothness($Observer, 'px', $w[0], $w[1])))"
    Write-Host "    observer rigidbody speed: $(Format-Result ([S13Analysis]::Stats($Observer, 'vx', $w[0], $w[1], $true))) angular: $(Format-Result ([S13Analysis]::Stats($Observer, 'wx', $w[0], $w[1], $true))) maxLat: $(Format-Result ([S13Analysis]::Stats($Observer, 'maxLat', $w[0], $w[1], $false)))"
    $result
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    $script:aliceId = (Get-State $A).playerId
    $script:bobId = (Get-State $B).playerId

    Step 'Bob spawns a Cyclops; both games see it' {
        Invoke-Bridge $B ('teleport ' + (Format-Position $Area)) | Out-Null
        Invoke-Bridge $A ('teleport ' + (Format-Position @(($Area[0] - 5), $Area[1], ($Area[2] - 4)))) | Out-Null
        Invoke-Bridge $B 'look 0 0' | Out-Null
        Start-Sleep -Seconds 3
        $before = @((Invoke-BridgeJson $B 'find SubRoot 20') | ForEach-Object { $_.id })
        Invoke-Bridge $B 'console sub cyclops' | Out-Null
        $cyclops = Wait-Until { (Invoke-BridgeJson $B 'find SubRoot 20') | Where-Object { $_.name -like 'Cyclops*' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 30
        Assert-True ([bool]$cyclops) 'the Cyclops spawned'
        $script:cyclopsId = $cyclops.id
        Write-Host "  Cyclops $cyclopsId"
        $seen = Wait-Until { (Invoke-BridgeJson $A 'find SubRoot 20') | Where-Object { $_.id -eq $cyclopsId } } -TimeoutSeconds 30
        Assert-True ([bool]$seen) "$A sees the Cyclops"
        Start-Sleep -Seconds 3
        Assert-Equal $B (Get-LockOwner) 'Bob (the spawner) simulates the Cyclops'
    }

    Step 'Both board, Alice takes the helm' {
        Assert-True (Enter-Cyclops $B) "$B is in the Cyclops"
        Assert-True (Enter-Cyclops $A) "$A is in the Cyclops"
        Assert-True (Enter-Helm $A) "$A is piloting"
        Assert-Equal $A (Get-LockOwner) 'the server gave Alice the Cyclops'
        foreach ($player in $A, $B) {
            $engine = Invoke-Bridge $player "get $cyclopsId SubControl.canAccel"
            Write-Host "  $player : engine canAccel=$engine, replicator=$(Invoke-Bridge $player "get $cyclopsId MovementReplicator" -AllowError)"
        }
    }

    Start-AllRecordings
    $t0 = Now-Ms

    Step 'Alice drives straight, then turns; Bob stands' {
        Invoke-Bridge $A 'input move 0 0 1' | Out-Null
        Start-Sleep -Seconds 4
        $from = Now-Ms
        Start-Sleep -Seconds 10
        Mark 'driveStraightA' $from (Now-Ms)
        Invoke-Bridge $A 'input move 1 0 1' | Out-Null
        Start-Sleep -Seconds 2
        $from = Now-Ms
        Start-Sleep -Seconds 8
        Mark 'driveTurnA' $from (Now-Ms)
        Invoke-Bridge $A 'input move 0 0 1' | Out-Null
    }

    Step 'Bob walks around inside while Alice drives' {
        $from = Now-Ms
        foreach ($yaw in 0, 90, 180, 270, 45) {
            Invoke-Bridge $B "look $yaw 0" | Out-Null
            Invoke-Bridge $B 'input move 0 0 1' | Out-Null
            Start-Sleep -Seconds 3
        }
        Invoke-Bridge $B 'input off' | Out-Null
        Mark 'walkB' $from (Now-Ms)
    }

    Step 'Alice leaves the helm with forward held (#2379); the sub coasts' {
        $from = Now-Ms
        Assert-True (Exit-Helm $A) "$A left the helm"
        Start-Sleep -Seconds 3
        Invoke-Bridge $A 'input off' | Out-Null
        Mark 'exitHelmA' $from (Now-Ms)
        Assert-Equal $A (Get-LockOwner) 'Alice keeps simulating after leaving the helm (transient)'
        $from = Now-Ms
        Start-Sleep -Seconds 10
        Mark 'coastA' $from (Now-Ms)
    }

    Step 'Bob takes the helm (handover) and drives; Alice rides' {
        Assert-True (Enter-Helm $B) "$B is piloting"
        Assert-Equal $B (Get-LockOwner) 'the server gave Bob the Cyclops'
        Invoke-Bridge $B 'input move 0 0 1' | Out-Null
        Start-Sleep -Seconds 4
        $from = Now-Ms
        Start-Sleep -Seconds 10
        Mark 'driveStraightB' $from (Now-Ms)
        Invoke-Bridge $B 'input move -1 0 1' | Out-Null
        Start-Sleep -Seconds 2
        $from = Now-Ms
        Start-Sleep -Seconds 8
        Mark 'driveTurnB' $from (Now-Ms)
        Invoke-Bridge $B 'input off' | Out-Null
        Assert-True (Exit-Helm $B) "$B left the helm"
        $from = Now-Ms
        Start-Sleep -Seconds 10
        Mark 'coastB' $from (Now-Ms)
    }

    if (-not $SkipDocking) {
        try {
            Step 'Alice docks a Seamoth in the Cyclops Bob simulates' {
                Assert-True (Exit-Cyclops $A) "$A is out through the dive hatch"
                $script:seamothId = Invoke-DockSeamoth
                Assert-True ([bool]$script:seamothId) "$A docked a Seamoth"
            }
        }
        catch {
            # Already recorded as a failure; still analyse what was recorded.
            $script:seamothId = $null
        }
        if ($script:seamothId) {
            $script:recordings += @(
                @{ Player = $A; Name = 'aMoth'; Target = 'seamoth'; RelativeTo = 'cyclops' },
                @{ Player = $B; Name = 'bMoth'; Target = 'seamoth'; RelativeTo = 'cyclops' }
            )
            Start-Recording $A 'aMoth' $script:seamothId $script:cyclopsId
            Start-Recording $B 'bMoth' $script:seamothId $script:cyclopsId
            Step 'The sub rests with the Seamoth docked, then Bob drives' {
                $from = Now-Ms
                Start-Sleep -Seconds 10
                Mark 'restDocked' $from (Now-Ms)
                Assert-True (Enter-Helm $B) "$B is piloting"
                Invoke-Bridge $B 'input move 0 0 1' | Out-Null
                Start-Sleep -Seconds 3
                $from = Now-Ms
                Start-Sleep -Seconds 6
                Invoke-Bridge $B 'input move 1 0 1' | Out-Null
                Start-Sleep -Seconds 6
                Mark 'driveDocked' $from (Now-Ms)
                Invoke-Bridge $B 'input off' | Out-Null
                Assert-True (Exit-Helm $B) "$B left the helm"
            }
        }
    }

    $tracks = $null
    Step 'Analysis' {
        $tracks = Stop-AllRecordings
        $script:tracks = $tracks
        foreach ($t in $tracks.Values) { $t.TimeField = 'ms' }
        Write-Host "  clocks: Alice $(Format-Result ([S13Analysis]::Clock($tracks.aCyc))) | Bob $(Format-Result ([S13Analysis]::Clock($tracks.bCyc)))"
        foreach ($name in 'aCyc', 'bCyc', 'aSelf', 'bSelf') { Show-Events $tracks[$name] }

        $results = @{}
        $results.straightA = Show-Compare 'Bob sees Alice drive' $tracks.aCyc $tracks.bCyc 'driveStraightA'
        $results.turnA = Show-Compare 'Bob sees Alice turn' $tracks.aCyc $tracks.bCyc 'driveTurnA'
        $results.coastA = Show-Compare 'Bob sees the sub coast (Alice simulates)' $tracks.aCyc $tracks.bCyc 'coastA'
        $results.straightB = Show-Compare 'Alice sees Bob drive' $tracks.bCyc $tracks.aCyc 'driveStraightB'
        $results.turnB = Show-Compare 'Alice sees Bob turn' $tracks.bCyc $tracks.aCyc 'driveTurnB'
        $results.coastB = Show-Compare 'Alice sees the sub coast (Bob simulates)' $tracks.bCyc $tracks.aCyc 'coastB'

        foreach ($key in 'straightA', 'turnA', 'straightB', 'turnB') {
            $r = $results[$key]
            Assert-True ($r.residualRms -le $MaxResidual) "$key : replicated sub follows the pilot's path (residual RMS $($r.residualRms) m <= $MaxResidual)"
            $stallShare = if ($r.movingFrames -gt 0) { $r.stallFrames / $r.movingFrames } else { 0 }
            Assert-True ($stallShare -le $MaxStallShare) "$key : stalled frames $($r.stallFrames) of $($r.movingFrames) (<= $($MaxStallShare * 100) %)"
            Assert-Equal 0 $r.reverseFrames "$key : the replicated sub never steps backwards"
        }
        foreach ($key in 'coastA', 'coastB') {
            Assert-True ($results[$key].instantMax -le $MaxCoastDesync) "$key : both games agree where the coasting sub is (max $($results[$key].instantMax) m <= $MaxCoastDesync)"
        }

        foreach ($w in 'driveStraightA', 'driveTurnA') {
            $win = $script:windows[$w]
            $drift = [S13Analysis]::Drift($tracks.bSelf, 'lx', $win[0], $win[1])
            Write-Host "  Bob standing [$w], his own game, local position: $(Format-Result $drift)"
            Assert-True ($drift.maxDistFromFirst -le $MaxStandingDrift) "Bob stays where he stands while Alice drives [$w] ($($drift.maxDistFromFirst) m)"
            $view = [S13Analysis]::CompareField($tracks.bSelf, 'lx', $tracks.aBob, 'lx', $win[0], $win[1], 1000)
            Write-Host "  Bob as Alice sees him [$w]: $(Format-Result $view)"
            Assert-True ($view.max -le $MaxPartnerViewError) "Alice sees Bob where he is in the sub [$w] ($($view.max) m)"
        }
        foreach ($w in 'driveStraightB', 'driveTurnB') {
            $win = $script:windows[$w]
            $drift = [S13Analysis]::Drift($tracks.aSelf, 'lx', $win[0], $win[1])
            Write-Host "  Alice standing [$w], her own game: $(Format-Result $drift)"
            Assert-True ($drift.maxDistFromFirst -le $MaxStandingDrift) "Alice stays where she stands while Bob drives [$w] ($($drift.maxDistFromFirst) m)"
            $view = [S13Analysis]::CompareField($tracks.aSelf, 'lx', $tracks.bAlice, 'lx', $win[0], $win[1], 1000)
            Write-Host "  Alice as Bob sees her [$w]: $(Format-Result $view)"
            Assert-True ($view.max -le $MaxPartnerViewError) "Bob sees Alice where she is in the sub [$w] ($($view.max) m)"
        }

        $win = $script:windows['walkB']
        $walk = [S13Analysis]::Drift($tracks.bSelf, 'lx', $win[0], $win[1])
        Write-Host "  Bob walking, his own game: $(Format-Result $walk)"
        Write-Host "  Bob walking, pawn handle: $(Format-Result ([S13Analysis]::Drift($tracks.bSelf, 'hx', $win[0], $win[1])))"
        $view = [S13Analysis]::CompareField($tracks.bSelf, 'lx', $tracks.aBob, 'lx', $win[0], $win[1], 1000)
        Write-Host "  Bob walking as Alice sees him: $(Format-Result $view)"
        $win = $script:windows['exitHelmA']
        Write-Host "  Alice leaving the helm, her own game: $(Format-Result ([S13Analysis]::Drift($tracks.aSelf, 'lx', $win[0], $win[1])))"
        Write-Host "  Alice leaving the helm, as Bob sees her: $(Format-Result ([S13Analysis]::Drift($tracks.bAlice, 'lx', $win[0], $win[1])))"
        $view = [S13Analysis]::CompareField($tracks.aSelf, 'lx', $tracks.bAlice, 'lx', $win[0], $win[1], 1000)
        Write-Host "  Alice leaving the helm, partner view vs own: $(Format-Result $view)"

        if ($tracks.aMoth) {
            foreach ($w in 'restDocked', 'driveDocked') {
                $win = $script:windows[$w]
                if (-not $win) { continue }
                Write-Host "  [$w] Cyclops in Bob's game (simulating): $(Format-Result ([S13Analysis]::Drift($tracks.bCyc, 'px', $win[0], $win[1]))) speed $(Format-Result ([S13Analysis]::Stats($tracks.bCyc, 'vx', $win[0], $win[1], $true)))"
                foreach ($name in 'aMoth', 'bMoth') {
                    $drift = [S13Analysis]::Drift($tracks[$name], 'lx', $win[0], $win[1])
                    Write-Host "  [$w] docked Seamoth in the sub, $name : $(Format-Result $drift) speed $(Format-Result ([S13Analysis]::Stats($tracks[$name], 'vx', $win[0], $win[1], $true)))"
                    Assert-True ($drift.maxDistFromFirst -le $MaxStandingDrift) "[$w] the docked Seamoth stays in the bay in $name ($($drift.maxDistFromFirst) m)"
                }
                Show-Events $tracks.bMoth
            }
            $results.drivenDocked = Show-Compare 'Alice sees Bob drive with the Seamoth docked' $tracks.bCyc $tracks.aCyc 'driveDocked'
        }
    }
    Assert-NoDiff -Ids $cyclopsId 'no desync reported for the Cyclops'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
    Write-Host $_.ScriptStackTrace
}
finally {
    foreach ($player in $A, $B) { try { Invoke-Bridge $player 'input off' | Out-Null } catch { } }
}
Complete-Scenario -Name 'S13 CyclopsMovement' -Keep:$Keep
