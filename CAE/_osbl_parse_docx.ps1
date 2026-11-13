# Parse CAE_OSBL.docx tables into structured JSON (same model as EBSM)
$ErrorActionPreference = 'Stop'
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
$model = @{ items = New-Object 'System.Collections.Generic.List[object]'; systems = New-Object 'System.Collections.Generic.List[object]' }
$seq = 0
try {
  $doc = $word.Documents.Open('C:\VSCode\CAE\CAE_OSBL.docx', $false, $true)
  $tblCount = $doc.Tables.Count
  Write-Output ("tables: " + $tblCount)
  for ($ti = 1; $ti -le $tblCount; $ti++) {
    $tbl = $doc.Tables.Item($ti)
    $rc = $tbl.Rows.Count
    $cc = $tbl.Columns.Count
    # read all cells into 2D array
    $grid = @{}
    for ($r = 1; $r -le $rc; $r++) {
      for ($c = 1; $c -le $cc; $c++) {
        try {
          $txt = $tbl.Cell($r, $c).Range.Text
          $txt = $txt -replace "`r`a", '' -replace "`r`n", ' ' -replace "`r", ' ' -replace "`n", ' '
          $txt = $txt.Trim()
          if ($txt -ne '') { $grid["$r`:$c"] = $txt }
        } catch { }
      }
    }
    # find title (row 1) -> system tag and name
    $title = ''
    for ($c = 1; $c -le $cc; $c++) {
      $v = $grid["1`:$c"]
      if ($v -and $v -match 'Cause and Effect') { $title = $v; break }
    }
    if ($title -eq '') { continue }
    # extract system tag - try row 1 first, then last rows, then any cell with 'Cause and Effect'
    $sysTag = ''
    $sysName = ''
    # try row 1
    $fullRow1 = ''
    for ($c = 1; $c -le $cc; $c++) { $v = $grid["1`:$c"]; if ($v) { $fullRow1 = $fullRow1 + ' ' + $v } }
    $fullRow1 = $fullRow1.Trim()
    if ($fullRow1 -match '([0-9]{4}-[A-Z]+[-A-Z0-9]*)') { $sysTag = $Matches[1].Trim(); $sysName = $fullRow1 -replace 'Cause and Effect Matrix of\s+', '' -replace [regex]::Escape($sysTag), ''; $sysName = $sysName.Trim() }
    # try last rows
    if ($sysTag -eq '') {
      for ($r = $rc; $r -ge [Math]::Max(1, $rc - 3); $r--) {
        for ($c = 1; $c -le [Math]::Min($cc, 5); $c++) {
          $v = $grid["${r}:$c"]
          if ($v -match 'Cause and Effect.*([0-9]{4}-[A-Z]+[-A-Z0-9]*)') {
            $sysTag = $Matches[1].Trim(); $sysName = $v -replace 'Cause and Effect Matrix of\s+', '' -replace [regex]::Escape($sysTag), ''; $sysName = $sysName.Trim()
            break
          }
        }
        if ($sysTag -ne '') { break }
      }
    }
    # try any cell
    if ($sysTag -eq '') {
      foreach ($k in $grid.Keys) {
        $v = [string]$grid[$k]
        if ($v -match 'Cause and Effect.*([0-9]{4}-[A-Z]+[-A-Z0-9]*)') {
          $sysTag = $Matches[1].Trim(); $sysName = $v -replace 'Cause and Effect Matrix of\s+', '' -replace [regex]::Escape($sysTag), ''; $sysName = $sysName.Trim()
          break
        }
      }
    }
    if ($sysTag -eq '') { continue }
    # find TAG-No. header - try multiple orientations
    # Orientation A: TAG-No. in row (normal) - scan rows 1-10, cols 1-10
    $hdrRow = 0; $descRow = 0; $actionRow = 0; $hdrCol = 0
    for ($r = 1; $r -le [Math]::Min($rc, 10); $r++) {
      for ($c = 1; $c -le [Math]::Min($cc, 10); $c++) {
        $v = $grid["${r}:$c"]
        if ($v -eq 'TAG-No.' -and $hdrRow -eq 0) { $hdrRow = $r; $hdrCol = $c }
        if ($v -eq 'Description' -and $descRow -eq 0) { $descRow = $r; $hdrCol = $c }
        if ($v -eq 'Action' -and $actionRow -eq 0) { $actionRow = $r; $hdrCol = $c }
      }
    }
    # Orientation B: TAG-No. in column (rotated) - scan ALL cells
    if ($hdrRow -eq 0) {
      for ($c = 1; $c -le $cc; $c++) {
        for ($r = 1; $r -le $rc; $r++) {
          $v = $grid["${r}:$c"]
          if ($v -eq 'TAG-No.') { $hdrRow = $r; $hdrCol = $c; break }
        }
        if ($hdrRow -gt 0) { break }
      }
      if ($hdrRow -gt 0) {
        $descRow = 0; $actionRow = 0
        for ($r = $hdrRow + 1; $r -le $rc; $r++) {
          $v = $grid["${r}:$hdrCol"]
          if ($v -eq 'Description' -and $descRow -eq 0) { $descRow = $r }
          if ($v -eq 'Action' -and $actionRow -eq 0) { $actionRow = $r }
        }
      }
    }
    # Orientation C: system tag is at the END (bottom) - check last rows for title
    if ($hdrRow -eq 0) {
      for ($r = $rc; $r -ge [Math]::Max(1, $rc - 5); $r--) {
        for ($c = 1; $c -le [Math]::Min($cc, 5); $c++) {
          $v = $grid["${r}:$c"]
          if ($v -match 'Cause and Effect') {
            if ($v -match '([0-9]{4}-[A-Z]+[-A-Z0-9]*)') {
              $sysTag = $Matches[1].Trim()
              $sysName = $v -replace 'Cause and Effect Matrix of\s+', '' -replace [regex]::Escape($sysTag), ''
              $sysName = $sysName.Trim()
            }
            break
          }
        }
        if ($sysTag -ne '') { break }
      }
      # for bottom-title tables, TAG-No. might be in first rows after all - retry
      if ($sysTag -ne '') {
        for ($r = 1; $r -le [Math]::Min($rc, 10); $r++) {
          for ($c = 1; $c -le [Math]::Min($cc, 10); $c++) {
            $v = $grid["${r}:$c"]
            if ($v -eq 'TAG-No.' -and $hdrRow -eq 0) { $hdrRow = $r; $hdrCol = $c }
            if ($v -eq 'Description' -and $descRow -eq 0) { $descRow = $r; $hdrCol = $c }
            if ($v -eq 'Action' -and $actionRow -eq 0) { $actionRow = $r; $hdrCol = $c }
          }
        }
      }
    }
    # Orientation D: TAG-No. text might have extra spaces or different casing
    if ($hdrRow -eq 0) {
      for ($r = 1; $r -le [Math]::Min($rc, 10); $r++) {
        for ($c = 1; $c -le [Math]::Min($cc, 10); $c++) {
          $v = $grid["${r}:$c"]
          if ($v -match 'TAG.No' -and $hdrRow -eq 0) { $hdrRow = $r; $hdrCol = $c }
          if ($v -match 'Description' -and $descRow -eq 0) { $descRow = $r; $hdrCol = $c }
          if ($v -match 'Action' -and $actionRow -eq 0) { $actionRow = $r; $hdrCol = $c }
        }
      }
    }
    if ($hdrRow -eq 0) { continue }
    # effect tags from header row (cols 3+)
    $effects = @()
    for ($c = 3; $c -le $cc; $c++) {
      $tag = $grid["${hdrRow}:$c"]
      if ($tag -and $tag -match '^\d{4}-') {
        $desc = if ($descRow -gt 0 -and $grid.ContainsKey("${descRow}:$c")) { $grid["${descRow}:$c"] } else { '' }
        $act = if ($actionRow -gt 0 -and $grid.ContainsKey("${actionRow}:$c")) { $grid["${actionRow}:$c"] } else { '' }
        $effects += @{ col = $c; tag = ([string]$tag).Trim(); desc = ([string]$desc).Trim(); action = ([string]$act).Trim() }
      }
    }
    if ($effects.Count -eq 0) { continue }
    # find cause rows (rows after the sub-header, containing cause tags)
    $causeStart = 0
    for ($r = $hdrRow + 1; $r -le $rc; $r++) {
      $v = $grid["${r}:3"]
      if ($v -and $v -match '^\d{4}-') { $causeStart = $r; break }
    }
    if ($causeStart -eq 0) { continue }
    # offset: effect tags start at col 3 in header, X marks start at col 7 in cause rows
    # but need to detect dynamically: find the first X mark column in the first cause row
    $firstCauseRow = $causeStart
    $firstXCol = 0
    for ($c = 1; $c -le $cc; $c++) {
      $v = $grid["${firstCauseRow}:$c"]
      if ($v -eq 'X' -or $v -eq 'I') { $firstXCol = $c; break }
    }
    if ($firstXCol -eq 0) { $firstXCol = $effects[0].col + 4 }
    $xOffset = $firstXCol - $effects[0].col
    # emit system
    $model.systems.Add([ordered]@{
      sheet = "TBL_$ti"
      tag = $sysTag
      nameEN = $sysName
      nameRU = ''
      rel = ''; relName = ''
      start = $effects[0].col; end = $effects[$effects.Count-1].col
      tblIndex = $ti
      tblRows = $rc
    }) | Out-Null
    # emit cause rows
    for ($r = $causeStart; $r -le $rc; $r++) {
      $causeTag = $grid["${r}:3"]
      if (-not $causeTag -or $causeTag -notmatch '^\d{4}-') { continue }
      $causeDesc = if ($grid.ContainsKey("${r}:4")) { $grid["${r}:4"] } else { '' }
      $seq++
      $effList = New-Object 'System.Collections.Generic.List[object]'
      foreach ($eff in $effects) {
        $xCol = $eff.col + $xOffset
        $mark = $grid["${r}:$xCol"]
        if ($mark -and ($mark -eq 'X' -or $mark -eq 'I' -or $mark -eq 'P')) {
          $effList.Add([ordered]@{
            tag = $eff.tag; actionEN = $eff.action; actionRU = ''
            descEN = $eff.desc; descRU = ''; mark = $mark
          }) | Out-Null
        }
      }
      $model.items.Add([ordered]@{
        seq = $seq; sheet = "TBL_$ti"; sheetRow = $r; sysTag = $sysTag
        sysNameEN = $sysName; sysNameRU = ''; rel = ''; relName = ''
        no = [string]($r - $causeStart + 1)
        descEN = $causeDesc; descRU = ''
        inputEN = ''; inputRU = ''
        tagEN = $causeTag; tagRU = ''
        pid = ''
        effects = $effList
      }) | Out-Null
    }
    if ($ti % 20 -eq 0) { Write-Output ("  processed table " + $ti + "/" + $tblCount + " sysTag=" + $sysTag) }
  }
  $doc.Close($false)
} finally {
  $word.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
}
$modelPath = Join-Path $env:TEMP 'cae_osbl_model.json'
[System.IO.File]::WriteAllText($modelPath, ($model | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))
# audit: which tables were skipped and why
$skippedNoTag = 0; $skippedNoHdr = 0; $skippedNoCause = 0; $skippedNoEff = 0; $processed = 0
$skippedList = @()
for ($ti = 1; $ti -le 187; $ti++) {
  $found = $false
  foreach ($s in $model.systems) { if ([string]$s.sheet -eq "TBL_$ti") { $found = $true; break } }
  if ($found) { $processed++ } else {
    $skippedList += $ti
  }
}
Write-Output ("processed tables: " + $processed + " skipped: " + $skippedList.Count)
Write-Output ("skipped table indices: " + ($skippedList -join ','))
Write-Output ("MODEL -> " + $modelPath)