$outPath = 'C:\VSCode\CAE\CAE_OSBL_exel.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  foreach ($si in @(137, 159, 166, 167, 176, 188, 191, 192, 193, 161, 164)) {
    $ws = $wb.Worksheets.Item($si)
    $u = $ws.UsedRange
    $rc = $u.Rows.Count; $cc = $u.Columns.Count
    $lines += ('=== Sheet ' + $si + ' (' + $rc + 'x' + $cc + ')')
    $grid = @{}
    for ($r = 1; $r -le [Math]::Min($rc, 25); $r++) {
      for ($c = 1; $c -le [Math]::Min($cc, 60); $c++) {
        $v = ([string]$u.Cells.Item($r, $c).Text).Trim()
        if ($v -ne '') { $grid["$r`:$c"] = $v }
      }
    }
    for ($r = 1; $r -le [Math]::Min($rc, 14); $r++) {
      $cells = @()
      for ($c = 1; $c -le [Math]::Min($cc, 60); $c++) {
        $v = $grid["$r`:$c"]
        if ($v) {
          $short = $v; if ($short.Length -gt 25) { $short = $short.Substring(0, 25) + '..' }
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
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_other_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'OTHER PROBE DONE'