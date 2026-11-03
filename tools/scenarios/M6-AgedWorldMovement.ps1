<#
.SYNOPSIS
    MULTINAUGHTICA_SPEC M6: S13's Cyclops movement measurement in a world with 40 h of play. Windowed (frame timing and
    smoothness, AGENTS.md section 4): opens two game windows.

.DESCRIPTION
    Movement snapshots are stamped with the world's active play time (TimeManager.RealTimeElapsed), which is saved with
    the world. Runs S13-CyclopsMovement.ps1 with -AgeWorldSeconds: a fresh world is saved, its active play time (and
    the Aurora's explosion time on that clock) moved 40 h on in its WorldData.json, the world restarted; both games'
    movement clock must read at least that. Then the same drive, helm hand-over and passenger checks as S13, with S13's
    thresholds. The docked-Seamoth and rejoin parts are left out (no snapshot times in them).

    Fails without the fix by construction: past 2^17 s (36.4 h) a float steps 15.6 ms. With float times every stamp and
    every frame's "now" was rounded to that, so at 120 fps about every second frame of the partner's view reads the same
    time and the replicated sub doesn't move: S13's stalled-frame check (at most 10 % of moving frames) fails. The young
    world of a plain S13 run (a few hundred seconds, a 0.03-0.06 ms float step) can't show it.

    Needs a Debug build. Recordings go to -OutDir.
#>
param([switch]$Keep, [string]$SaveName = 'm6-aged-world', [double]$AgeWorldSeconds = 144000,
    [string]$OutDir = (Join-Path $env:TEMP 'm6-recordings'))

& (Join-Path $PSScriptRoot 'S13-CyclopsMovement.ps1') -SaveName $SaveName -AgeWorldSeconds $AgeWorldSeconds -OutDir $OutDir -SkipDocking -SkipRejoin -Keep:$Keep
exit $LASTEXITCODE
