<#
.SYNOPSIS
    COOP_FEATURES_SPEC G3: the Sea Emperor comes to the aquarium platform only once both players stand on it (3a), and
    the eggs hatch once both have used the incubator (3b). Plus FIX_SPEC S12 in game: a player who is offline while the
    partner takes the cure comes back cured.

.DESCRIPTION
    Fixture: a fresh world, reached with the game's own teleport spots ("warp aquarium" = 320 -1466 -436 is inside the
    platform's trigger; "warp incubator" = 278 -1611 -371). No story goal is needed for any of it: the platform, the
    incubator and the babies are there from the start. The incubator is powered with an ion crystal
    (console item precursorioncrystal, then a click on its CrystalTerminal) and hatched with "item hatchingenzymes".
    The no-oxygen and no-damage cheats keep the players alive at 1,500 m (without them a player drowns there).

    1. Alice teleports onto the platform alone, Bob stays at the pod: the platform doesn't start on her game
       (PrecursorAquariumPlatformTrigger.triggered false, the Emperor's "at_platform" parameter false), the Emperor's
       first words (Precursor_Prison_Aquarium_EmperorLog1) don't play anywhere, the server's gate waits for Bob.
    2. Bob teleports onto the platform: both games start it, the Emperor's animator gets "at_platform" on both, the
       Emperor's first words complete on both and Bob's game plays the line while he's on the platform, the invisible
       walls drop on both after 20 s. (The Emperor's animator is AlwaysAnimate, so its animation events, which play the
       dialog, do fire in windowless games; seen 2026-11-03.)
    3. Both at the incubator; Alice powers it and carries the enzymes. Alice uses it alone: nothing hatches on either
       game, the enzymes stay in her inventory, the server waits for Bob. Bob uses it: the eggs hatch on both games, the
       enzymes are used up, and both games play the hatching music (Bob's also the hatching soundscape).
    4. (unless -SkipCure) S12: both infected, Bob quits; Alice takes a cure ball (spawned through a baby's own
       DropEnzymes.SpawnCureBall, since the babies' animation events don't fire windowless) and is cured
       (Infection_Progress5) once an admin released the cure gate (G3c: locked while Bob is offline). After a restart Bob
       rejoins cured. The cure together is G3c-CureTogether.ps1.

    Passed windowless 2026-11-03 (31 checks). Negative control the same day, with the platform trigger patch removed and
    Incubator_OnHandClick_Patch as on coop before G3 (-SkipCure): 14 of 26 checks fail (the platform starts for Alice
    alone and Bob hears the Emperor at the pod, Bob's own Emperor never arrives, the eggs hatch at Alice's click alone,
    Bob hears no hatching music or soundscape).

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build
    (dual-client.ps1 -NoBuild is used, so build first).
#>
param([switch]$NoLaunch, [switch]$Keep, [switch]$SkipCure)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$SaveName = 'bridge-scenario'
$Platform = 'find:PrecursorAquariumPlatformTrigger'
$DialogGoal = 'Precursor_Prison_Aquarium_EmperorLog1'

function Test-Goal {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call Story.StoryGoalManager.main.IsGoalComplete $Key") -eq 'true'
}

function Get-GateStatus { (Invoke-Bridge 'server' 'storygate status') -join "`n" }

function Test-PlatformTriggered {
    param([string]$Player)
    (Invoke-Bridge $Player "get $Platform PrecursorAquariumPlatformTrigger.triggered") -eq 'true'
}

function Get-AtPlatform {
    param([string]$Player)
    [bool](Invoke-BridgeJson $Player 'animator find:SeaEmperor')[0].parameters.at_platform
}

# How often this game played the PDA voice line (bridge "sounds", recorded as "<sound> [<key>]").
function Get-LinePlays {
    param([string]$Player, [string]$Key)
    @(Invoke-BridgeJson $Player "sounds 600 $Key" | Where-Object { $_ -and $_.source -eq 'SoundQueue.Play' -and $_.path.EndsWith("[$Key]") }).Count
}

function Get-SoundPlays {
    param([string]$Player, [string]$Path, [int]$Seconds = 60)
    @(Invoke-BridgeJson $Player "sounds $Seconds $Path" | Where-Object { $_ -and $_.path -eq $Path }).Count
}

function Get-Inventory {
    param([string]$Player)
    @((Invoke-BridgeJson $Player 'state').inventory | ForEach-Object { $_.techType })
}

function Get-Infection {
    param([string]$Player)
    [double](Invoke-Bridge $Player 'get Player.main.infectedMixin.infectedAmount')
}

function Move-To {
    param([string]$Player, [double[]]$Spot)
    Invoke-Bridge $Player ("teleport {0} {1} {2}" -f $Spot[0], $Spot[1], $Spot[2]) | Out-Null
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch

    Step 'Alice alone on the aquarium platform: the Emperor doesn''t come, the server waits for Bob' {
        Move-To $A @(320, -1466, -436)
        $loaded = Wait-Until { (Invoke-BridgeJson $A "find PrecursorAquariumPlatformTrigger").Count -gt 0 -and (Invoke-BridgeJson $A 'find SeaEmperor').Count -gt 0 } -TimeoutSeconds 60
        Assert-True ([bool]$loaded) 'the platform and the Emperor are loaded on Alice''s game'
        Start-Sleep -Seconds 8
        Assert-True (-not (Test-PlatformTriggered $A)) 'the platform hasn''t started on Alice''s game'
        Assert-True (-not (Get-AtPlatform $A)) 'the Emperor''s animator has no "at_platform" on Alice''s game'
        $status = try { Get-GateStatus } catch { "$_" }
        Write-Host "  $($status -replace "`n", "`n  ")"
        Assert-True ($status -match 'SEA_EMPEROR[^\n]*WAITING[^\n]*waiting for Bob') 'the server''s Sea Emperor gate waits for Bob'
        Start-Sleep -Seconds 20
        Assert-True (-not (Test-Goal $A $DialogGoal)) "the Emperor's first words ($DialogGoal) haven't played on Alice's game"
        Assert-True (-not (Test-Goal $B $DialogGoal)) 'nor on Bob''s, at the pod'
        Assert-Equal 0 (Get-LinePlays $B $DialogGoal) 'Bob''s game didn''t play the line'
    }

    Step 'Bob joins her on the platform: the Emperor comes on both games' {
        Move-To $B @(316, -1466, -441)
        $both = Wait-Until { (Test-PlatformTriggered $A) -and (Test-PlatformTriggered $B) } -TimeoutSeconds 30
        Assert-True ([bool]$both) 'the platform started on both games'
        Assert-True (Get-AtPlatform $A) 'the Emperor''s animator has "at_platform" on Alice''s game'
        Assert-True (Get-AtPlatform $B) 'and on Bob''s'
        $spoke = Wait-Until { (Test-Goal $A $DialogGoal) -and (Test-Goal $B $DialogGoal) } -TimeoutSeconds 60
        Assert-True ([bool]$spoke) "the Emperor's first words ($DialogGoal) completed on both games"
        # The line may wait in the PDA voice queue behind a line about the place (D5a: each player's own).
        $played = Wait-Until { (Get-LinePlays $A $DialogGoal) -ge 1 -and (Get-LinePlays $B $DialogGoal) -ge 1 } -TimeoutSeconds 90 -IntervalSeconds 3
        Assert-True ([bool]$played) 'both games played the line'
        Assert-Equal 1 (Get-LinePlays $B $DialogGoal) 'Bob''s game played it once, on the platform'
        $wallsDown = Wait-Until { (Invoke-Bridge $A "get $Platform PrecursorAquariumPlatformTrigger.invisibleWalls.activeSelf") -eq 'false' -and (Invoke-Bridge $B "get $Platform PrecursorAquariumPlatformTrigger.invisibleWalls.activeSelf") -eq 'false' } -TimeoutSeconds 30
        Assert-True ([bool]$wallsDown) 'the invisible walls dropped on both games'
    }

    Step 'Both at the incubator; Alice powers it and carries the enzymes' {
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
        Assert-True ((Get-Inventory $A) -contains 'HatchingEnzymes') 'Alice carries the hatching enzymes'
    }

    Step 'Alice uses the incubator alone: nothing hatches, the server waits for Bob' {
        Invoke-Bridge $A 'sounds clear' | Out-Null
        Invoke-Bridge $B 'sounds clear' | Out-Null
        Invoke-Bridge $A 'use find:Incubator' | Out-Null
        Start-Sleep -Seconds 4
        Assert-True ((Invoke-Bridge $A 'get find:Incubator Incubator.hatched') -eq 'false') 'the eggs didn''t hatch on Alice''s game'
        Assert-True ((Invoke-Bridge $B 'get find:Incubator Incubator.hatched') -eq 'false') 'nor on Bob''s'
        Assert-True ((Get-Inventory $A) -contains 'HatchingEnzymes') 'Alice still carries the enzymes'
        $status = try { Get-GateStatus } catch { "$_" }
        Write-Host "  $($status -replace "`n", "`n  ")"
        Assert-True ($status -match 'HATCHING[^\n]*WAITING[^\n]*waiting for Bob') 'the server''s hatching gate waits for Bob'
    }

    Step 'Bob uses it too: the eggs hatch on both games, both hear the hatching music' {
        Invoke-Bridge $B 'use find:Incubator' | Out-Null
        $hatched = Wait-Until { (Invoke-Bridge $A 'get find:Incubator Incubator.hatched') -eq 'true' -and (Invoke-Bridge $B 'get find:Incubator Incubator.hatched') -eq 'true' } -TimeoutSeconds 20
        Assert-True ([bool]$hatched) 'the eggs hatched on both games'
        Assert-True (-not ((Get-Inventory $A) -contains 'HatchingEnzymes')) 'Alice''s enzymes are used up'
        $music = Wait-Until { (Get-SoundPlays $A 'event:/env/music/hatch_music') -ge 1 -and (Get-SoundPlays $B 'event:/env/music/hatch_music') -ge 1 } -TimeoutSeconds 10
        Assert-True ([bool]$music) 'the hatching music played on both games'
        Assert-Equal 1 (Get-SoundPlays $B 'event:/env/music/hatch_music') 'Bob heard it once'
        Assert-True ((Get-SoundPlays $B 'event:/player/hatch_eggs') -ge 1) 'Bob''s game played the hatching soundscape too'
    }

    if (-not $SkipCure) {
        Step 'S12: both infected; Bob quits; Alice takes the cure alone' {
            Invoke-Bridge $A 'set Player.main.infectedMixin.infectedAmount 1' | Out-Null
            Invoke-Bridge $B 'set Player.main.infectedMixin.infectedAmount 1' | Out-Null
            Start-Sleep -Seconds 5
            Invoke-Bridge 'server' 'save' | Out-Null
            Invoke-Bridge $B 'call IngameMenu.main.QuitGame false' | Out-Null
            $left = Wait-Until { -not ((Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $B -and $_.online }) } -TimeoutSeconds 60
            Assert-True ([bool]$left) "$B left the game"
            Invoke-Bridge $A 'call find:DropEnzymes DropEnzymes.SpawnCureBall' | Out-Null
            Start-Sleep -Seconds 3
            Invoke-Bridge $A 'call find:DropEnzymes DropEnzymes.ReleaseSpawnedCureBall' | Out-Null
            $ball = Wait-Until { @(Invoke-BridgeJson $A 'find EnzymeBall 3') | Where-Object { $_ } | Select-Object -First 1 } -TimeoutSeconds 15
            Assert-True ([bool]$ball) 'a cure ball is there on Alice''s game'
            $at = [double[]]$ball.position
            Move-To $A @($at[0], $at[1], ($at[2] + 1.5))
            Start-Sleep -Seconds 2
            Invoke-Bridge $A 'use find:EnzymeBall' | Out-Null
            # G3c: the cure waits for both; with Bob offline it is locked until an admin releases it
            Start-Sleep -Seconds 4
            Assert-True (-not (Test-Goal $A 'Infection_Progress5')) 'Alice''s cure waits while Bob is offline'
            $status = try { Get-GateStatus } catch { "$_" }
            Assert-True ($status -match 'CURE[^\n]*LOCKED[^\n]*waiting for Bob') 'the server''s cure gate is locked, waiting for Bob'
            Invoke-Bridge 'server' 'storygate release cure' | Out-Null
            $cured = Wait-Until { (Test-Goal $A 'Infection_Progress5') -and (Get-Infection $A) -eq 0 } -TimeoutSeconds 60
            Assert-True ([bool]$cured) "after the release Alice's cure finished: Infection_Progress5 complete, infection $(Get-Infection $A)"
        }

        Step 'S12: after a restart Bob rejoins cured' {
            Invoke-Bridge 'server' 'save' | Out-Null
            Start-Sleep -Seconds 3
            & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop | Out-Null
            Start-Sleep -Seconds 5
            Start-ScenarioGames -Players $A, $B -SaveName $SaveName -KeepSave
            Start-Sleep -Seconds 5
            Assert-Equal 0 (Get-Infection $B) 'Bob''s infection after rejoining'
            Assert-True (Test-Goal $B 'Infection_Progress5') 'Bob''s game has the cure goal'
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
    Write-Host $_.ScriptStackTrace
}
Complete-Scenario -Name 'G3 SeaEmperorTogether' -Keep:$Keep
