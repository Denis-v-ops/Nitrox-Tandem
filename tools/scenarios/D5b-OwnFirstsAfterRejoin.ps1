<#
.SYNOPSIS
    COOP_FEATURES_SPEC D5b: a biome line the partner heard in an earlier session still plays when you get there
    yourself, and a PDA the partner finds in the world reaches your PDA log without its voice line.

.DESCRIPTION
    1. Alice reaches the Kelp Forest and hears its line; Bob stays at the start (D5a: his game completes it silently).
    2. Both games and the server restart from the save (save, stop, start). After the rejoin Bob has the goal
       completed but no PDA entry, and his biome tracker still has the goal. Alice keeps her entry.
    3. Bob reaches the Kelp Forest and hears the line once; Alice doesn't hear it again.
    4. Alice picks up a survivor PDA lying in the Kelp Forest wreck (a StoryHandTarget with an Encyclopedia goal, used
       through the bridge's "use"). Her game plays the voice log; Bob's databank gets the entry, without his PDA
       starting or queueing any voice line.
    Lines are checked with the bridge's "sounds" (SoundQueue.Play; biome lines carry the key as subtitles) and
    "pdaqueue".

    -OptionOff starts the server with PartnerDiscoveriesSilent=False: the negative control for step 4, where Bob hears
    the found log (and steps 1 and 2 fail as in D5a). On a build without D5b, step 2 fails (Bob gets the partner's
    entry in his restored PDA log and his tracker loses the goal) and step 4 fails (Bob hears the found log).

    Starts a fresh world and two games (opens windows unless $env:NITROX_WINDOWLESS=1), unless -NoLaunch. Needs a
    Debug build (dual-client.ps1 -NoBuild is used, so build first).
#>
param([switch]$NoLaunch, [switch]$Keep, [switch]$OptionOff, [switch]$SkipFound)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$SaveName = 'd5b-own-firsts'
# The biome map calls these kelpForest (see D5a-OwnFirsts.ps1).
$kelpForestA = '100 -30 200'
$kelpForestB = '103 -30 203'
# Next to a survivor PDA in the Kelp Forest wreck (goto spot wreck17; PDA at 63 -38 383, StoryHandTarget goal
# InnerBiomeWreckLore2, an Encyclopedia goal with no delay; read through the bridge 2026-11-02).
$foundSpot = '63 -35 380'

function Test-PdaEntry {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call PDALog.Contains $Key") -eq 'true'
}

function Test-GoalComplete {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call StoryGoalManager.main.IsGoalComplete $Key") -eq 'true'
}

# How often the PDA voice queue started the line in the last 10 minutes (since the game started).
function Get-LineStarts {
    param([string]$Player, [string]$Key)
    @(Invoke-BridgeJson $Player "sounds 600 $Key" | Where-Object { $_ -and $_.source -eq 'SoundQueue.Play' -and $_.path.EndsWith("[$Key]") }).Count
}

function Wait-InBiome {
    param([string]$Player, [string]$Biome)
    $found = Wait-Until { $b = Invoke-Bridge $Player 'call player Player.GetBiomeString'; if ($b -match $Biome) { $b } } -TimeoutSeconds 60
    Assert-True ([bool]$found) "$Player is in the $Biome biome (biome: $(Invoke-Bridge $Player 'call player Player.GetBiomeString'))"
}

try {
    if ($OptionOff -and -not $NoLaunch) {
        $saveDir = Join-Path (Get-NitroxRig).SavesDir $SaveName
        if (Test-Path $saveDir) { Remove-Item -Recurse -Force $saveDir }
        New-Item -ItemType Directory -Force $saveDir | Out-Null
        Set-Content -Path (Join-Path $saveDir 'server.cfg') -Value 'PartnerDiscoveriesSilent=False' -Encoding UTF8
        Write-Host 'Negative control: PartnerDiscoveriesSilent=False' -ForegroundColor Yellow
    }
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch -KeepSave:$OptionOff
    $cfg = Get-Content (Join-Path (Join-Path (Get-NitroxRig).SavesDir $SaveName) 'server.cfg') -Raw -ErrorAction SilentlyContinue
    Write-Host "  server.cfg: $(([regex]::Match("$cfg", 'PartnerDiscoveriesSilent=\w+')).Value)"

    Step 'Find the Kelp Forest goal in the biome goal data' {
        $goals = @(Invoke-BridgeJson $A 'get StoryGoalManager.main.biomeGoalTracker.goalData.goals')
        $script:goal = $goals | Where-Object { $_.biome -eq 'kelpForest' } | Select-Object -First 1
        Assert-True ([bool]$goal) "the biome goal data has a Kelp Forest goal ($($goals.Count) biome goals)"
        $script:key = $goal.key
        $script:stayTimeout = [int]([double]$goal.minStayDuration + [double]$goal.delay + 30)
        Write-Host "  goal $key, type $($goal.goalType), delay $($goal.delay) s, stay $($goal.minStayDuration) s"
    }

    Step 'Alice reaches the Kelp Forest; Bob stays at the start' {
        Invoke-Bridge $A "teleport $kelpForestA" | Out-Null
        Wait-InBiome $A 'kelpForest'
        $heard = Wait-Until { Test-PdaEntry $A $key } -TimeoutSeconds $stayTimeout
        Assert-True ([bool]$heard) "$A's own tracker added the PDA entry"
        $completed = Wait-Until { Test-GoalComplete $B $key } -TimeoutSeconds 15
        Assert-True ([bool]$completed) "$B's game completed the goal (the partner's StoryGoalExecuted arrived)"
        Start-Sleep -Seconds 2
        Assert-True (-not (Test-PdaEntry $B $key)) "$B has no PDA entry for $key before the rejoin (D5a)"
    }

    Step 'Save, restart the server and both games' {
        Invoke-Bridge 'server' 'save' | Out-Null
        Start-Sleep -Seconds 3
        & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop | Out-Null
        # The stopped games' memory comes back over some seconds; dual-client.ps1 refuses to start until it has.
        Wait-Until { (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB -ge 10 } -TimeoutSeconds 90 | Out-Null
        Start-ScenarioGames -Players $A, $B -SaveName $SaveName -KeepSave
        Assert-True (Test-GoalComplete $B $key) "$B's game has the goal completed after the rejoin"
        Assert-True (-not (Test-PdaEntry $B $key)) "$B's restored PDA log has no entry for $key (he hasn't heard it)"
        $tracked = @(Invoke-BridgeJson $B 'get StoryGoalManager.main.biomeGoalTracker.goals' | Where-Object { $_.key -eq $key }).Count
        Assert-Equal 1 $tracked "$B's biome tracker still has $key, so his own visit can play it"
        Assert-True (Test-PdaEntry $A $key) "$A's restored PDA log has her entry for $key"
        $aTracked = @(Invoke-BridgeJson $A 'get StoryGoalManager.main.biomeGoalTracker.goals' | Where-Object { $_.key -eq $key }).Count
        Assert-Equal 0 $aTracked "$A's biome tracker no longer has $key (she heard it)"
    }

    Step 'Bob reaches the Kelp Forest himself after the rejoin' {
        Invoke-Bridge $B "teleport $kelpForestB" | Out-Null
        Wait-InBiome $B 'kelpForest'
        $heard = Wait-Until { Test-PdaEntry $B $key } -TimeoutSeconds $stayTimeout
        Assert-True ([bool]$heard) "$B's own tracker added the PDA entry"
        Wait-Until { (Get-LineStarts $B $key) -gt 0 } -TimeoutSeconds 15 | Out-Null
        Start-Sleep -Seconds 5
        Assert-Equal 1 (Get-LineStarts $B $key) "$B's PDA started the line once"
        Assert-Equal 0 (Get-LineStarts $A $key) "$A's PDA didn't start the line again (she rejoined in the Kelp Forest)"
    }

    if (-not $SkipFound) {
        Step 'Alice picks up a survivor PDA in the world; Bob gets the entry without the voice log' {
            Invoke-Bridge $A "teleport $foundSpot" | Out-Null
            $target = Wait-Until {
                $t = @(Invoke-BridgeJson $A 'find StoryHandTarget') | Select-Object -First 1
                if ($t -and $t.distance -lt 20) { $t }
            } -TimeoutSeconds 60
            Assert-True ([bool]$target) "a StoryHandTarget is loaded near $A ($($target.name))"
            $script:foundKey = Invoke-BridgeJson $A "get $($target.id) StoryHandTarget.goal.key"
            $type = Invoke-BridgeJson $A "get $($target.id) StoryHandTarget.goal.goalType"
            $delay = Invoke-BridgeJson $A "get $($target.id) StoryHandTarget.goal.delay"
            Write-Host "  found PDA goal $foundKey, type $type, delay $delay s"
            # Survivor PDAs are Encyclopedia goals (the databank); a PDA goal would go to the PDA log.
            $has = if ($type -eq 'Encyclopedia') { { param($p) (Invoke-Bridge $p "call PDAEncyclopedia.ContainsEntry $foundKey") -eq 'true' } } else { { param($p) Test-PdaEntry $p $foundKey } }
            Assert-True (-not (& $has $B)) "$B has no entry for $foundKey yet"
            Invoke-Bridge $A 'sounds clear' | Out-Null
            Invoke-Bridge $B 'sounds clear' | Out-Null
            Invoke-Bridge $A "use $($target.id)" | Out-Null
            $aHas = Wait-Until { & $has $A } -TimeoutSeconds ([int]([double]$delay) + 15)
            Assert-True ([bool]$aHas) "$A has the entry for $foundKey"
            $bHas = Wait-Until { & $has $B } -TimeoutSeconds 15
            Assert-True ([bool]$bHas) "$B got the entry for $foundKey (it waits in his PDA)"
            Start-Sleep -Seconds 5
            $aVoice = @(Invoke-BridgeJson $A 'sounds 60' | Where-Object { $_ -and $_.source -eq 'SoundQueue.Play' })
            $bVoice = @(Invoke-BridgeJson $B 'sounds 60' | Where-Object { $_ -and $_.source -eq 'SoundQueue.Play' })
            Assert-True ($aVoice.Count -ge 1) "$A's PDA started the voice log ($(@($aVoice | ForEach-Object path) -join ', '))"
            Assert-Equal 0 $bVoice.Count "$B's PDA started no voice line ($(@($bVoice | ForEach-Object path) -join ', '))"
            $queue = Invoke-BridgeJson $B 'pdaqueue'
            Assert-True (-not $queue.current -and @($queue.queued | Where-Object { $_ }).Count -eq 0) "$B's PDA voice queue is empty ($(Invoke-Bridge $B 'pdaqueue'))"
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'D5b OwnFirstsAfterRejoin' -Keep:$Keep
