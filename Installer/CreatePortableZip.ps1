# CreatePortableZip.ps1
# Creates a portable ZIP distribution of IndiLogs Suite
# This avoids antivirus issues with installer executables

param(
    [string]$OutputPath = ".\Output",
    [string]$Version = "1.0.0"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Creating IndiLogs Suite Portable ZIP" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Ensure InstallerFiles exists
$installerFilesPath = ".\InstallerFiles"
if (-not (Test-Path $installerFilesPath)) {
    Write-Host "ERROR: InstallerFiles folder not found!" -ForegroundColor Red
    Write-Host "Please run PrepareFiles.ps1 first." -ForegroundColor Yellow
    exit 1
}

# Create output directory
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath | Out-Null
}

$zipName = "IndiLogsSuite_Portable_v$Version.zip"
$zipPath = Join-Path $OutputPath $zipName
$tempFolder = Join-Path $env:TEMP "IndiLogsSuite_Temp"

Write-Host "`nPreparing files..." -ForegroundColor Yellow

# Clean and create temp folder
if (Test-Path $tempFolder) {
    Remove-Item $tempFolder -Recurse -Force
}
New-Item -ItemType Directory -Path $tempFolder | Out-Null

# Copy all files from InstallerFiles
Copy-Item "$installerFilesPath\*" -Destination $tempFolder -Recurse -Force

# Copy libSkiaSharp.dll to root (for .NET runtime)
$skiaSource = Join-Path $tempFolder "runtimes\win-x64\native\libSkiaSharp.dll"
if (Test-Path $skiaSource) {
    Copy-Item $skiaSource -Destination $tempFolder -Force
    Write-Host "  Copied libSkiaSharp.dll to root" -ForegroundColor Green
}

# Copy SQLite.Interop.dll to root if not there
$sqliteInteropX64 = Join-Path $tempFolder "x64\SQLite.Interop.dll"
$sqliteInteropRoot = Join-Path $tempFolder "SQLite.Interop.dll"
if ((Test-Path $sqliteInteropX64) -and (-not (Test-Path $sqliteInteropRoot))) {
    Copy-Item $sqliteInteropX64 -Destination $sqliteInteropRoot -Force
    Write-Host "  Copied SQLite.Interop.dll to root" -ForegroundColor Green
}

# Create README file with instructions
$readmeContent = @"
IndiLogs Suite - Portable Edition
==================================
Version: $Version

This is a portable installation of IndiLogs Suite.
No installation required - just extract and run!

CONTENTS:
---------
- IndiLogs 3.0.exe  - Log viewer and analyzer

NOTE: For CSV chart viewing, install Flow CSV Viewer separately.

REQUIREMENTS:
-------------
1. Visual C++ Redistributable 2015-2022 (for database browsing)
   Download: https://aka.ms/vs/17/release/vc_redist.x64.exe

QUICK START:
------------
1. Extract this ZIP to any folder (e.g., C:\Tools\IndiLogsSuite)
2. Run IndiLogs 3.0.exe for log analysis

OPTIONAL - CREATE SHORTCUTS:
----------------------------
Right-click the EXE files and select "Create shortcut"
Then move shortcuts to your Desktop or Start Menu

For any issues, contact HP Software QA team.
"@

$readmePath = Join-Path $tempFolder "README.txt"
Set-Content -Path $readmePath -Value $readmeContent -Encoding UTF8

# Create batch files for easy launching
$indilogsLauncher = @"
@echo off
start "" "%~dp0IndiLogs 3.0.exe" %*
"@
Set-Content -Path (Join-Path $tempFolder "Start_IndiLogs.bat") -Value $indilogsLauncher -Encoding ASCII

# Create the ZIP file
Write-Host "`nCreating ZIP archive..." -ForegroundColor Yellow
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Use Compress-Archive
Compress-Archive -Path "$tempFolder\*" -DestinationPath $zipPath -Force

# Cleanup
Remove-Item $tempFolder -Recurse -Force

$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "SUCCESS! Created: $zipName" -ForegroundColor Green
Write-Host "Size: $([math]::Round($zipSize, 2)) MB" -ForegroundColor Green
Write-Host "Location: $((Resolve-Path $zipPath).Path)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "`nThis ZIP can be distributed without antivirus issues!" -ForegroundColor Yellow
