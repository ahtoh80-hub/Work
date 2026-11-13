$outPath = 'C:\VSCode\CAE\TX.CAE-0001_3_ER-20260717_EBSM_Перечень блокировок.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  $w5 = $wb.Worksheets.Item('Теги причин и следствий')
  $last = $w5.UsedRange.Rows.Count
  $lines = @()
  $colTag = $w5.Range($w5.Cells(2, 14), $w5.Cells($last, 14)).Value2
  $colSys = $w5.Range($w5.Cells(2, 6), $w5.Cells($last, 6)).Value2
  $colCtrlB = $w5.Range($w5.Cells(2, 9), $w5.Cells($last, 9)).Value2
  $colLoop = $w5.Range($w5.Cells(2, 21), $w5.Cells($last, 21)).Value2
  $colFile = $w5.Range($w5.Cells(2, 28), $w5.Cells($last, 28)).Value2
  $colCLoop = $w5.Range($w5.Cells(2, 30), $w5.Cells($last, 30)).Value2
  $targets = @('1115-P-101A','1120-P-203A','1130-P-331A','1110-P-131A','1120-P-003A')
  foreach ($t in $targets) {
    $tu = $t.ToUpperInvariant()
    for ($i = 1; $i -le $colTag.GetLength(0); $i++) {
      if (([string]$colTag.GetValue($i, 1)).Trim().ToUpperInvariant() -eq $tu) {
        $lines += ($t + ' sys=[' + $colSys.GetValue($i, 1) + '] ctrlB=[' + $colCtrlB.GetValue($i, 1) + '] Loop=[' + $colLoop.GetValue($i, 1) + '] File=[' + $colFile.GetValue($i, 1) + '] Kontur=[' + $colCLoop.GetValue($i, 1) + ']')
      }
    }
  }
  $colF = $w5.Range($w5.Cells(2, 28), $w5.Cells($last, 28)).Value2
  $nd = 0; $fd = 0; $bl = 0
  for ($i = 1; $i -le $colF.GetLength(0); $i++) {
    $v = ([string]$colF.GetValue($i, 1)).Trim()
    if ($v -eq '') { $bl++ } elseif ($v -eq 'Нет данных') { $nd++ } else { $fd++ }
  }
  $lines += ('STATS: found=' + $fd + ', net-dannykh=' + $nd + ', blank=' + $bl)
  $wb.Close($false)
  [System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_isys_file_check.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
  Write-Output 'ISYS FILE CHECK DONE'
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}