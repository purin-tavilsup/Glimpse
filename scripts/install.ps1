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
# Claude Code puts a plugin's bin/ on its Bash tool's PATH only, so PowerShell, cmd and IDE
# terminals reach glimpse.cmd through the user PATH. The entry points through the junction so
# it survives moving the repo.
$bin    = [IO.Path]::GetFullPath((Join-Path $link 'bin'))

# The registry is edited directly because [Environment]::SetEnvironmentVariable rewrites the
# user PATH as REG_SZ with every %VAR% expanded, silently breaking entries that rely on them.
function Update-UserPath([string]$entry, [switch]$Remove) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment')
    try {
        $raw     = [string]$key.GetValue('Path', '', 'DoNotExpandEnvironmentNames')
        $entries = @($raw -split ';' | Where-Object { $_ })
        $others  = @($entries | Where-Object { $_.TrimEnd('\') -ne $entry })
        $present = $others.Count -lt $entries.Count
        $wanted  = -not $Remove
        if ($present -eq $wanted) { return $false }
        $updated = if ($Remove) { $others } else { $others + $entry }
        $key.SetValue('Path', ($updated -join ';'), 'ExpandString')
    } finally {
        $key.Dispose()
    }
    Send-EnvironmentChanged
    return $true
}

# New shells inherit Explorer's copy of the environment, which only reloads on this broadcast;
# without it the PATH change waits for the next sign-in.
function Send-EnvironmentChanged {
    if (-not ('Glimpse.Win32' -as [type])) {
        Add-Type -Namespace Glimpse -Name Win32 -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
'@
    }
    $result = [UIntPtr]::Zero
    # HWND_BROADCAST + WM_SETTINGCHANGE; SMTO_ABORTIFHUNG with a 5s cap so one hung window
    # cannot stall the install.
    [void][Glimpse.Win32]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$result)
}

if ($Uninstall) {
    if (Update-UserPath $bin -Remove) { Write-Output "Removed $bin from the user PATH" }

    # Only ever remove a link. A real entry here is someone's own directory: the install
    # path refuses to touch it, so uninstall must not delete it either.
    $existing = Get-Item $link -ErrorAction SilentlyContinue
    if ($existing -and $existing.LinkType) {
        $existing.Delete()
        Remove-Item (Join-Path $plugin 'bin/glimpse.repo') -ErrorAction SilentlyContinue
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

# The wrapper cannot resolve an ancestor junction from batch, so hand it the repo path. It is
# written as UTF-8 without a BOM and glimpse.cmd reads it under code page 65001, so the result
# does not depend on the code page of whichever shell later runs glimpse. Prove it by asking
# the wrapper itself under two different caller code pages. The answer is an exit code
# because printed text would be re-decoded on its way back here, which is the very trap
# being checked for. Refuse loudly rather than report an install that cannot work.
$sidecar = Join-Path $plugin 'bin/glimpse.repo'
[IO.File]::WriteAllText($sidecar, $repo, [Text.UTF8Encoding]::new($false))
$wrapper = Join-Path $plugin 'bin/glimpse.cmd'
# The probes share this console, so its code page is put back however the check ends.
$userCodePage = (cmd.exe /d /c chcp) -replace '\D', ''
try {
    foreach ($callerCodePage in 437, 65001) {
        cmd.exe /d /c "chcp $callerCodePage >nul & ""$wrapper"" --check-sidecar"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "REFUSE: under code page $callerCodePage the batch wrapper cannot read the repo path '$repo' back."
        }
    }
} finally {
    cmd.exe /d /c "chcp $userCodePage >nul"
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
if (Update-UserPath $bin) { Write-Output "Added $bin to the user PATH" }
Write-Output ''
Write-Output "Done. Restart Claude Code (or run /reload-plugins) to load the 'glimpse' plugin."
Write-Output 'Verify (in a NEW session):  claude plugin list   and   where.exe glimpse'
