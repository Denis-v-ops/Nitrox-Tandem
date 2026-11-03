<#
.SYNOPSIS
    COOP_FEATURES_SPEC P1: the cuddlefish follows whoever played with it last, on both games, and a "stay" command is
    shared and survives a restart.

.DESCRIPTION
    Fresh world. Alice and Bob swim in the shallows near the escape pod.
      1. Alice spawns a cuddlefish (console "spawn cutefish": a cell entity, as a cuddlefish released from a tank is; her
         game simulates it). Both games have it.
      2. Alice plays with it (CuteFishHandTarget.OnHandClick, the play scene ends by itself windowless). Her game keeps
         its simulation lock as a normal lock; the cuddlefish's friend on her game is her player.
      3. Alice swims 80 m away: on both games the cuddlefish is with Alice, and Bob's copy doesn't follow Bob. Before the
         fix, each game's copy warped to its own player every 5 s (CuteFish.WarpToPlayer), so Bob saw a cuddlefish
         follow him that wasn't there.
      4. Bob plays with it: the lock moves to Bob (exclusive, then normal), and it follows Bob on both games when he swims
         80 m away. Before the fix the click only played on Bob's game; the lock stayed with Alice.
      5. Bob tells it to stay (the right hand button while looking at it; the cuddlefish is held still for the aim). The
         server and Alice's game have "stay". Before the fix only Bob's game knew.
      6. Server and games restart: it still stays on both games, and doesn't follow Bob when he swims off.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'p1-cuddlefish')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'BaseHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$Players = @($A, $B)
# Kelp shallows next to the escape pod's start; 80 m apart, both well below the surface (CuteFishHandTarget.AllowedToInteract)
$Spots = @{
    Alice = @(-60, -10, 380)
    Bob   = @(-45, -10, 380)
    AliceAway = @(-60, -10, 300)
    BobAway   = @(-45, -10, 460)
}
$PlayerIds = @{}

function Format-Spot { param([double[]]$Spot) ($Spot | ForEach-Object { $_.ToString([Globalization.CultureInfo]::InvariantCulture) }) -join ' ' }

function Move-Player {
    param([string]$Player, [double[]]$Spot)
    Invoke-Bridge $Player "teleport $(Format-Spot $Spot)" | Out-Null
}

function Get-PlayerPosition { param([string]$Player) [double[]](Invoke-BridgeJson $Player 'state').position }

# The cuddlefish's position on a game, or $null when that game doesn't have it loaded
function Get-FishPosition {
    param([string]$Player, [string]$Id)
    $fish = @(Invoke-BridgeJson $Player 'find CuteFish 20') | Where-Object { $_.id -eq $Id } | Select-Object -First 1
    if ($fish) { [double[]]$fish.position }
}

function Get-LockHolder {
    param([string]$Id)
    $text = Invoke-Bridge 'server' "query $Id"
    if ($text -match 'Owner: (\w+) #') { $Matches[1] }
}

function Get-ServerFollowing {
    param([string]$Id)
    $text = Invoke-Bridge 'server' "query $Id"
    if ($text -match 'CuddlefishMetadata FollowingPlayer: (\w+)') { $Matches[1] }
}

function Wait-HandTargetIdle {
    param([string]$Player, [string]$Id)
    Wait-Until { (Invoke-Bridge $Player "get $Id CuteFishHandTarget.state") -eq '"None"' } -TimeoutSeconds 30 -IntervalSeconds 0.5
}

# Plays with the cuddlefish as $Player (OnHandClick, what the left click does) and waits until the play scene is over
function Invoke-Play {
    param([string]$Player, [string]$Id)
    $fish = Get-FishPosition $Player $Id
    Move-Player $Player @($fish[0], $fish[1], ($fish[2] - 1.5))
    Start-Sleep -Milliseconds 500
    Invoke-Bridge $Player "use $Id CuteFishHandTarget" | Out-Null
    $started = Wait-Until { (Invoke-Bridge $Player "get $Id CuteFishHandTarget.state") -ne '"None"' } -TimeoutSeconds 10 -IntervalSeconds 0.2
    Assert-True ([bool]$started) "$Player's play scene with the cuddlefish starts"
    Assert-True ([bool](Wait-HandTargetIdle $Player $Id)) "$Player's play scene ends"
}

# On every game, the cuddlefish has caught up with $Holder (it warps to 40 m, then swims to just in front of them) and is
# far from the other player (80 m away)
function Assert-FollowsOnBothGames {
    param([string]$Holder, [string]$Other, [string]$Id)
    $holderPosition = Get-PlayerPosition $Holder
    $otherPosition = Get-PlayerPosition $Other
    foreach ($game in $Players) {
        $near = Wait-Until {
            $fish = Get-FishPosition $game $Id
            if ($fish -and (Get-Distance $fish $holderPosition) -lt 15) { $fish }
        } -TimeoutSeconds 40
        $fish = Get-FishPosition $game $Id
        $toHolder = if ($fish) { [math]::Round((Get-Distance $fish $holderPosition), 1) } else { 'not loaded' }
        $toOther = if ($fish) { [math]::Round((Get-Distance $fish $otherPosition), 1) } else { 'not loaded' }
        Assert-True ([bool]$near) "on $game's game the cuddlefish is with $Holder ($toHolder m from $Holder, $toOther m from $Other)"
        Assert-True ($fish -and (Get-Distance $fish $otherPosition) -gt 50) "on $game's game it doesn't follow $Other ($toOther m from $Other)"
    }
}

try {
    Start-ScenarioGames -Players $Players -SaveName $SaveName -NoLaunch:$NoLaunch
    foreach ($player in $Players) {
        $PlayerIds[$player] = (Invoke-BridgeJson $player 'state').playerId
        Move-Player $player $Spots[$player]
    }
    Start-Sleep -Seconds 3
    $fishId = $null

    Step "$A spawns a cuddlefish" {
        Invoke-Bridge $A 'console spawn cutefish' | Out-Null
        $found = Wait-Until { @(Invoke-BridgeJson $A 'find CuteFish 5') | Select-Object -First 1 } -TimeoutSeconds 20
        Assert-True ([bool]$found) "$A has a cuddlefish"
        $script:fishId = $found.id
        $onB = Wait-Until { Get-FishPosition $B $script:fishId } -TimeoutSeconds 30
        Assert-True ([bool]$onB) "$B has the cuddlefish too"
        $holder = Wait-Until { Get-LockHolder $script:fishId } -TimeoutSeconds 15
        Assert-Equal $A $holder "$A's game simulates it"
    }
    $fishId = $script:fishId

    Step "$A plays with it and swims away: it follows $A on both games" {
        Invoke-Play $A $fishId
        Start-Sleep -Seconds 1
        Assert-Equal $A (Get-LockHolder $fishId) "$A still simulates it after playing"
        Assert-Equal $PlayerIds[$A] (Invoke-BridgeJson $A "get $fishId CreatureFriend._friend").id "the cuddlefish's friend on $A's game is $A"
        Move-Player $A $Spots.AliceAway
        Assert-FollowsOnBothGames $A $B $fishId
    }

    Step "$B plays with it and swims away: it follows $B on both games" {
        Move-Player $B @(($Spots.AliceAway[0] + 6), $Spots.AliceAway[1], $Spots.AliceAway[2])
        Start-Sleep -Seconds 3
        Invoke-Play $B $fishId
        $holder = Wait-Until { $h = Get-LockHolder $fishId; if ($h -eq $B) { $h } } -TimeoutSeconds 10
        Assert-Equal $B $holder "playing with it moved its lock to $B"
        Assert-Equal $PlayerIds[$B] (Invoke-BridgeJson $B "get $fishId CreatureFriend._friend").id "the cuddlefish's friend on $B's game is $B"
        Move-Player $A $Spots.Alice
        Move-Player $B $Spots.BobAway
        Assert-FollowsOnBothGames $B $A $fishId
        Assert-Equal $B (Get-LockHolder $fishId) "$B keeps simulating it (a normal lock once the play scene is over)"
    }

    Step "$B tells it to stay" {
        foreach ($player in $Players) { Invoke-Bridge $player "set $fishId Rigidbody.isKinematic true" | Out-Null }
        Start-Sleep -Seconds 1
        $fish = Get-FishPosition $B $fishId
        Move-Player $B @($fish[0], $fish[1], ($fish[2] - 1.5))
        Start-Sleep -Milliseconds 500
        Invoke-Bridge $B "hold $fishId 0.4 RightHand" | Out-Null
        $stays = Wait-Until { (Invoke-Bridge $B "get $fishId CuteFish._followingPlayer") -eq 'false' } -TimeoutSeconds 10 -IntervalSeconds 0.5
        Assert-True ([bool]$stays) "$B's command makes it stay on $B's game"
        Wait-HandTargetIdle $B $fishId | Out-Null
        foreach ($player in $Players) { Invoke-Bridge $player "set $fishId Rigidbody.isKinematic false" | Out-Null }
        $serverStays = Wait-Until { (Get-ServerFollowing $fishId) -eq 'False' } -TimeoutSeconds 10
        Assert-True ([bool]$serverStays) 'the server keeps "stay" in the cuddlefish''s metadata'
        $aStays = Wait-Until { (Invoke-Bridge $A "get $fishId CuteFish._followingPlayer") -eq 'false' } -TimeoutSeconds 10
        Assert-True ([bool]$aStays) "$A's game has it stay too"
        Assert-Equal 'false' (Invoke-Bridge $A "get $fishId CuteFish.creatureFollowPlayer.enabled") "$A's copy doesn't follow anyone"
        Assert-Equal $B (Get-LockHolder $fishId) "$B still simulates it after the command"
    }
    Assert-NoDiff -Ids @($fishId) 'no desync reported for the cuddlefish'

    Step 'Server and games restart: it still stays' {
        $before = Get-FishPosition $B $fishId
        Restart-ScenarioWorld -SaveName $SaveName -Players $Players
        foreach ($player in $Players) {
            # Players rejoin where they left: Bob next to it, Alice 80 m away; bring Alice over so both games load it
            if ($player -eq $A) { Move-Player $A @(($before[0] + 8), $before[1], $before[2]) }
        }
        foreach ($player in $Players) {
            $loaded = Wait-Until { Get-FishPosition $player $fishId } -TimeoutSeconds 60
            Assert-True ([bool]$loaded) "$player's game has the cuddlefish after the restart"
            Assert-True ([bool](Wait-Until { (Invoke-Bridge $player "get $fishId CuteFish._followingPlayer") -eq 'false' } -TimeoutSeconds 10)) "it still stays on $player's game"
        }
        Assert-Equal 'False' (Get-ServerFollowing $fishId) 'the server still has "stay"'
        $stayed = Get-FishPosition $B $fishId
        Move-Player $B $Spots.Bob
        Move-Player $A $Spots.Alice
        Start-Sleep -Seconds 15
        $fishNow = Get-FishPosition $A $fishId
        if (-not $fishNow) { $fishNow = Get-FishPosition $B $fishId }
        $bob = Get-PlayerPosition $B
        $moved = if ($fishNow) { [math]::Round((Get-Distance $fishNow $stayed), 1) } else { 'not loaded' }
        Assert-True ($fishNow -and (Get-Distance $fishNow $stayed) -lt 25 -and (Get-Distance $fishNow $bob) -gt 40) "it stays where it was when $B swims off (moved $moved m)"
    }
    Assert-NoDiff -Ids @($fishId) 'no desync reported for the cuddlefish after the restart'
}
catch {
    Add-Failure $_.Exception.Message
}
Complete-Scenario -Name 'P1 CuddlefishBoth' -Keep:$Keep
