<#
.SYNOPSIS
    FIX_SPEC S14 (#2781): aggressive creatures attack both players, whoever simulates them, and a bite hurts the bitten
    player exactly once.

.DESCRIPTION
    Each case spawns a creature from the game that should simulate it (console "spawn" gives the spawner the lock;
    checked with the server's "query"), puts the victim next to it and the other player away (or next to it too),
    and watches for up to -WindowSeconds:
      - the simulator's LastTarget.target and Creature.Aggression (does it pick the victim?),
      - the victim's own Player.liveMixin.health (does damage arrive?), healed back to 100 after every hit,
      - the size of every hit compared with the creature's MeleeAttack.biteDamage (a hit of about twice that, or two
        hits within half a second, means the bite was applied twice).
    The victim's no-damage cheat is off from before the spawn, and a spawning simulator that isn't the victim is
    invisible to creatures (NoAggression) until it has moved away: a creature bites once and then pauses for up to
    20 s (AttackLastTarget.pauseInterval), so the first bite must count. The creature is killed after its case. A
    matrix of all cases is printed at the end.

    Cases: Stalker A->A (vanilla control), Stalker A->B (Alice simulates, Bob is the victim), Stalker B->A, Stalker
    with both players next to it (either may be bitten), Stalker A->B while Bob's game also holds the Stalker
    aggressive (double-bite guard), Biter, Sandshark, Bone shark and Crashfish A->B, Reaper A->B (aggression-synced
    creature: Bob's own game bites him), and "far": Alice spawns a Stalker next to Bob and leaves for 400 m, the lock
    moves to Bob and the Stalker attacks him.

    Starts a fresh world (save s14-aggro) and two games (opens windows), unless -NoLaunch. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's14-aggro', [int]$WindowSeconds = 30,
    # Only these cases (names from the matrix), e.g. -Only stalker-AB,biter-AB
    [string[]]$Only)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'CreatureAggroHelpers.ps1')

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    foreach ($player in $A, $B) {
        $script:PlayerIds[$player] = (Invoke-BridgeJson $player 'state').playerId
        Write-Host "  $player : player id $($script:PlayerIds[$player]), local EcoTargets: $(Invoke-Bridge $player 'get player EcoTarget.type')"
    }
    $pod = (Invoke-BridgeJson $A 'find EscapePod 1') | Select-Object -First 1
    $script:Center = if ($pod) { [double[]](Invoke-BridgeJson $A "entity $($pod.id)").position } else { [double[]](Invoke-BridgeJson $A 'state').position }
    Write-Host "  cases on a 70 m ring around $(Format-Vector $script:Center)"

    Step 'Stalker simulated by Alice attacks Alice (vanilla control)' {
        Test-AggroCase 'stalker-AA' 'stalker' $A $A
    }
    Step 'Stalker simulated by Alice attacks Bob' {
        Test-AggroCase 'stalker-AB' 'stalker' $A $B
    }
    Step 'Stalker simulated by Bob attacks Alice' {
        Test-AggroCase 'stalker-BA' 'stalker' $B $A
    }
    Step 'Stalker simulated by Alice, both players next to it' {
        Test-AggroCase 'stalker-both' 'stalker' $A $B -Away 0
    }
    Step 'Stalker simulated by Alice attacks Bob, while Bob''s game also thinks it is aggressive' {
        Test-AggroCase 'stalker-AB-double' 'stalker' $A $B -VictimGameAggressive
    }
    Step 'Biter simulated by Alice attacks Bob' {
        Test-AggroCase 'biter-AB' 'biter' $A $B
    }
    Step 'Sandshark simulated by Alice attacks Bob' {
        Test-AggroCase 'sandshark-AB' 'sandshark' $A $B -MovingVictim
    }
    Step 'Bone shark simulated by Alice attacks Bob' {
        Test-AggroCase 'boneshark-AB' 'boneshark' $A $B
    }
    Step 'Crashfish simulated by Alice attacks Bob' {
        # 3 m: inside the crash fish's short-range AggressiveWhenSeePlayer (4 m), which now sees other players too.
        Test-AggroCase 'crash-AB' 'crash' $A $B -Explosion -VictimDistance 3
    }
    Step 'Reaper simulated by Alice attacks Bob' {
        # Near the pod it is too shallow for a Reaper (it keeps avoiding terrain); this spot worked in a probe.
        Test-AggroCase 'reaper-AB' 'reaperleviathan' $A $B -Spot @(-150, -10, 300) -Window 60 -TouchInVictimGame
    }
    Step 'Stalker next to Bob while its simulator Alice is 400 m away' {
        # Alice no longer sees the Stalker's cell: the server hands its lock to Bob, whose game then runs its AI.
        Test-AggroCase 'stalker-far' 'stalker' $A $B -Away 400 -SettleSeconds 8 -ExpectOwner $B
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Write-Host ''
Write-Host 'Matrix (owner = lock holder from the server right after the setup):'
$script:Matrix | Format-Table case, creature, simulator, victim, owner, targeted, maxAggression, hits, how, damage, maxHit, biteDamage, note -AutoSize | Out-String -Width 260 | Write-Host
Complete-Scenario -Name 'S14 CreatureAggro' -Keep:$Keep
