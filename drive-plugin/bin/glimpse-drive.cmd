@echo off
rem glimpse-drive on PATH (via the plugin's bin/). Runs the in-repo built Windows driver, rebuilding it
rem when the source changed so it is always current.
rem
rem A junction is transparent to lexical ".." traversal, so from the installed location
rem (~/.claude/skills/glimpse-drive/bin) "..\..\" resolves to ~/.claude/skills, NOT the repo.
rem install.ps1 therefore writes the real repo path to the glimpse-drive.repo sidecar; the
rem lexical fallback covers running this script directly from a clone.
setlocal EnableExtensions
set "BIN=%~dp0"
set "REPO="
call :read_sidecar
if defined REPO if not exist "%REPO%\tools\Glimpse.Drive\Glimpse.Drive.csproj" set "REPO="

rem install.ps1 verifies the sidecar through this exit code. It must not see the lexical
rem fallback, which resolves to the repo whenever this runs from the clone itself.
if "%~1"=="--check-sidecar" (
    if defined REPO exit /b 0
    exit /b 1
)

if not defined REPO for %%I in ("%BIN%..\..") do set "REPO=%%~fI"

set "PROJECT=%REPO%\tools\Glimpse.Drive\Glimpse.Drive.csproj"
set "DLL=%REPO%\tools\Glimpse.Drive\bin\Debug\net10.0\Glimpse.Drive.dll"
if not exist "%PROJECT%" (
    echo glimpse-drive: no clone found; run scripts\install.ps1 from a Glimpse clone 1>&2
    exit /b 127
)
where dotnet >nul 2>&1 || (
    echo glimpse-drive: needs the .NET 10 SDK ^(dotnet on PATH^): https://dot.net 1>&2
    exit /b 127
)

rem glimpse-drive-stale.ps1 decides whether to rebuild; the stamp it reads marks a finished build.
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%BIN%glimpse-drive-stale.ps1" -Repo "%REPO%" >nul 2>&1
if errorlevel 1 (
    dotnet build "%PROJECT%" -v quiet 1>&2
    if errorlevel 1 exit /b 1
    type nul > "%REPO%\tools\Glimpse.Drive\obj\glimpse-drive-wrapper.stamp"
)

dotnet "%DLL%" %*
exit /b %errorlevel%

rem The sidecar is UTF-8 and `set /p` decodes in the console code page, so the read runs
rem under code page 65001 and the caller's code page is restored straight after.
:read_sidecar
if not exist "%BIN%glimpse-drive.repo" exit /b 0
for /f "tokens=2 delims=:." %%C in ('chcp') do set "CALLER_CP=%%C"
chcp 65001 >nul
set /p REPO=<"%BIN%glimpse-drive.repo"
chcp %CALLER_CP% >nul
exit /b 0
