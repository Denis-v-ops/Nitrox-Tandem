<#
.SYNOPSIS
    Starts a Nitrox server and two (or more) Subnautica clients on this PC that join it automatically.

.DESCRIPTION
    Builds the solution (Debug), optionally copies a fixture save into the Nitrox saves folder, then starts the server
    for that save and one game per player name; each game joins the server from the main menu by itself.

    By default the Debug-only "Nitrox.Launcher.exe instantlaunch --save <save> --players <players...>" starts the server
    and the games, which join 127.0.0.1:11000. With -Windowless or -Rig this script starts them itself, without the
    launcher:
      -Windowless  the games run in Unity batch mode: no window, no rendering and no sound, about a quarter of a
                   windowed game's CPU and almost no GPU, but the same RAM. TESTING.md "Windowless games and parallel
                   rigs" lists what differs.
      -Rig <n>     n = 1..9: an isolated rig that can run next to others, with its own Nitrox data folder, port 11000+n
                   and game folder (tools\rig.ps1). Rig 0 is the usual one.
    Both default to $env:NITROX_WINDOWLESS and $env:NITROX_RIG, so a scenario started with them set keeps them when it
    restarts the games.

    With -WaitForJoin it watches the server log until every player has finished joining and exits 0 (PASS) or 1 (FAIL).
    Run it again with -Stop (on the same rig) to close everything this script started; -Stop waits until they're gone.

    Needs: Steam running (the games start without the Steam bootstrap, but Steam must be up), about 4 GB free RAM per
    game, and no Nitrox launcher already open (launcher mode only).

.EXAMPLE
    tools\dual-client.ps1 -WaitForJoin
.EXAMPLE
    tools\dual-client.ps1 -SaveName charger-test -Fixture fixtures\base-with-charger -Force -WaitForJoin
.EXAMPLE
    tools\dual-client.ps1 -Windowless -Rig 2 -WaitForJoin
.EXAMPLE
    tools\dual-client.ps1 -Stop
#>
[CmdletBinding()]
param(
    # Save to start. Created if it doesn't exist.
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
    # Close the launcher, server and games started by the last run of this script on this rig, then exit.
    [switch]$Stop,
    # Games without a window (Unity -batchmode).
    [switch]$Windowless = ($env:NITROX_WINDOWLESS -in @('1', 'true')),
    # 0: the usual rig. 1..9: an isolated rig that can run at the same time as others.
    [int]$Rig = [int]$env:NITROX_RIG,
    # Rig 0 only. Defaults to the launcher's setting (HKCU\SOFTWARE\Nitrox, SavesFolderDir) or %APPDATA%\Nitrox\saves.
    [string]$SavesDir
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'rig.ps1')
$rigInfo = Get-NitroxRig $Rig
$repoRoot = Split-Path -Parent $PSScriptRoot
$launcherDir = Join-Path $repoRoot 'Nitrox.Launcher\bin\Debug\net10.0'
# instantlaunch only exists in Debug builds (LaunchGameViewModel.HandleInstantLaunchForDevelopment is [Conditional("DEBUG")]).
$launcherExe = Join-Path $launcherDir 'Nitrox.Launcher.exe'
$logsDir = $rigInfo.LogsDir
$reportsDir = Join-Path $rigInfo.DataDir 'reports'
$stateFile = $rigInfo.StateFile
$processNames = @('Subnautica', 'Nitrox.Server.Subnautica', 'Nitrox.Launcher')
# Without the launcher: this script starts the server and the games and knows their process ids.
$direct = $Windowless -or $Rig -gt 0

function Get-RecordedProcesses($state) {
    $startedAfter = [datetime]::Parse($state.StartedAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
    if ($state.Direct) {
        # The start time check skips ids that were reused by newer processes.
        @($state.ProcessIds | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue } | Where-Object { $_.StartTime -ge $startedAfter })
    }
    else {
        # Started by the launcher: anything newer than the run (for example not a game you were playing before), except
        # the processes of other rigs, which all get --data-path.
        $otherRigs = @(Get-CimInstance Win32_Process -Filter "Name = 'Subnautica.exe' OR Name = 'Nitrox.Server.Subnautica.exe'" |
            Where-Object { $_.CommandLine -match '--data-path' } | ForEach-Object { $_.ProcessId })
        @(Get-Process -Name $processNames -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $startedAfter -and $_.Id -notin $otherRigs })
    }
}

function Stop-StartedProcesses {
    if (-not (Test-Path $stateFile)) {
        Write-Host 'Nothing to stop: no previous run recorded.'
        return $true
    }
    $processes = Get-RecordedProcesses (Get-Content $stateFile -Raw | ConvertFrom-Json)
    foreach ($process in $processes) {
        Write-Host "Stopping $($process.ProcessName) ($($process.Id))"
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    # Stop-Process returns before they are gone; the next start needs the port, the game folder and (launcher) no launcher.
    $processes | Wait-Process -Timeout 60 -ErrorAction SilentlyContinue
    Remove-Item $stateFile
    $left = @($processes | Where-Object { -not $_.HasExited })
    # An exited process stays listed while a handle to it is open (these objects hold one), and a listed launcher makes
    # the next start refuse.
    $ids = @($processes | ForEach-Object { $_.Id })
    $processes | ForEach-Object { $_.Dispose() }
    $deadline = (Get-Date).AddSeconds(30)
    while ($ids.Count -and (Get-Process -Id $ids -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    if ($left.Count) {
        Write-Warning "Still running after 60 s: $(($left | ForEach-Object { "$($_.ProcessName) ($($_.Id))" }) -join ', ')"
    }
    $left.Count -eq 0
}

if ($Stop) {
    if (Stop-StartedProcesses) { exit 0 } else { exit 1 }
}

if ($Rig -gt 0 -and $SavesDir) {
    throw '-SavesDir is for rig 0; other rigs keep their saves in their own data folder.'
}
if (-not $SavesDir) {
    $SavesDir = $rigInfo.SavesDir
}
$saveDir = Join-Path $SavesDir $SaveName

if ($direct) {
    if ($Rig -eq 0 -and $SavesDir -ne (Join-Path $rigInfo.DataDir 'saves')) {
        throw "Without the launcher the server only finds saves in $($rigInfo.DataDir)\saves, not in $SavesDir. Use -Rig 1..9."
    }
    if (Test-Path $stateFile) {
        $running = Get-RecordedProcesses (Get-Content $stateFile -Raw | ConvertFrom-Json)
        if ($running.Count) {
            throw "Rig $Rig is still running from its last start. Stop it first: tools\dual-client.ps1 -Stop -Rig $Rig"
        }
    }
    $portUser = Get-NetUDPEndpoint -LocalPort $rigInfo.Port -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($portUser) {
        throw "Port $($rigInfo.Port) (rig $Rig) is in use by process $($portUser.OwningProcess). Stop it or use another -Rig."
    }
    # Measured: available RAM drops by about 3.7 GB per game over a scenario, windowless or not. Keep 2 GB for the rest
    # of the PC, which slows to a crawl when it runs out.
    $freeGb = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    $neededGb = 4 * $Players.Count + 2
    if ($freeGb -lt $neededGb) {
        throw ('Only {0:N1} GB RAM free; {1} games need about {2:N1} GB. Stop another rig or use fewer players.' -f $freeGb, $Players.Count, $neededGb)
    }
}
elseif (Get-Process -Name 'Nitrox.Launcher' -ErrorAction SilentlyContinue) {
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
    New-Item -ItemType Directory -Force $SavesDir | Out-Null
    if ((Get-Item $Fixture).PSIsContainer) {
        Copy-Item $Fixture $saveDir -Recurse
    }
    else {
        Expand-Archive -Path $Fixture -DestinationPath $saveDir
    }
    Write-Host "Copied fixture $Fixture to $saveDir"
}

<#
    A rig's own game folder: the 6 GB of assets, the Mono runtime and the game's own save folder are junctions to the
    install; the rest (about 300 MB) is copied, so the rig's Managed folder can hold its own NitroxPatcher.dll.
#>
function Sync-RigGame([string]$source, [string]$target) {
    New-Item -ItemType Directory -Force (Join-Path $target 'Subnautica_Data') | Out-Null
    foreach ($shared in 'MonoBleedingEdge', 'SNAppData', 'Subnautica_Data\StreamingAssets') {
        $link = Join-Path $target $shared
        if (-not (Test-Path $link)) {
            New-Item -ItemType Junction -Path $link -Target (Join-Path $source $shared) | Out-Null
        }
    }
    function Copy-Folder([string]$from, [string]$to, [string[]]$extra) {
        robocopy $from $to /NJH /NJS /NFL /NDL /NP /R:2 /W:1 @extra | Out-Null
        if ($LASTEXITCODE -ge 8) {
            throw "Copying $from to $to failed (robocopy exit $LASTEXITCODE)"
        }
    }
    # Only the files of these two folders: their subfolders are the junctions above and the mirrors below.
    Copy-Folder $source $target @()
    Copy-Folder (Join-Path $source 'Subnautica_Data') (Join-Path $target 'Subnautica_Data') @()
    foreach ($folder in Get-ChildItem (Join-Path $source 'Subnautica_Data') -Directory | Where-Object Name -ne 'StreamingAssets') {
        Copy-Folder $folder.FullName (Join-Path $target "Subnautica_Data\$($folder.Name)") @('/MIR')
    }
}

if ($direct) {
    $gameInstall = Find-SubnauticaInstall
    # The launcher adds this call to the game code (NitroxEntryPatch) when it starts multiplayer; without it the game
    # never loads Nitrox. A singleplayer start from the launcher takes it out again.
    $assemblyBytes = [IO.File]::ReadAllBytes((Join-Path $gameInstall 'Subnautica_Data\Managed\Assembly-CSharp.dll'))
    if (-not [Text.Encoding]::ASCII.GetString($assemblyBytes).Contains('NitroxPatcher')) {
        throw "The game install has no Nitrox entry patch. Start multiplayer from the launcher once (or run tools\dual-client.ps1 without -Windowless and -Rig), then try again."
    }
    $gameDir = $gameInstall
    if ($rigInfo.GameDir) {
        Write-Host "Updating the rig's game folder $($rigInfo.GameDir)"
        Sync-RigGame $gameInstall $rigInfo.GameDir
        $gameDir = $rigInfo.GameDir
    }
    $patcherSource = Join-Path $launcherDir 'lib\net472\NitroxPatcher.dll'
    $patcherTarget = Join-Path $gameDir 'Subnautica_Data\Managed\NitroxPatcher.dll'
    try {
        Copy-Item $patcherSource $patcherTarget -Force
    }
    catch {
        # A running game keeps it open. Fine if it is this very build, otherwise the run would test another build.
        if (-not (Test-Path $patcherTarget) -or (Get-FileHash $patcherSource).Hash -ne (Get-FileHash $patcherTarget).Hash) {
            throw "Can't update $patcherTarget (a game is using it) and it is from another build. Stop that game or use another -Rig."
        }
    }
}

# Recorded so -Stop only closes what this run started.
$startedAt = (Get-Date).AddSeconds(-2)
$runId = [guid]::NewGuid().ToString()
$state = @{ RunId = $runId; StartedAt = $startedAt.ToString('o'); SaveName = $SaveName; Players = $Players; Rig = $Rig; Windowless = [bool]$Windowless
    Direct = $direct; ProcessIds = @() }
$state | ConvertTo-Json | Set-Content $stateFile

# The server appends to an existing log file for the same save and day, so only text written after this point counts.
# (LastWriteTime and Length from a directory listing are stale while the server holds the file open.)
function Read-SharedFile([string]$path, [long]$offset) {
    $stream = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        if ($offset -gt $stream.Length) {
            $offset = 0
        }
        $stream.Seek($offset, 'Begin') | Out-Null
        return (New-Object IO.StreamReader($stream)).ReadToEnd()
    }
    finally {
        $stream.Dispose()
    }
}
function Get-FileLength([string]$path) {
    $stream = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite, Delete')
    try { return $stream.Length } finally { $stream.Dispose() }
}
$serverLogPattern = "*_server_${SaveName}_*.log"
$serverLogOffsets = @{}
foreach ($file in Get-ChildItem -Path $logsDir -Filter $serverLogPattern -ErrorAction SilentlyContinue) {
    $serverLogOffsets[$file.FullName] = Get-FileLength $file.FullName
}

if ($direct) {
    # What the launcher gives the server and the games (ServerEntry.ServerProcess, Steam.CreateSteamGameStartInfo).
    # Start-Process rather than a redirected start: a child that inherits this script's output would keep a caller's
    # pipe (run-suite.ps1 | Out-File) open until it exits.
    $env:NITROX_LAUNCHER_PATH = $launcherDir
    $env:NITROX_HOST_HOME = $env:USERPROFILE
    $env:SteamAppId = '264710'
    $env:SteamGameId = '264710'
    # Unity doesn't create the folder of -logFile.
    New-Item -ItemType Directory -Force $logsDir | Out-Null
    $dataPathArg = if ($Rig -gt 0) { " --data-path `"$($rigInfo.DataDir)`"" } else { '' }
    $serverArgs = "--save `"$SaveName`" --game-path `"$gameInstall`"$dataPathArg --GameServer:ServerPort=$($rigInfo.Port) --embedded"
    Write-Host "Starting: Nitrox.Server.Subnautica.exe $serverArgs"
    $server = Start-Process -FilePath (Join-Path $launcherDir 'Nitrox.Server.Subnautica.exe') -ArgumentList $serverArgs -WorkingDirectory $launcherDir -WindowStyle Hidden -PassThru
    # Saved after every start, so -Stop finds them even if a later start fails.
    $state['ProcessIds'] += $server.Id
    $state | ConvertTo-Json | Set-Content $stateFile
    foreach ($player in $Players) {
        $gameArgs = "--instantlaunch `"$player`" --instantlaunch-port $($rigInfo.Port)$dataPathArg -logFile `"$(Join-Path $logsDir "Player-$player.log")`""
        if ($Windowless) {
            # -nographics is ignored by this Unity player (it still creates a Direct3D device), so it isn't passed.
            $gameArgs = "-batchmode $gameArgs"
        }
        Write-Host "Starting: Subnautica.exe $gameArgs"
        $state['ProcessIds'] += (Start-Process -FilePath (Join-Path $gameDir 'Subnautica.exe') -ArgumentList $gameArgs -WorkingDirectory $gameDir -PassThru).Id
        $state | ConvertTo-Json | Set-Content $stateFile
    }
}
else {
    # ConsoleAppFramework only binds named options here; "instantlaunch <save> <players>" is silently ignored.
    $quotedPlayers = $Players | ForEach-Object { '"' + $_ + '"' }
    $launcherArgs = "instantlaunch --save `"$SaveName`" --players $($quotedPlayers -join ' ')"
    Write-Host "Starting: $launcherExe $launcherArgs"
    Start-Process -FilePath $launcherExe -ArgumentList $launcherArgs -WorkingDirectory $launcherDir | Out-Null
}

Write-Host ''
Write-Host "Rig:           $Rig (port $($rigInfo.Port)$(if ($Windowless) { ', windowless' }))"
Write-Host "Save:          $saveDir"
Write-Host "Nitrox logs:   $logsDir"
Write-Host "               launcher-<date>.log, game[<player>]-<date>.log, <date>_server_${SaveName}_NNN.log,"
Write-Host "               Player-<player>.log (Unity log of each game)"
Write-Host "Bug reports:   $reportsDir  (/report or F4 in game)"
Write-Host "Stop all:      tools\dual-client.ps1 -Stop$(if ($Rig -gt 0) { " -Rig $Rig" })"

if (-not $WaitForJoin) {
    exit 0
}

Write-Host ''
Write-Host "Waiting up to $TimeoutSeconds s for $($Players -join ', ') to join..."
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$joined = @{}
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    # A newer run or -Stop replaced the state file: the logs aren't ours to judge any more.
    if (-not (Test-Path $stateFile) -or (Get-Content $stateFile -Raw | ConvertFrom-Json).RunId -ne $runId) {
        Write-Host 'FAIL: this run was stopped or replaced by a newer run'
        exit 1
    }
    $text = ''
    foreach ($file in Get-ChildItem -Path $logsDir -Filter $serverLogPattern -ErrorAction SilentlyContinue) {
        $offset = 0
        if ($serverLogOffsets.ContainsKey($file.FullName)) {
            $offset = $serverLogOffsets[$file.FullName]
        }
        $text += Read-SharedFile $file.FullName $offset
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
    $gamesRunning = if ($direct) {
        @(Get-RecordedProcesses (Get-Content $stateFile -Raw | ConvertFrom-Json) | Where-Object ProcessName -eq 'Subnautica').Count
    }
    else {
        @(Get-Process -Name 'Subnautica' -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $startedAt }).Count
    }
    if (-not $gamesRunning -and (Get-Date) -gt $startedAt.AddMinutes(3)) {
        Write-Host 'FAIL: no game started by this run is running'
        exit 1
    }
}
$missing = $Players | Where-Object { -not $joined[$_] }
Write-Host "FAIL: timed out; not joined: $($missing -join ', ')"
exit 1
