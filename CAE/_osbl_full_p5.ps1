$outPath = 'C:\VSCode\CAE\CAE_OSBL_Перечень блокировок.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
function Write-Sheet($wb, [string]$name, [string[]]$hdrs, [object[]]$dataRows) {
  $ws = $wb.Worksheets.Add([Type]::Missing, $wb.Worksheets.Item($wb.Worksheets.Count))
  $ws.Name = $name
  $n = $dataRows.Count + 1; $c = $hdrs.Count
  $arr = [Array]::CreateInstance([object], [int[]]@($n, $c), [int[]]@(1, 1))
  for ($j = 0; $j -lt $c; $j++) { $arr.SetValue([string]$hdrs[$j], 1, ($j+1)) }
  for ($i = 0; $i -lt $dataRows.Count; $i++) { for ($j = 0; $j -lt $c; $j++) { $arr.SetValue($dataRows[$i][$j], ($i+2), ($j+1)) } }
  $rng = $ws.Range($ws.Cells(1, 1), $ws.Cells($n, $c)); $rng.Value2 = $arr
  $rng.HorizontalAlignment = -4131; $rng.VerticalAlignment = -4160
  $hdr = $ws.Range($ws.Cells(1, 1), $ws.Cells(1, $c)); $hdr.Font.Bold = $true; $hdr.Interior.Color = 15123099; $hdr.HorizontalAlignment = -4108
  $rng.Borders.LineStyle = 1; $rng.Borders.Weight = 2
  $ws.Rows.Item(1).RowHeight = 30
  foreach ($wc in @(4,5,6,8,9,10,11,12)) { $ws.Columns.Item($wc).WrapText = $true }
  $ws.Columns.AutoFit() | Out-Null
  $ws.Activate(); $excel.ActiveWindow.SplitRow = 1; $excel.ActiveWindow.FreezePanes = $true
  $ws.Range($ws.Cells(1, 1), $ws.Cells(1, $c)).AutoFilter() | Out-Null
}
try {
  $wb = $excel.Workbooks.Add()
  while ($wb.Worksheets.Count -gt 1) { $wb.Worksheets.Item($wb.Worksheets.Count).Delete() }
  $r1Arr = $r1; $r2Arr = $r2; $r3Arr = $r3; $r4Arr = $r4; $r5Arr = $r5
  $h1Arr = $h1; $h2Arr = $h2; $h3Arr = $h3; $h4Arr = $h4; $h5Arr = $h5
  Write-Sheet $wb 'Перечень блокировок' $h1Arr $r1Arr
  Write-Sheet $wb 'Причины и следствия' $h2Arr $r2Arr
  Write-Sheet $wb 'Реестр систем' $h3Arr $r3Arr
  Write-Sheet $wb 'Теги причин и следствий' $h4Arr $r4Arr
  Write-Sheet $wb 'Сводка' $h5Arr $r5Arr
  foreach ($ws2 in @($wb.Worksheets)) { if ($ws2.Name -eq 'Лист1' -or $ws2.Name -eq 'Sheet1') { $ws2.Delete() } }
  if (Test-Path $outPath) { Remove-Item -LiteralPath $outPath -Force }
  $wb.SaveAs($outPath, 51); $wb.Close($false)
  Write-Output ("SAVED -> " + $outPath)
} finally { $excel.Quit(); [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null }
Write-Output 'OSBL PIPELINE DONE'