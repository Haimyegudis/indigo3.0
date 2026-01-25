# PrepareFiles.ps1
# Prepares all files for the IndiLogs Suite installer
# Combines IndiLogs 3.0 and IndiChart.UI into a single folder

param(
    [string]$IndiLogsPublishPath = "C:\Users\yegudish\OneDrive - HP Inc\Desktop\HAIM",
    [string]$IndiChartPublishPath = "C:\Users\yegudish\source\repos\IndiChartSuite\IndiChart.UI\bin\Release\net8.0-windows",
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

# Copy IndiChart files (skip duplicates, but ALWAYS overwrite SkiaSharp files)
$skippedFiles = 0
$copiedFiles = 0
$overwrittenFiles = 0

# Files that IndiChart version should always overwrite (newer SkiaSharp)
$alwaysOverwrite = @("libSkiaSharp.dll", "SkiaSharp.dll", "SkiaSharp.Views.Desktop.Common.dll", "SkiaSharp.Views.WPF.dll")

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
        $fileName = $_.Name -replace '\.deploy$', ''
        $shouldOverwrite = $alwaysOverwrite -contains $fileName

        if (Test-Path $destFile) {
            if ($shouldOverwrite) {
                # Overwrite SkiaSharp files with IndiChart's newer version
                $destDir = Split-Path $destFile -Parent
                if (-not (Test-Path $destDir)) {
                    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
                }
                Copy-Item $_.FullName -Destination $destFile -Force
                $overwrittenFiles++
            }
            else {
                $skippedFiles++
            }
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

Write-Host "  Copied $copiedFiles files, overwritten $overwrittenFiles SkiaSharp files (skipped $skippedFiles duplicates)" -ForegroundColor Green

# ========================================
# STEP 3: Copy correct SkiaSharp libraries from NuGet packages
# ========================================
Write-Host ""
Write-Host "Step 3: Copying correct SkiaSharp libraries from NuGet packages..." -ForegroundColor Cyan

$packagesPath = "..\..\packages"

# Copy SkiaSharp managed DLLs (3.119.0)
$skiaManagedPath = Join-Path $packagesPath "SkiaSharp.3.119.0\lib\net8.0"
if (Test-Path $skiaManagedPath) {
    Copy-Item (Join-Path $skiaManagedPath "SkiaSharp.dll") -Destination $DestPath -Force
    Write-Host "  Copied SkiaSharp.dll from NuGet package" -ForegroundColor Green
}

# Copy SkiaSharp.Views.Desktop.Common
$skiaViewsCommonPath = Join-Path $packagesPath "SkiaSharp.Views.Desktop.Common.3.119.0\lib\net8.0"
if (Test-Path $skiaViewsCommonPath) {
    Copy-Item (Join-Path $skiaViewsCommonPath "SkiaSharp.Views.Desktop.Common.dll") -Destination $DestPath -Force
    Write-Host "  Copied SkiaSharp.Views.Desktop.Common.dll from NuGet package" -ForegroundColor Green
}

# Copy SkiaSharp.Views.WPF
$skiaViewsWpfPath = Join-Path $packagesPath "SkiaSharp.Views.WPF.3.119.0\lib\net8.0-windows10.0.19041"
if (Test-Path $skiaViewsWpfPath) {
    Copy-Item (Join-Path $skiaViewsWpfPath "SkiaSharp.Views.WPF.dll") -Destination $DestPath -Force
    Write-Host "  Copied SkiaSharp.Views.WPF.dll from NuGet package" -ForegroundColor Green
}

# Copy native libraries
$skiaPackagePath = Join-Path $packagesPath "SkiaSharp.NativeAssets.Win32.3.119.0\runtimes"

if (Test-Path $skiaPackagePath) {
    # Copy all runtime folders (win-x64, win-x86, win-arm64)
    $runtimeDest = Join-Path $DestPath "runtimes"

    # Copy win-x64
    $srcX64 = Join-Path $skiaPackagePath "win-x64\native\libSkiaSharp.dll"
    $destX64 = Join-Path $runtimeDest "win-x64\native\libSkiaSharp.dll"
    if (Test-Path $srcX64) {
        Copy-Item $srcX64 -Destination $destX64 -Force
        Write-Host "  Copied libSkiaSharp.dll (win-x64) from NuGet package" -ForegroundColor Green
    }

    # Copy win-x86
    $srcX86 = Join-Path $skiaPackagePath "win-x86\native\libSkiaSharp.dll"
    $destX86 = Join-Path $runtimeDest "win-x86\native\libSkiaSharp.dll"
    if (Test-Path $srcX86) {
        Copy-Item $srcX86 -Destination $destX86 -Force
        Write-Host "  Copied libSkiaSharp.dll (win-x86) from NuGet package" -ForegroundColor Green
    }

    # Copy win-arm64
    $srcArm64 = Join-Path $skiaPackagePath "win-arm64\native\libSkiaSharp.dll"
    $destArm64 = Join-Path $runtimeDest "win-arm64\native\libSkiaSharp.dll"
    if (Test-Path $srcArm64) {
        Copy-Item $srcArm64 -Destination $destArm64 -Force
        Write-Host "  Copied libSkiaSharp.dll (win-arm64) from NuGet package" -ForegroundColor Green
    }

    # Also copy to root folder for .NET to find it
    Copy-Item $srcX64 -Destination $DestPath -Force
    Write-Host "  Copied libSkiaSharp.dll to root folder" -ForegroundColor Green
}
else {
    Write-Host "  WARNING: SkiaSharp NuGet package not found at: $skiaPackagePath" -ForegroundColor Yellow
}

# ========================================
# STEP 4: Copy SQLite.Interop.dll
# ========================================
Write-Host ""
Write-Host "Step 4: Copying SQLite.Interop.dll..." -ForegroundColor Cyan

# Try to find SQLite.Interop.dll from the publish folder or packages
$sqliteInteropSrc = $null

# First, check if it's in the x64 subfolder of the publish
$sqliteInPublish = Join-Path $latestIndiLogs.FullName "x64\SQLite.Interop.dll"
if (Test-Path $sqliteInPublish) {
    $sqliteInteropSrc = $sqliteInPublish
}

# If not found, try the NuGet packages folder (correct path - going up from Installer folder)
if (-not $sqliteInteropSrc) {
    $sqlitePackagePath = "..\..\packages\Stub.System.Data.SQLite.Core.NetFramework.1.0.118.0\build\net46\x64\SQLite.Interop.dll"
    if (Test-Path $sqlitePackagePath) {
        $sqliteInteropSrc = $sqlitePackagePath
        Write-Host "  Found SQLite.Interop.dll in NuGet packages" -ForegroundColor Gray
    }
}

# If still not found, try alternative package path
if (-not $sqliteInteropSrc) {
    $sqlitePackagePath2 = "..\..\packages\System.Data.SQLite.Core.1.0.118.0\build\net46\x64\SQLite.Interop.dll"
    if (Test-Path $sqlitePackagePath2) {
        $sqliteInteropSrc = $sqlitePackagePath2
        Write-Host "  Found SQLite.Interop.dll in alternative NuGet package" -ForegroundColor Gray
    }
}

# If still not found, try absolute path
if (-not $sqliteInteropSrc) {
    $sqlitePackagePath3 = "C:\Users\yegudish\source\repos\indilogs3.0\indigo3.0\indigo3.0\packages\Stub.System.Data.SQLite.Core.NetFramework.1.0.118.0\build\net46\x64\SQLite.Interop.dll"
    if (Test-Path $sqlitePackagePath3) {
        $sqliteInteropSrc = $sqlitePackagePath3
        Write-Host "  Found SQLite.Interop.dll using absolute path" -ForegroundColor Gray
    }
}

if ($sqliteInteropSrc) {
    # Copy to root folder
    Copy-Item $sqliteInteropSrc -Destination $DestPath -Force
    Write-Host "  Copied SQLite.Interop.dll to root folder" -ForegroundColor Green

    # Also copy to x64 subfolder for compatibility
    $x64Dest = Join-Path $DestPath "x64"
    if (-not (Test-Path $x64Dest)) {
        New-Item -ItemType Directory -Path $x64Dest -Force | Out-Null
    }
    Copy-Item $sqliteInteropSrc -Destination $x64Dest -Force
    Write-Host "  Copied SQLite.Interop.dll to x64 folder" -ForegroundColor Green
}
else {
    Write-Host "  WARNING: SQLite.Interop.dll not found! DB browsing may not work." -ForegroundColor Yellow
    Write-Host "  Looked in:" -ForegroundColor Yellow
    Write-Host "    - $sqliteInPublish" -ForegroundColor Gray
    Write-Host "    - NuGet packages folder" -ForegroundColor Gray
}

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
