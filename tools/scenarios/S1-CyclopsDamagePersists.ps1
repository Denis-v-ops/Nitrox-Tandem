<#
.SYNOPSIS
    FIX_SPEC S1 (#2220, #2230): Cyclops damage points, fires and health are the same for both players, a repair by
    one player is kept, and all of it survives a server restart.

.DESCRIPTION
    Alice spawns a Cyclops (console "sub cyclops"). The player holding its simulation lock damages it through
    LiveMixin.TakeDamage in small hits until a fire breaks out (SubFire.OnTakeDamage) and the hull is below 75 %;
    CyclopsExternalDamageManager then opens damage points. Both games must show the same damage points (same index in
    CyclopsExternalDamageManager.damagePoints, same NitroxId), the same fires and the same hull health, and so must the
    server's records. Bob then welds one damage point to full (LiveMixin.AddHealth, what Welder.Weld calls), which
    must close it for Alice too. The server saves, everything is stopped and started again from that save (server
    restart, both players rejoin), and the same checks run again: nothing repaired comes back, nothing is lost.

    Starts a fresh world "s1-cyclops" and two games (opens windows), unless -NoLaunch. Needs a Debug build.
#>
param(
    [switch]$NoLaunch,
    [switch]$Keep,
    # With -NoLaunch: the save the running server has (the restart step starts it again)
    [string]$SaveName = 's1-cyclops',
    # Where it happens; another spot lets several scenarios share one world
    [string]$spawnSpot = '-200 -40 -420'
)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
# Open water over the Grassy Plateaus edge, far below the surface and above the sea floor (a Cyclops is 50 m long).

function Get-CyclopsId {
    param([string]$Player)
    $sub = @(Invoke-BridgeJson $Player 'find SubRoot') | Where-Object { $_.techType -eq 'Cyclops' -or $_.name -like 'Cyclops*' } | Select-Object -First 1
    if ($sub -and $sub.id -match '^[0-9a-f-]{36}$') { $sub.id }
}

function Get-Health {
    param([string]$Player)
    [double]::Parse((Invoke-Bridge $Player "get $cyclopsId LiveMixin.health"), [Globalization.CultureInfo]::InvariantCulture)
}

<#
    Damage point index -> NitroxId (or '' when it has none) for every open damage point in one game.
#>
function Get-GameDamagePoints {
    param([string]$Player)
    $points = [ordered]@{}
    for ($i = 0; $i -lt $script:pointCount; $i++) {
        $point = Invoke-BridgeJson $Player "get $cyclopsId CyclopsExternalDamageManager.damagePoints[$i].gameObject"
        if ($point.active) { $points["$i"] = "$($point.id)" }
    }
    $points
}

<#
    The server's damage point and fire entities under the Cyclops: @{ Points = index -> id; Fires = id -> "Room/Node"; Health }.
#>
function Get-ServerDamage {
    $server = Get-BridgeEntities 'server'
    $points = [ordered]@{}
    $fires = [ordered]@{}
    foreach ($child in @($server.Entities.Values | Where-Object { $_.ParentId -eq $cyclopsId })) {
        if ($child.Kind -eq 'CyclopsDamagePointEntity') {
            $raw = Invoke-Bridge 'server' "query $($child.Id)"
            if ($raw -match 'DamagePointIndex: (\d+)') { $points[$Matches[1]] = $child.Id }
        }
        elseif ($child.Kind -eq 'CyclopsFireEntity') {
            $raw = Invoke-Bridge 'server' "query $($child.Id)"
            if ($raw -match 'Room: (\w+), NodeIndex: (\d+)') { $fires[$child.Id] = "$($Matches[1])/$($Matches[2])" }
        }
    }
    $health = $null
    if ((Invoke-Bridge 'server' "query $cyclopsId") -match 'Health: ([0-9]+(?:[.,][0-9]+)?)') { $health = [double]::Parse($Matches[1].Replace(',', '.'), [Globalization.CultureInfo]::InvariantCulture) }
    [pscustomobject]@{ Points = $points; Fires = $fires; Health = $health }
}

function Format-Points {
    param($Points)
    (($Points.Keys | Sort-Object { [int]$_ } | ForEach-Object { "$_" }) -join ',')
}

function Get-LockOwner {
    if ((Invoke-Bridge 'server' "query $cyclopsId") -match 'Owner: (\w+) #') { $Matches[1] }
}

<#
    Both games show the same open damage points as each other, with the server's ids; same fires; same health.
#>
function Assert-DamageInSync {
    param([string]$When)
    # Damage changes reach the other game and the server through packets: give them a moment to agree.
    $synced = Wait-Until {
        $s = Get-ServerDamage
        $a = Get-GameDamagePoints $A
        $b = Get-GameDamagePoints $B
        if ((Format-Points $a) -eq (Format-Points $b) -and (Format-Points $a) -eq (Format-Points $s.Points)) { $true }
    } -TimeoutSeconds 20 -IntervalSeconds 2
    $script:server = Get-ServerDamage
    $script:alicePoints = Get-GameDamagePoints $A
    $script:bobPoints = Get-GameDamagePoints $B
    Write-Host "  $When damage points: Alice [$(Format-Points $alicePoints)] Bob [$(Format-Points $bobPoints)] server [$(Format-Points $server.Points)]"
    Assert-Equal (Format-Points $alicePoints) (Format-Points $bobPoints) "$When Alice and Bob have the same damage points open"
    Assert-Equal (Format-Points $server.Points) (Format-Points $alicePoints) "$When the server has the same damage points as the games"
    foreach ($index in $server.Points.Keys) {
        Assert-Equal $server.Points[$index] $alicePoints[$index] "$When Alice's damage point $index has the server's id"
        Assert-Equal $server.Points[$index] $bobPoints[$index] "$When Bob's damage point $index has the server's id"
    }

    $serverFires = $server.Fires.Count
    foreach ($player in $A, $B) {
        Assert-Equal $serverFires (Invoke-Bridge $player "call $cyclopsId SubFire.GetFireCount") "$When $player has as many fires as the server ($($server.Fires.Values -join ', '))"
        foreach ($fireId in $server.Fires.Keys) {
            Assert-True ([bool](Invoke-Bridge $player "entity $fireId" -AllowError | Where-Object { -not $_.StartsWith('ERR') })) "$When $player has fire $fireId ($($server.Fires[$fireId]))"
        }
    }

    $healthA = Get-Health $A
    $healthB = Get-Health $B
    Write-Host "  $When health: Alice $healthA, Bob $healthB, server $($server.Health)"
    Assert-True ([Math]::Abs($healthA - $healthB) -lt 1) "$When Alice and Bob see the same hull health ($healthA vs $healthB)"
    Assert-True ($null -ne $server.Health -and [Math]::Abs($healthA - $server.Health) -lt 1) "$When the server stores the same hull health ($($server.Health))"
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    Step 'Alice spawns a Cyclops in open water, Bob comes along' {
        Invoke-Bridge $A "teleport $spawnSpot" | Out-Null
        Invoke-Bridge $A 'look 0 0' | Out-Null
        Start-Sleep -Seconds 3
        Invoke-Bridge $A 'console sub cyclops' | Out-Null
        $script:cyclopsId = Wait-Until { Get-CyclopsId $A } -TimeoutSeconds 60
        Assert-True ([bool]$cyclopsId) 'Alice has a Cyclops'
        Write-Host "  Cyclops $cyclopsId"
        Invoke-Bridge $B "teleport $spawnSpot" | Out-Null
        $seen = Wait-Until { Invoke-BridgeJson $B "entity $cyclopsId" } -TimeoutSeconds 60
        Assert-True ([bool]$seen) 'Bob sees the Cyclops'
        $script:pointCount = [int](Invoke-Bridge $A "get $cyclopsId CyclopsExternalDamageManager.damagePoints.Length")
        Write-Host "  the Cyclops has $pointCount damage point slots"
        $script:maxHealth = [double]::Parse((Invoke-Bridge $A "get $cyclopsId LiveMixin.maxHealth"), [Globalization.CultureInfo]::InvariantCulture)
        $script:owner = Wait-Until { Get-LockOwner } -TimeoutSeconds 20
        Write-Host "  simulation lock: $owner"
        Assert-True ($owner -in $A, $B) 'one of the players simulates the Cyclops'
    }

    Step 'The lock holder damages the Cyclops until it burns' {
        # The scenario's no-damage cheat zeroes all damage the cheating game deals (DamageSystem.CalculateDamage)
        Invoke-Bridge $owner 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
        for ($hit = 0; $hit -lt 60; $hit++) {
            $health = Get-Health $owner
            $fires = [int](Invoke-Bridge $owner "call $cyclopsId SubFire.GetFireCount")
            if (($fires -gt 0 -and $health -lt 0.75 * $maxHealth) -or $health -lt 0.35 * $maxHealth) { break }
            Invoke-Bridge $owner "call $cyclopsId LiveMixin.TakeDamage 30" | Out-Null
            Start-Sleep -Milliseconds 300
        }
        # Freezes fire damage too (FireSimulation goes through TakeDamage), so the state holds still while we compare
        Invoke-Bridge $owner 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
        # UpdateOvershield opens the remaining damage points once a second
        Start-Sleep -Seconds 4
        $health = Get-Health $owner
        $fires = [int](Invoke-Bridge $owner "call $cyclopsId SubFire.GetFireCount")
        Write-Host "  health $health of $maxHealth, $fires fire(s)"
        Assert-True ($fires -gt 0) 'the Cyclops is on fire'
        Assert-True ($health -lt 0.75 * $maxHealth) 'the hull is below 75 %'
    }

    Step 'Both players and the server agree on the damage' {
        Assert-DamageInSync 'damaged:'
        Assert-True ($alicePoints.Count -ge 2) 'at least two damage points are open'
    }

    Step 'Bob welds one damage point closed' {
        $script:repairedIndex = @($alicePoints.Keys)[0]
        $script:repairedId = $alicePoints[$repairedIndex]
        $healthBefore = Get-Health $owner
        Write-Host "  repairing point $repairedIndex ($repairedId)"
        Invoke-Bridge $B "call $repairedId LiveMixin.AddHealth 1000" | Out-Null
        $closed = Wait-Until { -not (Get-GameDamagePoints $A).Contains($repairedIndex) } -TimeoutSeconds 15
        Assert-True ([bool]$closed) "the repaired point $repairedIndex closes for Alice too"
        $healed = Wait-Until { (Get-Health $owner) -gt $healthBefore } -TimeoutSeconds 15
        Assert-True ([bool]$healed) 'the repair gives the hull health back'
        Assert-DamageInSync 'repaired:'
        Assert-True (-not $server.Points.Contains($repairedIndex)) "the server no longer has damage point $repairedIndex"
    }

    Step 'The server saves; server and both games restart from the save' {
        $script:beforeRestart = $server
        Write-Host "  before restart: points [$(Format-Points $server.Points)], fires $($server.Fires.Count), health $($server.Health)"
        Restart-ScenarioWorld -SaveName $SaveName
        Invoke-Bridge $A "teleport $spawnSpot" | Out-Null
        Invoke-Bridge $B "teleport $spawnSpot" | Out-Null
        $loaded = Wait-Until { (Invoke-BridgeJson $A "entity $cyclopsId") -and (Invoke-BridgeJson $B "entity $cyclopsId") } -TimeoutSeconds 60
        Assert-True ([bool]$loaded) 'both games have the Cyclops after the restart'
        $script:owner = Wait-Until { Get-LockOwner } -TimeoutSeconds 20
        Start-Sleep -Seconds 4
    }

    Step 'After the restart the damage is the same for both players' {
        Assert-DamageInSync 'after restart:'
        Assert-True (-not $alicePoints.Contains($repairedIndex)) "Alice's repaired point $repairedIndex stays repaired"
        Assert-True (-not $bobPoints.Contains($repairedIndex)) "Bob's repaired point $repairedIndex stays repaired"
        foreach ($index in $beforeRestart.Points.Keys) {
            Assert-Equal $beforeRestart.Points[$index] $server.Points[$index] "damage point $index survived the restart with its id"
        }
        foreach ($fireId in $beforeRestart.Fires.Keys) {
            Assert-True $server.Fires.Contains($fireId) "fire $fireId ($($beforeRestart.Fires[$fireId])) survived the restart"
        }
        # Until the no-damage cheat is back on after the rejoin, a fire tick (SubFire.FireSimulation, 15 per fire every 10 s) may land
        $allowedLoss = 2 * 15 * [Math]::Max(1, $beforeRestart.Fires.Count) + 1
        Assert-True ($server.Health -le $beforeRestart.Health + 1 -and $server.Health -ge $beforeRestart.Health - $allowedLoss) "the hull health survived the restart ($($beforeRestart.Health) -> $($server.Health))"
    }

    Assert-NoDiff -Ids (@($cyclopsId) + @($alicePoints.Values) + @($server.Fires.Keys)) 'no desync reported for the Cyclops, its damage points or fires'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S1 CyclopsDamagePersists' -Keep:$Keep
