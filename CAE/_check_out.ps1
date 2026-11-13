$path = 'C:\VSCode\CAE\CAE_OSBL_Перечень блокировок.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($path, 0, $true)
  $lines += ('sheets: ' + $wb.Worksheets.Count)
  foreach ($ws in $wb.Worksheets) {
    $u = $ws.UsedRange
    $lines += ('  ' + $ws.Name + ' rows=' + $u.Rows.Count + ' cols=' + $u.Columns.Count)
  }
  # summary sheet content
  foreach ($ws in $wb.Worksheets) {
    if ($ws.Name -match 'Сводка') {
      $u = $ws.UsedRange
      for ($r = 1; $r -le $u.Rows.Count; $r++) {
        $a = ([string]$u.Cells.Item($r, 1).Text).Trim()
        $b = ([string]$u.Cells.Item($r, 2).Text).Trim()
        if ($a -ne '') { $lines += ('  Сводка: ' + $a + ' = ' + $b) }
      }
    }
  }
  # sample rows: rotated-table systems in 'Перечень блокировок'
  $ws1 = $wb.Worksheets.Item(1)
  $last = $ws1.UsedRange.Rows.Count
  $lines += ('--- Перечень блокировок: rows=' + $last)
  $wantSys = @('2701-Z-10004','2701-Z-10006','2711-Z-00102','2711-Z-00106','2623-I-05001','2623-I-07001','2623-I-05002','2818-Z-00002','2622-I-70001','Z-23001','2623-I-02003')
  for ($r = 2; $r -le $last; $r++) {
    $st = ([string]$ws1.Cells.Item($r, 3).Text).Trim()
    if ($wantSys -contains $st) {
      $no = ([string]$ws1.Cells.Item($r, 6).Text).Trim()
      $desc = ([string]$ws1.Cells.Item($r, 7).Text).Trim()
      if ($desc.Length -gt 40) { $desc = $desc.Substring(0, 40) + '..' }
      $eff = ([string]$ws1.Cells.Item($r, 11).Text).Trim()
      if ($eff.Length -gt 60) { $eff = $eff.Substring(0, 60) + '..' }
      $lines += ('  r' + $r + ' sys=' + $st + ' no=' + $no + ' cause=' + $desc + ' | eff=' + $eff)
    }
  }
  $wb.Close($false)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_out_check.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'OUT CHECK DONE'