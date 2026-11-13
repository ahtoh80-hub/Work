# CAE_OSBL.pdf → качественный Excel: 1 страница PDF = 1 вкладка.
# Источник: CAE_OSBL_exel.xlsx (Acrobat-экспорт, листы 1:1 со страницами PDF).
# Нормализация: перевёрнутые (MIRRORED) страницы приводятся к горизонтальному виду,
# стиль: рамки, шапки, автоширина, закрепление, оглавление.
$ErrorActionPreference = 'Stop'
$src = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$outPath = 'C:\VSCode\CAE\CAE_OSBL_PDF_страницы.xlsx'
$tagRx = '^\d{4}-[A-Z]|^[A-Z]{2,7}-\d{2,6}[A-Z0-9/]*\s*$'
function Clean-Text([string]$v) {
  $v = ([string]$v) -replace "`r`n", ' ' -replace "`n", ' ' -replace "`r", ' '
  return $v.Trim()
}
function Fix-Tag([string]$v) { return ((Clean-Text $v) -replace '-\s+', '-') }
function Get-TitleInfo2($grid) {
  foreach ($k in $grid.Keys) {
    $v = [string]$grid[$k]
    if ($v -match 'Cause and Effect|Cause & Effect|Матрица причин') {
      $ft = Fix-Tag $v
      $tag = ''
      if ($ft -match '(\d{4}-[A-Z][-A-Z0-9]*)') { $tag = $Matches[1].Trim() }
      elseif ($ft -match '(?<![A-Za-z0-9-])[A-Z]{1,6}-\d{3,6}') { $tag = $Matches[0].Trim() }
      $name = ''
      if ($tag -ne '') {
        $name = $ft -replace 'Cause and Effect Matrix of\s*', '' -replace 'Матрица причин и следствий\s*', ''
        $name = (Clean-Text $name) -replace [regex]::Escape($tag), ''
      }
      $isRU = ($ft -match 'Матрица причин') -and -not ($ft -match 'Cause and Effect|Cause & Effect')
      return @{ tag = (Clean-Text $tag); name = (Clean-Text $name); ru = $isRU }
    }
  }
  return @{ tag = ''; name = ''; ru = $false }
}
# ---MAIN-A: read source pages---
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
$pages = @{}
$meta = @{}
try {
  $wb = $excel.Workbooks.Open($src, 0, $true)
  $nSheets = $wb.Worksheets.Count
  for ($si = 1; $si -le $nSheets; $si++) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $rc = [int]$u.Rows.Count; $cc = [int]$u.Columns.Count
    $grid = @{}; $nonEmpty = 0
    if ($rc -ge 1 -and $cc -ge 1) {
      if ($rc -eq 1 -and $cc -eq 1) {
        $v = ([string]$u.Value2).Trim()
        if ($v -ne '') { $grid[1, 1] = $v; $nonEmpty = 1 }
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
    $pages[$si] = @{ grid = $grid; rc = $rc; cc = $cc; nonEmpty = $nonEmpty }
  }
  $wb.Close($false)
} finally { }
Write-Output ("source sheets: " + $nSheets)
# ---CLASSIFY: detect layout per page (same logic as parser)---
function Detect-Page($grid, $rc, $cc) {
  $res = @{ kind = 'EMPTY'; labRow = 0; labCol = 0; effCol = 0; effDescCol = 0; effActCol = 0; causeCols = $null; bestRow = 0; bestFirst = 0; bestLast = 0 }
  $labRow = 0; $labCol = 0
  for ($r = 1; $r -le [Math]::Min($rc, 6); $r++) {
    for ($c = 1; $c -le $cc; $c++) {
      if (([string]$grid["$r`:$c"]) -match '^TAG-No\.?$|^№ технологич|^position number') { $labRow = $r; $labCol = $c; break }
    }
    if ($labRow -gt 0) { break }
  }
  $res.labRow = $labRow; $res.labCol = $labCol
  if ($labRow -gt 0) {
    $hasRight = $false
    for ($c = $labCol + 1; $c -le $cc; $c++) { if (([string]$grid["$labRow`:$c"]) -match $tagRx) { $hasRight = $true; break } }
    if ($hasRight) {
      $res.kind = 'NORMAL'
      $res.effRow = $labRow; $res.effFirst = $labCol + 1; $res.effLast = $labCol + 1
      $last = $labCol + 1
      for ($c = $labCol + 1; $c -le $cc; $c++) { if (([string]$grid["$labRow`:$c"]) -match $tagRx) { $last = $c } }
      $res.effLast = $last
      $res.hdrCol = $labCol
      return $res
    }
    # mirrored: effCol via row-6 label or max tags
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
      $res.kind = 'MIRRORED'; $res.effCol = $effCol; $res.effDescCol = $effCol - 1; $res.effActCol = $effCol - 2
      $cols = New-Object 'System.Collections.Generic.List[int]'
      $empties = 0
      for ($c = $labCol - 1; $c -ge 1; $c--) {
        $v3 = [string]$grid["$labRow`:$c"]; $v4 = [string]$grid["$($labRow+1)`:$c"]
        if ($v3 -ne '' -or $v4 -ne '') { $cols.Add($c); $empties = 0 }
        else { $empties++; if ($empties -ge 2) { break } }
      }
      if ($cols.Count -gt 1) { $cols.Reverse() }
      $res.causeCols = $cols
      return $res
    }
    # label without effects right and without vertical tags -> fallthrough to bestRow
  }
  $bestRow = 0; $bestCnt = 0; $bestFirst = 0; $bestLast = 0
  $maxScan = [Math]::Min($rc, 15)
  for ($r = 1; $r -le $maxScan; $r++) {
    $cnt = 0; $first = 0; $last = 0
    for ($c = 4; $c -le $cc; $c++) {
      if (([string]$grid["$r`:$c"]) -match $tagRx) { $cnt++; if ($first -eq 0) { $first = $c }; $last = $c }
    }
    if ($cnt -gt $bestCnt) { $bestCnt = $cnt; $bestRow = $r; $bestFirst = $first; $bestLast = $last }
  }
  if ($bestCnt -ge 2) {
    $isCplx = $false
    foreach ($k in $grid.Keys) { if (([string]$grid[$k]) -match 'CAUSE/ПРИЧИНА') { $isCplx = $true; break } }
    if ($isCplx) { $res.kind = 'COMPLEX'; $res.bestRow = $bestRow; $res.bestFirst = $bestFirst; $res.bestLast = $bestLast; return $res }
    if (($bestRow -eq $labRow) -and ($labRow -gt 0)) {
      # effects row is the label row but tags are LEFT of label (vertical/mirrored variant) -> mirrored view
      $effCol = 0; $effCnt = 0
      for ($c = 1; $c -lt $labCol; $c++) {
        $cnt = 0
        for ($r = $labRow + 2; $r -le $rc; $r++) { if (([string]$grid["$r`:$c"]) -match $tagRx) { $cnt++ } }
        if ($cnt -gt $effCnt) { $effCnt = $cnt; $effCol = $c }
      }
      if ($effCnt -ge 2) {
        $res.kind = 'MIRRORED'; $res.effCol = $effCol; $res.effDescCol = $effCol + 1; $res.effActCol = $effCol + 2
        $cols = New-Object 'System.Collections.Generic.List[int]'
        $empties = 0
        for ($c = $labCol - 1; $c -ge 1; $c--) {
          $v3 = [string]$grid["$labRow`:$c"]; $v4 = [string]$grid["$($labRow+1)`:$c"]
          if ($v3 -ne '' -or $v4 -ne '') { $cols.Add($c); $empties = 0 }
          else { $empties++; if ($empties -ge 2) { break } }
        }
        if ($cols.Count -gt 1) { $cols.Reverse() }
        $res.causeCols = $cols
        return $res
      }
    }
    $res.kind = 'NORMAL2'; $res.bestRow = $bestRow; $res.bestFirst = $bestFirst; $res.bestLast = $bestLast; $res.hdrCol = 0
    for ($c = 1; $c -lt $bestFirst; $c++) { if (([string]$grid["$bestRow`:$c"]) -match 'TAG-No|№ тех|position number') { $res.hdrCol = $c; break } }
    if ($res.hdrCol -eq 0) { $res.hdrCol = [Math]::Max(1, $bestFirst - 1) }
    return $res
  }
  $isCE = $false
  if (([string]$grid['1:1']) -match '^CE\s*\d') { $isCE = $true }
  foreach ($k in $grid.Keys) { if (([string]$grid[$k]) -match 'PROCESS INTERLOCKS') { $isCE = $true; break } }
  if ($isCE) { $res.kind = 'CE'; return $res }
  return $res
}
# <<<MIRRORGRID>>>
# ---MIRRORGRID: normalize a mirrored page into horizontal matrix layout---
function Build-MirroredGrid($grid, $rc, $cc, $d, $ti) {
  $rows = New-Object 'System.Collections.Generic.List[object]'
  $title = $ti.tag
  if ($ti.name -ne '') { $title = $title + ' ' + $ti.name }
  if ($title -eq '') { $title = 'Матрица причин и следствий (страница развёрнута из вертикального макета)' }
  $rows.Add(@($title))
  $rows.Add(@(''))
  $effRows = New-Object 'System.Collections.Generic.List[int]'
  for ($r = $d.labRow + 3; $r -le $rc; $r++) {
    if (([string]$grid["$r`:$($d.effCol)"]) -match $tagRx) { $effRows.Add($r) }
  }
  $r3 = @('TAG-No.')
  $r4 = @('Description')
  $r5 = @('Action')
  foreach ($er in $effRows) {
    $r3 += (Fix-Tag ([string]$grid["$er`:$($d.effCol)"]))
    $r4 += (Clean-Text ([string]$grid["$er`:$($d.effDescCol)"]))
    $r5 += (Clean-Text ([string]$grid["$er`:$($d.effActCol)"]))
  }
  $rows.Add($r3); $rows.Add($r4); $rows.Add($r5)
  $rows.Add(@('Cause'))
  $rows.Add(@('Rev.', 'TAG-No.', 'Description', 'Notes'))
  foreach ($c in $d.causeCols) {
    $tag = Fix-Tag ([string]$grid["$($d.labRow)`:$c"])
    $desc = Clean-Text ([string]$grid["$($d.labRow+1)`:$c"])
    $notes = Clean-Text ([string]$grid["$($d.labRow+2)`:$c"])
    $line = @('', $tag, $desc, $notes)
    foreach ($er in $effRows) {
      $line += ([string]$grid["$er`:$c"])
    }
    $rows.Add($line)
  }
  return @{ rows = $rows; headerRows = 7; causeStart = 8 }
}

# ---RENDER---
$meta = New-Object 'System.Collections.Generic.List[object]'
$prepared = @{}
for ($si = 1; $si -le $nSheets; $si++) {
  $p = $pages[$si]
  $grid = $p.grid; $rc = $p.rc; $cc = $p.cc
  $ti = Get-TitleInfo2 $grid
  $lang = if ($ti.ru) { 'RU' } elseif ($ti.tag -ne '') { 'EN' } else { '' }
  if ($p.nonEmpty -lt 3) {
    $prepared[$si] = @{ kind = 'EMPTY'; rows = $null; headerRows = 0; ti = $ti }
    $meta.Add([pscustomobject]@{ page = $si; kind = 'EMPTY'; lang = ''; sys = ''; causes = 0; effects = 0; note = 'пустая страница-разделитель' })
    continue
  }
  $d = Detect-Page $grid $rc $cc
  $kind = $d.kind
  if ($kind -eq 'MIRRORED') {
    $g2 = Build-MirroredGrid $grid $rc $cc $d $ti
    $prepared[$si] = @{ kind = 'MIRRORED'; rows = $g2.rows; headerRows = $g2.headerRows; ti = $ti }
    $nc = @($g2.rows[($g2.causeStart - 1)..($g2.rows.Count - 1)]) | Where-Object { ([string]$_[1]) -match $tagRx }
    $meta.Add([pscustomobject]@{ page = $si; kind = 'MIRRORED→нормализовано'; lang = $lang; sys = $ti.tag; causes = @($nc).Count; effects = ($g2.rows[2].Count - 1); note = $ti.name })
    continue
  }
  # plain copy: build dense rows from grid (values as-is)
  $rows = New-Object 'System.Collections.Generic.List[object]'
  for ($r = 1; $r -le $rc; $r++) {
    $line = @()
    for ($c = 1; $c -le $cc; $c++) { $line += ([string]$grid["$r`:$c"]) }
    $rows.Add($line)
  }
  $prepared[$si] = @{ kind = $kind; rows = $rows; headerRows = 0; ti = $ti }
  $causeCount = 0; $effCount = 0
  if ($kind -eq 'NORMAL' -or $kind -eq 'NORMAL2') {
    $start = if ($kind -eq 'NORMAL') { $d.effRow + 2 } else { $d.bestRow + 2 }
    for ($r = $start; $r -le $rc; $r++) {
      $t3 = ''
      foreach ($cc2 in @(3, 2, 4)) { $v = Fix-Tag ([string]$grid["$r`:$cc2"]); if ($v -match $tagRx) { $t3 = $v; break } }
      if ($t3 -ne '') { $causeCount++ }
    }
    $first = if ($kind -eq 'NORMAL') { $d.effFirst } else { $d.bestFirst }
    $lastE = if ($kind -eq 'NORMAL') { $d.effLast } else { $d.bestLast }
    for ($c = $first; $c -le $lastE; $c++) { if (([string]$grid["$(if ($kind -eq 'NORMAL') { $d.effRow } else { $d.bestRow })`:$c"]) -match $tagRx) { $effCount++ } }
  }
  $note = $ti.name
  if ($kind -eq 'CE') { $note = 'PROCESS INTERLOCKS MATRIX (эффекты в строках)' }
  if ($kind -eq 'COMPLEX') { $note = 'Расширенный формат CAE (P&ID/пороги/SIL)' }
  $meta.Add([pscustomobject]@{ page = $si; kind = $kind; lang = $lang; sys = $ti.tag; causes = $causeCount; effects = $effCount; note = $note })
}
Write-Output ("pages classified: " + $prepared.Count)

# ---WRITEBOOK---
$out = New-Object -ComObject Excel.Application
$out.Visible = $false; $out.DisplayAlerts = $false
try {
  $wbN = $out.Workbooks.Add()
  $toc = $wbN.Worksheets.Item(1)
  $toc.Name = 'Оглавление'
  for ($si = 1; $si -le $nSheets; $si++) {
    $prep = $prepared[$si]
    $ws = $wbN.Worksheets.Add([Type]::Missing, $wbN.Worksheets.Item($wbN.Worksheets.Count))
    $ws.Name = ('P' + $si.ToString('000'))
    if ($prep.kind -eq 'EMPTY') {
      $ws.Cells.Item(1, 1) = 'Пустая страница-разделитель PDF (без контента)'
      $ws.Cells.Item(1, 1).Font.Italic = $true
      $ws.Cells.Item(1, 1).Font.Color = 8421504
      continue
    }
    $rows = $prep.rows
    $n = $rows.Count
    $maxc = 1
    foreach ($line in $rows) { if ($line.Count -gt $maxc) { $maxc = $line.Count } }
    $arr = [Array]::CreateInstance([object], [int[]]@($n, $maxc), [int[]]@(1, 1))
    for ($i = 0; $i -lt $n; $i++) {
      $line = $rows[$i]
      for ($j = 0; $j -lt $line.Count; $j++) { $arr.SetValue([string]$line[$j], ($i + 1), ($j + 1)) }
    }
    $rng = $ws.Range($ws.Cells.Item(1, 1), $ws.Cells.Item($n, $maxc))
    $rng.Value2 = $arr
    $rng.Borders.LineStyle = 1; $rng.Borders.Weight = 2
    $rng.HorizontalAlignment = -4131; $rng.VerticalAlignment = -4160
    $hRows = 7
    if ($prep.kind -in @('NORMAL', 'NORMAL2', 'CE', 'COMPLEX')) { $hRows = 6 }
    $hdr = $ws.Range($ws.Cells.Item(1, 1), $ws.Cells.Item($hRows, $maxc))
    $hdr.Font.Bold = $true
    $hdr.Interior.Color = 15123099
    $ws.Rows.Item(1).RowHeight = 30
    if ($prep.kind -eq 'MIRRORED') {
      $ws.Cells.Item(2, 1) = 'Страница нормализована: в Acrobat-экспорте матрица была повёрнута на 90'
      $ws.Cells.Item(2, 1).Font.Italic = $true
      $ws.Cells.Item(2, 1).Font.Color = 8388736
    }
    $ws.Columns.AutoFit() | Out-Null
    for ($c = 1; $c -le $maxc; $c++) {
      $w = [double]$ws.Columns.Item($c).ColumnWidth
      if ($w -gt 55) { $ws.Columns.Item($c).ColumnWidth = 55; $ws.Columns.Item($c).WrapText = $true }
    }
    $ws.Activate()
    $out.ActiveWindow.SplitRow = $hRows
    $out.ActiveWindow.FreezePanes = $true
  }
  # --- TOC ---
  $tocHdrs = @('Страница PDF', 'Вкладка', 'Тип', 'Язык', 'Система (тег)', 'Причин', 'Эффектов', 'Примечание')
  for ($c = 1; $c -le $tocHdrs.Count; $c++) { $toc.Cells.Item(1, $c) = $tocHdrs[$c - 1] }
  $hT = $toc.Range($toc.Cells.Item(1, 1), $toc.Cells.Item(1, $tocHdrs.Count))
  $hT.Font.Bold = $true; $hT.Interior.Color = 15123099
  $ri = 2
  foreach ($mrow in $meta) {
    $tabName = ('P' + ([int]$mrow.page).ToString('000'))
    $toc.Cells.Item($ri, 1) = [string]$mrow.page
    $toc.Hyperlinks.Add($toc.Cells.Item($ri, 2), '', ("'" + $tabName + "'!A1"), '', $tabName)
    $toc.Cells.Item($ri, 3) = [string]$mrow.kind
    $toc.Cells.Item($ri, 4) = [string]$mrow.lang
    $toc.Cells.Item($ri, 5) = [string]$mrow.sys
    $toc.Cells.Item($ri, 6) = [string]$mrow.causes
    $toc.Cells.Item($ri, 7) = [string]$mrow.effects
    $toc.Cells.Item($ri, 8) = [string]$mrow.note
    $ri++
  }
  $toc.Columns.AutoFit() | Out-Null
  $toc.Activate()
  $out.ActiveWindow.SplitRow = 1
  $out.ActiveWindow.FreezePanes = $true
  if (Test-Path $outPath) { Remove-Item -LiteralPath $outPath -Force }
  $wbN.SaveAs($outPath, 51)
  $wbN.Close($false)
  Write-Output ("SAVED -> " + $outPath)
} finally {
  $out.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($out) | Out-Null
  if ($null -ne $excel) { $excel.Quit(); [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null }
}
