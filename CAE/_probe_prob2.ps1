$outPath = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  foreach ($si in @(93, 99, 172, 176, 188, 191, 168, 170, 178)) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $rc = $u.Rows.Count; $cc = $u.Columns.Count
    $lines += ('=== Sheet ' + $si + ' (' + $rc + 'x' + $cc + ')')
    for ($r = 1; $r -le $rc; $r++) {
      $cells = @()
      for ($c = 1; $c -le $cc; $c++) {
        $v = ([string]$u.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') {
          $short = $v -replace "`r`n", '/' -replace "`n", '/'
          if ($short.Length -gt 28) { $short = $short.Substring(0, 28) + '..' }
          $cells += ("[$c]$short")
        }
      }
      if ($cells.Count -gt 0) { $lines += ('  r' + $r + ': ' + ($cells -join ' | ')) }
    }
  }
  # col2 dump for 166, 192, 127, 91, 155 cause rows
  foreach ($si in @(166, 192, 127, 91, 155)) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $rc = $u.Rows.Count
    $lines += ('=== Sheet ' + $si + ' col2/col4/col8 dump (rows 9-' + $rc + ')')
    for ($r = 9; $r -le $rc; $r++) {
      $c2 = ([string]$u.Cells.Item($r, 2).Text).Trim() -replace "`r`n", '/'
      $c4 = ([string]$u.Cells.Item($r, 4).Text).Trim() -replace "`r`n", '/'
      if ($c2 -ne '' -or $c4 -ne '') {
        $c2s = $c2; if ($c2s.Length -gt 40) { $c2s = $c2s.Substring(0, 40) + '..' }
        $c4s = $c4; if ($c4s.Length -gt 40) { $c4s = $c4s.Substring(0, 40) + '..' }
        $lines += ('  r' + $r + ' c2=[' + $c2s + '] c4=[' + $c4s + ']')
      }
    }
  }
  $wb.Close($false)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_prob2.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'PROB2 DONE'