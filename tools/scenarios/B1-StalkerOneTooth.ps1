<#
.SYNOPSIS
    COOP_FEATURES_SPEC B1 (teeth): only the game that simulates a stalker decides whether a bite costs it a tooth, so one
    bite drops at most one tooth for both players.

.DESCRIPTION
    Alice spawns a stalker next to both players (console "spawn" gives the spawner the lock; checked with the server's
    "query"). Each player gets a piece of scrap metal, a hard target (Stalker_CheckLoseTooth_Patch: 25 % tooth chance,
    times the game's 50 %). Then Stalker.CheckLoseTooth, what a bite (Stalker.OnMeleeAttack) and a shiny pickup
    (Stalker.OnShinyPickedUp) call, runs 40 times in the game that doesn't simulate the stalker: the server must get no
    tooth (with the old && check: 1 - 0.875^40 = 99.5 % that it gets one). Then 40 times in the simulating game: at least
    one tooth (same odds), and both games have every new tooth.

    Starts a fresh world and two games (windowless with $env:NITROX_WINDOWLESS = 1), unless -NoLaunch. Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep, [int]$Bites = 40)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Get-ServerTeeth {
    @((Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.TechType -eq 'StalkerTooth' } | ForEach-Object { $_.Id })
}

function Get-LockOwner {
    param([string]$Id)
    if ((Invoke-Bridge 'server' "query $Id" -AllowError) -match 'Owner: (\w+)') { $Matches[1] } else { $null }
}

function Test-Loaded {
    param([string]$Player, [string]$Id)
    -not (Invoke-Bridge $Player "entity $Id" -AllowError).StartsWith('ERR ')
}

function Invoke-Bites {
    param([string]$Player, [string]$StalkerId, [string]$TargetId)
    for ($i = 0; $i -lt $Bites; $i++) {
        Invoke-Bridge $Player "call $StalkerId Stalker.CheckLoseTooth $TargetId" | Out-Null
    }
}

try {
    Start-ScenarioGames -Players $A, $B -NoLaunch:$NoLaunch

    Step 'Both players in shallow water next to each other' {
        Invoke-Bridge $A 'teleport -60 -6 380' | Out-Null
        Invoke-Bridge $B 'teleport -58 -6 382' | Out-Null
        Start-Sleep -Seconds 3
    }

    Step 'Alice spawns a stalker, both games have it' {
        $before = @((Invoke-BridgeJson $A 'find Stalker 80') | ForEach-Object { $_.id })
        Invoke-Bridge $A 'console spawn stalker 1 4' | Out-Null
        $new = Wait-Until { (Invoke-BridgeJson $A 'find Stalker 80') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 15
        if (-not $new) { throw "the stalker didn't spawn" }
        $script:stalkerId = $new.id
        Assert-True ([bool](Wait-Until { Test-Loaded $B $stalkerId } -TimeoutSeconds 15)) "$B has the stalker $stalkerId"
        $script:owner = Wait-Until { Get-LockOwner $stalkerId } -TimeoutSeconds 10
        Assert-True ($owner -in $A, $B) "a player simulates the stalker (owner: $owner)"
        $script:remote = if ($owner -eq $A) { $B } else { $A }
        Write-Host "  simulator $owner, other game $remote"
    }

    Step 'Each player gets a piece of scrap metal' {
        $script:scrap = @{}
        foreach ($player in $A, $B) {
            Invoke-Bridge $player 'console item scrapmetal' | Out-Null
            $item = Wait-Until { Get-InventoryItem $player -TechType 'ScrapMetal' | Select-Object -First 1 } -TimeoutSeconds 10
            Assert-True ([bool]$item) "$player has scrap metal"
            $scrap[$player] = $item.id
        }
        $script:teethBefore = Get-ServerTeeth
        Write-Host "  server teeth before: $($teethBefore.Count)"
    }

    Step "The game that doesn't simulate the stalker checks $Bites bites: no tooth" {
        Invoke-Bites $remote $stalkerId $scrap[$remote]
        Start-Sleep -Seconds 3
        $new = @(Get-ServerTeeth | Where-Object { $teethBefore -notcontains $_ })
        Assert-Equal 0 $new.Count "$remote's bites gave the server no tooth"
        $script:teethBefore = Get-ServerTeeth
    }

    Step "The simulating game checks $Bites bites: teeth, and both games have them" {
        Invoke-Bites $owner $stalkerId $scrap[$owner]
        Wait-Until { @(Get-ServerTeeth | Where-Object { $teethBefore -notcontains $_ }).Count -gt 0 } -TimeoutSeconds 10 | Out-Null
        Start-Sleep -Seconds 1
        $script:newTeeth = @(Get-ServerTeeth | Where-Object { $teethBefore -notcontains $_ })
        Assert-True ($newTeeth.Count -gt 0) "$owner's bites gave the server $($newTeeth.Count) teeth (expected about $($Bites / 8))"
        foreach ($tooth in $newTeeth) {
            Assert-True ([bool](Wait-Until { Test-Loaded $remote $tooth } -TimeoutSeconds 10)) "$remote has the tooth $tooth"
        }
    }

    if ($newTeeth.Count -gt 0) {
        Assert-NoDiff -Ids $newTeeth 'no desync reported for the new teeth'
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'B1 StalkerOneTooth' -Keep:$Keep
