@echo off
rem Glimpse CLI on PATH (via the plugin's bin/). Runs the in-repo built DLL so it is
rem always current; builds once if the DLL is missing.
rem
rem A junction is transparent to lexical ".." traversal, so from the installed location
rem (~/.claude/skills/glimpse/bin) "..\..\" resolves to ~/.claude/skills, NOT the repo.
rem install.ps1 therefore writes the real repo path to the glimpse.repo sidecar; the
rem lexical fallback covers running this script directly from a clone.
setlocal EnableExtensions
set "BIN=%~dp0"
set "REPO="
if exist "%BIN%glimpse.repo" set /p REPO=<"%BIN%glimpse.repo"
if not defined REPO for %%I in ("%BIN%..\..") do set "REPO=%%~fI"

set "PROJECT=%REPO%\tools\Glimpse.Capture\Glimpse.Capture.csproj"
set "DLL=%REPO%\tools\Glimpse.Capture\bin\Debug\net10.0\Glimpse.Capture.dll"

if not exist "%DLL%" (
    dotnet build "%PROJECT%" -v quiet 1>&2
    if errorlevel 1 exit /b 1
)

dotnet "%DLL%" %*
exit /b %errorlevel%
