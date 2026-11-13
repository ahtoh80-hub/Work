$outPath = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  foreach ($si in @(7, 11)) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $rc = $u.Rows.Count; $cc = $u.Columns.Count
    $lines += ('=== Sheet ' + $si + ' (' + $rc + 'x' + $cc + ')')
    # build grid
    $grid = @{}
    for ($r = 1; $r -le $rc; $r++) {
      for ($c = 1; $c -le $cc; $c++) {
        $v = ([string]$u.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') { $grid["$r`:$c"] = $v }
      }
    }
    # show ALL cells grouped by row
    for ($r = 1; $r -le $rc; $r++) {
      $cells = @()
      for ($c = 1; $c -le $cc; $c++) {
        $v = $grid["$r`:$c"]
        if ($v) {
          $short = $v; if ($short.Length -gt 30) { $short = $short.Substring(0, 30) + '..' }
          $cells += ("[$c]$short")
        }
      }
      if ($cells.Count -gt 0) { $lines += ('  r' + $r + ': ' + ($cells -join ' | ')) }
    }
  }
  $wb.Close($false)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_rot_full.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'FULL DUMP DONE'