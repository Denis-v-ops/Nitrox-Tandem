<#
.SYNOPSIS
    MULTINAUGHTICA_SPEC M9: where a ping lands. An item 1 m away and the partner's body can be pinged; pings from the
    Seamoth seat and the Cyclops helm reach what is outside; a passenger still pings the Cyclops' own floor.

.DESCRIPTION
    Alice pings, Bob's game must show the same ping. Pings and their markers are read with PlayerPingManager.Describe
    (Debug builds), what the ray met with PingAim.Describe / PingAim.DescribeToward (every hit, which were skipped as
    Alice's own body, vehicle or the Cyclops she steers, and the winner), printed for each ping.

    Steps:
    - an item 1 m in front of Alice: a "Look" ping at it (before M9 anything nearer than 3 m was skipped);
    - Bob 10 m in front of Alice, open water behind him: a "Look" ping named after him, Signal icon in Alice's colour,
      still there on both games a second later (a "come here" ping would end at once, he is at it). Before M9 the ray
      went through his body (a trigger). The "come here" key still goes through him;
    - from the Seamoth seat: titanium 8 m straight ahead (crosshair);
    - from the Cyclops helm: titanium 15 m straight ahead (crosshair), and titanium 12 m ahead and below, through the
      sub's floor and hull (PlayerPingManager.RequestPingToward: the bridge can't turn a seated player's camera, so the
      ray goes from the camera toward the point instead of through the crosshair);
    - Alice leaves the helm and pings the floor below her: that one lands on the Cyclops ("come here").

    Windowless: like F2-ContextPings.ps1, the manager's Update is run from the bridge between frames (a batch-mode
    cursor can't lock, so during the scripts' Update the ping is refused as if a menu were open; TESTING.md).

    Negative control: on a build without M9 the first step fails (PingAim.Describe is missing). With M9 but one rule
    reverted: the 3 m minimum fails the 1 m item; triggers skipped fail the partner; without the helm skip the ping
    through the hull lands on the Cyclops. The Seamoth step is a guard: the old 3 m minimum also stepped over the
    Seamoth's nose; with 0.5 m only the own-vehicle skip does (the printed hits show what it skipped).
    Starts a fresh world and two games (opens windows unless $env:NITROX_WINDOWLESS=1), unless -NoLaunch. Needs a Debug
    build (dual-client.ps1 -NoBuild is used, so build first).
#>
param([switch]$NoLaunch, [switch]$Keep,
    # Over the Grassy Plateaus, open water (S13, T4): deep enough for the Cyclops.
    [double[]]$Area = @(263, -60, 64))

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'CreatureAggroHelpers.ps1')
$SaveName = 'm9-ping-aim'
$inv = [Globalization.CultureInfo]::InvariantCulture

function Format-Vector3Arg {
    param([double[]]$P)
    ($P | ForEach-Object { $_.ToString('0.###', $inv) }) -join ','
}

function Get-Pings {
    param([string]$Player)
    @(Invoke-BridgeJson $Player 'call PlayerPingManager.Describe' | Where-Object { $_ })
}

function Get-Ping {
    param([string]$Player, [string]$PingId)
    Get-Pings $Player | Where-Object { $_.pingId -eq $PingId } | Select-Object -First 1
}

function Get-Text {
    param([string]$Player, [string]$Key)
    Invoke-Bridge $Player "call Language.main.Get $Key" | ConvertFrom-Json
}

# What the ray met: each hit, skipped or not, and the winner.
function Show-Aim {
    param($Aim)
    Write-Host ("  ray from ({0}) toward ({1}), at the helm: {2}; winner: {3} at {4} m" -f ($Aim.origin -join ', '), ($Aim.direction -join ', '), $Aim.atHelm, $Aim.winner, $Aim.winnerDistance) -ForegroundColor DarkGray
    foreach ($hit in @($Aim.hits)) {
        $flags = @('trigger', 'partner', 'ownBody', 'ownVehicle', 'currentSub') | Where-Object { $hit.$_ }
        $state = if ($hit.winner) { 'WINNER' } elseif ($hit.skipped) { 'skipped' } else { '' }
        Write-Host ("    {0,7} m  {1} ({2}, {3}) {4} {5}" -f $hit.distance, $hit.object, $hit.root, $hit.layer, ($flags -join ','), $state) -ForegroundColor DarkGray
    }
}

<#
    The player presses the ping key: aimed at the target first (a NitroxId; none: the current view), or with -Toward the
    ray from the camera toward a point. Returns the new ping as the player's own game describes it.
#>
function Send-Ping {
    param([string]$Player, [string]$Target, [double[]]$Toward, [string]$Method = 'RequestPing', [switch]$AllowNone)
    $before = @(Get-Pings $Player | ForEach-Object { $_.pingId })
    if ($Target) {
        Invoke-Bridge $Player "hold $Target 3 None" | Out-Null
        Start-Sleep -Milliseconds 500
    }
    # The 0.5 s cooldown between pings.
    Start-Sleep -Milliseconds 600
    if ($Toward) {
        Show-Aim (Invoke-BridgeJson $Player "call PingAim.DescribeToward $(Format-Vector3Arg $Toward)")
        Invoke-Bridge $Player "call PlayerPingManager.RequestPingToward $(Format-Vector3Arg $Toward)" | Out-Null
    }
    else {
        Show-Aim (Invoke-BridgeJson $Player 'call PingAim.Describe')
        Invoke-Bridge $Player "call PlayerPingManager.$Method" | Out-Null
    }
    # Windowless only (F2-ContextPings.ps1): run the manager's Update between frames, where the cursor is hidden.
    Invoke-Bridge $Player 'call PlayerPingManager.instance.Update' | Out-Null
    $new = Wait-Until { Get-Pings $Player | Where-Object { $before -notcontains $_.pingId } | Select-Object -First 1 } -TimeoutSeconds 5 -IntervalSeconds 0.25
    if ($Target) { Invoke-Bridge $Player 'hold off' | Out-Null }
    if (-not $new -and -not $AllowNone) { throw "$Player's $Method made no ping" }
    $new
}

function Wait-RemotePing {
    param([string]$Player, [string]$PingId)
    Wait-Until { Get-Ping $Player $PingId } -TimeoutSeconds 5 -IntervalSeconds 0.25
}

# Alice pings; both games must show a ping of this kind at this target.
function Assert-PingAt {
    param([string]$What, [string]$Kind, [string]$TechType, [string]$Target, [double[]]$Toward)
    $own = Send-Ping $A $Target $Toward
    Assert-Equal $Kind $own.kind "${What}: $A's ping"
    Assert-Equal $TechType $own.targetTechType "${What}: $A's target"
    $remote = Wait-RemotePing $B $own.pingId
    Assert-True ([bool]$remote) "${What}: $B shows the ping"
    if ($remote) {
        Assert-Equal $Kind $remote.kind "${What}: $B's ping"
        Assert-Equal $TechType $remote.targetTechType "${What}: $B's target"
    }
    $own
}

# Frozen titanium near Alice (spawned outside any vehicle, so it is a world entity), to be moved where a ping should land.
function New-Titanium {
    param([int]$Distance = 3)
    $before = @((Invoke-BridgeJson $A 'find Pickupable 50') | ForEach-Object { $_.id })
    Invoke-Bridge $A "console spawn titanium 1 $Distance" | Out-Null
    $item = Wait-Until { (Invoke-BridgeJson $A 'find Pickupable 50') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id -and $_.techType -eq 'Titanium' } | Select-Object -First 1 } -TimeoutSeconds 10
    if (-not $item) { throw 'the titanium did not spawn' }
    Invoke-Bridge $A "set $($item.id) Rigidbody.isKinematic true" | Out-Null
    $item.id
}

function Move-To {
    param([string]$Id, [double[]]$Point)
    Invoke-Bridge $A "set $Id Transform.position $(Format-Vector3Arg $Point)" | Out-Null
    Start-Sleep -Milliseconds 300
}

# A point along Alice's crosshair ray, or along a direction turned down from it.
function Get-AimPoint {
    param([double]$Distance, [double]$Down = 0)
    $aim = Invoke-BridgeJson $A 'call PingAim.Describe'
    $o = [double[]]$aim.origin
    $d = [double[]]$aim.direction
    $dy = $d[1] - $Down
    $length = [math]::Sqrt($d[0] * $d[0] + $dy * $dy + $d[2] * $d[2])
    @(($o[0] + $d[0] / $length * $Distance), ($o[1] + $dy / $length * $Distance), ($o[2] + $d[2] / $length * $Distance))
}

function Get-State { param([string]$Player) Invoke-BridgeJson $Player 'state' }

function Get-Mode {
    param([string]$Player)
    (Invoke-Bridge $Player 'get player Player.mode').Trim('"')
}

function Wait-NoCinematic {
    param([string]$Player)
    Wait-Until { [int](Invoke-Bridge $Player 'get PlayerCinematicController.cinematicModeCount') -eq 0 -and (Invoke-Bridge $Player 'get player Player.cinematicModeActive') -eq 'false' } -TimeoutSeconds 20 -IntervalSeconds 0.3
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    Invoke-Bridge $A "teleport $(Format-Point $Area)" | Out-Null
    Invoke-Bridge $B "teleport $(Format-Point @($Area[0], $Area[1], ($Area[2] + 30)))" | Out-Null
    Start-Sleep -Seconds 3
    $lookTemplate = Get-Text $B 'Nitrox_Ping_Look'

    Step 'Set up: the build has M9' {
        Assert-True (-not (Invoke-Bridge $A 'call PingAim.Describe' -AllowError).StartsWith('ERR ')) 'the build has M9 (PingAim.Describe)'
        $script:aliceId = (Get-State $A).playerId
        $script:bobId = (Get-State $B).playerId
    }

    Step 'An item 1 m in front of Alice is pinged' {
        Invoke-Bridge $A 'look 0 0' | Out-Null
        Start-Sleep -Milliseconds 300
        $near = New-Titanium 1
        $own = Assert-PingAt 'titanium 1 m away' 'Look' 'Titanium' $near
        $distance = Get-Distance ([double[]]$own.position) (Get-Position $A $near)
        Assert-True ($distance -lt 1.5) "the ping sits at the titanium ($([math]::Round($distance, 2)) m from it)"
        # Out of the way of the next rays.
        Move-To $near @($Area[0], ($Area[1] - 30), $Area[2])
    }

    Step "Bob's body is pinged: a Look ping named after him, on both games" {
        Invoke-Bridge $B "teleport $(Format-Point @(($Area[0] + 10), $Area[1], $Area[2]))" | Out-Null
        Start-Sleep -Seconds 2
        $label = ($lookTemplate -f $A, $B).Trim()
        $own = Assert-PingAt 'Bob' 'Look' 'Player' $bobId
        Assert-Equal $label $own.label "$A's label"
        Assert-Equal 'Signal' $own.pingType "$A's ping type"
        Assert-True (-not $own.expectedIcon) "the partner ping keeps the Signal icon (item icon: $($own.expectedIcon))"
        Assert-Equal $own.pingTypeIcon $own.markerIcon "$A's marker shows the Signal icon"
        Assert-Equal 10 $own.lifetime "$A's lifetime"
        $remote = Get-Ping $B $own.pingId
        if ($remote) {
            Assert-Equal $label $remote.label "$B's label"
            Assert-Equal $own.expectedColor $remote.expectedColor "$B's ping has $A's colour"
            Assert-Equal $remote.expectedColor $remote.markerColor "$B's marker has its colour"
        }
        # A "come here" ping at him would already be gone (F2's arrival rule); this one lasts 10 s.
        Start-Sleep -Seconds 1
        Assert-True ([bool](Get-Ping $A $own.pingId)) "$A's ping is still there a second later"
        Assert-True ([bool](Get-Ping $B $own.pingId)) "$B's ping is still there a second later"

        # The "come here" key goes through him, as before M9: no ping, or one beyond him.
        $comeHere = Send-Ping $A $bobId -Method 'RequestComeHerePing' -AllowNone
        $bobAt = (Get-State $B).position
        $beyond = -not $comeHere -or (Get-Distance ([double[]]$comeHere.position) ([double[]]$bobAt)) -gt 3
        Assert-True $beyond "the come-here key at $B doesn't land on him ($(if ($comeHere) { "$($comeHere.kind) at $($comeHere.position -join ', ')" } else { 'no ping' }))"
        Invoke-Bridge $B "teleport $(Format-Point @($Area[0], $Area[1], ($Area[2] + 30)))" | Out-Null
    }

    Step 'From the Seamoth seat, a ping reaches what is ahead' {
        Invoke-Bridge $A "teleport $(Format-Point $Area)" | Out-Null
        Invoke-Bridge $A 'look 0 0' | Out-Null
        Start-Sleep -Seconds 2
        $before = @((Invoke-BridgeJson $A 'find SeaMoth 200') | ForEach-Object { $_.id })
        Invoke-Bridge $A 'console spawn seamoth 1 6' | Out-Null
        $seamoth = Wait-Until { (Invoke-BridgeJson $A 'find SeaMoth 200') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
        if (-not $seamoth) { throw "the Seamoth didn't spawn" }
        $script:seamothId = $seamoth.id
        $script:titanium = New-Titanium 3
        $p = Get-Position $A $seamothId
        Invoke-Bridge $A "teleport $(Format-Point @($p[0], ($p[1] + 2), $p[2]))" | Out-Null
        Start-Sleep -Seconds 1
        Invoke-Bridge $A "use $seamothId" | Out-Null
        $inside = Wait-Until { if ((Get-State $A).vehicle) { $true } } -TimeoutSeconds 10
        if (-not $inside) { throw "$A isn't in the Seamoth" }
        Start-Sleep -Seconds 1
        Move-To $titanium (Get-AimPoint 8)
        Assert-PingAt 'Seamoth seat, titanium 8 m ahead' 'Look' 'Titanium' | Out-Null
        Invoke-Bridge $A 'call player Player.ExitLockedMode false true' -AllowError | Out-Null
        Wait-Until { -not (Get-State $A).vehicle } -TimeoutSeconds 10 | Out-Null
    }

    Step 'From the Cyclops helm, a ping reaches what is outside, ahead and through the hull' {
        # Clear of the Seamoth.
        Invoke-Bridge $A "teleport $(Format-Point @($Area[0], $Area[1], ($Area[2] - 80)))" | Out-Null
        Invoke-Bridge $A 'look 0 0' | Out-Null
        Start-Sleep -Seconds 3
        $before = @((Invoke-BridgeJson $A 'find SubRoot 20') | ForEach-Object { $_.id })
        Invoke-Bridge $A 'console sub cyclops' | Out-Null
        $cyclops = Wait-Until { (Invoke-BridgeJson $A 'find SubRoot 20') | Where-Object { $_.name -like 'Cyclops*' -and $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 30
        if (-not $cyclops) { throw 'the Cyclops did not spawn' }
        $script:cyclopsId = $cyclops.id
        Start-Sleep -Seconds 3
        # Spawned before boarding, so it is a world entity and not part of the Cyclops.
        $script:titanium = New-Titanium 3
        $hatch = (Invoke-BridgeJson $A 'find CinematicModeTrigger 40') | Where-Object { $_.name -eq 'EntranceHatch' -and $_.id -eq "(in $cyclopsId)" } | Select-Object -First 1
        if (-not $hatch) { throw "$A doesn't see the Cyclops' entrance hatch" }
        Invoke-Bridge $A "teleport $(Format-Point @($hatch.position[0], ($hatch.position[1] - 6.9), $hatch.position[2]))" | Out-Null
        Start-Sleep -Milliseconds 500
        Invoke-Bridge $A 'use find:CinematicModeTrigger#EntranceHatch' | Out-Null
        $boarded = Wait-Until { (Get-State $A).currentSub.id -eq $cyclopsId } -TimeoutSeconds 15 -IntervalSeconds 0.3
        if (-not $boarded) { throw "$A isn't in the Cyclops" }
        Wait-NoCinematic $A | Out-Null
        Invoke-Bridge $A 'use find:PilotingChair' | Out-Null
        $piloting = Wait-Until { (Get-Mode $A) -eq 'Piloting' } -TimeoutSeconds 15 -IntervalSeconds 0.3
        if (-not $piloting) { throw "$A isn't at the helm" }
        Wait-NoCinematic $A | Out-Null
        Start-Sleep -Seconds 1

        Move-To $titanium (Get-AimPoint 15)
        Assert-PingAt 'helm, titanium 15 m ahead' 'Look' 'Titanium' | Out-Null

        # 45 degrees down: through the helm console, the floor and the hull below the bow
        $below = Get-AimPoint 12 1
        Move-To $titanium $below
        $aim = Invoke-BridgeJson $A "call PingAim.DescribeToward $(Format-Vector3Arg $below)"
        $skippedSub = @($aim.hits | Where-Object { $_.currentSub -and $_.skipped }).Count
        Write-Host "  the ray through the hull steps over $skippedSub hits of the Cyclops"
        Assert-PingAt 'helm, titanium 12 m ahead and below' 'Look' 'Titanium' -Toward $below | Out-Null
    }

    Step 'Away from the helm, the Cyclops floor is pingable ("come here")' {
        Invoke-Bridge $A 'call player Player.TryEject' | Out-Null
        $left = Wait-Until { (Get-Mode $A) -eq 'Normal' } -TimeoutSeconds 15 -IntervalSeconds 0.3
        if (-not $left) { throw "$A didn't leave the helm" }
        Wait-NoCinematic $A | Out-Null
        Assert-Equal $cyclopsId (Get-State $A).currentSub.id "$A is still in the Cyclops"
        $p = (Get-State $A).position
        $floor = @([double]$p[0], ([double]$p[1] - 4), [double]$p[2])
        $aim = Invoke-BridgeJson $A "call PingAim.DescribeToward $(Format-Vector3Arg $floor)"
        $winner = @($aim.hits | Where-Object { $_.winner }) | Select-Object -First 1
        Assert-True ($winner -and $winner.currentSub) "the ray below $A lands on the Cyclops (winner: $($aim.winner))"
        Assert-PingAt 'the Cyclops floor' 'ComeHere' 'None' -Toward $floor | Out-Null
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'M9 PingAim' -Keep:$Keep
