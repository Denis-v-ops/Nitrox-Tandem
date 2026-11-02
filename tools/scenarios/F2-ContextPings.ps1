<#
.SYNOPSIS
    COOP_FEATURES_SPEC F2: context pings. Alice pings a creature, an item and open ground; Bob's game shows a Danger, a
    Look and a Come here marker with the right icon, colour, label and lifetime. The item icon survives uGUI_Pings
    re-adding its markers, a "come here" ping ends when the partner arrives, and interference limits pings to 50 m.

.DESCRIPTION
    Alice aims with the bridge's "hold <id> <s> None" (turns her camera toward the target every frame) and presses the
    ping through PlayerPingManager.RequestPing / RequestComeHerePing, the methods the two key bindings call. Both games'
    pings and what their markers show are read with PlayerPingManager.Describe (Debug builds): kind, label, the sprite
    the uGUI_Pings marker shows and the one it should show, colours, age and lifetime. Labels are compared with the
    game's own language strings, so the check holds in any game language.

    Steps: Danger (a frozen Stalker), Look (frozen titanium), the markers re-added (uGUI_Pings.OnDisable/OnEnable, what
    the game runs when the component is re-enabled), Come here (ground), lifetimes (15/10/30 s), the "come here" key on a
    creature, arrival, interference (SignalInterference.ForceLocalInterfered on either side), and a table of what a
    ping at each creature type would say (PingTarget.Describe), alive and dead.

    On a build without F2 the first step fails (PlayerPingManager.Describe is missing): the negative control.
    Starts a fresh world and two games (opens windows unless $env:NITROX_WINDOWLESS=1), unless -NoLaunch. Needs a Debug
    build (dual-client.ps1 -NoBuild is used, so build first).
#>
param([switch]$NoLaunch, [switch]$Keep,
    # Skip the creature table at the end (about 1 min).
    [switch]$NoTable)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'CreatureAggroHelpers.ps1')
$SaveName = 'f2-context-pings'

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

<#
    The player aims at the target (a NitroxId; none: keep the current view) and presses a ping key. Returns the new ping
    as the player's own game describes it.
#>
function Send-Ping {
    param([string]$Player, [string]$Target, [string]$Method = 'RequestPing')
    $before = @(Get-Pings $Player | ForEach-Object { $_.pingId })
    if ($Target) {
        Invoke-Bridge $Player "hold $Target 3 None" | Out-Null
        Start-Sleep -Milliseconds 500
    }
    # The 0.5 s cooldown between pings.
    Start-Sleep -Milliseconds 600
    Invoke-Bridge $Player "call PlayerPingManager.$Method" | Out-Null
    # Windowless only: a batch-mode cursor can't lock, so MainGameController.Update shows it every frame and during the
    # scripts' Update AvatarInputHandler.IsEnabled() is false (the ping is refused as if a menu were open). Between
    # frames, where the bridge runs, the cursor is hidden again: run the manager's Update (the real CanCreatePing and
    # CreatePing) from there.
    Invoke-Bridge $Player 'call PlayerPingManager.instance.Update' | Out-Null
    $new =Wait-Until { Get-Pings $Player | Where-Object { $before -notcontains $_.pingId } | Select-Object -First 1 } -TimeoutSeconds 5 -IntervalSeconds 0.25
    if ($Target) { Invoke-Bridge $Player 'hold off' | Out-Null }
    if (-not $new) { throw "$Player's $Method made no ping" }
    $new
}

function Wait-RemotePing {
    param([string]$Player, [string]$PingId, [int]$TimeoutSeconds = 5)
    Wait-Until { Get-Ping $Player $PingId } -TimeoutSeconds $TimeoutSeconds -IntervalSeconds 0.25
}

function Assert-Ping {
    param($Ping, [string]$Who, [string]$Kind, [string]$TechType, [string]$Label, [string]$PingType, [double]$Lifetime, [string]$Color)
    Assert-True ([bool]$Ping) "$Who shows the ping"
    if (-not $Ping) { return }
    Assert-Equal $Kind $Ping.kind "${Who}: kind"
    Assert-Equal $TechType $Ping.targetTechType "${Who}: target"
    Assert-Equal $Label $Ping.label "${Who}: label"
    Assert-Equal $PingType $Ping.pingType "${Who}: ping type"
    Assert-Equal $Lifetime $Ping.lifetime "${Who}: lifetime"
    Assert-Ui $Ping $Who $Color
}

# What the marker on screen shows: the item's own sprite (Look) or the ping type's, in the ping's colour.
function Assert-Ui {
    param($Ping, [string]$Who, [string]$Color)
    $expectedIcon = if ($Ping.expectedIcon) { $Ping.expectedIcon } else { $Ping.pingTypeIcon }
    Assert-True ([bool]$expectedIcon) "${Who}: $($Ping.kind) has an icon to show"
    Assert-Equal $expectedIcon $Ping.markerIcon "${Who}: the $($Ping.kind) marker shows its icon"
    if ($Color) { Assert-Equal $Color $Ping.expectedColor "${Who}: $($Ping.kind) colour" }
    Assert-Equal $Ping.expectedColor $Ping.markerColor "${Who}: the $($Ping.kind) marker has its colour"
}

# Freezes a spawned object where it is, so the aim and the ping ray find it.
function Set-Frozen {
    param([string]$Player, [string]$Id)
    Invoke-Bridge $Player "set $Id Rigidbody.isKinematic true" | Out-Null
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    $pod = (Invoke-BridgeJson $A 'find EscapePod 1') | Select-Object -First 1
    $center = if ($pod) { [double[]](Invoke-BridgeJson $A "entity $($pod.id)").position } else { [double[]](Invoke-BridgeJson $A 'state').position }
    # Open water in the Safe Shallows, 70 m from the pod; Bob 25 m away (beyond a "come here" arrival).
    $spot = @(($center[0] + 70), -6, $center[2])
    $bobSpot = @($spot[0], -6, ($spot[2] + 25))
    Invoke-Bridge $A "teleport $(Format-Point $spot)" | Out-Null
    Invoke-Bridge $B "teleport $(Format-Point $bobSpot)" | Out-Null
    Start-Sleep -Seconds 3
    $language = Invoke-Bridge $B 'get Language.main.currentLanguage'
    Write-Host "  game language: $language"
    $templates = @{}
    foreach ($key in 'Nitrox_Ping_Danger', 'Nitrox_Ping_Look', 'Nitrox_Ping_ComeHere') { $templates[$key] = Get-Text $B $key }
    $pings = [ordered]@{}

    Step 'Set up: a frozen Stalker on one side of Alice, frozen titanium on the other' {
        Assert-True (-not (Invoke-Bridge $A 'call PlayerPingManager.Describe' -AllowError).StartsWith('ERR ')) 'the build has F2 (PlayerPingManager.Describe)'
        Invoke-Bridge $A 'look 90 0' | Out-Null
        $script:stalker = New-Creature $A 'Stalker' 10
        Set-Frozen $A $stalker
        Assert-Equal 'Danger Stalker' (Invoke-Bridge $A "call PingTarget.Describe $stalker" | ConvertFrom-Json) 'a ping at the Stalker would be Danger'

        $before = @((Invoke-BridgeJson $A 'find Pickupable 50') | ForEach-Object { $_.id })
        Invoke-Bridge $A 'look -90 0' | Out-Null
        Invoke-Bridge $A 'console spawn titanium 1 6' | Out-Null
        $item = Wait-Until { (Invoke-BridgeJson $A 'find Pickupable 50') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id -and $_.techType -eq 'Titanium' } | Select-Object -First 1 } -TimeoutSeconds 10
        if (-not $item) { throw 'the titanium did not spawn' }
        $script:titanium = $item.id
        Set-Frozen $A $titanium
        Assert-Equal 'Look Titanium' (Invoke-Bridge $A "call PingTarget.Describe $titanium" | ConvertFrom-Json) 'a ping at the titanium would be Look'
    }

    # One step from the first ping to the last lifetime: a step's desync check takes seconds, the pings last 10-30 s.
    Step 'Alice pings the Stalker, the titanium and the ground; what Bob sees and for how long' {
        Write-Host '  -- Danger: the Stalker'
        $own = Send-Ping $A $stalker
        $label = ($templates['Nitrox_Ping_Danger'] -f $A, (Get-Text $A 'Stalker')).Trim()
        Assert-Ping $own "$A (own)" 'Danger' 'Stalker' $label 'Signal' 15 'FF0000'
        $remote = Wait-RemotePing $B $own.pingId
        Assert-Ping $remote $B 'Danger' 'Stalker' $label 'Signal' 15 'FF0000'
        $pings['Danger'] = $own.pingId

        Write-Host '  -- Look: the titanium'
        $own = Send-Ping $A $titanium
        $label = ($templates['Nitrox_Ping_Look'] -f $A, (Get-Text $A 'Titanium')).Trim()
        Assert-Ping $own "$A (own)" 'Look' 'Titanium' $label 'Signal' 10 'FFFFFF'
        Assert-True ($own.expectedIcon -and $own.expectedIcon -ne $own.pingTypeIcon) "the Look ping uses the item's own sprite ($($own.expectedIcon), not $($own.pingTypeIcon))"
        $remote = Wait-RemotePing $B $own.pingId
        Assert-Ping $remote $B 'Look' 'Titanium' $label 'Signal' 10 'FFFFFF'
        $pings['Look'] = $own.pingId

        Write-Host '  -- Come here: the ground below Alice'
        Invoke-Bridge $A 'look 0 -75' | Out-Null
        Start-Sleep -Milliseconds 300
        $own = Send-Ping $A $null
        $label = ($templates['Nitrox_Ping_ComeHere'] -f $A).Trim()
        Assert-Ping $own "$A (own)" 'ComeHere' 'None' $label 'Beacon' 30 $null
        Assert-True ([double]$own.position[1] -lt $spot[1] - 1) "the ping is on the ground below Alice (y $($own.position[1]))"
        $remote = Wait-RemotePing $B $own.pingId
        Assert-Ping $remote $B 'ComeHere' 'None' $label 'Beacon' 30 $own.expectedColor
        $pings['ComeHere'] = $own.pingId

        Write-Host "  -- Bob's markers re-added (uGUI_Pings.OnDisable, OnEnable)"
        Invoke-Bridge $B 'call find:uGUI_Pings uGUI_Pings.OnDisable' | Out-Null
        $released = Get-Ping $B $pings['Look']
        Assert-True ($released -and -not $released.markerIcon) "the markers are gone after OnDisable (marker: $($released.markerIcon))"
        Invoke-Bridge $B 'call find:uGUI_Pings uGUI_Pings.OnEnable' | Out-Null
        foreach ($kind in 'Danger', 'Look', 'ComeHere') {
            $ping = Get-Ping $B $pings[$kind]
            Assert-True ([bool]$ping) "$B still has the $kind ping"
            if ($ping) { Assert-Ui $ping "$B after the re-add" $null }
        }

        Write-Host '  -- Lifetimes on Bob: Danger 15 s, Look 10 s, Come here 30 s'
        $lastAge = @{}
        $gone = @{}
        $deadline = (Get-Date).AddSeconds(40)
        while ((Get-Date) -lt $deadline -and $gone.Count -lt 3) {
            $now = Get-Pings $B
            foreach ($kind in 'Danger', 'Look', 'ComeHere') {
                if ($gone.ContainsKey($kind)) { continue }
                $ping = $now | Where-Object { $_.pingId -eq $pings[$kind] } | Select-Object -First 1
                if ($ping) { $lastAge[$kind] = [double]$ping.age } else { $gone[$kind] = $true }
            }
            Start-Sleep -Milliseconds 400
        }
        foreach ($kind in 'Danger', 'Look', 'ComeHere') {
            $lifetime = @{ Danger = 15; Look = 10; ComeHere = 30 }[$kind]
            Assert-True ($gone.ContainsKey($kind)) "$B's $kind ping ended"
            Assert-True ($lastAge[$kind] -ge $lifetime - 1.5 -and $lastAge[$kind] -le $lifetime) "$B's $kind ping lasted its $lifetime s (last seen at $($lastAge[$kind]) s)"
        }
    }

    Step 'The "come here" key on a creature still says come here' {
        $own = Send-Ping $A $stalker 'RequestComeHerePing'
        Assert-Equal 'ComeHere' $own.kind "$A's ping"
        $remote = Wait-RemotePing $B $own.pingId
        Assert-Equal 'ComeHere' $remote.kind "$B's ping"
    }

    Step 'Come here ends when the partner arrives' {
        Invoke-Bridge $A 'look 0 -75' | Out-Null
        $own = Send-Ping $A $null
        $remote = Wait-RemotePing $B $own.pingId
        Assert-True ([bool]$remote) "$B shows the come-here ping"
        Start-Sleep -Seconds 2
        Assert-True ([bool](Get-Ping $B $own.pingId)) "$B's ping stays while he is 25 m away"
        $target = @([double]$own.position[0], ([double]$own.position[1] + 1), [double]$own.position[2])
        Invoke-Bridge $B "teleport $(Format-Point $target)" | Out-Null
        $bobDone = Wait-Until { -not (Get-Ping $B $own.pingId) } -TimeoutSeconds 3 -IntervalSeconds 0.25
        Assert-True ([bool]$bobDone) "$B's ping ends when he arrives"
        $aliceDone = Wait-Until { -not (Get-Ping $A $own.pingId) } -TimeoutSeconds 5 -IntervalSeconds 0.25
        Assert-True ([bool]$aliceDone) "$A's own ping ends when $B arrives"
    }

    Step 'Interference: pings only within 50 m of the sender' {
        foreach ($case in @(
                @{ interfered = $A; distance = 80; shown = $false },
                @{ interfered = $A; distance = 30; shown = $true },
                @{ interfered = $B; distance = 80; shown = $false },
                @{ interfered = $null; distance = 80; shown = $true })) {
            foreach ($player in $A, $B) {
                Invoke-Bridge $player "set SignalInterference.ForceLocalInterfered $(($case.interfered -eq $player).ToString().ToLower())" | Out-Null
            }
            Invoke-Bridge $B "teleport $(Format-Point @(($spot[0] + $case.distance), $spot[1], $spot[2]))" | Out-Null
            # PlayerStats (with the flag) goes out every 3 s; movement is faster.
            Start-Sleep -Seconds 4
            $dropped = [int](Invoke-Bridge $B 'get PlayerPingManager.DroppedByInterference')
            Invoke-Bridge $A 'look 0 -75' | Out-Null
            $own = Send-Ping $A $null
            $remote = Wait-RemotePing $B $own.pingId 3
            $who = if ($case.interfered) { "$($case.interfered) in interference" } else { 'no interference' }
            Assert-Equal $case.shown ([bool]$remote) "$who, $B $($case.distance) m away: shown"
            $droppedNow = [int](Invoke-Bridge $B 'get PlayerPingManager.DroppedByInterference')
            Assert-Equal ([int](-not $case.shown)) ($droppedNow - $dropped) "$who, $B $($case.distance) m away: pings $B dropped"
        }
        foreach ($player in $A, $B) { Invoke-Bridge $player 'set SignalInterference.ForceLocalInterfered false' | Out-Null }
    }

    if (-not $NoTable) {
        Step 'What a ping at each creature would say (alive, then dead)' {
            Invoke-Bridge $B "teleport $(Format-Point $bobSpot)" | Out-Null
            $expected = [ordered]@{
                Stalker = 'Danger'; Sandshark = 'Danger'; Biter = 'Danger'; Blighter = 'Danger'; Crash = 'Danger'; BoneShark = 'Danger'; Shocker = 'Danger'
                Crabsnake = 'Danger'; Mesmer = 'Danger'; Warper = 'Danger'; LavaLizard = 'Danger'; CrabSquid = 'Danger'; SpineEel = 'Danger'; CaveCrawler = 'Danger'
                Bleeder = 'Danger'; Peeper = 'Look'; Bladderfish = 'Look'; Boomerang = 'Look'; Gasopod = 'ComeHere'; Jellyray = 'ComeHere'; RabbitRay = 'ComeHere'
                SeaTreader = 'ComeHere'; Reefback = 'ComeHere'; ReaperLeviathan = 'Danger'
            }
            $rows = New-Object System.Collections.ArrayList
            foreach ($techType in $expected.Keys) {
                $before = @((Invoke-BridgeJson $A 'find Creature 100') | ForEach-Object { $_.id })
                Invoke-Bridge $A "console spawn $techType 1 15" | Out-Null
                $new = Wait-Until { (Invoke-BridgeJson $A 'find Creature 100') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id -and $_.techType -eq $techType } | Select-Object -First 1 } -TimeoutSeconds 10
                if (-not $new) { [void]$rows.Add([pscustomobject]@{ creature = $techType; alive = 'not spawned'; dead = '' }); continue }
                $alive = Invoke-Bridge $A "call PingTarget.Describe $($new.id)" | ConvertFrom-Json
                Invoke-Bridge $A "call $($new.id) LiveMixin.Kill" -AllowError | Out-Null
                Start-Sleep -Milliseconds 300
                $dead = Invoke-Bridge $A "call PingTarget.Describe $($new.id)" -AllowError
                [void]$rows.Add([pscustomobject]@{ creature = $techType; alive = $alive; dead = $dead })
                # A "come here" ping names no target.
                $want = if ($expected[$techType] -eq 'ComeHere') { 'ComeHere None' } else { "$($expected[$techType]) $techType" }
                Assert-Equal $want $alive "a ping at a live $techType"
                if ("$dead" -match "isn't loaded") {
                    Write-Host "  (a dead $techType is gone at once: nothing to ping)"
                }
                else {
                    Assert-True (-not "$dead".StartsWith('"Danger') -and -not "$dead".StartsWith('ERR ')) "a dead $techType is no danger ($dead)"
                }
            }
            $rows | Format-Table -AutoSize | Out-String | Write-Host
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
foreach ($player in $A, $B) { Invoke-Bridge $player 'set SignalInterference.ForceLocalInterfered false' -AllowError | Out-Null }
Complete-Scenario -Name 'F2 ContextPings' -Keep:$Keep
