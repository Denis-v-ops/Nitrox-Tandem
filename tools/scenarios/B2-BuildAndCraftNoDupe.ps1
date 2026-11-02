<#
.SYNOPSIS
    COOP_FEATURES_SPEC B2: two players building the same ghost pay for it once, and two players taking the same finished
    item out of a fabricator get one item between them.

.DESCRIPTION
    Building: Alice places a corridor ghost (Builder.BeginAsync / Builder.TryPlace, what the builder menu and the left click
    call; real material costs, no fast building). Both players get the builder tool and enough materials to build it
    alone. Then both build it at the same moment, frame after frame (BuilderTool.Construct, what holding the button calls;
    both requests are sent together) until it's a base. Together they must have paid exactly the ghost's resources
    (Constructable.resourceMap), and both games and the server have the base.

    Fabricator: both players float about 6 m from the escape pod's fabricator (out of its 3 m auto-pickup range). Alice
    crafts a knife (GhostCrafter.Craft, no-cost cheat on in her game), both games show the finished knife, and both
    players click it at the same moment (OnHandClick, both requests sent together). Between them they get one knife, and
    so does the server.

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$BuildSpot = '-40 -42 360', [string]$GhostTechType = 'BaseCorridorI')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

<#
    Sends one command to each game at the same moment (both connections are open before either request is written) and
    returns name -> reply.
#>
function Invoke-BridgeTogether {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Commands)
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $open = @()
    try {
        foreach ($name in $Commands.Keys) {
            $client = New-Object System.Net.Sockets.TcpClient
            $client.Connect('127.0.0.1', [int](Get-BridgeInfo $name).port)
            $client.ReceiveTimeout = 60000
            $open += [pscustomobject]@{ Name = $name; Client = $client; Stream = $client.GetStream() }
        }
        foreach ($connection in $open) {
            $bytes = $utf8.GetBytes($Commands[$connection.Name] + "`n")
            $connection.Stream.Write($bytes, 0, $bytes.Length)
            $connection.Stream.Flush()
        }
        $replies = @{}
        foreach ($connection in $open) {
            $replies[$connection.Name] = (New-Object System.IO.StreamReader($connection.Stream, $utf8)).ReadToEnd()
        }
        $replies
    }
    finally {
        foreach ($connection in $open) { $connection.Client.Close() }
    }
}

function Get-ItemCount {
    param([string]$Player, [string]$TechType)
    @(Get-InventoryItem $Player -TechType $TechType).Count
}

function Get-ServerItems {
    param([string]$TechType)
    @((Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.TechType -eq $TechType } | ForEach-Object { $_.Id })
}

<#
    A finished base piece: the base takes the ghost's id (BuildingHandler.BuildBase).
#>
function Test-IsBase {
    param([string]$Player, [string]$Id)
    [bool](@(Invoke-BridgeJson $Player 'find Base 50') | Where-Object { $_.id -eq $Id })
}

function Test-Loaded {
    param([string]$Player, [string]$Id)
    -not (Invoke-Bridge $Player "entity $Id" -AllowError).StartsWith('ERR ')
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch

    Step 'Both players at the build spot with a builder tool, real material costs' {
        foreach ($player in $A, $B) {
            Invoke-Bridge $player "teleport $BuildSpot" | Out-Null
            Invoke-Bridge $player 'console item builder' | Out-Null
            Invoke-Bridge $player 'call GameModeUtils.DeactivateCheat NoCost' | Out-Null
            if ((Invoke-Bridge $player 'get NoCostConsoleCommand.main.fastBuildCheat') -eq 'true') {
                Invoke-Bridge $player 'console fastbuild' | Out-Null
            }
        }
        Start-Sleep -Seconds 3
        $script:builder = @{}
        foreach ($player in $A, $B) {
            $tool = Wait-Until { Get-InventoryItem $player -TechType 'Builder' | Select-Object -First 1 } -TimeoutSeconds 10
            Assert-True ([bool]$tool) "$player has a builder tool"
            $builder[$player] = $tool.id
            Assert-Equal 'true' (Invoke-Bridge $player 'call GameModeUtils.RequiresIngredients') "$player's game charges materials"
            Assert-Equal 'false' (Invoke-Bridge $player 'get NoCostConsoleCommand.main.fastBuildCheat') "$player builds at normal speed"
        }
    }

    Step "Alice places a $GhostTechType ghost, both games have it" {
        $before = @(Invoke-BridgeJson $A 'find ConstructableBase' | ForEach-Object { $_.id })
        Invoke-Bridge $A "call Builder.BeginAsync $GhostTechType" | Out-Null
        $placeable = $false
        foreach ($pitch in -20, -10, -30, -15, -25, -5, -40) {
            Invoke-Bridge $A "look 0 $pitch" | Out-Null
            $placeable = Wait-Until { (Invoke-Bridge $A 'get Builder.canPlace') -eq 'true' } -TimeoutSeconds 3 -IntervalSeconds 0.5
            if ($placeable) { break }
        }
        if (-not $placeable) {
            Invoke-Bridge $A 'call Builder.End' | Out-Null
            throw "$A can't place $GhostTechType here (Builder.canPlace stays false)"
        }
        Assert-Equal 'true' (Invoke-Bridge $A 'call Builder.TryPlace') "$A placed the ghost"
        $ghost = Wait-Until { @(Invoke-BridgeJson $A 'find ConstructableBase') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 10
        if (-not $ghost) { throw 'no new ghost with an id' }
        $script:ghostId = $ghost.id
        Assert-True ([bool](Wait-Until { Test-Loaded $B $ghostId } -TimeoutSeconds 15)) "$B has the ghost $ghostId"
        $script:resources = @(Invoke-BridgeJson $A "get $ghostId Constructable.resourceMap")
        Write-Host "  ghost $ghostId costs: $($resources -join ', ')"
        Assert-True ($resources.Count -gt 0) 'the ghost costs materials'
    }

    Step 'Both players get enough materials to build it alone' {
        $script:types = @($resources | Sort-Object -Unique)
        foreach ($player in $A, $B) {
            foreach ($type in $types) {
                $count = @($resources | Where-Object { $_ -eq $type }).Count
                Invoke-Bridge $player "console item $type $count" | Out-Null
            }
        }
        Start-Sleep -Seconds 2
        $script:materialsBefore = @{}
        foreach ($player in $A, $B) {
            foreach ($type in $types) {
                $materialsBefore["$player/$type"] = Get-ItemCount $player $type
                Write-Host "  $player has $($materialsBefore["$player/$type"]) $type"
            }
        }
    }

    Step 'Both build the ghost at the same moment until it is built' {
        $deadline = (Get-Date).AddSeconds(90)
        $calls = 0
        do {
            $replies = Invoke-BridgeTogether ([ordered]@{
                $A = "call $($builder[$A]) BuilderTool.Construct $ghostId true false"
                $B = "call $($builder[$B]) BuilderTool.Construct $ghostId true false"
            })
            $calls++
            $built = ($replies[$A] -match 'No Constructable' -and $replies[$B] -match 'No Constructable') -or ($calls % 10 -eq 0 -and (Test-IsBase $A $ghostId))
        } while (-not $built -and (Get-Date) -lt $deadline)
        Write-Host "  $calls rounds; last replies: $A '$($replies[$A].Trim())', $B '$($replies[$B].Trim())'"
        foreach ($player in $A, $B) {
            Assert-True ([bool](Wait-Until { Test-IsBase $player $ghostId } -TimeoutSeconds 10)) "$player's ghost became a base"
        }
        Start-Sleep -Seconds 2
        Assert-True ((Invoke-Bridge 'server' "query $ghostId" -AllowError) -match 'BuildEntity') 'the server has the base'
    }

    Step 'Together they paid for the ghost exactly once' {
        foreach ($type in $types) {
            $cost = @($resources | Where-Object { $_ -eq $type }).Count
            $paid = 0
            foreach ($player in $A, $B) {
                $spent = $materialsBefore["$player/$type"] - (Get-ItemCount $player $type)
                Write-Host "  $player paid $spent $type"
                $paid += $spent
            }
            Assert-Equal $cost $paid "the two players paid $cost $type between them"
        }
    }
    Assert-NoDiff -Ids $ghostId 'no desync reported for the base'

    Step 'Both players about 6 m from the escape pod fabricator' {
        $fabricator = (Invoke-BridgeJson $A 'find Fabricator 1') | Select-Object -First 1
        if (-not $fabricator) {
            $pod = (Invoke-BridgeJson $A 'find EscapePod 1') | Select-Object -First 1
            $p = [double[]]$pod.position
            Invoke-Bridge $A ('teleport {0} {1} {2}' -f $p[0], ($p[1] - 4), ($p[2] + 6)) | Out-Null
            $fabricator = Wait-Until { (Invoke-BridgeJson $A 'find Fabricator 1') | Select-Object -First 1 } -TimeoutSeconds 15
        }
        if (-not $fabricator) { throw "$A finds no fabricator" }
        $script:fabricatorTarget = if ($fabricator.id -match '^[0-9a-f-]{36}$') { $fabricator.id } else { 'find:Fabricator' }
        $f = [double[]]$fabricator.position
        Invoke-Bridge $A ('teleport {0} {1} {2}' -f ($f[0] + 6), ($f[1] - 3), $f[2]) | Out-Null
        Invoke-Bridge $B ('teleport {0} {1} {2}' -f ($f[0] - 6), ($f[1] - 3), $f[2]) | Out-Null
        Start-Sleep -Seconds 3
        Write-Host "  fabricator $fabricatorTarget at $(Format-Vector $f)"
        foreach ($player in $A, $B) {
            Assert-True (Test-Loaded $player $fabricatorTarget) "$player has the fabricator"
            Assert-Equal 'false' (Invoke-Bridge $player "call $fabricatorTarget GhostCrafter.PlayerIsInRange 3") "$player is out of the auto-pickup range"
        }
    }

    Step 'Alice crafts a knife, both games show it finished' {
        $script:knivesBefore = @{ $A = (Get-ItemCount $A 'Knife'); $B = (Get-ItemCount $B 'Knife') }
        $script:serverKnivesBefore = Get-ServerItems 'Knife'
        Invoke-Bridge $A 'call GameModeUtils.ActivateCheat NoCost' | Out-Null
        Invoke-Bridge $A "call $fabricatorTarget GhostCrafter.Craft Knife 1" | Out-Null
        foreach ($player in $A, $B) {
            $done = Wait-Until {
                (Invoke-Bridge $player "get $fabricatorTarget GhostCrafter.logic.craftingTechType") -match 'Knife' -and
                (Invoke-Bridge $player "get $fabricatorTarget GhostCrafter.logic.inProgress") -eq 'false'
            } -TimeoutSeconds 30
            Assert-True ([bool]$done) "$player's fabricator shows the finished knife"
        }
        $script:crafted = [int](Invoke-Bridge $A "get $fabricatorTarget GhostCrafter.logic.numCrafted")
        Write-Host "  the fabricator holds $crafted knife/knives"
        Invoke-Bridge $A 'call GameModeUtils.DeactivateCheat NoCost' | Out-Null
    }

    Step 'Both click the finished knife at the same moment' {
        $replies = Invoke-BridgeTogether ([ordered]@{ $A = "use $fabricatorTarget GhostCrafter"; $B = "use $fabricatorTarget GhostCrafter" })
        Write-Host "  replies: $A '$($replies[$A].Trim())', $B '$($replies[$B].Trim())'"
        Start-Sleep -Seconds 4
        $got = 0
        foreach ($player in $A, $B) {
            $new = (Get-ItemCount $player 'Knife') - $knivesBefore[$player]
            Write-Host "  $player got $new"
            $got += $new
        }
        Assert-Equal $crafted $got "the players got $crafted knife between them"
        $script:newServerKnives = @(Get-ServerItems 'Knife' | Where-Object { $serverKnivesBefore -notcontains $_ })
        Assert-Equal $crafted $newServerKnives.Count 'the server has one new knife per crafted knife'
        foreach ($player in $A, $B) {
            $empty = Wait-Until { (Invoke-Bridge $player "get $fabricatorTarget GhostCrafter.logic.craftingTechType") -match 'None' } -TimeoutSeconds 10
            Assert-True ([bool]$empty) "$player's fabricator is empty again"
        }
    }
    if ($newServerKnives.Count -gt 0) {
        Assert-NoDiff -Ids $newServerKnives 'no desync reported for the knives'
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'B2 BuildAndCraftNoDupe' -Keep:$Keep
