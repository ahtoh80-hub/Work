$outPath = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  $lines += ('sheets: ' + $wb.Worksheets.Count)
  foreach ($ws in $wb.Worksheets) {
    $u = $ws.UsedRange
    $lines += ('  ' + $ws.Name + ' rows=' + $u.Rows.Count + ' cols=' + $u.Columns.Count)
  }
  # read first 5 sheets, show first 5 rows
  for ($si = 1; $si -le [Math]::Min($wb.Worksheets.Count, 10); $si++) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $lines += ('--- Sheet ' + $si + ': ' + $ws.Name + ' (' + $u.Rows.Count + 'x' + $u.Columns.Count + ')')
    for ($r = 1; $r -le [Math]::Min($u.Rows.Count, 5); $r++) {
      $cells = @()
      for ($c = 1; $c -le [Math]::Min($u.Columns.Count, 15); $c++) {
        $v = ([string]$u.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') { $cells += ("[$c]$v") }
      }
      $lines += ('    row ' + $r + ': ' + ($cells -join ' | '))
    }
  }
  $wb.Close($false)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_exel_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'EXEL PROBE DONE'