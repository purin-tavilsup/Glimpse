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
    # Only ever remove a link. A real entry here is someone's own directory: the install
    # path refuses to touch it, so uninstall must not delete it either.
    $existing = Get-Item $link -ErrorAction SilentlyContinue
    if ($existing -and $existing.LinkType) {
        $existing.Delete()
        Write-Output "Removed $link"
    } elseif ($existing) {
        Write-Error "REFUSE: a real (non-link) entry exists at $link — remove it manually."
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
# glimpse.cmd reads this with `set /p`, which decodes in the CONSOLE code page, so no single
# encoding is universally right — writing ASCII turns 'Müller' into 'M?ller' and the wrapper
# then dies on a path that never existed. Prove cmd.exe reads back exactly what we wrote, and
# refuse loudly rather than report a successful install that cannot work.
$sidecar = Join-Path $plugin 'bin/glimpse.repo'
$readable = $false
foreach ($encoding in 'oem', 'utf8', 'ascii') {
    Set-Content -Path $sidecar -Value $repo -NoNewline -Encoding $encoding
    if ((cmd.exe /v:on /c "set /p R=<""$sidecar"" & echo !R!") -eq $repo) {
        $readable = $true
        break
    }
}
if (-not $readable) {
    Write-Error "REFUSE: the batch wrapper cannot read back '$repo' — move the clone to a path your console code page can represent (ASCII is always safe)."
}

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
