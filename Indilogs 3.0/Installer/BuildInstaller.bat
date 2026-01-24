@echo off
echo ========================================
echo IndiLogs Suite Installer Builder
echo ========================================
echo.

echo Step 1: Preparing all files (IndiLogs + IndiChart)...
powershell -ExecutionPolicy Bypass -File PrepareFiles.ps1
if errorlevel 1 (
    echo ERROR: Failed to prepare files
    pause
    exit /b 1
)

echo.
echo Step 2: Building installer with Inno Setup...
echo Looking for Inno Setup...

set ISCC=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files (x86)\Inno Setup 5\ISCC.exe" (
    set "ISCC=C:\Program Files (x86)\Inno Setup 5\ISCC.exe"
)

if "%ISCC%"=="" (
    echo.
    echo ERROR: Inno Setup not found!
    echo Please install Inno Setup from: https://jrsoftware.org/isinfo.php
    echo Then run this script again, or manually compile IndiLogsSuite.iss
    pause
    exit /b 1
)

echo Found Inno Setup: %ISCC%
"%ISCC%" IndiLogsSuite.iss
if errorlevel 1 (
    echo ERROR: Failed to build installer
    pause
    exit /b 1
)

echo.
echo ========================================
echo SUCCESS! Installer created in: Output\IndiLogsSuite_Setup.exe
echo ========================================
pause
