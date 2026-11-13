$outPath = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  foreach ($si in @(176, 188, 191, 164, 161)) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $rc = $u.Rows.Count; $cc = $u.Columns.Count
    $lines += ('=== Sheet ' + $si + ' (' + $rc + 'x' + $cc + ')')
    $maxR = [Math]::Min($rc, 12)
    for ($r = 1; $r -le $maxR; $r++) {
      for ($c = 1; $c -le $cc; $c++) {
        $v = ([string]$u.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') {
          $full = $v -replace "`r`n", '\' -replace "`n", '\'
          $lines += ('  [' + $r + ',' + $c + ']=' + $full)
        }
      }
    }
  }
  $wb.Close($false)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_title_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'TITLE PROBE DONE'