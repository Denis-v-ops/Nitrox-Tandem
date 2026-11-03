<#
.SYNOPSIS
    COOP_FEATURES_SPEC B1 (warpers, bleeders): both players meet the same warpers, and bleeders have a simulating player.

.DESCRIPTION
    Warpers: both players float near three warper spawners (found in game about 100 m from -650 -495 -951, the "base3"
    teleport spot; server-spawned world entities, level 1). Each spawner's lock must be held by a player (the server's
    utility whitelist). For WatchSeconds (a warper lives about 10 s without a target, WarpOut.maxSearchTime, and its
    spawner waits 20-23 s, warpInterval, before the next) both games list their warpers about every second. Checked:
    every warper has a NitroxId; both games see the same warpers (an id that only one game has for more than
    MismatchSeconds is a mismatch: a new warper or a warp-out reaches the other game within a second); new warpers appear
    after warp-outs; every warper that warped out is gone from the server; the partner's spawner knows its warper
    (WarperEntitySpawner's link, so it wouldn't spawn a second one if it took the spawner over).

    Bleeders: both players near the Sparse Reef's deep wall (bleeders found there, MushroomForest/SparseReef/BonesField
    biomes in the game's loot distribution). For a few of them: a player holds the lock, its AI runs in that game
    (Creature.prevBestAction), it moves, and both games agree on where it is.

    Not covered: the warp ball and a bleeder attaching to the partner (FIX_SPEC S15c, S15d).

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [int]$WatchSeconds = 80,
    # How long a warper may be in one game only: the warp-out animation (0.73 s) plus a packet and a poll.
    [double]$MismatchSeconds = 2.5,
    # Only warpers nearer than this (in the game that has it) must be in both games.
    [double]$CompareRadius = 100)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$WarperSpot = @(-650, -495, -951)
$BleederSpot = @(-726, -295, -726)

function Get-LockOwner {
    param([string]$Id)
    if ((Invoke-Bridge 'server' "query $Id" -AllowError) -match 'Owner: (\w+)') { $Matches[1] } else { $null }
}

function Test-ServerHas {
    param([string]$Id)
    (Invoke-Bridge 'server' "query $Id" -AllowError) -match 'Type: '
}

function Get-Found {
    param([string]$Player, [string]$Type, [int]$Radius = 250)
    @(Invoke-BridgeJson $Player "find $Type $Radius")
}

function Test-Id {
    param($Id)
    "$Id" -match '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
}

function Move-Both {
    param([double[]]$Spot)
    Invoke-Bridge $A "teleport $($Spot[0]) $($Spot[1]) $($Spot[2])" | Out-Null
    # Nearly the same spot: both games load the same cells, so a warper at the edge of the loaded area is in both or neither.
    Invoke-Bridge $B "teleport $($Spot[0] + 1) $($Spot[1]) $($Spot[2])" | Out-Null
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch

    Step 'Both players near the warper spawners; each spawner has a simulating player' {
        Move-Both $WarperSpot
        # The cells load over several seconds: wait until both games list the same spawners twice in a row.
        $script:lastCount = -1
        $script:spawners = Wait-Until {
            $found = @(Get-Found $A 'WarperSpawner')
            $stable = $found.Count -ge 1 -and $found.Count -eq $script:lastCount -and @(Get-Found $B 'WarperSpawner').Count -eq $found.Count
            $script:lastCount = $found.Count
            if ($stable) { $found }
        } -TimeoutSeconds 60 -IntervalSeconds 4
        Assert-True ($spawners.Count -ge 1) "both games have the warper spawners loaded ($($spawners.Count))"
        $bobSpawners = @(Get-Found $B 'WarperSpawner' | ForEach-Object { $_.id })
        $script:owners = @{}
        foreach ($spawner in $spawners) {
            Assert-True (Test-Id $spawner.id) "the spawner at $($spawner.position -join ' ') has a NitroxId ($($spawner.id))"
            Assert-True ($bobSpawners -contains $spawner.id) "$B has the spawner $($spawner.id)"
            $owner = Wait-Until { Get-LockOwner $spawner.id } -TimeoutSeconds 10
            Assert-True ($owner -in $A, $B) "a player simulates the spawner $($spawner.id) (owner: $owner)"
            $owners[$spawner.id] = $owner
        }
    }

    Step "Both games see the same warpers for $WatchSeconds s" {
        $seen = @{ $A = @{}; $B = @{} }
        $idless = @{ $A = 0; $B = 0 }
        # id -> when it was first seen in one game only (a warp-out or a new warper reaches the other game a moment later)
        $oneGameSince = @{}
        $mismatches = New-Object System.Collections.ArrayList
        $maxCount = @{ $A = 0; $B = 0 }
        $script:partnerLinked = 0
        $script:partnerLinkChecks = 0
        $linkChecked = @{}
        $farNoted = @{}
        $deadline = (Get-Date).AddSeconds($WatchSeconds)
        $poll = 0
        while ((Get-Date) -lt $deadline) {
            $now = @{}
            $distance = @{}
            foreach ($player in $A, $B) {
                $found = Get-Found $player 'Warper'
                if ($player -eq $A) { $foundA = $found }
                $now[$player] = @($found | Where-Object { Test-Id $_.id } | ForEach-Object { $_.id })
                foreach ($f in $found) { $distance["$player/$($f.id)"] = [double]$f.distance }
                $idless[$player] += @($found | Where-Object { -not (Test-Id $_.id) }).Count
                if ($found.Count -gt $maxCount[$player]) { $maxCount[$player] = $found.Count }
                foreach ($id in $now[$player]) { $seen[$player][$id] = $true }
            }
            $polledAt = Get-Date
            foreach ($id in @($now[$A] + $now[$B] | Select-Object -Unique)) {
                if (($now[$A] -contains $id) -ne ($now[$B] -contains $id)) {
                    if (-not $oneGameSince[$id]) { $oneGameSince[$id] = $polledAt }
                    $where = if ($now[$A] -contains $id) { $A } else { $B }
                    $away = $distance["$where/$id"]
                    # Far away it may be at the edge of the loaded cells, where the two games differ a little.
                    if (($polledAt - $oneGameSince[$id]).TotalSeconds -gt $MismatchSeconds -and $away -lt $CompareRadius -and
                        -not ($mismatches | Where-Object { $_.StartsWith($id) })) {
                        [void]$mismatches.Add("$id (only $where, $([math]::Round($away)) m away)")
                    }
                    elseif (($polledAt - $oneGameSince[$id]).TotalSeconds -gt $MismatchSeconds -and -not $farNoted.ContainsKey($id)) {
                        $farNoted[$id] = $true
                        Write-Host "    not counted: $id only in $where's game, $([math]::Round($away)) m away (edge of the loaded cells)" -ForegroundColor DarkYellow
                    }
                }
                else {
                    $oneGameSince.Remove($id)
                }
            }
            # Gone from both games: no longer a mismatch.
            foreach ($id in @($oneGameSince.Keys)) {
                if ($now[$A] -notcontains $id -and $now[$B] -notcontains $id) { $oneGameSince.Remove($id) }
            }
            # A new warper in both games, next to its spawner: the partner's spawner knows it (WarperEntitySpawner).
            foreach ($f in @($foundA | Where-Object { (Test-Id $_.id) -and $now[$B] -contains $_.id -and -not $linkChecked.ContainsKey($_.id) })) {
                $linkChecked[$f.id] = $true
                $spawner = $spawners | Where-Object { (Get-Distance ([double[]]$_.position) ([double[]]$f.position)) -le 4 } | Select-Object -First 1
                if (-not $spawner) { continue }
                $partner = if ($owners[$spawner.id] -eq $A) { $B } else { $A }
                $script:partnerLinkChecks++
                $partnerLink = Invoke-Bridge $partner "get $($spawner.id) WarperSpawner.warper" -AllowError
                if ($partnerLink -match '"\$component":\s*"Warper"') { $script:partnerLinked++ } else { Write-Host "    $partner's spawner $($spawner.id) doesn't know the warper $($f.id): $partnerLink" -ForegroundColor Yellow }
            }
            Write-Host "    $A $($now[$A].Count) warpers, $B $($now[$B].Count)" -ForegroundColor DarkGray
            Start-Sleep -Milliseconds 300
        }
        $script:allIds = @(@($seen[$A].Keys) + @($seen[$B].Keys) | Select-Object -Unique)
        Write-Host "  warpers seen: $($allIds.Count) ($A $($seen[$A].Count), $B $($seen[$B].Count)); most at once: $A $($maxCount[$A]), $B $($maxCount[$B])"
        Assert-Equal 0 $idless[$A] "$A saw no warper without a NitroxId (a warper only its own game spawned)"
        Assert-Equal 0 $idless[$B] "$B saw no warper without a NitroxId"
        Assert-True ($allIds.Count -gt $spawners.Count) "the spawners spawned new warpers after warp-outs ($($allIds.Count) warpers from $($spawners.Count) spawners)"
        $onlyA = @($seen[$A].Keys | Where-Object { -not $seen[$B].ContainsKey($_) })
        $onlyB = @($seen[$B].Keys | Where-Object { -not $seen[$A].ContainsKey($_) })
        Assert-Equal 0 ($onlyA.Count + $onlyB.Count) "every warper was in both games (only $A $($onlyA -join ', '); only $B $($onlyB -join ', '))"
        Assert-Equal 0 $mismatches.Count "no warper was in one game only for more than $MismatchSeconds s ($($mismatches -join ', '))"
        Assert-True ($partnerLinkChecks -gt 0 -and $partnerLinked -eq $partnerLinkChecks) "the partner's spawner knows its new warper ($partnerLinked of $partnerLinkChecks new warpers)"
    }

    Step 'Warpers that warped out are gone from the server' {
        # Past any warp-out animation still running (0.73 s), so the diff below doesn't catch one half-destroyed.
        Start-Sleep -Seconds 2
        $present = @(@(Get-Found $A 'Warper') + @(Get-Found $B 'Warper') | ForEach-Object { $_.id })
        $gone = @($allIds | Where-Object { $present -notcontains $_ })
        Assert-True ($gone.Count -ge 1) "at least one warper warped out during the watch ($($gone.Count))"
        $stale = @($gone | Where-Object { Test-ServerHas $_ })
        Assert-Equal 0 $stale.Count "the server dropped every warper that warped out (still there: $($stale -join ', '))"
    }
    foreach ($hit in @($script:LastDiff | Where-Object { $_.TechType -eq 'Warper' })) { Write-Host "    [$($hit.Kind)] [$($hit.Where)] $($hit.Id): $($hit.Detail)" -ForegroundColor Yellow }
    Assert-NoDiff -Ids $allIds 'no desync reported for any warper seen (no ghost left in a game)'

    Step 'Bleeders near both players have a simulating player and move' {
        Move-Both $BleederSpot
        $script:bleeders = Wait-Until { $found = Get-Found $A 'Bleeder' 120; if ($found.Count -ge 3) { $found } } -TimeoutSeconds 40
        Assert-True ($bleeders.Count -ge 3) "$A has bleeders loaded ($($bleeders.Count))"
        $script:checked = @($bleeders | Where-Object { Test-Id $_.id } | Select-Object -First 4)
        $start = @{}
        foreach ($bleeder in $checked) {
            $owner = Wait-Until { Get-LockOwner $bleeder.id } -TimeoutSeconds 10
            Assert-True ($owner -in $A, $B) "a player simulates the bleeder $($bleeder.id) (owner: $owner)"
            $bleeder | Add-Member -NotePropertyName owner -NotePropertyValue $owner -Force
            if ($owner -in $A, $B) { $start[$bleeder.id] = [double[]](Invoke-BridgeJson $owner "entity $($bleeder.id)").position }
        }
        Start-Sleep -Seconds 6
        foreach ($bleeder in $checked | Where-Object { $_.owner -in $A, $B }) {
            $action = Invoke-Bridge $bleeder.owner "get $($bleeder.id) Creature.prevBestAction" -AllowError
            Assert-True ($action -match '"\$component"') "the bleeder $($bleeder.id) has an AI action in $($bleeder.owner)'s game ($(if ($action -match '"\$component":\s*"(\w+)"') { $Matches[1] } else { $action }))"
            $end = [double[]](Invoke-BridgeJson $bleeder.owner "entity $($bleeder.id)").position
            $moved = [math]::Sqrt([math]::Pow($end[0] - $start[$bleeder.id][0], 2) + [math]::Pow($end[1] - $start[$bleeder.id][1], 2) + [math]::Pow($end[2] - $start[$bleeder.id][2], 2))
            Assert-True ($moved -gt 0.5) "the bleeder $($bleeder.id) moved ($([math]::Round($moved, 1)) m in 6 s)"
        }
    }
    Assert-NoDiff -Ids @($checked | ForEach-Object { $_.id }) 'no desync reported for the bleeders'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'B1 WarpersAndBleeders' -Keep:$Keep
