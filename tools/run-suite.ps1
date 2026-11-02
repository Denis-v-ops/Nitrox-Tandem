<#
.SYNOPSIS
    Runs every in-game scenario (tools\scenarios\S*.ps1 and the co-op feature ones like B2-*.ps1, T2-*.ps1) one after another on the current Debug build: the regression
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
    [int]$Rig = 0,
    # Seconds the rig is left free between scenarios, so agents' short runs can go in between (0 = hold it throughout)
    [int]$YieldSeconds = 30
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$scenarioDir = Join-Path $PSScriptRoot 'scenarios'
New-Item -ItemType Directory -Force $OutDir | Out-Null
# Restored at the end, so the caller's shell doesn't stay on this rig and mode.
$callerEnv = @{ Rig = $env:NITROX_RIG; Windowless = $env:NITROX_WINDOWLESS }
$env:NITROX_RIG = "$Rig"
$env:NITROX_WINDOWLESS = if ($Windowless) { '1' } else { '' }

# Fix scenarios (S<n>-…) and co-op feature scenarios (<item>-…, e.g. B2-, D5a-, T2-). A0-Probes is a probe run, not a check.
$scenarios = Get-ChildItem $scenarioDir -Filter '*.ps1' |
    Where-Object { $_.BaseName -match '^[A-Z]\d+[a-z]?-' -and $_.BaseName -notmatch 'Helpers$' -and $_.BaseName -notmatch '^A0-' } |
    ForEach-Object { $_.BaseName }
if ($Only) { $scenarios = $scenarios | Where-Object { $_ -in $Only } }
# S scenarios by number, then the feature scenarios by name
$ordered = @($First | Where-Object { $_ -in $scenarios }) + @($scenarios | Where-Object { $_ -notin $First } |
    Sort-Object { if ($_ -match '^S(\d+)') { [int]$Matches[1] } else { [int]::MaxValue } }, { $_ })

$commit = (git -C $repo rev-parse --short HEAD)
& (Join-Path $PSScriptRoot 'game-lock.ps1') -Acquire -Owner $Owner
$results = New-Object System.Collections.ArrayList
try {
    if (-not $NoBuild) {
        Write-Host 'Building Debug (-m:1)...'
        dotnet build (Join-Path $repo 'Nitrox.slnx') -m:1 -v q -p:UseSharedCompilation=false | Out-File (Join-Path $OutDir 'build.log')
        if ($LASTEXITCODE -ne 0) { throw "Build failed, see $OutDir\build.log" }
    }
    foreach ($name in $ordered) {
        $log = Join-Path $OutDir "$name.log"
        $started = Get-Date
        # Longer than game-lock's stale limit in total, so renew the lock before each scenario
        & (Join-Path $PSScriptRoot 'game-lock.ps1') -Refresh -Owner $Owner | Out-Null
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
        # Let a waiting run (game-lock -Acquire polls every 20 s) take the rig in between. Safe: every launch copies its
        # own NitroxPatcher.dll into the game folder (dual-client.ps1), and the suite's build stays in this checkout.
        if ($YieldSeconds -gt 0 -and $name -ne $ordered[-1]) {
            & (Join-Path $PSScriptRoot 'game-lock.ps1') -Release -Owner $Owner | Out-Null
            Start-Sleep -Seconds $YieldSeconds
            & (Join-Path $PSScriptRoot 'game-lock.ps1') -Acquire -Owner $Owner | Out-Null
        }
    }
}
finally {
    if ((& (Join-Path $PSScriptRoot 'game-lock.ps1') -Status) -match "'$Owner'") { & (Join-Path $PSScriptRoot 'game-lock.ps1') -Release -Owner $Owner }
    $env:NITROX_RIG = $callerEnv.Rig
    $env:NITROX_WINDOWLESS = $callerEnv.Windowless
    $lines = @("# Suite run $(Split-Path -Leaf $OutDir) on $commit (rig $Rig$(if ($Windowless) { ', windowless' }))", '', '| Scenario | Result | Minutes |', '|---|---|---|')
    $lines += $results | ForEach-Object { "| $($_.Scenario) | $($_.Result -replace '\|', '/') | $($_.Minutes) |" }
    $lines | Out-File (Join-Path $OutDir 'summary.md')
    Write-Host ''
    Write-Host "Passed $(@($results | Where-Object Passed).Count) of $($results.Count). Logs: $OutDir"
}
