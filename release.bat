@echo off
setlocal
REM ============================================================================
REM  Lore - build a release  (double-click me, or run: release.bat)
REM ============================================================================
REM  This is the ONE step to prep a release. It runs the full per-release chain:
REM
REM    1. Ensure Native\sweph.dll exists (built once by Build-SwephDll.ps1).
REM    2. scripts\build-installer.ps1, which:
REM         - publishes the self-contained app   (scripts\build-portable.ps1)
REM         - runs the personal-data safety gate (scripts\assert-clean-release.ps1)
REM         - compiles  artifacts\LoreSetup-<version>.exe   (needs Inno Setup)
REM
REM  The other scripts are ONE-TIME / occasional setup, NOT part of a release
REM  (their outputs are already committed or present, so you normally never run
REM  them):
REM    Build-SwephDll.ps1      -> Native\sweph.dll    (fresh clone / SwEph upgrade; needs VS Build Tools)
REM    scripts\build-icons.ps1 -> Assets\AppIcon.ico  (only if the icon changes; needs ImageMagick)
REM    scripts\build-cities.py -> Data\cities.json    (only if the city DB changes; needs Python)
REM
REM  Any arguments are forwarded to build-installer.ps1. For example:
REM    release.bat -SkipPublish   (reuse the last publish; just recompile the installer)
REM ============================================================================

cd /d "%~dp0"
set "PS=powershell -NoProfile -ExecutionPolicy Bypass -File"

if not exist "Native\sweph.dll" (
    echo [release] Native\sweph.dll is missing - building it once ^(needs Visual Studio Build Tools^)...
    %PS% "%~dp0Build-SwephDll.ps1"
    if errorlevel 1 goto :failed
)

echo [release] Building the installer...
%PS% "%~dp0scripts\build-installer.ps1" %*
if errorlevel 1 goto :failed

echo.
echo [release] Done. Look in the artifacts\ folder for LoreSetup-^<version^>.exe
pause
exit /b 0

:failed
echo.
echo [release] BUILD FAILED - see the messages above. Nothing was produced.
pause
exit /b 1
