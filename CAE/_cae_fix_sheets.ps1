$outPath = 'C:\VSCode\CAE\TX.CAE-0001_3_ER-20260717_EBSM_Перечень блокировок.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
try {
  $wb = $excel.Workbooks.Open($outPath)
  foreach ($ws in @($wb.Worksheets)) {
    if ($ws.Name -eq 'Лист1' -or $ws.Name -eq 'Sheet1') { $ws.Delete() }
  }
  $names = @(); foreach ($ws in $wb.Worksheets) { $names += $ws.Name }
  $wb.Save()
  $wb.Close($false)
  Write-Output ('SHEETS NOW: ' + ($names -join ' | '))
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}