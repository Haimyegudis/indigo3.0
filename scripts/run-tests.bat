@echo off
setlocal enabledelayedexpansion

:: ============================================================================
:: IndiLogs 3.0 — Detailed Unit Test Runner
:: ============================================================================
:: Usage:
::   run-tests.bat                                  Run all tests
::   run-tests.bat --filter ClassName=FooTests      Run filtered tests
::   run-tests.bat --no-build                       Skip build step
::   run-tests.bat --coverage                       Collect code coverage
::   run-tests.bat --release                        Build Release config
:: ============================================================================

set "ROOT=%~dp0.."
set "TEST_PROJECT=%ROOT%\IndiLogs.Tests\IndiLogs.Tests.csproj"
set "MAIN_PROJECT=%ROOT%\Indilogs 3.0\Indilogs 3.0.csproj"
set "RESULTS_DIR=%ROOT%\TestResults"
set "SCRIPTS_DIR=%~dp0"
set "CONFIG=Debug"
set "FILTER="
set "NO_BUILD="
set "COVERAGE="
set "START_TIME=%TIME%"

:: Parse arguments — use goto to avoid parenthesized blocks with labels
:parse_args
if "%~1"=="" goto :args_done
if /i "%~1"=="--help" goto :show_help
if /i "%~1"=="--no-build" set "NO_BUILD=1" & shift & goto :parse_args
if /i "%~1"=="--coverage"  set "COVERAGE=1"  & shift & goto :parse_args
if /i "%~1"=="--release"   set "CONFIG=Release" & shift & goto :parse_args
if /i "%~1"=="--filter" goto :handle_filter
shift
goto :parse_args

:handle_filter
:: Reassemble filter value split by cmd.exe = delimiter
:: e.g. "--filter ClassName=FooTests" arrives as args: --filter, ClassName, FooTests
shift
set "FILTER=%~1"
shift
:rejoin_filter
if "%~1"=="" goto :args_done
set "_T=%~1"
if "!_T:~0,2!"=="--" goto :parse_args
set "FILTER=!FILTER!^=!_T!"
shift
goto :rejoin_filter

:args_done

echo.
echo  ============================================================
echo   IndiLogs 3.0 - Unit Test Runner
echo  ============================================================
echo   Config:    %CONFIG%
if defined FILTER echo   Filter:    !FILTER!
if defined COVERAGE echo   Coverage:  ENABLED
echo   Time:      %DATE% %TIME%
echo  ============================================================
echo.

:: -------------------------------------------------------------------
:: Step 1: Restore
:: -------------------------------------------------------------------
echo [1/4] Restoring NuGet packages...
dotnet restore "%TEST_PROJECT%" --verbosity quiet
if !ERRORLEVEL! neq 0 (
    echo       RESTORE FAILED
    exit /b 1
)
echo       OK
echo.

:: -------------------------------------------------------------------
:: Step 2: Build
:: -------------------------------------------------------------------
if defined NO_BUILD (
    echo [2/4] Build skipped [--no-build]
    echo.
    goto :step3
)

echo [2/4] Building main project [%CONFIG%]...
dotnet build "%MAIN_PROJECT%" --no-restore --configuration %CONFIG% --verbosity quiet
if !ERRORLEVEL! neq 0 (
    echo       BUILD FAILED - main project
    exit /b 1
)
echo       Main project OK

echo       Building test project...
dotnet build "%TEST_PROJECT%" --no-restore --configuration %CONFIG% --verbosity quiet
if !ERRORLEVEL! neq 0 (
    echo       BUILD FAILED - test project
    exit /b 1
)
echo       Test project OK
echo.

:: -------------------------------------------------------------------
:: Step 3: Clean previous results
:: -------------------------------------------------------------------
:step3
echo [3/4] Cleaning previous test results...
if exist "%RESULTS_DIR%" rmdir /s /q "%RESULTS_DIR%" >nul 2>&1
mkdir "%RESULTS_DIR%" >nul 2>&1
echo       OK
echo.

:: -------------------------------------------------------------------
:: Step 4: Run tests
:: -------------------------------------------------------------------
echo [4/4] Running tests...
echo.

set "TEST_CMD=dotnet test "%TEST_PROJECT%" --no-build --configuration %CONFIG% --verbosity normal"
set "TEST_CMD=!TEST_CMD! --results-directory "%RESULTS_DIR%" --logger "trx;LogFileName=TestResults.trx""
set "TEST_CMD=!TEST_CMD! --logger "console;verbosity=detailed""

if defined FILTER set "TEST_CMD=!TEST_CMD! --filter "!FILTER!""
if defined COVERAGE set "TEST_CMD=!TEST_CMD! --collect:"XPlat Code Coverage""

!TEST_CMD!
set "TEST_EXIT=!ERRORLEVEL!"

:: -------------------------------------------------------------------
:: Report results via PowerShell
:: -------------------------------------------------------------------
echo.
echo  ============================================================

if defined COVERAGE (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPTS_DIR%report-results.ps1" -ResultsDir "%RESULTS_DIR%" -Coverage -Threshold 60
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPTS_DIR%report-results.ps1" -ResultsDir "%RESULTS_DIR%"
)

:: -------------------------------------------------------------------
:: Final summary
:: -------------------------------------------------------------------
echo.
echo  Started:  %START_TIME%
echo  Finished: !TIME!
echo  ============================================================

if !TEST_EXIT! neq 0 (
    echo.
    echo  TESTS FAILED [exit code !TEST_EXIT!]
    echo.
    exit /b !TEST_EXIT!
)

echo.
echo  ALL TESTS PASSED
echo.
exit /b 0

:: -------------------------------------------------------------------
:show_help
echo.
echo  Usage: run-tests.bat [OPTIONS]
echo.
echo  Options:
echo    --filter X     Run only tests matching filter X
echo                   Examples:
echo                     --filter FullyQualifiedName~CsvExport
echo                     --filter ClassName=CsvExportServiceTests
echo                     --filter DisplayName~ParseAxisMon
echo    --no-build     Skip the build step (use previous build)
echo    --coverage     Collect XPlat Code Coverage (Cobertura)
echo    --release      Build in Release configuration
echo    --help         Show this help
echo.
echo  Examples:
echo    run-tests.bat                          Run all tests
echo    run-tests.bat --coverage               Run all + coverage report
echo    run-tests.bat --filter ClassName=AuthenticodeVerifierTests
echo    run-tests.bat --release --coverage     Release build + coverage
echo.
exit /b 0
