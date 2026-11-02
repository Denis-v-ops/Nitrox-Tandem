<#
.SYNOPSIS
    MULTINAUGHTICA_SPEC M7: while the server saves, both games show the game's own "Saving game" indicator (or, where
    no indicator runs in the play scene, the game's notice with the same text, once).

.DESCRIPTION
    Starts a new world (save m7-saving-indicator, replaced every run) with Alice and Bob, then:
    1. Before the save, each game's SavingIndicator (if one runs in the play scene: bridge "find:SavingIndicator") has
       CanvasGroup alpha 0.
    2. "bridge.ps1 server save" (the save command; it only queues the save). Both games are sampled every ~0.1 s for
       up to 20 s. Indicator path: alpha becomes 1, stays 1 for at least about 2 s from the start (a save of a new
       world takes well under a second, so this shows the minimum time; the check allows 0.8 s for sampling delays),
       then goes back to 0, and the game showed no notice for this save. Notice path (no SavingIndicator found): the
       game's log says it showed the notice for this save exactly once.
    The scenario prints which path each game used (the final rig pass answers the spec's open question with it).

    Without the fix it fails by construction: with SavingIndicator_Update_Patch removed, alpha never leaves 0
    (SavingIndicator.Update sets it from SaveLoadManager.isSaving, which a Nitrox game never sets), and without the
    ServerSaving packet nothing happens on either path. Negative control: move
    NitroxPatcher\Patches\Dynamic\SavingIndicator_Update_Patch.cs out, build, run (indicator path: FAIL), restore.

    Windowless is fine (TESTING.md): no camera renders, but SavingIndicator.Update still runs on an active GameObject
    and the alpha it sets is a plain float, so reading it shows what a windowed game would draw. Whether a canvas that
    switches off when not visible hides it is not checked here. Needs a Debug build. -NoLaunch uses running games.
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'm7-saving-indicator')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$Players = @('Alice', 'Bob')

<#
    The CanvasGroup alpha of the game's SavingIndicator, or $null when no SavingIndicator runs (is active) in the scene.
#>
function Get-IndicatorAlpha {
    param([string]$Player)
    $reply = Invoke-Bridge $Player 'get find:SavingIndicator SavingIndicator.canvasGroup.alpha' -AllowError
    if ($reply.StartsWith('ERR ')) { return $null }
    [double]($reply | ConvertFrom-Json)
}

function Get-GameLogSince {
    param([hashtable]$Marks, [string]$Player)
    $texts = Get-LogTextSince $Marks @($Player) $SaveName
    ($texts.Keys | Where-Object { (Split-Path -Leaf $_).StartsWith("game[$Player]-") } | ForEach-Object { $texts[$_] }) -join "`n"
}

try {
    Start-ScenarioGames -Players $Players -SaveName $SaveName -NoLaunch:$NoLaunch
    $paths = @{}

    Step 'No save runs: the indicator is hidden' {
        foreach ($player in $Players) {
            $alpha = Get-IndicatorAlpha $player
            $paths[$player] = if ($null -eq $alpha) { 'notice' } else { 'indicator' }
            Write-Host "  $player`: $(if ($paths[$player] -eq 'indicator') { "the game's SavingIndicator runs in the play scene" } else { 'no SavingIndicator runs in the play scene, the fallback notice is expected' })"
            if ($null -ne $alpha) {
                Assert-Equal 0 $alpha "$player's SavingIndicator is hidden before the save"
            }
        }
    }

    Step 'The server saves: both games show it' {
        $marks = Get-LogMarks $Players $SaveName
        $samples = @{}
        foreach ($player in $Players) { $samples[$player] = [pscustomobject]@{ FirstShown = $null; LastShown = $null; HiddenAfter = $false } }
        $clock = [Diagnostics.Stopwatch]::StartNew()
        Invoke-Bridge 'server' 'save' | Out-Null
        while ($clock.Elapsed.TotalSeconds -lt 20) {
            $pending = $false
            foreach ($player in $Players | Where-Object { $paths[$_] -eq 'indicator' }) {
                $s = $samples[$player]
                if ($s.HiddenAfter) { continue }
                $pending = $true
                $alpha = Get-IndicatorAlpha $player
                $at = $clock.Elapsed.TotalSeconds
                if ($alpha -eq 1) {
                    if ($null -eq $s.FirstShown) { $s.FirstShown = $at }
                    $s.LastShown = $at
                }
                elseif ($null -ne $s.FirstShown) {
                    $s.HiddenAfter = $true
                }
            }
            if (-not $pending) { break }
            Start-Sleep -Milliseconds 100
        }

        foreach ($player in $Players) {
            if ($paths[$player] -eq 'indicator') {
                $s = $samples[$player]
                Assert-True ($null -ne $s.FirstShown) "$player's SavingIndicator showed during the save (alpha 1)"
                if ($null -ne $s.FirstShown) {
                    $shown = $s.LastShown - $s.FirstShown
                    Write-Host ("  {0}: shown from {1:0.00} s to {2:0.00} s after the save command" -f $player, $s.FirstShown, $s.LastShown)
                    Assert-True ($shown -ge 1.2) ("{0}'s indicator stayed for the minimum time (seen for {1:0.00} s, at least 1.2 s expected)" -f $player, $shown)
                }
                Assert-True $s.HiddenAfter "$player's SavingIndicator is hidden again after the save (alpha 0)"
            }
        }

        foreach ($player in $Players) {
            # The game logs the end of each server save (Debug level): wait for it so the log holds the whole save
            $ended = Wait-Until { (Get-GameLogSince $marks $player) -match 'Server save \d+ FINISHED' } -TimeoutSeconds 10 -IntervalSeconds 0.5
            Assert-True ([bool]$ended) "$player's game was told that the save finished"
            $log = Get-GameLogSince $marks $player
            $notices = [regex]::Matches($log, 'Showed the notice for server save \d+').Count
            if ($log.Contains("show through the game's SavingIndicator")) { Write-Host "  $player's log: server saves show through the game's SavingIndicator" }
            if ($log.Contains('server saves show as the game''s notice')) { Write-Host "  $player's log: no SavingIndicator runs, server saves show as the game's notice" }
            if ($paths[$player] -eq 'indicator') {
                Assert-Equal 0 $notices "$player's game showed no notice on top of the indicator"
            }
            else {
                Assert-Equal 1 $notices "$player's game showed the 'Saving game' notice once for the save"
            }
        }
    }

    Write-Host ''
    foreach ($player in $Players) {
        Write-Host "Path used by $player`: $($paths[$player])" -ForegroundColor Cyan
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'M7 SavingIndicator' -Keep:$Keep
