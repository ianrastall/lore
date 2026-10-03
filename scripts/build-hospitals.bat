@echo off
rem ===========================================================================
rem  Lore - Hospital database builder (double-click me)
rem
rem  Rebuilds Data\hospitals.json, the list the Add-Chart dialog searches.
rem  Pipeline:
rem    Stage 1  wikidata_hospitals.py  -> Data\hospitals_wikidata.csv  (Wikidata)
rem    Stage 2  build-hospitals.py     -> Data\hospitals.json          (+ OpenStreetMap)
rem
rem  You normally only need Stage 2 (option 1): the Wikidata CSV is committed,
rem  and OpenStreetMap is where the small local hospitals come from.
rem ===========================================================================
setlocal enableextensions
title Lore - Build hospitals.json

rem This file lives in scripts\, so the repository root is one level up.
cd /d "%~dp0.."

rem --- Self-update once, then re-launch the fresh copy -----------------------
rem  A git pull can rewrite this very file; re-executing a new cmd afterwards
rem  avoids the "batch file changed while running" corruption on Windows.
if not defined LORE_HOSP_REEXEC (
    where git >nul 2>&1 && (
        echo Updating scripts to the latest version ^(git pull, fast-forward only^)...
        git pull --ff-only
        echo.
    )
    set LORE_HOSP_REEXEC=1
    cmd /c "%~f0" %*
    exit /b
)

echo ============================================================
echo   Lore - Hospital database builder
echo ============================================================
echo   Repository: %CD%
echo.

rem --- Python must be on PATH ------------------------------------------------
where python >nul 2>&1
if errorlevel 1 (
    echo ERROR: "python" was not found on your PATH.
    echo Install Python 3 and make sure "python" works in a terminal, then retry.
    goto end
)

echo What would you like to do?
echo.
echo   [1] Quick build    Merge the committed Wikidata list with OpenStreetMap
echo                      worldwide, plus former names and closed hospitals.
echo                      Recommended. Needs internet. An hour or more for the
echo                      whole world; a single country takes a minute or two.
echo.
echo   [2] Full refresh   Re-download the Wikidata list first, THEN merge
echo                      OpenStreetMap. Slow (30+ minutes). Needs internet.
echo.
echo   [3] Offline        Rebuild from the committed Wikidata list only.
echo                      No internet, no OpenStreetMap (no new coverage).
echo.

choice /c 123 /n /m "Press 1, 2, or 3 (or close this window to cancel): "
set "PICK=%errorlevel%"
echo.

if "%PICK%"=="3" goto offline
if "%PICK%"=="2" goto refresh
if "%PICK%"=="1" goto quick
goto end

rem Ask which countries to pull from OpenStreetMap. Blank = whole world.
:ask_countries
echo Which countries should I pull from OpenStreetMap?
echo   - Leave BLANK and press Enter for the whole world (slow; public servers
echo     are often busy, but it resumes where it left off if you re-run).
echo   - Or type one or more 2-letter codes, e.g.  US   or   US,CA,GB
echo     (US = United States, CA = Canada, GB = United Kingdom, etc.)
echo.
set "COUNTRIES="
set /p "COUNTRIES=Countries (blank = world): "
set "CFLAG="
if defined COUNTRIES set "CFLAG=--countries %COUNTRIES%"
echo.
goto :eof

:refresh
call :ask_countries
echo -- Step 1 of 2: querying Wikidata for hospitals worldwide --
echo    (this is the slow part; progress is saved after each country,
echo     so you can re-run and it resumes)
python scripts\wikidata_hospitals.py -o Data\hospitals_wikidata.csv --checkpoint Data\.wikidata_progress.json
if errorlevel 1 (
    echo.
    echo ERROR: the Wikidata step failed. Data\hospitals.json was NOT rebuilt.
    goto end
)
echo.
echo -- Step 2 of 2: merging with OpenStreetMap --
python scripts\build-hospitals.py %CFLAG%
goto report

:quick
call :ask_countries
python scripts\build-hospitals.py %CFLAG%
goto report

:offline
python scripts\build-hospitals.py --no-osm
goto report

:report
if errorlevel 1 (
    echo.
    echo ERROR: the build failed - see the messages above.
    echo Data\hospitals.json may be unchanged.
    goto end
)
echo.
echo ============================================================
echo   Done. Data\hospitals.json has been rebuilt.
echo.
echo   To ship it, commit and push:
echo       git add Data\hospitals.json
echo       git commit -m "Rebuild hospital database"
echo       git push
echo ============================================================

:end
echo.
pause
endlocal
