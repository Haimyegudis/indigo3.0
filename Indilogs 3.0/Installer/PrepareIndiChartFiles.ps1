# PrepareIndiChartFiles.ps1
# This script prepares IndiChart.UI files for the installer
# It copies the ClickOnce published files and removes .deploy extensions

param(
    [string]$SourcePath = "..\IndiChartSuite\publish\Application Files\IndiChart.UI_1_0_0_0",
    [string]$DestPath = ".\IndiChartFiles"
)

Write-Host "Preparing IndiChart.UI files for installer..." -ForegroundColor Cyan

# Create destination folder
if (Test-Path $DestPath) {
    Remove-Item $DestPath -Recurse -Force
}
New-Item -ItemType Directory -Path $DestPath -Force | Out-Null

# Copy all files
Write-Host "Copying files from: $SourcePath" -ForegroundColor Yellow
Get-ChildItem -Path $SourcePath -Recurse | ForEach-Object {
    $relativePath = $_.FullName.Substring((Get-Item $SourcePath).FullName.Length + 1)
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

# Rename .deploy files (remove .deploy extension)
Write-Host "Removing .deploy extensions..." -ForegroundColor Yellow
Get-ChildItem -Path $DestPath -Recurse -Filter "*.deploy" | ForEach-Object {
    $newName = $_.FullName -replace '\.deploy$', ''
    Rename-Item $_.FullName -NewName $newName -Force
    Write-Host "  Renamed: $($_.Name) -> $(Split-Path $newName -Leaf)" -ForegroundColor Gray
}

# Remove manifest files (not needed for standalone installation)
Write-Host "Removing manifest files..." -ForegroundColor Yellow
Get-ChildItem -Path $DestPath -Recurse -Filter "*.manifest" | Remove-Item -Force

Write-Host "`nDone! Files are ready in: $DestPath" -ForegroundColor Green
Write-Host "You can now run Inno Setup with IndiLogsSuite.iss" -ForegroundColor Green
