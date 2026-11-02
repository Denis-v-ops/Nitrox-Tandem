<#
.SYNOPSIS
    COOP_FEATURES_SPEC B3 (#1811): base power is the same in both games and on the server, survives a restart, and a
    nuclear reactor rod is used up once (not once per game).

.DESCRIPTION
    Starts the fixture world tools\fixtures\s7-machine-base (a room with two filtration machines and a bioreactor) unless
    -NoLaunch. Both players stand in the room; power is required (no noenergy cheat, which freezes PowerSource values).
      1. The base's lock holder (the producer) drains the empty bioreactor's power source to about a third (bridge "set",
         like consumption would). The server stores that value and the partner's game takes it over.
      2. The producer builds a nuclear reactor in the room and puts 2 rods in. Both games are told the first rod is nearly
         used up (BaseNuclearReactor._toConsume) with an empty power source: the reactor swaps it for a depleted rod in
         the producer's game only. Server and both games have 1 depleted rod and 1 rod, with the same ids.
      3. Restart: the bioreactor keeps its stored charge (not refilled to the maximum) in both games, and the reactor still
         has 1 depleted rod and 1 rod everywhere.
    Without B3 the server never hears of the power (step 1 fails), each game swaps its own rod (step 2 fails) and every
    power source is full again after the restart (step 3 fails).
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'b3-base-power')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'S7Helpers.ps1')
. (Join-Path $PSScriptRoot 'BaseHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseEquipmentHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$bio = $S7.BioReactor
# Base consumption (filtration machines) and the sync's own granularity (BasePowerSync: 1 % of the maximum, 5 s)
$tolerance = 15

function Get-Power {
    param([string]$Player, [string]$Id)
    ConvertTo-Double (Invoke-Bridge $Player "get $Id PowerSource.power")
}

# The power the server stores for an entity (PowerSourceMetadata.Power), or $null
function Get-ServerPower {
    param([string]$Id)
    $record = (Get-BridgeEntities 'server').Entities[$Id]
    if ($record -and $record.Metadata -match '"Power":(-?\d+(\.\d+)?(E[-+]?\d+)?)') { return [double]::Parse($Matches[1], $script:Invariant) }
    $null
}

function Initialize-B3Players {
    foreach ($player in $A, $B) {
        if ((Invoke-Bridge $player 'call GameModeUtils.RequiresPower') -ne 'true') {
            Invoke-Bridge $player 'console noenergy' | Out-Null
        }
        Assert-Equal 'true' (Invoke-Bridge $player 'call GameModeUtils.RequiresPower') "$player's game uses power"
        Enter-S7Base $player
    }
    foreach ($player in $A, $B) {
        $found = Wait-Until { Invoke-BridgeJson $player "entity $bio" } -TimeoutSeconds 30
        Assert-True ([bool]$found) "$player has the bioreactor loaded"
    }
}

# Builds a nuclear reactor in the room, trying the free spots around the middle; returns its id
function New-NuclearReactor {
    param([string]$Player)
    Enable-BuildCheats $Player
    $before = @(Invoke-BridgeJson $Player 'find BaseNuclearReactor' | ForEach-Object { $_.id })
    foreach ($yaw in 0, 90, 180, 270, 45, 135, 225, 315) {
        Enter-S7Base $Player
        Invoke-Bridge $Player "look $yaw -50" | Out-Null
        Invoke-Bridge $Player 'call Builder.End' | Out-Null
        Invoke-Bridge $Player 'call Builder.BeginAsync BaseNuclearReactor' | Out-Null
        $canPlace = Wait-Until { (Invoke-Bridge $Player 'get Builder.canPlace') -eq 'true' } -TimeoutSeconds 4 -IntervalSeconds 0.5
        if (-not $canPlace) { continue }
        Assert-Equal 'true' (Invoke-Bridge $Player 'call Builder.TryPlace') "$Player placed a nuclear reactor (looking $yaw)"
        Invoke-Bridge $Player 'call Builder.End' | Out-Null
        $reactor = Wait-Until {
            Invoke-Bridge $Player 'call find:ConstructableBase Constructable.Construct' -AllowError | Out-Null
            @(Invoke-BridgeJson $Player 'find BaseNuclearReactor') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1
        } -TimeoutSeconds 30 -IntervalSeconds 0.3
        if ($reactor) { return $reactor.id }
        throw "$Player's nuclear reactor wasn't built"
    }
    Invoke-Bridge $Player 'call Builder.End' | Out-Null
    throw "$Player found no spot for a nuclear reactor in the room"
}

# Rods in the reactor as "DepletedReactorRod=1, ReactorRod=1" with their ids, from a game or the server
function Assert-ReactorRods {
    param([string]$Reactor, [string]$Expected, [string]$Context)
    $agreed = Wait-Until { @('server', $A, $B | Where-Object { (Get-ContainerContent $_ $Reactor).Text -eq $Expected }).Count -eq 3 } -TimeoutSeconds 30
    $ids = @{}
    foreach ($name in 'server', $A, $B) {
        $content = Get-ContainerContent $name $Reactor
        Assert-Equal $Expected $content.Text "$Context, $name's reactor holds"
        $ids[$name] = ($content.Ids | Sort-Object) -join ','
    }
    Assert-True ($ids[$A] -eq $ids['server'] -and $ids[$B] -eq $ids['server']) "$Context, both games have the server's rods ($($ids['server']))"
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -Fixture $S7.Fixture -NoLaunch:$NoLaunch
    Initialize-B3Players

    Step 'The producer drains the bioreactor: the server and the partner get the value' {
        $script:producer = Get-S7Producer
        Assert-True ($producer -in $A, $B) "one of the players holds the base lock ($producer)"
        $script:partner = if ($producer -eq $A) { $B } else { $A }
        $script:bioMax = ConvertTo-Double (Invoke-Bridge $producer "get $bio PowerSource.maxPower")
        Write-Host "  producer $producer, bioreactor max $bioMax, power now $(Get-Power $producer $bio) / $(Get-Power $partner $bio)"
        $script:target = [math]::Round($bioMax * 0.37)
        Invoke-Bridge $producer "set $bio PowerSource.power $target" | Out-Null
        $stored = Wait-Until { $p = Get-ServerPower $bio; if ($null -ne $p -and [math]::Abs($p - $target) -le $tolerance) { "$p" } } -TimeoutSeconds 20
        Assert-True ([bool]$stored) "the server stores the bioreactor's power (about $target, got '$stored', record $((Get-BridgeEntities 'server').Entities[$bio].Metadata))"
        $mirrored = Wait-Until { [math]::Abs((Get-Power $partner $bio) - $target) -le $tolerance } -TimeoutSeconds 20
        Assert-True ([bool]$mirrored) "$partner's game took the value over ($partner $(Get-Power $partner $bio), $producer $(Get-Power $producer $bio))"
    }

    Step 'A nuclear reactor rod is used up once' {
        $script:nuclear = New-NuclearReactor $producer
        Write-Host "  nuclear reactor $nuclear"
        $seen = Wait-Until { Invoke-BridgeJson $partner "entity $nuclear" } -TimeoutSeconds 30
        Assert-True ([bool]$seen) "$partner has the nuclear reactor with the same id"
        Invoke-Bridge $producer 'console item ReactorRod 2' | Out-Null
        $rods = @(Wait-Until { $r = @((Invoke-BridgeJson $producer 'state').inventory | Where-Object { $_.techType -eq 'ReactorRod' }); if ($r.Count -ge 2) { , $r } } -TimeoutSeconds 15)
        Assert-Equal 2 $rods.Count "$producer has 2 rods"
        $slot = 1
        foreach ($rod in $rods) {
            Assert-Equal 'true' (Invoke-Bridge $producer "call $nuclear BaseNuclearReactor.equipment.AddItem NuclearReactor$slot $($rod.id) false") "$producer put rod $($rod.id) in slot $slot"
            $slot++
        }
        Assert-ReactorRods $nuclear 'ReactorRod=2' 'with 2 rods'
        # Every game is about to use up the first rod (20000 energy each); an empty power source makes the reactor ask for power
        foreach ($player in $A, $B) {
            Invoke-Bridge $player "set $nuclear BaseNuclearReactor._toConsume 19990" | Out-Null
            Invoke-Bridge $player "set $nuclear PowerSource.power 0" | Out-Null
        }
        Start-Sleep -Seconds 5
        Assert-ReactorRods $nuclear 'DepletedReactorRod=1, ReactorRod=1' 'after the first rod ran out'
        foreach ($player in $A, $B) { Invoke-Bridge $player "set $nuclear BaseNuclearReactor._toConsume 0" | Out-Null }
    }

    Step 'Restart: the stored power and rods come back in both games' {
        # Let the last value reach the server (BasePowerSync sends at most every 5 s)
        Start-Sleep -Seconds 7
        $script:savedBio = Get-ServerPower $bio
        Write-Host "  stored before the restart: bioreactor $savedBio (producer's game $(Get-Power $producer $bio))"
        Assert-True ($null -ne $savedBio -and $savedBio -lt $bioMax - 2 * $tolerance) "the server stores a bioreactor value below its maximum ($savedBio of $bioMax)"
        Restart-ScenarioWorld $SaveName $A, $B
        Initialize-B3Players
        foreach ($player in $A, $B) {
            $loaded = Wait-Until { Invoke-BridgeJson $player "entity $nuclear" } -TimeoutSeconds 30
            Assert-True ([bool]$loaded) "$player has the nuclear reactor after the restart"
        }
        foreach ($player in $A, $B) {
            $power = Get-Power $player $bio
            Assert-True ($null -ne $savedBio -and [math]::Abs($power - $savedBio) -le $tolerance) "$player's bioreactor has the stored power ($power, stored $savedBio, max $bioMax)"
        }
        Assert-ReactorRods $nuclear 'DepletedReactorRod=1, ReactorRod=1' 'after the restart'
    }

    Assert-NoDiff -Ids @($bio, $nuclear) 'no desync reported for the bioreactor or the nuclear reactor'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'B3 BasePowerPersists' -Keep:$Keep
