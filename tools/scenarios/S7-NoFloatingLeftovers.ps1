<#
.SYNOPSIS
    FIX_SPEC S7 (#2691, #2047): items a player moves from their inventory into a locker don't float around afterwards
    (not where that player stood, not after the other player leaves and returns), and opening the locker again and
    again doesn't duplicate its content (also not after a restart).

.DESCRIPTION
    Starts the fixture world tools\fixtures\s7-machine-base unless -NoLaunch, and uses the escape pod's locker.
      1. Bob spawns 8 items and moves every one that fits from his inventory into the locker (ItemsContainer.AddItem,
         the add the inventory UI makes).
      2. Alice quits to the main menu (that ends her game), then everything restarts and Alice goes where Bob stood.
      3. The moved items are in the locker on the server and in both games, none of them is a world item anywhere, the
         desync check finds nothing for them, and the server logged no stale cell entry.
      4. #2047: both players open and close the locker 3 times; the content stays the same. Restart: still the same.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 's7-leftovers')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
. (Join-Path $PSScriptRoot 'S7Helpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Assert-LockerContent {
    param([string]$Context)
    foreach ($name in 'server', $A, $B) {
        $content = Get-ContainerContent $name $locker
        Assert-Equal $script:expectedText $content.Text "$Context, $name's copy of the locker"
    }
}

function Assert-NoWorldCopies {
    param([string]$Context, [string[]]$Players = @($A, $B))
    $server = Get-BridgeEntities 'server'
    foreach ($id in $moved) {
        Assert-Equal $locker $server.Entities[$id].ParentId "$Context, server: $id is in the locker ($($server.Entities[$id].Kind))"
    }
    foreach ($player in $Players) {
        $game = Get-BridgeEntities $player
        $outside = @($moved | Where-Object { $game.Entities.ContainsKey($_) -and $game.Entities[$_].ParentKind -ne 'container' })
        Assert-Equal 0 $outside.Count "$Context, $player has none of the moved items outside a container ($($outside -join ', '))"
    }
}

try {
    $logMark = Get-ServerLogMark
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -Fixture $S7.Fixture -NoLaunch:$NoLaunch

    Step 'Both players at the escape pod' {
        $pod = (Get-BridgeEntities 'server').Entities.Values | Where-Object { $_.Kind -eq 'EscapePodEntity' } | Select-Object -First 1
        $script:podPosition = $pod.Position
        Write-Host "  escape pod $($pod.Id) at $(Format-Vector $pod.Position)"
        foreach ($player in $B, $A) {
            Invoke-Bridge $player ("teleport {0} {1} {2}" -f $pod.Position[0], ($pod.Position[1] + 1), $pod.Position[2]) | Out-Null
        }
        $script:locker = (Wait-Until { Invoke-BridgeJson $B 'find StorageContainer 5' | Where-Object { $_.id -match '^[0-9a-f-]{36}$' } | Select-Object -First 1 } -TimeoutSeconds 30).id
        Write-Host "  locker $locker"
        Assert-True ([bool]$locker) 'the escape pod locker is loaded'
        $script:before = Get-ContainerContent 'server' $locker
        Write-Host "  locker before: $($before.Text)"
    }

    Step 'Bob moves his items into the locker' {
        $script:bobPosition = (Invoke-BridgeJson $B 'state').position
        $known = @((Invoke-BridgeJson $B 'state').inventory | ForEach-Object { $_.id })
        Invoke-Bridge $B 'console item titanium 4' | Out-Null
        Invoke-Bridge $B 'console item copper 4' | Out-Null
        $new = Wait-Until { $items = @((Invoke-BridgeJson $B 'state').inventory | Where-Object { $known -notcontains $_.id }); if ($items.Count -ge 8) { , $items } } -TimeoutSeconds 15
        Assert-True ([bool]$new) "$B got 8 new items"
        $script:moved = @()
        foreach ($item in $new) {
            $result = Invoke-Bridge $B "call $locker StorageContainer.container.AddItem $($item.id)"
            if ($result -ne 'null') { $script:moved += $item.id }
            Start-Sleep -Milliseconds 300
        }
        Write-Host "  moved $($moved.Count) of 8 into the locker"
        Assert-True ($moved.Count -ge 1) 'at least one item fit into the locker'
        $arrived = Wait-Until { $s = Get-BridgeEntities 'server'; @($moved | Where-Object { $s.Entities[$_].ParentId -eq $locker }).Count -eq $moved.Count } -TimeoutSeconds 15
        Assert-True ([bool]$arrived) 'the server has every moved item in the locker'
        $script:expectedText = (Get-ContainerContent 'server' $locker).Text
        Assert-LockerContent 'after moving'
        Assert-NoWorldCopies 'after moving'
    }

    # Quitting to the main menu ends the game process, so Alice comes back through a restart of everything
    Step 'Alice leaves, everything restarts, Alice goes where Bob stood' {
        Disconnect-S7Player $A
        Start-Sleep -Seconds 5
        Assert-NoWorldCopies 'while Alice is away' -Players $B
        Restart-S7World $SaveName $A, $B
        Invoke-Bridge $A ("teleport {0} {1} {2}" -f $bobPosition[0], $bobPosition[1], $bobPosition[2]) | Out-Null
        Start-Sleep -Seconds 8
        Assert-LockerContent 'after Alice returned'
        Assert-NoWorldCopies 'after Alice returned'
    }
    Assert-NoDiff -Ids (@($locker) + $moved) 'no desync reported for the locker or the moved items'

    Step '#2047: both players open and close the locker 3 times' {
        foreach ($round in 1..3) {
            foreach ($player in $A, $B) {
                Invoke-Bridge $player "use $locker StorageContainer" | Out-Null
                Start-Sleep -Seconds 1
                Invoke-Bridge $player 'call player PDA.Close' -AllowError | Out-Null
                Start-Sleep -Milliseconds 500
            }
        }
        Start-Sleep -Seconds 2
        Assert-LockerContent 'after opening it 6 times'
    }

    Step 'Restart: the locker content is unchanged' {
        Restart-S7World $SaveName $A, $B
        foreach ($player in $A, $B) {
            Invoke-Bridge $player ("teleport {0} {1} {2}" -f $podPosition[0], ($podPosition[1] + 1), $podPosition[2]) | Out-Null
        }
        Wait-Until { Invoke-BridgeJson $A "entity $locker" } -TimeoutSeconds 30 | Out-Null
        Start-Sleep -Seconds 5
        Assert-LockerContent 'after restart'
        foreach ($player in $A, $B) {
            Invoke-Bridge $player "use $locker StorageContainer" | Out-Null
            Start-Sleep -Seconds 1
            Invoke-Bridge $player 'call player PDA.Close' -AllowError | Out-Null
        }
        Start-Sleep -Seconds 2
        Assert-LockerContent 'after restart and opening it again'
        Assert-NoWorldCopies 'after restart'
    }

    $stale = @(Get-ServerLogLines -Mark $logMark -Pattern 'Removing stale entry')
    $stale | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
    Assert-Equal 0 $stale.Count 'the server logged no stale cell entry (S7 ghost entries)'
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'S7 NoFloatingLeftovers' -Keep:$Keep
