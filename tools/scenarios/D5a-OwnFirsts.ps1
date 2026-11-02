<#
.SYNOPSIS
    COOP_FEATURES_SPEC D5a: the PDA's biome line belongs to whoever gets there. When Alice reaches the Kelp Forest,
    Bob's PDA stays silent and gets no log entry; when Bob gets there himself, his own line plays once.

.DESCRIPTION
    The Kelp Forest goal is read from the game's BiomeGoalData (StoryGoalManager.biomeGoalTracker.goalData). Alice
    teleports into the Kelp Forest and stays until her own tracker adds the PDA entry. Bob, at the start area, must
    then have the goal completed (StoryGoalManager.IsGoalComplete) but no PDA entry (PDALog.Contains). Then Bob
    teleports there too, and his own tracker adds the entry. With the bridge's "sounds"/"pdaqueue" commands (A0), the
    voice line is also checked: not queued for Bob while Alice is there, started once after he gets there.

    -OptionOff starts the server with PartnerDiscoveriesSilent=False (written into the save's server.cfg first): the
    negative control, where Bob gets the entry (and the line) as soon as Alice gets there, so this scenario FAILS.

    Starts a fresh world and two games (opens windows unless $env:NITROX_WINDOWLESS=1), unless -NoLaunch. Needs a
    Debug build (dual-client.ps1 -NoBuild is used, so build first).
#>
param([switch]$NoLaunch, [switch]$Keep, [switch]$OptionOff)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$SaveName = 'd5a-own-firsts'
# GotoConsoleCommand location of the Kelp Forest (as in A0-Probes.ps1); checked with Player.GetBiomeString below.
$kelpForestA = '75 -28 -95'
$kelpForestB = '78 -28 -92'

function Test-PdaEntry {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call PDALog.Contains $Key") -eq 'true'
}

function Test-GoalComplete {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call StoryGoalManager.main.IsGoalComplete $Key") -eq 'true'
}

# How often the line's sound was started in the last 10 minutes, or $null without the A0 "sounds" bridge command.
function Get-LineStarts {
    param([string]$Player, [string]$SoundPath)
    if (-not $script:hasSounds -or -not $SoundPath) { return $null }
    $sounds = @(Invoke-BridgeJson $Player "sounds 600 $SoundPath")
    @($sounds | Where-Object { $_ }).Count
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
    if ($OptionOff) {
        $cfg = Get-Content (Join-Path (Join-Path (Get-NitroxRig).SavesDir $SaveName) 'server.cfg') -Raw
        Write-Host "  server.cfg: $(([regex]::Match($cfg, 'PartnerDiscoveriesSilent=\w+')).Value)"
    }
    $script:hasSounds = (Invoke-Bridge $A 'help') -match 'pdaqueue'
    if (-not $hasSounds) { Write-Host '  The bridge has no "sounds"/"pdaqueue" (A0): the voice line is not checked, only the PDA entry' -ForegroundColor Yellow }

    Step 'Find the Kelp Forest goal in the biome goal data' {
        $goals = @(Invoke-BridgeJson $A 'get StoryGoalManager.main.biomeGoalTracker.goalData.goals')
        $script:goal = $goals | Where-Object { $_.biome -eq 'kelpForest' } | Select-Object -First 1
        Assert-True ([bool]$goal) "the biome goal data has a Kelp Forest goal ($($goals.Count) biome goals)"
        $script:key = $goal.key
        Write-Host "  goal $key, type $($goal.goalType), delay $($goal.delay) s, stay $($goal.minStayDuration) s"
        $script:soundPath = $null
        if ($hasSounds) {
            # The line's FMOD event, from the PDA log's mapping (what PDALog.Add queues).
            $entry = Invoke-BridgeJson $A "get PDALog.mapping"
            $script:soundPath = $entry.$key.sound.path
            Write-Host "  sound $soundPath"
        }
        foreach ($player in $A, $B) {
            Assert-True (-not (Test-PdaEntry $player $key)) "$player has no PDA entry for $key yet (fresh world)"
        }
    }

    Step 'Alice reaches the Kelp Forest; Bob stays at the start' {
        Invoke-Bridge $A "teleport $kelpForestA" | Out-Null
        $biome = Wait-Until { $b = Invoke-Bridge $A 'call player Player.GetBiomeString'; if ($b -match 'kelpForest') { $b } } -TimeoutSeconds 20
        Assert-True ([bool]$biome) "$A is in the Kelp Forest biome (biome: $(Invoke-Bridge $A 'call player Player.GetBiomeString'))"
        $timeout = [int]([double]$goal.minStayDuration + [double]$goal.delay + 30)
        $heard = Wait-Until { Test-PdaEntry $A $key } -TimeoutSeconds $timeout
        Assert-True ([bool]$heard) "$A's own tracker added the PDA entry"
        $completed = Wait-Until { Test-GoalComplete $B $key } -TimeoutSeconds 15
        Assert-True ([bool]$completed) "$B's game completed the goal (the partner's StoryGoalExecuted arrived)"
        Start-Sleep -Seconds 2
        Assert-True (-not (Test-PdaEntry $B $key)) "$B has no PDA entry for $key (the partner's line stays silent)"
        $starts = Get-LineStarts $B $soundPath
        if ($null -ne $starts) {
            Assert-Equal 0 $starts "$B's PDA didn't start the line"
            Write-Host "  $B pdaqueue: $(Invoke-Bridge $B 'pdaqueue')"
        }
    }

    Step 'Bob reaches the Kelp Forest himself' {
        Invoke-Bridge $B "teleport $kelpForestB" | Out-Null
        $timeout = [int]([double]$goal.minStayDuration + [double]$goal.delay + 30)
        $heard = Wait-Until { Test-PdaEntry $B $key } -TimeoutSeconds $timeout
        Assert-True ([bool]$heard) "$B's own tracker added the PDA entry"
        $starts = Get-LineStarts $B $soundPath
        if ($null -ne $starts) {
            $starts = Wait-Until { $n = Get-LineStarts $B $soundPath; if ($n -gt 0) { $n } } -TimeoutSeconds 10
            Start-Sleep -Seconds 3
            Assert-Equal 1 (Get-LineStarts $B $soundPath) "$B's PDA started the line once"
        }
        Start-Sleep -Seconds 3
        $aStarts = Get-LineStarts $A $soundPath
        if ($null -ne $aStarts) { Assert-Equal 1 $aStarts "$A heard her line once (not again when $B got there)" }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'D5a OwnFirsts' -Keep:$Keep
