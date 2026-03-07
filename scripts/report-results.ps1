# IndiLogs 3.0 — Test Results Reporter
# Called by run-tests.bat and run-ci.bat
param(
    [string]$ResultsDir,
    [switch]$Coverage,
    [int]$Threshold = 60
)

# TRX Report
$trxFile = Join-Path $ResultsDir "TestResults.trx"
if (Test-Path $trxFile) {
    [xml]$trx = Get-Content $trxFile
    $c = $trx.TestRun.ResultSummary.Counters
    $outcome = $trx.TestRun.ResultSummary.outcome

    Write-Host ""
    Write-Host "  --- Test Results ---"
    Write-Host "  +----------+-------+"
    Write-Host ("  | Total    | {0,5} |" -f $c.total)

    $passColor = if ([int]$c.passed -gt 0) { "Green" } else { "Yellow" }
    Write-Host ("  | Passed   | {0,5} |" -f $c.passed) -ForegroundColor $passColor

    $failColor = if ([int]$c.failed -gt 0) { "Red" } else { "Green" }
    Write-Host ("  | Failed   | {0,5} |" -f $c.failed) -ForegroundColor $failColor

    Write-Host ("  | Skipped  | {0,5} |" -f $c.notExecuted)
    Write-Host "  +----------+-------+"

    $outcomeColor = if ($outcome -eq "Completed") { "Green" } else { "Red" }
    Write-Host "  Outcome: $outcome" -ForegroundColor $outcomeColor
}

# Coverage Report
if ($Coverage) {
    $coverageFile = Get-ChildItem -Path $ResultsDir -Recurse -Filter "coverage.cobertura.xml" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($coverageFile) {
        [xml]$cov = Get-Content $coverageFile.FullName
        $lineRate = [math]::Round([double]$cov.coverage.'line-rate' * 100, 2)
        $branchRate = [math]::Round([double]$cov.coverage.'branch-rate' * 100, 2)
        $linesCovered = $cov.coverage.'lines-covered'
        $linesValid = $cov.coverage.'lines-valid'

        Write-Host ""
        Write-Host "  --- Code Coverage ---"
        Write-Host "  File: $($coverageFile.FullName)"
        Write-Host "  Line coverage:   $lineRate% ($linesCovered / $linesValid lines)"
        Write-Host "  Branch coverage: $branchRate%"
        Write-Host ""

        if ($lineRate -lt $Threshold) {
            Write-Host "  FAIL: Coverage $lineRate% is below threshold $Threshold%" -ForegroundColor Red
            exit 1
        } else {
            Write-Host "  PASS: Coverage $lineRate% meets $Threshold% threshold" -ForegroundColor Green
        }
    } else {
        Write-Host ""
        Write-Host "  FAIL: No coverage file found in $ResultsDir" -ForegroundColor Red
        exit 1
    }
}
