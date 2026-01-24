# PrepareFiles.ps1
# Prepares all files for the IndiLogs Suite installer
# Combines IndiLogs 3.0 and IndiChart.UI into a single folder

param(
    [string]$IndiLogsPublishPath = "C:\Users\yegudish\OneDrive - HP Inc\Desktop\HAIM",
    [string]$IndiChartPublishPath = "..\IndiChartSuite\publish\Application Files\IndiChart.UI_1_0_0_0",
    [string]$DestPath = ".\InstallerFiles"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Preparing IndiLogs Suite Installer Files" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Create/clean destination folder
if (Test-Path $DestPath) {
    Write-Host "Cleaning existing folder..." -ForegroundColor Yellow
    Remove-Item $DestPath -Recurse -Force
}
New-Item -ItemType Directory -Path $DestPath -Force | Out-Null

# ========================================
# STEP 1: Copy IndiLogs 3.0 files
# ========================================
Write-Host ""
Write-Host "Step 1: Copying IndiLogs 3.0 files..." -ForegroundColor Cyan

$indiLogsAppFiles = Join-Path $IndiLogsPublishPath "Application Files"
$latestIndiLogs = Get-ChildItem $indiLogsAppFiles -Directory | Sort-Object Name -Descending | Select-Object -First 1

if (-not $latestIndiLogs) {
    Write-Host "ERROR: Could not find IndiLogs publish files at: $indiLogsAppFiles" -ForegroundColor Red
    Write-Host "Please publish IndiLogs 3.0 first!" -ForegroundColor Red
    exit 1
}

Write-Host "  Found: $($latestIndiLogs.Name)" -ForegroundColor Gray

# Copy IndiLogs files
Get-ChildItem -Path $latestIndiLogs.FullName -Recurse | ForEach-Object {
    $relativePath = $_.FullName.Substring($latestIndiLogs.FullName.Length + 1)
    $destFile = Join-Path $DestPath $relativePath

    if ($_.PSIsContainer) {
        New-Item -ItemType Directory -Path $destFile -Force | Out-Null
    }
    else {
        $destDir = Split-Path $destFile -Parent
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item $_.FullName -Destination $destFile -Force
    }
}

# Remove .deploy extensions from IndiLogs files
Write-Host "  Removing .deploy extensions..." -ForegroundColor Gray
Get-ChildItem -Path $DestPath -Recurse -Filter "*.deploy" | ForEach-Object {
    $newName = $_.FullName -replace '\.deploy$', ''
    Rename-Item $_.FullName -NewName $newName -Force
}

# Remove manifest files
Get-ChildItem -Path $DestPath -Recurse -Filter "*.manifest" | Remove-Item -Force -ErrorAction SilentlyContinue

$indiLogsCount = (Get-ChildItem -Path $DestPath -Recurse -File).Count
Write-Host "  Copied $indiLogsCount files" -ForegroundColor Green

# ========================================
# STEP 2: Copy IndiChart.UI files
# ========================================
Write-Host ""
Write-Host "Step 2: Copying IndiChart.UI files..." -ForegroundColor Cyan

if (-not (Test-Path $IndiChartPublishPath)) {
    Write-Host "ERROR: Could not find IndiChart publish files at: $IndiChartPublishPath" -ForegroundColor Red
    Write-Host "Please check the path!" -ForegroundColor Red
    exit 1
}

# Copy IndiChart files (skip duplicates)
$skippedFiles = 0
$copiedFiles = 0

Get-ChildItem -Path $IndiChartPublishPath -Recurse | ForEach-Object {
    $relativePath = $_.FullName.Substring((Get-Item $IndiChartPublishPath).FullName.Length + 1)
    # Remove .deploy extension from path
    $relativePath = $relativePath -replace '\.deploy$', ''
    $destFile = Join-Path $DestPath $relativePath

    if ($_.PSIsContainer) {
        if (-not (Test-Path $destFile)) {
            New-Item -ItemType Directory -Path $destFile -Force | Out-Null
        }
    }
    else {
        # Skip if file already exists (from IndiLogs)
        if (Test-Path $destFile) {
            $skippedFiles++
        }
        else {
            $destDir = Split-Path $destFile -Parent
            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }
            Copy-Item $_.FullName -Destination $destFile -Force
            $copiedFiles++
        }
    }
}

# Remove .deploy extensions from any remaining files
Get-ChildItem -Path $DestPath -Recurse -Filter "*.deploy" | ForEach-Object {
    $newName = $_.FullName -replace '\.deploy$', ''
    if (-not (Test-Path $newName)) {
        Rename-Item $_.FullName -NewName $newName -Force
    }
    else {
        Remove-Item $_.FullName -Force
    }
}

# Remove manifest files
Get-ChildItem -Path $DestPath -Recurse -Filter "*.manifest" | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "  Copied $copiedFiles files (skipped $skippedFiles duplicates)" -ForegroundColor Green

# ========================================
# Summary
# ========================================
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
$totalFiles = (Get-ChildItem -Path $DestPath -Recurse -File).Count
Write-Host "Total files prepared: $totalFiles" -ForegroundColor Green
Write-Host "Output folder: $DestPath" -ForegroundColor Green
Write-Host ""
Write-Host "You can now compile IndiLogsSuite.iss with Inno Setup!" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
