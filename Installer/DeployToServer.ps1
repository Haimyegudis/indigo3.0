# DeployToServer.ps1
# Deploys the published IndiLogs single-file exe to the corporate network share
# and updates version.txt so all users are prompted to update.
#
# USAGE:
#   1. Build:  dotnet publish "Indilogs 3.0\Indilogs 3.0.csproj" -c Release -r win-x64 --self-contained true
#   2. Deploy: .\DeployToServer.ps1 -Version "1.0.0.11"
#
# The version must match <AssemblyVersion> in Indilogs 3.0.csproj

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [string]$PublishFolder   = "..\Indilogs 3.0\bin\Release\net10.0-windows\win-x64\publish",
    [string]$ServerFolder    = "\\iihome.inr.rd.hpicorp.net\softwareqa$\QA-Utils\Indilogs3.0"
)

$exeName    = "IndiLogs 3.0.exe"
$sourceExe  = Join-Path $PublishFolder $exeName

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "IndiLogs Deploy to Server (Single-File)" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ---- Step 1: Validate version format ----
Write-Host "Step 1: Validating version format..." -ForegroundColor Cyan
try {
    $v = [System.Version]::Parse($Version)
    Write-Host "  Version OK: $v" -ForegroundColor Green
} catch {
    Write-Host "ERROR: Version '$Version' is not a valid format." -ForegroundColor Red
    Write-Host "Expected format: X.X.X.X  (e.g. 1.0.0.11)" -ForegroundColor Yellow
    pause
    exit 1
}

# ---- Step 2: Check published exe exists ----
Write-Host ""
Write-Host "Step 2: Checking published exe..." -ForegroundColor Cyan
if (-not (Test-Path $sourceExe)) {
    Write-Host "ERROR: Published exe not found at: $sourceExe" -ForegroundColor Red
    Write-Host "Run 'dotnet publish' first!" -ForegroundColor Red
    pause
    exit 1
}
$exeInfo = Get-Item $sourceExe
Write-Host "  Found: $($exeInfo.Name) ($([Math]::Round($exeInfo.Length / 1MB, 2)) MB)" -ForegroundColor Green

# ---- Step 3: Check server accessibility ----
Write-Host ""
Write-Host "Step 3: Checking server access..." -ForegroundColor Cyan
if (-not (Test-Path $ServerFolder)) {
    Write-Host "ERROR: Cannot reach server folder:" -ForegroundColor Red
    Write-Host "  $ServerFolder" -ForegroundColor Red
    Write-Host ""
    Write-Host "Possible causes:" -ForegroundColor Yellow
    Write-Host "  - Not connected to HP network / VPN" -ForegroundColor Yellow
    Write-Host "  - No write permission to softwareqa$" -ForegroundColor Yellow
    pause
    exit 1
}
Write-Host "  Server reachable: YES" -ForegroundColor Green

# ---- Step 4: Remove old IndiLogs*.exe files from server ----
Write-Host ""
Write-Host "Step 4: Cleaning old files from server..." -ForegroundColor Cyan
$oldFiles = Get-ChildItem -Path $ServerFolder -Filter "IndiLogs*.exe" -ErrorAction SilentlyContinue
if ($oldFiles.Count -eq 0) {
    Write-Host "  No old files found." -ForegroundColor Gray
} else {
    foreach ($f in $oldFiles) {
        Write-Host "  Removing: $($f.Name)" -ForegroundColor Yellow
        Remove-Item $f.FullName -Force
    }
    Write-Host "  Removed $($oldFiles.Count) old file(s)." -ForegroundColor Green
}

# ---- Step 5: Copy new exe to server (versioned filename) ----
Write-Host ""
Write-Host "Step 5: Copying exe to server..." -ForegroundColor Cyan
$destName = "IndiLogs3.0_$Version.exe"
$destPath = Join-Path $ServerFolder $destName
Copy-Item $sourceExe -Destination $destPath -Force
Write-Host "  Copied as: $destName" -ForegroundColor Green

# ---- Step 5b: Copy appsettings.json alongside (for reference / first-time users) ----
$sourceSettings = Join-Path $PublishFolder "appsettings.json"
if (Test-Path $sourceSettings) {
    $destSettings = Join-Path $ServerFolder "appsettings.json"
    Copy-Item $sourceSettings -Destination $destSettings -Force
    Write-Host "  Copied: appsettings.json" -ForegroundColor Green
}

# ---- Step 6: Write version.txt ----
Write-Host ""
Write-Host "Step 6: Updating version.txt on server..." -ForegroundColor Cyan
$versionFilePath = Join-Path $ServerFolder "version.txt"
Set-Content -Path $versionFilePath -Value $Version -Encoding ASCII -NoNewline
Write-Host "  version.txt contents: $Version" -ForegroundColor Green

# ---- Done ----
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "SUCCESS! IndiLogs $Version deployed." -ForegroundColor Green
Write-Host ""
Write-Host "  Server folder : $ServerFolder" -ForegroundColor White
Write-Host "  Exe file      : $destName" -ForegroundColor White
Write-Host "  version.txt   : $Version" -ForegroundColor White
Write-Host ""
Write-Host "Users running a version < $Version will be prompted to" -ForegroundColor Yellow
Write-Host "update the next time they start IndiLogs." -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
pause
