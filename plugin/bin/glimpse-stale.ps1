# Exits 1 when the clone's CLI needs a rebuild: its DLL or the wrappers' build stamp is missing, or a source file
# changed after the last build. glimpse.cmd asks this script because cmd.exe cannot compare file times reliably.
param([Parameter(Mandatory)][string]$Repo)

$capture = Join-Path $Repo 'tools/Glimpse.Capture'
$stamp = Join-Path $capture 'obj/glimpse-wrapper.stamp'
$dll = Join-Path $capture 'bin/Debug/net10.0/Glimpse.Capture.dll'
if (-not (Test-Path $stamp) -or -not (Test-Path $dll)) { exit 1 }

$built = (Get-Item $stamp).LastWriteTimeUtc
$changed = @(
    Get-ChildItem $capture, (Join-Path $Repo 'src') -Recurse -File -Include *.cs, *.csproj, *.props |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    Get-ChildItem $Repo -File -Filter 'Directory.*.props'
) | Where-Object { $_.LastWriteTimeUtc -gt $built } | Select-Object -First 1

if ($changed) { exit 1 }
exit 0
