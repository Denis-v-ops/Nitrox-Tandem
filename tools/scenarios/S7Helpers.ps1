<#
    Shared parts of the FIX_SPEC S7 scenarios. Dot-source after ScenarioHelpers.ps1.

    The fixture tools\fixtures\s7-machine-base is a world (seed 95311395) with one room at about (-100, -31, 251), built
    through the dev bridge (Builder.BeginAsync / TryPlace / ConstructableBase.Construct, nocost + fastbuild): two water
    filtration machines and a bioreactor. Its EntityData only keeps the player-made entities (inventories, escape pod
    locker); everything generated from the game's batches is regenerated from the seed.
#>

$script:S7 = @{
    Fixture     = Join-Path $script:RepoRoot 'tools\fixtures\s7-machine-base'
    Base        = '28776a84-b005-4e66-b757-ff59c0844faf'
    FiltrationA = '67d7ae57-937b-4334-95c4-7bcbf38f3fee'
    FiltrationB = '0543cc28-c2d7-49a8-b23c-3c25f0a95b76'
    BioReactor  = '5bcff679-3b8b-4763-a811-57026a1fa09b'
    # Middle of the room (its grid is rotated by 180 degrees around the base origin at (-95, -31.2, 256.2))
    Inside      = '-100 -31 251.2'
}

function Get-SavePath {
    param([Parameter(Mandatory)][string]$SaveName)
    $savesDir = (Get-ItemProperty -Path 'HKCU:\SOFTWARE\Nitrox' -Name 'SavesFolderDir' -ErrorAction SilentlyContinue).SavesFolderDir
    if (-not $savesDir) { $savesDir = Join-Path $env:APPDATA 'Nitrox\saves' }
    Join-Path $savesDir $SaveName
}

<#
    Puts the player in the room: teleporting doesn't enter a base (that needs the hatch), so the current sub is set too.
#>
function Enter-S7Base {
    param([Parameter(Mandatory)][string]$Player)
    Invoke-Bridge $Player "teleport $($S7.Inside)" | Out-Null
    Invoke-Bridge $Player "call Player.main.SetCurrentSub $($S7.Base) true" | Out-Null
}

function Initialize-S7Players {
    param([Parameter(Mandatory)][string[]]$Players)
    foreach ($player in $Players) {
        # Machines and base power aren't the subject here: no power needed in any game
        if ((Invoke-Bridge $player 'call GameModeUtils.RequiresPower') -eq 'true') {
            Invoke-Bridge $player 'console noenergy' | Out-Null
        }
        Enter-S7Base $player
    }
}

<#
    Name of the player whose game holds the simulation lock on the base (the producer), from the server.
#>
function Get-S7Producer {
    $query = Invoke-Bridge 'server' "query $($S7.Base)"
    if ($query -match 'Owner: (\S+) #') { return $Matches[1] }
    $null
}

<#
    Items in a container by tech type, as "Salt=2, BigFilteredWater=2", from a game or the server ('server').
    Also returns the ids.
#>
function Get-ContainerContent {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$OwnerId)
    $items = @((Get-BridgeEntities $Name).Entities.Values | Where-Object { $_.ParentId -eq $OwnerId })
    $counts = @{}
    foreach ($item in $items) { $counts[$item.TechType] = 1 + [int]$counts[$item.TechType] }
    [pscustomobject]@{
        Name   = $Name
        Count  = $items.Count
        Counts = $counts
        Text   = (($counts.Keys | Sort-Object | ForEach-Object { "$_=$($counts[$_])" }) -join ', ')
        Ids    = @($items | ForEach-Object { $_.Id })
    }
}

<#
    Saves the world, closes everything and starts it again from that save; both players rejoin.
#>
function Restart-S7World {
    param([Parameter(Mandatory)][string]$SaveName, [Parameter(Mandatory)][string[]]$Players)
    $worldData = Join-Path (Get-SavePath $SaveName) 'WorldData.json'
    $before = (Get-Item $worldData -ErrorAction SilentlyContinue).LastWriteTimeUtc
    Invoke-Bridge 'server' 'save' | Out-Null
    $saved = Wait-Until { (Get-Item $worldData -ErrorAction SilentlyContinue).LastWriteTimeUtc -gt $before } -TimeoutSeconds 60
    if (-not $saved) { throw "The server didn't write $worldData" }
    Start-Sleep -Seconds 3
    & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop
    Start-ScenarioGames -Players $Players -SaveName $SaveName -KeepSave
    Initialize-S7Players $Players
}

<#
    The player quits to the main menu (Nitrox disconnects) and the instant-launch auto-join runs again there.
#>
function Disconnect-S7Player {
    param([Parameter(Mandatory)][string]$Player)
    Invoke-Bridge $Player 'set NitroxPatcher.Patches.Persistent.uGUI_MainMenu_Start_Patch.applied false' | Out-Null
    Invoke-Bridge $Player 'call IngameMenu.main.QuitGame false' | Out-Null
    $left = Wait-Until { -not (Invoke-BridgeJson $Player 'state').joined } -TimeoutSeconds 60
    if (-not $left) { throw "$Player didn't leave the game" }
}

function Wait-S7Rejoined {
    param([Parameter(Mandatory)][string]$Player)
    Wait-BridgeJoined $Player | Out-Null
    Invoke-Bridge $Player 'call GameModeUtils.ActivateCheat NoOxygen' | Out-Null
    Invoke-Bridge $Player 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
    Initialize-S7Players $Player
}

<#
    Line counts of the server logs, to look only at what they gain afterwards (Get-ServerLogLines).
#>
function Get-ServerLogMark {
    $mark = @{}
    Get-ChildItem (Join-Path $env:APPDATA 'Nitrox\logs') -Filter '*_server_*.log' | ForEach-Object { $mark[$_.FullName] = @(Get-Content $_.FullName).Count }
    $mark
}

function Get-ServerLogLines {
    param([Parameter(Mandatory)][hashtable]$Mark, [Parameter(Mandatory)][string]$Pattern)
    Get-ChildItem (Join-Path $env:APPDATA 'Nitrox\logs') -Filter '*_server_*.log' | ForEach-Object {
        $skip = if ($Mark.ContainsKey($_.FullName)) { $Mark[$_.FullName] } else { 0 }
        Get-Content $_.FullName | Select-Object -Skip $skip
    } | Where-Object { $_ -match $Pattern }
}
