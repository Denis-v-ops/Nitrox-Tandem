<#
.SYNOPSIS
    Takes turns on this PC's game rig. Only one dual-client run (server on port 11000 + games) can exist at a time,
    and every worktree's launch copies its own NitroxPatcher.dll into the shared game install.

.DESCRIPTION
    -Acquire waits until the lock is free, then takes it. -Release frees it (only for its owner unless -Force).
    -Status shows the holder. A lock older than -StaleMinutes is treated as abandoned and taken over.
    Hold it from before `dual-client.ps1` / a scenario script until after `dual-client.ps1 -Stop`.

.EXAMPLE
    tools\game-lock.ps1 -Acquire -Owner s6-swimming
    tools\scenarios\S6-InteriorStateHolds.ps1
    tools\game-lock.ps1 -Release -Owner s6-swimming
#>
param(
    [switch]$Acquire,
    [switch]$Release,
    [switch]$Status,
    [string]$Owner = $env:USERNAME,
    [int]$StaleMinutes = 45,
    [int]$TimeoutMinutes = 180,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$lockFile = Join-Path $env:LOCALAPPDATA 'Nitrox\game-rig.lock'
New-Item -ItemType Directory -Force (Split-Path $lockFile) | Out-Null

function Read-Lock {
    if (-not (Test-Path $lockFile)) { return $null }
    try { return Get-Content $lockFile -Raw | ConvertFrom-Json } catch { return $null }
}

if ($Status) {
    $lock = Read-Lock
    if ($lock) { "Held by '$($lock.owner)' since $($lock.since)" } else { 'Free' }
    exit 0
}

if ($Release) {
    $lock = Read-Lock
    if (-not $lock) { 'Already free'; exit 0 }
    if ($lock.owner -ne $Owner -and -not $Force) { throw "Lock is held by '$($lock.owner)', not '$Owner'. Use -Force only if that owner is gone." }
    Remove-Item $lockFile -Force
    "Released ($Owner)"
    exit 0
}

if ($Acquire) {
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ($true) {
        $lock = Read-Lock
        if ($lock -and ((Get-Date) - [datetime]$lock.since).TotalMinutes -gt $StaleMinutes) {
            Write-Host "Taking over stale lock from '$($lock.owner)' (since $($lock.since))"
            Remove-Item $lockFile -Force -ErrorAction SilentlyContinue
        }
        try {
            $stream = [IO.File]::Open($lockFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $bytes = [Text.Encoding]::UTF8.GetBytes((@{ owner = $Owner; since = (Get-Date).ToString('o') } | ConvertTo-Json -Compress))
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Dispose()
            "Acquired ($Owner)"
            exit 0
        }
        catch [System.IO.IOException] {
            if ((Get-Date) -gt $deadline) { throw "Timed out waiting for the game rig (held by '$((Read-Lock).owner)')" }
            Start-Sleep -Seconds 20
        }
    }
}

throw 'Pass -Acquire, -Release or -Status'
