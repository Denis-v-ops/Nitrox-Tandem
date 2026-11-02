<#
.SYNOPSIS
    COOP_FEATURES_SPEC D4: do the creatures S14 didn't run hurt the player whose game doesn't simulate them? And does a
    creature hurt a vehicle its pilot's game doesn't simulate?

.DESCRIPTION
    Same method as S14-CreatureAggro.ps1 (shared helpers in CreatureAggroHelpers.ps1): the simulating game spawns the
    creature, the victim floats next to it, the case waits for a natural attack and forces the contact only as a
    fallback. A->B means Alice's game simulates the creature and Bob is the victim.

    Cases:
      - crabsnake-AB/BA, river prowler (spineeel-AB), crabsquid-AB (bite), lavalizard-AB (bite): plain S14 bites.
      - ampeel-AB/BA: the shock (ShockerMeleeAttack.OnTouch, Electrical, no dealer) happens wherever the eel touches
        the local player; the bite (OnMouthTouch) goes the S14 way.
      - warper-AB: the warper bites only infected players (> 0.33), so Bob is infected first.
      - mesmer-AA (control) and mesmer-AB: hypnosis (PullCreatures) before the bite; reported: the pull's target in the
        simulating game and Player.mesmerizedSpeedMultiplier in the victim's game.
      - lavalizard-ranged-AB (report only): lava shell on, victim 15 m away, a ranged attack?
      - bleeder-AA / bleeder-AB (report only): a spawned bleeder attaching to its victim.
      - stalker-born-AB: a Stalker born in a containment (friendly to players) must not bite Bob, even when touched.
      - vehicle-BB (control), vehicle-AB, vehicle-BA: a Bone shark bites the Seamoth its simulator's partner pilots.
        The Seamoth's health is read in the pilot's game, which holds the Seamoth's lock.
      - emp-BB (control), emp-AB: a Crabsquid's EMP blast next to the piloted Seamoth: are its electronics disabled in
        the pilot's game?

    Starts a fresh world (save d4-threat) and two games, unless -NoLaunch. Needs a Debug build. Run windowless
    ($env:NITROX_WINDOWLESS = 1).
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'd4-threat', [int]$WindowSeconds = 30,
    # Only these cases (names from the matrix), e.g. -Only ampeel-AB,vehicle-AB
    [string[]]$Only,
    # The creature that bites the Seamoth in the vehicle cases.
    [string]$VehicleBiter = 'boneshark')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'CreatureAggroHelpers.ps1')
$script:VehicleMatrix = New-Object System.Collections.ArrayList

# A probe case that fails or throws is recorded and the next case still runs (Step alone stops the scenario).
function Probe {
    param([string]$Title, [scriptblock]$Body)
    try { Step $Title $Body } catch { Write-Host "  (continuing)" -ForegroundColor DarkGray }
}

function Get-Other {
    param([string]$Player)
    if ($Player -eq $A) { $B } else { $A }
}

function Test-Wanted {
    param([string[]]$Names)
    -not $Only -or @($Names | Where-Object { $Only -contains $_ }).Count -gt 0
}

<#
    The pilot spawns a Seamoth in front of them and gets in. Returns its id.
#>
function New-PilotedSeamoth {
    param([string]$Pilot, [double[]]$Spot)
    $other = Get-Other $Pilot
    Invoke-Bridge $Pilot "teleport $(Format-Point $Spot)" | Out-Null
    Invoke-Bridge $Pilot 'look 0 0' | Out-Null
    Start-Sleep -Seconds 2
    $before = @((Invoke-BridgeJson $Pilot 'find SeaMoth 200') | ForEach-Object { $_.id })
    Invoke-Bridge $Pilot 'console spawn seamoth 1 6' | Out-Null
    $new = Wait-Until { (Invoke-BridgeJson $Pilot 'find SeaMoth 200') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
    if (-not $new) { throw "the Seamoth didn't spawn" }
    $seen = Wait-Until { Test-Loaded $other $new.id } -TimeoutSeconds 15
    Assert-True ([bool]$seen) "$other has the Seamoth $($new.id)"
    Enter-Seamoth $Pilot $new.id
    $new.id
}

function Enter-Seamoth {
    param([string]$Pilot, [string]$SeamothId)
    $seamothPos = Get-Position $Pilot $SeamothId
    Invoke-Bridge $Pilot "teleport $(Format-Point @(($seamothPos[0]), ($seamothPos[1] + 2), ($seamothPos[2])))" | Out-Null
    Start-Sleep -Seconds 1
    Invoke-Bridge $Pilot "use $SeamothId" | Out-Null
    $inside = Wait-Until { $s = Invoke-BridgeJson $Pilot 'state'; if ($s.vehicle) { $true } } -TimeoutSeconds 10
    Assert-True ([bool]$inside) "$Pilot pilots the Seamoth $SeamothId"
    $owner = Wait-Until { $o = Get-LockOwner $SeamothId; if ($o -eq $Pilot) { $o } } -TimeoutSeconds 10
    Assert-Equal $Pilot $owner "$Pilot holds the Seamoth's lock"
}

function Exit-Seamoth {
    param([string]$Pilot)
    Invoke-Bridge $Pilot 'call player Player.ExitLockedMode false true' -AllowError | Out-Null
    Wait-Until { -not (Invoke-BridgeJson $Pilot 'state').vehicle } -TimeoutSeconds 10 | Out-Null
}

function Get-SeamothHealth {
    param([string]$Player, [string]$SeamothId)
    Get-Number $Player "$SeamothId LiveMixin.health"
}

<#
    The simulator spawns a creature about 8 m from the Seamoth (from 16 m behind it, or from the pilot's seat), then
    leaves (30 m, invisible to creatures) unless it is the pilot. Returns the creature's id.
#>
function New-CreatureNearSeamoth {
    param([string]$Simulator, [string]$Pilot, [string]$SeamothId, [string]$TechType)
    $seamothPos = Get-Position $Pilot $SeamothId
    if ($Simulator -ne $Pilot) {
        Invoke-Bridge $Simulator 'call GameModeUtils.ActivateCheat NoAggression' | Out-Null
        Invoke-Bridge $Simulator "teleport $(Format-Point @(($seamothPos[0]), ($seamothPos[1]), ($seamothPos[2] - 16)))" | Out-Null
        Invoke-Bridge $Simulator 'look 0 0' | Out-Null
        Start-Sleep -Seconds 2
        $id = New-Creature $Simulator $TechType -Distance 8
        Invoke-Bridge $Simulator "teleport $(Format-Point @(($seamothPos[0] + 30), ($seamothPos[1]), ($seamothPos[2] - 16)))" | Out-Null
    }
    else {
        # The other player waits 30 m away, near enough to load the creature.
        Invoke-Bridge (Get-Other $Pilot) "teleport $(Format-Point @(($seamothPos[0] + 30), ($seamothPos[1]), ($seamothPos[2] - 16)))" | Out-Null
        Start-Sleep -Seconds 2
        $id = New-Creature $Simulator $TechType -Distance 10
    }
    Invoke-Bridge $Simulator "set $id Creature.Hunger.Value 0.6" -AllowError | Out-Null
    Show-Facts $Simulator $TechType $id
    $id
}

function Remove-Creature {
    param([string]$Id)
    $owner = Get-LockOwner $Id
    $killer = if ($owner -eq $B) { $B } else { $A }
    if (Test-Loaded $killer $Id) {
        Invoke-Bridge $killer 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
        Invoke-Bridge $killer "call $Id LiveMixin.TakeDamage 100000" -AllowError | Out-Null
        Invoke-Bridge $killer 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
    }
}

<#
    A creature simulated by $Simulator bites the Seamoth $Pilot pilots. A hit = the Seamoth's health dropping in the
    pilot's game (which holds the Seamoth's lock and broadcasts its health). Natural first, then the forced contact in
    the simulating game: the creature next to the Seamoth and its MeleeAttack.OnTouch with the Seamoth's collider.
#>
function Test-VehicleBiteCase {
    param([string]$Name, [string]$TechType, [string]$Simulator, [string]$Pilot, [string]$SeamothId, [int]$Window = $WindowSeconds, [switch]$ReportOnly)
    if ($Only -and $Only -notcontains $Name) { return }
    Write-Host "  case $Name : $TechType simulated by $Simulator bites the Seamoth piloted by $Pilot"
    $row = [ordered]@{ case = $Name; creature = $TechType; simulator = $Simulator; pilot = $Pilot; owner = '?'; vehicleOwner = '?'; targeted = $false; maxAggression = 0.0; hits = 0; damage = 0.0; how = ''; drops = ''; maxDrop = 0.0; biteDamage = $null; pilotGameHealth = $null; simulatorGameHealth = $null; pilotBodyHits = 0; note = '' }
    $maxHealth = Get-Number $Pilot "$SeamothId LiveMixin.maxHealth"
    Invoke-Bridge $Pilot "set $SeamothId LiveMixin.health $maxHealth" | Out-Null
    Set-Vulnerable $Pilot $true
    $id = New-CreatureNearSeamoth $Simulator $Pilot $SeamothId $TechType
    try {
        Start-Sleep -Seconds 2
        $row.owner = Get-LockOwner $id
        $row.vehicleOwner = Get-LockOwner $SeamothId
        $start = Get-SeamothHealth $Pilot $SeamothId
        $deadline = (Get-Date).AddSeconds($Window)
        $poll = 0
        while ((Get-Date) -lt $deadline -and $row.hits -lt 1) {
            $poll++
            $target = Invoke-Bridge $Simulator "get $id LastTarget.target.name" -AllowError
            if ($target -match 'SeaMoth') { $row.targeted = $true }
            $aggression = Get-Number $Simulator "$id Creature.Aggression.Value"
            if ($aggression -ne $null -and $aggression -gt $row.maxAggression) { $row.maxAggression = [math]::Round($aggression, 2) }
            if (($poll % 6) -eq 1) {
                $distance = try { [math]::Round((Get-Distance (Get-Position $Simulator $id) (Get-Position $Simulator $SeamothId)), 1) } catch { '?' }
                Write-Host "    t+$([int]((Get-Date) - $deadline.AddSeconds(-$Window)).TotalSeconds) s: aggression $([math]::Round([double]$aggression, 2)), target $target, $distance m from the Seamoth" -ForegroundColor DarkGray
            }
            $now = Get-SeamothHealth $Pilot $SeamothId
            if ($now -ne $null -and $now -lt $start - 0.5) { $row.hits++; $row.damage = [math]::Round($start - $now, 1); $row.how = 'natural' }
            if ((Get-Health $Pilot) -lt 99.9) { $row.pilotBodyHits++; Invoke-Bridge $Pilot 'set Player.main.liveMixin.health 100' | Out-Null }
            Start-Sleep -Milliseconds 300
        }
        if ($row.hits -eq 0 -and -not $ReportOnly) {
            Write-Host "    no natural bite in $Window s: touching the Seamoth with the $TechType in ${Simulator}'s game" -ForegroundColor DarkGray
            $seamothThere = Get-Position $Simulator $SeamothId
            Invoke-Bridge $Simulator "set $id GameObject.transform.position $(($seamothThere[0] + 3).ToString('0.00')),$($seamothThere[1].ToString('0.00')),$($seamothThere[2].ToString('0.00'))" | Out-Null
            Start-Sleep -Seconds 1.5
            Invoke-Bridge $Simulator "set $id MeleeAttack.timeLastBite -100" | Out-Null
            Invoke-Bridge $Simulator "set $id Creature.Aggression.Value 1" | Out-Null
            Invoke-Bridge $Simulator "set $id LastTarget.target $SeamothId" -AllowError | Out-Null
            Invoke-Bridge $Simulator "call $id MeleeAttack.OnTouch $SeamothId" | Out-Null
            $forcedDeadline = (Get-Date).AddSeconds(3)
            while ((Get-Date) -lt $forcedDeadline -and $row.hits -lt 1) {
                $now = Get-SeamothHealth $Pilot $SeamothId
                if ($now -ne $null -and $now -lt $start - 0.5) { $row.hits++; $row.damage = [math]::Round($start - $now, 1); $row.how = 'touch' }
                Start-Sleep -Milliseconds 300
            }
        }
        # Every further drop for a while: none may be bigger than one bite, nor two land at once (a double bite).
        $last = Get-SeamothHealth $Pilot $SeamothId
        $lastDropAt = Get-Date
        $drops = @($row.damage)
        $watchUntil = (Get-Date).AddSeconds(4)
        while ($row.hits -ge 1 -and (Get-Date) -lt $watchUntil) {
            Start-Sleep -Milliseconds 200
            $now = Get-SeamothHealth $Pilot $SeamothId
            if ($now -ne $null -and $now -lt $last - 0.5) {
                $drops += [math]::Round($last - $now, 1)
                if (((Get-Date) - $lastDropAt).TotalSeconds -lt 0.5) { $row.note += 'two drops <0.5 s apart; ' }
                $lastDropAt = Get-Date
                $last = $now
            }
        }
        $row.drops = $drops -join ' '
        $row.maxDrop = ($drops | Measure-Object -Maximum).Maximum
        $row.pilotGameHealth = Get-SeamothHealth $Pilot $SeamothId
        $row.simulatorGameHealth = Get-SeamothHealth $Simulator $SeamothId
        $row.biteDamage = Get-BiteDamage $TechType
        if ($row.pilotBodyHits -gt 0) { $row.note += "pilot's body hurt $($row.pilotBodyHits)x; " }
    }
    finally {
        Invoke-Bridge $Simulator 'call GameModeUtils.DeactivateCheat NoAggression' | Out-Null
        Set-Vulnerable $Pilot $false
        Remove-Creature $id
        Invoke-Bridge $Pilot "set $SeamothId LiveMixin.health $maxHealth" | Out-Null
        [void]$script:VehicleMatrix.Add([pscustomobject]$row)
    }
    if ($ReportOnly) { return }
    Assert-Equal $Simulator $row.owner "$Name : $Simulator simulates the $TechType"
    Assert-Equal $Pilot $row.vehicleOwner "$Name : $Pilot (the pilot) holds the Seamoth's lock"
    Assert-True ($row.hits -ge 1) "$Name : the $TechType damages $Pilot's Seamoth in $Pilot's game ($($row.how), $($row.damage) damage)"
    Assert-Equal 0 $row.pilotBodyHits "$Name : the pilot's body isn't bitten through the hull"
    if ($row.biteDamage) {
        Assert-True ($row.maxDrop -le $row.biteDamage * 1.05 + 0.1) "$Name : no drop is bigger than one bite (drops $($row.drops), biteDamage $($row.biteDamage))"
    }
    Assert-True (-not $row.note.Contains('two drops')) "$Name : no bite lands twice"
    Assert-True ([math]::Abs([double]$row.pilotGameHealth - [double]$row.simulatorGameHealth) -lt 0.5) "$Name : $Simulator's game shows the Seamoth's new health ($($row.simulatorGameHealth), pilot's game $($row.pilotGameHealth))"
}

<#
    A Crabsquid simulated by $Simulator casts its EMP blast right next to the Seamoth $Pilot pilots. Reported: whether
    the Seamoth's electronics are disabled in each game (EnergyMixin.electronicsDisabled of its power source).
#>
function Test-EmpCase {
    param([string]$Name, [string]$Simulator, [string]$Pilot, [string]$SeamothId, [switch]$ReportOnly)
    if ($Only -and $Only -notcontains $Name) { return }
    Write-Host "  case $Name : Crabsquid simulated by $Simulator blasts the Seamoth piloted by $Pilot"
    $row = [ordered]@{ case = $Name; creature = 'crabsquid'; simulator = $Simulator; pilot = $Pilot; owner = '?'; vehicleOwner = '?'; targeted = $false; maxAggression = 0.0; hits = 0; damage = 0.0; how = ''; simulatorGameDamage = 0.0; pilotBodyHits = 0; note = '' }
    $id = New-CreatureNearSeamoth $Simulator $Pilot $SeamothId 'crabsquid'
    try {
        Start-Sleep -Seconds 2
        $row.owner = Get-LockOwner $id
        $row.vehicleOwner = Get-LockOwner $SeamothId
        $seamothThere = Get-Position $Simulator $SeamothId
        Invoke-Bridge $Simulator "set $id GameObject.transform.position $(($seamothThere[0] + 4).ToString('0.00')),$($seamothThere[1].ToString('0.00')),$($seamothThere[2].ToString('0.00'))" | Out-Null
        Invoke-Bridge $Simulator "call $id EMPAttack.CastEMPBlast" | Out-Null
        $disabled = @{ $A = $false; $B = $false }
        $deadline = (Get-Date).AddSeconds(6)
        while ((Get-Date) -lt $deadline) {
            foreach ($player in $A, $B) {
                if ((Invoke-Bridge $player "get $SeamothId EnergyInterface.sources[0].electronicsDisabled" -AllowError) -eq 'true') { $disabled[$player] = $true }
            }
            Start-Sleep -Milliseconds 250
        }
        $row.how = "disabled in Alice's game: $($disabled[$A]), in Bob's: $($disabled[$B])"
        if ($disabled[$Pilot]) { $row.hits = 1 }
        Write-Host "    $($row.how)"
    }
    finally {
        Invoke-Bridge $Simulator 'call GameModeUtils.DeactivateCheat NoAggression' | Out-Null
        Remove-Creature $id
        [void]$script:VehicleMatrix.Add([pscustomobject]$row)
    }
    if ($ReportOnly) { return }
    Assert-True ($row.hits -ge 1) "$Name : the EMP disables the Seamoth's electronics in $Pilot's (the pilot's) game ($($row.how))"
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    foreach ($player in $A, $B) {
        $script:PlayerIds[$player] = (Invoke-BridgeJson $player 'state').playerId
        Write-Host "  $player : player id $($script:PlayerIds[$player])"
    }
    $pod = (Invoke-BridgeJson $A 'find EscapePod 1') | Select-Object -First 1
    $script:Center = if ($pod) { [double[]](Invoke-BridgeJson $A "entity $($pod.id)").position } else { [double[]](Invoke-BridgeJson $A 'state').position }
    Write-Host "  cases on a 70 m ring around $(Format-Vector $script:Center)"

    Probe 'Crabsnake simulated by Alice attacks Bob' {
        Test-AggroCase 'crabsnake-AB' 'crabsnake' $A $B
    }
    Probe 'Crabsnake simulated by Bob attacks Alice' {
        Test-AggroCase 'crabsnake-BA' 'crabsnake' $B $A
    }
    Probe 'Ampeel simulated by Alice attacks Bob' {
        # The shock passes no dealer (Electrical); the forced contact is the mouth's, which shocks and bites. Its scan for
        # players only sees targets faster than 1 m/s (minimumVelocity, an asset value): the victim keeps moving.
        Test-AggroCase 'ampeel-AB' 'shocker' $A $B -DamageType 'Electrical' -TouchMethod 'ShockerMeleeAttack.OnMouthTouch' -MovingVictim
    }
    Probe 'Ampeel simulated by Bob attacks Alice' {
        Test-AggroCase 'ampeel-BA' 'shocker' $B $A -DamageType 'Electrical' -TouchMethod 'ShockerMeleeAttack.OnMouthTouch' -MovingVictim
    }
    Probe 'River prowler simulated by Alice attacks Bob' {
        Test-AggroCase 'spineeel-AB' 'spineeel' $A $B
    }
    Probe 'Crabsquid simulated by Alice bites Bob' {
        Test-AggroCase 'crabsquid-AB' 'crabsquid' $A $B
    }
    Probe 'Lava lizard simulated by Alice bites Bob' {
        Test-AggroCase 'lavalizard-AB' 'lavalizard' $A $B
    }
    Probe 'Warper simulated by Alice attacks an infected Bob' {
        if (Test-Wanted 'warper-AB') {
            try {
                # WarperMeleeAttack.CanBite: only targets with an infection above 0.33 (the body's InfectedMixin is synced).
                Invoke-Bridge $B 'set Player.main.infectedMixin.infectedAmount 0.5' | Out-Null
                $synced = Wait-Until { $v = Get-Number $A "$($script:PlayerIds[$B]) InfectedMixin.infectedAmount"; if ($v -ge 0.45) { $v } } -TimeoutSeconds 20
                Assert-True ([bool]$synced) "Bob's infection ($synced) reached his body in Alice's game"
                Test-AggroCase 'warper-AB' 'warper' $A $B -Watch {
                    param($id, $owner)
                    if (-not $row.extra.Contains('ranged') -and (Invoke-Bridge $owner "get $id RangedAttackLastTarget.isActive" -AllowError) -eq 'true') { $row.extra += 'ranged attack; ' }
                }
            }
            finally {
                Invoke-Bridge $B 'set Player.main.infectedMixin.infectedAmount 0' | Out-Null
            }
        }
    }
    $mesmerWatch = {
        param($id, $owner)
        if ((Invoke-Bridge $owner "get $id PullCreatures.isActive" -AllowError) -eq 'true') {
            $pulled = (Invoke-Bridge $owner "get $id PullCreatures._currentTarget.name" -AllowError).Trim().Trim('"')
            if (-not $row.extra.Contains("pulls $pulled")) { $row.extra += "pulls $pulled; " }
        }
        $multiplier = Get-Number $Victim 'Player.main.mesmerizedSpeedMultiplier'
        if ($multiplier -ne $null -and $multiplier -lt 0.99 -and -not $row.extra.Contains('hypnotized')) { $row.extra += "$Victim hypnotized (speed x$([math]::Round($multiplier, 2))); " }
    }
    Probe 'Mesmer simulated by Alice hypnotizes and bites Alice (vanilla control)' {
        Test-AggroCase 'mesmer-AA' 'mesmer' $A $A -VictimAims -Watch $mesmerWatch
    }
    Probe 'Mesmer simulated by Alice hypnotizes and bites Bob' {
        Test-AggroCase 'mesmer-AB' 'mesmer' $A $B -VictimAims -Watch $mesmerWatch
    }
    Probe 'Lava lizard with its lava shell, simulated by Alice, Bob 15 m away (ranged attack)' {
        Test-AggroCase 'lavalizard-ranged-AB' 'lavalizard' $A $B -VictimDistance 15 -ReportOnly -DamageType 'Heat|Fire' -Setup {
            param($id)
            Invoke-Bridge $A "call $id LavaShell.Activate" -AllowError | Out-Null
            Write-Host "    ammo: $(Invoke-Bridge $A "get $id LavaLiazardRangedAttack.attackTypes[0].ammoPrefab.name" -AllowError)" -ForegroundColor DarkGray
        } -Watch {
            param($id, $owner)
            $state = Invoke-Bridge $owner "get $id LavaLiazardRangedAttack.state" -AllowError
            if ($state -match 'Cast|Charge' -and -not $row.extra.Contains($state.Trim())) { $row.extra += "ranged $($state.Trim()); " }
        }
    }
    Probe 'Bleeder spawned by Alice next to Alice and next to Bob (report only)' {
        if (Test-Wanted 'bleeder-AA', 'bleeder-AB') {
            $body = Invoke-BridgeJson $A "entity $($script:PlayerIds[$B])"
            $attach = @($body.components | Where-Object { $_.'$component' -match 'Bleeder' })
            Write-Host "    Bob's body in Alice's game has $($attach.Count) bleeder attach targets ($(($body.components | ForEach-Object { $_.'$component' }) -join ', '))"
        }
        Test-AggroCase 'bleeder-AA' 'bleeder' $A $A -ReportOnly -DamageType 'Normal' -VictimDistance 3
        Test-AggroCase 'bleeder-AB' 'bleeder' $A $B -ReportOnly -DamageType 'Normal' -VictimDistance 3
    }
    Probe 'Stalker born in a containment, simulated by Alice, spares Bob' {
        Test-AggroCase 'stalker-born-AB' 'stalker' $A $B -ExpectNoDamage -Window 20 -Setup {
            param($id)
            Invoke-Bridge $A "call $id WaterParkCreature.InitializeCreatureBornInWaterPark" | Out-Null
        }
    }

    if (Test-Wanted 'vehicle-BB', 'vehicle-AB', 'emp-BB', 'emp-AB', 'vehicle-BA') {
        $seamothSpot = @(($script:Center[0] - 40), -8, ($script:Center[2] + 120))
        $seamothId = $null
        Step 'Bob pilots a Seamoth' {
            $script:SeamothId = New-PilotedSeamoth $B $seamothSpot
        }
        $seamothId = $script:SeamothId
        Probe "$VehicleBiter simulated by Bob bites the Seamoth Bob pilots (vanilla control)" {
            Test-VehicleBiteCase 'vehicle-BB' $VehicleBiter $B $B $seamothId
        }
        Probe "$VehicleBiter simulated by Alice bites the Seamoth Bob pilots" {
            Test-VehicleBiteCase 'vehicle-AB' $VehicleBiter $A $B $seamothId
        }
        Probe 'Crabsquid simulated by Bob blasts the Seamoth Bob pilots (vanilla control)' {
            Test-EmpCase 'emp-BB' $B $B $seamothId
        }
        Probe 'Crabsquid simulated by Alice blasts the Seamoth Bob pilots' {
            Test-EmpCase 'emp-AB' $A $B $seamothId -ReportOnly
        }
        Probe 'Alice takes over the Seamoth' {
            Exit-Seamoth $B
            Invoke-Bridge $B "teleport $(Format-Point @(($seamothSpot[0] + 30), $seamothSpot[1], $seamothSpot[2]))" | Out-Null
            Enter-Seamoth $A $seamothId
        }
        Probe "$VehicleBiter simulated by Bob bites the Seamoth Alice pilots" {
            Test-VehicleBiteCase 'vehicle-BA' $VehicleBiter $B $A $seamothId
        }
        # A pilot is safe from bites: scenarios run after this one with -NoLaunch need Alice on foot.
        Exit-Seamoth $A
        Exit-Seamoth $B
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Write-Host ''
Write-Host 'Matrix (owner = lock holder from the server right after the setup; extra = what else the case saw):'
$script:Matrix | Format-Table case, creature, simulator, victim, owner, targeted, maxAggression, hits, how, types, damage, maxHit, biteDamage, extra, note -AutoSize | Out-String -Width 300 | Write-Host
if ($script:VehicleMatrix.Count -gt 0) {
    Write-Host 'Vehicles (hits/damage = the Seamoth in the pilot''s game):'
    $script:VehicleMatrix | Format-Table case, creature, simulator, pilot, owner, vehicleOwner, targeted, maxAggression, hits, how, damage, drops, biteDamage, pilotGameHealth, simulatorGameHealth, note -AutoSize | Out-String -Width 300 | Write-Host
}
Complete-Scenario -Name 'D4 ThreatParity' -Keep:$Keep
