# DeployToServer.ps1
# Deploys the built IndiLogs installer to the corporate network share
# and updates version.txt so all users are prompted to update.
#
# USAGE:
#   Run AFTER BuildInstaller.bat has succeeded.
#   .\DeployToServer.ps1 -Version "1.0.0.7"
#
# The version must match AssemblyVersion in:
#   Indilogs 3.0\Properties\AssemblyInfo.cs

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [string]$InstallerSource = ".\Output\IndiLogsSuite_Setup.exe",
    [string]$ServerFolder    = "\\iihome.inr.rd.hpicorp.net\softwareqa$\QA-Utils\Indilogs3.0"
)

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "IndiLogs Deploy to Server" -ForegroundColor Cyan
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
    Write-Host "Expected format: X.X.X.X  (e.g. 1.0.0.7)" -ForegroundColor Yellow
    pause
    exit 1
}

# ---- Step 2: Check installer file exists ----
Write-Host ""
Write-Host "Step 2: Checking installer file..." -ForegroundColor Cyan
if (-not (Test-Path $InstallerSource)) {
    Write-Host "ERROR: Installer not found at: $InstallerSource" -ForegroundColor Red
    Write-Host "Run BuildInstaller.bat first!" -ForegroundColor Red
    pause
    exit 1
}
$installerInfo = Get-Item $InstallerSource
Write-Host "  Found: $($installerInfo.Name) ($([Math]::Round($installerInfo.Length / 1MB, 2)) MB)" -ForegroundColor Green

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

# ---- Step 4: Remove old IndiLogs*.exe installers from server ----
Write-Host ""
Write-Host "Step 4: Cleaning old installers from server..." -ForegroundColor Cyan
$oldFiles = Get-ChildItem -Path $ServerFolder -Filter "IndiLogs*.exe" -ErrorAction SilentlyContinue
if ($oldFiles.Count -eq 0) {
    Write-Host "  No old installers found." -ForegroundColor Gray
} else {
    foreach ($f in $oldFiles) {
        Write-Host "  Removing: $($f.Name)" -ForegroundColor Yellow
        Remove-Item $f.FullName -Force
    }
    Write-Host "  Removed $($oldFiles.Count) old installer(s)." -ForegroundColor Green
}

# ---- Step 5: Copy new installer to server (versioned filename) ----
Write-Host ""
Write-Host "Step 5: Copying installer to server..." -ForegroundColor Cyan
$destName = "IndiLogsSetup_$Version.exe"
$destPath  = Join-Path $ServerFolder $destName
Copy-Item $InstallerSource -Destination $destPath -Force
Write-Host "  Copied as: $destName" -ForegroundColor Green

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
Write-Host "  Installer file: $destName" -ForegroundColor White
Write-Host "  version.txt   : $Version" -ForegroundColor White
Write-Host ""
Write-Host "Users running a version < $Version will be prompted to" -ForegroundColor Yellow
Write-Host "update the next time they start IndiLogs." -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
pause
