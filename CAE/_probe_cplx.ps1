$outPath = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  foreach ($si in @(166, 167, 191, 192, 193, 161, 164, 18)) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $rc = $u.Rows.Count; $cc = $u.Columns.Count
    $lines += ('=== Sheet ' + $si + ' (' + $rc + 'x' + $cc + ')')
    $grid = @{}
    $maxR = [Math]::Min($rc, 40)
    for ($r = 1; $r -le $maxR; $r++) {
      for ($c = 1; $c -le $cc; $c++) {
        $v = ([string]$u.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') { $grid["$r`:$c"] = $v }
      }
    }
    for ($r = 1; $r -le $maxR; $r++) {
      $cells = @()
      for ($c = 1; $c -le $cc; $c++) {
        $v = $grid["$r`:$c"]
        if ($v) {
          $short = $v -replace "`r`n", '/' -replace "`n", '/'
          if ($short.Length -gt 24) { $short = $short.Substring(0, 24) + '..' }
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
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_cplx_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'CPLEX PROBE DONE'