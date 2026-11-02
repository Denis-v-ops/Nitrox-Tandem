<#
.SYNOPSIS
    FIX_SPEC S16: do both players see the same leviathan in the same place over time? Measured with per-frame tracks of
    a natural leviathan in both games (bridge "record", S13Helpers.ps1), the server's record and its lock holder.

.DESCRIPTION
    Player report: "Leviathans aren't synced, so when I see one my friend doesn't, or sees it in a different place."
    One leviathan per run (-Creature), found where the game spawns it (-Spots). Both players are invisible to creatures
    (NoAggression) unless -Chase, so the leviathan patrols as it does when nobody is near; both games keep their camera
    on it (hold ... None), so a windowless game runs it at full level of detail (TESTING.md, BehaviourLOD).

    Cases (-Only picks some, in this order):
      arrive    the first player is near it for a while, the second arrives from far away: where does their game spawn it?
      ab        both near, the first player's game simulates it: the partner's track vs the simulator's
      handover  the simulating player leaves (1 km): does the lock move, does it jump, stall or vanish for the one who stays?
      ba        the other way round: the second player simulates, the first comes back and watches
      roam      it swims (leash moved by the simulator's game) from the simulating player to the partner 600 m ahead: does
                the server's cell follow, does the lock follow, does the partner keep seeing it move?
    Starts a fresh world (save s16-leviathans) unless -NoLaunch. Needs a Debug build; windowless if $env:NITROX_WINDOWLESS.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's16-leviathans',
    [ValidateSet('ReaperLeviathan', 'GhostLeviathan', 'SeaDragon', 'Reefback')]
    [string]$Creature = 'ReaperLeviathan',
    # Where to look for it (x y z per spot); the first spot with one within -SearchRadius wins.
    [double[][]]$Spots,
    [double]$SearchRadius = 250,
    [string[]]$Only = @('arrive', 'ab', 'handover', 'ba', 'roam'),
    [int]$WindowSeconds = 60,
    # Players aggressive targets: the leviathan chases them (attack movement instead of patrol).
    [switch]$Chase,
    # Also fail on the replication-quality thresholds below (S16b/c: the partner's copy within 1 m of the simulator's on
    # average, never more than 5 m, no one-frame step over 3 m, spawned within 5 m). Without it they are printed as "info"
    # and PASS means what S16 fixed: the lock follows the leviathan, no jump at a hand-over, the partner keeps it.
    [switch]$Strict,
    # How far the players float from the leviathan, and how far the roaming leviathan travels.
    # VeryFar entities (Reapers) load within about 500 m of a player (clipmap level 3: 8 cells of 128 m, read in game), so the
    # roaming leviathan must end up further than that from the simulating player (150 m behind its start), and the box is
    # per axis, so it travels along x. -RoamAngle is the heading in degrees (x towards z).
    [double]$Near = 40, [double]$RoamDistance = 600, [double]$RoamAngle = 0,
    # Thresholds (metres / seconds).
    # Before S16b (runs 1-7): 0.8-4.3 m mean, up to 36 m, snaps up to 16.6 m, spawns up to 34 m off.
    [double]$MaxMeanError = 1, [double]$MaxError = 5, [double]$MaxSnap = 3, [double]$MaxHandoverJump = 5, [double]$MaxLockDelaySeconds = 10,
    [string]$OutDir = (Join-Path $env:TEMP 's16-recordings'))

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'S13Helpers.ps1')
$A = 'Alice'
$B = 'Bob'
$inv = [Globalization.CultureInfo]::InvariantCulture
New-Item -ItemType Directory -Force $OutDir | Out-Null
$script:Summary = New-Object System.Collections.ArrayList

# Leviathan component (class) per tech type, for "find". GhostLeviathan also finds juveniles (same class).
$component = @{ ReaperLeviathan = 'ReaperLeviathan'; GhostLeviathan = 'GhostLeviathan'; SeaDragon = 'SeaDragon'; Reefback = 'Reefback' }[$Creature]
if (-not $Spots) {
    # Known spawn areas (game 83031, read in game 2026-11-02). Spots are tried in order.
    $Spots = @{
        ReaperLeviathan = @(@(1150, -70, 260), @(1100, -60, 400))
        GhostLeviathan  = @(@(-700, -350, -900), @(-500, -500, -600), @(-900, -300, -1100))
        SeaDragon       = @(@(-200, -1300, -200), @(250, -1300, -300))
        Reefback        = @(@(-500, -40, 600), @(-600, -40, 100), @(800, -40, -400))
    }[$Creature]
}

function Format-P { param([double[]]$P) ($P | ForEach-Object { $_.ToString('0.0', $inv) }) -join ' ' }
function Distance { param([double[]]$P, [double[]]$Q) [math]::Sqrt([math]::Pow($P[0] - $Q[0], 2) + [math]::Pow($P[1] - $Q[1], 2) + [math]::Pow($P[2] - $Q[2], 2)) }
function Now-Ms { [DateTime]::UtcNow.Ticks / 10000.0 }
function Other { param([string]$Player) if ($Player -eq $A) { $B } else { $A } }

function Get-Query { param([string]$Id) Invoke-Bridge 'server' "query $Id" -AllowError }

<# S16b's send counters in a game ($null on a build without them). #>
function Get-CreatureSent {
    param([string]$Player)
    $packets = Invoke-Bridge $Player 'get EntityPositionBroadcaster.CreaturePacketsSent' -AllowError
    $poses = Invoke-Bridge $Player 'get EntityPositionBroadcaster.CreatureMovementsSent' -AllowError
    if ($packets.StartsWith('ERR ') -or $poses.StartsWith('ERR ')) { return $null }
    [pscustomobject]@{ packets = [double]$packets; poses = [double]$poses }
}

<# How many moving entities (creatures, thrown items) this game simulates and broadcasts. #>
function Get-SimulatedCount {
    param([string]$Player)
    $reply = Invoke-Bridge $Player 'get EntityPositionBroadcaster.watchingEntityIds.Count' -AllowError
    if ($reply.StartsWith('ERR ')) { '?' } else { $reply }
}

<# A replication-quality threshold: a check with -Strict, printed as information otherwise (see -Strict). #>
function Assert-Measure {
    param([bool]$Condition, [string]$Message)
    if ($Strict) { Assert-True $Condition $Message }
    else { Write-Host "  info $(if ($Condition) { 'within' } else { 'OVER' }): $Message" -ForegroundColor $(if ($Condition) { 'DarkGray' } else { 'Yellow' }) }
}

function Get-LockOwner {
    param([string]$Id)
    if ((Get-Query $Id) -match 'Owner: (\w+)') { $Matches[1] } else { $null }
}

<# The server's record: position (the last one its simulator sent) and cell. #>
function Get-ServerRecord {
    param([string]$Id)
    $text = Get-Query $Id
    $record = [ordered]@{ position = $null; cell = $null; owner = $null; level = $null }
    # The server formats numbers in the PC's culture ("[1184,75, -81,63, 262,88]" on a German PC): ", " separates them.
    if ($text -match '\(Position: \[([^\]]+)\]') {
        $parts = $Matches[1] -split ', '
        if ($parts.Count -eq 3) { $record.position = [double[]]@($parts | ForEach-Object { [double]::Parse(($_ -replace ',', '.'), $inv) }) }
    }
    if ($text -match 'Cell: \[AbsoluteEntityCell Position: (\[[^\]]+\]) BatchId: (\[[^\]]+\]) CellId: (\[[^\]]+\])') { $record.cell = "$($Matches[2])/$($Matches[3])" }
    if ($text -match 'Level: (\d+)') { $record.level = [int]$Matches[1] }
    if ($text -match 'Owner: (\w+)') { $record.owner = $Matches[1] }
    [pscustomobject]$record
}

<# The leviathan in a game: $null when not loaded there; position, active, remote (moved by the partner's packets), lod. #>
function Get-Copy {
    param([string]$Player, [string]$Id)
    $reply = Invoke-Bridge $Player "get $Id Transform.position" -AllowError
    if ($reply.StartsWith('ERR ')) { return $null }
    $position = [double[]]($reply | ConvertFrom-Json)
    $active = (Invoke-Bridge $Player "get $Id GameObject.activeInHierarchy" -AllowError) -eq 'true'
    $remote = -not (Invoke-Bridge $Player "get $Id RemotelyControlled.enabled" -AllowError).StartsWith('ERR ')
    $lod = Invoke-Bridge $Player "get $Id BehaviourLOD.current" -AllowError
    [pscustomobject]@{ position = $position; active = $active; remote = $remote; lod = $lod.Trim('"') }
}

function Show-Copies {
    param([string]$Id, [string]$Label)
    $server = Get-ServerRecord $Id
    $parts = @("server $(if ($server.position) { Format-P $server.position } else { '?' }) owner $($server.owner)")
    $copies = @{}
    foreach ($player in $A, $B) {
        $copy = Get-Copy $player $Id
        $copies[$player] = $copy
        $parts += if ($copy) { "$player $(Format-P $copy.position) active=$($copy.active) remote=$($copy.remote) lod=$($copy.lod)" } else { "$player -" }
    }
    if ($copies[$A] -and $copies[$B]) { $parts += ('apart {0:0.0} m' -f (Distance $copies[$A].position $copies[$B].position)) }
    Write-Host "  [$Label] $($parts -join ' | ')"
    [pscustomobject]@{ server = $server; copies = $copies }
}

<# Both games keep their camera on the leviathan, so a windowless game runs it at full level of detail. #>
function Watch-Leviathan {
    param([string]$Id, [int]$Seconds)
    foreach ($player in $A, $B) {
        Invoke-Bridge $player 'hold off' -AllowError | Out-Null
        if (Test-Loaded $player $Id) { Invoke-Bridge $player "hold $Id $Seconds None" -AllowError | Out-Null }
    }
}

function Test-Loaded {
    param([string]$Player, [string]$Id)
    -not (Invoke-Bridge $Player "entity $Id" -AllowError).StartsWith('ERR ')
}

function Set-Players-Invisible {
    foreach ($player in $A, $B) {
        if ($Chase) { Invoke-Bridge $player 'call GameModeUtils.DeactivateCheat NoAggression' | Out-Null }
        else { Invoke-Bridge $player 'call GameModeUtils.ActivateCheat NoAggression' | Out-Null }
        Invoke-Bridge $player 'set player Survival.food 100' | Out-Null
        Invoke-Bridge $player 'set player Survival.water 100' | Out-Null
    }
}

<# A point $Distance m from the leviathan, on the side away from the other spot, at its depth. #>
function Get-Beside {
    param([double[]]$Center, [double]$Distance, [double]$AngleDegrees)
    $angle = $AngleDegrees * [math]::PI / 180
    [double[]]@(($Center[0] + $Distance * [math]::Cos($angle)), $Center[1], ($Center[2] + $Distance * [math]::Sin($angle)))
}

<#
    Records the leviathan in both games for $Seconds, then compares the observer's track with the owner's
    (S13Analysis.Compare: lag, residual after the lag, instant error without it, stalls and jumps) and prints the
    state events (lock, active, remote, lod) of both.
#>
function Measure-Pair {
    param([string]$Id, [string]$Owner, [int]$Seconds, [string]$Name)
    $observer = Other $Owner
    Watch-Leviathan $Id ($Seconds + 5)
    $sentBefore = Get-CreatureSent $Owner
    Start-Recording $Owner "$Name-o" $Id
    Start-Recording $observer "$Name-r" $Id
    Start-Sleep -Seconds $Seconds
    $ownerTrack = Stop-Recording $Owner "$Name-o" -SaveTo (Join-Path $OutDir "$Name-$Owner.json")
    $observerTrack = Stop-Recording $observer "$Name-r" -SaveTo (Join-Path $OutDir "$Name-$observer.json")
    $sentAfter = Get-CreatureSent $Owner
    if ($sentBefore -and $sentAfter) {
        # About 17 bytes per packet and 70 per creature (CreatureMovementPolicyTest.FullPacketFitsTheSmallestMtu)
        $packets = ($sentAfter.packets - $sentBefore.packets) / $Seconds; $poses = ($sentAfter.poses - $sentBefore.poses) / $Seconds
        Write-Host ('    {0} sent {1:0.0} creature poses/s in {2:0.0} packets/s, about {3:0.0} KB/s ({4} creatures simulated)' -f $Owner, $poses, $packets, (($packets * 17 + $poses * 70) / 1024), (Get-SimulatedCount $Owner))
    }
    $t = $observerTrack.Times
    $from = [math]::Max($t[0], $ownerTrack.Times[0]) + 1000
    $to = [math]::Min($t[$t.Length - 1], $ownerTrack.Times[$ownerTrack.Times.Length - 1])
    $result = [S13Analysis]::Compare($ownerTrack, $observerTrack, $from, $to, 1500)
    $snaps = Get-Snaps $observerTrack $from $to -OwnerTrack $ownerTrack
    Write-Host "  $Name ($Owner simulates, $observer watches): $(Format-Result $result)"
    Assert-Measure ($snaps.excessMax -le $MaxSnap) ('{0}: no snap over {1} m for {2} (a one-frame step beyond what the original moved meanwhile: {3:0.0} m)' -f $Name, $MaxSnap, $observer, $snaps.excessMax)
    Write-Host ('    observer snaps (one-frame steps over {0} m): {1} in {2:0} s, largest {3:0.0} m; beyond the original''s movement: {4} over {0} m, largest {5:0.0} m' -f $SnapMetres, $snaps.count, (($to - $from) / 1000), $snaps.max, $snaps.excessCount, $snaps.excessMax)
    Write-Host "    owner: $(Format-Result ([S13Analysis]::Frames($ownerTrack, $from, $to))) speed $(Format-Result ([S13Analysis]::Stats($ownerTrack, 'vx', $from, $to, $true)))"
    Write-Host "    observer: $(Format-Result ([S13Analysis]::Frames($observerTrack, $from, $to))) speed $(Format-Result ([S13Analysis]::Stats($observerTrack, 'vx', $from, $to, $true)))"
    Show-Events $ownerTrack @('lock', 'active', 'remote', 'lod', 'target')
    Show-Events $observerTrack @('lock', 'active', 'remote', 'lod', 'target')
    [void]$script:Summary.Add([pscustomobject]@{ case = $Name; owner = $Owner; lagMs = $result.lagMs; residualRms = $result.residualRms; instantMean = $result.instantMean; instantMax = $result.instantMax; speed = $result.ownerSpeed; snaps = $snaps.count; maxSnap = $snaps.max; maxExcess = $snaps.excessMax })
    [pscustomobject]@{ result = $result; owner = $ownerTrack; observer = $observerTrack; snaps = $snaps }
}

<#
    One-frame steps longer than $SnapMetres (a leviathan at 25 m/s moves 0.2 m per frame at 120 fps): the observer's copy
    put back at the position in a partner's packet (before S16b: RemotelyControlled.TeleportIfTooFar, beyond 5 m).
    With -OwnerTrack also the *excess*: how much further the copy stepped in that frame than the simulator's creature moved
    over the same time. A long frame (100-350 ms, cells loading) moves both by metres and the physics catch-up in the next
    frame does too, in the simulating game as well (seen after S16b: a 353 ms frame stepped the copy 8.4 m while the
    original swam 5.3 m); a teleport moves only the copy. -Strict checks the excess.
#>
$SnapMetres = 1.0
function Get-Snaps {
    param([S13Track]$Track, [double]$From, [double]$To, [S13Track]$OwnerTrack)
    $t = $Track.Times; $x = $Track.Column('px'); $y = $Track.Column('py'); $z = $Track.Column('pz')
    if ($OwnerTrack) { $ot = $OwnerTrack.Times; $ox = $OwnerTrack.Column('px'); $oy = $OwnerTrack.Column('py'); $oz = $OwnerTrack.Column('pz') }
    $count = 0; $max = 0.0; $excessCount = 0; $excessMax = 0.0
    for ($i = 1; $i -lt $t.Length; $i++) {
        if ($t[$i] -lt $From -or $t[$i] -gt $To -or [double]::IsNaN($x[$i]) -or [double]::IsNaN($x[$i - 1])) { continue }
        $step = [math]::Sqrt([math]::Pow($x[$i] - $x[$i - 1], 2) + [math]::Pow($y[$i] - $y[$i - 1], 2) + [math]::Pow($z[$i] - $z[$i - 1], 2))
        if ($step -gt $SnapMetres) { $count++ }
        if ($step -gt $max) { $max = $step }
        if ($OwnerTrack -and $step -gt $SnapMetres) {
            # The simulator's samples at or before both ends of this frame
            $a = [Array]::BinarySearch($ot, $t[$i - 1]); if ($a -lt 0) { $a = [math]::Max(0, -$a - 2) }
            $b = [Array]::BinarySearch($ot, $t[$i]); if ($b -lt 0) { $b = [math]::Max(0, -$b - 2) }
            $ownerStep = [math]::Sqrt([math]::Pow($ox[$b] - $ox[$a], 2) + [math]::Pow($oy[$b] - $oy[$a], 2) + [math]::Pow($oz[$b] - $oz[$a], 2))
            if ([double]::IsNaN($ownerStep)) { $ownerStep = 0 }
            $excess = $step - $ownerStep
            if ($excess -gt $SnapMetres) { $excessCount++ }
            if ($excess -gt $excessMax) { $excessMax = $excess }
        }
    }
    [pscustomobject]@{ count = $count; max = $max; excessCount = $excessCount; excessMax = $excessMax }
}

<# Largest per-frame step of a track (a jump) between two times. #>
function Get-MaxStep {
    param([S13Track]$Track, [double]$From, [double]$To)
    $t = $Track.Times; $x = $Track.Column('px'); $y = $Track.Column('py'); $z = $Track.Column('pz')
    $max = 0.0; $at = $null
    for ($i = 1; $i -lt $t.Length; $i++) {
        if ($t[$i] -lt $From -or $t[$i] -gt $To -or [double]::IsNaN($x[$i]) -or [double]::IsNaN($x[$i - 1])) { continue }
        $step = [math]::Sqrt([math]::Pow($x[$i] - $x[$i - 1], 2) + [math]::Pow($y[$i] - $y[$i - 1], 2) + [math]::Pow($z[$i] - $z[$i - 1], 2))
        if ($step -gt $max) { $max = $step; $at = $t[$i] }
    }
    [pscustomobject]@{ step = $max; at = $at }
}

function Get-PathLength {
    param([S13Track]$Track, [double]$From, [double]$To)
    $t = $Track.Times; $x = $Track.Column('px'); $y = $Track.Column('py'); $z = $Track.Column('pz')
    $sum = 0.0
    for ($i = 1; $i -lt $t.Length; $i++) {
        if ($t[$i] -lt $From -or $t[$i] -gt $To -or [double]::IsNaN($x[$i]) -or [double]::IsNaN($x[$i - 1])) { continue }
        $sum += [math]::Sqrt([math]::Pow($x[$i] - $x[$i - 1], 2) + [math]::Pow($y[$i] - $y[$i - 1], 2) + [math]::Pow($z[$i] - $z[$i - 1], 2))
    }
    $sum
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    Set-Players-Invisible
    $podPos = [double[]](Invoke-BridgeJson $B 'state').position
    $faraway = @(($podPos[0]), ($podPos[1] - 5), ($podPos[2] - 20))

    $script:levId = $null
    Step "Find a natural $Creature" {
        Invoke-Bridge $B "teleport $(Format-P $faraway)" | Out-Null
        foreach ($spot in $Spots) {
            Invoke-Bridge $A "teleport $(Format-P $spot)" | Out-Null
            $found = Wait-Until { @(Invoke-BridgeJson $A "find $component 5") | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $_.distance -le $SearchRadius } | Select-Object -First 1 } -TimeoutSeconds 25 -IntervalSeconds 2
            if ($found) {
                $script:levId = $found.id
                Write-Host "  $Creature $($found.id) at $(Format-P ([double[]]$found.position)), $($found.distance) m from spot $(Format-P $spot)"
                break
            }
            Write-Host "  none within $SearchRadius m of $(Format-P $spot)"
        }
        Assert-True ([bool]$script:levId) "found a $Creature"
        if (-not $script:levId) { throw "no $Creature found" }
        $record = Get-ServerRecord $script:levId
        Write-Host "  server: level $($record.level), cell $($record.cell), owner $($record.owner), spawned by server: $((Get-Query $script:levId) -match 'SpawnedByServer: True')"
        Write-Host "  facts: $(Invoke-Bridge $A "get $($script:levId) LargeWorldEntity.cellLevel") cell level; leash $(Invoke-Bridge $A "get $($script:levId) Creature.leashPosition"); swim velocity $(Invoke-Bridge $A "get $($script:levId) SwimRandom.swimVelocity" -AllowError) every $(Invoke-Bridge $A "get $($script:levId) SwimRandom.swimInterval" -AllowError) s; leash distance $(Invoke-Bridge $A "get $($script:levId) StayAtLeashPosition.leashDistance" -AllowError)"
        # Alice floats beside it.
        $pos = (Get-Copy $A $script:levId).position
        Invoke-Bridge $A "teleport $(Format-P (Get-Beside $pos $Near 0))" | Out-Null
        $owner = Wait-Until { Get-LockOwner $script:levId } -TimeoutSeconds 15
        if (-not $NoLaunch) { Assert-Equal $A $owner "$A simulates it (first to load its cell)" } else { Write-Host "  $owner simulates it" }
    }
    $id = $script:levId

    if ($Only -contains 'arrive') {
        Step 'arrive: Alice has been near it for 30 s, Bob arrives from far away' {
            Watch-Leviathan $id 40
            Start-Sleep -Seconds 30
            $before = Show-Copies $id 'before Bob arrives'
            $alicePos = $before.copies[$A].position
            Assert-True (-not (Test-Loaded $B $id)) "$B, 1 km+ away, doesn't have it"
            $serverError = Distance $before.server.position $alicePos
            Write-Host ('  server record is {0:0.0} m from where Alice sees it' -f $serverError)
            Invoke-Bridge $B "teleport $(Format-P (Get-Beside $alicePos ($Near * 2) 0))" | Out-Null
            $t0 = Now-Ms
            $seen = Wait-Until { Test-Loaded $B $id } -TimeoutSeconds 30 -IntervalSeconds 0.25
            $spawnMs = (Now-Ms) - $t0
            Assert-True ([bool]$seen) "$B's game spawns it after arriving"
            if ($seen) {
                $at = Show-Copies $id 'Bob just spawned it'
                $error0 = Distance $at.copies[$A].position $at.copies[$B].position
                Write-Host ('  spawned after {0:0.0} s, {1:0.0} m from where Alice sees it' -f ($spawnMs / 1000), $error0)
                Assert-Measure ($error0 -le $MaxError) ('Bob spawns it within {0} m of where Alice sees it ({1:0.0} m)' -f $MaxError, $error0)
                $m = Measure-Pair $id $A 20 'arrive'
                Assert-Measure ($m.result.instantMean -le $MaxMeanError) ('after arriving, Bob sees it within {0} m on average ({1})' -f $MaxMeanError, $m.result.instantMean)
            }
        }
    }

    if ($Only -contains 'ab') {
        Step 'ab: both near, Alice simulates' {
            if (-not (Test-Loaded $B $id)) { Invoke-Bridge $B "teleport $(Format-P (Get-Beside (Get-Copy $A $id).position ($Near * 2) 0))" | Out-Null; Wait-Until { Test-Loaded $B $id } -TimeoutSeconds 30 | Out-Null }
            Assert-Equal $A (Get-LockOwner $id) "$A still simulates it"
            $m = Measure-Pair $id $A $WindowSeconds 'ab'
            Assert-Measure ($m.result.instantMean -le $MaxMeanError) ('Bob sees it within {0} m of Alice on average ({1})' -f $MaxMeanError, $m.result.instantMean)
            Assert-Measure ($m.result.instantMax -le $MaxError) ('Bob never sees it more than {0} m from Alice ({1})' -f $MaxError, $m.result.instantMax)
            Show-Copies $id 'after ab' | Out-Null
        }
    }

    if ($Only -contains 'handover') {
        Step 'handover: Alice (simulating) leaves, Bob stays' {
            if (-not (Test-Loaded $B $id)) { Invoke-Bridge $B "teleport $(Format-P (Get-Beside (Get-Copy $A $id).position ($Near * 2) 0))" | Out-Null; Wait-Until { Test-Loaded $B $id } -TimeoutSeconds 30 | Out-Null }
            Watch-Leviathan $id 45
            Start-Recording $B 'handover-r' $id
            Start-Sleep -Seconds 5
            $left = Now-Ms
            Invoke-Bridge $A "teleport $(Format-P $faraway)" | Out-Null
            $owner = Wait-Until { $o = Get-LockOwner $id; if ($o -eq $B) { $o } } -TimeoutSeconds 30 -IntervalSeconds 0.5
            $lockDelay = ((Now-Ms) - $left) / 1000
            Assert-True ($owner -eq $B) ('the lock moved to Bob ({0:0.0} s after Alice left)' -f $lockDelay)
            Start-Sleep -Seconds 25
            $track = Stop-Recording $B 'handover-r' -SaveTo (Join-Path $OutDir "handover-$B.json")
            Show-Events $track @('lock', 'active', 'remote', 'lod', 'target')
            $lockEvent = @($track.Events | Where-Object { $_.Key -eq 'lock' -and $_.Value -ne '-' } | Select-Object -First 1)
            $handAt = if ($lockEvent) { $lockEvent[0].Ms } else { $left }
            # From the lock event on (before it, Bob only watched: his copy snaps as in "ab")
            $jump = Get-MaxStep $track $handAt ($handAt + 3000)
            $before = Get-PathLength $track ($handAt - 5000) $handAt
            $after = Get-PathLength $track ($handAt + 2000) ($handAt + 22000)
            Write-Host ('  largest step around the hand-over {0:0.00} m, {3:0} ms after the lock event; path 5 s before {1:0.0} m, 20 s after {2:0.0} m' -f $jump.step, $before, $after, ($jump.at - $handAt))
            Assert-True ($jump.step -le $MaxHandoverJump) ('no jump over {0} m at the hand-over ({1:0.00} m)' -f $MaxHandoverJump, $jump.step)
            Assert-True ($after -ge 5) ('it keeps moving for Bob after the hand-over ({0:0.0} m in 20 s)' -f $after)
            [void]$script:Summary.Add([pscustomobject]@{ case = 'handover'; owner = $B; lagMs = [math]::Round($lockDelay * 1000); residualRms = $null; instantMean = $null; instantMax = $null; speed = [math]::Round($after / 20, 2); snaps = $null; maxSnap = $jump.step })
        }
    }

    if ($Only -contains 'ba') {
        Step 'ba: Bob simulates, Alice comes back and watches' {
            if ((Get-LockOwner $id) -ne $B) {
                Invoke-Bridge $A "teleport $(Format-P $faraway)" | Out-Null
                Wait-Until { (Get-LockOwner $id) -eq $B } -TimeoutSeconds 30 | Out-Null
            }
            $pos = (Get-Copy $B $id).position
            Invoke-Bridge $A "teleport $(Format-P (Get-Beside $pos $Near 180))" | Out-Null
            $seen = Wait-Until { Test-Loaded $A $id } -TimeoutSeconds 30 -IntervalSeconds 0.25
            Assert-True ([bool]$seen) "$A's game spawns it again"
            $at = Show-Copies $id 'Alice just spawned it'
            if ($at.copies[$A] -and $at.copies[$B]) { Write-Host ('  Alice spawned it {0:0.0} m from where Bob sees it' -f (Distance $at.copies[$A].position $at.copies[$B].position)) }
            Assert-Equal $B (Get-LockOwner $id) "$B still simulates it"
            $m = Measure-Pair $id $B $WindowSeconds 'ba'
            Assert-Measure ($m.result.instantMean -le $MaxMeanError) ('Alice sees it within {0} m of Bob on average ({1})' -f $MaxMeanError, $m.result.instantMean)
            Assert-Measure ($m.result.instantMax -le $MaxError) ('Alice never sees it more than {0} m from Bob ({1})' -f $MaxError, $m.result.instantMax)
        }
    }

    if ($Only -contains 'roam') {
        Step "roam: it swims $RoamDistance m from the simulating player to the partner" {
            $sim = Get-LockOwner $id
            $partner = Other $sim
            $start = (Get-Copy $sim $id).position
            # The simulator floats 150 m behind the start, the partner waits $RoamDistance m ahead (Crash Zone, open water
            # towards its east edge).
            $goal = Get-Beside $start $RoamDistance $RoamAngle
            $simSpot = Get-Beside $start 150 (180 + $RoamAngle)
            Invoke-Bridge $sim "teleport $(Format-P $simSpot)" | Out-Null
            Invoke-Bridge $partner "teleport $(Format-P (Get-Beside $goal $Near 90))" | Out-Null
            Start-Sleep -Seconds 8
            Assert-True (-not (Test-Loaded $partner $id)) "$partner, $RoamDistance m away, doesn't have it yet"
            # Only the simulating game moves its leash: StayAtLeashPosition swims it there as it would on its own. A game that
            # takes it over swims it back towards its real leash (spawn metadata), so the lock may come back too.
            Invoke-Bridge $sim "set $id Creature.leashPosition $(($goal | ForEach-Object { $_.ToString('0.0', $inv) }) -join ',')" | Out-Null
            Invoke-Bridge $sim 'hold off' -AllowError | Out-Null
            Invoke-Bridge $sim "hold $id 200 None" -AllowError | Out-Null
            Start-Recording $sim 'roam-s' $id
            $t0 = Now-Ms
            $partnerRecording = $false
            # Entities load in a box around a player: how far it is from the simulator, per axis (the larger one).
            $handedAt = $null; $maxOwnedOutside = 0.0
            $deadline = (Get-Date).AddSeconds(90)
            while ((Get-Date) -lt $deadline) {
                $s = Show-Copies $id ('{0,5:0.0} s' -f (((Now-Ms) - $t0) / 1000))
                if ($s.server.position) {
                    $box = [math]::Max([math]::Abs($s.server.position[0] - $simSpot[0]), [math]::Abs($s.server.position[2] - $simSpot[2]))
                    if ($s.server.owner -eq $sim -and $s.copies[$partner]) { $maxOwnedOutside = [math]::Max($maxOwnedOutside, $box) }
                    if ($s.server.owner -eq $partner -and -not $handedAt) { $handedAt = $box; Write-Host ('  lock moved to {0}, {1:0} m (per axis) from {2}' -f $partner, $box, $sim) }
                }
                if (-not $partnerRecording -and $s.copies[$partner]) {
                    Invoke-Bridge $partner 'hold off' -AllowError | Out-Null
                    Invoke-Bridge $partner "hold $id 200 None" -AllowError | Out-Null
                    Start-Recording $partner 'roam-p' $id
                    $partnerRecording = $true
                }
                Start-Sleep -Seconds 3
            }
            $simTrack = Stop-Recording $sim 'roam-s' -SaveTo (Join-Path $OutDir "roam-$sim.json")
            Show-Events $simTrack @('lock', 'active', 'remote', 'lod', 'target')
            if ($partnerRecording) {
                $pTrack = Stop-Recording $partner 'roam-p' -SaveTo (Join-Path $OutDir "roam-$partner.json")
                Show-Events $pTrack @('lock', 'active', 'remote', 'lod', 'target')
                $tp = $pTrack.Times
                $last10 = Get-PathLength $pTrack ($tp[$tp.Length - 1] - 10000) $tp[$tp.Length - 1]
                Write-Host ('  partner track: {0:0.0} m path in its last 10 s' -f $last10)
                Assert-True ($last10 -ge 3) ("it still moves for $partner at the end ({0:0.0} m in 10 s)" -f $last10)
            }
            Write-Host ("  while $partner had it loaded, $sim still simulated it up to {0:0} m (per axis) away" -f $maxOwnedOutside)
            Assert-True ([bool]$handedAt) "the lock moved to $partner while it was with them"
            Assert-True ($maxOwnedOutside -le 600) ("$sim didn't keep simulating it from outside their loaded area while $partner had it ({0:0} m per axis; the area reaches about 512 m)" -f $maxOwnedOutside)
        }
    }

    Write-Host ''
    Write-Host 'Summary (metres, ms):'
    $script:Summary | Format-Table -AutoSize | Out-String | Write-Host
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
    Write-Host $_.ScriptStackTrace
}

Complete-Scenario -Name "S16-LeviathanSync ($Creature)" -Keep:$Keep
