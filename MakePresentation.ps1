# IndiLogs 3.0 — Technical Presentation Generator
# Generates a .pptx using System.IO.Packaging (no external dependencies)

param(
    [string]$OutputPath = "$PSScriptRoot\IndiLogs3_Technical_Presentation.pptx"
)

Add-Type -AssemblyName WindowsBase  # System.IO.Packaging

function Write-Part {
    param($package, [string]$uri, [string]$contentType, [string]$xml)
    $partUri  = [System.IO.Packaging.PackUriHelper]::CreatePartUri((New-Object System.Uri($uri, [System.UriKind]::Relative)))
    $part     = $package.CreatePart($partUri, $contentType, [System.IO.Packaging.CompressionOption]::Normal)
    $stream   = $part.GetStream([System.IO.FileMode]::Create)
    $sw       = New-Object System.IO.StreamWriter($stream, [System.Text.Encoding]::UTF8)
    $sw.Write($xml)
    $sw.Flush(); $sw.Close(); $stream.Close()
    return $part
}

function New-Pptx {
    param([string]$Path, [array]$Slides)

    if (Test-Path $Path) { Remove-Item $Path -Force }

    # Colors
    $bgDark = "0D1B2A"; $bgMed = "1B2838"; $accent = "3B82F6"; $accentG = "60A5FA"
    $white = "FFFFFF"; $gray = "94A3B8"; $green = "10B981"; $yellow = "F59E0B"; $red = "EF4444"
    $slideW = 12192000; $slideH = 6858000

    $package = [System.IO.Packaging.Package]::Open($Path, [System.IO.FileMode]::Create)

    # --- Theme ---
    $themeXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="IndiLogs">
  <a:themeElements>
    <a:clrScheme name="IndiLogs">
      <a:dk1><a:srgbClr val="$bgDark"/></a:dk1><a:lt1><a:srgbClr val="$white"/></a:lt1>
      <a:dk2><a:srgbClr val="$bgMed"/></a:dk2><a:lt2><a:srgbClr val="$gray"/></a:lt2>
      <a:accent1><a:srgbClr val="$accent"/></a:accent1><a:accent2><a:srgbClr val="$green"/></a:accent2>
      <a:accent3><a:srgbClr val="$yellow"/></a:accent3><a:accent4><a:srgbClr val="$red"/></a:accent4>
      <a:accent5><a:srgbClr val="$accentG"/></a:accent5><a:accent6><a:srgbClr val="$gray"/></a:accent6>
      <a:hlink><a:srgbClr val="$accent"/></a:hlink><a:folHlink><a:srgbClr val="$accentG"/></a:folHlink>
    </a:clrScheme>
    <a:fontScheme name="IndiLogs">
      <a:majorFont><a:latin typeface="Segoe UI Semibold"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont>
      <a:minorFont><a:latin typeface="Segoe UI"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont>
    </a:fontScheme>
    <a:fmtScheme name="IndiLogs">
      <a:fillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:fillStyleLst>
      <a:lnStyleLst><a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln><a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln><a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln></a:lnStyleLst>
      <a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>
      <a:bgFillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:bgFillStyleLst>
    </a:fmtScheme>
  </a:themeElements>
</a:theme>
"@
    $themePart = Write-Part $package "/ppt/theme/theme1.xml" "application/vnd.openxmlformats-officedocument.theme+xml" $themeXml

    # --- Slide Layout ---
    $slXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sldLayout xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" type="blank">
  <p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld>
</p:sldLayout>
"@
    $slPart = Write-Part $package "/ppt/slideLayouts/slideLayout1.xml" "application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml" $slXml

    # --- Slide Master ---
    $smXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sldMaster xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <p:cSld>
    <p:bg><p:bgPr><a:solidFill><a:srgbClr val="$bgDark"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>
    <p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree>
  </p:cSld>
  <p:sldLayoutIdLst><p:sldLayoutId id="2147483649" r:id="rId1"/></p:sldLayoutIdLst>
</p:sldMaster>
"@
    $smPart = Write-Part $package "/ppt/slideMasters/slideMaster1.xml" "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml" $smXml

    # Slide Master relationships
    $slPartUri = [System.IO.Packaging.PackUriHelper]::CreatePartUri((New-Object System.Uri("/ppt/slideLayouts/slideLayout1.xml", [System.UriKind]::Relative)))
    $themePartUri = [System.IO.Packaging.PackUriHelper]::CreatePartUri((New-Object System.Uri("/ppt/theme/theme1.xml", [System.UriKind]::Relative)))
    $smPart.CreateRelationship($slPartUri, [System.IO.Packaging.TargetMode]::Internal, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout", "rId1") | Out-Null
    $smPart.CreateRelationship($themePartUri, [System.IO.Packaging.TargetMode]::Internal, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme", "rId2") | Out-Null

    # Slide Layout -> Slide Master relationship
    $smPartUri = [System.IO.Packaging.PackUriHelper]::CreatePartUri((New-Object System.Uri("/ppt/slideMasters/slideMaster1.xml", [System.UriKind]::Relative)))
    $slPart.CreateRelationship($smPartUri, [System.IO.Packaging.TargetMode]::Internal, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster", "rId1") | Out-Null

    # --- Create Slides ---
    $slideParts = @()
    for ($i = 0; $i -lt $Slides.Count; $i++) {
        $slide    = $Slides[$i]
        $slideNum = $i + 1
        $shapesXml = ""
        $shapeId   = 2

        # Background
        $bgXml = "<p:bg><p:bgPr><a:solidFill><a:srgbClr val=`"$bgDark`"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>"

        # Accent bar
        $shapesXml += @"
<p:sp><p:nvSpPr><p:cNvPr id="$shapeId" name="Bar"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
<p:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="$slideW" cy="80000"/></a:xfrm>
<a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val="$accent"/></a:solidFill><a:ln><a:noFill/></a:ln></p:spPr>
<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang="en-US"/></a:p></p:txBody></p:sp>
"@
        $shapeId++

        # Title
        if ($slide.Title) {
            $tsz = if ($slide.TitleSize) { $slide.TitleSize } else { 3200 }
            $ty  = if ($slide.TitleY) { $slide.TitleY } else { 200000 }
            $tc  = if ($slide.TitleColor) { $slide.TitleColor } else { $white }
            $tt  = [System.Security.SecurityElement]::Escape($slide.Title)
            $shapesXml += @"
<p:sp><p:nvSpPr><p:cNvPr id="$shapeId" name="Title"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
<p:spPr><a:xfrm><a:off x="457200" y="$ty"/><a:ext cx="11277600" cy="600000"/></a:xfrm>
<a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></p:spPr>
<p:txBody><a:bodyPr wrap="square" anchor="t"/><a:lstStyle/>
<a:p><a:pPr algn="l"/><a:r><a:rPr lang="en-US" sz="$tsz" b="1" dirty="0"><a:solidFill><a:srgbClr val="$tc"/></a:solidFill><a:latin typeface="Segoe UI Semibold"/></a:rPr><a:t>$tt</a:t></a:r></a:p>
</p:txBody></p:sp>
"@
            $shapeId++
        }

        # Subtitle
        if ($slide.Subtitle) {
            $sy = if ($slide.SubtitleY) { $slide.SubtitleY } else { 820000 }
            $st = [System.Security.SecurityElement]::Escape($slide.Subtitle)
            $shapesXml += @"
<p:sp><p:nvSpPr><p:cNvPr id="$shapeId" name="Sub"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
<p:spPr><a:xfrm><a:off x="457200" y="$sy"/><a:ext cx="11277600" cy="400000"/></a:xfrm>
<a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></p:spPr>
<p:txBody><a:bodyPr wrap="square" anchor="t"/><a:lstStyle/>
<a:p><a:pPr algn="l"/><a:r><a:rPr lang="en-US" sz="1800" dirty="0"><a:solidFill><a:srgbClr val="$gray"/></a:solidFill><a:latin typeface="Segoe UI"/></a:rPr><a:t>$st</a:t></a:r></a:p>
</p:txBody></p:sp>
"@
            $shapeId++
        }

        # Bullet list
        if ($slide.Bullets) {
            $by = if ($slide.BulletY) { $slide.BulletY } else { 1300000 }
            $bh = if ($slide.BulletH) { $slide.BulletH } else { 5200000 }
            $bs = if ($slide.BulletSize) { $slide.BulletSize } else { 1600 }
            $bx = if ($slide.BulletX) { $slide.BulletX } else { 457200 }
            $bw = if ($slide.BulletW) { $slide.BulletW } else { 11277600 }
            $bp = ""
            foreach ($b in $slide.Bullets) {
                $lvl = 0; $txt = $b; $bc = $white; $bsz = $bs
                if ($b -match "^\t\t") { $lvl = 2; $txt = $b.TrimStart("`t"); $bsz = [int]($bs * 0.85); $bc = $gray }
                elseif ($b -match "^\t") { $lvl = 1; $txt = $b.TrimStart("`t"); $bsz = [int]($bs * 0.92); $bc = "CBD5E1" }
                if ([string]::IsNullOrWhiteSpace($txt)) {
                    $bp += "<a:p><a:pPr><a:buNone/></a:pPr><a:r><a:rPr lang=`"en-US`" sz=`"800`"/><a:t> </a:t></a:r></a:p>`n"
                    continue
                }
                $runs = ""
                if ($txt -match '\*\*(.+?)\*\*(.*)') {
                    $bold = [System.Security.SecurityElement]::Escape($Matches[1])
                    $rest = [System.Security.SecurityElement]::Escape($Matches[2])
                    $runs = "<a:r><a:rPr lang=`"en-US`" sz=`"$bsz`" b=`"1`" dirty=`"0`"><a:solidFill><a:srgbClr val=`"$accent`"/></a:solidFill><a:latin typeface=`"Segoe UI`"/></a:rPr><a:t>$bold</a:t></a:r>"
                    if ($rest) { $runs += "<a:r><a:rPr lang=`"en-US`" sz=`"$bsz`" dirty=`"0`"><a:solidFill><a:srgbClr val=`"$bc`"/></a:solidFill><a:latin typeface=`"Segoe UI`"/></a:rPr><a:t>$rest</a:t></a:r>" }
                } else {
                    $esc = [System.Security.SecurityElement]::Escape($txt)
                    $runs = "<a:r><a:rPr lang=`"en-US`" sz=`"$bsz`" dirty=`"0`"><a:solidFill><a:srgbClr val=`"$bc`"/></a:solidFill><a:latin typeface=`"Segoe UI`"/></a:rPr><a:t>$esc</a:t></a:r>"
                }
                $mL = 342900 + ($lvl * 342900)
                $bp += "<a:p><a:pPr lvl=`"$lvl`" marL=`"$mL`" indent=`"-285750`"><a:buFont typeface=`"Arial`"/><a:buChar char=`"&#x2022;`"/></a:pPr>$runs</a:p>`n"
            }
            $shapesXml += @"
<p:sp><p:nvSpPr><p:cNvPr id="$shapeId" name="Bullets"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
<p:spPr><a:xfrm><a:off x="$bx" y="$by"/><a:ext cx="$bw" cy="$bh"/></a:xfrm>
<a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></p:spPr>
<p:txBody><a:bodyPr wrap="square" anchor="t" lIns="91440" tIns="45720" rIns="91440" bIns="45720"/><a:lstStyle/>
$bp</p:txBody></p:sp>
"@
            $shapeId++
        }

        # Two-column bullets
        if ($slide.LeftBullets -and $slide.RightBullets) {
            $colY = if ($slide.ColY) { $slide.ColY } else { 1300000 }
            $colH = if ($slide.ColH) { $slide.ColH } else { 5200000 }
            $cbs  = if ($slide.ColBulletSize) { $slide.ColBulletSize } else { 1400 }
            foreach ($side in @("Left","Right")) {
                $items = if ($side -eq "Left") { $slide.LeftBullets } else { $slide.RightBullets }
                $cx = if ($side -eq "Left") { 457200 } else { 6200000 }
                $cw = 5500000
                $hdr = if ($side -eq "Left") { $slide.LeftHeader } else { $slide.RightHeader }
                $cp = ""
                if ($hdr) {
                    $eh = [System.Security.SecurityElement]::Escape($hdr)
                    $cp += "<a:p><a:pPr algn=`"l`"/><a:r><a:rPr lang=`"en-US`" sz=`"1800`" b=`"1`" dirty=`"0`"><a:solidFill><a:srgbClr val=`"$accent`"/></a:solidFill><a:latin typeface=`"Segoe UI Semibold`"/></a:rPr><a:t>$eh</a:t></a:r></a:p>`n"
                }
                foreach ($item in $items) {
                    if ([string]::IsNullOrWhiteSpace($item)) {
                        $cp += "<a:p><a:pPr><a:buNone/></a:pPr><a:r><a:rPr lang=`"en-US`" sz=`"800`"/><a:t> </a:t></a:r></a:p>`n"
                        continue
                    }
                    $ei = [System.Security.SecurityElement]::Escape($item)
                    $cp += "<a:p><a:pPr marL=`"228600`" indent=`"-228600`"><a:buFont typeface=`"Arial`"/><a:buChar char=`"&#x2022;`"/></a:pPr><a:r><a:rPr lang=`"en-US`" sz=`"$cbs`" dirty=`"0`"><a:solidFill><a:srgbClr val=`"$white`"/></a:solidFill><a:latin typeface=`"Segoe UI`"/></a:rPr><a:t>$ei</a:t></a:r></a:p>`n"
                }
                $shapesXml += @"
<p:sp><p:nvSpPr><p:cNvPr id="$shapeId" name="${side}Col"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
<p:spPr><a:xfrm><a:off x="$cx" y="$colY"/><a:ext cx="$cw" cy="$colH"/></a:xfrm>
<a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></p:spPr>
<p:txBody><a:bodyPr wrap="square" anchor="t" lIns="91440" tIns="45720"/><a:lstStyle/>
$cp</p:txBody></p:sp>
"@
                $shapeId++
            }
        }

        # Slide number
        $shapesXml += @"
<p:sp><p:nvSpPr><p:cNvPr id="$shapeId" name="Num"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
<p:spPr><a:xfrm><a:off x="11400000" y="6500000"/><a:ext cx="600000" cy="300000"/></a:xfrm>
<a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></p:spPr>
<p:txBody><a:bodyPr/><a:lstStyle/>
<a:p><a:pPr algn="r"/><a:r><a:rPr lang="en-US" sz="1000" dirty="0"><a:solidFill><a:srgbClr val="$gray"/></a:solidFill><a:latin typeface="Segoe UI"/></a:rPr><a:t>$slideNum / $($Slides.Count)</a:t></a:r></a:p>
</p:txBody></p:sp>
"@

        $slideXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<p:cSld>$bgXml<p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>
$shapesXml
</p:spTree></p:cSld>
</p:sld>
"@
        $sPart = Write-Part $package "/ppt/slides/slide$slideNum.xml" "application/vnd.openxmlformats-officedocument.presentationml.slide+xml" $slideXml
        $sPart.CreateRelationship($slPartUri, [System.IO.Packaging.TargetMode]::Internal, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout", "rId1") | Out-Null
        $slideParts += $sPart
    }

    # --- Presentation ---
    $slideIdList = ""
    for ($i = 0; $i -lt $Slides.Count; $i++) {
        $sid = 256 + $i; $rid = "rId$($i + 3)"
        $slideIdList += "    <p:sldId id=`"$sid`" r:id=`"$rid`"/>`n"
    }
    $presXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" saveSubsetFonts="1">
  <p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rId1"/></p:sldMasterIdLst>
  <p:sldIdLst>
$slideIdList  </p:sldIdLst>
  <p:sldSz cx="$slideW" cy="$slideH"/>
  <p:notesSz cx="$slideH" cy="$slideW"/>
</p:presentation>
"@
    $presPart = Write-Part $package "/ppt/presentation.xml" "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml" $presXml

    # Presentation relationships
    $presPart.CreateRelationship($smPartUri, [System.IO.Packaging.TargetMode]::Internal, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster", "rId1") | Out-Null
    $presPart.CreateRelationship($themePartUri, [System.IO.Packaging.TargetMode]::Internal, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme", "rId2") | Out-Null
    for ($i = 0; $i -lt $slideParts.Count; $i++) {
        $rid = "rId$($i + 3)"
        $slidePartUri = [System.IO.Packaging.PackUriHelper]::CreatePartUri((New-Object System.Uri("/ppt/slides/slide$($i+1).xml", [System.UriKind]::Relative)))
        $presPart.CreateRelationship($slidePartUri, [System.IO.Packaging.TargetMode]::Internal, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide", $rid) | Out-Null
    }

    # Package relationship
    $presPartUri = [System.IO.Packaging.PackUriHelper]::CreatePartUri((New-Object System.Uri("/ppt/presentation.xml", [System.UriKind]::Relative)))
    $package.CreateRelationship($presPartUri, [System.IO.Packaging.TargetMode]::Internal, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "rId1") | Out-Null

    $package.Close()
    Write-Host "Created: $Path"
}

# ============================================================
# SLIDE CONTENT — 21 slides
# ============================================================
$slides = @(

    # SLIDE 1: Title
    @{
        Title = "IndiLogs 3.0"; TitleSize = 4800; TitleY = 2000000; TitleColor = "FFFFFF"
        Subtitle = "Technical Architecture and Feature Overview"; SubtitleY = 2800000
        Bullets = @(
            "**WPF Desktop Application** for HP Indigo Digital Press log analysis",
            "**.NET 10.0** | C# | MVVM | 200+ source files | 291 unit tests",
            "**Single-file self-contained** deployment with Inno Setup installer"
        )
        BulletY = 3600000; BulletSize = 1800
    },

    # SLIDE 2: Agenda
    @{
        Title = "Agenda"
        Bullets = @(
            "**1.** Application Overview and Features",
            "**2.** Project Structure and Folder Organization",
            "**3.** Architecture: MVVM Pattern and Dependency Injection",
            "**4.** Frontend: Views, Controls and UX Design",
            "**5.** Backend: Services Layer",
            "**6.** Models and Data Structures",
            "**7.** ViewModels: The Application Brain",
            "**8.** Plugin System and Extensibility",
            "**9.** Security Hardening",
            "**10.** Performance Optimizations",
            "**11.** Build, CI/CD and Deployment",
            "**12.** Testing Strategy",
            "**13.** Summary and Key Strengths"
        )
        BulletSize = 1600
    },

    # SLIDE 3: Application Overview
    @{
        Title = "1. Application Overview"; Subtitle = "What IndiLogs 3.0 does and who it serves"
        Bullets = @(
            "**Purpose:** Analyze, search, and visualize log data from HP Indigo digital presses",
            "**Users:** Field engineers, support teams, R&D diagnostics",
            "**Input formats:** ZIP archives (S4-5 binary, S6 text), .log files, CSV, JSON, XML",
            "**13 integrated tabs:** PLC, APP, Filtered, Screenshots, Globals, Systab, Events, Terminals, Timeline, Charts, CPR, Step Recorder, Different Logs",
            "**Key workflows:**",
            "`tLoad ZIP/log files with automatic format detection",
            "`tFilter and search across millions of log entries",
            "`tVisualize signals on interactive charts with playback",
            "`tRun scheduled searches across network locations",
            "`tExport to CSV/Excel with customizable presets",
            "`tSave and share investigation cases with annotations"
        )
        BulletSize = 1500
    },

    # SLIDE 4: Feature Highlights
    @{
        Title = "Feature Highlights"; Subtitle = "Core capabilities at a glance"
        LeftHeader = "Analysis and Search"; RightHeader = "Visualization and Export"
        LeftBullets = @(
            "Advanced filter builder (AND/OR/NOT tree)",
            "Global Grep: search across network locations",
            "State failure analysis with step-by-step reports",
            "Log comparison / diff engine",
            "Regex search with 2-second timeout (ReDoS safe)",
            "Logger hierarchy tree with selective filtering",
            "Thread filtering and time-range selection",
            "Case files with annotations and bookmarks"
        )
        RightBullets = @(
            "Signal charts (Line, Gantt, Thread views)",
            "Visual timeline with state transitions",
            "Chart playback with time scrubbing",
            "CSV/Excel export with forward-fill",
            "HTML report generation for scheduled searches",
            "Email notifications with SMTP integration",
            "Windows Task Scheduler integration",
            "Dark theme UI with configurable fonts"
        )
        ColBulletSize = 1300
    },

    # SLIDE 5: Project Structure
    @{
        Title = "2. Project Structure"; Subtitle = "Solution organization and folder layout"
        Bullets = @(
            "**Solution:** 3 projects in Indilogs 3.0.sln",
            "`tIndilogs 3.0 -- Main WPF application (200+ files)",
            "`tIndiLogs.PluginAPI -- Plugin contract library (ILogFilePlugin interface)",
            "`tIndiLogs.Tests -- xUnit test project (17 test classes, 291 tests)",
            "",
            "**Main application folders:**",
            "`tViews/ (39 files) -- XAML windows and dialogs",
            "`tViewModels/ (29 files) -- Application logic and composition",
            "`tServices/ (60 files) -- Data loading, parsing, export, search",
            "`tControls/ (25 files) -- Custom WPF controls (charts, grids, timeline)",
            "`tModels/ (22+ files) -- Data structures and domain objects",
            "`tConverters/ (11 files) -- XAML value converters for data binding",
            "",
            "**Support:**",
            "`tInstaller/ -- Inno Setup scripts, PowerShell build/deploy scripts",
            "`t.github/workflows/ -- CI/CD pipeline (build, test, coverage)"
        )
        BulletSize = 1350
    },

    # SLIDE 6: Architecture
    @{
        Title = "3. Architecture: MVVM + Manual DI"; Subtitle = "Clean separation of concerns without framework bloat"
        Bullets = @(
            "**MVVM Pattern** -- Views bind to ViewModels via DataContext, no code-behind logic",
            "**Manual Dependency Injection** -- Bootstrapper.cs is the composition root",
            "`tDictionary-based singleton registry (no external DI container needed)",
            "`tExplicit registration order with typed resolution: Bootstrapper.Resolve<T>()",
            "`tConfigure() at startup, Shutdown() disposes all singletons",
            "",
            "**Registered services (in startup order):**",
            "`tPluginLoader -> DialogService -> LogFileService -> LogColoringService",
            "`tCsvExportService -> DefaultConfigurationService -> WindowManager",
            "`tViewFactory -> MainViewModel (depends on all services above)",
            "",
            "**12 service interfaces** in Services/Interfaces/:",
            "`tILogFileService, IDialogService, IViewFactory, IPluginLoader,",
            "`tILogColoringService, ICsvExportService, IDefaultConfigurationService,",
            "`tIWindowManager, ITabTearOffManager, IUpdateService, IGlobalGrepService"
        )
        BulletSize = 1300
    },

    # SLIDE 7: Composition
    @{
        Title = "ViewModel Composition"; Subtitle = "MainViewModel delegates to 10+ specialized child ViewModels"
        Bullets = @(
            "**MainViewModel** is the orchestrator -- owns all child VMs:",
            "`tSessionVM (LogSessionViewModel) -- Session lifecycle, file loading",
            "`tFilterVM (FilterSearchViewModel) -- Filtering, logger tree, search",
            "`tCaseVM (CaseManagementViewModel) -- Case files, marks, annotations",
            "`tConfigVM (ConfigExplorerViewModel) -- Configuration database browser",
            "`tChartVM (ChartTabViewModel) -- Signal charts and playback",
            "`tCprVM (CprAnalysisViewModel) -- CPR data analysis",
            "`tLiveVM (LiveMonitoringViewModel) -- Real-time log tail",
            "`tDifferentLogsVM -- Plugin-based log parsing",
            "`tStepRecorderVM / VisualTimelineVM -- Recording and visualization",
            "",
            "**Partial classes** split large VMs by concern:",
            "`tMainViewModel.cs + .FileOps.cs + .Filtering.cs + .Windows.cs",
            "`t+ .Tabs.cs + .StateAnalysis.cs + .Globals.cs + .Systab.cs + .Theme.cs"
        )
        BulletSize = 1350
    },

    # SLIDE 8: Frontend
    @{
        Title = "4. Frontend: Views and UX"; Subtitle = "39 XAML windows with a modern dark theme"
        Bullets = @(
            "**Main Window Layout:**",
            "`tTop: Toolbar with action buttons and session controls",
            "`tLeft panel: Session Explorer (collapsible)",
            "`tCenter: 13-tab TabControl (PLC, APP, Charts, Timeline, ...)",
            "`tRight panel: Configuration/Details (collapsible)",
            "`tBottom: Status bar with progress indicator",
            "",
            "**Theme and Styling:**",
            "`tDark color palette: #0D1B2A background, #3B82F6 accent blue",
            "`tConsolas / JetBrains Mono for log content, Segoe UI for UI chrome",
            "`tCustom button styles (ModernBtn, PrimaryBtn) with drop shadows",
            "`tRow coloring: selection (yellow), marked (green), hover (white overlay)",
            "`tDiff highlighting: added (green), removed (coral), sync delta (color-coded)",
            "",
            "**Keyboard shortcuts:** Ctrl+F search, Space mark row, Ctrl+Z undo,",
            "`tCtrl+/- zoom, F1 help, tab tear-off into separate windows"
        )
        BulletSize = 1300
    },

    # SLIDE 9: Custom Controls
    @{
        Title = "Custom Controls (25 files)"; Subtitle = "Specialized UI components built for log analysis"
        LeftHeader = "Data Grid Controls"; RightHeader = "Chart Controls (SkiaSharp)"
        LeftBullets = @(
            "BaseLogGridControl -- Shared grid behavior",
            "PlcLogsGridControl -- PLC log display",
            "AppLogsTabControl -- APP log display",
            "DifferentLogsTabControl -- Plugin logs",
            "LogHeatmapControl -- Visual density overlay",
            "HeaderControl -- Main toolbar",
            "StatusBarControl -- Status and progress"
        )
        RightBullets = @(
            "ChartTabControl -- Main chart host (5 partials)",
            "ChartGraphView -- Line/scatter signal charts",
            "ChartGanttView -- Gantt timeline bars",
            "ChartThreadView -- Thread activity view",
            "ChartStateTimeline -- State transitions",
            "ChartSignalList -- Signal browser/selector",
            "ChartToolbar -- Navigation and zoom controls"
        )
        ColBulletSize = 1300
    },

    # SLIDE 10: Services
    @{
        Title = "5. Backend: Services Layer"; Subtitle = "60 service files organized by domain"
        Bullets = @(
            "**Core Services:**",
            "`tLogFileService (4 partials) -- ZIP processing, format detection, parsing",
            "`tLogParserService -- Field extraction (Pattern, Data, Exception)",
            "`tGlobalGrepService -- Multi-location search with streaming results",
            "`tLogColoringService -- Rule-based row coloring (parallel, cached regex)",
            "`tCsvExportService -- CSV/Excel export with progress and presets",
            "",
            "**Grep Sub-services (Services/Grep/):**",
            "`tSearchSchedulerService, WindowsTaskSchedulerService, EmailNotificationService",
            "`tSearchConfigService, SearchLocationService, SearchReportService",
            "",
            "**Chart Services (Services/Charts/):**",
            "`tChartDataService (memory-mapped CSV), ChartSyncService, EmStatisticsService",
            "",
            "**Infrastructure:**",
            "`tUpdateService (auto-update + SHA256), AppLogger (rotating log, 10MB max)",
            "`tDialogService, ViewFactory, WindowManager, TabTearOffManager, PluginLoader"
        )
        BulletSize = 1300
    },

    # SLIDE 11: Models
    @{
        Title = "6. Models and Data Structures"; Subtitle = "22+ model classes representing the domain"
        LeftHeader = "Core Domain Models"; RightHeader = "Configuration Models"
        LeftBullets = @(
            "LogEntry -- Log record (Level, Date, Thread, Logger, Message, Color, Annotation)",
            "LogSessionData -- Session (PLC/APP logs, configs, DBs, terminals)",
            "FilterNode -- Tree filter (Group/Condition, AND/OR/NOT)",
            "GrepResult -- Search result referencing source entry",
            "StateEntry -- State transition record",
            "EventEntry -- Terminal event data",
            "GlobalEntry / SystabEntry -- System data",
            "IndigoStripeEntry -- Stripe analysis data",
            "CaseFile -- Investigation (metadata, annotations, coloring)"
        )
        RightBullets = @(
            "SearchCriteria -- Structured search with groups",
            "SearchLocation -- Network path + connection status",
            "ScheduledSearch -- Schedule (Daily/Weekly/Interval)",
            "EmailNotificationConfig -- SMTP + DPAPI encryption",
            "ExportPreset -- CSV export template",
            "SavedConfiguration -- Filter/coloring preset",
            "TabSelectionConfig -- Tab visibility on load",
            "ColumnSettings -- Grid column persistence",
            "ChartModels -- SignalSeries, ReferenceLine"
        )
        ColBulletSize = 1200
    },

    # SLIDE 12: ViewModels
    @{
        Title = "7. ViewModels: The Application Brain"; Subtitle = "29 ViewModel files implementing all application logic"
        Bullets = @(
            "**Pattern:** ViewModelBase (INotifyPropertyChanged) + RelayCommand (ICommand)",
            "**50+ ICommand properties** in MainViewModel alone",
            "",
            "**Key ViewModels:**",
            "`tFilterSearchViewModel (4 partials) -- Filter tree, logger tree, time/thread filters",
            "`tFilterEditorViewModel -- Visual filter editor with tree node CRUD",
            "`tGlobalGrepViewModel (6 partials) -- Search, scheduling, profiles, locations",
            "`tScheduleEditorViewModel -- Schedule CRUD with SMTP config and validation",
            "`tCaseManagementViewModel (4 partials) -- Cases, marks, configs, coloring",
            "`tExportConfigurationViewModel -- Export wizard with presets and column selection",
            "`tChartTabViewModel -- Signal management, data loading, playback",
            "",
            "**Testability:** VMs depend on interfaces (IDialogService, IViewFactory),",
            "`tnot concrete Window types -- fully unit-testable without WPF runtime"
        )
        BulletSize = 1350
    },

    # SLIDE 13: Plugin System
    @{
        Title = "8. Plugin System and Extensibility"; Subtitle = "ILogFilePlugin interface for custom log formats"
        Bullets = @(
            "**Plugin Contract** (IndiLogs.PluginAPI project):",
            "`tCanHandle(fileName, sampleLines) -- Fast detection on UI thread",
            "`tParse(stream, context, progress, token) -- Streaming parse with cancellation",
            "`tGetColumns() -- Optional custom column definitions",
            "",
            "**Plugin Loading** (PluginLoader.cs):",
            "`tScans %AppData%\IndiLogs3.0\Plugins\ folder for DLLs at startup",
            "`tAssembly.LoadFrom() with redirect to prevent duplicate PluginAPI loads",
            "`tOptional strong-name validation with public key token allowlist",
            "",
            "**5 Built-in Plugins:**",
            "`tIndigoAppLogPlugin -- Pipe-delimited and legacy delimited formats",
            "`tJsonLogPlugin -- NDJSON (Winston, Bunyan, Serilog, NLog, generic)",
            "`tLog4NetPlugin -- Log4Net XML format",
            "`tCsvAutoPlugin -- Auto-detect CSV structure and delimiters",
            "`tGenericTextLogPlugin -- Fallback for unrecognized plain text",
            "",
            "**Plugin errors are isolated** -- exceptions caught, logged, file skipped"
        )
        BulletSize = 1300
    },

    # SLIDE 14: Security
    @{
        Title = "9. Security Hardening"; Subtitle = "Defense-in-depth across the application"
        Bullets = @(
            "**Credential Protection:**",
            "`tSMTP passwords encrypted with DPAPI (DataProtectionScope.CurrentUser)",
            "`tUpdate server credentials: DPAPI-encrypted in %LocalAppData%",
            "`tLegacy plaintext passwords auto-migrated to encrypted on first access",
            "`tNo hardcoded passwords or secrets in source code",
            "",
            "**Input Validation:**",
            "`tRegex timeout: 2-second limit (ReDoS prevention)",
            "`tJSON max depth: 64 levels (stack overflow prevention)",
            "`tSHA256 verification of downloaded update installers",
            "",
            "**Configuration Security:**",
            "`tServer paths in appsettings.json (shipped empty)",
            "`tappsettings.local.json for per-site overrides (gitignored)",
            "",
            "**Architecture:**",
            "`tIDialogService and IViewFactory abstractions prevent coupling",
            "`tPlugin sandboxing via ILogFilePlugin interface boundary"
        )
        BulletSize = 1350
    },

    # SLIDE 15: Performance
    @{
        Title = "10. Performance Optimizations"; Subtitle = "Designed for millions of log entries"
        Bullets = @(
            "**Memory Efficiency:**",
            "`tStringPool -- Thread-safe string interning reduces GC pressure",
            "`tFrozen WPF brushes shared across LogEntry instances",
            "`tDeferred ZIP parsing -- nested archives processed on-demand",
            "`tTerminalCsvBytes stored as byte[] until tab is accessed",
            "",
            "**Async and Parallel Processing:**",
            "`tParallel.ForEach for log coloring and filtering operations",
            "`tStreaming search results via ConcurrentQueue + timer flush",
            "`tDispatcher.InvokeAsync/BeginInvoke (never blocking Invoke)",
            "`tConfigureAwait(false) in service-layer methods",
            "",
            "**UI Responsiveness:**",
            "`tObservableRangeCollection -- batch UI updates (500 items/batch)",
            "`tList.GetRange() instead of LINQ Skip/Take for O(1) slicing",
            "`tHashSet for O(1) lookups; non-blocking export on separate thread",
            "",
            "**I/O:** Memory-mapped files for charts, buffered logger (500ms flush), compiled regex"
        )
        BulletSize = 1300
    },

    # SLIDE 16: Build
    @{
        Title = "11. Build, CI/CD and Deployment"; Subtitle = "Automated pipeline from commit to installer"
        Bullets = @(
            "**Build Configuration (.csproj):**",
            "`tTarget: .NET 10.0 Windows (x64, self-contained, single-file)",
            "`tC# latest with nullable reference types enabled project-wide",
            "`tMicrosoft.CodeAnalysis.NetAnalyzers 9.0 enforced",
            "",
            "**CI/CD Pipeline (.github/workflows/ci.yml):**",
            "`tTrigger: Push/PR to main branch",
            "`tSteps: Restore -> Build (Release) -> Test -> Coverage check",
            "`tCoverage threshold: 20% minimum (Cobertura XML report)",
            "`tArtifact upload: Coverage report (30-day retention)",
            "",
            "**Installer (Inno Setup):**",
            "`tIndiLogsSuite.iss -- Full installer with VC++ runtime",
            "`tBuildInstaller.bat -> PrepareFiles.ps1 -> CreatePortableZip.ps1",
            "`tDeployToServer.ps1 -- Server upload automation",
            "",
            "**Auto-Update:** Version check -> SHA256 verify -> Download -> Elevate -> Install"
        )
        BulletSize = 1350
    },

    # SLIDE 17: Testing
    @{
        Title = "12. Testing Strategy"; Subtitle = "291 tests across 17 test classes"
        LeftHeader = "Test Coverage Areas"; RightHeader = "Testing Infrastructure"
        LeftBullets = @(
            "GlobalGrepServiceTests -- Condition evaluation, group logic",
            "FilterNodeTests -- Tree construction, AND/OR/NOT",
            "FilterEditorViewModelTests -- CRUD, PropertyChanged",
            "LogFileServiceTests -- ZIP loading, session building",
            "LogParserServiceTests -- Pattern/Data/Exception extraction",
            "DiffEngineTests -- LCS diff, masking, segments",
            "StateTransitionHelperTests -- State change detection",
            "SearchCriteriaTests -- Serialization, evaluation",
            "PluginLoaderTests -- Discovery and loading",
            "ZipClassificationHelpersTests -- S4-5 vs S6"
        )
        RightBullets = @(
            "Framework: xUnit with XPlat Code Coverage",
            "Target: net10.0-windows (WPF support)",
            "TestDialogService: Mock with call counting",
            "NSubstitute available for mock generation",
            "CI enforces 20% minimum line coverage",
            "Coverage report uploaded as build artifact",
            "",
            "Additional: QueryParser, TextFilterParser,",
            "SystabParser, ObservableRangeCollection,",
            "LogStatistics test classes"
        )
        ColBulletSize = 1200
    },

    # SLIDE 18: Data Flow
    @{
        Title = "Data Flow: From ZIP to Screen"; Subtitle = "End-to-end processing pipeline"
        Bullets = @(
            "**1. File Input** -- Drag-drop or Open dialog routes by extension:",
            "`t.zip/.log -> LogFileService  |  .csv -> CPR tab  |  other -> Plugin detection",
            "",
            "**2. ZIP Classification** -- ZipClassificationHelpers detects S4-5 vs S6:",
            "`tPLC logs, APP logs (binary/text), config, databases, terminals, globals",
            "",
            "**3. Parsing** -- LogFileService.Parsers with StringPool interning:",
            "`tOld format (0x1e-delimited) and new format (pipe-delimited)",
            "`tLogParserService extracts Pattern, Data, Exception fields",
            "",
            "**4. Coloring** -- LogColoringService applies rules (parallel, compiled regex)",
            "",
            "**5. Session Assembly** -- LogSessionData populated with all parsed data",
            "",
            "**6. UI Binding** -- ViewModels expose ObservableCollections to Views",
            "`tFilterSearchVM applies filters, updates FILTERED tab",
            "`tChartVM loads signal data, renders via SkiaSharp",
            "",
            "**7. Export** -- CsvExportService and SearchReportService for output"
        )
        BulletSize = 1300
    },

    # SLIDE 19: File Storage
    @{
        Title = "Configuration and File Storage"; Subtitle = "Where the application persists data"
        LeftHeader = "%AppData%\IndiLogs3.0\"; RightHeader = "Other Locations"
        LeftBullets = @(
            "Configs/ -- Filter configs, _defaults.json",
            "Configs/ -- search_locations.json",
            "Configs/ -- search_schedules.json",
            "Configs/ -- Search profiles (*.json)",
            "Plugins/ -- External plugin DLLs",
            "GridColumnSettings/ -- Column layout",
            "",
            "All user settings in one roaming folder",
            "Portable across Windows profiles"
        )
        RightBullets = @(
            "%LocalAppData%\IndiLogs3.0\",
            "  update_credentials.dat (DPAPI encrypted)",
            "  Logs/app.log (rotating, 10MB max)",
            "",
            "Next to EXE (install directory):",
            "  appsettings.json (ships with empty values)",
            "  appsettings.local.json (gitignored)",
            "",
            "All passwords: DPAPI encrypted",
            "No secrets in source control"
        )
        ColBulletSize = 1300
    },

    # SLIDE 20: Key Strengths
    @{
        Title = "13. Key Strengths"; Subtitle = "What makes IndiLogs 3.0 a solid engineering product"
        Bullets = @(
            "**Architecture (8.0/10):**",
            "`tClean MVVM with 12 service interfaces and IViewFactory abstraction",
            "`tMainViewModel + 10 focused child VMs; partial classes by concern",
            "",
            "**Code Quality (8.5/10):**",
            "`tNullable reference types enabled across entire codebase (zero #nullable disable)",
            "`tMicrosoft code analyzers enforced, .editorconfig formatting rules",
            "",
            "**Security (8.0/10):**",
            "`tDPAPI encryption, regex timeout, JSON depth limit, SHA256 verification",
            "",
            "**Performance (8.5/10):**",
            "`tStringPool, parallel processing, streaming results, memory-mapped files",
            "`tDeferred parsing, batch UI updates, non-blocking async throughout",
            "",
            "**Testing (8.5/10):**",
            "`t291 tests, 20% CI threshold, TestDialogService mock infrastructure",
            "",
            "**Extensibility:** Plugin architecture for custom log formats, 5 built-in parsers"
        )
        BulletSize = 1300
    },

    # SLIDE 21: Thank You
    @{
        Title = "Thank You"; TitleSize = 4400; TitleY = 2400000; TitleColor = "3B82F6"
        Subtitle = "IndiLogs 3.0 -- Built for reliability, designed for productivity"; SubtitleY = 3200000
        Bullets = @(
            "**.NET 10.0** | **WPF** | **MVVM** | **291 Tests** | **200+ Source Files**"
        )
        BulletY = 4200000; BulletSize = 1800
    }
)

New-Pptx -Path $OutputPath -Slides $slides
Write-Host "`nPresentation generated with $($slides.Count) slides."
Write-Host "Output: $OutputPath"
