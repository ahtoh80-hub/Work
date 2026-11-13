# Parse CAE_OSBL_exel.xlsx (193 sheets) into cae_osbl_model.json
# Layouts: NORMAL (fast), MIRRORED (rotated pages), COMPLEX (CAUSE/ПРИЧИНА), CE (PROCESS INTERLOCKS MATRIX)
# v2: tags without unit prefix, broken-tag repair, col2 carry-forward,
#     RU parsing + dedup by (sysTag|causeTag), CE action legend, MIRRORED 2-label detect, sys suffix repair
$ErrorActionPreference = 'Stop'
$src = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$modelPath = Join-Path $env:TEMP 'cae_osbl_model.json'
$auditPath = Join-Path $env:TEMP 'cae_exel_audit.txt'

$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
$model = @{ items = New-Object 'System.Collections.Generic.List[object]'; systems = New-Object 'System.Collections.Generic.List[object]' }
$script:seq = 0
$audit = New-Object 'System.Collections.Generic.List[string]'
$sysSeen = @{}
$dupSeen = @{}
$tagRx = '^\d{4}-[A-Z]|^[A-Z]{2,7}-\d{2,6}[A-Z0-9/]*\s*$'
$marksOk = @('X','I','P','O','C','R','S','M','T')
$actMap = @{ 'X'='Equipment shutdown'; 'C'='Close'; 'O'='Open'; 'R'='Run'; 'S'='Stop'; 'I'='Initiate'; 'M'='Minimum'; 'P'='Start prohibit'; 'T'='Timer' }

function Clean-Text([string]$v) {
  $v = ([string]$v) -replace "`r`n", ' ' -replace "`n", ' ' -replace "`r", ' '
  return $v.Trim()
}
function Fix-Tag([string]$v) {
  return ((Clean-Text $v) -replace '-\s+', '-')
}
function First-Token([string]$v) {
  $t = (Fix-Tag $v) -split '\s+'
  return [string]$t[0]
}
function Add-System([string]$sheet, [string]$tag, [string]$nameEN) {
  $tag = ([string]$tag).Trim()
  if ($tag -eq '') { return }
  if (-not $sysSeen.ContainsKey($tag)) {
    $sysSeen[$tag] = $true
    $model.systems.Add([ordered]@{ sheet=$sheet; tag=$tag; nameEN=$nameEN; nameRU=''; rel=''; relName=''; start=0; end=0; tblIndex=0; tblRows=0 }) | Out-Null
  }
}
function Add-Cause([string]$sheet, [int]$sheetRow, [string]$sysTag, [string]$sysName, [int]$no, [string]$descEN, [string]$tagEN, $effList, [string]$dupKey) {
  if ($dupKey -ne '') {
    if ($dupSeen.ContainsKey($dupKey)) { return $false }
    $dupSeen[$dupKey] = $true
  }
  $script:seq++
  $model.items.Add([ordered]@{
    seq=$script:seq; sheet=$sheet; sheetRow=$sheetRow; sysTag=$sysTag
    sysNameEN=$sysName; sysNameRU=''; rel=''; relName=''
    no=[string]$no; descEN=$descEN; descRU=''; inputEN=''; inputRU=''
    tagEN=$tagEN; tagRU=''; pid=''
    effects=$effList
  }) | Out-Null
  Add-System $sheet $sysTag $sysName
  return $true
}
function Get-TitleInfo($grid) {
  foreach ($k in $grid.Keys) {
    $v = [string]$grid[$k]
    if ($v -match 'Cause and Effect|Cause & Effect|Матрица причин') {
      $ft = Fix-Tag $v
      $after = $ft
      if ($ft -match '(?i)(matrix of|следствий)(.*)') { $after = $Matches[2] }
      $tag = ''
      $m1 = [regex]::Match($after, '\d{4}-[A-Z][-A-Z0-9]*')
      $m2 = [regex]::Match($after, '(?<![A-Za-z0-9-])[A-Z]{1,6}-\d{3,6}')
      if ($m1.Success -and $m2.Success -and $m2.Index -lt $m1.Index) { $tag = $m2.Value }
      elseif ($m1.Success) { $tag = $m1.Value }
      elseif ($m2.Success) { $tag = $m2.Value }
      else { $m3 = [regex]::Match($ft, '\d{4}-[A-Z][-A-Z0-9]*'); if ($m3.Success) { $tag = $m3.Value } }
      $name = ''
      if ($tag -ne '') {
        $name = $ft -replace 'Cause and Effect Matrix of\s*', '' -replace 'Матрица причин и следствий\s*', ''
        $name = (Clean-Text $name) -replace [regex]::Escape($tag), ''
      }
      $unit = ''
      if ($tag -notmatch '^\d{4}-') {
        $mu = [regex]::Match($ft, '(\d{4})-[A-Z]')
        if ($mu.Success) { $unit = $mu.Groups[1].Value }
      }
      return @{ tag=(Clean-Text $tag); name=(Clean-Text $name); unit=$unit }
    }
  }
  return @{ tag=''; name=''; unit='' }
}
function Repair-SysTag([string]$tag, [string]$unit) {
  $tag = ([string]$tag).Trim()
  if ($tag -match '^\d{4}-') { return $tag }
  if ($tag -match '^[A-Z]{1,6}-\d{3,6}$' -and $unit -ne '') { return ($unit + '-' + $tag) }
  return $tag
}
function Eff-Signature($effList) {
  if ($null -eq $effList -or $effList.Count -eq 0) { return '' }
  $a = @($effList | ForEach-Object { ([string]$_.tag) + ([string]$_.mark) })
  [Array]::Sort($a)
  return ($a -join ',')
}
# ---MAIN---
try {
  $wb = $excel.Workbooks.Open($src, 0, $true)
  $nSheets = $wb.Worksheets.Count
  Write-Output ("sheets: " + $nSheets)
  for ($si = 1; $si -le $nSheets; $si++) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $rc = [int]$u.Rows.Count; $cc = [int]$u.Columns.Count
    $grid = @{}; $nonEmpty = 0
    if ($rc -ge 1 -and $cc -ge 1) {
      if ($rc -eq 1 -and $cc -eq 1) {
        $v = ([string]$u.Value2).Trim()
        if ($v -ne '') { $grid['1:1'] = $v; $nonEmpty = 1 }
      } else {
        $vals = $u.Value2
        for ($r = 1; $r -le $rc; $r++) {
          for ($c = 1; $c -le $cc; $c++) {
            $o = $vals[$r, $c]
            if ($o -ne $null -and $o -isnot [System.DBNull]) {
              $v = ([string]$o).Trim()
              if ($v -ne '') { $grid["$r`:$c"] = $v; $nonEmpty++ }
            }
          }
        }
      }
    }
    $sheet = "TBL_$si"
    if ($nonEmpty -lt 3) { $audit.Add("S$si EMPTY ($rc x $cc)") | Out-Null; continue }
    $ti = Get-TitleInfo $grid
    # ---------- CE layout ----------
    $isCE = $false
    if (([string]$grid['1:1']) -match '^CE\s*\d') { $isCE = $true }
    foreach ($k in $grid.Keys) { if (([string]$grid[$k]) -match 'PROCESS INTERLOCKS') { $isCE = $true; break } }
    if ($isCE) {
      $effCol = 0; $effCnt = 0
      for ($c = 10; $c -le $cc; $c++) {
        $cnt = 0
        for ($r = 8; $r -le $rc; $r++) { if (([string]$grid["$r`:$c"]) -match $tagRx) { $cnt++ } }
        if ($cnt -gt $effCnt) { $effCnt = $cnt; $effCol = $c }
      }
      if ($effCnt -ge 2) {
        $hRow = 0
        for ($r = 1; $r -le 10; $r++) { if (([string]$grid["$r`:$effCol"]) -match 'TAG ?\(S\)') { $hRow = $r; break } }
        if ($hRow -eq 0) { $hRow = 7 }
        $effDescCol = $effCol + 2
        $causeNo = 0; $totMarks = 0; $dupsHere = 0
        for ($c = 2; $c -lt $effCol; $c++) {
          $ctag = Fix-Tag ([string]$grid["4`:$c"])
          if ($ctag -eq '' -or ($ctag -notmatch $tagRx)) { continue }
          $causeNo++
          $sysUse = ''
          for ($cc2 = $c; $cc2 -ge 2; $cc2--) {
            $v = Fix-Tag ([string]$grid["3`:$cc2"])
            if ($v -match $tagRx) { $sysUse = $v; break }
          }
          $cdesc = Clean-Text ([string]$grid["2`:$c"])
          $effList = New-Object 'System.Collections.Generic.List[object]'
          for ($r = $hRow + 2; $r -le $rc; $r++) {
            $etag = Fix-Tag ([string]$grid["$r`:$effCol"])
            if ($etag -notmatch $tagRx) { continue }
            $mark = ([string]$grid["$r`:$c"]).Trim().ToUpperInvariant()
            if ($mark -ne '' -and $mark -ne '-') {
              $edesc = Clean-Text ([string]$grid["$r`:$effDescCol"])
              $eact = ''
              if ($actMap.ContainsKey($mark)) { $eact = $actMap[$mark] }
              $effList.Add([ordered]@{ tag=$etag; actionEN=$eact; actionRU=''; descEN=$edesc; descRU=''; mark=$mark }) | Out-Null
              $totMarks++
            }
          }
          $ok = Add-Cause $sheet $c $sysUse '' $causeNo $cdesc $ctag $effList ($sysUse + '|' + $ctag + '|' + (Eff-Signature $effList))
          if (-not $ok) { $dupsHere++ }
        }
        $audit.Add("S$si CE causes=$causeNo effCol=$effCol marks=$totMarks dups=$dupsHere") | Out-Null
        continue
      }
      $audit.Add("S$si CE-FAIL no effCol") | Out-Null
      continue
    }
    # <<<COMPLEX>>>
    # ---------- effect tag row detection (fallback path) ----------
    $bestRow = 0; $bestCnt = 0; $bestFirst = 0; $bestLast = 0
    $maxScan = [Math]::Min($rc, 15)
    for ($r = 1; $r -le $maxScan; $r++) {
      $cnt = 0; $first = 0; $last = 0
      for ($c = 4; $c -le $cc; $c++) {
        if (([string]$grid["$r`:$c"]) -match $tagRx) { $cnt++; if ($first -eq 0) { $first = $c }; $last = $c }
      }
      if ($cnt -gt $bestCnt) { $bestCnt = $cnt; $bestRow = $r; $bestFirst = $first; $bestLast = $last }
    }
    $causeMarkRow = 0
    foreach ($k in $grid.Keys) { if (([string]$grid[$k]) -match 'CAUSE/ПРИЧИНА') { $causeMarkRow = [int]($k -split ':')[0]; break } }
    # ---------- COMPLEX layout ----------
    if ($bestCnt -ge 2 -and $causeMarkRow -gt 0) {
      $effRow = $bestRow; $descRow = $effRow - 1
      $causeNo = 0; $totMarks = 0; $dupsHere = 0
      $sysSet = New-Object 'System.Collections.Generic.List[string]'
      $lastSys = ''
      for ($r = $causeMarkRow + 1; $r -le $rc; $r++) {
        $ctag = Fix-Tag ([string]$grid["$r`:4"])
        if ($ctag -notmatch $tagRx) { continue }
        $causeNo++
        $sysRaw = [string]$grid["$r`:2"]
        $sysUse = ''
        if ((Clean-Text $sysRaw) -eq '') { $sysUse = $lastSys }
        elseif ((Fix-Tag $sysRaw) -match $tagRx) { $sysUse = First-Token $sysRaw; $lastSys = $sysUse }
        else { $lastSys = '' }
        if (-not $sysSet.Contains($sysUse)) { [void]$sysSet.Add($sysUse) }
        $cdesc = Clean-Text ([string]$grid["$r`:8"])
        $effList = New-Object 'System.Collections.Generic.List[object]'
        for ($c = $bestFirst; $c -le $bestLast; $c++) {
          $etag = Fix-Tag ([string]$grid["$effRow`:$c"])
          if ($etag -notmatch $tagRx) { continue }
          $mark = ([string]$grid["$r`:$c"]).Trim().ToUpperInvariant()
          if ($mark -in $marksOk) {
            $edesc = Clean-Text ([string]$grid["$descRow`:$c"])
            $eact = ''
            if ($actMap.ContainsKey($mark)) { $eact = $actMap[$mark] }
            $effList.Add([ordered]@{ tag=$etag; actionEN=$eact; actionRU=''; descEN=$edesc; descRU=''; mark=$mark }) | Out-Null
            $totMarks++
          }
        }
        $ok = Add-Cause $sheet $r $sysUse '' $causeNo $cdesc $ctag $effList ($sysUse + '|' + $ctag + '|' + (Eff-Signature $effList))
        if (-not $ok) { $dupsHere++ }
      }
      $sysList = ($sysSet | Where-Object { $_ -ne '' }) -join ','
      $audit.Add("S$si COMPLEX causes=$causeNo marks=$totMarks dups=$dupsHere sys=[$sysList]") | Out-Null
      continue
    }
    # ---------- NORMAL fast path: label in rows 1-6 + tags to the right ----------
    $labRow = 0; $labCol = 0
    for ($r = 1; $r -le [Math]::Min($rc, 6); $r++) {
      for ($c = 1; $c -le $cc; $c++) {
        if (([string]$grid["$r`:$c"]) -match '^TAG-No\.?$|^№ технологич|^position number') { $labRow = $r; $labCol = $c; break }
      }
      if ($labRow -gt 0) { break }
    }
    if ($labRow -gt 0) {
      $effCols = New-Object 'System.Collections.Generic.List[int]'
      for ($c = $labCol + 1; $c -le $cc; $c++) {
        if (([string]$grid["$labRow`:$c"]) -match $tagRx) { $effCols.Add($c) }
      }
      if ($effCols.Count -ge 1) {
        $effRow = $labRow; $hdrCol = $labCol
        $effFirst = $effCols[0]; $effLast = $effCols[$effCols.Count - 1]
        $descRow = 0
        foreach ($rr in @(($effRow+1), ($effRow-1), ($effRow+2))) {
          if ($rr -ge 1 -and $rr -le $rc -and $descRow -eq 0) {
            if (([string]$grid["$rr`:$hdrCol"]) -match 'Descr|Описан') { $descRow = $rr }
          }
        }
        if ($descRow -eq 0) {
          $b1 = 1; $b1r = 0
          foreach ($rr in @(($effRow-1), ($effRow+1))) {
            if ($rr -ge 1 -and $rr -le $rc) {
              $cnt = 0
              for ($c = $effFirst; $c -le $effLast; $c++) { $v = [string]$grid["$rr`:$c"]; if ($v -ne '' -and ($v -notmatch $tagRx)) { $cnt++ } }
              if ($cnt -gt $b1) { $b1 = $cnt; $b1r = $rr }
            }
          }
          if ($b1r -gt 0) { $descRow = $b1r }
        }
        $actRow = 0
        foreach ($rr in @(($effRow+2), ($effRow+1), ($effRow+3))) {
          if ($rr -ge 1 -and $rr -le $rc -and $actRow -eq 0) {
            if (([string]$grid["$rr`:$hdrCol"]) -match 'Action|Действ') { $actRow = $rr }
          }
        }
        $causeNo = 0; $totMarks = 0; $dupsHere = 0
        $sysSet = New-Object 'System.Collections.Generic.List[string]'
        $lastSys = ''
        for ($r = $effRow + 2; $r -le $rc; $r++) {
          $c1 = [string]$grid["$r`:1"]; $c3 = [string]$grid["$r`:3"]
          if (($c1 -match '^Rev|^Рев') -and ($c3 -match 'TAG|№ тех')) { continue }
          $causeTag = ''; $causeTagCol = 0
          foreach ($cc2 in @(3, 2, 4)) {
            $v = Fix-Tag ([string]$grid["$r`:$cc2"])
            if ($v -match $tagRx) { $causeTag = $v; $causeTagCol = $cc2; break }
          }
          if ($causeTag -eq '') { continue }
          $causeNo++
          $desc = ''
          for ($dc = $causeTagCol + 1; $dc -le $causeTagCol + 4 -and $dc -le $cc; $dc++) {
            $v = Clean-Text ([string]$grid["$r`:$dc"])
            if ($v.Length -gt $desc.Length) { $desc = $v }
          }
          $sysUse = $ti.tag
          $c2 = [string]$grid["$r`:2"]
          if ((Clean-Text $c2) -eq '') { if ($lastSys -ne '') { $sysUse = $lastSys } }
          elseif ((Fix-Tag $c2) -match $tagRx) { $sysUse = First-Token $c2; $lastSys = $sysUse }
          else { $lastSys = '' }
          $unit = ''
          foreach ($c in $effCols) { $tv = Fix-Tag ([string]$grid["$effRow`:$c"]); if ($tv -match '^(\d{4})-') { $unit = $Matches[1]; break } }
          if ($unit -eq '') { $unit = $ti.unit }
          $sysUse = Repair-SysTag $sysUse $unit
          if (-not $sysSet.Contains($sysUse)) { [void]$sysSet.Add($sysUse) }
          $effList = New-Object 'System.Collections.Generic.List[object]'
          foreach ($c in $effCols) {
            $etag = Fix-Tag ([string]$grid["$effRow`:$c"])
            if ($etag -notmatch $tagRx) { continue }
            $mark = ([string]$grid["$r`:$c"]).Trim().ToUpperInvariant()
            if ($mark -in $marksOk) {
              $edesc = if ($descRow -gt 0) { Clean-Text ([string]$grid["$descRow`:$c"]) } else { '' }
              $eact = if ($actRow -gt 0) { Clean-Text ([string]$grid["$actRow`:$c"]) } else { '' }
              if ($eact -eq '' -and $actMap.ContainsKey($mark)) { $eact = $actMap[$mark] }
              $effList.Add([ordered]@{ tag=$etag; actionEN=$eact; actionRU=''; descEN=$edesc; descRU=''; mark=$mark }) | Out-Null
              $totMarks++
            }
          }
          $ok = Add-Cause $sheet $r $sysUse $ti.name $causeNo $desc $causeTag $effList ($sysUse + '|' + $causeTag + '|' + (Eff-Signature $effList))
          if (-not $ok) { $dupsHere++ }
        }
        $sysList = ($sysSet | Where-Object { $_ -ne '' }) -join ','
        $audit.Add("S$si NORMAL causes=$causeNo marks=$totMarks dups=$dupsHere effRow=$effRow sys=[$sysList]") | Out-Null
        continue
      }
    }
    # <<<NORMAL>>>
    # ---------- NORMAL fallback: max tag row (only when no label in rows 1-6) ----------
    if ($bestCnt -ge 2 -and $labRow -eq 0) {
      $effRow = $bestRow; $effFirst = $bestFirst; $effLast = $bestLast
      $hdrCol = 0
      for ($c = 1; $c -lt $effFirst; $c++) { if (([string]$grid["$effRow`:$c"]) -match 'TAG-No|№ тех|position number') { $hdrCol = $c; break } }
      if ($hdrCol -eq 0) { $hdrCol = [Math]::Max(1, $effFirst - 1) }
      $descRow = 0
      foreach ($rr in @(($effRow+1), ($effRow-1), ($effRow+2))) {
        if ($rr -ge 1 -and $rr -le $rc -and $descRow -eq 0) {
          if (([string]$grid["$rr`:$hdrCol"]) -match 'Descr|Описан') { $descRow = $rr }
        }
      }
      if ($descRow -eq 0) {
        $b1 = 1; $b1r = 0
        foreach ($rr in @(($effRow-1), ($effRow+1))) {
          if ($rr -ge 1 -and $rr -le $rc) {
            $cnt = 0
            for ($c = $effFirst; $c -le $effLast; $c++) { $v = [string]$grid["$rr`:$c"]; if ($v -ne '' -and ($v -notmatch $tagRx)) { $cnt++ } }
            if ($cnt -gt $b1) { $b1 = $cnt; $b1r = $rr }
          }
        }
        if ($b1r -gt 0) { $descRow = $b1r }
      }
      $actRow = 0
      foreach ($rr in @(($effRow+2), ($effRow+1), ($effRow+3))) {
        if ($rr -ge 1 -and $rr -le $rc -and $actRow -eq 0) {
          if (([string]$grid["$rr`:$hdrCol"]) -match 'Action|Действ') { $actRow = $rr }
        }
      }
      $causeNo = 0; $totMarks = 0; $dupsHere = 0
      $sysSet = New-Object 'System.Collections.Generic.List[string]'
      $lastSys = ''
      for ($r = $effRow + 2; $r -le $rc; $r++) {
        $c1 = [string]$grid["$r`:1"]; $c3 = [string]$grid["$r`:3"]
        if (($c1 -match '^Rev|^Рев') -and ($c3 -match 'TAG|№ тех')) { continue }
        $causeTag = ''; $causeTagCol = 0
        foreach ($cc2 in @(3, 2, 4)) {
          $v = Fix-Tag ([string]$grid["$r`:$cc2"])
          if ($v -match $tagRx) { $causeTag = $v; $causeTagCol = $cc2; break }
        }
        if ($causeTag -eq '') { continue }
        $causeNo++
        $desc = ''
        for ($dc = $causeTagCol + 1; $dc -le $causeTagCol + 4 -and $dc -le $cc; $dc++) {
          $v = Clean-Text ([string]$grid["$r`:$dc"])
          if ($v.Length -gt $desc.Length) { $desc = $v }
        }
        $sysUse = $ti.tag
        $c2 = [string]$grid["$r`:2"]
        if ((Clean-Text $c2) -eq '') { if ($lastSys -ne '') { $sysUse = $lastSys } }
        elseif ((Fix-Tag $c2) -match $tagRx) { $sysUse = First-Token $c2; $lastSys = $sysUse }
        else { $lastSys = '' }
        $unit = ''
        for ($c = $effFirst; $c -le $effLast; $c++) { $tv = Fix-Tag ([string]$grid["$effRow`:$c"]); if ($tv -match '^(\d{4})-') { $unit = $Matches[1]; break } }
        if ($unit -eq '') { $unit = $ti.unit }
        $sysUse = Repair-SysTag $sysUse $unit
        if (-not $sysSet.Contains($sysUse)) { [void]$sysSet.Add($sysUse) }
        $effList = New-Object 'System.Collections.Generic.List[object]'
        for ($c = $effFirst; $c -le $effLast; $c++) {
          $etag = Fix-Tag ([string]$grid["$effRow`:$c"])
          if ($etag -notmatch $tagRx) { continue }
          $mark = ([string]$grid["$r`:$c"]).Trim().ToUpperInvariant()
          if ($mark -in $marksOk) {
            $edesc = if ($descRow -gt 0) { Clean-Text ([string]$grid["$descRow`:$c"]) } else { '' }
            $eact = if ($actRow -gt 0) { Clean-Text ([string]$grid["$actRow`:$c"]) } else { '' }
            if ($eact -eq '' -and $actMap.ContainsKey($mark)) { $eact = $actMap[$mark] }
            $effList.Add([ordered]@{ tag=$etag; actionEN=$eact; actionRU=''; descEN=$edesc; descRU=''; mark=$mark }) | Out-Null
            $totMarks++
          }
        }
        $ok = Add-Cause $sheet $r $sysUse $ti.name $causeNo $desc $causeTag $effList ($sysUse + '|' + $causeTag + '|' + (Eff-Signature $effList))
        if (-not $ok) { $dupsHere++ }
      }
      $sysList = ($sysSet | Where-Object { $_ -ne '' }) -join ','
      $audit.Add("S$si NORMAL2 causes=$causeNo marks=$totMarks dups=$dupsHere effRow=$effRow sys=[$sysList]") | Out-Null
      continue
    }
    # ---------- MIRRORED layout ----------
    if ($labRow -gt 0) {
      $effCol = 0
      for ($c = $labCol + 2; $c -le [Math]::Min($cc, $labCol + 8); $c++) {
        if (([string]$grid["6`:$c"]) -match '^TAG-No\.?$|^№ технологич') {
          $cnt = 0
          for ($r = $labRow + 3; $r -le $rc; $r++) { if (([string]$grid["$r`:$c"]) -match $tagRx) { $cnt++ } }
          if ($cnt -ge 2) { $effCol = $c; break }
        }
      }
      if ($effCol -eq 0) {
        $effCnt = 0
        for ($c = $labCol + 1; $c -le $cc; $c++) {
          $cnt = 0
          for ($r = $labRow + 2; $r -le $rc; $r++) { if (([string]$grid["$r`:$c"]) -match $tagRx) { $cnt++ } }
          if ($cnt -gt $effCnt) { $effCnt = $cnt; $effCol = $c }
        }
        if ($effCnt -lt 2) { $effCol = 0 }
      }
      if ($effCol -gt 0) {
        $effDescCol = $effCol - 1; $effActCol = $effCol - 2
        $causeCols = New-Object 'System.Collections.Generic.List[int]'
        $empties = 0
        for ($c = $labCol - 1; $c -ge 1; $c--) {
          $v3 = [string]$grid["$labRow`:$c"]; $v4 = [string]$grid["$($labRow+1)`:$c"]
          if ($v3 -ne '' -or $v4 -ne '') { $causeCols.Add($c); $empties = 0 }
          else { $empties++; if ($empties -ge 2) { break } }
        }
        if ($causeCols.Count -gt 1) { $causeCols.Reverse() }
        $sysName = $ti.name
        if ($sysName -eq '') { $sysName = Clean-Text ([string]$grid["$labRow`:$($labCol+2)"]) }
        if ($sysName -match '^Cause and Effect Matrix of|^Матрица причин') { $sysName = '' }
        $sysTagUse = $ti.tag
        if ($sysTagUse -notmatch '^\d{4}-') {
          $unit = ''
          for ($r = $labRow + 3; $r -le $rc; $r++) {
            $tv = Fix-Tag ([string]$grid["$r`:$effCol"])
            if ($tv -match '^(\d{4})-') { $unit = $Matches[1]; break }
          }
          if ($unit -eq '') { $unit = $ti.unit }
          $sysTagUse = Repair-SysTag $sysTagUse $unit
        }
        $causeNo = 0; $totMarks = 0; $dupsHere = 0
        $lastSysM = ''
        foreach ($c in $causeCols) {
          $ctag = Fix-Tag ([string]$grid["$labRow`:$c"])
          if ($ctag -notmatch $tagRx) { continue }
          $causeNo++
          $cdesc = Clean-Text ([string]$grid["$($labRow+1)`:$c"])
          $sysC = $sysTagUse
          $v2c = Fix-Tag ([string]$grid["$($labRow-1)`:$c"])
          if ($v2c -match '^\d{4}-[A-Z]') { $sysC = (First-Token $v2c); $lastSysM = $sysC }
          elseif ($lastSysM -ne '' -and (Clean-Text ([string]$grid["$($labRow-1)`:$c"])) -eq '') { $sysC = $lastSysM }
          $effList = New-Object 'System.Collections.Generic.List[object]'
          for ($r = $labRow + 3; $r -le $rc; $r++) {
            $etag = Fix-Tag ([string]$grid["$r`:$effCol"])
            if ($etag -notmatch $tagRx) { continue }
            $mark = ([string]$grid["$r`:$c"]).Trim().ToUpperInvariant()
            if ($mark -in $marksOk) {
              $edesc = Clean-Text ([string]$grid["$r`:$effDescCol"])
              $eact = Clean-Text ([string]$grid["$r`:$effActCol"])
              if ($eact -eq '' -and $actMap.ContainsKey($mark)) { $eact = $actMap[$mark] }
              $effList.Add([ordered]@{ tag=$etag; actionEN=$eact; actionRU=''; descEN=$edesc; descRU=''; mark=$mark }) | Out-Null
              $totMarks++
            }
          }
          $ok = Add-Cause $sheet $c $sysC $sysName $causeNo $cdesc $ctag $effList ($sysC + '|' + $ctag + '|' + (Eff-Signature $effList))
          if (-not $ok) { $dupsHere++ }
        }
        $audit.Add("S$si MIRRORED causes=$causeNo marks=$totMarks dups=$dupsHere effCol=$effCol sys=$sysTagUse name=$sysName") | Out-Null
        continue
      }
    }
    $audit.Add("S$si UNKNOWN ($rc x $cc nonEmpty=$nonEmpty) tag=$($ti.tag)") | Out-Null
  }
  $wb.Close($false)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
[System.IO.File]::WriteAllText($modelPath, ($model | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($auditPath, ($audit -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
$lay = @{}
$dups = 0
foreach ($a in $audit) { if ($a -match 'DUP') { $dups++ } }
foreach ($a in $audit) { $t = ($a -split ' ')[1]; if (-not $lay.ContainsKey($t)) { $lay[$t] = 0 }; $lay[$t]++ }
Write-Output ("items=" + $model.items.Count + " systems=" + $model.systems.Count + " dups=" + $dups)
Write-Output ("layouts: " + (($lay.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' '))
Write-Output "AUDIT -> $auditPath"
Write-Output "MODEL -> $modelPath"