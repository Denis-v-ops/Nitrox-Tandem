<#
.SYNOPSIS
    Playtest finding: PDA and radio subtitles ran ahead of the voice. Each line must start when the language file's
    <delay=ms> mark says, and last as long as its <duration=ms> mark.

.DESCRIPTION
    The game reads those marks once when it loads the language file and removes them from the text. Nitrox's
    Language_LoadLanguageFile_Patch parsed everything a second time, from the text without marks, so every line fell back
    to the 15 characters per second guess (the last RadioSunbeam2 line started at 17.8 s instead of 28.1 s).

    Alice shows the subtitles of voice lines that are timed with marks (Subtitles.Add, what SoundQueue.Play calls with
    the voice), and the queued lines' delay and duration are compared with the marks in the game's own language file.
    Text only, no sound, so it runs windowless. Starts a fresh world and two games, unless -NoLaunch. Needs a Debug build.
#>
param([switch]$NoLaunch, [switch]$Keep)

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$keys = 'RadioSunbeam2', 'RadioRadiationSuit'

function Get-MarkedLines {
    param([hashtable]$Strings, [string]$Key)
    foreach ($line in ($Strings[$Key] -split "`n")) {
        [pscustomobject]@{
            Delay = if ($line -match '<delay=(\d+)>') { [int]$Matches[1] / 1000 } else { $null }
            Duration = if ($line -match '<duration=(\d+)>') { [int]$Matches[1] / 1000 } else { $null }
        }
    }
}

try {
    Start-ScenarioGames -Players $A, 'Bob' -NoLaunch:$NoLaunch

    $language = (Invoke-Bridge $A 'get Language.main.currentLanguage').Trim('"')
    $gamePath = if ($env:SUBNAUTICA_INSTALLATION_PATH) { $env:SUBNAUTICA_INSTALLATION_PATH } else { 'F:\Spiele\SteamLibrary\steamapps\common\Subnautica' }
    $file = Join-Path $gamePath "Subnautica_Data\StreamingAssets\SNUnmanagedData\LanguageFiles\$language.json"
    $strings = Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable

    foreach ($key in $keys) {
        Step "Subtitles of $key follow the language file's timing marks" {
            $expected = @(Get-MarkedLines $strings $key)
            Assert-True (@($expected | Where-Object { $null -ne $_.Delay }).Count -gt 0) "$key has delay marks in $language.json"
            # The queue sorts its messages, so earlier lines still showing would mix with these: wait until it is empty.
            $empty = Wait-Until { if (@((Invoke-BridgeJson $A 'get Subtitles.main.queue').messages).Count -eq 0) { $true } } -TimeoutSeconds 90
            Assert-True ([bool]$empty) 'no subtitles are showing before the line is added'
            Invoke-Bridge $A "call Subtitles.main.Add $key" | Out-Null
            $messages = @((Invoke-BridgeJson $A 'get Subtitles.main.queue').messages)
            Assert-Equal $expected.Count $messages.Count "$key queued one subtitle per line"
            for ($i = 0; $i -lt $expected.Count; $i++) {
                $got = $messages[$i]
                Write-Host ("  line {0}: delay {1:0.###} (mark {2}), duration {3:0.###} (mark {4})" -f $i, $got.delay, $expected[$i].Delay, $got.duration, $expected[$i].Duration)
                if ($null -ne $expected[$i].Delay) {
                    Assert-True ([math]::Abs($got.delay - $expected[$i].Delay) -lt 0.01) "$key line $i starts at its mark, $($expected[$i].Delay) s (got $($got.delay))"
                }
                if ($null -ne $expected[$i].Duration) {
                    Assert-True ([math]::Abs($got.duration - $expected[$i].Duration) -lt 0.01) "$key line $i lasts its mark, $($expected[$i].Duration) s (got $($got.duration))"
                }
            }
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'PT1 SubtitleTiming' -Keep:$Keep
