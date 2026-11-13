$outPath = 'C:\VSCode\CAE\TX.CAE-0001_3_ER-20260717_EBSM_Перечень блокировок.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  $lines = @()
  foreach ($ws in $wb.Worksheets) { $lines += ($ws.Name + ' rows=' + $ws.UsedRange.Rows.Count + ' cols=' + $ws.UsedRange.Columns.Count) }
  $w5 = $wb.Worksheets.Item('Теги причин и следствий')
  $cnt = @{ X = 0; I = 0; P = 0; c = 0; e = 0 }
  $last = $w5.UsedRange.Rows.Count
  $t = $w5.Range($w5.Cells(2, 1), $w5.Cells($last, 2)).Value2
  for ($i = 1; $i -le $t.GetLength(0); $i++) {
    $ty = [string]$t.GetValue($i, 1)
    $mk = ([string]$t.GetValue($i, 2)).Trim()
    if ($ty -eq 'Причина') { $cnt['c']++ } elseif ($ty -eq 'Следствие') { $cnt['e']++ }
    if ($mk -ne '') { $cnt[$mk]++ }
  }
  $lines += ('типы: строк-тегов причин=' + $cnt['c'] + ', строк-следствий=' + $cnt['e'] + '; метки: X=' + $cnt['X'] + ', I=' + $cnt['I'] + ', P=' + $cnt['P'])
  # lookup stats: col27 'Файл ТЕХНОЛ_ОБЪЕКТЫ' (main), col28 'Способ поиска'
  $vals = $w5.Range($w5.Cells(2, 27), $w5.Cells($last, 27)).Value2
  $nd = 0; $found = 0; $blank = 0
  for ($i = 1; $i -le $vals.GetLength(0); $i++) {
    $v = ([string]$vals.GetValue($i, 1)).Trim()
    if ($v -eq '') { $blank++ } elseif ($v -eq 'Нет данных') { $nd++ } else { $found++ }
  }
  $lines += ('lookup (col27 Файл): найдено=' + $found + ', нет данных=' + $nd + ', пусто=' + $blank)
  $vals2 = $w5.Range($w5.Cells(2, 28), $w5.Cells($last, 28)).Value2
  $byPair = 0; $direct = 0; $nf = 0
  for ($i = 1; $i -le $vals2.GetLength(0); $i++) {
    $v = ([string]$vals2.GetValue($i, 1)).Trim()
    if ($v -eq '') { continue }
    elseif ($v -like 'По парному тегу*') { $byPair++ }
    elseif ($v -eq 'не найдено') { $nf++ }
    else { $direct++ }
  }
  $lines += ('способы: прямой=' + $direct + ', по парному=' + $byPair + ', не найдено=' + $nf)
  foreach ($r in 2, 3, 4, 6, 5383) {
    $cells = @()
    foreach ($j in 1,2,4,12,13,14,19,20,21,22,23,24,25,26,27,28) { $cells += (([string]$w5.Cells.Item($r, $j).Text) -replace "`n", '/') }
    $lines += ('row ' + $r + ': ' + ($cells -join ' | '))
  }
  $wb.Close($false)
  [System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_final_check2.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
  Write-Output 'CHECK2 WRITTEN'
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}