$outPath = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  foreach ($si in @(7,8)) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $lines += ('=== Table ' + $si + ' (' + $u.Rows.Count + 'x' + $u.Columns.Count + ')')
    # scan ALL cells for any non-empty value
    $found = 0
    for ($r = 1; $r -le $u.Rows.Count; $r++) {
      for ($c = 1; $c -le $u.Columns.Count; $c++) {
        $v = ([string]$u.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') {
          $found++
          if ($found -le 20) { $lines += ('  [' + $r + ',' + $c + '] = ' + $v.Substring(0, [Math]::Min(80, $v.Length))) }
        }
      }
    }
    $lines += ('  total non-empty cells: ' + $found)
  }
  $wb.Close($false)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_rot_scan.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'ROT SCAN DONE'