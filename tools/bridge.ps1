<#
.SYNOPSIS
    Talks to the Debug-only dev bridge of running Subnautica games and of the Nitrox server.

.DESCRIPTION
    Every Debug game and Debug server opens a localhost-only endpoint (NitroxClient/Debuggers/Bridge,
    Nitrox.Server.Subnautica/Services/DevBridgeService.cs) and writes its port to %APPDATA%\Nitrox\bridge\<name>.json.
    Games started by "instantlaunch" (tools\dual-client.ps1) use their player name, the server uses "server".

    bridge.ps1 <name> <command...>   sends one command and prints the reply (exit 1 if it is an error)
    bridge.ps1 <name> help           lists the commands of that game or of the server
    bridge.ps1 list                  shows the running bridges
    bridge.ps1 diff                  compares every game's loaded entities with the server's records and with each other

    Dot-source it (. tools\bridge.ps1) to use Invoke-Bridge, Invoke-BridgeJson, Wait-BridgeJoined and
    Compare-BridgeEntities from scenario scripts (tools\scenarios\*.ps1).

.EXAMPLE
    tools\bridge.ps1 Alice state
.EXAMPLE
    tools\bridge.ps1 Bob call <nitroxId> LiveMixin.Kill
.EXAMPLE
    tools\bridge.ps1 Bob get StoryGoalCustomEventHandler.main.gunDisabled
.EXAMPLE
    tools\bridge.ps1 server query <nitroxId>
.EXAMPLE
    tools\bridge.ps1 diff -Tolerance 2
#>
param(
    [Parameter(Position = 0)]
    [string]$Name,
    # Command words. Words with spaces are quoted for the bridge.
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$CommandArgs,
    [int]$TimeoutSeconds = 60,
    # diff: metres two positions may differ before it is reported.
    [double]$Tolerance = 1.0,
    # diff: only these games (default: every running game bridge).
    [string[]]$Players,
    # diff: issue kinds not to report (GHOST, MISSING, TECHTYPE, PARENT, POSITION, METADATA).
    [string[]]$Ignore = @(),
    # diff: at most this many lines per issue kind.
    [int]$MaxPerKind = 25
)

$script:BridgeDir = Join-Path $env:APPDATA 'Nitrox\bridge'

function Get-BridgeInfo {
    param([string]$Name)
    $file = Join-Path $script:BridgeDir "$Name.json"
    if (-not (Test-Path $file)) {
        $known = (Get-ChildItem $script:BridgeDir -Filter *.json -ErrorAction SilentlyContinue | ForEach-Object { $_.BaseName }) -join ', '
        throw "No dev bridge named '$Name' ($file missing). Running: $known. Only Debug builds have one."
    }
    Get-Content $file -Raw | ConvertFrom-Json
}

function Join-BridgeArgs {
    param([string[]]$Words)
    ($Words | ForEach-Object {
        $word = [string]$_
        if ($word -match '[\s"]') { '"' + $word.Replace('"', '\"') + '"' } else { $word }
    }) -join ' '
}

<#
    Sends one request line and returns the reply text. Throws if the reply is an error, unless -AllowError.
#>
function Invoke-Bridge {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Command,
        [int]$TimeoutSeconds = 60,
        [switch]$AllowError
    )
    $info = Get-BridgeInfo $Name
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        try {
            $client.Connect('127.0.0.1', [int]$info.port)
        }
        catch {
            throw "Dev bridge '$Name' doesn't answer on port $($info.port) (pid $($info.pid)). Is the game still running?"
        }
        $client.ReceiveTimeout = $TimeoutSeconds * 1000
        $stream = $client.GetStream()
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        $bytes = $utf8.GetBytes($Command.Replace("`r", ' ').Replace("`n", ' ') + "`n")
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush()
        $reader = New-Object System.IO.StreamReader($stream, $utf8)
        $reply = $reader.ReadToEnd()
    }
    finally {
        $client.Close()
    }
    if ($reply.StartsWith('ERR ') -and -not $AllowError) {
        throw "[$Name] $Command -> $($reply.Substring(4))"
    }
    $reply
}

function Invoke-BridgeJson {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Command, [int]$TimeoutSeconds = 60)
    Invoke-Bridge -Name $Name -Command $Command -TimeoutSeconds $TimeoutSeconds | ConvertFrom-Json
}

<#
    Waits until the game's bridge answers and its player has joined and finished the initial sync.
#>
function Wait-BridgeJoined {
    param([Parameter(Mandatory)][string]$Name, [int]$TimeoutSeconds = 300)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $state = Invoke-BridgeJson $Name 'state'
            if ($state.joined -and $state.initialSyncCompleted -and $state.position) {
                return $state
            }
        }
        catch {
            # Bridge not up yet, or game still loading.
        }
        Start-Sleep -Seconds 2
    }
    throw "Timed out waiting for $Name to join"
}

function Get-BridgeEntities {
    param([Parameter(Mandatory)][string]$Name)
    $text = Invoke-Bridge -Name $Name -Command 'entities' -TimeoutSeconds 120
    $entities = @{}
    $player = $null
    foreach ($line in $text -split "`n") {
        $line = $line.TrimEnd("`r")
        if ($line -eq '') { continue }
        if ($line.StartsWith('#player')) { $player = $line.Split("`t")[1]; continue }
        if ($line.StartsWith('#')) { continue }
        $f = $line.Split("`t")
        $position = $null
        if ($f[5] -ne '-') { $position = [double[]]($f[5].Split(',') | ForEach-Object { [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) }) }
        $entities[$f[0]] = [pscustomobject]@{
            Id = $f[0]; TechType = $f[1]; Kind = $f[2]; ParentId = $f[3]; ParentKind = $f[4]; Position = $position; Metadata = $f[6]
        }
    }
    [pscustomobject]@{ Name = $Name; Player = $player; Entities = $entities }
}

function Get-Distance {
    param([double[]]$A, [double[]]$B)
    [math]::Sqrt([math]::Pow($A[0] - $B[0], 2) + [math]::Pow($A[1] - $B[1], 2) + [math]::Pow($A[2] - $B[2], 2))
}

function Get-RunningGameBridges {
    Get-ChildItem $script:BridgeDir -Filter *.json -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -ne 'server' } |
        Where-Object { $info = Get-Content $_.FullName -Raw | ConvertFrom-Json; Get-Process -Id $info.pid -ErrorAction SilentlyContinue } |
        ForEach-Object { $_.BaseName }
}

<#
    Desync detector. Compares each game's loaded entities with the server's records, and games with each other.
    Returns the issues; each has Kind, Where, Id, TechType, Detail.
      GHOST     the game has an entity the server doesn't know
      MISSING   the server has a child (inventory item, battery, module, ...) of an entity the game has loaded, the game doesn't
      TECHTYPE  different tech types
      PARENT    container owner / parent entity differs (items in a container use the container owner, like the server)
      POSITION  more than -Tolerance metres apart (server position is where it last heard the entity was)
      METADATA  persistent state (metadata) differs; the game's side is extracted live, so it can be newer than the server's
#>
function Compare-BridgeEntities {
    param([string[]]$Players, [double]$Tolerance = 1.0)
    if (-not $Players) { $Players = @(Get-RunningGameBridges) }
    if (-not $Players) { throw 'No running game bridges found.' }

    $server = Get-BridgeEntities 'server'
    $childrenByParent = @{}
    foreach ($entity in $server.Entities.Values) {
        if ($entity.ParentId -ne '-') {
            if (-not $childrenByParent.ContainsKey($entity.ParentId)) { $childrenByParent[$entity.ParentId] = New-Object System.Collections.ArrayList }
            [void]$childrenByParent[$entity.ParentId].Add($entity)
        }
    }
    $games = @($Players | ForEach-Object { Get-BridgeEntities $_ })
    $issues = New-Object System.Collections.ArrayList
    function Add-Issue($kind, $where, $id, $techType, $detail) {
        [void]$issues.Add([pscustomobject]@{ Kind = $kind; Where = $where; Id = $id; TechType = $techType; Detail = $detail })
    }

    foreach ($game in $games) {
        foreach ($entity in $game.Entities.Values) {
            $record = $server.Entities[$entity.Id]
            if (-not $record) {
                Add-Issue 'GHOST' $game.Name $entity.Id $entity.TechType "not on the server ($($entity.Kind), parent $($entity.ParentId) [$($entity.ParentKind)])"
                continue
            }
            if ($entity.TechType -ne 'None' -and $record.TechType -ne 'None' -and $entity.TechType -ne $record.TechType) {
                Add-Issue 'TECHTYPE' $game.Name $entity.Id $entity.TechType "server: $($record.TechType)"
            }
            $compareParent = $entity.ParentKind -eq 'container' -or $record.Kind -eq 'InventoryItemEntity' -or ($entity.ParentKind -eq 'ancestor' -and $record.ParentId -ne '-')
            if ($compareParent -and $entity.ParentId -ne $record.ParentId) {
                Add-Issue 'PARENT' $game.Name $entity.Id $entity.TechType "game: $($entity.ParentId) [$($entity.ParentKind)], server: $($record.ParentId) ($($record.Kind))"
            }
            if ($entity.ParentKind -ne 'container' -and $entity.Position -and $record.Position) {
                $distance = Get-Distance $entity.Position $record.Position
                if ($distance -gt $Tolerance) {
                    Add-Issue 'POSITION' $game.Name $entity.Id $entity.TechType ("{0:0.0} m apart (game {1}, server {2})" -f $distance, ($entity.Position -join ','), ($record.Position -join ','))
                }
            }
            if ($entity.Metadata -ne '-' -and $record.Metadata -ne '-' -and $entity.Metadata -ne $record.Metadata) {
                Add-Issue 'METADATA' $game.Name $entity.Id $entity.TechType "game: $($entity.Metadata)  server: $($record.Metadata)"
            }
            # Children the server records under this entity but the game doesn't have. Other players' inventories aren't spawned in a game.
            if ($record.Kind -eq 'PlayerEntity' -and $entity.Id -ne $game.Player) { continue }
            $children = $childrenByParent[$entity.Id]
            if ($children) {
                foreach ($child in $children) {
                    if (-not $game.Entities.ContainsKey($child.Id) -and $child.Kind -ne 'PlayerEntity') {
                        Add-Issue 'MISSING' $game.Name $child.Id $child.TechType "server has it under $($entity.Id) ($($entity.TechType)) as $($child.Kind)"
                    }
                }
            }
        }
    }

    for ($i = 0; $i -lt $games.Count; $i++) {
        for ($j = $i + 1; $j -lt $games.Count; $j++) {
            $a = $games[$i]; $b = $games[$j]
            $where = "$($a.Name)<>$($b.Name)"
            foreach ($entity in $a.Entities.Values) {
                $other = $b.Entities[$entity.Id]
                if (-not $other) { continue }
                if ($entity.TechType -ne $other.TechType) { Add-Issue 'TECHTYPE' $where $entity.Id $entity.TechType "$($b.Name): $($other.TechType)" }
                if ($entity.ParentId -ne $other.ParentId -and -not ($entity.ParentKind -eq '-' -or $other.ParentKind -eq '-')) {
                    Add-Issue 'PARENT' $where $entity.Id $entity.TechType "$($a.Name): $($entity.ParentId) [$($entity.ParentKind)], $($b.Name): $($other.ParentId) [$($other.ParentKind)]"
                }
                if ($entity.ParentKind -ne 'container' -and $other.ParentKind -ne 'container' -and $entity.Position -and $other.Position) {
                    $distance = Get-Distance $entity.Position $other.Position
                    if ($distance -gt $Tolerance) { Add-Issue 'POSITION' $where $entity.Id $entity.TechType ("{0:0.0} m apart" -f $distance) }
                }
                if ($entity.Metadata -ne $other.Metadata) { Add-Issue 'METADATA' $where $entity.Id $entity.TechType "$($a.Name): $($entity.Metadata)  $($b.Name): $($other.Metadata)" }
            }
        }
    }

    Write-Host ("diff: server {0} entities; {1}" -f $server.Entities.Count, (($games | ForEach-Object { "$($_.Name) {0} loaded" -f $_.Entities.Count }) -join ', '))
    $issues
}

# Run as a script (not dot-sourced).
if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    if (-not $Name) {
        Get-Help $MyInvocation.MyCommand.Path -Detailed
        exit 0
    }
    switch ($Name) {
        'list' {
            Get-ChildItem $script:BridgeDir -Filter *.json -ErrorAction SilentlyContinue | ForEach-Object {
                $info = Get-Content $_.FullName -Raw | ConvertFrom-Json
                $alive = [bool](Get-Process -Id $info.pid -ErrorAction SilentlyContinue)
                [pscustomobject]@{ Name = $info.name; Port = $info.port; Pid = $info.pid; Running = $alive }
            } | Format-Table -AutoSize
            exit 0
        }
        'diff' {
            $issues = @(Compare-BridgeEntities -Players $Players -Tolerance $Tolerance | Where-Object { $Ignore -notcontains $_.Kind })
            foreach ($group in ($issues | Group-Object Kind | Sort-Object Name)) {
                Write-Host ("{0}: {1}" -f $group.Name, $group.Count)
                $group.Group | Select-Object -First $MaxPerKind | ForEach-Object { Write-Host ("  [{0}] {1} {2}: {3}" -f $_.Where, $_.Id, $_.TechType, $_.Detail) }
                if ($group.Count -gt $MaxPerKind) { Write-Host "  ... $($group.Count - $MaxPerKind) more" }
            }
            if ($issues.Count -eq 0) { Write-Host 'diff: no mismatches'; exit 0 }
            exit 1
        }
        default {
            try {
                Invoke-Bridge -Name $Name -Command (Join-BridgeArgs $CommandArgs) -TimeoutSeconds $TimeoutSeconds
            }
            catch {
                Write-Host $_.Exception.Message -ForegroundColor Red
                exit 1
            }
        }
    }
}
