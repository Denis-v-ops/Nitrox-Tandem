<#
.SYNOPSIS
    Decompiles Subnautica's game assemblies to readable C# for code analysis.

.DESCRIPTION
    Output goes OUTSIDE the repo (default: ..\game-src\<game version>\) because it is the game's code and must never be
    committed. Re-run after a Subnautica update; the game version is read from plastic_status.ignore, like the build does.
    Requires ilspycmd: dotnet tool install --global ilspycmd

.EXAMPLE
    .\tools\decompile-game.ps1
    .\tools\decompile-game.ps1 -GameDir 'D:\Games\Subnautica' -Force
#>
param(
    [string]$GameDir = $env:SUBNAUTICA_INSTALLATION_PATH,
    [string]$OutRoot = (Join-Path $PSScriptRoot '..\..\game-src'),
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

if (-not $GameDir) { $GameDir = 'F:\Spiele\SteamLibrary\steamapps\common\Subnautica' }
$managed = Join-Path $GameDir 'Subnautica_Data\Managed'
$versionFile = Join-Path $GameDir 'Subnautica_Data\StreamingAssets\SNUnmanagedData\plastic_status.ignore'
if (-not (Test-Path $managed)) { throw "Game not found at '$GameDir'. Pass -GameDir or set SUBNAUTICA_INSTALLATION_PATH." }
if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) { throw 'ilspycmd missing: dotnet tool install --global ilspycmd' }

$version = (Get-Content $versionFile -TotalCount 1).Trim()
$outDir = Join-Path ([IO.Path]::GetFullPath($OutRoot)) $version

$assemblies = @{
    'Assembly-CSharp'           = $outDir
    'Assembly-CSharp-firstpass' = "$outDir-firstpass"
}
foreach ($name in $assemblies.Keys) {
    $target = $assemblies[$name]
    if ((Test-Path (Join-Path $target "$name.csproj")) -and -not $Force) {
        Write-Host "Skip $name (already at $target; use -Force to redo)"
        continue
    }
    New-Item -ItemType Directory -Force $target | Out-Null
    Write-Host "Decompiling $name ($version) -> $target"
    & ilspycmd -p -o $target (Join-Path $managed "$name.dll")
    if ($LASTEXITCODE -ne 0) { throw "ilspycmd failed for $name" }
}
Write-Host "Done. Search with: rg -n 'class Player\b' '$outDir'"
