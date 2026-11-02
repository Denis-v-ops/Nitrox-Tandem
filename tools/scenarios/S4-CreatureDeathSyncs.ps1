<#
.SYNOPSIS
    FIX_SPEC S4 (#2510, #1701): a creature killed by the player who doesn't simulate it dies for both players, things
    destroyed by damage disappear for both, and a dying player isn't deleted on the server (regression check for the
    LiveMixin_Kill_Patch fix of #2867).

.DESCRIPTION
    1. Alice spawns creatures (a Peeper, then a Stalker) next to both players. The player who doesn't hold the
       creature's simulation lock (server "query") damages it to death with LiveMixin.TakeDamage, the call the knife
       ends in (the no-damage cheat is off for that call: DamageSystem zeroes all damage while it's on). Both games must
       then have a corpse (the object with LiveMixin.IsAlive() false, still dead 5 s later) and the server must no
       longer have the creature (RemoveCreatureCorpse destroys the entity).
    2. Alice spawns an acid mushroom (pickupable); Bob damages it to death. It must be gone in both games and on the
       server.
    3. Alice dies (LiveMixin.Kill on the player). The server still has her PlayerEntity ("query <playerId>"), and after
       she respawned Bob still has her body, near her new position.

    Starts a fresh world (save s4-creature) and two games (opens windows), unless -NoLaunch. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's4-creature')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Get-ServerQuery {
    param([string]$Id)
    Invoke-Bridge 'server' "query $Id" -AllowError
}

function Test-ServerHas {
    param([string]$Id)
    $reply = Get-ServerQuery $Id
    -not ($reply -match 'not found' -or $reply.StartsWith('ERR '))
}

function Get-LockOwner {
    param([string]$Id)
    if ((Get-ServerQuery $Id) -match 'Owner: (\w+)') { $Matches[1] } else { $null }
}

function Test-Alive {
    param([string]$Player, [string]$Id)
    (Invoke-Bridge $Player "call $Id LiveMixin.IsAlive") -eq 'true'
}

function Test-Loaded {
    param([string]$Player, [string]$Id)
    $reply = Invoke-Bridge $Player "entity $Id" -AllowError
    -not $reply.StartsWith('ERR ')
}

<#
    Alice spawns a <TechType> in front of her; returns the new object's NitroxId once both games have it.
#>
function New-SpawnedObject {
    param([string]$TechType, [string]$Component)
    $before = @((Invoke-BridgeJson $A "find $Component 50") | ForEach-Object { $_.id })
    Invoke-Bridge $A "console spawn $TechType" | Out-Null
    $new = Wait-Until { (Invoke-BridgeJson $A "find $Component 50") | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
    if (-not $new) { throw "$TechType didn't spawn" }
    $seen = Wait-Until { Test-Loaded $B $new.id } -TimeoutSeconds 15
    Assert-True ([bool]$seen) "$B has the $TechType $($new.id)"
    $new.id
}

<#
    Damages the object to death in that player's game, with the no-damage cheat off for that one call.
#>
function Invoke-DamageToDeath {
    param([string]$Player, [string]$Id)
    Invoke-Bridge $Player 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
    try {
        Invoke-Bridge $Player "call $Id LiveMixin.TakeDamage 10000" | Out-Null
    }
    finally {
        Invoke-Bridge $Player 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
    }
}

function Test-CreatureKill {
    param([string]$TechType)
    $id = New-SpawnedObject $TechType 'Creature'
    Start-Sleep -Seconds 3
    $owner = Wait-Until { Get-LockOwner $id } -TimeoutSeconds 10
    $killer = if ($owner -eq $A) { $B } else { $A }
    Write-Host "  $TechType $id simulated by '$owner', killed by $killer"
    Assert-True ($owner -and $owner -ne $killer) "the killer ($killer) doesn't simulate the $TechType (owner '$owner')"
    Invoke-DamageToDeath $killer $id
    foreach ($player in $A, $B) {
        $dead = Wait-Until { (Test-Loaded $player $id) -and -not (Test-Alive $player $id) } -TimeoutSeconds 10
        Assert-True ([bool]$dead) "$player has the dead $TechType (a corpse)"
    }
    $gone = Wait-Until { -not (Test-ServerHas $id) } -TimeoutSeconds 10
    Assert-True ([bool]$gone) "the server no longer has the $TechType"
    Start-Sleep -Seconds 5
    foreach ($player in $A, $B) {
        Assert-True (-not (Test-Loaded $player $id) -or -not (Test-Alive $player $id)) "5 s later the $TechType is still dead for $player"
    }
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    $script:alicePlayerId = (Invoke-BridgeJson $A 'state').playerId

    Step 'Both players in the Safe Shallows next to each other' {
        Invoke-Bridge $A 'teleport -60 -8 380' | Out-Null
        Invoke-Bridge $B 'teleport -57 -8 382' | Out-Null
        Invoke-Bridge $A 'look 30 0' | Out-Null
        Start-Sleep -Seconds 4
    }

    Step 'A Peeper killed by the player who does not simulate it' {
        Test-CreatureKill 'peeper'
    }

    Step 'A Stalker killed by the player who does not simulate it' {
        Test-CreatureKill 'stalker'
    }

    Step 'Bob destroys an acid mushroom' {
        $id = New-SpawnedObject 'acidmushroom' 'Pickupable'
        $liveMixin = Invoke-Bridge $B "get $id LiveMixin.destroyOnDeath" -AllowError
        Write-Host "  acid mushroom $id, LiveMixin.destroyOnDeath: $liveMixin, broadcastKillOnDeath: $(Invoke-Bridge $B "get $id LiveMixin.broadcastKillOnDeath" -AllowError)"
        Invoke-DamageToDeath $B $id
        foreach ($player in $B, $A) {
            $gone = Wait-Until { -not (Test-Loaded $player $id) } -TimeoutSeconds 10
            Assert-True ([bool]$gone) "the acid mushroom is gone for $player"
        }
        $serverGone = Wait-Until { -not (Test-ServerHas $id) } -TimeoutSeconds 10
        Assert-True ([bool]$serverGone) 'the server no longer has the acid mushroom'
    }

    Step 'Alice dies and respawns' {
        Invoke-Bridge $A 'call Player.main.liveMixin.Kill' | Out-Null
        $dead = Wait-Until { (Invoke-Bridge $A 'call Player.main.liveMixin.IsAlive') -eq 'false' } -TimeoutSeconds 5 -IntervalSeconds 0.3
        Assert-True ([bool]$dead) "$A died"
        Start-Sleep -Seconds 2
        Assert-True (Test-ServerHas $alicePlayerId) "the server still has $A's PlayerEntity while she's dead"
        $alive = Wait-Until { (Invoke-Bridge $A 'call Player.main.liveMixin.IsAlive') -eq 'true' -and (Invoke-Bridge $A 'get player Player.cinematicModeActive') -eq 'false' } -TimeoutSeconds 60
        Assert-True ([bool]$alive) "$A respawned"
        Start-Sleep -Seconds 5
        Assert-True (Test-ServerHas $alicePlayerId) "the server still has $A's PlayerEntity after the respawn"
        $alicePosition = [double[]](Invoke-BridgeJson $A 'state').position
        $body = Wait-Until {
            $entity = Invoke-BridgeJson $B "entity $alicePlayerId"
            if ($entity -and (Get-Distance ([double[]]$entity.position) $alicePosition) -lt 5) { $entity }
        } -TimeoutSeconds 20
        Assert-True ([bool]$body) "$B still has $A's body, near where she respawned ($(Format-Vector $alicePosition))"
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S4 CreatureDeathSyncs' -Keep:$Keep
