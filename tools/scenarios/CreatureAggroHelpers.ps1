<#
    Shared by the creature-aggression scenarios (S14-CreatureAggro.ps1, D4-ThreatParity.ps1): spawn a creature in the
    game that should simulate it, put a victim next to it and watch who it targets and what reaches the victim.
    Dot-source after ScenarioHelpers.ps1. The caller defines $WindowSeconds and $Only (script parameters).
#>

$A = 'Alice'
$B = 'Bob'
$script:Matrix = New-Object System.Collections.ArrayList
$script:Facts = @{}
$script:PlayerIds = @{}
# Every case gets its own spot on a 70 m ring around the escape pod (Safe Shallows), about 43 m apart, 6 m deep, so
# corpses and leftovers of earlier cases stay out of the way.
$script:NextSpot = 0
$script:Center = $null

function Get-ServerQuery {
    param([string]$Id)
    Invoke-Bridge 'server' "query $Id" -AllowError
}

function Get-LockOwner {
    param([string]$Id)
    if ((Get-ServerQuery $Id) -match 'Owner: (\w+)') { $Matches[1] } else { $null }
}

function Test-Loaded {
    param([string]$Player, [string]$Id)
    $reply = Invoke-Bridge $Player "entity $Id" -AllowError
    -not $reply.StartsWith('ERR ')
}

function Get-Number {
    param([string]$Player, [string]$Path)
    $reply = Invoke-Bridge $Player "get $Path" -AllowError
    if ($reply.StartsWith('ERR ')) { return $null }
    [double]::Parse($reply, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-Health {
    param([string]$Player)
    Get-Number $Player 'Player.main.liveMixin.health'
}

function Set-Vulnerable {
    param([string]$Player, [bool]$Vulnerable)
    Invoke-Bridge $Player "set NoDamageConsoleCommand.main.noDamageCheat $((-not $Vulnerable).ToString().ToLower())" | Out-Null
    Invoke-Bridge $Player 'set Player.main.liveMixin.health 100' | Out-Null
}

function Get-Position {
    param([string]$Player, [string]$Id)
    $entity = Invoke-BridgeJson $Player "entity $Id"
    [double[]]$entity.position
}

function Format-Point {
    param([double[]]$P)
    ($P | ForEach-Object { $_.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) }) -join ' '
}

<#
    The spawner spawns <TechType> about 4 m in front of it; returns the new object's NitroxId once both games have it.
#>
function New-Creature {
    param([string]$Spawner, [string]$TechType, [int]$Distance = 4)
    $other = if ($Spawner -eq $A) { $B } else { $A }
    $before = @((Invoke-BridgeJson $Spawner 'find Creature 80') | ForEach-Object { $_.id })
    Invoke-Bridge $Spawner "console spawn $TechType 1 $Distance" | Out-Null
    # Natural creatures load in around the spawner too: only a new one of the spawned type counts.
    $new = Wait-Until { (Invoke-BridgeJson $Spawner 'find Creature 80') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id -and $_.techType -eq $TechType } | Select-Object -First 1 } -TimeoutSeconds 15
    if (-not $new) { throw "$TechType didn't spawn" }
    $seen = Wait-Until { Test-Loaded $other $new.id } -TimeoutSeconds 15
    Assert-True ([bool]$seen) "$other has the $TechType $($new.id)"
    $new.id
}

<#
    Asset facts the decompiled code can't show: which aggression component the creature has, its target type, range
    and hunger threshold, and its MeleeAttack type and bite values. Printed once per creature type.
#>
function Show-Facts {
    param([string]$Player, [string]$TechType, [string]$Id)
    if ($script:Facts.ContainsKey($TechType)) { return }
    $entity = Invoke-BridgeJson $Player "entity $Id"
    $wanted = 'Aggressive|MeleeAttack|AttackLastTarget|LastTarget|ProtectCrashHome|Creature$|^Crash$|Stalker|SandShark|BoneShark|Biter|ReaperLeviathan|EcoTarget|AttackCyclops|' +
        'Shocker|CrabSnake|Warper|Mesmer|PullCreatures|LavaLizard|LavaShell|SpineEel|CrabSquid|EMPAttack|Bleeder|AttachAndSuck|WaterParkCreature'
    $facts = @($entity.components | Where-Object { $_.'$component' -and $_.'$component' -match $wanted } | ForEach-Object {
        $c = $_
        $keep = [ordered]@{ component = $c.'$component' }
        foreach ($name in 'targetType', 'maxRangeScalar', 'maxSearchRings', 'aggressionPerSecond', 'hungerThreshold', 'targetShouldBeInfected', 'minimumVelocity', 'ignoreSameKind',
            'playerAttackInterval', 'biteDamage', 'biteInterval', 'biteAggressionThreshold', 'biteAggressionDecrement', 'canBitePlayer', 'canBiteCreature', 'canBiteVehicle', 'canBeFed',
            'aggressionThreshold', 'minAttackDuration', 'maxAttackDuration', 'pauseInterval', 'rememberTargetTime', 'type', 'range', 'hasEyes', 'eyeFOV',
            'electricalDamage', 'electricalDamageInterval', 'cinematicAttackAdditionalDamage', 'seamothDamage', 'minDistanceToTarget', 'maxCastingDistance', 'minDistance', 'maxDistance',
            'pullPlayerSpeed', 'searchRange', 'maxAttackRange', 'blastInterval', 'leechDamage', 'maxArmorPoints', 'armorPoints', 'bornInside') {
            if ($c.PSObject.Properties[$name]) { $keep[$name] = $c.$name }
        }
        [pscustomobject]$keep
    })
    $script:Facts[$TechType] = $facts
    Write-Host "  facts for ${TechType}:"
    foreach ($f in $facts) { Write-Host "    $(($f.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' ')" -ForegroundColor DarkGray }
}

function Get-BiteDamage {
    param([string]$TechType)
    $melee = @($script:Facts[$TechType] | Where-Object { $_.PSObject.Properties['biteDamage'] }) | Select-Object -First 1
    if ($melee) { [double]$melee.biteDamage } else { $null }
}

<#
    Reads the player's health in their own game; on damage, records who dealt it (LiveMixin.damageInfo) and heals back to
    100. Only damage dealt by the case's creature counts as a hit (wild fauna bites the test players too). Uses the
    caller's $row and $lastHitAt. Returns true for a hit of the creature.
#>
function Test-Hit {
    param([string]$Player, [string]$CreatureId, [string]$How,
        # A crash fish destroys itself when it explodes: its explosion counts whatever the dealer reads now.
        [switch]$Explosion,
        # The creature's attack passes no dealer (an Ampeel's shock: Electrical): damage of this type without a dealer
        # counts too.
        [string]$DamageType)
    $health = Get-Health $Player
    if ($health -eq $null -or $health -ge 99.9) { return $false }
    $hit = 100 - $health
    $type = Invoke-Bridge $Player 'get Player.main.liveMixin.damageInfo.type' -AllowError
    $dealer = Invoke-Bridge $Player 'get Player.main.liveMixin.damageInfo.dealer' -AllowError
    $dealerId = if ($dealer -match '"id":\s*"([0-9a-f-]{36})"') { $Matches[1] } else { $null }
    $dealerName = if ($dealer -match '"name":\s*"([^"]+)"') { $Matches[1] } else { $dealer.Trim() }
    Invoke-Bridge $Player 'set Player.main.liveMixin.health 100' | Out-Null
    $typeName = $type.Trim().Trim('"')
    $dealerless = $DamageType -and $typeName -match $DamageType -and $dealer.Trim() -eq 'null'
    if ($dealerId -ne $CreatureId -and -not ($Explosion -and $type -match 'Explosive') -and -not $dealerless) {
        Write-Host "    other damage: $([math]::Round($hit, 1)) to $Player by $dealerName ($type), not counted" -ForegroundColor DarkYellow
        $row.note += "other damage $([math]::Round($hit, 1)) by $dealerName; "
        return $false
    }
    $now = Get-Date
    $row.hits++
    $row.damage += $hit
    if ($hit -gt $row.maxHit) { $row.maxHit = [math]::Round($hit, 1) }
    if ($lastHitAt[$Player] -and ($now - $lastHitAt[$Player]).TotalSeconds -lt 0.5) { $row.note += 'two hits <0.5 s apart; ' }
    $lastHitAt[$Player] = $now
    if (-not $row.how.Contains($How)) { $row.how = ($row.how + " $How").Trim() }
    if (-not $row.types.Contains($typeName)) { $row.types = ($row.types + " $typeName").Trim() }
    Write-Host "    hit ($How): $([math]::Round($hit, 1)) damage to $Player by $dealerName ($type)"
    $true
}

<#
    One matrix cell. Simulator = the game that should own the creature, Victim = the player who should be attacked.
    -Away: the non-victim player stands this many metres away (0 = both next to the creature, victim nearer).
#>
function Test-AggroCase {
    param([string]$Name, [string]$TechType, [string]$Simulator, [string]$Victim, [double]$Away = 30,
        # How far from the creature the victim starts.
        [double]$VictimDistance = 6,
        # Keep the victim moving (small teleports): some scans only see targets faster than minimumVelocity (an asset
        # value: 1 m/s for the Sandshark's, 0.2 for the crash fish's long-range one).
        [switch]$MovingVictim,
        # The victim takes damage without a bite (crashfish explosion): skip the per-bite size check.
        [switch]$Explosion,
        # Expect no damage (documentation only: the case is reported, not asserted).
        [switch]$ReportOnly,
        # Seconds between placing the players and reading the lock owner (longer when the lock is expected to move).
        [int]$SettleSeconds = 2,
        # Where to spawn instead of the next ring spot (big creatures need open water), and a longer watch.
        [double[]]$Spot,
        [int]$Window = $WindowSeconds,
        # Keep the creature aggressive in the victim's own game too (as after the victim hurt it, AggressiveOnDamage):
        # if both games then bit the victim, every bite would land twice.
        [switch]$VictimGameAggressive,
        # Who should hold the lock once the players are in place (default: the simulator, who spawned it).
        [string]$ExpectOwner,
        # Creatures whose aggression is synced (AI.syncedCreatureWhitelist, e.g. the Reaper) bite in the victim's own
        # game: the forced contact happens there.
        [switch]$TouchInVictimGame,
        # Damage of this type without a dealer counts as the creature's (see Test-Hit).
        [string]$DamageType,
        # Creature.Hunger set after the spawn (gates the aggression scans, see below).
        [double]$Hunger = 0.6,
        # The method the forced contact calls (subclasses: e.g. ShockerMeleeAttack.OnMouthTouch for an Ampeel's bite).
        [string]$TouchMethod = 'MeleeAttack.OnTouch',
        # Runs after the spawn in the simulator's game: & $Setup $id (e.g. activate a lava shell).
        [scriptblock]$Setup,
        # Runs on every poll: & $Watch $id $ownerNow; may add to $row.note / $row.extra.
        [scriptblock]$Watch,
        # The victim keeps looking at the creature (Mesmer: hypnosis needs eye contact).
        [switch]$VictimAims,
        # The creature must NOT hurt the victim (born in a containment): the forced contact happens too, and any hit fails.
        [switch]$ExpectNoDamage)
    if ($Only -and $Only -notcontains $Name) { return }
    $other = if ($Victim -eq $A) { $B } else { $A }
    $angle = [math]::PI * 2 * $script:NextSpot / 10
    # PowerShell's comma binds tighter than + and *: every computed element needs its own parentheses.
    $spot = if ($Spot) { $Spot } else { @(($script:Center[0] + 70 * [math]::Cos($angle)), -6, ($script:Center[2] + 70 * [math]::Sin($angle))) }
    $script:NextSpot++
    $row = [ordered]@{ case = $Name; creature = $TechType; simulator = $Simulator; victim = $Victim; owner = '?'; targeted = $false; maxAggression = 0.0; hits = 0; damage = 0.0; maxHit = 0.0; biteDamage = $null; how = ''; types = ''; ownGameHits = 0; extra = ''; note = '' }
    Write-Host "  case $Name : $TechType simulated by $Simulator, victim $Victim, other player $(if ($Away -gt 0) { "$Away m away" } else { 'next to it' })"

    # A creature bites at most every few seconds and then pauses (AttackLastTarget.pauseInterval, 20 s for a Stalker),
    # so the first bite must count: the victim is vulnerable before the creature exists, and the spawning simulator
    # is invisible to creatures (NoAggression, Player.CanBeAttacked false) until it is out of the way, so the first
    # attack isn't spent on it.
    if ($Victim -ne $Simulator) {
        Invoke-Bridge $Simulator 'call GameModeUtils.ActivateCheat NoAggression' | Out-Null
    }
    Invoke-Bridge $Simulator "teleport $(Format-Point $spot)" | Out-Null
    $victimSpot = @(($spot[0] + 2), $spot[1], $spot[2])
    if ($Victim -ne $Simulator) {
        Invoke-Bridge $Victim "teleport $(Format-Point $victimSpot)" | Out-Null
    }
    Invoke-Bridge $Simulator 'look 0 0' | Out-Null
    Start-Sleep -Seconds 3
    Set-Vulnerable $Victim $true
    foreach ($player in $A, $B) {
        # Long runs starve the test players (DamageType Starve), which would hide the bites.
        Invoke-Bridge $player 'set player Survival.food 100' | Out-Null
        Invoke-Bridge $player 'set player Survival.water 100' | Out-Null
    }
    $id = New-Creature $Simulator $TechType
    # Hunger gates each target scan (AggressiveWhenSeeTarget.hungerThreshold, an asset value): 0.6 enables the scan
    # for players ("Shark" targets: threshold 0 for Stalker, Sandshark, Bone shark, 0.5 for the Biter) but not the
    # Stalker's and sharks' scans for the small fish of the Safe Shallows (0.8 and 0.95), which otherwise distract them.
    Invoke-Bridge $Simulator "set $id Creature.Hunger.Value $($Hunger.ToString([Globalization.CultureInfo]::InvariantCulture))" -AllowError | Out-Null
    if ($Setup) { & $Setup $id }

    $creaturePos = Get-Position $Simulator $id
    # The victim floats 6 m from the creature, the other player $Away m further along x (or 8 m on the other side).
    # Not closer: a bite needs the mouth's trigger to be entered (OnTouch.OnTriggerEnter) while the creature is
    # aggressive. A player teleported into the trigger before that is never bitten until they separate.
    $victimPos = @(($creaturePos[0] + $VictimDistance), $creaturePos[1], $creaturePos[2])
    Invoke-Bridge $Victim "teleport $(Format-Point $victimPos)" | Out-Null
    $otherPos = if ($Away -gt 0) { @(($creaturePos[0] + $VictimDistance + $Away), $creaturePos[1], $creaturePos[2]) } else { @(($creaturePos[0] - 8), $creaturePos[1], $creaturePos[2]) }
    Invoke-Bridge $other "teleport $(Format-Point $otherPos)" | Out-Null
    if ($Victim -ne $Simulator -and $Away -le 0) {
        # Both players are targets now, the victim the nearer one.
        Set-Vulnerable $other $true
        Invoke-Bridge $Simulator 'call GameModeUtils.DeactivateCheat NoAggression' | Out-Null
    }
    Show-Facts $Simulator $TechType $id
    $row.biteDamage = Get-BiteDamage $TechType
    Start-Sleep -Seconds $SettleSeconds
    $row.owner = Get-LockOwner $id
    $victimState = Invoke-BridgeJson $Victim 'state'
    if (-not $victimState.isUnderwater) { $row.note += 'victim not underwater; ' }

    # With both players next to the creature, either may be bitten (the simulator's AI picks the nearest target it
    # sees): both are watched, and the case counts bites on either.
    $watched = if ($Away -le 0 -and $Victim -ne $Simulator) { @($Victim, $other) } else { @($Victim) }
    $hitsBy = @{}
    $watched | ForEach-Object { $hitsBy[$_] = 0 }
    $deadline = (Get-Date).AddSeconds($Window)
    $lastHitAt = @{}
    $ownerNow = if ($row.owner -eq $A -or $row.owner -eq $B) { $row.owner } else { $Simulator }
    $poll = 0
    if ($VictimAims) {
        Invoke-Bridge $Victim "hold $id $Window None" | Out-Null
    }
    try {
        while ((Get-Date) -lt $deadline -and ($row.hits -lt 1 -or $ExpectNoDamage)) {
            if ($Watch) { & $Watch $id $ownerNow }
            # The lock can move during a case (the "far" case); read the AI state where it runs.
            if ((++$poll % 6) -eq 0) {
                $owner = Get-LockOwner $id
                if ($owner -eq $A -or $owner -eq $B) { $ownerNow = $owner }
            }
            if ($VictimGameAggressive -and $ownerNow -ne $Victim) {
                Invoke-Bridge $Victim "set $id Creature.Aggression.Value 1" -AllowError | Out-Null
            }
            if ($MovingVictim) {
                $offset = if ($poll % 2) { 1 } else { -1 }
                Invoke-Bridge $Victim "teleport $(Format-Point @(($victimPos[0] + $offset), $victimPos[1], $victimPos[2]))" | Out-Null
            }
            $target = Invoke-Bridge $ownerNow "get $id LastTarget.target.name" -AllowError
            $aggression = Get-Number $ownerNow "$id Creature.Aggression.Value"
            if ($aggression -ne $null -and $aggression -gt $row.maxAggression) { $row.maxAggression = [math]::Round($aggression, 2) }
            if (($poll % 6) -eq 1) {
                # Trace: what the AI does and how far the victim is (in the simulating game).
                $action = Invoke-Bridge $ownerNow "get $id Creature.prevBestAction" -AllowError
                $actionName = if ($action -match '"\$component":\s*"(\w+)"') { $Matches[1] } else { 'none' }
                $distance = try { [math]::Round((Get-Distance (Get-Position $ownerNow $id) ([double[]](Invoke-BridgeJson $Victim 'state').position)), 1) } catch { '?' }
                Write-Host "    t+$([int]((Get-Date) - $deadline.AddSeconds(-$Window)).TotalSeconds) s: $actionName, aggression $([math]::Round([double]$aggression, 2)), target $target, $distance m from $Victim" -ForegroundColor DarkGray
            }
            foreach ($player in $watched) {
                # In the simulator's game a player is either the local player ("Player") or the remote body named after the player.
                $nameThere = if ($ownerNow -eq $player) { 'Player' } else { $player }
                if ($target -eq "`"$nameThere`"" -or $target -eq $nameThere) { $row.targeted = $true }
                if (Test-Hit $player $id 'natural' -Explosion:$Explosion -DamageType $DamageType) { $hitsBy[$player]++ }
            }
            Start-Sleep -Milliseconds 300
        }

        # Wild AI is fickle (it chases small fish, collects shinies, swims through the player without a new trigger
        # contact), so a case without a natural bite then makes the touch itself happen: the simulating game runs the
        # creature's own MeleeAttack.OnTouch with the victim's collider (its local player, or the victim's body), as the
        # mouth trigger would. Everything after the physics contact is the real game and Nitrox code.
        # The double-bite guard always gets the touch: it has to happen in both games at once.
        if ($row.hits -eq 0 -and -not $ReportOnly) {
            $toucher = if ($TouchInVictimGame) { $Victim } elseif ($row.owner -eq $A -or $row.owner -eq $B) { $ownerNow } else { $Simulator }
            Write-Host "    no natural hit in $Window s: touching $Victim with the $TechType in ${toucher}'s game" -ForegroundColor DarkGray
            if ($Explosion -and -not (Test-Loaded $toucher $id)) {
                # It already blew up, out of the victim's reach: a fresh one, detonated right away.
                $id = New-Creature $toucher $TechType
                $row.note += "exploded out of reach, detonated a new one; "
            }
            # By now the creature may have wandered off (or burrowed): bring them together, as the bite's raycast must
            # not cross terrain. The simulating game moves its creature next to the victim (its local player, or the
            # victim's body there); otherwise the victim moves next to the creature.
            if ($Explosion -or $toucher -ne $ownerNow) {
                $creatureNow = Get-Position $toucher $id
                Invoke-Bridge $Victim "teleport $(Format-Point @(($creatureNow[0] + 2), $creatureNow[1], $creatureNow[2]))" | Out-Null
            }
            else {
                $victimThere = if ($toucher -eq $Victim) { [double[]](Invoke-BridgeJson $toucher 'state').position } else { Get-Position $toucher $script:PlayerIds[$Victim] }
                Invoke-Bridge $toucher "set $id GameObject.transform.position $(($victimThere[0] + 1.5).ToString('0.00')),$($victimThere[1].ToString('0.00')),$($victimThere[2].ToString('0.00'))" | Out-Null
            }
            Start-Sleep -Seconds 1.5
            # A bite from that contact is a natural hit, and then no touch is needed.
            if (Test-Hit $Victim $id 'natural' -Explosion:$Explosion -DamageType $DamageType) {
                $hitsBy[$Victim]++
            }
            else {
                if ($Explosion) {
                    Invoke-Bridge $toucher "call $id Crash.Detonate" | Out-Null
                }
                else {
                    $collider = if ($toucher -eq $Victim) { 'player' } else { $script:PlayerIds[$Victim] }
                    Invoke-Bridge $toucher "set $id MeleeAttack.timeLastBite -100" | Out-Null
                    Invoke-Bridge $toucher "set $id Creature.Aggression.Value 1" | Out-Null
                    Invoke-Bridge $toucher "call $id $TouchMethod $collider" | Out-Null
                }
                $forcedDeadline = (Get-Date).AddSeconds(3)
                while ((Get-Date) -lt $forcedDeadline) {
                    if (Test-Hit $Victim $id 'touch' -Explosion:$Explosion -DamageType $DamageType) { $hitsBy[$Victim]++ }
                    Start-Sleep -Milliseconds 300
                }
            }
        }

        if ($VictimGameAggressive -and -not $ReportOnly) {
            # The contact in the victim's own game, which holds the creature aggressive: that game must not bite (the
            # simulating game's bite already reached the victim). The simulator can't bite meanwhile (last bite "in the
            # future"), so any damage now comes from the victim's game.
            $simulatorNow = if ($row.owner -eq $A -or $row.owner -eq $B) { $ownerNow } else { $Simulator }
            Invoke-Bridge $simulatorNow "set $id MeleeAttack.timeLastBite 100000" | Out-Null
            Invoke-Bridge $Victim "set $id MeleeAttack.timeLastBite -100" | Out-Null
            Invoke-Bridge $Victim "set $id Creature.Aggression.Value 1" | Out-Null
            Invoke-Bridge $Victim "call $id MeleeAttack.OnTouch player" | Out-Null
            $ownGameDeadline = (Get-Date).AddSeconds(2)
            while ((Get-Date) -lt $ownGameDeadline) {
                if (Test-Hit $Victim $id 'own game') { $row.ownGameHits++ }
                Start-Sleep -Milliseconds 300
            }
            Invoke-Bridge $simulatorNow "set $id MeleeAttack.timeLastBite 0" | Out-Null
        }
    }
    finally {
        Invoke-Bridge $Simulator 'call GameModeUtils.DeactivateCheat NoAggression' | Out-Null
        foreach ($player in $watched) { Set-Vulnerable $player $false }
        if ($watched.Count -gt 1) { $row.victim = 'both'; $row.note += "hits: $(($watched | ForEach-Object { "$_ $($hitsBy[$_])" }) -join ', '); " }
        $row.damage = [math]::Round($row.damage, 1)
        $ownerAtEnd = Get-LockOwner $id
        if ($ownerAtEnd -and $ownerAtEnd -ne $row.owner) { $row.note += "owner at the end: $ownerAtEnd; " }
        $killer = if ($ownerAtEnd -eq $B) { $B } else { $A }
        if (Test-Loaded $killer $id) {
            Invoke-Bridge $killer 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
            Invoke-Bridge $killer "call $id LiveMixin.TakeDamage 100000" -AllowError | Out-Null
            Invoke-Bridge $killer 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
        }
        [void]$script:Matrix.Add([pscustomobject]$row)
    }

    if ($ReportOnly) { return }
    $who = if ($watched.Count -gt 1) { 'a player' } else { $Victim }
    $expectedOwner = if ($ExpectOwner) { $ExpectOwner } else { $Simulator }
    Assert-Equal $expectedOwner $row.owner "$Name : $expectedOwner simulates the $TechType"
    if ($ExpectNoDamage) {
        Assert-Equal 0 $row.hits "$Name : the $TechType doesn't hurt $who, even when touched ($($row.damage) damage)"
        return
    }
    # A natural bite counts too: the watch ends with it, maybe before a poll saw the target.
    Assert-True ($row.targeted -or $row.how -match 'natural') "$Name : the $TechType picks $who as its target (on the simulator's game)"
    Assert-True ($row.hits -ge 1) "$Name : $who takes damage from the $TechType ($($row.hits) hits ($($row.how)), $($row.damage) damage, $($row.types))"
    if (-not $Explosion -and -not $DamageType -and $row.biteDamage) {
        Assert-True ($row.maxHit -le $row.biteDamage * 1.05 + 0.1) "$Name : no hit is bigger than one bite (biggest $($row.maxHit), biteDamage $($row.biteDamage))"
        Assert-True (-not $row.note.Contains('two hits')) "$Name : no bite lands twice"
    }
    if ($VictimGameAggressive) {
        Assert-Equal 0 $row.ownGameHits "$Name : $Victim's own game doesn't bite $Victim with a $TechType it doesn't simulate (that bite comes from the simulator)"
    }
}

