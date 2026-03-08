param([string]$CoverageDir = "./CoverageCheck")

$file = Get-ChildItem -Path $CoverageDir -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
if (-not $file) { Write-Error "No coverage file found"; exit 1 }

[xml]$x = Get-Content $file.FullName

$rows = @()
foreach ($pkg in $x.coverage.packages.package) {
    foreach ($cls in $pkg.classes.class) {
        $lines = $cls.lines.line
        if ($null -eq $lines) { continue }
        $lineList = @($lines)
        $lv = $lineList.Count
        if ($lv -eq 0) { continue }
        $lc = ($lineList | Where-Object { [int]$_.hits -gt 0 }).Count
        $rows += [PSCustomObject]@{
            Class     = $cls.name
            File      = [System.IO.Path]::GetFileName($cls.filename)
            Lines     = $lv
            Covered   = $lc
            Uncovered = $lv - $lc
            Pct       = [math]::Round($lc / $lv * 100, 1)
        }
    }
}

Write-Host "`n=== Top 40 classes by UNCOVERED lines ===`n"
$rows | Sort-Object Uncovered -Descending | Select-Object -First 40 | Format-Table -AutoSize

Write-Host "`n=== Classes with 0% coverage and >20 lines ===`n"
$rows | Where-Object { $_.Pct -eq 0 -and $_.Lines -gt 20 } | Sort-Object Lines -Descending | Format-Table -AutoSize

$totalLines = ($rows | Measure-Object -Property Lines -Sum).Sum
$totalCovered = ($rows | Measure-Object -Property Covered -Sum).Sum
Write-Host "Total: $totalCovered / $totalLines lines covered ($([math]::Round($totalCovered/$totalLines*100,2))%)"
