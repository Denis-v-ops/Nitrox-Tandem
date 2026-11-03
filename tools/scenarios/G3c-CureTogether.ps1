<#
.SYNOPSIS
    COOP_FEATURES_SPEC G3c: the partner's Sea Emperor babies visit their mother and drop cure balls in the partner's game
    too (FIX_SPEC S17), and the cure starts once both players have taken a cure ball, each with their own cinematic.

.DESCRIPTION
    Fixture as in G3-SeaEmperorTogether.ps1: a fresh world, the game's teleport spots ("warp aquarium" = 320 -1466 -436
    on the platform, "warp incubator" = 278 -1611 -371), an ion crystal powers the incubator, "item hatchingenzymes"
    hatches it. Alice carries the enzymes, so her game simulates the incubator and the five networked babies; Bob's game
    gets them as copies.

    Windowless games don't fire animation events (the baby animators aren't AlwaysAnimate), so the scenario drives those
    links of the chain through the bridge, on both games, as the animation would: the end of each egg's hatch animation
    (IncubatorEggAnimation.OnHatchAnimationEnd), the end of each baby's visit at its mother
    (SeaEmperorBabyCinematicController.OnCinematicModeEnd) and the ball a baby burps (DropEnzymes.SpawnCureBall, then
    ReleaseSpawnedCureBall). Everything between those links runs as in game: the babies swim, the mother starts their visit
    (SeaEmperor.Update), the babies leave and DropEnzymes starts its burp animation.

    1. Both on the platform (G3a), then at the incubator; Alice powers it, both use it (G3b): the eggs hatch on both.
       On Bob's game each of Alice's babies got the id of its egg, as on Alice's.
    2. The hatch animations end on both games: Bob's temporary babies are gone, and Bob's copies of the networked babies
       swim to the mother on Bob's game too (swim target set, registered at the mother).
    3. The mother starts the babies' visit on both games (each baby's cinematic set).
    4. The visit ends: Bob's babies leave on his game too (DropEnzymes enabled, its "Burp_ball" animation starts), i.e.
       his game drops cure balls of its own.
    5. Both infected; each game gets a cure ball from one of its babies. Alice takes hers: no cure, the server waits for
       Bob. Bob takes his: both cure cinematics start, both are cured (Infection_Progress5, infection 0). Neither game
       shows infection 0 while its own cure cinematic still plays.
    6. The babies reach the teleporter on Alice's game and teleport: the server and Bob's game drop them.

    -Natural (for windowed games): the game plays those links itself; the scenario only waits for them, and also checks
    that Bob's copies are hidden while his game plays the hatching, and prints how long the hatching took.

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build
    (dual-client.ps1 -NoBuild is used, so build first).
#>
param([switch]$NoLaunch, [switch]$Keep,
    # Windowed games fire the animation events themselves: don't drive the egg hatch end, the visit's end and the ball
    # through the bridge, wait for the game instead (and check that the partner's copies are hidden while they hatch).
    [switch]$Natural)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$CureGoal = 'Infection_Progress5'

function Test-Goal {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call Story.StoryGoalManager.main.IsGoalComplete $Key") -eq 'true'
}

function Get-GateStatus { (Invoke-Bridge 'server' 'storygate status') -join "`n" }

function Get-Infection {
    param([string]$Player)
    [double](Invoke-Bridge $Player 'get Player.main.infectedMixin.infectedAmount')
}

function Test-InCinematic {
    param([string]$Player)
    (Invoke-Bridge $Player 'get Player.main.cinematicModeActive') -eq 'true'
}

function Move-To {
    param([string]$Player, [double[]]$Spot)
    Invoke-Bridge $Player ("teleport {0} {1} {2}" -f $Spot[0], $Spot[1], $Spot[2]) | Out-Null
}

# The networked babies loaded on a game (the temporary hatching babies have no id of their own: the bridge shows "(in <incubator>)").
function Get-NetworkedBabies {
    param([string]$Player)
    @(Invoke-BridgeJson $Player 'find SeaEmperorBaby 20' | Where-Object { $_ -and $_.id -and -not $_.id.StartsWith('(') })
}

function Get-BabyField {
    param([string]$Player, [string]$Id, [string]$Path)
    try { Invoke-Bridge $Player "get $Id $Path" } catch { "error: $($_.Exception.Message)" }
}

# A cure ball from one of this game's babies, as its burp animation would make it; returns the ball's position.
function New-CureBall {
    param([string]$Player, [string]$BabyId)
    # Only a baby that drops enzymes plays the burp animation whose events make the ball
    if ((Get-BabyField $Player $BabyId 'DropEnzymes.enabled') -ne 'true') { return $null }
    Invoke-Bridge $Player "call $BabyId DropEnzymes.SpawnCureBall" | Out-Null
    Start-Sleep -Seconds 3
    Invoke-Bridge $Player "call $BabyId DropEnzymes.ReleaseSpawnedCureBall" | Out-Null
    $ball = Wait-Until { @(Invoke-BridgeJson $Player 'find EnzymeBall 3') | Where-Object { $_ } | Select-Object -First 1 } -TimeoutSeconds 15
    if (-not $ball) { return $null }
    [double[]]$ball.position
}

# A cure ball on this game: one that is there, else a new one from the babies still there, the last to leave first.
# Infected fish eat balls they touch (EnzymeBall.OnTriggerEnter; the prison's vents release peepers), so one may be gone.
function Get-CureBall {
    param([string]$Player)
    $there = @(Invoke-BridgeJson $Player 'find EnzymeBall 1') | Where-Object { $_ } | Select-Object -First 1
    if ($there) { return [double[]]$there.position }
    $present = @(Get-NetworkedBabies $Player | ForEach-Object { $_.id })
    $order = $script:babyIds | Where-Object { $present -contains $_ } | Sort-Object { [int](Get-BabyField $Player $_ 'SeaEmperorBaby.babyId') } -Descending
    foreach ($id in $order) {
        $ball = New-CureBall $Player $id
        if ($ball) { return $ball }
    }
    $null
}

$babyIds = @()
try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch

    Step 'Fixture: the Emperor on the platform, both at the incubator, the eggs hatch on both games' {
        Move-To $A @(320, -1466, -436)
        Move-To $B @(316, -1466, -441)
        $both = Wait-Until { (Invoke-Bridge $A 'get find:PrecursorAquariumPlatformTrigger PrecursorAquariumPlatformTrigger.triggered') -eq 'true' -and (Invoke-Bridge $B 'get find:PrecursorAquariumPlatformTrigger PrecursorAquariumPlatformTrigger.triggered') -eq 'true' } -TimeoutSeconds 60
        Assert-True ([bool]$both) 'the platform started on both games'
        Move-To $A @(278, -1611, -371)
        Move-To $B @(280, -1611, -373)
        $loaded = Wait-Until { (Invoke-BridgeJson $A 'find Incubator').Count -gt 0 -and (Invoke-BridgeJson $B 'find Incubator').Count -gt 0 } -TimeoutSeconds 60
        Assert-True ([bool]$loaded) 'the incubator is loaded on both games'
        Invoke-Bridge $A 'console item precursorioncrystal' | Out-Null
        Invoke-Bridge $A 'console item hatchingenzymes' | Out-Null
        Start-Sleep -Seconds 2
        Invoke-Bridge $A 'use find:IncubatorActivationTerminal' | Out-Null
        $powered = Wait-Until { (Invoke-Bridge $A 'get find:Incubator Incubator.powered') -eq 'true' -and (Invoke-Bridge $B 'get find:Incubator Incubator.powered') -eq 'true' } -TimeoutSeconds 20
        Assert-True ([bool]$powered) 'the incubator is powered on both games'
        Wait-Until { (Invoke-Bridge $A 'get Player.main.cinematicModeActive') -eq 'false' } -TimeoutSeconds 20 | Out-Null
        Invoke-Bridge $A 'use find:Incubator' | Out-Null
        Start-Sleep -Seconds 2
        Invoke-Bridge $B 'use find:Incubator' | Out-Null
        $hatched = Wait-Until { (Invoke-Bridge $A 'get find:Incubator Incubator.hatched') -eq 'true' -and (Invoke-Bridge $B 'get find:Incubator Incubator.hatched') -eq 'true' } -TimeoutSeconds 20
        Assert-True ([bool]$hatched) 'the eggs hatched on both games'
        # Each egg hatches 1.5-5 s after the hatch (IncubatorEgg.StartHatch)
        $all = Wait-Until { (Get-NetworkedBabies $A).Count -ge 5 -and (Get-NetworkedBabies $B).Count -ge 5 } -TimeoutSeconds 30
        Assert-True ([bool]$all) "five networked babies on both games (Alice $((Get-NetworkedBabies $A).Count), Bob $((Get-NetworkedBabies $B).Count))"
        $script:babyIds = @(Get-NetworkedBabies $A | ForEach-Object { $_.id })
        $eggs = 0..4 | ForEach-Object { Invoke-Bridge $B "get find:Incubator Incubator.eggs[$_].attachPoint.position" }
        Write-Host "  egg attach points on Bob's game: $($eggs -join ' ')"
        $mismatch = @($script:babyIds | Where-Object { (Get-BabyField $A $_ 'SeaEmperorBaby.babyId') -ne (Get-BabyField $B $_ 'SeaEmperorBaby.babyId') })
        foreach ($id in $script:babyIds) { Write-Host "  baby $id id Alice $(Get-BabyField $A $id 'SeaEmperorBaby.babyId') Bob $(Get-BabyField $B $id 'SeaEmperorBaby.babyId')" }
        Assert-Equal 0 $mismatch.Count 'every baby has its egg''s id on Bob''s game too'
        $hidden = @($script:babyIds | Where-Object { (Get-BabyField $B $_ 'Renderer.enabled') -eq 'false' })
        Write-Host "  Bob's copies hidden while his game plays the hatching: $($hidden.Count) of 5"
        if ($Natural) { Assert-Equal 5 $hidden.Count 'Bob''s copies are hidden while his game plays the hatching' }
        $script:hatchedAt = Get-Date
    }

    Step 'The hatch animations end: the babies swim to their mother on both games' {
        if ($Natural) {
            $ended = Wait-Until { @(0..4 | Where-Object { (Invoke-Bridge $B "get find:Incubator Incubator.eggs[$_].animationController.animationActive") -eq 'true' }).Count -eq 0 } -TimeoutSeconds 90 -IntervalSeconds 1
            Write-Host "  the hatching on Bob's game ended $([int]((Get-Date) - $script:hatchedAt).TotalSeconds) s after the babies were there"
            Assert-True ([bool]$ended) 'the hatch animations ended on Bob''s game'
        }
        else {
            foreach ($player in $A, $B) {
                0..4 | ForEach-Object { Invoke-Bridge $player "call find:Incubator Incubator.eggs[$_].animationController.OnHatchAnimationEnd" | Out-Null }
            }
        }
        Start-Sleep -Seconds 2
        $shown = @($script:babyIds | Where-Object { (Get-BabyField $B $_ 'Renderer.enabled') -eq 'true' })
        Assert-Equal 5 $shown.Count 'Bob''s copies are visible once his hatching ended'
        $temporary = @(Invoke-BridgeJson $B 'find SeaEmperorBaby 20' | Where-Object { $_ -and (-not $_.id -or $_.id.StartsWith('(')) })
        Assert-Equal 0 $temporary.Count 'Bob''s temporary hatching babies are gone'
        foreach ($player in $A, $B) {
            $swimming = @($script:babyIds | Where-Object { (Get-BabyField $player $_ 'SeaEmperorBaby.swimToTarget.target') -notmatch '^(null|error)' })
            Assert-Equal 5 $swimming.Count "all five babies swim to their mother on $player's game"
        }
    }

    Step 'The mother starts the babies'' visit on both games' {
        # SeaEmperor.Update: all five registered babies arrived, or 40 s after the last one registered
        foreach ($player in $A, $B) {
            $visit = Wait-Until { @($script:babyIds | Where-Object { (Get-BabyField $player $_ 'SeaEmperorBabyCinematicController.currentCinematic') -match '\w' }).Count -eq 5 } -TimeoutSeconds 90 -IntervalSeconds 3
            Assert-True ([bool]$visit) "the visit started for all five babies on $player's game"
        }
        # During the visit Bob's copies follow his own animation, not Alice's poses
        $following = @($script:babyIds | ForEach-Object { Get-BabyField $B $_ 'RemotelyControlled.enabled' })
        Write-Host "  RemotelyControlled.enabled on Bob's copies during the visit: $($following -join ', ')"
        Assert-Equal 5 @($following | Where-Object { $_ -eq 'false' }).Count 'Bob''s copies play their visit by his own animation'
    }

    Step 'The visit ends: Bob''s babies leave and start dropping cure balls on his game too' {
        foreach ($player in @(if (-not $Natural) { $A, $B })) {
            # The visit's end is an animation event of the visit: only a baby that plays its visit gets it
            foreach ($id in $script:babyIds) {
                if ((Get-BabyField $player $id 'SeaEmperorBabyCinematicController.currentCinematic') -match '\w') {
                    Invoke-Bridge $player "call $id SeaEmperorBabyCinematicController.OnCinematicModeEnd" | Out-Null
                }
            }
        }
        foreach ($player in $A, $B) {
            Wait-Until { @($script:babyIds | Where-Object { (Get-BabyField $player $_ 'DropEnzymes.enabled') -eq 'true' }).Count -eq 5 } -TimeoutSeconds $(if ($Natural) { 240 } else { 1 }) -IntervalSeconds 2 | Out-Null
            $dropping = @($script:babyIds | Where-Object { (Get-BabyField $player $_ 'DropEnzymes.enabled') -eq 'true' })
            Assert-Equal 5 $dropping.Count "all five babies drop enzymes on $player's game"
        }
        # A ball on each game now, from the last baby to leave (they leave 5 s apart by id, and a ball still held when its
        # baby teleports goes with it): what the burp animation's events would do
        if ($Natural) {
            # The babies burp their balls themselves (DropEnzymes, every 7-13 s)
            # A released ball: one that stays where it is for 2 s (a ball still held moves with its baby)
            $released = {
                param($player)
                $first = @(Invoke-BridgeJson $player 'find EnzymeBall 1') | Where-Object { $_ } | Select-Object -First 1
                if (-not $first) { return $null }
                Start-Sleep -Seconds 2
                $second = @(Invoke-BridgeJson $player 'find EnzymeBall 1') | Where-Object { $_ } | Select-Object -First 1
                $moved = [Math]::Abs($first.position[0] - $second.position[0]) + [Math]::Abs($first.position[2] - $second.position[2])
                if ($moved -lt 0.5) { $second }
            }
            $foundA = Wait-Until { & $released $A } -TimeoutSeconds 60
            $foundB = Wait-Until { & $released $B } -TimeoutSeconds 60
            $script:ballA = if ($foundA) { [double[]]$foundA.position }
            $script:ballB = if ($foundB) { [double[]]$foundB.position }
        }
        else {
            $script:ballA = Get-CureBall $A
            $script:ballB = Get-CureBall $B
        }
        Write-Host "  cure balls: Alice's at $(if ($script:ballA) { Format-Vector $script:ballA }), Bob's at $(if ($script:ballB) { Format-Vector $script:ballB })"
        Assert-True ([bool]$script:ballA) 'a cure ball is there on Alice''s game'
        Assert-True ([bool]$script:ballB) 'and one on Bob''s'
        # DropEnzymes.Update starts the burp animation 7-13 s after it starts; its ball is an animation event
        $burp = Wait-Until { @($script:babyIds | Where-Object { ((Invoke-BridgeJson $B "animator $_").parameters.Burp_ball) -eq $true }).Count -ge 1 } -TimeoutSeconds 30 -IntervalSeconds 2
        Assert-True ([bool]$burp) 'a baby on Bob''s game burps a cure ball ("Burp_ball")'
        $remote = @($script:babyIds | ForEach-Object { Get-BabyField $B $_ 'RemotelyControlled.enabled' })
        Write-Host "  RemotelyControlled.enabled on Bob's copies: $($remote -join ', ')"
        Assert-True (@($remote | Where-Object { $_ -eq 'false' }).Count -eq 0) 'Bob''s copies follow Alice''s babies again after the visit'
    }

    Step 'Both infected; Alice takes a cure ball alone: no cure, the server waits for Bob' {
        Invoke-Bridge $A 'set Player.main.infectedMixin.infectedAmount 1' | Out-Null
        Invoke-Bridge $B 'set Player.main.infectedMixin.infectedAmount 1' | Out-Null
        # Each next to their own ball (it may have drifted since)
        foreach ($pair in @(@($A, $script:ballA, 1.5), @($B, $script:ballB, -1.5))) {
            Move-To $pair[0] $pair[1]
            Start-Sleep -Seconds 1
            $now = Get-CureBall $pair[0]
            Write-Host "  $($pair[0])'s ball is now at $(Format-Vector $now)"
            Move-To $pair[0] @($now[0], $now[1], ($now[2] + $pair[2]))
        }
        Start-Sleep -Seconds 2
        Invoke-Bridge $A 'use find:EnzymeBall' | Out-Null
        Start-Sleep -Seconds 4
        Assert-True (-not (Test-InCinematic $A)) 'Alice''s cure doesn''t start'
        Assert-Equal 1 (Get-Infection $A) 'Alice is still infected'
        Assert-True (-not (Test-Goal $A $CureGoal)) "$CureGoal isn't complete"
        $status = try { Get-GateStatus } catch { "$_" }
        Write-Host "  $($status -replace "`n", "`n  ")"
        Assert-True ($status -match 'CURE[^\n]*WAITING[^\n]*waiting for Bob') 'the server''s cure gate waits for Bob'
    }

    Step 'Bob takes his: both cures start together, both are cured' {
        $script:CureMarks = Get-LogMarks @($A, $B) 'bridge-scenario'
        Invoke-Bridge $B 'use find:EnzymeBall' | Out-Null
        $started = Wait-Until { (Test-InCinematic $A) -and (Test-InCinematic $B) } -TimeoutSeconds 10 -IntervalSeconds 0.5
        Assert-True ([bool]$started) 'both cure cinematics play'
        # Neither game may drop the infection while its own cure still plays (the partner's cure relayed early). The game
        # completes the cure goal itself when the cinematic's animation ends, then plays the way out (state Out).
        $early = @()
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $deadline) {
            foreach ($player in $A, $B) {
                $state = Invoke-Bridge $player 'get find:EnzymeBall EnzymeBall.playerCinematicController.state'
                if ($state -match 'In|Update' -and (Get-Infection $player) -eq 0) { $early += $player }
            }
            if ((Test-Goal $A $CureGoal) -and (Test-Goal $B $CureGoal) -and -not (Test-InCinematic $A) -and -not (Test-InCinematic $B)) { break }
            Start-Sleep -Milliseconds 500
        }
        Assert-Equal 0 $early.Count "no game cured its player before its own cinematic ended ($($early -join ', '))"
        $deferred = @($A, $B | Where-Object { ((Get-LogTextSince $script:CureMarks @($_) 'bridge-scenario').Values -join "`n") -match 'The partner''s cure \(Infection_Progress5\) waits' })
        Write-Host "  the partner's relayed cure waited for the own cure cinematic on: $($deferred -join ', ')"
        foreach ($player in $A, $B) {
            Assert-True (Test-Goal $player $CureGoal) "$CureGoal is complete on $player's game"
            Assert-Equal 0 (Get-Infection $player) "$player's infection"
        }
    }

    Step 'The babies teleport away on Alice''s game: the server and Bob''s game drop them' {
        $gone = Wait-Until { (Get-NetworkedBabies $A).Count -eq 0 } -TimeoutSeconds 180 -IntervalSeconds 5
        Assert-True ([bool]$gone) "the babies teleported on Alice's game ($((Get-NetworkedBabies $A).Count) left)"
        $goneB = Wait-Until { (Get-NetworkedBabies $B).Count -eq 0 } -TimeoutSeconds 15
        Assert-True ([bool]$goneB) "and are gone from Bob's game ($((Get-NetworkedBabies $B).Count) left)"
        $known = @($script:babyIds | Where-Object { ((Invoke-Bridge 'server' "query $_") -join "`n") -notmatch 'not found' })
        foreach ($id in $known) { Write-Host "  server: $(Invoke-Bridge 'server' "query $id")" }
        Assert-Equal 0 $known.Count 'the server forgot the teleported babies'
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
    Write-Host $_.ScriptStackTrace
}
Complete-Scenario -Name 'G3c CureTogether' -Keep:$Keep
