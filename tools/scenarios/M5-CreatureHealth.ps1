<#
.SYNOPSIS
    MULTINAUGHTICA_SPEC M5 (upstream #2903 + MultiNaughtica's hand-over grace): damage from both players adds up on a
    creature, a creature simulated by one player hurts a creature simulated by the other, and the death shows in both
    games.

.DESCRIPTION
    A->B means Alice's game simulates the creature (she spawned it, checked with the server's "query"). A player's hit
    is LiveMixin.TakeDamage with the local player as dealer, the call the knife ends in (Knife_OnToolUseAnim_Patch passes
    Player.mainObject). Both games' no-damage cheat is off during a case: the hit lands in the simulating game, whose
    cheat would zero it (DamageSystem.CalculateDamage). Both players are invisible to creatures (NoAggression).
      - sum-AB, sum-BA: the player who doesn't simulate the Stalker hits it for 60 % of its health. The simulating game's
        health must drop (without the fix: only the hitter's copy changes, the simulator's stays full), the hitter's game
        must then show the simulator's health, and show the hit at once (LiveMixin.timeLastDamageEffect moves, if the
        creature has a damage effect). Then the simulator hits it for the same: it must die (a corpse in both games, gone
        on the server). Without the fix each copy keeps its own health and neither reaches 0.
      - handover-AB: Alice and Bob each hit Alice's Stalker for 30 %; Alice leaves 400 m, the server hands the Stalker to
        Bob. Bob's game must continue from the summed health (without the fix: from its own copy, which only lost Bob's
        hit). Then Bob's hit kills it. The 1 s grace for a late report isn't provoked here (L2 CreatureHealthRoutingTest).
      - predator-prey: Alice's game simulates a Stalker, Bob's a Peeper next to it, streamed to Alice's game. Alice's
        Stalker bites the Peeper (MeleeAttack.OnTouch in her game, which alone makes the Stalker aggressive, FIX_SPEC
        S14), up to 5 times. The Peeper must lose health or die in Bob's game, and its death must show in both. Without
        the fix the bite only shows in Alice's game (her copy is streamed, so the hit counted as Bob's to apply) and
        Bob's copy of the Stalker never bites: the Peeper never dies. The case first waits until Alice's copy is
        streamed (RemotelyControlled); before that a bite in her game counted locally and the case couldn't fail.
      - crash-blast: Alice's crash fish explodes 2 m from Bob's Stalker (Crash.Detonate in Alice's game, the only one
        where it explodes). The Stalker must lose health in Bob's game (falloff of 50 over 5 m), and Alice's game shows
        it. Without the fix: no damage.
    A matrix of the cases is printed at the end.

    Starts a fresh world (save m5-health) and two games, unless -NoLaunch. Needs a Debug build. Run windowless
    ($env:NITROX_WINDOWLESS = 1).
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'm5-health',
    # Only these cases, e.g. -Only sum-AB,predator-prey
    [string[]]$Only)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'CreatureAggroHelpers.ps1')
$script:Rows = New-Object System.Collections.ArrayList
$inv = [Globalization.CultureInfo]::InvariantCulture

function Test-ServerHas {
    param([string]$Id)
    $reply = Get-ServerQuery $Id
    -not ($reply -match 'not found' -or $reply.StartsWith('ERR '))
}

function Test-Alive {
    param([string]$Player, [string]$Id)
    (Invoke-Bridge $Player "call $Id LiveMixin.IsAlive" -AllowError) -eq 'true'
}

function Get-CreatureHealth {
    param([string]$Player, [string]$Id)
    Get-Number $Player "$Id LiveMixin.health"
}

<#
    Damage on or off in both games (the no-damage cheat zeroes every hit, also on creatures).
#>
function Set-DamageOn {
    param([bool]$On)
    foreach ($player in $A, $B) {
        Invoke-Bridge $player "set NoDamageConsoleCommand.main.noDamageCheat $((-not $On).ToString().ToLower())" | Out-Null
        Invoke-Bridge $player 'set Player.main.liveMixin.health 100' | Out-Null
    }
}

<#
    The player hits the creature as the knife does: LiveMixin.TakeDamage with the local player as dealer.
#>
function Invoke-PlayerHit {
    param([string]$Player, [string]$Id, [double]$Damage)
    Invoke-Bridge $Player ("call $Id LiveMixin.TakeDamage " + $Damage.ToString('0.##', $inv) + ' 0,0,0 Normal player') | Out-Null
}

<#
    The other game streams the creature to this one (its copy has RemotelyControlled).
#>
function Test-Streamed {
    param([string]$Player, [string]$Id)
    $entity = Invoke-BridgeJson $Player "entity $Id"
    [bool]($entity.components | Where-Object { $_.'$component' -eq 'RemotelyControlled' })
}

<#
    Waits until the creature's health in that game drops below $Below; returns it, or $null.
#>
function Wait-HealthBelow {
    param([string]$Player, [string]$Id, [double]$Below, [int]$TimeoutSeconds = 5)
    $result = Wait-Until { $h = Get-CreatureHealth $Player $Id; if ($h -ne $null -and $h -lt $Below - 0.1) { [pscustomobject]@{ health = $h } } } -TimeoutSeconds $TimeoutSeconds -IntervalSeconds 0.3
    if ($result) { $result.health } else { $null }
}

function Wait-HealthNear {
    param([string]$Player, [string]$Id, [double]$Expected, [int]$TimeoutSeconds = 5)
    [bool](Wait-Until { $h = Get-CreatureHealth $Player $Id; $h -ne $null -and [math]::Abs($h - $Expected) -le 0.5 } -TimeoutSeconds $TimeoutSeconds -IntervalSeconds 0.3)
}

<#
    The creature is dead in every game listed (a corpse, or gone) and the server no longer has it (RemoveCreatureCorpse
    or EntityDestroyed removes the entity).
#>
function Assert-DeadEverywhere {
    param([string]$Case, [string]$Id, [string]$What, [string[]]$Players = @($A, $B))
    foreach ($player in $Players) {
        $dead = Wait-Until { -not (Test-Loaded $player $Id) -or -not (Test-Alive $player $Id) } -TimeoutSeconds 10
        Assert-True ([bool]$dead) "$Case : the $What is dead in $player's game"
    }
    $gone = Wait-Until { -not (Test-ServerHas $Id) } -TimeoutSeconds 10
    Assert-True ([bool]$gone) "$Case : the server no longer has the $What"
}

<#
    Both players at the next spot of a 70 m ring around the pod, 6 m apart, invisible to creatures.
#>
function Move-PlayersToNextSpot {
    $angle = [math]::PI * 2 * $script:NextSpot / 10
    $script:NextSpot++
    $spot = @(($script:Center[0] + 70 * [math]::Cos($angle)), -6, ($script:Center[2] + 70 * [math]::Sin($angle)))
    Invoke-Bridge $A "teleport $(Format-Point $spot)" | Out-Null
    Invoke-Bridge $B "teleport $(Format-Point @(($spot[0] + 6), $spot[1], $spot[2]))" | Out-Null
    foreach ($player in $A, $B) { Invoke-Bridge $player 'look 0 0' | Out-Null }
    Start-Sleep -Seconds 3
    $spot
}

function Add-Row {
    param([hashtable]$Row)
    [void]$script:Rows.Add([pscustomobject]$Row)
}

function Test-SummedHits {
    param([string]$Case, [string]$Simulator)
    if ($Only -and $Only -notcontains $Case) { return }
    $hitter = Get-Other $Simulator
    $row = [ordered]@{ case = $Case; simulator = $Simulator; max = $null; afterFirst = $null; hitterSees = $false; effect = '?'; dead = $false; note = '' }
    Move-PlayersToNextSpot | Out-Null
    $id = New-Creature $Simulator 'stalker'
    Start-Sleep -Seconds 2
    Assert-Equal $Simulator (Get-LockOwner $id) "$Case : $Simulator simulates the Stalker"
    $max = Get-Number $Simulator "$id LiveMixin.maxHealth"
    $row.max = $max
    $hit = [math]::Round($max * 0.6, 1)
    $hasEffect = -not ((Invoke-Bridge $hitter "get $id LiveMixin.damageEffect" -AllowError) -match '^(null|ERR)')
    $effectBefore = Get-Number $hitter "$id LiveMixin.timeLastDamageEffect"
    Set-DamageOn $true
    try {
        Invoke-PlayerHit $hitter $id $hit
        $afterFirst = Wait-HealthBelow $Simulator $id $max
        $row.afterFirst = $afterFirst
        Assert-True ($afterFirst -ne $null) "$Case : $hitter's hit of $hit reaches the Stalker in $Simulator's game, which simulates it ($afterFirst of $max)"
        Assert-True (Test-Alive $Simulator $id) "$Case : one hit doesn't kill it"
        if ($afterFirst -ne $null) {
            $row.hitterSees = Wait-HealthNear $hitter $id $afterFirst
            Assert-True $row.hitterSees "$Case : $hitter's game shows the simulator's health ($afterFirst)"
        }
        if ($hasEffect) {
            $effectAfter = Get-Number $hitter "$id LiveMixin.timeLastDamageEffect"
            $row.effect = "$effectBefore -> $effectAfter"
            Assert-True ($effectAfter -gt $effectBefore) "$Case : $hitter's game showed the hit at once (damage effect)"
        }
        else {
            $row.effect = 'none on this creature'
        }
        Invoke-PlayerHit $Simulator $id $hit
        Assert-DeadEverywhere $Case $id 'Stalker'
        $row.dead = -not (Test-Alive $Simulator $id)
    }
    finally {
        Set-DamageOn $false
        Add-Row $row
    }
}

function Test-HandOver {
    param([string]$Case = 'handover-AB')
    if ($Only -and $Only -notcontains $Case) { return }
    $row = [ordered]@{ case = $Case; simulator = $A; max = $null; afterFirst = $null; hitterSees = $false; effect = ''; dead = $false; note = '' }
    $spot = Move-PlayersToNextSpot
    $id = New-Creature $A 'stalker'
    Start-Sleep -Seconds 2
    Assert-Equal $A (Get-LockOwner $id) "$Case : $A simulates the Stalker"
    $max = Get-Number $A "$id LiveMixin.maxHealth"
    $row.max = $max
    $hit = [math]::Round($max * 0.3, 1)
    Set-DamageOn $true
    try {
        Invoke-PlayerHit $A $id $hit
        $afterAlice = Wait-HealthBelow $A $id $max
        Assert-True ($afterAlice -ne $null) "$Case : $A's own hit lowers the Stalker's health ($afterAlice)"
        if ($afterAlice -eq $null) { return }
        Invoke-PlayerHit $B $id $hit
        $summed = Wait-HealthBelow $A $id $afterAlice
        $row.afterFirst = $summed
        Assert-True ($summed -ne $null) "$Case : $B's hit adds to it in $A's game ($summed)"
        if ($summed -eq $null) { return }
        $row.hitterSees = Wait-HealthNear $B $id $summed
        Assert-True $row.hitterSees "$Case : $B's game shows the summed health"

        # Alice no longer sees the Stalker's cell: the server hands its lock to Bob
        Invoke-Bridge $A "teleport $(Format-Point @(($spot[0] + 400), $spot[1], $spot[2]))" | Out-Null
        $owner = Wait-Until { $o = Get-LockOwner $id; if ($o -eq $B) { $o } } -TimeoutSeconds 20
        Assert-Equal $B $owner "$Case : the lock moved to $B"
        Start-Sleep -Seconds 2
        $bobHealth = Get-CreatureHealth $B $id
        $row.note += "after the hand-over: $bobHealth; "
        Assert-True ($bobHealth -ne $null -and [math]::Abs($bobHealth - $summed) -le 0.5) "$Case : $B's game continues from the summed health ($bobHealth, expected $summed)"
        Invoke-PlayerHit $B $id $max
        Assert-DeadEverywhere $Case $id 'Stalker' -Players @($B)
        Invoke-Bridge $A "teleport $(Format-Point $spot)" | Out-Null
        Start-Sleep -Seconds 4
        Assert-True (-not (Test-Loaded $A $id) -or -not (Test-Alive $A $id)) "$Case : back at the spot, $A doesn't have the Stalker alive"
        $row.dead = $true
    }
    finally {
        Set-DamageOn $false
        Add-Row $row
    }
}

function Test-PredatorPrey {
    param([string]$Case = 'predator-prey')
    if ($Only -and $Only -notcontains $Case) { return }
    $row = [ordered]@{ case = $Case; simulator = "$A (Stalker), $B (Peeper)"; max = $null; afterFirst = $null; hitterSees = $false; effect = ''; dead = $false; note = '' }
    Move-PlayersToNextSpot | Out-Null
    $prey = New-Creature $B 'peeper'
    $predator = New-Creature $A 'stalker'
    Start-Sleep -Seconds 2
    Assert-Equal $B (Get-LockOwner $prey) "$Case : $B simulates the Peeper"
    Assert-Equal $A (Get-LockOwner $predator) "$Case : $A simulates the Stalker"
    $streamed = Wait-Until { Test-Streamed $A $prey } -TimeoutSeconds 15
    Assert-True ([bool]$streamed) "$Case : $A's game gets the Peeper from $B's (RemotelyControlled), so her bite alone can't count"
    Show-Facts $A 'stalker' $predator
    if ((Invoke-Bridge $A "get $predator MeleeAttack.canBiteCreature" -AllowError) -ne 'true') {
        # Asset value; only gates the bite, the damage path under test is the same
        Invoke-Bridge $A "set $predator MeleeAttack.canBiteCreature true" | Out-Null
        $row.note += 'canBiteCreature was off, set on; '
    }
    $preyMax = Get-Number $B "$prey LiveMixin.maxHealth"
    $row.max = $preyMax
    Set-DamageOn $true
    try {
        $bites = 0
        while ($bites -lt 5 -and (Test-Loaded $B $prey) -and (Test-Alive $B $prey)) {
            $bites++
            # Mouth next to the Peeper in Alice's game, nothing in between (the bite's raycast)
            $preyThere = Get-Position $A $prey
            Invoke-Bridge $A "set $predator GameObject.transform.position $(($preyThere[0] + 1.2).ToString('0.00', $inv)),$($preyThere[1].ToString('0.00', $inv)),$($preyThere[2].ToString('0.00', $inv))" | Out-Null
            Invoke-Bridge $A "set $predator MeleeAttack.timeLastBite -100" | Out-Null
            Invoke-Bridge $A "set $predator Creature.Aggression.Value 1" | Out-Null
            Invoke-Bridge $A "call $predator MeleeAttack.OnTouch $prey" | Out-Null
            Start-Sleep -Seconds 1.5
            $health = if (Test-Loaded $B $prey) { Get-CreatureHealth $B $prey } else { $null }
            Write-Host "    bite $bites : Peeper in $B's game: $(if ($health -eq $null) { 'gone' } else { $health })" -ForegroundColor DarkGray
            if ($row.afterFirst -eq $null) { $row.afterFirst = $health }
        }
        $row.note += "$bites bites; "
        $hurt = -not (Test-Loaded $B $prey) -or -not (Test-Alive $B $prey) -or ((Get-CreatureHealth $B $prey) -lt $preyMax - 0.1)
        Assert-True $hurt "$Case : the bites of $A's Stalker reach the Peeper in $B's game, which simulates it"
        Assert-DeadEverywhere $Case $prey 'Peeper'
        $row.dead = $true
    }
    finally {
        Set-DamageOn $false
        Remove-Creature $predator
        Add-Row $row
    }
}

function Test-CrashBlast {
    param([string]$Case = 'crash-blast')
    if ($Only -and $Only -notcontains $Case) { return }
    $row = [ordered]@{ case = $Case; simulator = "$A (crash fish), $B (Stalker)"; max = $null; afterFirst = $null; hitterSees = $false; effect = ''; dead = $false; note = '' }
    Move-PlayersToNextSpot | Out-Null
    $stalker = New-Creature $B 'stalker'
    $crash = New-Creature $A 'crash'
    Start-Sleep -Seconds 2
    Assert-Equal $B (Get-LockOwner $stalker) "$Case : $B simulates the Stalker"
    Assert-Equal $A (Get-LockOwner $crash) "$Case : $A simulates the crash fish"
    $streamed = Wait-Until { Test-Streamed $A $stalker } -TimeoutSeconds 15
    Assert-True ([bool]$streamed) "$Case : $A's game gets the Stalker from $B's (RemotelyControlled)"
    $max = Get-Number $B "$stalker LiveMixin.maxHealth"
    $row.max = $max
    Set-DamageOn $true
    try {
        # Both players move out of the blast's 5 m (they are vulnerable now)
        $stalkerThere = Get-Position $A $stalker
        foreach ($player in $A, $B) {
            $offset = if ($player -eq $A) { 10 } else { -10 }
            Invoke-Bridge $player "teleport $(Format-Point @(($stalkerThere[0] + $offset), $stalkerThere[1], ($stalkerThere[2] + 4)))" | Out-Null
        }
        Start-Sleep -Seconds 1
        $stalkerThere = Get-Position $A $stalker
        Invoke-Bridge $A "set $crash GameObject.transform.position $(($stalkerThere[0] + 2).ToString('0.00', $inv)),$($stalkerThere[1].ToString('0.00', $inv)),$($stalkerThere[2].ToString('0.00', $inv))" | Out-Null
        Invoke-Bridge $A "call $crash Crash.Detonate" | Out-Null
        $after = Wait-HealthBelow $B $stalker $max
        $row.afterFirst = $after
        Assert-True ($after -ne $null) "$Case : the blast of $A's crash fish hurts the Stalker in $B's game, which simulates it ($after of $max)"
        if ($after -ne $null) {
            $row.hitterSees = Wait-HealthNear $A $stalker $after
            Assert-True $row.hitterSees "$Case : $A's game shows the Stalker's new health"
        }
    }
    finally {
        Set-DamageOn $false
        Remove-Creature $stalker
        Add-Row $row
    }
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    foreach ($player in $A, $B) {
        $script:PlayerIds[$player] = (Invoke-BridgeJson $player 'state').playerId
        Invoke-Bridge $player 'call GameModeUtils.ActivateCheat NoAggression' | Out-Null
    }
    $pod = (Invoke-BridgeJson $A 'find EscapePod 1') | Select-Object -First 1
    $script:Center = if ($pod) { [double[]](Invoke-BridgeJson $A "entity $($pod.id)").position } else { [double[]](Invoke-BridgeJson $A 'state').position }
    Write-Host "  cases on a 70 m ring around $(Format-Vector $script:Center)"

    Probe 'Bob hits the Stalker Alice simulates, then Alice: it dies' { Test-SummedHits 'sum-AB' $A }
    Probe 'Alice hits the Stalker Bob simulates, then Bob: it dies' { Test-SummedHits 'sum-BA' $B }
    Probe 'Both hit, Alice leaves: Bob continues from the summed health' { Test-HandOver }
    Probe 'Alice''s Stalker bites Bob''s Peeper' { Test-PredatorPrey }
    Probe 'Alice''s crash fish explodes next to Bob''s Stalker' { Test-CrashBlast }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
finally {
    foreach ($player in $A, $B) {
        Invoke-Bridge $player 'call GameModeUtils.DeactivateCheat NoAggression' -AllowError | Out-Null
    }
}
Write-Host ''
Write-Host 'Matrix:'
$script:Rows | Format-Table case, simulator, max, afterFirst, hitterSees, effect, dead, note -AutoSize | Out-String -Width 220 | Write-Host
Complete-Scenario -Name 'M5 CreatureHealth' -Keep:$Keep
