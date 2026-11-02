<#
.SYNOPSIS
    Runs every in-game scenario (tools\scenarios\S*.ps1) one after another on the current Debug build: the regression
    pass after integrating fixes into coop (TESTING.md "Game-time budget"). Opens game windows for a long time, unless
    -Windowless.

.DESCRIPTION
    Holds the game-rig lock for the whole run, builds once (unless -NoBuild), runs each scenario in its own PowerShell
    process, and writes every scenario's full output to <OutDir>\<scenario>.log plus a summary.md. Stops leftover games
    between scenarios so one failure can't break the next. -Windowless and -Rig reach the scenarios' dual-client.ps1 calls
    through $env:NITROX_WINDOWLESS and $env:NITROX_RIG (tools\rig.ps1).

.EXAMPLE
    tools\run-suite.ps1
    tools\run-suite.ps1 -Only S6-InteriorStateHolds, S13-CyclopsMovement -NoBuild
    tools\run-suite.ps1 -Windowless -Rig 1 -Only S3-GunDisableSyncs, S8-EmptyToolNoDupe
#>
param(
    [string[]]$Only,
    [string[]]$First = @('S6-InteriorStateHolds'),
    [switch]$NoBuild,
    [string]$OutDir = (Join-Path $env:LOCALAPPDATA "Nitrox\suite-runs\$(Get-Date -Format 'yyyyMMdd-HHmmss')"),
    [string]$Owner = 'suite',
    [switch]$Windowless,
    [int]$Rig = 0
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$scenarioDir = Join-Path $PSScriptRoot 'scenarios'
New-Item -ItemType Directory -Force $OutDir | Out-Null
# Restored at the end, so the caller's shell doesn't stay on this rig and mode.
$callerEnv = @{ Rig = $env:NITROX_RIG; Windowless = $env:NITROX_WINDOWLESS }
$env:NITROX_RIG = "$Rig"
$env:NITROX_WINDOWLESS = if ($Windowless) { '1' } else { '' }

$scenarios = Get-ChildItem $scenarioDir -Filter 'S*.ps1' | Where-Object { $_.BaseName -notmatch 'Helpers$' } | ForEach-Object { $_.BaseName }
if ($Only) { $scenarios = $scenarios | Where-Object { $_ -in $Only } }
$ordered = @($First | Where-Object { $_ -in $scenarios }) + @($scenarios | Where-Object { $_ -notin $First } | Sort-Object { [int]($_ -replace '^S(\d+).*', '$1') }, { $_ })

& (Join-Path $PSScriptRoot 'game-lock.ps1') -Acquire -Owner $Owner
$results = New-Object System.Collections.ArrayList
try {
    if (-not $NoBuild) {
        Write-Host 'Building Debug (-m:1)...'
        dotnet build (Join-Path $repo 'Nitrox.slnx') -m:1 -v q | Out-File (Join-Path $OutDir 'build.log')
        if ($LASTEXITCODE -ne 0) { throw "Build failed, see $OutDir\build.log" }
    }
    foreach ($name in $ordered) {
        $log = Join-Path $OutDir "$name.log"
        $started = Get-Date
        Write-Host "== $name" -ForegroundColor Cyan
        & pwsh -NoProfile -File (Join-Path $scenarioDir "$name.ps1") *>&1 | Out-File $log
        $exit = $LASTEXITCODE
        $summaryLine = (Select-String -Path $log -Pattern '^(PASS|FAIL): ' | Select-Object -Last 1).Line
        if (-not $summaryLine) { $summaryLine = "no result line (exit $exit)" }
        $minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
        Write-Host "   $summaryLine ($minutes min)"
        [void]$results.Add([pscustomobject]@{ Scenario = $name; Passed = ($exit -eq 0); Result = $summaryLine; Minutes = $minutes })
        # Whatever the scenario left running on this rig
        & (Join-Path $PSScriptRoot 'dual-client.ps1') -Stop *>&1 | Out-Null
        Start-Sleep -Seconds 5
    }
}
finally {
    & (Join-Path $PSScriptRoot 'game-lock.ps1') -Release -Owner $Owner
    $env:NITROX_RIG = $callerEnv.Rig
    $env:NITROX_WINDOWLESS = $callerEnv.Windowless
    $commit = (git -C $repo rev-parse --short HEAD)
    $lines = @("# Suite run $(Split-Path -Leaf $OutDir) on $commit (rig $Rig$(if ($Windowless) { ', windowless' }))", '', '| Scenario | Result | Minutes |', '|---|---|---|')
    $lines += $results | ForEach-Object { "| $($_.Scenario) | $($_.Result -replace '\|', '/') | $($_.Minutes) |" }
    $lines | Out-File (Join-Path $OutDir 'summary.md')
    Write-Host ''
    Write-Host "Passed $(@($results | Where-Object Passed).Count) of $($results.Count). Logs: $OutDir"
}
