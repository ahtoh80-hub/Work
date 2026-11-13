$ErrorActionPreference = 'Stop'
$src = 'C:\VSCode\CAE\CAE_OSBL_PDF_страницы.xlsx'
$dst = 'C:\VSCode\CAE\1401_CAE_PDF_pages.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
try {
  $wbS = $excel.Workbooks.Open($src, 0, $true)
  $wbD = $excel.Workbooks.Open($dst)
  foreach ($nm in @('P007', 'P008', 'P011', 'P012')) {
    $old = $null
    foreach ($wsx in $wbD.Worksheets) { if ($wsx.Name -eq $nm) { $old = $wsx; break } }
    if ($null -ne $old) { $old.Delete() }
    $wbS.Worksheets.Item($nm).Copy($wbD.Worksheets.Item($wbD.Worksheets.Count))
    Write-Output ('copied: ' + $nm)
  }
  $wbD.Save()
  $wbD.Close($false)
  $wbS.Close($false)
  Write-Output 'MIRRORED COPIED'
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}