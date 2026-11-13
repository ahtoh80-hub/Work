$outPath = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  foreach ($si in @(7,8,11,12)) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $lines += ('=== Table ' + $si + ' (' + $u.Rows.Count + 'x' + $u.Columns.Count + ')')
    for ($r = 1; $r -le [Math]::Min($u.Rows.Count, 15); $r++) {
      $cells = @()
      for ($c = 1; $c -le [Math]::Min($u.Columns.Count, 15); $c++) {
        $v = ([string]$u.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') { $cells += ("[$c]$v") }
      }
      if ($cells.Count -gt 0) { $lines += ('  row ' + $r + ': ' + ($cells -join ' | ')) }
    }
  }
  $wb.Close($false)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_rotated_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'ROTATED PROBE DONE'