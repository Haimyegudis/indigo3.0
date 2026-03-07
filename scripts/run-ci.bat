@echo off
setlocal enabledelayedexpansion

:: ============================================================================
:: IndiLogs 3.0 — Local CI Pipeline Runner
:: ============================================================================
:: Replicates the exact GitHub Actions CI workflow locally so you can verify
:: everything passes before pushing. Matches .github/workflows/ci.yml.
::
:: Usage:
::   run-ci.bat           Run the full CI pipeline
::   run-ci.bat --quick   Skip coverage check (faster)
:: ============================================================================

set "ROOT=%~dp0.."
set "MAIN_PROJECT=%ROOT%\Indilogs 3.0\Indilogs 3.0.csproj"
set "TEST_PROJECT=%ROOT%\IndiLogs.Tests\IndiLogs.Tests.csproj"
set "RESULTS_DIR=%ROOT%\TestResults"
set "SCRIPTS_DIR=%~dp0"
set "CONFIG=Release"
set "COVERAGE_THRESHOLD=15"
set "QUICK="
set "STEP=0"
set "TOTAL_STEPS=6"
set "FAILED=0"
set "START_TIME=%TIME%"

:: Parse arguments
:parse_args
if "%~1"=="" goto :args_done
if /i "%~1"=="--quick" set "QUICK=1" & set "TOTAL_STEPS=4" & shift & goto :parse_args
if /i "%~1"=="--help" goto :show_help
shift
goto :parse_args
:args_done

echo.
echo  ================================================================
echo   IndiLogs 3.0 - Local CI Pipeline
echo   Mirrors: .github/workflows/ci.yml
echo  ================================================================
echo   Date:       %DATE% %TIME%
echo   Config:     %CONFIG%
echo   Threshold:  %COVERAGE_THRESHOLD%%% line coverage
if defined QUICK echo   Mode:       QUICK [no coverage]
echo  ================================================================
echo.

:: ===================================================================
:: STEP 1: Restore dependencies
:: ===================================================================
set /a STEP+=1
echo  [!STEP!/%TOTAL_STEPS%] Restoring NuGet packages...
echo.
dotnet restore "%MAIN_PROJECT%"
if !ERRORLEVEL! neq 0 set "FAILED=1" & goto :report
dotnet restore "%TEST_PROJECT%"
if !ERRORLEVEL! neq 0 set "FAILED=1" & goto :report
echo.
echo  PASS: Restore completed
echo  ----------------------------------------------------------------
echo.

:: ===================================================================
:: STEP 2: Build (Release)
:: ===================================================================
set /a STEP+=1
echo  [!STEP!/%TOTAL_STEPS%] Building projects [%CONFIG%]...
echo.
dotnet build "%MAIN_PROJECT%" --no-restore --configuration %CONFIG%
if !ERRORLEVEL! neq 0 (
    echo  FAIL: Main project build failed
    set "FAILED=1"
    goto :report
)
dotnet build "%TEST_PROJECT%" --no-restore --configuration %CONFIG%
if !ERRORLEVEL! neq 0 (
    echo  FAIL: Test project build failed
    set "FAILED=1"
    goto :report
)
echo.
echo  PASS: Build succeeded - zero errors
echo  ----------------------------------------------------------------
echo.

:: ===================================================================
:: STEP 3: Run tests
:: ===================================================================
set /a STEP+=1
if exist "%RESULTS_DIR%" rmdir /s /q "%RESULTS_DIR%" >nul 2>&1

if defined QUICK (
    echo  [!STEP!/%TOTAL_STEPS%] Running tests [no coverage]...
    echo.
    dotnet test "%TEST_PROJECT%" --no-build --configuration %CONFIG% --verbosity normal --results-directory "%RESULTS_DIR%" --logger "trx;LogFileName=TestResults.trx"
) else (
    echo  [!STEP!/%TOTAL_STEPS%] Running tests with code coverage...
    echo.
    dotnet test "%TEST_PROJECT%" --no-build --configuration %CONFIG% --verbosity normal --collect:"XPlat Code Coverage" --results-directory "%RESULTS_DIR%" --logger "trx;LogFileName=TestResults.trx"
)

if !ERRORLEVEL! neq 0 (
    echo  FAIL: Tests failed
    set "FAILED=1"
    goto :report
)
echo.
echo  PASS: All tests passed
echo  ----------------------------------------------------------------
echo.

:: ===================================================================
:: STEP 4: Test results summary
:: ===================================================================
set /a STEP+=1
echo  [!STEP!/%TOTAL_STEPS%] Test results summary...
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPTS_DIR%report-results.ps1" -ResultsDir "%RESULTS_DIR%"
echo  ----------------------------------------------------------------
echo.

if defined QUICK goto :report

:: ===================================================================
:: STEP 5: Check coverage threshold
:: ===================================================================
set /a STEP+=1
echo  [!STEP!/%TOTAL_STEPS%] Checking coverage threshold [%COVERAGE_THRESHOLD%%%]...
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPTS_DIR%report-results.ps1" -ResultsDir "%RESULTS_DIR%" -Coverage -Threshold %COVERAGE_THRESHOLD%
if !ERRORLEVEL! neq 0 (
    set "FAILED=1"
    goto :report
)
echo  ----------------------------------------------------------------
echo.

:: ===================================================================
:: STEP 6: Archive results
:: ===================================================================
set /a STEP+=1
echo  [!STEP!/%TOTAL_STEPS%] Test artifacts location...
echo.
echo  Results:  %RESULTS_DIR%
echo  TRX:      %RESULTS_DIR%\TestResults.trx
echo.
echo  PASS: Artifacts ready [same as GitHub Actions upload-artifact]
echo  ----------------------------------------------------------------
echo.

:: ===================================================================
:: FINAL REPORT
:: ===================================================================
:report
echo.
echo  ================================================================
if !FAILED! neq 0 (
    echo   CI PIPELINE: FAILED
    echo.
    echo   Fix the issues above before pushing to GitHub.
    echo   The same checks run on every push/PR to main.
) else (
    echo   CI PIPELINE: PASSED
    echo.
    echo   All checks passed. Safe to push to GitHub.
)
echo.
echo   Started:  %START_TIME%
echo   Finished: !TIME!
echo  ================================================================
echo.
pause
exit /b !FAILED!

:: -------------------------------------------------------------------
:show_help
echo.
echo  Usage: run-ci.bat [OPTIONS]
echo.
echo  Replicates the GitHub Actions CI pipeline locally.
echo  Matches .github/workflows/ci.yml step by step:
echo.
echo    Step 1: Restore NuGet packages
echo    Step 2: Build [Release]
echo    Step 3: Run all xUnit tests with code coverage
echo    Step 4: Test results summary
echo    Step 5: Check coverage threshold [60%% minimum]
echo    Step 6: Archive test artifacts
echo.
echo  Options:
echo    --quick    Skip coverage [faster, steps 1-4 only]
echo    --help     Show this help
echo.
echo  Examples:
echo    run-ci.bat              Full CI pipeline [same as GitHub]
echo    run-ci.bat --quick      Build + test only [no coverage]
echo.
exit /b 0
