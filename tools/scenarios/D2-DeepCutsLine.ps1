<#
.SYNOPSIS
    COOP_FEATURES_SPEC D2: the partner's marker drops to a last contact in caves and the deep, and beacons bring it back.

.DESCRIPTION
    Alice stays in open water (Grassy Plateaus, 60 m). Bob goes to 400 m (above Lifepod 29, biome bloodKelpTwo: the
    depth decides), moves on at that depth, gets a beacon next to him, loses it, comes back to 50 m, then visits the
    Jellyshroom Cave at 258 m (the biome decides). Then Alice goes deep herself (either player cuts the line), and her
    setting "Always" ignores it.

    Each game's own decision is read from SignalInterference.LocalReason; what Alice sees of Bob from her game: the
    marker's PingInstance (origin position, label, minDist; through PartnerSignal, see Read-Marker), the PartnerSignal
    component (rule, link cut) and Bob's name tag. The marker must stay where contact was lost while Bob's body moves on.

    On unfixed coop the first deep step fails (the marker keeps following Bob): the negative control.
    Starts a fresh world and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\D2-DeepCutsLine.ps1
#>
param([switch]$NoLaunch, [switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$aliceSpot = @(263, -60, 64)
$shallowSpot = @(263, -50, 164)          # Grassy Plateaus, 50 m, 100 m from Alice
$deepSpot = @(-485, -400, 1326)          # above Lifepod 29 (floor about 500 m), bloodKelpTwo
$deepSpot2 = @(-445, -400, 1326)         # 40 m on, same depth
$jellyshroomSpot = @(114, -258, -372)    # Jellyshroom Cave at the Degasi base, 258 m (A0: JellyshroomCaves)

function Format-Spot { param([double[]]$P) ($P | ForEach-Object { $_.ToString('0.###') }) -join ' ' }

function Get-Distance {
    param($P, $Q)
    [math]::Sqrt(([double]$P[0] - [double]$Q[0]) * ([double]$P[0] - [double]$Q[0]) + ([double]$P[1] - [double]$Q[1]) * ([double]$P[1] - [double]$Q[1]) + ([double]$P[2] - [double]$Q[2]) * ([double]$P[2] - [double]$Q[2]))
}

# A field of the partner's marker (its PingInstance). Through PartnerSignal, which holds the marker it found: once the
# partner carries a beacon, "get <id> PingInstance" can reach the beacon's PingInstance instead. A build without D2 has
# no such members, so the PingInstance is read directly there (before the beacon step that is the marker).
function Read-Marker {
    param([string]$Observer, [string]$PartnerId, [string]$SignalMember, [string]$PingMember)
    $value = Invoke-Bridge $Observer "get $PartnerId PartnerSignal.$SignalMember" -AllowError
    if ($value.StartsWith('ERR ')) { $value = Invoke-Bridge $Observer "get $PartnerId PingInstance.$PingMember" }
    $value
}

# What one game sees of its partner
function Read-Signal {
    param([string]$Observer, [string]$PartnerId)
    $s = [ordered]@{}
    foreach ($member in 'Rule', 'Label', 'LinkCut', 'Frozen') { $s[$member] = (Invoke-Bridge $Observer "get $PartnerId PartnerSignal.$member" -AllowError).Trim('"') }
    $s['minDist'] = [double](Read-Marker $Observer $PartnerId 'MinDist' 'minDist')
    # Where uGUI_Pings draws the marker (PingInstance.GetPosition() is origin.position)
    $s['marker'] = Read-Marker $Observer $PartnerId 'MarkerPosition' 'origin.position' | ConvertFrom-Json
    $s['body'] = Invoke-BridgeJson $Observer "get $PartnerId Transform.position"
    $s['nameTag'] = Invoke-Bridge $Observer "get $PartnerId RemotePlayerVitals.canvas.enabled"
    Write-Host "  $Observer sees: $($s | ConvertTo-Json -Compress)"
    $s
}

# "ERR ..." on a build without D2, so the steps go on to the behavioural checks
function Get-Reason { param([string]$Player) (Invoke-Bridge $Player 'get SignalInterference.LocalReason' -AllowError).Trim('"') }

function Wait-Rule {
    param([string]$Observer, [string]$PartnerId, [string]$Rule)
    # The partner's flag comes with PlayerStats (every 3 s); the marker is updated four times a second
    [bool](Wait-Until { (Invoke-Bridge $Observer "get $PartnerId PartnerSignal.Rule" -AllowError).Trim('"') -eq $Rule } -TimeoutSeconds 12 -IntervalSeconds 0.5)
}

function Move-Player {
    param([string]$Player, [double[]]$Spot)
    Invoke-Bridge $Player "teleport $(Format-Spot $Spot)" | Out-Null
    # Movement packets, the biome (LateUpdate) and the next interference check
    Start-Sleep -Seconds 2
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch
    $script:aliceId = (Invoke-BridgeJson $A 'state').playerId
    $script:bobId = (Invoke-BridgeJson $B 'state').playerId

    Step 'Both in open water: the faint marker follows Bob' {
        Move-Player $A $aliceSpot
        Move-Player $B $shallowSpot
        $reason = Get-Reason $B
        Assert-True ($reason -like 'clear*') "Bob's game: not in interference at 50 m ($reason)"
        Assert-True (Wait-Rule $A $bobId 'faint') "Alice's marker for Bob is faint"
        $s = Read-Signal $A $bobId
        Assert-True ((Get-Distance $s.marker $s.body) -lt 2) 'the marker is on Bob'
        # The marker sits a little above his feet: 49 m
        Assert-True ($s.Label -match '^Bob . (4|5)\d m deep$') "the label carries his depth ($($s.Label))"
    }

    Step 'Bob goes to 400 m: the marker freezes and the label counts up' {
        Move-Player $B $deepSpot
        $reason = Get-Reason $B
        Assert-True ($reason -like 'interfered: depth 4*') "Bob's game: in interference by depth ($reason)"
        Assert-True (Wait-Rule $A $bobId 'last contact') "Alice's marker for Bob drops to the last contact"
        $first = Read-Signal $A $bobId
        Assert-Equal 'True' $first.LinkCut 'the link is cut'
        $match = [regex]::Match($first.Label, '^Bob . last contact (\d+) s$')
        Assert-True $match.Success "the label says when contact was lost ($($first.Label))"
        Assert-Equal 'false' $first.nameTag 'no name tag'

        Write-Host '  Bob moves on 40 m at the same depth'
        Move-Player $B $deepSpot2
        Start-Sleep -Seconds 2
        $later = Read-Signal $A $bobId
        Assert-True ((Get-Distance $later.marker $first.marker) -lt 0.5) "the marker stays where contact was lost (moved $([math]::Round((Get-Distance $later.marker $first.marker), 2)) m)"
        Assert-True ((Get-Distance $later.body $first.body) -gt 30) "while Bob's body moved on in Alice's game ($([math]::Round((Get-Distance $later.body $first.body), 1)) m)"
        $laterMatch = [regex]::Match($later.Label, '^Bob . last contact (\d+) s$')
        Assert-True ($laterMatch.Success -and $match.Success -and [int]$laterMatch.Groups[1].Value -gt [int]$match.Groups[1].Value) "the label counts up ($($first.Label) -> $($later.Label))"

        # The flicker: gone for half a second every 3 s
        $hidden = 0
        for ($i = 0; $i -lt 16; $i++) {
            if ([double](Read-Marker $A $bobId 'MinDist' 'minDist') -gt 1e30) { $hidden++ }
            Start-Sleep -Milliseconds 250
        }
        Assert-True ($hidden -ge 1 -and $hidden -le 8) "the frozen marker flickers ($hidden of 16 reads dark)"
    }

    Step 'A beacon next to Bob: contact returns' {
        $before = @((Invoke-BridgeJson $B 'find Beacon 50') | ForEach-Object { $_.id })
        Invoke-Bridge $B 'console spawn beacon 1 3' | Out-Null
        $beacon = Wait-Until { (Invoke-BridgeJson $B 'find Beacon 50') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 10
        if (-not $beacon) { throw 'the beacon did not spawn' }
        $script:beaconId = $beacon.id
        Start-Sleep -Seconds 1
        $reason = Get-Reason $B
        Assert-True ($reason -like 'relayed by beacon*') "Bob's game: relayed ($reason)"
        Assert-True (Wait-Rule $A $bobId 'faint') "Alice's marker for Bob is back to faint"
        $s = Read-Signal $A $bobId
        Assert-True ((Get-Distance $s.marker $s.body) -lt 2) 'the marker follows Bob again'
        Assert-True ($s.Label -match '^Bob . (39|40)\d m deep$') "the label shows his depth again ($($s.Label))"
    }

    Step 'Bob picks the beacon up: contact is lost again' {
        Invoke-Bridge $B "use $beaconId" | Out-Null
        $picked = Wait-Until { Get-InventoryItem $B -Id $beaconId } -TimeoutSeconds 10
        Assert-True ([bool]$picked) 'the beacon is in his inventory'
        Start-Sleep -Seconds 1
        $reason = Get-Reason $B
        Assert-True ($reason -like 'interfered: depth*') "Bob's game: a beacon in the pocket relays nothing ($reason)"
        Assert-True (Wait-Rule $A $bobId 'last contact') 'Alice sees the last contact again'
    }

    Step 'Bob comes back to 50 m: contact returns' {
        Move-Player $B $shallowSpot
        Assert-True ((Get-Reason $B) -like 'clear*') "Bob's game: clear"
        Assert-True (Wait-Rule $A $bobId 'faint') 'faint again'
        $s = Read-Signal $A $bobId
        Assert-True ((Get-Distance $s.marker $s.body) -lt 2) 'the marker is on Bob'
    }

    Step 'Bob in the Jellyshroom Cave at 258 m: the biome cuts the line' {
        Move-Player $B $jellyshroomSpot
        $reason = Get-Reason $B
        Assert-True ($reason -like 'interfered: biome *') "Bob's game: in interference by biome, above 300 m ($reason)"
        Assert-True ($reason -match '(?i)jellyshroom') "the biome is the Jellyshroom Cave ($reason)"
        Assert-True (Wait-Rule $A $bobId 'last contact') 'Alice sees the last contact'
        Move-Player $B $shallowSpot
        Assert-True (Wait-Rule $A $bobId 'faint') 'back at 50 m: faint'
    }

    Step 'Alice goes deep: the line is cut for both' {
        Move-Player $A $deepSpot
        Assert-True ((Get-Reason $A) -like 'interfered: depth*') "Alice's game: in interference"
        Assert-True (Wait-Rule $A $bobId 'last contact') 'Alice loses Bob (her own interference)'
        Assert-True (Wait-Rule $B $aliceId 'last contact') 'Bob loses Alice (her flag)'
        try {
            Invoke-Bridge $A 'set NitroxPrefs.PartnerSignal.Value 2' | Out-Null
            Assert-True (Wait-Rule $A $bobId 'always') "Alice's setting Always ignores interference"
            $s = Read-Signal $A $bobId
            Assert-True ((Get-Distance $s.marker $s.body) -lt 2) 'with Always the marker follows Bob'
        }
        finally {
            Invoke-Bridge $A 'call PlayerPrefs.DeleteKey Nitrox.partnerSignal' | Out-Null
            Invoke-Bridge $A 'call PlayerPrefs.Save' | Out-Null
        }
        Move-Player $A $aliceSpot
        Assert-True (Wait-Rule $A $bobId 'faint') "both shallow: Alice's marker for Bob is faint"
        Assert-True (Wait-Rule $B $aliceId 'faint') "and Bob's for Alice"
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}

Complete-Scenario -Name 'D2-DeepCutsLine' -Keep:$Keep
