<#
.SYNOPSIS
    FIX_SPEC S13: Cyclops movement and sync between two players, measured. One player pilots, the other rides along,
    then they swap; both games record the sub, both players and a docked Seamoth every frame, and the tracks are
    compared by wall clock (both games run on this PC).

.DESCRIPTION
    Bob spawns a Cyclops (he holds its transient lock), both board through the entrance hatch, Alice takes the helm
    (PilotingChair.OnHandClick -> exclusive lock) and starts the engine. The pilot drives with the game's own movement
    input (bridge "input move", read by SubControl.Update in DirectInput mode): straight, then turning. Bob's game gets
    one long frame (-HitchMs, Thread.Sleep on its main thread, as when cells load). Bob stands, then walks around.
    Alice leaves the helm at speed with her forward key still held (#2379) and Bob takes the helm at once (hand-over of
    a moving sub), drives, leaves the helm, and the sub coasts. Alice docks a Seamoth, the sub rests, Bob drives it.
    Then Bob quits inside the sub, Alice drives it 40+ m on, and both games restart: Bob must come back inside.

    Checked (S13Helpers.ps1): the non-simulating game follows the pilot's path (residual after the replication delay,
    stalled and backward frames); the long frame doesn't raise the replication delay; passengers stay where they
    stand, can walk without falling through, and the partner sees them there; the partner sees the pilot where she is
    while she leaves the helm; at the hand-over the sub keeps moving for the new pilot and doesn't snap back for the
    old one; the docked Seamoth stays in the bay in both games; the rejoin. Raw recordings are saved to -OutDir.

    Starts a fresh world (save s13-cyclops) and two games (opens windows), unless -NoLaunch. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's13-cyclops',
    # Over the Grassy Plateaus; at this depth there was nothing within 120 m in any direction (raycasts, 2026-11-02).
    [double[]]$Area = @(263, -60, 64),
    [string]$OutDir = (Join-Path $env:TEMP 's13-recordings'),
    # Thresholds (metres / count) for the asserts.
    [double]$MaxResidual = 0.5, [double]$MaxStandingDrift = 0.3, [double]$MaxPartnerViewError = 1.0, [double]$MaxStallShare = 0.1,
    [double]$MaxCoastDesync = 0.5, [double]$MaxLatencyAfterHitch = 0.2, [double]$MaxHandoverBackStep = 0.3,
    # The long frame given to Bob's game while he watches Alice drive.
    [int]$HitchMs = 300,
    # Leave out the docked-Seamoth part (it needs a few docking attempts) / the quit-and-restart part.
    [switch]$SkipDocking, [switch]$SkipRejoin)

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

function Get-Speed {
    param([S13Track]$Track, [double]$From, [double]$To)
    $drift = [S13Analysis]::Drift($Track, 'px', $From, $To)
    if ($drift.samples -lt 2) { return 0 }
    $t = $Track.Times
    $first = $null; $last = $null
    for ($i = 0; $i -lt $t.Length; $i++) {
        if ($t[$i] -ge $From -and $t[$i] -le $To) { if ($null -eq $first) { $first = $i }; $last = $i }
    }
    $p = $Track.Col('px')
    $d = [math]::Sqrt([math]::Pow($Track.Rows[$last][$p] - $Track.Rows[$first][$p], 2) + [math]::Pow($Track.Rows[$last][$p + 1] - $Track.Rows[$first][$p + 1], 2) + [math]::Pow($Track.Rows[$last][$p + 2] - $Track.Rows[$first][$p + 2], 2))
    $d / (($t[$last] - $t[$first]) / 1000)
}

<#
    Largest step of a track against the direction the sub was moving just before (a snap back), within [From, To].
#>
function Get-MaxBackStep {
    param([S13Track]$Track, [double]$From, [double]$To)
    $t = $Track.Times
    $p = $Track.Col('px')
    $max = 0
    for ($i = 30; $i -lt $t.Length; $i++) {
        if ($t[$i] -lt $From -or $t[$i] -gt $To) { continue }
        $r = $Track.Rows
        $dir = @(($r[$i - 1][$p] - $r[$i - 30][$p]), ($r[$i - 1][$p + 1] - $r[$i - 30][$p + 1]), ($r[$i - 1][$p + 2] - $r[$i - 30][$p + 2]))
        $len = [math]::Sqrt($dir[0] * $dir[0] + $dir[1] * $dir[1] + $dir[2] * $dir[2])
        if ($len -lt 0.01) { continue }
        $step = (($r[$i][$p] - $r[$i - 1][$p]) * $dir[0] + ($r[$i][$p + 1] - $r[$i - 1][$p + 1]) * $dir[1] + ($r[$i][$p + 2] - $r[$i - 1][$p + 2]) * $dir[2]) / $len
        if (-$step -gt $max) { $max = -$step }
    }
    $max
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
        # A spawned Cyclops' engine is off: the helm's engine button (synced as CyclopsMetadata.EngineOn), on after 5 s
        Invoke-Bridge $A 'call find:CyclopsEngineChangeState CyclopsEngineChangeState.OnClick' | Out-Null
        foreach ($player in $A, $B) {
            $on = Wait-Until { (Invoke-Bridge $player "get $cyclopsId SubControl.canAccel") -eq 'true' } -TimeoutSeconds 15 -IntervalSeconds 0.5
            Assert-True ([bool]$on) "the engine is on in $player's game"
        }
    }

    Start-AllRecordings

    Step 'Alice drives straight (Bob hitches once), then turns; Bob stands' {
        Invoke-Bridge $A 'input move 0 0 1' | Out-Null
        Start-Sleep -Seconds 3
        $from = Now-Ms
        Start-Sleep -Seconds 2
        # One long frame in the observer's game, as when cells load (Thread.Sleep on its main thread)
        Invoke-Bridge $B "call Thread.Sleep $HitchMs" | Out-Null
        $script:hitchAt = Now-Ms
        Start-Sleep -Seconds 3
        Mark 'driveStraightA' $from (Now-Ms)
        # The sub heads west and meets rising ground about 100 m on (2026-11-02), so from here on it circles
        # (input 1,0,1 turns about 7 deg/s, a circle of about 60 m) until Bob drives it back east.
        Invoke-Bridge $A 'input move 1 0 1' | Out-Null
        Start-Sleep -Seconds 2
        $from = Now-Ms
        Start-Sleep -Seconds 6
        Mark 'driveTurnA' $from (Now-Ms)
    }

    Step 'Bob walks around inside while Alice drives in circles' {
        $from = Now-Ms
        foreach ($yaw in 0, 90, 180, 270) {
            Invoke-Bridge $B "look $yaw 0" | Out-Null
            Invoke-Bridge $B 'input move 0 0 1' | Out-Null
            Start-Sleep -Seconds 3
        }
        Invoke-Bridge $B 'input off' | Out-Null
        Mark 'walkB' $from (Now-Ms)
    }

    Step 'Alice leaves the helm at speed with her keys held (#2379); Bob takes it over at once' {
        $from = Now-Ms
        Assert-True (Exit-Helm $A) "$A left the helm"
        $script:handoverFrom = Now-Ms
        Assert-True (Enter-Helm $B) "$B is piloting"
        Assert-Equal $B (Get-LockOwner) 'the server gave Bob the Cyclops'
        Invoke-Bridge $A 'input off' | Out-Null
        Mark 'exitHelmA' $from (Now-Ms)
        Start-Sleep -Seconds 3
        $script:handoverTo = Now-Ms
    }

    Step 'Bob turns, then drives straight; Alice rides' {
        Invoke-Bridge $B 'input move 1 0 1' | Out-Null
        Start-Sleep -Seconds 2
        $from = Now-Ms
        Start-Sleep -Seconds 6
        Mark 'driveTurnB' $from (Now-Ms)
        Invoke-Bridge $B 'input move 0 0 1' | Out-Null
        Start-Sleep -Seconds 3
        $from = Now-Ms
        Start-Sleep -Seconds 5
        Mark 'driveStraightB' $from (Now-Ms)
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
                Start-Sleep -Seconds 4
                Invoke-Bridge $B 'input move 1 0 1' | Out-Null
                Start-Sleep -Seconds 4
                Mark 'driveDocked' $from (Now-Ms)
                Invoke-Bridge $B 'input off' | Out-Null
                Assert-True (Exit-Helm $B) "$B left the helm"
            }
        }
    }

    Step 'Analysis' {
        $tracks = Stop-AllRecordings
        $script:tracks = $tracks
        Write-Host "  clocks: Alice $(Format-Result ([S13Analysis]::Clock($tracks.aCyc))) | Bob $(Format-Result ([S13Analysis]::Clock($tracks.bCyc)))"
        foreach ($name in 'aCyc', 'bCyc', 'aSelf', 'bSelf') { Show-Events $tracks[$name] @('lock', 'replicator', 'mode', 'cinematic', 'sub') }

        $results = @{}
        $results.straightA = Show-Compare 'Bob sees Alice drive' $tracks.aCyc $tracks.bCyc 'driveStraightA'
        $results.turnA = Show-Compare 'Bob sees Alice turn' $tracks.aCyc $tracks.bCyc 'driveTurnA'
        $results.straightB = Show-Compare 'Alice sees Bob drive' $tracks.bCyc $tracks.aCyc 'driveStraightB'
        $results.turnB = Show-Compare 'Alice sees Bob turn' $tracks.bCyc $tracks.aCyc 'driveTurnB'
        $results.coastB = Show-Compare 'Alice sees the sub coast (Bob simulates)' $tracks.bCyc $tracks.aCyc 'coastB'

        foreach ($key in 'straightA', 'turnA', 'straightB', 'turnB') {
            $r = $results[$key]
            Assert-True ($r.ownerSpeed -ge 1) "$key : the sub really moved ($($r.ownerSpeed) m/s)"
            Assert-True ($r.residualRms -le $MaxResidual) "$key : replicated sub follows the pilot's path (residual RMS $($r.residualRms) m <= $MaxResidual)"
            $stallShare = if ($r.movingFrames -gt 0) { $r.stallFrames / $r.movingFrames } else { 0 }
            Assert-True ($stallShare -le $MaxStallShare) "$key : stalled frames $($r.stallFrames) of $($r.movingFrames) (<= $($MaxStallShare * 100) %)"
            Assert-Equal 0 $r.reverseFrames "$key : the replicated sub never steps backwards"
        }

        # One long frame must not keep the partner's view of the sub further behind (it did: 0.08 -> 0.31 s for 10+ s)
        $lat = [S13Analysis]::Stats($tracks.bCyc, 'maxLat', $script:hitchAt + 500, $script:windows['driveTurnA'][1], $false)
        $frames = [S13Analysis]::Frames($tracks.bCyc, $script:hitchAt - 1000, $script:hitchAt + 1000)
        Write-Host "  Bob's replicator after his $HitchMs ms frame (longest frame $($frames.dtMaxMs) ms): MaxAllowedLatency $(Format-Result $lat)"
        Assert-True ($frames.dtMaxMs -ge $HitchMs * 0.8) "Bob's game had the long frame ($($frames.dtMaxMs) ms)"
        Assert-True ($lat.max -le $MaxLatencyAfterHitch) "the long frame didn't raise Bob's replication delay (MaxAllowedLatency $($lat.max) s <= $MaxLatencyAfterHitch)"
        Assert-True ($results.turnA.lagMs -le 300) "Bob sees the sub at most 300 ms late after the long frame ($($results.turnA.lagMs) ms)"

        $win = $script:windows['coastB']
        $end = [S13Analysis]::Compare($tracks.bCyc, $tracks.aCyc, $win[1] - 1000, $win[1], 1500)
        Assert-True ($end.instantMax -le $MaxCoastDesync) "coastB : both games agree where the sub came to rest ($($end.instantMax) m)"

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
        Assert-True ($walk.maxDistFromFirst -ge 3) "Bob can walk around inside the moving sub ($($walk.maxDistFromFirst) m)"
        Assert-True ($walk.min[1] -ge $walk.first[1] - 1) "Bob doesn't fall through the floor while walking (lowest local y $($walk.min[1]), start $($walk.first[1]))"
        $view = [S13Analysis]::CompareField($tracks.bSelf, 'lx', $tracks.aBob, 'lx', $win[0], $win[1], 1000)
        Write-Host "  Bob walking as Alice sees him: $(Format-Result $view)"
        Assert-True ($view.max -le $MaxPartnerViewError) "Alice sees Bob walk where he walks ($($view.max) m)"

        # #2379: the partner must see the pilot where the pilot really is while she leaves the helm
        $win = $script:windows['exitHelmA']
        Write-Host "  Alice leaving the helm, her own game: $(Format-Result ([S13Analysis]::Drift($tracks.aSelf, 'lx', $win[0], $win[1])))"
        Write-Host "  Alice leaving the helm, her pawn: $(Format-Result ([S13Analysis]::Drift($tracks.aSelf, 'hx', $win[0], $win[1])))"
        Write-Host "  Alice leaving the helm, as Bob sees her: $(Format-Result ([S13Analysis]::Drift($tracks.bAlice, 'lx', $win[0], $win[1])))"
        $view = [S13Analysis]::CompareField($tracks.aSelf, 'lx', $tracks.bAlice, 'lx', $win[0], $win[1], 1000)
        Write-Host "  Alice leaving the helm, partner view vs own: $(Format-Result $view)"
        Assert-True ($view.max -le $MaxPartnerViewError) "#2379: Bob sees Alice where she is while she leaves the helm with a key held ($($view.max) m)"

        # Hand-over at speed: the new owner must not stop the sub dead, the old owner must not see it jump back
        $lockAtB = ($tracks.bCyc.Events | Where-Object { $_.Key -eq 'lock' -and $_.Value -eq 'EXCLUSIVE' -and $_.Ms -ge $script:handoverFrom } | Select-Object -First 1).Ms
        if ($lockAtB) {
            $before = Get-Speed $tracks.bCyc ($lockAtB - 400) ($lockAtB - 50)
            $after = Get-Speed $tracks.bCyc ($lockAtB + 50) ($lockAtB + 400)
            $back = Get-MaxBackStep $tracks.aCyc $lockAtB ($lockAtB + 3000)
            Write-Host ("  hand-over: Bob's sub {0:0.00} m/s before, {1:0.00} m/s after he took over; largest step back in Alice's game {2:0.000} m" -f $before, $after, $back)
            Assert-True ($before -ge 1) ("the sub was moving at the hand-over ({0:0.00} m/s)" -f $before)
            Assert-True ($after -ge 0.5 * $before) ("the sub keeps moving when Bob takes over ({0:0.00} -> {1:0.00} m/s)" -f $before, $after)
            Assert-True ($back -le $MaxHandoverBackStep) ("Alice doesn't see the sub snap back at the hand-over ({0:0.000} m <= $MaxHandoverBackStep)" -f $back)
        }
        else {
            Add-Failure 'no hand-over to Bob found in his recording'
        }

        if ($tracks.aMoth) {
            foreach ($w in 'restDocked', 'driveDocked') {
                $win = $script:windows[$w]
                if (-not $win) { continue }
                Write-Host "  [$w] Cyclops in Bob's game (simulating): $(Format-Result ([S13Analysis]::Drift($tracks.bCyc, 'px', $win[0], $win[1]))) speed $(Format-Result ([S13Analysis]::Stats($tracks.bCyc, 'vx', $win[0], $win[1], $true)))"
                foreach ($name in 'aMoth', 'bMoth') {
                    $drift = [S13Analysis]::Drift($tracks[$name], 'lx', $win[0], $win[1])
                    Write-Host "  [$w] docked Seamoth in the sub, $name : $(Format-Result $drift)"
                    Assert-True ($drift.maxDistFromFirst -le $MaxStandingDrift) "[$w] the docked Seamoth stays in the bay in $name ($($drift.maxDistFromFirst) m)"
                }
            }
            $results.drivenDocked = Show-Compare 'Alice sees Bob drive with the Seamoth docked' $tracks.bCyc $tracks.aCyc 'driveDocked'
        }
    }
    Assert-NoDiff -Ids $cyclopsId 'no desync reported for the Cyclops'

    if (-not $SkipRejoin) {
        Step 'Bob leaves the game inside the sub, Alice drives on, both come back' {
            if ((Get-State $A).currentSub.id -ne $cyclopsId) { Assert-True (Enter-Cyclops $A) "$A is back in the Cyclops" }
            if ((Get-State $B).currentSub.id -ne $cyclopsId) { Assert-True (Enter-Cyclops $B) "$B is back in the Cyclops" }
            Invoke-Bridge $B 'call IngameMenu.main.QuitGame false' | Out-Null
            $left = Wait-Until { -not ((Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $B -and $_.online }) } -TimeoutSeconds 60
            Assert-True ([bool]$left) "$B left the game"
            Assert-True (Enter-Helm $A) "$A is piloting"
            $start = [double[]](Invoke-BridgeJson $A "get $cyclopsId Transform.position")
            # The sub ends up heading west towards the rising ground: turn on the spot (about 100 deg), then drive away
            Invoke-Bridge $A 'input move 1 0 0' | Out-Null
            Start-Sleep -Seconds 15
            Invoke-Bridge $A 'input move 0 0 1' | Out-Null
            Start-Sleep -Seconds 12
            Invoke-Bridge $A 'input off' | Out-Null
            Start-Sleep -Seconds 4
            $end = [double[]](Invoke-BridgeJson $A "get $cyclopsId Transform.position")
            $moved = [math]::Sqrt([math]::Pow($end[0] - $start[0], 2) + [math]::Pow($end[1] - $start[1], 2) + [math]::Pow($end[2] - $start[2], 2))
            Assert-True ($moved -ge 40) ("the sub moved on {0:0} m without Bob (more than ValidateCurrentSub's 35 m)" -f $moved)
            Invoke-Bridge 'server' 'save' | Out-Null
            Start-Sleep -Seconds 3
            & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop | Out-Null
            Start-Sleep -Seconds 5
            Start-ScenarioGames -Players $A, $B -SaveName $SaveName -KeepSave
            Start-Sleep -Seconds 5
            $bob = Get-State $B
            $sub = [double[]](Invoke-BridgeJson $B "get $cyclopsId Transform.position")
            $distance = [math]::Sqrt([math]::Pow($bob.position[0] - $sub[0], 2) + [math]::Pow($bob.position[1] - $sub[1], 2) + [math]::Pow($bob.position[2] - $sub[2], 2))
            Write-Host ("  Bob came back at {0}, {1:0.0} m from the sub's origin, sub '{2}', underwater {3}" -f (Format-Vector ([double[]]$bob.position)), $distance, $bob.currentSub.name, $bob.isUnderwater)
            Assert-True ($distance -le 25) ("Bob came back inside the sub ({0:0.0} m from its origin)" -f $distance)
            Assert-Equal $cyclopsId $bob.currentSub.id "Bob's currentSub is the Cyclops after rejoining"
            Assert-True (-not $bob.isUnderwater) 'Bob is not underwater after rejoining'
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
    Write-Host $_.ScriptStackTrace
}
finally {
    foreach ($player in $A, $B) { try { Invoke-Bridge $player 'input off' | Out-Null } catch { } }
}
Complete-Scenario -Name 'S13 CyclopsMovement' -Keep:$Keep
