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
    param([string[]]$Players = @('Alice', 'Bob'), [string]$SaveName = 'bridge-scenario', [switch]$NoLaunch, [switch]$KeepSave,
        # Save folder or zip (e.g. tools\fixtures\s7-machine-base) to start from instead of a new world. Ignored with -KeepSave.
        [string]$Fixture)
    if (-not $NoLaunch) {
        $savesDir = (Get-ItemProperty -Path 'HKCU:\SOFTWARE\Nitrox' -Name 'SavesFolderDir' -ErrorAction SilentlyContinue).SavesFolderDir
        if (-not $savesDir) { $savesDir = Join-Path $env:APPDATA 'Nitrox\saves' }
        $saveDir = Join-Path $savesDir $SaveName
        if ((Test-Path $saveDir) -and -not $KeepSave) {
            # Only this scenario's own throw-away world.
            Write-Host "Removing the previous scenario world $saveDir"
            Remove-Item -Recurse -Force $saveDir
        }
        $fixtureArgs = @{}
        if ($Fixture -and -not $KeepSave) { $fixtureArgs['Fixture'] = $Fixture }
        & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -SaveName $SaveName -Players $Players -NoBuild -WaitForJoin @fixtureArgs
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
