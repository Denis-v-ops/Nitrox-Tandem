<#
.SYNOPSIS
    Starts a Nitrox server and two (or more) Subnautica clients on this PC that join it automatically.

.DESCRIPTION
    Builds the solution (Debug), optionally copies a fixture save into the Nitrox saves folder, then runs the
    Debug-only "Nitrox.Launcher.exe instantlaunch <save> <players...>". The launcher starts the server for that save
    and one game per player name; each game joins 127.0.0.1:11000 from the main menu by itself.

    With -WaitForJoin it watches the server log until every player has finished joining and exits 0 (PASS) or 1 (FAIL).
    Run it again with -Stop to close everything this script started.

    Needs: Steam running (the games start without the Steam bootstrap, but Steam must be up), about 6 GB free RAM per
    game, and no Nitrox launcher already open.

.EXAMPLE
    tools\dual-client.ps1 -WaitForJoin
.EXAMPLE
    tools\dual-client.ps1 -SaveName charger-test -Fixture fixtures\base-with-charger -Force -WaitForJoin
.EXAMPLE
    tools\dual-client.ps1 -Stop
#>
[CmdletBinding()]
param(
    # Save to start. Created by the launcher if it doesn't exist.
    [string]$SaveName = 'dual-client',
    # One game is started per name.
    [string[]]$Players = @('Alice', 'Bob'),
    # Save folder or save .zip (for example a server backup) to copy to the saves folder as -SaveName.
    [string]$Fixture,
    # With -Fixture: move an existing save with the same name aside (to <name>.replaced-<time>) instead of failing.
    [switch]$Force,
    [switch]$NoBuild,
    # Wait until every player has joined, according to the server log.
    [switch]$WaitForJoin,
    [int]$TimeoutSeconds = 900,
    # Close the launcher, server and games started by the last run of this script, then exit.
    [switch]$Stop,
    # Defaults to the launcher's setting (HKCU\SOFTWARE\Nitrox, SavesFolderDir) or %APPDATA%\Nitrox\saves.
    [string]$SavesDir
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
# instantlaunch only exists in Debug builds (LaunchGameViewModel.HandleInstantLaunchForDevelopment is [Conditional("DEBUG")]).
$launcherExe = Join-Path $repoRoot 'Nitrox.Launcher\bin\Debug\net10.0\Nitrox.Launcher.exe'
$nitroxDir = Join-Path $env:APPDATA 'Nitrox'
$logsDir = Join-Path $nitroxDir 'logs'
$reportsDir = Join-Path $nitroxDir 'reports'
$playerLog = Join-Path $env:USERPROFILE 'AppData\LocalLow\Unknown Worlds\Subnautica\Player.log'
$stateFile = Join-Path $env:TEMP 'nitrox-dual-client.json'
$processNames = @('Subnautica', 'Nitrox.Server.Subnautica', 'Nitrox.Launcher')

function Stop-StartedProcesses {
    if (-not (Test-Path $stateFile)) {
        Write-Host 'Nothing to stop: no previous run recorded.'
        return
    }
    $state = Get-Content $stateFile -Raw | ConvertFrom-Json
    $startedAfter = [datetime]::Parse($state.StartedAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
    # Only processes started by that run: anything older (for example a game you were playing before) is left alone.
    $processes = Get-Process -Name $processNames -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $startedAfter }
    foreach ($process in $processes) {
        Write-Host "Stopping $($process.ProcessName) ($($process.Id))"
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $stateFile
}

if ($Stop) {
    Stop-StartedProcesses
    exit 0
}

if (-not $SavesDir) {
    $SavesDir = (Get-ItemProperty -Path 'HKCU:\SOFTWARE\Nitrox' -Name 'SavesFolderDir' -ErrorAction SilentlyContinue).SavesFolderDir
    if (-not $SavesDir) {
        $SavesDir = Join-Path $nitroxDir 'saves'
    }
}
$saveDir = Join-Path $SavesDir $SaveName

if (Get-Process -Name 'Nitrox.Launcher' -ErrorAction SilentlyContinue) {
    throw 'A Nitrox launcher is already running. Close it (or run with -Stop) first: a second launcher would not start the games.'
}
if (-not (Get-Process -Name 'steam' -ErrorAction SilentlyContinue)) {
    Write-Warning 'Steam is not running. The launcher will try to start it, which can time out.'
}

if (-not $NoBuild) {
    Write-Host 'Building Nitrox.slnx (Debug)...'
    dotnet build (Join-Path $repoRoot 'Nitrox.slnx') -c Debug -v quiet -nologo
    if ($LASTEXITCODE -ne 0) {
        # A cold build sometimes fails with MSB4018 (publicizer file in use); a second run succeeds.
        Write-Host 'Build failed, retrying once...'
        dotnet build (Join-Path $repoRoot 'Nitrox.slnx') -c Debug -v quiet -nologo
        if ($LASTEXITCODE -ne 0) {
            throw 'Build failed.'
        }
    }
}
if (-not (Test-Path $launcherExe)) {
    throw "Debug launcher not found at $launcherExe. Build without -NoBuild."
}

if ($Fixture) {
    $Fixture = (Resolve-Path $Fixture).Path
    if (Test-Path $saveDir) {
        if (-not $Force) {
            throw "Save '$saveDir' already exists. Use -Force to move it aside, or pick another -SaveName."
        }
        $aside = "$saveDir.replaced-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Write-Host "Moving existing save to $aside"
        Move-Item $saveDir $aside
    }
    if ((Get-Item $Fixture).PSIsContainer) {
        Copy-Item $Fixture $saveDir -Recurse
    }
    else {
        Expand-Archive -Path $Fixture -DestinationPath $saveDir
    }
    Write-Host "Copied fixture $Fixture to $saveDir"
}

# Recorded so -Stop and the log search only look at what this run started.
$startedAt = (Get-Date).AddSeconds(-2)
@{ StartedAt = $startedAt.ToString('o'); SaveName = $SaveName; Players = $Players } | ConvertTo-Json | Set-Content $stateFile

Write-Host "Starting: $launcherExe instantlaunch $SaveName $($Players -join ' ')"
Start-Process -FilePath $launcherExe -ArgumentList (@('instantlaunch', $SaveName) + $Players) -WorkingDirectory (Split-Path $launcherExe) | Out-Null

Write-Host ''
Write-Host "Save:          $saveDir"
Write-Host "Nitrox logs:   $logsDir  (launcher-*.log, game*.log per player, <date>_server_${SaveName}_NNN.log)"
Write-Host "Unity log:     $playerLog"
Write-Host "Bug reports:   $reportsDir  (/report or F4 in game)"
Write-Host "Stop all:      tools\dual-client.ps1 -Stop"

if (-not $WaitForJoin) {
    exit 0
}

Write-Host ''
Write-Host "Waiting up to $TimeoutSeconds s for $($Players -join ', ') to join..."
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$joined = @{}
$serverLog = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    if (-not $serverLog) {
        $serverLog = Get-ChildItem -Path $logsDir -Filter "*_server_${SaveName}_*.log" -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -ge $startedAt } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($serverLog) {
            Write-Host "Server log:    $($serverLog.FullName)"
        }
        continue
    }
    # The server keeps its log open for writing, so read it with shared access.
    $stream = [IO.File]::Open($serverLog.FullName, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        $text = (New-Object IO.StreamReader($stream)).ReadToEnd()
    }
    finally {
        $stream.Dispose()
    }
    foreach ($player in $Players) {
        if (-not $joined[$player] -and $text.Contains("Player $player joined successfully")) {
            $joined[$player] = $true
            Write-Host "  $player joined"
        }
        if ($text.Contains("Initial sync timed out for player $player")) {
            Write-Host "FAIL: initial sync timed out for $player"
            exit 1
        }
    }
    if ($joined.Count -eq $Players.Count) {
        Write-Host "PASS: all players joined ($($Players -join ', '))"
        exit 0
    }
    if (-not (Get-Process -Name 'Subnautica' -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $startedAt })) {
        if ((Get-Date) -gt $startedAt.AddMinutes(3)) {
            Write-Host 'FAIL: no game started by this run is running'
            exit 1
        }
    }
}
$missing = $Players | Where-Object { -not $joined[$_] }
Write-Host "FAIL: timed out; not joined: $($missing -join ', ')"
exit 1
