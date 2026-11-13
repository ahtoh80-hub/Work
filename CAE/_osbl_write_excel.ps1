# OSBL: Write the interlock list to Excel
$ErrorActionPreference = 'Stop'
$prepPath = Join-Path $env:TEMP 'cae_osbl_prep.json'
$p = Get-Content $prepPath -Raw -Encoding UTF8 | ConvertFrom-Json
$outPath = $p.out
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
try {
  $wb = $excel.Workbooks.Add()
  while ($wb.Worksheets.Count -gt 1) { $wb.Worksheets.Item($wb.Worksheets.Count).Delete() }
  $ws = $wb.Worksheets.Item(1)
  $ws.Name = 'Перечень блокировок'
  $n = @($p.r1).Count + 1
  $c = @($p.h1).Count
  $arr = [Array]::CreateInstance([object], [int[]]@($n, $c), [int[]]@(1, 1))
  for ($j = 0; $j -lt $c; $j++) { $arr.SetValue($p.h1[$j], 1, ($j+1)) }
  for ($i = 0; $i -lt @($p.r1).Count; $i++) {
    for ($j = 0; $j -lt $c; $j++) { $arr.SetValue($p.r1[$i][$j], ($i+2), ($j+1)) }
  }
  $rng = $ws.Range($ws.Cells(1, 1), $ws.Cells($n, $c))
  $rng.Value2 = $arr
  $rng.HorizontalAlignment = -4131; $rng.VerticalAlignment = -4160
  $hdr = $ws.Range($ws.Cells(1, 1), $ws.Cells(1, $c))
  $hdr.Font.Bold = $true; $hdr.Interior.Color = 15123099; $hdr.HorizontalAlignment = -4108
  $rng.Borders.LineStyle = 1; $rng.Borders.Weight = 2
  $ws.Rows.Item(1).RowHeight = 30
  $ws.Columns.AutoFit() | Out-Null
  $ws.Activate(); $excel.ActiveWindow.SplitRow = 1; $excel.ActiveWindow.FreezePanes = $true
  $ws.Columns.Item(5).ColumnWidth = 30
  $ws.Columns.Item(6).ColumnWidth = 20
  $ws.Range($ws.Cells(1, 1), $ws.Cells(1, $c)).AutoFilter() | Out-Null
  foreach ($ws2 in @($wb.Worksheets)) { if ($ws2.Name -eq 'Лист1' -or $ws2.Name -eq 'Sheet1') { $ws2.Delete() } }
  if (Test-Path $outPath) { Remove-Item -LiteralPath $outPath -Force }
  $wb.SaveAs($outPath, 51)
  $wb.Close($false)
  Write-Output ("SAVED -> " + $outPath)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}