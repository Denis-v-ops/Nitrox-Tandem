<#
    FIX_SPEC S15d: a bleeder simulated by one player's game latches onto the other player.

    Cases (Alice's game simulates every bleeder: she spawns it; Bob is the victim):
      - fact: Bob's body in Alice's game has a bleeder attach target, and it doesn't point at Alice's own player.
      - self-AA (control and mirror): a bleeder touching Alice latches on in her game (the game's own collision); Bob's
        game shows its copy on Alice's body.
      - partner-AB: a bleeder touching Bob's body in Alice's game latches on there; Bob loses health (5 per suck, the
        bleeder as dealer), his game shows the copy on him and marks his own attach target (his arms fight it), the
        copy follows him when he moves; Bob hits it (ArmsController.OnToolBleederHitAnim, an animation event, called
        through the bridge as windowless games don't fire them) until it dies, and it is gone from him in both games.
      - vehicle-AB: latched onto Bob, Bob gets into a Seamoth: it lets go in both games, no more damage.
      - downed-AB: latched onto Bob, Bob goes down (COOP_FEATURES_SPEC T1): it lets go in both games. Last: Bob stays down.
      - stuck-target-B: Bob touches a free bleeder that Alice's game simulates (and that can't latch on there yet): his
        own attach target stays free (it stayed occupied before the fix).
    A contact is forced (the bleeder is put where the player is in the simulating game) after a short natural window:
    bleeders swim at random and attach only on contact.

    Starts a fresh world (save s15d-bleeder) and two games, unless -NoLaunch. Needs a Debug build. Run windowless
    ($env:NITROX_WINDOWLESS = 1).
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's15d-bleeder', [int]$WindowSeconds = 8,
    # Only these cases, e.g. -Only partner-AB
    [string[]]$Only)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'CreatureAggroHelpers.ps1')

function Test-Wanted {
    param([string[]]$Names)
    -not $Only -or @($Names | Where-Object { $Only -contains $_ }).Count -gt 0
}

function Get-Bool {
    param([string]$Player, [string]$Path)
    (Invoke-Bridge $Player "get $Path" -AllowError).Trim() -eq 'true'
}

function Get-PlayerPosition {
    param([string]$Player)
    [double[]](Invoke-BridgeJson $Player 'state').position
}

<#
    Where the player is in a game: the local player there, or their body.
#>
function Get-PlayerPositionIn {
    param([string]$Game, [string]$Player)
    if ($Game -eq $Player) { Get-PlayerPosition $Player } else { Get-Position $Game $script:PlayerIds[$Player] }
}

<#
    The attach target the bleeder holds or approaches, in a game: '' (none), 'local' (that game's own player) or the
    body it belongs to (a player name).
#>
function Get-BleederTarget {
    param([string]$Game, [string]$Id)
    if (-not (Get-Bool $Game "$Id AttachAndSuck.currentTarget.enabled")) { return '' }
    if (Get-Bool $Game "$Id AttachAndSuck.currentTarget.player.enabled") { return 'local' }
    foreach ($player in $A, $B) {
        if ($player -ne $Game -and (Get-Bool $Game "$($script:PlayerIds[$player]) BodyBleederTarget.Target.occupied")) { return $player }
    }
    'unknown'
}

function Get-LocalTargetState {
    param([string]$Game)
    [pscustomobject]@{
        occupied = Get-Bool $Game 'Player.main.armsController.bleederAttackTarget.occupied'
        attached = Get-Bool $Game 'Player.main.armsController.bleederAttackTarget.attached'
        hasBleeder = (Invoke-Bridge $Game 'get Player.main.armsController.bleederAttackTarget.bleeder' -AllowError).Trim() -ne 'null'
    }
}

<#
    Puts both players at the next spot (Alice 30 m away unless -Together), Bob vulnerable, and spawns a bleeder in
    Alice's game next to her. Returns its id.
#>
function New-CaseBleeder {
    param([string]$Name, [switch]$Together)
    $angle = [math]::PI * 2 * $script:NextSpot / 10
    $spot = @(($script:Center[0] + 70 * [math]::Cos($angle)), -6, ($script:Center[2] + 70 * [math]::Sin($angle)))
    $script:NextSpot++
    Write-Host "  case $Name at $(Format-Point $spot)"
    Invoke-Bridge $A "teleport $(Format-Point $spot)" | Out-Null
    Invoke-Bridge $A 'look 0 0' | Out-Null
    Invoke-Bridge $B "teleport $(Format-Point @(($spot[0] + 10), $spot[1], $spot[2]))" | Out-Null
    Start-Sleep -Seconds 3
    foreach ($player in $A, $B) {
        Invoke-Bridge $player 'set player Survival.food 100' | Out-Null
        Invoke-Bridge $player 'set player Survival.water 100' | Out-Null
    }
    $id = New-Creature $A 'bleeder' -Distance 3
    Assert-Equal $A (Get-LockOwner $id) "$Name : Alice simulates the bleeder"
    $id
}

<#
    Waits up to $WindowSeconds for the bleeder to latch onto $Victim in Alice's game, then forces the contact: Alice's
    game puts it where the victim is there (teleports fire triggers and collisions). Returns how it latched on.
#>
function Wait-Latch {
    param([string]$Id, [string]$Victim)
    $expected = if ($Victim -eq $A) { 'local' } else { $Victim }
    $victimThere = Get-PlayerPositionIn $A $Victim
    $bleederPos = Get-Position $A $Id
    Invoke-Bridge $Victim "teleport $(Format-Point @(($bleederPos[0] + 1.5), $bleederPos[1], $bleederPos[2]))" | Out-Null
    $natural = Wait-Until { if ((Get-BleederTarget $A $Id) -eq $expected) { $true } } -TimeoutSeconds $WindowSeconds
    if ($natural) { return 'natural' }
    for ($try = 0; $try -lt 3; $try++) {
        $victimThere = Get-PlayerPositionIn $A $Victim
        Invoke-Bridge $A "set $Id GameObject.transform.position $(($victimThere[0]).ToString('0.00')),$(($victimThere[1]).ToString('0.00')),$(($victimThere[2]).ToString('0.00'))" | Out-Null
        if (Wait-Until { if ((Get-BleederTarget $A $Id) -eq $expected) { $true } } -TimeoutSeconds 3 -IntervalSeconds 0.3) { return 'forced' }
        # Moved off before the trigger saw it: a fresh contact from 1 m away
        Invoke-Bridge $A "set $Id GameObject.transform.position $(($victimThere[0] + 1.5).ToString('0.00')),$(($victimThere[1]).ToString('0.00')),$(($victimThere[2]).ToString('0.00'))" | Out-Null
        Start-Sleep -Milliseconds 500
    }
    'none'
}

<#
    Bob's health in his game since the last call: damage taken and by whom. Heals him back to 100.
#>
function Read-Damage {
    param([string]$Player)
    $health = Get-Health $Player
    if ($health -eq $null -or $health -ge 99.9) { return $null }
    $dealer = Invoke-Bridge $Player 'get Player.main.liveMixin.damageInfo.dealer' -AllowError
    $dealerId = if ($dealer -match '"id":\s*"([0-9a-f-]{36})"') { $Matches[1] } else { $dealer.Trim() }
    Invoke-Bridge $Player 'set Player.main.liveMixin.health 100' | Out-Null
    [pscustomobject]@{ damage = [math]::Round(100 - $health, 2); dealer = $dealerId }
}

function Watch-Damage {
    param([string]$Player, [double]$Seconds)
    $hits = New-Object System.Collections.ArrayList
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $hit = Read-Damage $Player
        if ($hit) { [void]$hits.Add($hit) }
        Start-Sleep -Milliseconds 250
    }
    , $hits
}

function Remove-Bleeder {
    param([string]$Id)
    if (Test-Loaded $A $Id) {
        Invoke-Bridge $A 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
        Invoke-Bridge $A "call $Id LiveMixin.TakeDamage 100000" -AllowError | Out-Null
        Invoke-Bridge $A 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
    }
}

<#
    Latched onto Bob: both games agree, and the copy sits on him in his game.
#>
function Assert-LatchedOnBob {
    param([string]$Name, [string]$Id, [string]$How)
    Assert-True ($How -ne 'none') "$Name : the bleeder latches onto Bob's body in Alice's game ($How)"
    $attachedA = Wait-Until { if (Get-Bool $A "$Id AttachAndSuck.attached") { $true } } -TimeoutSeconds 4 -IntervalSeconds 0.3
    Assert-True ([bool]$attachedA) "$Name : Alice's game: attached to Bob's body"
    $mirrored = Wait-Until { $s = Get-LocalTargetState $B; if ($s.attached -and $s.hasBleeder) { $s } } -TimeoutSeconds 5 -IntervalSeconds 0.3
    Assert-True ([bool]$mirrored) "$Name : Bob's game: his own attach target holds the bleeder (attached, so his arms fight it)"
    Assert-Equal 'local' (Get-BleederTarget $B $Id) "$Name : Bob's game: the copy's target is Bob"
    $distance = Get-Distance (Get-Position $B $Id) (Get-PlayerPosition $B)
    Assert-True ($distance -lt 1.5) "$Name : Bob's game: the copy sits on Bob ($([math]::Round($distance, 2)) m)"
}

<#
    Not latched onto Bob any more, in either game, and no more damage.
#>
function Assert-LetGo {
    param([string]$Name, [string]$Id, [string]$Why, [switch]$Downed)
    $freeA = Wait-Until { if ((Get-BleederTarget $A $Id) -ne $B -and -not (Get-Bool $A "$($script:PlayerIds[$B]) BodyBleederTarget.Target.occupied")) { $true } } -TimeoutSeconds 5 -IntervalSeconds 0.3
    Assert-True ([bool]$freeA) "$Name : Alice's game: the bleeder let go of Bob's body ($Why)"
    $freeB = Wait-Until { $s = Get-LocalTargetState $B; if (-not $s.occupied -and -not $s.attached) { $true } } -TimeoutSeconds 5 -IntervalSeconds 0.3
    Assert-True ([bool]$freeB) "$Name : Bob's game: his own attach target is free ($Why)"
    if (-not $Downed) {
        Invoke-Bridge $B 'set Player.main.liveMixin.health 100' | Out-Null
        $after = Watch-Damage $B 3
        $bled = @($after | Where-Object { $_.dealer -eq $Id })
        Assert-Equal 0 $bled.Count "$Name : no more sucks on Bob ($Why)"
    }
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    foreach ($player in $A, $B) {
        $script:PlayerIds[$player] = (Invoke-BridgeJson $player 'state').playerId
        Write-Host "  $player : player id $($script:PlayerIds[$player])"
    }
    $pod = (Invoke-BridgeJson $A 'find EscapePod 1') | Select-Object -First 1
    $script:Center = if ($pod) { [double[]](Invoke-BridgeJson $A "entity $($pod.id)").position } else { [double[]](Invoke-BridgeJson $A 'state').position }

    Step 'Bob''s body in Alice''s game has its own bleeder attach target' {
        $bobId = $script:PlayerIds[$B]
        Assert-True (Get-Bool $A "$bobId BodyBleederTarget.Target.enabled") 'the body has a BleederAttachTarget'
        Assert-Equal 'null' (Invoke-Bridge $A "get $bobId BodyBleederTarget.Target.player" -AllowError).Trim() 'it does not point at Alice''s own player'
    }

    if (Test-Wanted 'self-AA') {
        Step 'Control: a bleeder latches onto Alice in her game; Bob''s game shows it on her body' {
            $id = New-CaseBleeder 'self-AA'
            try {
                $how = Wait-Latch $id $A
                Assert-True ($how -ne 'none') "self-AA : the bleeder latches onto Alice ($how)"
                Assert-True ([bool](Wait-Until { if (Get-Bool $A "$id AttachAndSuck.attached") { $true } } -TimeoutSeconds 4 -IntervalSeconds 0.3)) 'self-AA : attached in Alice''s game'
                $mirrored = Wait-Until { if ((Get-BleederTarget $B $id) -eq $A -and (Get-Bool $B "$id AttachAndSuck.attached")) { $true } } -TimeoutSeconds 5 -IntervalSeconds 0.3
                Assert-True ([bool]$mirrored) 'self-AA : Bob''s game shows the copy attached to Alice''s body'
                $distance = Get-Distance (Get-Position $B $id) (Get-Position $B $script:PlayerIds[$A])
                Assert-True ($distance -lt 1.5) "self-AA : Bob's game: the copy sits on Alice's body ($([math]::Round($distance, 2)) m)"
                Assert-True (-not (Get-LocalTargetState $B).occupied) 'self-AA : Bob''s own attach target stays free'
            }
            finally { Remove-Bleeder $id }
        }
    }

    if (Test-Wanted 'partner-AB') {
        Step 'A bleeder simulated by Alice latches onto Bob, sucks his blood and is knocked off by him' {
            Set-Vulnerable $B $true
            $id = New-CaseBleeder 'partner-AB'
            try {
                $how = Wait-Latch $id $B
                Assert-LatchedOnBob 'partner-AB' $id $how
                Invoke-Bridge $B 'set Player.main.liveMixin.health 100' | Out-Null
                $hits = Watch-Damage $B 4
                $own = @($hits | Where-Object { $_.dealer -eq $id })
                $other = @($hits | Where-Object { $_.dealer -ne $id })
                foreach ($h in $other) { Write-Host "    other damage: $($h.damage) by $($h.dealer), not counted" -ForegroundColor DarkYellow }
                Write-Host "    sucks on Bob in 4 s: $(($own | ForEach-Object { $_.damage }) -join ', ')"
                Assert-True ($own.Count -ge 2) "partner-AB : Bob loses health to the bleeder, the bleeder as the dealer ($($own.Count) sucks in 4 s)"
                Assert-True (@($own | Where-Object { $_.damage -gt 5.3 }).Count -eq 0) 'partner-AB : no suck is bigger than one (5)'

                # He swims on: the copy goes with him in his game, Alice's bleeder stays on his body
                $bobPos = Get-PlayerPosition $B
                Invoke-Bridge $B "teleport $(Format-Point @(($bobPos[0] + 3), $bobPos[1], ($bobPos[2] + 2)))" | Out-Null
                Start-Sleep -Seconds 2
                $distanceB = Get-Distance (Get-Position $B $id) (Get-PlayerPosition $B)
                Assert-True ($distanceB -lt 1.5) "partner-AB : after Bob moved, his game still shows it on him ($([math]::Round($distanceB, 2)) m)"
                $distanceA = Get-Distance (Get-Position $A $id) (Get-Position $A $script:PlayerIds[$B])
                Assert-True ($distanceA -lt 1.5 -and (Get-BleederTarget $A $id) -eq $B) "partner-AB : Alice's game: still on Bob's body after he moved ($([math]::Round($distanceA, 2)) m)"

                # Knock it off: the fight animation's hit, until it dies
                $hitsDealt = 0
                while ($hitsDealt -lt 15 -and (Test-Loaded $B $id) -and (Get-Number $B "$id LiveMixin.health") -gt 0) {
                    Invoke-Bridge $B 'call Player.main.armsController.OnToolBleederHitAnim null' | Out-Null
                    $hitsDealt++
                    Start-Sleep -Milliseconds 200
                }
                Write-Host "    Bob hit it $hitsDealt times"
                Assert-True ($hitsDealt -lt 15) "partner-AB : Bob's hits kill the bleeder in his game ($hitsDealt hits)"
                $deadA = Wait-Until { if (-not (Test-Loaded $A $id) -or (Get-Number $A "$id LiveMixin.health") -le 0) { $true } } -TimeoutSeconds 5
                Assert-True ([bool]$deadA) 'partner-AB : it is dead in Alice''s game too'
                Assert-LetGo 'partner-AB' $id 'knocked off'
            }
            finally {
                Remove-Bleeder $id
                Set-Vulnerable $B $false
            }
        }
    }

    if (Test-Wanted 'vehicle-AB') {
        Step 'Latched onto Bob, Bob gets into a Seamoth: it lets go' {
            Set-Vulnerable $B $true
            $id = New-CaseBleeder 'vehicle-AB'
            try {
                # The Seamoth first, 6 m in front of Bob, so the attach isn't spent on the way
                $bobPos = Get-PlayerPosition $B
                Invoke-Bridge $B 'look 0 0' | Out-Null
                $before = @((Invoke-BridgeJson $B 'find SeaMoth 200') | ForEach-Object { $_.id })
                Invoke-Bridge $B 'console spawn seamoth 1 6' | Out-Null
                $seamoth = Wait-Until { (Invoke-BridgeJson $B 'find SeaMoth 200') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
                Assert-True ([bool]$seamoth) 'vehicle-AB : a Seamoth next to Bob'
                $how = Wait-Latch $id $B
                Assert-LatchedOnBob 'vehicle-AB' $id $how
                $seamothPos = Get-Position $B $seamoth.id
                Invoke-Bridge $B "teleport $(Format-Point @(($seamothPos[0]), ($seamothPos[1] + 2), ($seamothPos[2])))" | Out-Null
                Start-Sleep -Milliseconds 500
                Invoke-Bridge $B "use $($seamoth.id)" | Out-Null
                $inside = Wait-Until { $s = Invoke-BridgeJson $B 'state'; if ($s.vehicle) { $true } } -TimeoutSeconds 10
                Assert-True ([bool]$inside) 'vehicle-AB : Bob pilots the Seamoth'
                Assert-LetGo 'vehicle-AB' $id 'in a vehicle'
            }
            finally {
                Invoke-Bridge $B 'call player Player.ExitLockedMode false true' -AllowError | Out-Null
                Wait-Until { -not (Invoke-BridgeJson $B 'state').vehicle } -TimeoutSeconds 10 | Out-Null
                Remove-Bleeder $id
                Set-Vulnerable $B $false
            }
        }
    }

    if (Test-Wanted 'stuck-target-B') {
        Step 'Bob touches a free bleeder that Alice''s game simulates: his own attach target stays free' {
            $id = New-CaseBleeder 'stuck-target-B'
            try {
                # In Alice's game it can't latch on (just let go: 4 s pause), so only Bob's game could mark him
                Invoke-Bridge $A "set $id AttachAndSuck.timeDetached 100000" | Out-Null
                $touched = $false
                for ($try = 0; $try -lt 4; $try++) {
                    $bleederPos = Get-Position $B $id
                    Invoke-Bridge $B "teleport $(Format-Point @(($bleederPos[0] + 0.3), $bleederPos[1], $bleederPos[2]))" | Out-Null
                    Start-Sleep -Milliseconds 700
                    $distance = Get-Distance (Get-Position $B $id) (Get-PlayerPosition $B)
                    if ($distance -lt 1.2) { $touched = $true }
                }
                Assert-True $touched 'stuck-target-B : Bob touched the copy in his game'
                $state = Get-LocalTargetState $B
                Assert-True (-not $state.occupied -and -not $state.hasBleeder) "stuck-target-B : Bob's own attach target stays free (occupied $($state.occupied))"
                Assert-Equal '' (Get-BleederTarget $A $id) 'stuck-target-B : Alice''s game: no target'
            }
            finally { Remove-Bleeder $id }
        }
    }

    if (Test-Wanted 'downed-AB') {
        Step 'Latched onto Bob, Bob goes down: it lets go (Bob stays down; last case)' {
            Set-Vulnerable $B $true
            $id = New-CaseBleeder 'downed-AB'
            try {
                $how = Wait-Latch $id $B
                Assert-LatchedOnBob 'downed-AB' $id $how
                Invoke-Bridge $B 'call player LiveMixin.TakeDamage 500' | Out-Null
                $down = Wait-Until { if ((Invoke-Bridge $B 'get RescueWindow.State').Trim().Trim('"') -ne 'UP') { $true } } -TimeoutSeconds 5 -IntervalSeconds 0.3
                Assert-True ([bool]$down) 'downed-AB : Bob is down'
                Assert-LetGo 'downed-AB' $id 'downed' -Downed
            }
            finally { Remove-Bleeder $id }
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S15d BleederOnPartner' -Keep:$Keep
