<#
.SYNOPSIS
    COOP_FEATURES_SPEC D5a: the PDA's biome line belongs to whoever gets there. When Alice reaches the Kelp Forest,
    Bob's PDA stays silent and gets no log entry; when Bob gets there himself, his own line plays once.

.DESCRIPTION
    The Kelp Forest goal is read from the game's BiomeGoalData (StoryGoalManager.biomeGoalTracker.goalData). Alice
    teleports into the Kelp Forest and stays until her own tracker adds the PDA entry. Bob, at the start area, must
    then have the goal completed (StoryGoalManager.IsGoalComplete) but no PDA entry (PDALog.Contains). Then Bob
    teleports there too, and his own tracker adds the entry. With the bridge's "sounds"/"pdaqueue" commands (A0), the
    voice line is checked too: not started or queued for Bob while Alice is there, started once after he gets there,
    and not again for Alice.

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
# Open water that the biome map calls kelpForest (LargeWorld.main.GetBiome, read through the bridge); the
# GotoConsoleCommand "kelpforest" spot (75 -28 -95) maps to safeShallows. Checked with Player.GetBiomeString below.
$kelpForestA = '100 -30 200'
$kelpForestB = '103 -30 203'

function Test-PdaEntry {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call PDALog.Contains $Key") -eq 'true'
}

function Test-GoalComplete {
    param([string]$Player, [string]$Key)
    (Invoke-Bridge $Player "call StoryGoalManager.main.IsGoalComplete $Key") -eq 'true'
}

# How often the PDA voice queue started the line in the last 10 minutes. PDALog.Add queues it with the key as its
# subtitles, which the bridge records as "<sound> [<key>]". $null without the A0 "sounds" bridge command.
function Get-LineStarts {
    param([string]$Player, [string]$Key)
    if (-not $script:hasSounds) { return $null }
    @(Invoke-BridgeJson $Player "sounds 600 $Key" | Where-Object { $_ -and $_.source -eq 'SoundQueue.Play' -and $_.path.EndsWith("[$Key]") }).Count
}

# True if the line waits in the PDA voice queue (bridge "pdaqueue", A0).
function Test-LineQueued {
    param([string]$Player, [string]$Key)
    @((Invoke-BridgeJson $Player 'pdaqueue').queued | Where-Object { $_ -and $_.subtitles -eq $Key }).Count -gt 0
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
    $script:hasSounds = (Invoke-Bridge $A 'help') -match 'pdaqueue'
    if (-not $hasSounds) { Write-Host '  The bridge has no "sounds"/"pdaqueue" (A0): only the PDA entry is checked, not the voice line' -ForegroundColor Yellow }

    Step 'Find the Kelp Forest goal in the biome goal data' {
        $goals = @(Invoke-BridgeJson $A 'get StoryGoalManager.main.biomeGoalTracker.goalData.goals')
        $script:goal = $goals | Where-Object { $_.biome -eq 'kelpForest' } | Select-Object -First 1
        Assert-True ([bool]$goal) "the biome goal data has a Kelp Forest goal ($($goals.Count) biome goals)"
        $script:key = $goal.key
        Write-Host "  goal $key, type $($goal.goalType), delay $($goal.delay) s, stay $($goal.minStayDuration) s"
        foreach ($player in $A, $B) {
            Assert-True (-not (Test-PdaEntry $player $key)) "$player has no PDA entry for $key yet (fresh world)"
        }
    }

    Step 'Alice reaches the Kelp Forest; Bob stays at the start' {
        Invoke-Bridge $A "teleport $kelpForestA" | Out-Null
        $biome = Wait-Until { $b = Invoke-Bridge $A 'call player Player.GetBiomeString'; if ($b -match 'kelpForest') { $b } } -TimeoutSeconds 60 # the pod's "Lifepod" override lingers a moment
        Assert-True ([bool]$biome) "$A is in the Kelp Forest biome (biome: $(Invoke-Bridge $A 'call player Player.GetBiomeString'))"
        $timeout = [int]([double]$goal.minStayDuration + [double]$goal.delay + 30)
        $heard = Wait-Until { Test-PdaEntry $A $key } -TimeoutSeconds $timeout
        Assert-True ([bool]$heard) "$A's own tracker added the PDA entry"
        $completed = Wait-Until { Test-GoalComplete $B $key } -TimeoutSeconds 15
        Assert-True ([bool]$completed) "$B's game completed the goal (the partner's StoryGoalExecuted arrived)"
        Start-Sleep -Seconds 2
        Assert-True (-not (Test-PdaEntry $B $key)) "$B has no PDA entry for $key (the partner's line stays silent)"
        if ($hasSounds) {
            Assert-Equal 0 (Get-LineStarts $B $key) "$B's PDA didn't start the line"
            Assert-True (-not (Test-LineQueued $B $key)) "$B's PDA voice queue doesn't hold the line ($(Invoke-Bridge $B 'pdaqueue'))"
            $aStarts = Wait-Until { $n = Get-LineStarts $A $key; if ($n -gt 0) { $n } } -TimeoutSeconds 15
            Assert-Equal 1 $aStarts "$A's PDA started her own line once"
        }
    }

    Step 'Bob reaches the Kelp Forest himself' {
        Invoke-Bridge $B "teleport $kelpForestB" | Out-Null
        $biome = Wait-Until { $b = Invoke-Bridge $B 'call player Player.GetBiomeString'; if ($b -match 'kelpForest') { $b } } -TimeoutSeconds 60
        Assert-True ([bool]$biome) "$B is in the Kelp Forest biome (biome: $(Invoke-Bridge $B 'call player Player.GetBiomeString'))"
        $tracked = @(Invoke-BridgeJson $B 'get StoryGoalManager.main.biomeGoalTracker.goals' | Where-Object { $_.key -eq $key }).Count
        Write-Host "  $B's biome tracker still has the goal: $tracked"
        $timeout = [int]([double]$goal.minStayDuration + [double]$goal.delay + 30)
        $heard = Wait-Until { Test-PdaEntry $B $key } -TimeoutSeconds $timeout
        Assert-True ([bool]$heard) "$B's own tracker added the PDA entry"
        if ($hasSounds) {
            Wait-Until { (Get-LineStarts $B $key) -gt 0 } -TimeoutSeconds 15 | Out-Null
            Start-Sleep -Seconds 5
            Assert-Equal 1 (Get-LineStarts $B $key) "$B's PDA started the line once"
            Assert-Equal 1 (Get-LineStarts $A $key) "$A's PDA didn't start the line again when $B got there"
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'D5a OwnFirsts' -Keep:$Keep
