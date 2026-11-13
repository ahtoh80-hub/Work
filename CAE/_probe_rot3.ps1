$outPath = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  # Process ALL sheets, including transposed ones
  $sheetCount = $wb.Worksheets.Count
  $lines += ('total sheets: ' + $sheetCount)
  $allData = New-Object 'System.Collections.Generic.List[object]'
  for ($si = 1; $si -le $sheetCount; $si++) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $rc = $u.Rows.Count; $cc = $u.Columns.Count
    if ($rc -le 1 -and $cc -le 1) { continue }
    # read all data
    $grid = @{}
    $nonEmpty = 0
    for ($r = 1; $r -le $rc; $r++) {
      for ($c = 1; $c -le $cc; $c++) {
        $v = ([string]$u.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') { $grid["$r`:$c"] = $v; $nonEmpty++ }
      }
    }
    if ($nonEmpty -eq 0) { continue }
    # detect orientation: find 'TAG-No.' position
    $tagNoRow = 0; $tagNoCol = 0
    for ($r = 1; $r -le $rc; $r++) {
      for ($c = 1; $c -le $cc; $c++) {
        $v = $grid["$r`:$c"]
        if ($v -eq 'TAG-No.' -or $v -match 'TAG.No') {
          if ($tagNoRow -eq 0) { $tagNoRow = $r; $tagNoCol = $c }
        }
      }
    }
    # detect if transposed: TAG-No. in a column means many effect tags in the same column below
    $isTransposed = $false
    if ($tagNoRow -gt 0 -and $tagNoCol -gt 0) {
      # check if effect tags are in the same column (transposed) or same row (normal)
      $tagsInRow = 0; $tagsInCol = 0
      for ($c = $tagNoCol + 1; $c -le $cc; $c++) {
        $v = $grid["$tagNoRow`:$c"]
        if ($v -match '^\d{4}-') { $tagsInRow++ }
      }
      for ($r = $tagNoRow + 1; $r -le $rc; $r++) {
        $v = $grid["$r`:$tagNoCol"]
        if ($v -match '^\d{4}-') { $tagsInCol++ }
      }
      $isTransposed = ($tagsInCol -gt $tagsInRow)
    }
    $lines += ('Sheet ' + $si + ' (' + $rc + 'x' + $cc + '): TAG-No at [' + $tagNoRow + ',' + $tagNoCol + '] transposed=' + $isTransposed + ' nonEmpty=' + $nonEmpty)
    if ($isTransposed) {
      # TRANSPOSE the grid: swap rows and columns
      $tGrid = @{}
      foreach ($k in $grid.Keys) {
        $parts = $k -split ':'
        $tGrid[$parts[1] + ':' + $parts[0]] = $grid[$k]
      }
      # find TAG-No. in transposed grid (should now be in a row)
      $tTagNoRow = 0
      for ($r = 1; $r -le $cc; $r++) {
        for ($c = 1; $c -le $rc; $c++) {
          $v = $tGrid["$r`:$c"]
          if ($v -eq 'TAG-No.' -or $v -match 'TAG.No') { $tTagNoRow = $r; break }
        }
        if ($tTagNoRow -gt 0) { break }
      }
      $lines += ('  transposed: TAG-No at row ' + $tTagNoRow)
      # find system tag (Cause and Effect Matrix of ...)
      $sysTag = ''; $sysName = ''
      foreach ($k in $tGrid.Keys) {
        $v = [string]$tGrid[$k]
        if ($v -match 'Cause and Effect.*?(\d{4}-[A-Z]+[-A-Z0-9]*)') {
          $sysTag = $Matches[1].Trim(); $sysName = $v -replace 'Cause and Effect Matrix of\s+', ''; break
        }
      }
      if ($sysTag -eq '') {
        # try original grid
        foreach ($k in $grid.Keys) {
          $v = [string]$grid[$k]
          if ($v -match 'Cause and Effect.*?(\d{4}-[A-Z]+[-A-Z0-9]*)') { $sysTag = $Matches[1].Trim(); $sysName = $v; break }
        }
      }
      $lines += ('  sysTag=' + $sysTag + ' sysName=' + $sysName)
      # extract effect tags from TAG-No. row
      $effTags = @()
      if ($tTagNoRow -gt 0) {
        for ($c = 1; $c -le $rc; $c++) {
          $v = $tGrid["$tTagNoRow`:$c"]
          if ($v -match '^\d{4}-') { $effTags += @{ col = $c; tag = $v.Trim() } }
        }
      }
      $lines += ('  effect tags: ' + $effTags.Count)
      # find cause rows in transposed grid
      $causeRows = @()
      for ($r = $tTagNoRow + 3; $r -le $cc; $r++) {
        $v = $tGrid["$r`:3"]
        if ($v -match '^\d{4}-') { $causeRows += @{ row = $r; tag = $v.Trim() } }
      }
      $lines += ('  cause rows: ' + $causeRows.Count)
      $allData.Add([pscustomobject]@{ sheet=$si; transposed=$true; sysTag=$sysTag; effects=($effTags.Count); causes=($causeRows.Count) }) | Out-Null
    } else {
      $allData.Add([pscustomobject]@{ sheet=$si; transposed=$false; nonEmpty=$nonEmpty }) | Out-Null
    }
  }
  $wb.Close($false)
  # summary
  $transCount = 0; $normCount = 0
  foreach ($d in $allData) { if ($d.transposed) { $transCount++ } else { $normCount++ } }
  $lines += ('--- Summary: normal=' + $normCount + ' transposed=' + $transCount)
  foreach ($d in $allData) {
    if ($d.transposed) { $lines += ('  TBL ' + $d.sheet + ': sys=' + $d.sysTag + ' effects=' + $d.effects + ' causes=' + $d.causes) }
  }
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_rot_detect.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'ROT DETECT DONE'