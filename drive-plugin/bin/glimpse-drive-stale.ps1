# Exits 1 when the clone's driver needs a rebuild: its DLL or the wrapper's build stamp is missing, or a source file
# changed after the last build (src/Glimpse.Core counts: the driver reads its window list). glimpse-drive.cmd asks this script because cmd.exe cannot compare file times reliably.
param([Parameter(Mandatory)][string]$Repo)

$drive = Join-Path $Repo 'tools/Glimpse.Drive'
$stamp = Join-Path $drive 'obj/glimpse-drive-wrapper.stamp'
$dll = Join-Path $drive 'bin/Debug/net10.0/Glimpse.Drive.dll'
if (-not (Test-Path $stamp) -or -not (Test-Path $dll)) { exit 1 }

$built = (Get-Item $stamp).LastWriteTimeUtc
$changed = @(
    Get-ChildItem $drive, (Join-Path $Repo 'src') -Recurse -File -Include *.cs, *.csproj, *.props |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    Get-ChildItem $Repo -File -Filter 'Directory.*.props'
) | Where-Object { $_.LastWriteTimeUtc -gt $built } | Select-Object -First 1

if ($changed) { exit 1 }
exit 0
