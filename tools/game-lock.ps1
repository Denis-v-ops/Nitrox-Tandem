<#
.SYNOPSIS
    Takes turns on one of this PC's game rigs. Only one dual-client run (server on the rig's port + games) can exist
    per rig at a time, and every worktree's launch copies its own NitroxPatcher.dll into the rig's game folder (rig 0:
    the game install itself). Rigs 1..9 (tools\rig.ps1) have their own lock, port and game folder.

.DESCRIPTION
    -Acquire waits its turn (first come, first served) until the lock is free, then takes it. -Release frees it (only for its owner unless -Force).
    -Status shows the holder. A lock older than -StaleMinutes is treated as abandoned and taken over, so a holder that
    keeps the rig longer (toolsun-suite.ps1) calls -Refresh now and then to renew it.
    Hold it from before `dual-client.ps1` / a scenario script until after `dual-client.ps1 -Stop`.

.EXAMPLE
    tools\game-lock.ps1 -Acquire -Owner s6-swimming
    tools\scenarios\S6-InteriorStateHolds.ps1
    tools\game-lock.ps1 -Release -Owner s6-swimming
.EXAMPLE
    tools\game-lock.ps1 -Acquire -Owner s3-windowless -Rig 2
#>
param(
    [switch]$Acquire,
    [switch]$Release,
    [switch]$Status,
    [switch]$Refresh,
    [string]$Owner = $env:USERNAME,
    [int]$StaleMinutes = 45,
    [int]$TimeoutMinutes = 180,
    [switch]$Force,
    [int]$Rig = [int]$env:NITROX_RIG
)
$ErrorActionPreference = 'Stop'
$lockFile = Join-Path $env:LOCALAPPDATA "Nitrox\game-rig$(if ($Rig -gt 0) { $Rig }).lock"
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

if ($Refresh) {
    $lock = Read-Lock
    if (-not $lock -or $lock.owner -ne $Owner) { throw "Lock isn't held by '$Owner'" }
    Set-Content $lockFile -NoNewline -Value (@{ owner = $Owner; since = (Get-Date).ToString('o') } | ConvertTo-Json -Compress)
    "Refreshed ($Owner)"
    exit 0
}

if ($Acquire) {
    # First come, first served: each waiter keeps a ticket in the queue folder and only the oldest live ticket may take
    # the lock. Without it, a waiter that retries at the wrong moment can lose for hours (the suite did, 2026-11-02).
    # Tickets are touched on every poll; one untouched for 90 s belongs to a waiter that's gone and is ignored.
    $queueDir = "$lockFile.queue"
    New-Item -ItemType Directory -Force $queueDir | Out-Null
    $ticket = Join-Path $queueDir ('{0:D20}-{1}-{2}' -f (Get-Date).ToUniversalTime().Ticks, $PID, ($Owner -replace '[^\w.-]', '_'))
    New-Item -ItemType File -Force $ticket | Out-Null
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    try {
        while ($true) {
            (Get-Item $ticket).LastWriteTime = Get-Date
            $live = @(Get-ChildItem $queueDir -File | Where-Object { ((Get-Date) - $_.LastWriteTime).TotalSeconds -lt 90 } | Sort-Object Name)
            $first = $live.Count -eq 0 -or $live[0].FullName -eq $ticket
            $lock = Read-Lock
            if ($lock -and ((Get-Date) - [datetime]$lock.since).TotalMinutes -gt $StaleMinutes) {
                Write-Host "Taking over stale lock from '$($lock.owner)' (since $($lock.since))"
                Remove-Item $lockFile -Force -ErrorAction SilentlyContinue
            }
            if ($first) {
                try {
                    $stream = [IO.File]::Open($lockFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    $bytes = [Text.Encoding]::UTF8.GetBytes((@{ owner = $Owner; since = (Get-Date).ToString('o') } | ConvertTo-Json -Compress))
                    $stream.Write($bytes, 0, $bytes.Length)
                    $stream.Dispose()
                    Remove-Item $ticket -Force -ErrorAction SilentlyContinue
                    "Acquired ($Owner)"
                    exit 0
                }
                catch [System.IO.IOException] { }
            }
            if ((Get-Date) -gt $deadline) { throw "Timed out waiting for the game rig (held by '$((Read-Lock).owner)')" }
            # The head of the queue retries quickly, so callers of an older version of this script (20 s polls, no
            # ticket) don't keep beating it to a freed lock.
            Start-Sleep -Seconds $(if ($first) { 3 } else { 10 })
        }
    }
    finally {
        Remove-Item $ticket -Force -ErrorAction SilentlyContinue
    }
}

throw 'Pass -Acquire, -Release, -Refresh or -Status'
