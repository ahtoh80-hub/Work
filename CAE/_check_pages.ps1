$path = 'C:\VSCode\CAE\CAE_OSBL_PDF_страницы.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($path, 0, $true)
  $lines += ('sheets: ' + $wb.Worksheets.Count)
  $toc = $wb.Worksheets.Item('Оглавление')
  $lines += ('TOC rows: ' + $toc.UsedRange.Rows.Count)
  foreach ($nm in @('P001', 'P005', 'P007', 'P093', 'P176', 'P166', 'P161')) {
    $ws = $wb.Worksheets.Item($nm)
    $u = $ws.UsedRange
    $lines += ('=== ' + $nm + ' (' + $u.Rows.Count + 'x' + $u.Columns.Count + ')')
    $maxR = [Math]::Min($u.Rows.Count, 9)
    for ($r = 1; $r -le $maxR; $r++) {
      $cells = @()
      $maxC = [Math]::Min($u.Columns.Count, 14)
      for ($c = 1; $c -le $maxC; $c++) {
        $v = ([string]$u.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') {
          $s = $v -replace "`r`n", '/'
          if ($s.Length -gt 24) { $s = $s.Substring(0, 24) + '..' }
          $cells += ("[$c]$s")
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
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_pages_check.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'PAGES CHECK DONE'