<#
.SYNOPSIS
    COOP_FEATURES_SPEC D1: Bob hears Alice's pain and breathing on her body when she is near, and nothing from 100 m.

.DESCRIPTION
    Alice wears a tank and swims 6 m from Bob. She is hurt twice through the game's own LiveMixin.TakeDamage (the
    no-damage cheat is off for that moment), which runs PlayerDamageSounds.OnTakeDamage. Bob's sound list (bridge
    "sounds") must show a pain event on Alice's body (remotePlayer = Alice), and her breathing loop must play there.
    Then Bob moves 100 m away: another hurt plays nothing on her body in his game and the breathing loop stops.
    Before D1 (A0 probe, 2026-11-02) Bob's game played neither, with the same steps.

    Starts a fresh world and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\D1-HearPartner.ps1
#>
param([switch]$NoLaunch, [switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$aliceSpot = @(-60, -15, 380)

function Hurt-Alice {
    param([double]$Damage)
    Invoke-Bridge $A 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
    try {
        Invoke-Bridge $A "call player LiveMixin.TakeDamage $Damage 0,0,0 Normal null" | Out-Null
    }
    finally {
        Invoke-Bridge $A 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
    }
}

function Get-SoundsOnAlice {
    param([int]$Seconds, [string]$Pattern)
    @(Invoke-BridgeJson $B "sounds $Seconds" | Where-Object { $_.remotePlayer -eq $A -and $_.path -match $Pattern })
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch
    $script:aliceId = (Invoke-BridgeJson $A 'state').playerId

    Step 'Alice wears a tank and swims 6 m from Bob' {
        Invoke-Bridge $A ('teleport ' + ($aliceSpot -join ' ')) | Out-Null
        Invoke-Bridge $B ("teleport {0} {1} {2}" -f ($aliceSpot[0] + 6), $aliceSpot[1], $aliceSpot[2]) | Out-Null
        if (-not (Invoke-BridgeJson $A 'state').equipment.Tank) {
            Invoke-Bridge $A 'console item tank' | Out-Null   # equips itself into the free tank slot
            Assert-True ([bool](Wait-Until { (Invoke-BridgeJson $A 'state').equipment.Tank } -TimeoutSeconds 10)) 'Alice wears a tank'
        }
        Start-Sleep -Seconds 3
    }

    Step 'Bob hears Alice breathe' {
        $breathes = Wait-Until { (Invoke-Bridge $B "get $aliceId RemotePlayerSounds.BreathingPlaying") -eq 'true' } -TimeoutSeconds 10
        Assert-True ([bool]$breathes) "Alice's breathing loop plays on her body in Bob's game"
        Write-Host "  breathing events: $((Get-SoundsOnAlice 30 'breathing' | ForEach-Object { "$($_.source) $($_.path) dist=$($_.distance)" }) -join '; ')"
    }

    Step 'Alice is hurt; Bob hears her pain' {
        Invoke-Bridge $B 'sounds clear' | Out-Null
        Hurt-Alice 10
        Start-Sleep -Seconds 1
        Hurt-Alice 4
        $pain = Wait-Until { Get-SoundsOnAlice 10 'pain' } -TimeoutSeconds 5
        Assert-True ($pain.Count -ge 1) "a pain sound played on Alice's body in Bob's game"
        foreach ($sound in $pain) { Write-Host "  $($sound.source) $($sound.path) dist=$($sound.distance)" }
        $own = @(Invoke-BridgeJson $A 'sounds 10' | Where-Object { $_.localPlayer -and $_.path -match 'pain' } | ForEach-Object { $_.path } | Sort-Object -Unique)
        Write-Host "  Alice's own game played: $($own -join ', ')"
        Assert-True (@($pain | Where-Object { $own -contains $_.path }).Count -ge 1) 'Bob heard the same event Alice played'
    }

    Step 'Bob 100 m away: nothing' {
        Invoke-Bridge $B ("teleport {0} {1} {2}" -f ($aliceSpot[0] + 100), $aliceSpot[1], $aliceSpot[2]) | Out-Null
        Start-Sleep -Seconds 3
        Invoke-Bridge $B 'sounds clear' | Out-Null
        Hurt-Alice 10
        Start-Sleep -Seconds 2
        Assert-Equal 0 (Get-SoundsOnAlice 10 'pain').Count "no pain sound on Alice's body at 100 m"
        Assert-Equal 'false' (Invoke-Bridge $B "get $aliceId RemotePlayerSounds.BreathingPlaying") 'her breathing loop stopped at 100 m'
        Assert-Equal 'true' (Invoke-Bridge $B "get $aliceId RemotePlayerSounds.Breathes") 'though she still breathes from her tank'
    }
}
catch {
    Write-Host "Scenario aborted: $($_.Exception.Message)" -ForegroundColor Red
}

Complete-Scenario -Name 'D1-HearPartner' -Keep:$Keep
