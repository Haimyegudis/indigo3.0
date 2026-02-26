# CopyVCRuntime.ps1
# Copies Visual C++ Runtime DLLs required for SQLite.Interop.dll
# Run this script to include VC++ runtime with your published files

param(
    [string]$DestPath = "..\Indilogs 3.0\bin\Debug"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Copying Visual C++ Runtime DLLs" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Common locations for VC++ runtime DLLs
$vcRuntimePaths = @(
    "C:\Windows\System32",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Redist\MSVC\14.38.33135\x64\Microsoft.VC143.CRT",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Redist\MSVC\14.38.33135\x64\Microsoft.VC143.CRT",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Redist\MSVC\14.38.33135\x64\Microsoft.VC143.CRT",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\VC\Redist\MSVC\14.29.30133\x64\Microsoft.VC142.CRT",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\VC\Redist\MSVC\14.29.30133\x64\Microsoft.VC142.CRT"
)

# Required DLLs for SQLite.Interop.dll
$requiredDlls = @(
    "vcruntime140.dll",
    "vcruntime140_1.dll",
    "msvcp140.dll"
)

$copiedCount = 0

foreach ($dll in $requiredDlls) {
    $found = $false

    # First check if already in destination
    $destFile = Join-Path $DestPath $dll
    if (Test-Path $destFile) {
        Write-Host "  $dll already exists in destination" -ForegroundColor Gray
        $copiedCount++
        continue
    }

    # Search for the DLL
    foreach ($searchPath in $vcRuntimePaths) {
        $srcFile = Join-Path $searchPath $dll
        if (Test-Path $srcFile) {
            Copy-Item $srcFile -Destination $DestPath -Force
            Write-Host "  Copied $dll from $searchPath" -ForegroundColor Green
            $found = $true
            $copiedCount++
            break
        }
    }

    if (-not $found) {
        Write-Host "  WARNING: Could not find $dll" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Copied $copiedCount of $($requiredDlls.Count) VC++ runtime DLLs" -ForegroundColor Green
Write-Host ""
Write-Host "Alternative: Install VC++ Redistributable on target machine:" -ForegroundColor Yellow
Write-Host "  https://aka.ms/vs/17/release/vc_redist.x64.exe" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
