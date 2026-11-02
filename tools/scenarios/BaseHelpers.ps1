<#
    Helpers for scenarios that need a seabase (S5) or a restart (S1, S5). Dot-source after ScenarioHelpers.ps1.

    New-ScenarioBase builds one base piece the way a player does: Builder.BeginAsync (what the builder menu calls),
    aiming with the bridge's "look" until Builder.canPlace, Builder.TryPlace (what the left click calls), then
    Constructable.Construct (what the builder tool calls every frame) until it's built. bobthebuilder gives the builder
    tool, no-cost and fast building.
#>

$script:Invariant = [Globalization.CultureInfo]::InvariantCulture

function ConvertTo-Double {
    param([string]$Text)
    [double]::Parse($Text.Trim('"'), $script:Invariant)
}

<#
    Builds a base piece in front of the player and returns the new base's NitroxId.
#>
function New-ScenarioBase {
    param([Parameter(Mandatory)][string]$Player, [string]$TechType = 'BaseCorridorI')
    Invoke-Bridge $Player 'console bobthebuilder' | Out-Null
    $existing = @(Invoke-BridgeJson $Player 'find Base' | ForEach-Object { $_.id })
    Invoke-Bridge $Player "call Builder.BeginAsync $TechType" | Out-Null
    $placeable = $false
    # Negative pitch looks down; the ghost needs terrain within placeDefaultDistance (8 m) and 1-10 m below it
    foreach ($pitch in -20, -10, -30, -15, -25, -5, -40) {
        Invoke-Bridge $Player "look 0 $pitch" | Out-Null
        $placeable = Wait-Until { (Invoke-Bridge $Player 'get Builder.canPlace') -eq 'true' } -TimeoutSeconds 3 -IntervalSeconds 0.5
        if ($placeable) { break }
    }
    if (-not $placeable) {
        Invoke-Bridge $Player 'call Builder.End' | Out-Null
        throw "$Player can't place $TechType here (Builder.canPlace stays false)"
    }
    Assert-Equal 'true' (Invoke-Bridge $Player 'call Builder.TryPlace') "$Player placed the $TechType ghost"
    $built = Wait-Until {
        $ghost = @(Invoke-BridgeJson $Player 'find ConstructableBase') | Select-Object -First 1
        if ($ghost) { Invoke-Bridge $Player 'call find:ConstructableBase Constructable.Construct' -AllowError | Out-Null }
        $newBase = @(Invoke-BridgeJson $Player 'find Base' | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $existing -notcontains $_.id }) | Select-Object -First 1
        if ($newBase -and -not $ghost) { $newBase.id }
    } -TimeoutSeconds 30 -IntervalSeconds 0.2
    if (-not $built) {
        throw "$Player's $TechType wasn't built"
    }
    $built
}

<#
    Water level of every cell of the base's flood grid (BaseFloodSim.cellWaterLevel) in one game.
#>
function Get-FloodLevels {
    param([Parameter(Mandatory)][string]$Player, [Parameter(Mandatory)][string]$BaseId)
    $levels = Invoke-BridgeJson $Player "get $BaseId BaseFloodSim.cellWaterLevel"
    [double[]]@($levels)
}

function Get-FloodTotal {
    param([Parameter(Mandatory)][string]$Player, [Parameter(Mandatory)][string]$BaseId)
    $total = 0.0
    foreach ($level in Get-FloodLevels $Player $BaseId) { $total += $level }
    $total
}

<#
    Health of the base's cell objects (the leak victims, BaseHullStrength.victims) in one game.
#>
function Get-CellHealth {
    param([Parameter(Mandatory)][string]$Player, [Parameter(Mandatory)][string]$BaseId)
    $count = [int](Invoke-Bridge $Player "get $BaseId BaseHullStrength.victims.Count")
    $health = @()
    for ($i = 0; $i -lt $count; $i++) {
        $health += ConvertTo-Double (Invoke-Bridge $Player "get $BaseId BaseHullStrength.victims[$i].health")
    }
    $health
}

<#
    The server's leak entities under a base: id -> health.
#>
function Get-ServerLeaks {
    param([Parameter(Mandatory)][string]$BaseId)
    $leaks = [ordered]@{}
    foreach ($leak in @((Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.ParentId -eq $BaseId -and $_.Kind -eq 'BaseLeakEntity' })) {
        $leaks[$leak.Id] = $leak.Metadata
    }
    $leaks
}

<#
    Lets the base's lock holder crush the hull, like a base below its crush depth: BaseHullStrength.CrushDamageUpdate (run
    every 2 s by the game, patched by Nitrox to send the leak) only damages a cell while totalStrength < 0. Waits until at
    least -MinLeaks cells leak, then gives the strength back.
#>
function Invoke-HullCrush {
    param([Parameter(Mandatory)][string]$Owner, [Parameter(Mandatory)][string]$BaseId, [double]$MaxCellHealth = 60, [int]$TimeoutSeconds = 40)
    # CrushDamageUpdate deals its damage with LiveMixin.TakeDamage, which the no-damage cheat zeroes
    Invoke-Bridge $Owner 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
    Invoke-Bridge $Owner "set $BaseId BaseHullStrength.totalStrength -1" | Out-Null
    $crushed = Wait-Until { (Get-CellHealth $Owner $BaseId | Measure-Object -Minimum).Minimum -le $MaxCellHealth } -TimeoutSeconds $TimeoutSeconds -IntervalSeconds 1
    Invoke-Bridge $Owner "set $BaseId BaseHullStrength.totalStrength 10" | Out-Null
    Invoke-Bridge $Owner 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
    [bool]$crushed
}

function Get-BaseLockOwner {
    param([Parameter(Mandatory)][string]$BaseId)
    if ((Invoke-Bridge 'server' "query $BaseId") -match 'Owner: (\w+) #') { $Matches[1] }
}

<#
    Server save, stop everything, start the server and both games again from that save.
#>
function Restart-ScenarioWorld {
    param([Parameter(Mandatory)][string]$SaveName, [string[]]$Players = @('Alice', 'Bob'))
    Write-Host (Invoke-Bridge 'server' 'save')
    Start-Sleep -Seconds 3
    # -Stop waits until they are gone.
    & (Join-Path $script:RepoRoot 'tools\dual-client.ps1') -Stop
    if ($LASTEXITCODE -ne 0) { throw 'The games, server or launcher are still running after dual-client.ps1 -Stop' }
    Start-ScenarioGames -Players $Players -SaveName $SaveName -KeepSave
}
