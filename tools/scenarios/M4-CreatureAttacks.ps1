<#
.SYNOPSIS
    MULTINAUGHTICA_SPEC M4: creature attacks that reached only one game. A Reaper's grab shows in both games and is let
    go in both; a Crabsquid's EMP reaches the partner's vehicle and tool in the partner's game, once; crash fish stay on
    the server when their area unloads and stay at their home through a restart; a crash fish blast hurts the partner's
    vehicle.

.DESCRIPTION
    A->B means Alice's game simulates the creature (she spawned it, checked with the server's "query") and Bob is the
    target. Each case forces the creature's own attack call in the simulating game (the natural attack is fickle and
    S14/D4 cover it), so everything after that call is the game and Nitrox code.
      - reaper-grab-AB: Bob pilots a Seamoth; Alice's Reaper grabs it (ReaperLeviathan.GrabSeamoth). Both games must
        show the Reaper holding it; the Seamoth loses the grip's 15 per second in Bob's game, never two drops at once;
        both games let go within 16 s (Alice's own timer is 8 to 13 s, Bob's copy waits up to 20 s for her release).
        Without the fix Bob's game never holds the Seamoth and it takes no grip damage.
      - reaper-fallback-AB: the same grab, then Alice leaves (her copy unloads and lets go without telling anyone): Bob's
        game must still let go, within 25 s.
      - emp-AB: Alice's Crabsquid casts its EMP next to Bob's Seamoth (EMPAttack.CastEMPBlast). The Seamoth's electronics
        must be disabled in Bob's game, and only for about the blast's disable time (not extended by a second hit).
        Without the fix: never disabled in Bob's game (D4's emp-AB).
      - emp-tools-AB: on foot. Bob holds a flashlight next to the Crabsquid, Alice holds one $EmpSafeDistance m away. Bob's
        flashlight must be disabled in Bob's game (without the fix: never). Reported: the tag of Bob's body in Alice's
        game and the blast's final radius; if the body is tagged "Player" and Alice is outside the blast, Alice's
        flashlight must stay on (EMPBlast_OnTouch_Patch: before, the blast touching Bob's body disabled her own tool).
      - crash-blast-AB: Alice's crash fish explodes 2 m from Bob's Seamoth (Crash.Detonate): the Seamoth loses health in
        Bob's game (falloff of 50 over 5 m), and Alice's game shows it. Without the fix: no damage.
      - crash-home-unload: a natural sulfur plant near the pod gets its fish (Alice holds the home), both players leave
        until neither game has it loaded, then come back. The fish must still be on the server, and both games get it
        back as the home's fish, one fish only. Without the fix the server deleted it ("not found") when Alice left.
      - crash-home-restart: the server saves, everything restarts; both games must have the fish within 3 m of its home.
        Without the load fix it is about the home's distance from the world origin away (L2 CreatureAttackGapsTest).

    Starts a fresh world (save m4-attacks) and two games, unless -NoLaunch. Needs a Debug build. Run windowless
    ($env:NITROX_WINDOWLESS = 1); the restart case restarts the server and both games.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'm4-attacks',
    # Only these cases, e.g. -Only reaper-grab-AB,emp-AB
    [string[]]$Only,
    # How far Alice stays from the EMP in emp-tools-AB
    [double]$EmpSafeDistance = 120)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'CreatureAggroHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseHelpers.ps1')
$script:Rows = New-Object System.Collections.ArrayList
# Open water deep enough for a Reaper (S14's Reaper spot)
$script:DeepSpot = @(-150, -10, 300)

function Test-Holding {
    param([string]$Player, [string]$ReaperId)
    (Invoke-Bridge $Player "call $ReaperId ReaperLeviathan.IsHoldingVehicle" -AllowError) -eq 'true'
}

function Set-SeamothFull {
    param([string]$Pilot, [string]$SeamothId)
    $max = Get-Number $Pilot "$SeamothId LiveMixin.maxHealth"
    Invoke-Bridge $Pilot "set $SeamothId LiveMixin.health $max" | Out-Null
    $max
}

<#
    Alice's Reaper grabs the Seamoth Bob pilots (natural or forced). Returns the Reaper's id and the time both games held it.
#>
function Start-ReaperGrab {
    param([string]$SeamothId, [hashtable]$Row)
    $reaperId = New-CreatureNearSeamoth $A $B $SeamothId 'reaperleviathan'
    $Row.reaper = $reaperId
    Start-Sleep -Seconds 2
    $Row.owner = Get-LockOwner $reaperId
    Set-SeamothFull $B $SeamothId | Out-Null
    if (-not (Test-Holding $A $reaperId)) {
        $seamothThere = Get-Position $A $SeamothId
        Invoke-Bridge $A "set $reaperId GameObject.transform.position $(($seamothThere[0] + 6).ToString('0.00')),$($seamothThere[1].ToString('0.00')),$($seamothThere[2].ToString('0.00'))" | Out-Null
        Invoke-Bridge $A "call $reaperId ReaperLeviathan.GrabSeamoth $SeamothId" | Out-Null
        $Row.how = 'forced'
    }
    else {
        $Row.how = 'natural'
    }
    $grabbedAt = Get-Date
    $Row.aliceHolds = [bool](Wait-Until { Test-Holding $A $reaperId } -TimeoutSeconds 3 -IntervalSeconds 0.25)
    $Row.bobHolds = [bool](Wait-Until { Test-Holding $B $reaperId } -TimeoutSeconds 5 -IntervalSeconds 0.25)
    @{ ReaperId = $reaperId; GrabbedAt = $grabbedAt }
}

function Test-ReaperGrabCase {
    param([string]$SeamothId)
    if ($Only -and $Only -notcontains 'reaper-grab-AB') { return }
    $row = @{ case = 'reaper-grab-AB'; owner = '?'; how = ''; aliceHolds = $false; bobHolds = $false; drops = @(); doubleDrop = $false; aliceReleased = $null; bobReleased = $null; note = '' }
    $grab = Start-ReaperGrab $SeamothId $row
    try {
        # The grip: every drop of the Seamoth's health in Bob's game, healed back before it gets low
        $last = Get-SeamothHealth $B $SeamothId
        $lastDropAt = $null
        $deadline = $grab.GrabbedAt.AddSeconds(18)
        while ((Get-Date) -lt $deadline -and ($null -eq $row.aliceReleased -or $null -eq $row.bobReleased)) {
            $elapsed = [math]::Round(((Get-Date) - $grab.GrabbedAt).TotalSeconds, 1)
            if ($null -eq $row.aliceReleased -and -not (Test-Holding $A $grab.ReaperId)) { $row.aliceReleased = $elapsed }
            if ($null -eq $row.bobReleased -and $row.bobHolds -and -not (Test-Holding $B $grab.ReaperId)) { $row.bobReleased = $elapsed }
            $now = Get-SeamothHealth $B $SeamothId
            if ($now -ne $null -and $last -ne $null -and $now -lt $last - 0.5) {
                $row.drops += [math]::Round($last - $now, 1)
                if ($lastDropAt -and ((Get-Date) - $lastDropAt).TotalSeconds -lt 0.5) { $row.doubleDrop = $true }
                $lastDropAt = Get-Date
            }
            $last = $now
            if ($now -ne $null -and $now -lt 80) { $last = Set-SeamothFull $B $SeamothId }
            Start-Sleep -Milliseconds 200
        }
        $row.simulatorHealth = Get-SeamothHealth $A $SeamothId
        $row.pilotHealth = Get-SeamothHealth $B $SeamothId
    }
    finally {
        Remove-Creature $grab.ReaperId
        Set-SeamothFull $B $SeamothId | Out-Null
        [void]$script:Rows.Add([pscustomobject]$row)
    }
    Write-Host "    held: Alice $($row.aliceHolds), Bob $($row.bobHolds); grip drops in Bob's game: $($row.drops -join ' '); let go after: Alice $($row.aliceReleased) s, Bob $($row.bobReleased) s"
    Assert-Equal $A $row.owner 'reaper-grab-AB : Alice simulates the Reaper'
    Assert-True $row.aliceHolds "reaper-grab-AB : Alice's Reaper holds Bob's Seamoth in Alice's game ($($row.how))"
    Assert-True $row.bobHolds "reaper-grab-AB : Bob's game shows the Reaper holding his Seamoth"
    Assert-True ($row.drops.Count -ge 2) "reaper-grab-AB : the grip damages the Seamoth in Bob's game (drops $($row.drops -join ' '))"
    Assert-True (@($row.drops | Where-Object { $_ -gt 15 * 1.05 + 0.1 }).Count -eq 0 -and -not $row.doubleDrop) "reaper-grab-AB : the grip hurts once per second, not twice (drops $($row.drops -join ' '))"
    Assert-True ($null -ne $row.aliceReleased -and $row.aliceReleased -le 15) "reaper-grab-AB : Alice's Reaper lets go (after $($row.aliceReleased) s)"
    Assert-True ($null -ne $row.bobReleased -and $row.bobReleased -le 16) "reaper-grab-AB : Bob's game lets go with Alice's, before its own 20 s fallback (after $($row.bobReleased) s)"
}

function Test-ReaperFallbackCase {
    param([string]$SeamothId)
    if ($Only -and $Only -notcontains 'reaper-fallback-AB') { return }
    $row = @{ case = 'reaper-fallback-AB'; owner = '?'; how = ''; aliceHolds = $false; bobHolds = $false; bobReleased = $null; note = '' }
    $grab = Start-ReaperGrab $SeamothId $row
    $aliceBack = Get-Position $A $SeamothId
    try {
        # Alice leaves: her copy unloads and lets go in her game only (OnDisable sends nothing)
        Invoke-Bridge $A "teleport $(Format-Point @(($aliceBack[0] + 600), $aliceBack[1], $aliceBack[2]))" | Out-Null
        $deadline = $grab.GrabbedAt.AddSeconds(30)
        while ((Get-Date) -lt $deadline -and $null -eq $row.bobReleased) {
            if (-not (Test-Holding $B $grab.ReaperId)) { $row.bobReleased = [math]::Round(((Get-Date) - $grab.GrabbedAt).TotalSeconds, 1) }
            if ((Get-SeamothHealth $B $SeamothId) -lt 80) { Set-SeamothFull $B $SeamothId | Out-Null }
            Start-Sleep -Milliseconds 250
        }
        $row.ownerAfter = Get-LockOwner $grab.ReaperId
    }
    finally {
        Invoke-Bridge $A "teleport $(Format-Point @(($aliceBack[0] + 30), $aliceBack[1], ($aliceBack[2] - 16)))" | Out-Null
        Start-Sleep -Seconds 3
        Remove-Creature $grab.ReaperId
        Set-SeamothFull $B $SeamothId | Out-Null
        [void]$script:Rows.Add([pscustomobject]$row)
    }
    Write-Host "    Bob's game let go after $($row.bobReleased) s (the Reaper's lock then: $($row.ownerAfter))"
    Assert-True $row.bobHolds "reaper-fallback-AB : Bob's game shows the grab"
    Assert-True ($null -ne $row.bobReleased -and $row.bobReleased -le 25) "reaper-fallback-AB : Bob's game lets go although Alice's release never came (after $($row.bobReleased) s)"
}

<#
    The electronics of the Seamoth in each game, sampled for 8 s after the cast.
#>
function Test-EmpVehicleCase {
    param([string]$SeamothId)
    if ($Only -and $Only -notcontains 'emp-AB') { return }
    $row = @{ case = 'emp-AB'; owner = '?'; aliceDisabled = $false; bobDisabled = $false; bobDisabledFor = $null; disableTime = $null; note = '' }
    $id = New-CreatureNearSeamoth $A $B $SeamothId 'crabsquid'
    try {
        Start-Sleep -Seconds 2
        $row.owner = Get-LockOwner $id
        $seamothThere = Get-Position $A $SeamothId
        Invoke-Bridge $A "set $id GameObject.transform.position $(($seamothThere[0] + 4).ToString('0.00')),$($seamothThere[1].ToString('0.00')),$($seamothThere[2].ToString('0.00'))" | Out-Null
        # Bob's copy follows (position stream every 0.25 s)
        Start-Sleep -Seconds 1.5
        Invoke-Bridge $A "call $id EMPAttack.CastEMPBlast" | Out-Null
        $castAt = Get-Date
        $bobFrom = $null
        $bobTo = $null
        while (((Get-Date) - $castAt).TotalSeconds -lt 8) {
            if (-not $row.disableTime) {
                $time = Invoke-Bridge $B 'get find:EMPBlast EMPBlast.disableElectronicsTime' -AllowError
                if (-not $time.StartsWith('ERR ')) { $row.disableTime = $time.Trim() }
            }
            if ((Invoke-Bridge $A "get $SeamothId EnergyInterface.sources[0].electronicsDisabled" -AllowError) -eq 'true') { $row.aliceDisabled = $true }
            $bob = (Invoke-Bridge $B "get $SeamothId EnergyInterface.sources[0].electronicsDisabled" -AllowError) -eq 'true'
            $elapsed = ((Get-Date) - $castAt).TotalSeconds
            if ($bob -and $null -eq $bobFrom) { $bobFrom = $elapsed; $row.bobDisabled = $true }
            if (-not $bob -and $null -ne $bobFrom -and $null -eq $bobTo) { $bobTo = $elapsed }
            Start-Sleep -Milliseconds 200
        }
        if ($null -ne $bobFrom) { $row.bobDisabledFor = if ($null -ne $bobTo) { [math]::Round($bobTo - $bobFrom, 1) } else { 'still' } }
    }
    finally {
        Invoke-Bridge $A 'call GameModeUtils.DeactivateCheat NoAggression' | Out-Null
        Remove-Creature $id
        [void]$script:Rows.Add([pscustomobject]$row)
    }
    Write-Host "    disabled in Alice's game: $($row.aliceDisabled), in Bob's: $($row.bobDisabled) (for $($row.bobDisabledFor) s, the blast's disableElectronicsTime is $($row.disableTime))"
    Assert-Equal $A $row.owner 'emp-AB : Alice simulates the Crabsquid'
    Assert-True $row.bobDisabled "emp-AB : the EMP disables Bob's Seamoth in Bob's (the pilot's) game"
    # A second hit at the same moment doesn't extend it (EnergyMixin.DisableElectronicsForTime takes the max); a hit replayed later would
    $disableTime = 2.0
    $read = 0.0
    if ($row.disableTime -and [double]::TryParse($row.disableTime, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$read)) { $disableTime = $read }
    Assert-True ($row.bobDisabledFor -is [double] -and $row.bobDisabledFor -le $disableTime + 1.5) "emp-AB : it is disabled once, for about the blast's disable time ($($row.bobDisabledFor) s, disableElectronicsTime $disableTime s)"
}

function Get-HeldFlashlight {
    param([string]$Player)
    if (-not ((Invoke-BridgeJson $Player 'state').inventory | Where-Object techType -eq 'Flashlight')) {
        Invoke-Bridge $Player 'console item flashlight' | Out-Null
        Start-Sleep -Seconds 3
    }
    $state = Invoke-BridgeJson $Player 'state'
    $flashlight = ($state.inventory | Where-Object techType -eq 'Flashlight' | Select-Object -First 1).id
    if ($state.held -ne $flashlight) {
        Invoke-Bridge $Player "call Inventory.main.quickSlots.SelectImmediate $(Invoke-Bridge $Player "call Inventory.main.quickSlots.GetSlotByItem $flashlight")" | Out-Null
        Start-Sleep -Seconds 2
    }
    $flashlight
}

function Test-EmpToolsCase {
    if ($Only -and $Only -notcontains 'emp-tools-AB') { return }
    $row = @{ case = 'emp-tools-AB'; owner = '?'; bodyTag = '?'; touchedBody = ''; aliceToolDisabled = $false; bobToolDisabled = $false; note = '' }
    $spot = @(($script:Center[0] + 40), -8, ($script:Center[2] - 60))
    Invoke-Bridge $A 'call GameModeUtils.ActivateCheat NoAggression' | Out-Null
    Invoke-Bridge $B "teleport $(Format-Point $spot)" | Out-Null
    Invoke-Bridge $A "teleport $(Format-Point @(($spot[0] - 4), $spot[1], $spot[2]))" | Out-Null
    Invoke-Bridge $A 'look 90 0' | Out-Null
    Start-Sleep -Seconds 3
    $id = New-Creature $A 'crabsquid'
    try {
        $row.owner = Get-LockOwner $id
        $aliceTool = Get-HeldFlashlight $A
        Invoke-Bridge $A 'call Inventory.main.quickSlots.DeselectImmediate' | Out-Null
        $bobTool = Get-HeldFlashlight $B
        # Both next to the Crabsquid: inside the blast from its first moment, so each player's own collider is touched once,
        # right away. Alice holds nothing then.
        $creaturePos = Get-Position $A $id
        Invoke-Bridge $B "teleport $(Format-Point @(($creaturePos[0] + 2), $creaturePos[1], $creaturePos[2]))" | Out-Null
        Invoke-Bridge $A "teleport $(Format-Point @(($creaturePos[0] - 2), $creaturePos[1], $creaturePos[2]))" | Out-Null
        Start-Sleep -Seconds 2
        $row.bodyTag = (Invoke-Bridge $A "get $($script:PlayerIds[$B]) GameObject.tag" -AllowError).Trim().Trim('"')
        Invoke-Bridge $A "call $id EMPAttack.CastEMPBlast" | Out-Null
        $castAt = Get-Date
        Start-Sleep -Milliseconds 500
        # Then Alice draws her flashlight and her game's blast touches Bob's body (as its growing sphere did a moment ago):
        # for a target tagged "Player" the game disables Inventory.main's held tool, which is Alice's
        Invoke-Bridge $A "call Inventory.main.quickSlots.SelectImmediate $(Invoke-Bridge $A "call Inventory.main.quickSlots.GetSlotByItem $aliceTool")" | Out-Null
        Start-Sleep -Milliseconds 300
        $row.touchedBody = Invoke-Bridge $A "call find:EMPBlast EMPBlast.OnTouch $($script:PlayerIds[$B])" -AllowError
        while (((Get-Date) - $castAt).TotalSeconds -lt 4) {
            if ((Invoke-Bridge $A "get $aliceTool EnergyMixin.electronicsDisabled" -AllowError) -eq 'true') { $row.aliceToolDisabled = $true }
            if ((Invoke-Bridge $B "get $bobTool EnergyMixin.electronicsDisabled" -AllowError) -eq 'true') { $row.bobToolDisabled = $true }
            Start-Sleep -Milliseconds 200
        }
    }
    finally {
        Invoke-Bridge $A 'call GameModeUtils.DeactivateCheat NoAggression' | Out-Null
        foreach ($player in $A, $B) { Invoke-Bridge $player 'call Inventory.main.quickSlots.DeselectImmediate' -AllowError | Out-Null }
        Remove-Creature $id
        [void]$script:Rows.Add([pscustomobject]$row)
    }
    Write-Host "    Bob's body in Alice's game is tagged '$($row.bodyTag)'; the blast touching it: $($row.touchedBody)"
    Write-Host "    tool disabled: Bob's in Bob's game $($row.bobToolDisabled), Alice's in Alice's game $($row.aliceToolDisabled)"
    Assert-Equal $A $row.owner 'emp-tools-AB : Alice simulates the Crabsquid'
    Assert-True $row.bobToolDisabled "emp-tools-AB : the EMP disables the flashlight Bob holds, in Bob's game"
    Assert-True (-not $row.touchedBody.StartsWith('ERR ')) "emp-tools-AB : Alice's blast touched Bob's body ($($row.touchedBody))"
    # Fails without EMPBlast_OnTouch_Patch only if the body is tagged "Player" (asset data, reported above)
    Assert-True (-not $row.aliceToolDisabled) "emp-tools-AB : the blast touching Bob's body doesn't disable the tool Alice drew after her own touch"
}

function Test-CrashBlastVehicleCase {
    param([string]$SeamothId)
    if ($Only -and $Only -notcontains 'crash-blast-AB') { return }
    $row = @{ case = 'crash-blast-AB'; owner = '?'; pilotDrop = 0.0; simulatorHealth = $null; pilotHealth = $null; pilotBodyHit = $false; note = '' }
    $max = Set-SeamothFull $B $SeamothId
    Set-Vulnerable $B $true
    $id = New-CreatureNearSeamoth $A $B $SeamothId 'crash'
    try {
        Start-Sleep -Seconds 2
        $row.owner = Get-LockOwner $id
        $seamothThere = Get-Position $A $SeamothId
        Invoke-Bridge $A "set $id GameObject.transform.position $(($seamothThere[0] + 2).ToString('0.00')),$($seamothThere[1].ToString('0.00')),$($seamothThere[2].ToString('0.00'))" | Out-Null
        Invoke-Bridge $A "call $id Crash.Detonate" | Out-Null
        $hit = Wait-Until { $h = Get-SeamothHealth $B $SeamothId; if ($h -ne $null -and $h -lt $max - 0.5) { $h } } -TimeoutSeconds 5 -IntervalSeconds 0.25
        Start-Sleep -Seconds 1.5
        $row.pilotHealth = Get-SeamothHealth $B $SeamothId
        $row.simulatorHealth = Get-SeamothHealth $A $SeamothId
        $row.pilotDrop = [math]::Round($max - $row.pilotHealth, 1)
        $row.pilotBodyHit = (Get-Health $B) -lt 99.9
    }
    finally {
        Invoke-Bridge $A 'call GameModeUtils.DeactivateCheat NoAggression' | Out-Null
        Set-Vulnerable $B $false
        if (Test-Loaded $A $id) { Remove-Creature $id }
        Set-SeamothFull $B $SeamothId | Out-Null
        [void]$script:Rows.Add([pscustomobject]$row)
    }
    Write-Host "    Seamoth: -$($row.pilotDrop) in Bob's game (now $($row.pilotHealth)), Alice's game shows $($row.simulatorHealth)"
    Assert-Equal $A $row.owner 'crash-blast-AB : Alice simulates the crash fish'
    Assert-True ($row.pilotDrop -gt 0 -and $row.pilotDrop -le 50 * 1.05) "crash-blast-AB : the blast damages Bob's Seamoth in Bob's game, at most a full blast ($($row.pilotDrop))"
    Assert-True ([math]::Abs([double]$row.pilotHealth - [double]$row.simulatorHealth) -lt 0.5) "crash-blast-AB : Alice's game shows the Seamoth's new health ($($row.simulatorHealth), Bob's game $($row.pilotHealth))"
    Assert-True (-not $row.pilotBodyHit) "crash-blast-AB : Bob isn't hurt inside the Seamoth"
}

<#
    A natural sulfur plant near the pod: Alice looks around a ring of spots. Returns the home's id and position.
#>
function Find-CrashHome {
    $spots = @(@(0, 0)) + @(0..7 | ForEach-Object { $angle = [math]::PI * 2 * $_ / 8; @((90 * [math]::Cos($angle)), (90 * [math]::Sin($angle))) })
    foreach ($offset in $spots) {
        Invoke-Bridge $A "teleport $(Format-Point @(($script:Center[0] + $offset[0]), -8, ($script:Center[2] + $offset[1])))" | Out-Null
        $found = Wait-Until { (Invoke-BridgeJson $A 'find CrashHome 5') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' } | Select-Object -First 1 } -TimeoutSeconds 8
        if ($found) {
            return @{ Id = $found.id; Position = [double[]](Invoke-BridgeJson $A "entity $($found.id)").position }
        }
    }
    throw 'no sulfur plant (CrashHome) found around the pod'
}

function Get-HomeFishId {
    param([string]$HomeId)
    $query = Get-ServerQuery $HomeId
    $fish = [regex]::Matches($query, 'Id: ([0-9a-f-]{36})\s+\S+ TechType: Crash\b')
    @($fish | ForEach-Object { $_.Groups[1].Value })
}

function Get-HomeFishInGame {
    param([string]$Player, [string]$HomeId)
    $reply = Invoke-Bridge $Player "get $HomeId CrashHome.crash.gameObject" -AllowError
    if ($reply -match '"id":\s*"([0-9a-f-]{36})"') { $Matches[1] } else { $null }
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    foreach ($player in $A, $B) {
        $script:PlayerIds[$player] = (Invoke-BridgeJson $player 'state').playerId
    }
    $pod = (Invoke-BridgeJson $A 'find EscapePod 1') | Select-Object -First 1
    $script:Center = if ($pod) { [double[]](Invoke-BridgeJson $A "entity $($pod.id)").position } else { [double[]](Invoke-BridgeJson $A 'state').position }

    if (-not $Only -or @('reaper-grab-AB', 'reaper-fallback-AB', 'emp-AB', 'crash-blast-AB' | Where-Object { $Only -contains $_ }).Count -gt 0) {
        Step 'Bob pilots a Seamoth in deep open water' {
            $script:SeamothId = New-PilotedSeamoth $B $script:DeepSpot
        }
        Probe "Alice's Reaper grabs the Seamoth Bob pilots" { Test-ReaperGrabCase $script:SeamothId }
        Probe "Alice's Reaper grabs the Seamoth, then Alice leaves" { Test-ReaperFallbackCase $script:SeamothId }
        Probe "Alice's Crabsquid casts its EMP next to the Seamoth Bob pilots" { Test-EmpVehicleCase $script:SeamothId }
        Probe "Alice's crash fish explodes next to the Seamoth Bob pilots" { Test-CrashBlastVehicleCase $script:SeamothId }
        Exit-Seamoth $B
    }
    Probe "Alice's Crabsquid casts its EMP next to Bob on foot, both holding a flashlight" { Test-EmpToolsCase }

    if (-not $Only -or @('crash-home-unload', 'crash-home-restart' | Where-Object { $Only -contains $_ }).Count -gt 0) {
        $script:CrashHome = $null
        $script:FishId = $null
        Step 'Alice holds a sulfur plant near the pod and it has its fish' {
            $script:CrashHome = Find-CrashHome
            $homePos = $script:CrashHome.Position
            Invoke-Bridge $A "teleport $(Format-Point @(($homePos[0]), ($homePos[1] + 4), ($homePos[2])))" | Out-Null
            Invoke-Bridge $B "teleport $(Format-Point @(($homePos[0] + 8), ($homePos[1] + 4), ($homePos[2])))" | Out-Null
            Write-Host "  home $($script:CrashHome.Id) at $(Format-Point $homePos), lock: $(Get-LockOwner $script:CrashHome.Id)"
            $fish = Wait-Until { $f = Get-HomeFishId $script:CrashHome.Id; if ($f.Count -ge 1) { $f } } -TimeoutSeconds 45
            Assert-True ([bool]$fish) "the home has its fish on the server ($((Get-ServerQuery $script:CrashHome.Id) -replace '\s+', ' '))"
            $script:FishId = @($fish)[0]
            foreach ($player in $A, $B) {
                Assert-True ([bool](Wait-Until { Test-Loaded $player $script:FishId } -TimeoutSeconds 15)) "$player's game has the fish $($script:FishId)"
            }
        }
        if (-not $Only -or $Only -contains 'crash-home-unload') {
            Step 'Both players leave until neither game has the home, then come back' {
                $homePos = $script:CrashHome.Position
                foreach ($player in $A, $B) {
                    Invoke-Bridge $player "teleport $(Format-Point @(($homePos[0] + 700), -8, ($homePos[2] + 700)))" | Out-Null
                }
                foreach ($player in $A, $B) {
                    Assert-True ([bool](Wait-Until { -not (Test-Loaded $player $script:CrashHome.Id) } -TimeoutSeconds 40)) "$player's game unloaded the home"
                }
                Start-Sleep -Seconds 3
                $query = Get-ServerQuery $script:FishId
                Assert-True (-not ($query -match 'not found')) "the fish is still on the server after its area unloaded ($(($query -split "`n")[0]))"
                Invoke-Bridge $A "teleport $(Format-Point @(($homePos[0]), ($homePos[1] + 4), ($homePos[2])))" | Out-Null
                Invoke-Bridge $B "teleport $(Format-Point @(($homePos[0] + 8), ($homePos[1] + 4), ($homePos[2])))" | Out-Null
                foreach ($player in $A, $B) {
                    $back = Wait-Until { $f = Get-HomeFishInGame $player $script:CrashHome.Id; if ($f) { $f } } -TimeoutSeconds 30
                    Assert-Equal $script:FishId $back "$player's game has the server's fish back as the home's fish"
                }
                Start-Sleep -Seconds 5
                Assert-Equal 1 (Get-HomeFishId $script:CrashHome.Id).Count 'the home has one fish on the server (no second one was made)'
            }
        }
        if (-not $Only -or $Only -contains 'crash-home-restart') {
            Step 'The server saves and everything restarts: the fish is at its home in both games' {
                Restart-ScenarioWorld -SaveName $SaveName
                foreach ($player in $A, $B) {
                    $loaded = Wait-Until { Test-Loaded $player $script:FishId } -TimeoutSeconds 60
                    Assert-True ([bool]$loaded) "$player's game has the fish after the restart"
                    if ($loaded) {
                        $distance = Get-Distance (Get-Position $player $script:FishId) (Get-Position $player $script:CrashHome.Id)
                        Assert-True ($distance -lt 3) "$player's game has the fish at its home after the restart ($([math]::Round($distance, 1)) m away)"
                    }
                }
            }
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Write-Host ''
Write-Host 'Cases:'
$script:Rows | Format-List | Out-String -Width 300 | Write-Host
Complete-Scenario -Name 'M4 CreatureAttacks' -Keep:$Keep
