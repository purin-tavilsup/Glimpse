#!/usr/bin/env pwsh
# Install the Glimpse plugin (CLI + skills) for use in any repo on this machine.
# Junctions ~/.claude/skills/glimpse -> <repo>/plugin (repo stays canonical, edits live).
# A junction is used rather than a symlink because it needs neither admin rights nor
# Developer Mode.
#   Usage:  ./scripts/install.ps1              install/refresh
#           ./scripts/install.ps1 -Uninstall
[CmdletBinding()]
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'

$repo   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$plugin = Join-Path $repo 'plugin'
$link   = Join-Path $HOME '.claude/skills/glimpse'

if ($Uninstall) {
    if (Test-Path $link) {
        (Get-Item $link).Delete()
        Write-Output "Removed $link"
    } else {
        Write-Output "No link at $link"
    }
    exit 0
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'dotnet not found — install the .NET 10 SDK first.'
}

Write-Output 'Building Glimpse.Capture...'
dotnet build (Join-Path $repo 'tools/Glimpse.Capture/Glimpse.Capture.csproj') -v quiet
if ($LASTEXITCODE -ne 0) { Write-Error 'Build failed.' }

# The wrapper cannot resolve an ancestor junction from batch, so hand it the repo path.
Set-Content -Path (Join-Path $plugin 'bin/glimpse.repo') -Value $repo -NoNewline -Encoding ascii

$skills = Join-Path $HOME '.claude/skills'
New-Item -ItemType Directory -Force -Path $skills | Out-Null

if (Test-Path $link) {
    $existing = Get-Item $link
    if (-not $existing.LinkType) {
        Write-Error "REFUSE: a real (non-link) entry exists at $link — remove it manually, then re-run."
    }
    $existing.Delete()
}

New-Item -ItemType Junction -Path $link -Target $plugin | Out-Null
Write-Output "Linked $link -> $plugin"
Write-Output ''
Write-Output "Done. Restart Claude Code (or run /reload-plugins) to load the 'glimpse' plugin."
Write-Output 'Verify (in a NEW session):  claude plugin list   and   where.exe glimpse'
