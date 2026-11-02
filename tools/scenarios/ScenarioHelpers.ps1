<#
    Shared helpers for scenario scripts over the dev bridge (tools\bridge.ps1). Dot-source it:
        . (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')

    A scenario: Start-ScenarioGames, then Step blocks that act through Invoke-Bridge and check with Assert-*, then
    Complete-Scenario (prints PASS/FAIL, stops the games unless -Keep, exits 0/1). Every Step ends with a desync check
    (bridge.ps1 diff) whose summary is printed; use Assert-NoDiff for the entities a scenario is about.
#>

. (Join-Path $PSScriptRoot '..\bridge.ps1')

$ErrorActionPreference = 'Stop'
$script:ScenarioFailures = New-Object System.Collections.ArrayList
$script:ScenarioChecks = 0
$script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

<#
    Starts a server with a brand-new world (save "bridge-scenario", replaced every run) and two games that join it,
    or with -NoLaunch uses the games that are already running. Opens game windows on this PC.
#>
function Start-ScenarioGames {
    param([string[]]$Players = @('Alice', 'Bob'), [string]$SaveName = 'bridge-scenario', [switch]$NoLaunch, [switch]$KeepSave)
    if (-not $NoLaunch) {
        $savesDir = (Get-ItemProperty -Path 'HKCU:\SOFTWARE\Nitrox' -Name 'SavesFolderDir' -ErrorAction SilentlyContinue).SavesFolderDir
        if (-not $savesDir) { $savesDir = Join-Path $env:APPDATA 'Nitrox\saves' }
        $saveDir = Join-Path $savesDir $SaveName
        if ((Test-Path $saveDir) -and -not $KeepSave) {
            # Only this scenario's own throw-away world.
            Write-Host "Removing the previous scenario world $saveDir"
            Remove-Item -Recurse -Force $saveDir
        }
        & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -SaveName $SaveName -Players $Players -NoBuild -WaitForJoin
        if ($LASTEXITCODE -ne 0) { throw 'dual-client.ps1 failed: the games did not join' }
    }
    foreach ($player in $Players) {
        $state = Wait-BridgeJoined $player
        Write-Host "$player joined at $(Format-Vector ([double[]]$state.position))"
        # Tests shouldn't die of drowning or creatures while they wait.
        Invoke-Bridge $player 'call GameModeUtils.ActivateCheat NoOxygen' | Out-Null
        Invoke-Bridge $player 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
    }
}

function Step {
    param([Parameter(Mandatory)][string]$Title, [Parameter(Mandatory)][scriptblock]$Body)
    Write-Host ''
    Write-Host "== $Title" -ForegroundColor Cyan
    try {
        & $Body
    }
    catch {
        Add-Failure "$Title threw: $($_.Exception.Message)"
        throw
    }
    finally {
        Show-Diff
    }
}

function Add-Failure {
    param([string]$Message)
    [void]$script:ScenarioFailures.Add($Message)
    Write-Host "  FAIL $Message" -ForegroundColor Red
}

function Assert-True {
    param([bool]$Condition, [Parameter(Mandatory)][string]$Message)
    $script:ScenarioChecks++
    if ($Condition) { Write-Host "  ok   $Message" -ForegroundColor Green } else { Add-Failure $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [Parameter(Mandatory)][string]$Message)
    $shown = "$Actual"
    if ($shown.Length -gt 200) { $shown = $shown.Substring(0, 200) + '...' }
    Assert-True ("$Expected" -eq "$Actual") "$Message (expected '$Expected', got '$shown')"
}

<#
    Polls until the script block returns something truthy; returns that value, or $null on timeout.
#>
function Wait-Until {
    param([Parameter(Mandatory)][scriptblock]$Condition, [int]$TimeoutSeconds = 30, [double]$IntervalSeconds = 1)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $result = & $Condition
            if ($result) { return $result }
        }
        catch {
            # Not there yet (entity not loaded, ...).
        }
        Start-Sleep -Milliseconds ([int]($IntervalSeconds * 1000))
    } while ((Get-Date) -lt $deadline)
    $null
}

$script:LastDiff = @()

function Show-Diff {
    try {
        $script:LastDiff = @(Compare-BridgeEntities -Tolerance 1 -MoverTolerance 15 | Where-Object { -not (Test-KnownNoise $_) })
    }
    catch {
        Write-Host "  diff failed: $($_.Exception.Message)" -ForegroundColor Yellow
        return
    }
    if ($script:LastDiff.Count -eq 0) {
        Write-Host '  diff: no mismatches'
    }
    else {
        Write-Host ('  diff: ' + (($script:LastDiff | Group-Object Kind | Sort-Object Name | ForEach-Object { "$($_.Name) $($_.Count)" }) -join ', '))
    }
}

<#
    No desync reported by the last diff for these entity ids.
#>
function Assert-NoDiff {
    param([Parameter(Mandatory)][string[]]$Ids, [Parameter(Mandatory)][string]$Message)
    $hits = @($script:LastDiff | Where-Object { $Ids -contains $_.Id })
    foreach ($hit in $hits) { Write-Host "    [$($hit.Kind)] [$($hit.Where)] $($hit.Id) $($hit.TechType): $($hit.Detail)" -ForegroundColor Yellow }
    Assert-True ($hits.Count -eq 0) $Message
}

function Get-InventoryItem {
    param([Parameter(Mandatory)][string]$Player, [string]$TechType, [string]$Id)
    (Invoke-BridgeJson $Player 'state').inventory | Where-Object { (-not $TechType -or $_.techType -eq $TechType) -and (-not $Id -or $_.id -eq $Id) }
}

<#
    Log files of a run: the Nitrox game logs (game[<player>]-<date>.log, appended across runs), each game's Unity log
    (Player-<player>.log) and the server logs of the save. Get-LogMarks before the launch, Get-LogTextSince after it:
    only what was written in between is returned (the files are shared with earlier runs and other worktrees).
#>
function Get-ScenarioLogFiles {
    param([string[]]$Players, [string]$SaveName)
    $logsDir = Join-Path $env:APPDATA 'Nitrox\logs'
    $date = Get-Date -Format 'yyyyMMdd'
    $files = @()
    foreach ($player in $Players) {
        $files += Join-Path $logsDir "game[$player]-$date.log"
        $files += Join-Path $logsDir "Player-$player.log"
    }
    $files += Get-ChildItem -Path $logsDir -Filter "*_server_${SaveName}_*.log" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
    $files
}

function Get-LogMarks {
    param([string[]]$Players, [string]$SaveName)
    $marks = @{}
    foreach ($file in Get-ScenarioLogFiles $Players $SaveName) {
        # Each launch recreates the Unity log (Player-<player>.log), so all of it belongs to the run.
        if (-not (Test-Path -LiteralPath $file) -or (Split-Path -Leaf $file) -like 'Player-*') { continue }
        $stream = [IO.File]::Open($file, 'Open', 'Read', 'ReadWrite, Delete')
        try { $marks[$file] = $stream.Length } finally { $stream.Dispose() }
    }
    $marks
}

<#
    Text written to each log since the marks, as a hashtable file -> text.
#>
function Get-LogTextSince {
    param([hashtable]$Marks, [string[]]$Players, [string]$SaveName)
    $texts = @{}
    foreach ($file in Get-ScenarioLogFiles $Players $SaveName) {
        if (-not (Test-Path -LiteralPath $file)) { continue }
        $offset = if ($Marks.ContainsKey($file)) { $Marks[$file] } else { 0 }
        $stream = [IO.File]::Open($file, 'Open', 'Read', 'ReadWrite, Delete')
        try {
            if ($offset -gt $stream.Length) { $offset = 0 }
            $stream.Seek($offset, 'Begin') | Out-Null
            $texts[$file] = (New-Object IO.StreamReader($stream)).ReadToEnd()
        }
        finally {
            $stream.Dispose()
        }
    }
    $texts
}

function Complete-Scenario {
    param([Parameter(Mandatory)][string]$Name, [switch]$Keep)
    Write-Host ''
    $passed = $script:ScenarioFailures.Count -eq 0
    if ($passed) {
        Write-Host "PASS: $Name ($script:ScenarioChecks checks)" -ForegroundColor Green
    }
    else {
        Write-Host "FAIL: $Name ($($script:ScenarioFailures.Count) of $script:ScenarioChecks checks failed)" -ForegroundColor Red
        $script:ScenarioFailures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    }
    if (-not $Keep) {
        & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop
    }
    if ($passed) { exit 0 } else { exit 1 }
}
