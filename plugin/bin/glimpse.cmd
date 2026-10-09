@echo off
rem Glimpse CLI on PATH (via the plugin's bin/). Runs the in-repo built DLL so it is
rem always current; builds once if the DLL is missing.
rem
rem A junction is transparent to lexical ".." traversal, so from the installed location
rem (~/.claude/skills/glimpse/bin) "..\..\" resolves to ~/.claude/skills, NOT the repo.
rem install.ps1 therefore writes the real repo path to the glimpse.repo sidecar; the
rem lexical fallback covers running this script directly from a clone, and a sidecar
rem left behind by a moved clone.
setlocal EnableExtensions
set "BIN=%~dp0"
set "REPO="
call :read_sidecar
if defined REPO if not exist "%REPO%\tools\Glimpse.Capture\Glimpse.Capture.csproj" set "REPO="

rem install.ps1 verifies the sidecar through this exit code. It must not see the lexical
rem fallback, which resolves to the repo whenever this runs from the clone itself.
if "%~1"=="--check-sidecar" (
    if defined REPO exit /b 0
    exit /b 1
)

if not defined REPO for %%I in ("%BIN%..\..") do set "REPO=%%~fI"

set "PROJECT=%REPO%\tools\Glimpse.Capture\Glimpse.Capture.csproj"
set "DLL=%REPO%\tools\Glimpse.Capture\bin\Debug\net10.0\Glimpse.Capture.dll"
if not exist "%PROJECT%" goto run_release

if not exist "%DLL%" (
    dotnet build "%PROJECT%" -v quiet 1>&2
    if errorlevel 1 exit /b 1
)

dotnet "%DLL%" %*
exit /b %errorlevel%

rem No clone to build from, as in a marketplace install: run the released tool this plugin pins.
rem --yes skips dnx's download prompt, which would hang a non-interactive caller.
:run_release
set /p VERSION=<"%BIN%glimpse.version"
where dotnet >nul 2>&1 || (
    echo glimpse: needs the .NET 10 SDK ^(dotnet on PATH^): https://dot.net 1>&2
    exit /b 127
)
dotnet dnx "Glimpse.Capture@%VERSION%" --yes -- %*
exit /b %errorlevel%

rem The sidecar is UTF-8 and `set /p` decodes in the console code page, so the read runs
rem under code page 65001 and the caller's code page is restored straight after.
:read_sidecar
if not exist "%BIN%glimpse.repo" exit /b 0
for /f "tokens=2 delims=:." %%C in ('chcp') do set "CALLER_CP=%%C"
chcp 65001 >nul
set /p REPO=<"%BIN%glimpse.repo"
chcp %CALLER_CP% >nul
exit /b 0
