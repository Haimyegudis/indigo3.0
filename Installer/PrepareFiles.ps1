# PrepareFiles.ps1
# Prepares all files for the IndiLogs Suite installer
# Prepares IndiLogs 3.0 files for installation

param(
    [string]$IndiLogsPublishPath = "..\Indilogs 3.0\bin\Debug",
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

# Try to find IndiLogs files - check multiple locations
$latestIndiLogs = $null
$useDirectFiles = $false

# Option 1: Check if path ends with app.publish (ClickOnce publish folder)
$appFilesPath = Join-Path $IndiLogsPublishPath "Application Files"
if (Test-Path $appFilesPath) {
    $latestIndiLogs = Get-ChildItem $appFilesPath -Directory | Sort-Object Name -Descending | Select-Object -First 1
}

# Option 2: Check for app.publish subfolder inside the path
if (-not $latestIndiLogs) {
    $appPublishPath = Join-Path $IndiLogsPublishPath "app.publish\Application Files"
    if (Test-Path $appPublishPath) {
        $latestIndiLogs = Get-ChildItem $appPublishPath -Directory | Sort-Object Name -Descending | Select-Object -First 1
    }
}

# Option 3: Use direct files from bin folder
if (-not $latestIndiLogs) {
    if (Test-Path (Join-Path $IndiLogsPublishPath "IndiLogs 3.0.exe")) {
        $useDirectFiles = $true
        Write-Host "  Using direct files from: $IndiLogsPublishPath" -ForegroundColor Gray
    }
    else {
        Write-Host "ERROR: Could not find IndiLogs files at: $IndiLogsPublishPath" -ForegroundColor Red
        Write-Host "Please build IndiLogs 3.0 first!" -ForegroundColor Red
        exit 1
    }
}
else {
    Write-Host "  Found ClickOnce publish: $($latestIndiLogs.Name)" -ForegroundColor Gray
}

# Copy IndiLogs files
if ($useDirectFiles) {
    # Copy directly from bin folder (exclude pdb, xml, app.publish subfolder)
    Get-ChildItem -Path $IndiLogsPublishPath -File | Where-Object {
        $_.Extension -notin @('.pdb', '.xml') -and $_.Name -ne 'app.publish'
    } | ForEach-Object {
        Copy-Item $_.FullName -Destination $DestPath -Force
    }

    # Copy Resources folder if exists
    $resourcesPath = Join-Path $IndiLogsPublishPath "Resources"
    if (Test-Path $resourcesPath) {
        $destResources = Join-Path $DestPath "Resources"
        if (-not (Test-Path $destResources)) {
            New-Item -ItemType Directory -Path $destResources -Force | Out-Null
        }
        Copy-Item "$resourcesPath\*" -Destination $destResources -Recurse -Force
    }

    # Copy runtimes folder if exists
    $runtimesPath = Join-Path $IndiLogsPublishPath "runtimes"
    if (Test-Path $runtimesPath) {
        Copy-Item $runtimesPath -Destination $DestPath -Recurse -Force
    }

    # Copy x64 folder if exists
    $x64Path = Join-Path $IndiLogsPublishPath "x64"
    if (Test-Path $x64Path) {
        Copy-Item $x64Path -Destination $DestPath -Recurse -Force
    }

    # Copy arm64 folder if exists
    $arm64Path = Join-Path $IndiLogsPublishPath "arm64"
    if (Test-Path $arm64Path) {
        Copy-Item $arm64Path -Destination $DestPath -Recurse -Force
    }

    # Copy x86 folder if exists
    $x86Path = Join-Path $IndiLogsPublishPath "x86"
    if (Test-Path $x86Path) {
        Copy-Item $x86Path -Destination $DestPath -Recurse -Force
    }
}
else {
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
}

$indiLogsCount = (Get-ChildItem -Path $DestPath -Recurse -File).Count
Write-Host "  Copied $indiLogsCount files" -ForegroundColor Green

# ========================================
# STEP 2: Copy correct SkiaSharp libraries from NuGet packages
# ========================================
Write-Host ""
Write-Host "Step 2: Copying correct SkiaSharp libraries from NuGet packages..." -ForegroundColor Cyan

$packagesPath = "..\packages"

# Copy SkiaSharp managed DLLs (3.119.0)
$skiaManagedPath = Join-Path $packagesPath "SkiaSharp.3.119.0\lib\net462"
if (Test-Path $skiaManagedPath) {
    Copy-Item (Join-Path $skiaManagedPath "SkiaSharp.dll") -Destination $DestPath -Force
    Write-Host "  Copied SkiaSharp.dll from NuGet package" -ForegroundColor Green
}

# Copy SkiaSharp.Views.Desktop.Common
$skiaViewsCommonPath = Join-Path $packagesPath "SkiaSharp.Views.Desktop.Common.3.119.0\lib\net462"
if (Test-Path $skiaViewsCommonPath) {
    Copy-Item (Join-Path $skiaViewsCommonPath "SkiaSharp.Views.Desktop.Common.dll") -Destination $DestPath -Force
    Write-Host "  Copied SkiaSharp.Views.Desktop.Common.dll from NuGet package" -ForegroundColor Green
}

# Copy SkiaSharp.Views.WPF
$skiaViewsWpfPath = Join-Path $packagesPath "SkiaSharp.Views.WPF.3.119.0\lib\net462"
if (Test-Path $skiaViewsWpfPath) {
    Copy-Item (Join-Path $skiaViewsWpfPath "SkiaSharp.Views.WPF.dll") -Destination $DestPath -Force
    Write-Host "  Copied SkiaSharp.Views.WPF.dll from NuGet package" -ForegroundColor Green
}

# Copy native libraries
$skiaPackagePath = Join-Path $packagesPath "SkiaSharp.NativeAssets.Win32.3.119.0\runtimes"

if (Test-Path $skiaPackagePath) {
    # Copy all runtime folders (win-x64, win-x86, win-arm64)
    $runtimeDest = Join-Path $DestPath "runtimes"

    # Create runtime directories (must exist before Copy-Item)
    New-Item -ItemType Directory -Path (Join-Path $runtimeDest "win-x64\native") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $runtimeDest "win-x86\native") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $runtimeDest "win-arm64\native") -Force | Out-Null

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

# Copy HarfBuzzSharp native libraries (required by SkiaSharp)
$harfBuzzPackagePath = Join-Path $packagesPath "HarfBuzzSharp.NativeAssets.Win32.8.3.1.1\runtimes"

if (Test-Path $harfBuzzPackagePath) {
    # Copy win-x64
    $hbSrcX64 = Join-Path $harfBuzzPackagePath "win-x64\native\libHarfBuzzSharp.dll"
    if (Test-Path $hbSrcX64) {
        Copy-Item $hbSrcX64 -Destination (Join-Path $runtimeDest "win-x64\native\libHarfBuzzSharp.dll") -Force
        Write-Host "  Copied libHarfBuzzSharp.dll (win-x64) from NuGet package" -ForegroundColor Green
    }

    # Copy win-x86
    $hbSrcX86 = Join-Path $harfBuzzPackagePath "win-x86\native\libHarfBuzzSharp.dll"
    if (Test-Path $hbSrcX86) {
        Copy-Item $hbSrcX86 -Destination (Join-Path $runtimeDest "win-x86\native\libHarfBuzzSharp.dll") -Force
        Write-Host "  Copied libHarfBuzzSharp.dll (win-x86) from NuGet package" -ForegroundColor Green
    }

    # Copy win-arm64
    $hbSrcArm64 = Join-Path $harfBuzzPackagePath "win-arm64\native\libHarfBuzzSharp.dll"
    if (Test-Path $hbSrcArm64) {
        Copy-Item $hbSrcArm64 -Destination (Join-Path $runtimeDest "win-arm64\native\libHarfBuzzSharp.dll") -Force
        Write-Host "  Copied libHarfBuzzSharp.dll (win-arm64) from NuGet package" -ForegroundColor Green
    }

    # Also copy to root folder for .NET to find it
    Copy-Item $hbSrcX64 -Destination $DestPath -Force
    Write-Host "  Copied libHarfBuzzSharp.dll to root folder" -ForegroundColor Green
}
else {
    Write-Host "  WARNING: HarfBuzzSharp NuGet package not found at: $harfBuzzPackagePath" -ForegroundColor Yellow
}

# ========================================
# STEP 3: Copy SQLite.Interop.dll
# ========================================
Write-Host ""
Write-Host "Step 3: Copying SQLite.Interop.dll..." -ForegroundColor Cyan

# Try to find SQLite.Interop.dll from the publish folder or packages
$sqliteInteropSrc = $null

# First, check if it's in the x64 subfolder of the publish or build folder
if ($useDirectFiles) {
    $sqliteInPublish = Join-Path $IndiLogsPublishPath "x64\SQLite.Interop.dll"
} else {
    $sqliteInPublish = Join-Path $latestIndiLogs.FullName "x64\SQLite.Interop.dll"
}
if (Test-Path $sqliteInPublish) {
    $sqliteInteropSrc = $sqliteInPublish
    Write-Host "  Found SQLite.Interop.dll in publish folder" -ForegroundColor Gray
}

# If not found, try the NuGet packages folder (correct path - going up from Installer folder)
if (-not $sqliteInteropSrc) {
    $sqlitePackagePath = "..\packages\Stub.System.Data.SQLite.Core.NetFramework.1.0.118.0\build\net46\x64\SQLite.Interop.dll"
    if (Test-Path $sqlitePackagePath) {
        $sqliteInteropSrc = $sqlitePackagePath
        Write-Host "  Found SQLite.Interop.dll in NuGet packages (net46)" -ForegroundColor Gray
    }
}

# If still not found, try net48 folder (for .NET Framework 4.8 projects)
if (-not $sqliteInteropSrc) {
    $sqlitePackagePath2 = "..\packages\Stub.System.Data.SQLite.Core.NetFramework.1.0.118.0\build\net48\x64\SQLite.Interop.dll"
    if (Test-Path $sqlitePackagePath2) {
        $sqliteInteropSrc = $sqlitePackagePath2
        Write-Host "  Found SQLite.Interop.dll in NuGet packages (net48)" -ForegroundColor Gray
    }
}

# If still not found, try the alternative package name
if (-not $sqliteInteropSrc) {
    $sqlitePackagePath3 = "..\packages\System.Data.SQLite.Core.1.0.118.0\build\net46\x64\SQLite.Interop.dll"
    if (Test-Path $sqlitePackagePath3) {
        $sqliteInteropSrc = $sqlitePackagePath3
        Write-Host "  Found SQLite.Interop.dll in alternative NuGet package" -ForegroundColor Gray
    }
}

# If still not found, try absolute path to current project packages
if (-not $sqliteInteropSrc) {
    $sqlitePackagePath4 = "C:\Users\yegudish\source\repos\indilogs3.0\packages\Stub.System.Data.SQLite.Core.NetFramework.1.0.118.0\build\net46\x64\SQLite.Interop.dll"
    if (Test-Path $sqlitePackagePath4) {
        $sqliteInteropSrc = $sqlitePackagePath4
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
# STEP 4: Copy Visual C++ Runtime DLLs (required for SQLite.Interop.dll)
# ========================================
Write-Host ""
Write-Host "Step 4: Copying Visual C++ Runtime DLLs..." -ForegroundColor Cyan

$vcRuntimeDlls = @("vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll")
$vcCopied = 0

foreach ($dll in $vcRuntimeDlls) {
    # Check if already in destination
    $destFile = Join-Path $DestPath $dll
    if (Test-Path $destFile) {
        Write-Host "  $dll already exists" -ForegroundColor Gray
        $vcCopied++
        continue
    }

    # Check in source publish folder first
    $srcInPublish = Join-Path $IndiLogsPublishPath $dll
    if (Test-Path $srcInPublish) {
        Copy-Item $srcInPublish -Destination $DestPath -Force
        Write-Host "  Copied $dll from publish folder" -ForegroundColor Green
        $vcCopied++
        continue
    }

    # Fall back to System32
    $srcInSystem = "C:\Windows\System32\$dll"
    if (Test-Path $srcInSystem) {
        Copy-Item $srcInSystem -Destination $DestPath -Force
        Write-Host "  Copied $dll from System32" -ForegroundColor Green
        $vcCopied++
    }
    else {
        Write-Host "  WARNING: Could not find $dll" -ForegroundColor Yellow
    }
}

if ($vcCopied -lt $vcRuntimeDlls.Count) {
    Write-Host "  NOTE: Some VC++ DLLs missing. Target machines may need VC++ Redistributable installed." -ForegroundColor Yellow
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
