$path = 'C:\VSCode\CAE\CAE_OSBL_Перечень блокировок.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($path, 0, $true)
  $ws1 = $wb.Worksheets.Item(1)
  # headers
  $hdrs = @()
  for ($c = 1; $c -le 12; $c++) { $hdrs += ('[' + $c + ']' + ([string]$ws1.Cells.Item(1, $c).Text).Trim()) }
  $lines += ('H1: ' + ($hdrs -join ' | '))
  # full rows for rotated systems
  $last = $ws1.UsedRange.Rows.Count
  $wantSys = @('2701-Z-10004','2711-Z-00102','2711-Z-00106','2623-I-05001','2623-I-07001','2818-Z-00002','Z-23001')
  for ($r = 2; $r -le $last; $r++) {
    $st = ([string]$ws1.Cells.Item($r, 3).Text).Trim()
    if ($wantSys -contains $st) {
      $cells = @()
      for ($c = 1; $c -le 12; $c++) {
        $v = ([string]$ws1.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') {
          $short = $v -replace "`r`n", '/'
          if ($short.Length -gt 45) { $short = $short.Substring(0, 45) + '..' }
          $cells += ("[$c]$short")
        }
      }
      $lines += ('r' + $r + ': ' + ($cells -join ' | '))
    }
  }
  # sheet 2 rows for 2701-Z-10004 and 2623-I-07001
  $ws2 = $wb.Worksheets.Item(2)
  $hdrs2 = @()
  for ($c = 1; $c -le 12; $c++) { $hdrs2 += ('[' + $c + ']' + ([string]$ws2.Cells.Item(1, $c).Text).Trim()) }
  $lines += ('H2: ' + ($hdrs2 -join ' | '))
  $last2 = $ws2.UsedRange.Rows.Count
  for ($r = 2; $r -le $last2; $r++) {
    $st = ([string]$ws2.Cells.Item($r, 3).Text).Trim()
    if ($st -in @('2701-Z-10004','2623-I-07001')) {
      $cells = @()
      foreach ($c in @(1,3,9,10,11,12)) {
        $v = ([string]$ws2.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') {
          $short = $v -replace "`r`n", '/'
          if ($short.Length -gt 40) { $short = $short.Substring(0, 40) + '..' }
          $cells += ("[$c]$short")
        }
      }
      $lines += ('r' + $r + ': ' + ($cells -join ' | '))
    }
  }
  $wb.Close($false)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_out_check2.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'OUT CHECK2 DONE'