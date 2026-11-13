# Write the prepared rows into a formatted Excel workbook.
$ErrorActionPreference = 'Stop'
$prepPath = Join-Path $env:TEMP 'cae_prep.json'
$p = Get-Content $prepPath -Raw -Encoding UTF8 | ConvertFrom-Json
$outPath = $p.out

function Add-Sheet($wb, $name, $headers, $rows, $wrapCols, $widths) {
  $ws = $wb.Worksheets.Add([Type]::Missing, $wb.Worksheets.Item($wb.Worksheets.Count))
  $ws.Name = $name
  $n = $rows.Count + 1
  $c = $headers.Count
  $arr = [Array]::CreateInstance([object], [int[]]@($n, $c), [int[]]@(1, 1))
  for ($j = 0; $j -lt $c; $j++) { $arr.SetValue($headers[$j], 1, ($j+1)) }
  for ($i = 0; $i -lt $rows.Count; $i++) {
    for ($j = 0; $j -lt $c; $j++) { $arr.SetValue($rows[$i][$j], ($i+2), ($j+1)) }
  }
  $rng = $ws.Range($ws.Cells(1, 1), $ws.Cells($n, $c))
  $rng.Value2 = $arr
  $rng.HorizontalAlignment = -4131  # xlLeft
  $rng.VerticalAlignment = -4160    # xlTop
  $rng.WrapText = $false
  $hdr = $ws.Range($ws.Cells(1, 1), $ws.Cells(1, $c))
  $hdr.Font.Bold = $true
  $hdr.Interior.Color = 15123099  # light gray-blue
  $hdr.HorizontalAlignment = -4108  # xlCenter
  $rng.Borders.LineStyle = 1
  $rng.Borders.Weight = 2
  $ws.Rows.Item(1).RowHeight = 30
  foreach ($wc in $wrapCols) { $ws.Columns.Item($wc).WrapText = $true }
  $ws.Columns.AutoFit() | Out-Null
  foreach ($w in $widths.GetEnumerator()) {
    $colIdx = [int]([string]$w.Key)
    if ($ws.Columns.Item($colIdx).ColumnWidth -gt [double]$w.Value) { $ws.Columns.Item($colIdx).ColumnWidth = [double]$w.Value }
  }
  $ws.Activate()
  $excel.ActiveWindow.SplitRow = 1
  $excel.ActiveWindow.FreezePanes = $true
  $ws.Range($ws.Cells(1, 1), $ws.Cells(1, $c)).AutoFilter() | Out-Null
  return $ws
}

$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
try {
  $wb = $excel.Workbooks.Add()
  while ($wb.Worksheets.Count -gt 1) { $wb.Worksheets.Item($wb.Worksheets.Count).Delete() }

  Add-Sheet $wb 'Перечень блокировок' @($p.h1) @($p.r1) @(2,4,5,6,8,9,10,11,12,13,14,16,17) @{'5'=30;'6'=30;'7'=20;'9'=45;'10'=45;'11'=28;'12'=28;'13'=28;'14'=28;'16'=60;'17'=60} | Out-Null
  Add-Sheet $wb 'Причины и следствия' @($p.h2) @($p.r2) @(4,5,7,8,10,11,12,13) @{'4'=30;'5'=30;'7'=40;'8'=40;'10'=35;'11'=35;'12'=15;'13'=15} | Out-Null
  Add-Sheet $wb 'Реестр систем' @($p.h3) @($p.r3) @(3,4,6) @{'3'=45;'4'=45;'6'=30} | Out-Null
  Add-Sheet $wb 'Теги причин и следствий' @($p.h5) @($p.r5) @(2,7,8,11,12,16,17,24,30) @{'6'=15;'7'=28;'8'=28;'9'=20;'11'=38;'12'=38;'13'=16;'14'=20;'15'=20;'16'=28;'17'=28;'18'=13;'19'=13;'21'=16;'22'=14;'23'=18;'24'=35;'25'=12;'26'=13;'27'=14;'28'=24;'29'=30;'30'=16;'31'=14;'32'=18;'33'=14;'34'=24} | Out-Null
  Add-Sheet $wb 'Сводка' @($p.h4) @($p.r4) @(2) @{'1'=45;'2'=100} | Out-Null

  foreach ($ws in @($wb.Worksheets)) {
    if ($ws.Name -eq 'Лист1' -or $ws.Name -eq 'Sheet1') { $ws.Delete() }
  }

  if (Test-Path $outPath) { Remove-Item -LiteralPath $outPath -Force }
  $wb.SaveAs($outPath, 51)
  $wb.Close($false)
  Write-Output ("SAVED -> " + $outPath)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}