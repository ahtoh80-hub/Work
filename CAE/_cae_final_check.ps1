$outPath = 'C:\VSCode\CAE\TX.CAE-0001_3_ER-20260717_EBSM_Перечень блокировок.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
try {
  $wb = $excel.Workbooks.Open($outPath, 0, $true)
  $lines = @()
  foreach ($ws in $wb.Worksheets) {
    $u = $ws.UsedRange
    $lines += ("SHEET '" + $ws.Name + "' rows=" + $u.Rows.Count + " cols=" + $u.Columns.Count)
  }
  $ws1 = $wb.Worksheets.Item('Перечень блокировок')
  $ws2 = $wb.Worksheets.Item('Причины и следствия')
  $ws4 = $wb.Worksheets.Item('Сводка')
  # mark counts on long sheet (col 14)
  $cnt = @{ X = 0; I = 0; P = 0 }
  $last = $ws2.UsedRange.Rows.Count
  $vals = $ws2.Range($ws2.Cells(2, 14), $ws2.Cells($last, 14)).Value2
  if ($last -ge 2) {
    if ($last -eq 2) { $m = [string]$vals; $cnt[$m]++ }
    else { for ($i = 1; $i -le $vals.Length; $i++) { $m = ([string]$vals[$i, 1]).Trim(); if ($cnt.ContainsKey($m)) { $cnt[$m]++ } } }
  }
  $lines += ("crossings: X=" + $cnt['X'] + " I=" + $cnt['I'] + " P=" + $cnt['P'] + " total=" + ($cnt['X'] + $cnt['I'] + $cnt['P']))
  # samples
  foreach ($r in 2, 100, 400, 700) {
    $a = $ws1.Range($ws1.Cells($r, 1), $ws1.Cells($r, 16)).Value2
    $c1 = [string]$a.GetValue(1, 1); $c2 = [string]$a.GetValue(1, 2); $c3 = [string]$a.GetValue(1, 3); $c4 = [string]$a.GetValue(1, 4)
    $lines += ("LIST row $r : " + $c1 + ' | ' + $c2 + ' | ' + $c3 + ' | ' + $c4)
    $e = ([string]$a.GetValue(1, 15)) -replace "`n", ' ;; '
    $lines += ("   effectsEN: " + $e.Substring(0, [Math]::Min(160, $e.Length)))
  }
  foreach ($r in 2, 2000) {
    $a = $ws2.Range($ws2.Cells($r, 1), $ws2.Cells($r, 14)).Value2
    $cells = @(); for ($j = 1; $j -le 14; $j++) { $cells += (([string]$a.GetValue(1, $j)) -replace "`n", '/') }
    $lines += ("LONG row $r : " + ($cells -join ' | '))
  }
  $sv = $ws4.UsedRange
  for ($r = 1; $r -le $sv.Rows.Count; $r++) {
    $lines += ('SV: ' + ([string]$sv.Cells.Item($r, 1).Text) + ' = ' + ([string]$sv.Cells.Item($r, 2).Text))
  }
  $wb.Close($false)
  [System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_final_check.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
  Write-Output 'CHECK WRITTEN'
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}