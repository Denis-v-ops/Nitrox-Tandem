<#
.SYNOPSIS
    COOP_FEATURES_SPEC T2: buddy breathing. Alice holds the interact button on Bob's body while Bob is low on air; Bob
    gets about half of what Alice pays (BuddyBreathingEfficiency 0.5, applied by the server), and at depth it slows his
    drowning without refilling.

.DESCRIPTION
    1. Shallows, oxygen cheat on for both (no breathing, so only the transfer moves the numbers): Alice equips a tank,
       Bob drops to 8 of 45, Alice sees it in Bob's PlayerStats, then holds on Bob's body for 3 s through the bridge
       (GUIHand targets the body, the reticle shows progress). Alice pays about 1 per 0.5 s; Bob gets half of it; both
       games play the bubbles.
    2. Control: Bob back to full air, the same hold gives nothing (the action is only offered below 30 %).
    3. At 226 m (Crush depth class, no Rebreather: 2 units/s), Bob's oxygen cheat off and a high-capacity tank: his
       drain over 8 s alone, then over an 8 s hold by Alice. Buddy breathing must slow it without stopping it.

    Negative control (2026-11-02): with OxygenShareProcessor taken out, the default processor relays the donor's
    packet unchanged, and step 1 fails (Bob gets as much as Alice pays).

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Get-State { param([string]$Player) Invoke-BridgeJson $Player 'state' }
function Get-Oxygen { param([string]$Player) [double](Get-State $Player).oxygen }

# Bob's oxygen as Alice's game knows it, from Bob's last PlayerStats (every 3 s).
function Get-SeenOxygen { [double](Invoke-Bridge $A "get $bobId PartnerHandTarget.Oxygen") }

function Set-Oxygen {
    param([string]$Player, [double]$Value)
    $now = Get-Oxygen $Player
    if ($now -gt $Value) { Invoke-Bridge $Player "call player OxygenManager.RemoveOxygen $($now - $Value)" | Out-Null }
    elseif ($now -lt $Value) { Invoke-Bridge $Player "call player OxygenManager.AddOxygen $($Value - $now)" | Out-Null }
}

function Add-Tank {
    param([string]$Player, [string]$Item = 'tank')
    if (-not (Get-State $Player).equipment.Tank) {
        Invoke-Bridge $Player "console item $Item" | Out-Null   # equips itself into the free tank slot
        Wait-Until { (Get-State $Player).equipment.Tank } -TimeoutSeconds 10 | Out-Null
    }
    Assert-True ([bool](Get-State $Player).equipment.Tank) "$Player wears a tank ($((Get-State $Player).equipment.Tank.techType))"
}

function Get-Capacity { param([string]$Player) [double](Invoke-Bridge $Player 'call player OxygenManager.GetOxygenCapacity') }

# Alice holds the interact button on Bob's body; returns the bridge's hold status once it ended.
function Invoke-AliceHold {
    param([double]$Seconds)
    Invoke-Bridge $A "hold $bobId $Seconds" | Out-Null
    Wait-Until { $s = Invoke-BridgeJson $A 'hold status'; if (-not $s.active) { $s } } -TimeoutSeconds ([int]$Seconds + 10) -IntervalSeconds 0.5
}

function Move-Both {
    param([double[]]$Spot)
    Invoke-Bridge $A "teleport $($Spot[0]) $($Spot[1]) $($Spot[2])" | Out-Null
    Invoke-Bridge $B "teleport $($Spot[0] + 1) $($Spot[1]) $($Spot[2])" | Out-Null
    Start-Sleep -Seconds 4
    foreach ($player in $A, $B) {
        $state = Get-State $player
        Assert-True ([bool]$state.isUnderwater) "$player is underwater at $(Format-Vector ([double[]]$state.position))"
    }
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch
    $script:bobId = (Get-State $B).playerId

    Step 'Both players underwater in the shallows, a metre apart; Alice wears a tank' {
        Move-Both @(-60, -10, 380)
        Add-Tank $A
        Set-Oxygen $A (Get-Capacity $A)
        Invoke-Bridge $A 'sounds clear' | Out-Null
        Invoke-Bridge $B 'sounds clear' | Out-Null
    }

    Step 'Bob runs low; Alice holds on him for 3 s: he gets about half of what she pays' {
        Set-Oxygen $B 8
        $seen = Wait-Until { $o = Get-SeenOxygen; if ($o -lt 13.5 -and $o -gt 0) { $o } } -TimeoutSeconds 10
        Assert-True ([bool]$seen) "Alice's game sees Bob low on air ($seen of 45)"
        $a0 = Get-Oxygen $A; $b0 = Get-Oxygen $B
        $status = Invoke-AliceHold 3
        Start-Sleep -Seconds 1
        $a1 = Get-Oxygen $A; $b1 = Get-Oxygen $B
        $paid = $a0 - $a1; $got = $b1 - $b0
        Write-Host "  Alice $a0 -> $a1 (paid $paid), Bob $b0 -> $b1 (got $got); hold: $($status | ConvertTo-Json -Compress -Depth 3)"
        Assert-True ($status.targetedFrames -gt 0) "GUIHand targeted Bob's body ($($status.targetedFrames) of $($status.frames) frames, $($status.distance) m)"
        Assert-True ($status.maxProgress -gt 0) "the reticle showed progress ($($status.maxProgress))"
        Assert-True ($paid -ge 4 -and $paid -le 7) "Alice paid about 1 per 0.5 s ($paid)"
        Assert-True ($paid -gt 0 -and [Math]::Abs($got - $paid * 0.5) -le 0.6) "Bob got half of it ($got of $paid)"
        $sounds = @(Invoke-BridgeJson $A 'sounds 15 BuddyBreathing')
        Assert-True ($sounds.Count -ge 4) "Alice's game played the bubbles at Bob's body ($($sounds.Count) times)"
        $sounds = @(Invoke-BridgeJson $B 'sounds 15 BuddyBreathing')
        Assert-True ($sounds.Count -ge 4) "Bob's game played the bubbles ($($sounds.Count) times)"
    }

    Step 'Control: Bob has air again, the same hold gives nothing' {
        Set-Oxygen $B 45
        $seen = Wait-Until { $o = Get-SeenOxygen; if ($o -gt 40) { $o } } -TimeoutSeconds 10
        Assert-True ([bool]$seen) "Alice's game sees Bob's air back up ($seen)"
        $a0 = Get-Oxygen $A; $b0 = Get-Oxygen $B
        Invoke-AliceHold 2 | Out-Null
        Start-Sleep -Seconds 1
        Assert-True ([Math]::Abs((Get-Oxygen $A) - $a0) -lt 0.2) "Alice paid nothing ($a0 -> $(Get-Oxygen $A))"
        Assert-True ([Math]::Abs((Get-Oxygen $B) - $b0) -lt 0.2) "Bob got nothing ($b0 -> $(Get-Oxygen $B))"
    }

    Step 'At 226 m Bob breathes for real: his drain alone, then with Alice sharing' {
        Move-Both @(-802, -226, 409)
        # A high-capacity tank, so Bob can be below 30 % and still breathe for 16 s.
        Add-Tank $B 'doubletank'
        $depthClass = Invoke-Bridge $B 'get Player.main.depthClass.value'
        Assert-Equal 3 $depthClass "Bob is in the Crush depth class (2 units/s without a Rebreather)"
        $capacity = Get-Capacity $B
        $low = [Math]::Floor($capacity * 0.3) - 1
        Set-Oxygen $B $low
        Set-Oxygen $A (Get-Capacity $A)
        Invoke-Bridge $B 'call GameModeUtils.DeactivateCheat NoOxygen' | Out-Null
        try {
            $seen = Wait-Until { $o = Get-SeenOxygen; if ($o -lt $capacity * 0.3 -and $o -gt 0) { $o } } -TimeoutSeconds 10
            Assert-True ([bool]$seen) "Alice's game sees Bob below 30 % ($seen of $capacity)"
            $t0 = Get-Date; $b0 = Get-Oxygen $B
            Start-Sleep -Seconds 8
            $t1 = Get-Date; $b1 = Get-Oxygen $B; $a1 = Get-Oxygen $A
            $status = Invoke-AliceHold 8
            $t2 = Get-Date; $b2 = Get-Oxygen $B; $a2 = Get-Oxygen $A
            $aloneRate = ($b0 - $b1) / ($t1 - $t0).TotalSeconds
            $helpedRate = ($b1 - $b2) / ($t2 - $t1).TotalSeconds
            $paidRate = ($a1 - $a2) / ($t2 - $t1).TotalSeconds
            Write-Host ("  Bob alone {0} -> {1} ({2:N2}/s); with Alice {1} -> {3} ({4:N2}/s); Alice paid {5:N2}/s; held {6} s, targeted {7} frames" -f $b0, $b1, $aloneRate, $b2, $helpedRate, $paidRate, $status.elapsed, $status.targetedFrames)
            Assert-True ($aloneRate -ge 1.5) ("Bob drowns at about 2/s alone ({0:N2}/s)" -f $aloneRate)
            Assert-True ($helpedRate -gt 0) ("buddy breathing doesn't refill Bob ({0:N2}/s)" -f $helpedRate)
            Assert-True ($helpedRate -le $aloneRate * 0.75) ("buddy breathing slows Bob's drowning ({0:N2}/s instead of {1:N2}/s)" -f $helpedRate, $aloneRate)
        }
        finally {
            Invoke-Bridge $B 'call GameModeUtils.ActivateCheat NoOxygen' | Out-Null
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'T2 BuddyBreathing' -Keep:$Keep
