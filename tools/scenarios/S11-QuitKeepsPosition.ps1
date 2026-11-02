<#
.SYNOPSIS
    FIX_SPEC S11: quitting to the main menu must not change where the server will put the player next time.

.DESCRIPTION
    Starts a new world (save s11-quit, replaced every run) with Alice and Bob. Alice swims out of the escape pod;
    Bob stays inside it. Both quit to the main menu (in Nitrox that closes the game). Afterwards the server must still
    have each player where they were before quitting:
    - Alice: within 2 m of her swim spot, no sub;
    - Bob: within 2 m of his spot in the pod, sub = the escape pod.
    The server's player record is what it saves and sends back on the next join (L2 PlayerPositionPersistenceTest
    covers save -> restart -> rejoin), so checking the record right after the quit is enough.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's11-quit')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Get-ServerPlayer {
    param([string]$Name)
    (Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $Name } | Select-Object -First 1
}

function Get-Distance {
    param([double[]]$P, [double[]]$Q)
    [math]::Sqrt([math]::Pow($P[0] - $Q[0], 2) + [math]::Pow($P[1] - $Q[1], 2) + [math]::Pow($P[2] - $Q[2], 2))
}

function Exit-ToMainMenu {
    param([string]$Player)
    Invoke-Bridge $Player 'call IngameMenu.main.QuitGame false' | Out-Null
    $left = Wait-Until { -not ((Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $Player -and $_.online }) } -TimeoutSeconds 60
    if (-not $left) { throw "$Player didn't leave the game" }
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch

    $before = @{}
    Step 'Alice swims out of the pod, Bob stays inside' {
        $state = Invoke-BridgeJson $A 'state'
        $start = [double[]]$state.position
        [double[]]$swim = @(($start[0] + 25), ($start[1] - 8), ($start[2] + 5))
        Invoke-Bridge $A ("teleport {0} {1} {2}" -f $swim[0], $swim[1], $swim[2]) | Out-Null
        $synced = Wait-Until { $p = Get-ServerPlayer $A; $p -and (Get-Distance ([double[]]$p.position) $swim) -lt 2 -and -not $p.subRootId } -TimeoutSeconds 20
        Assert-True ([bool]$synced) "the server has Alice at her swim spot $(Format-Vector $swim) with no sub"
        $before[$A] = Get-ServerPlayer $A

        $bob = Get-ServerPlayer $B
        Assert-True ([bool]$bob.subRootId) "the server has Bob inside a sub (the pod): $($bob.subRootId)"
        $before[$B] = $bob
    }

    foreach ($player in $A, $B) {
        Step "$player quits to the main menu; the server keeps the position" {
            Exit-ToMainMenu $player
            Start-Sleep -Seconds 2
            $after = Get-ServerPlayer $player
            $moved = Get-Distance ([double[]]$before[$player].position) ([double[]]$after.position)
            Assert-True ($moved -lt 2) "$player's stored position didn't move on quit ($(Format-Vector ([double[]]$before[$player].position)) -> $(Format-Vector ([double[]]$after.position)), $([math]::Round($moved, 1)) m)"
            Assert-Equal "$($before[$player].subRootId)" "$($after.subRootId)" "$player's stored sub didn't change on quit"
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S11 QuitKeepsPosition' -Keep:$Keep
