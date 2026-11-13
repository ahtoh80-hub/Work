$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
$lines = @()
try {
  $wb = $excel.Workbooks.Open('C:\VSCode\CAE\1401_CAE_PDF_pages.xlsx', 0, $true)
  $ws = $wb.Worksheets.Item('P001')
  $u = $ws.UsedRange
  $lines += ('used: ' + $u.Rows.Count + 'x' + $u.Columns.Count)
  foreach ($rc in @(@(3,1), @(3,2), @(3,3), @(4,2), @(5,2), @(8,2), @(8,3), @(8,5), @(10,2), @(10,3), @(10,5), @(25,2), @(25,29))) {
    $v2 = [string]$ws.Cells.Item($rc[0], $rc[1]).Value2
    $tx = [string]$ws.Cells.Item($rc[0], $rc[1]).Text
    if ($v2.Length -gt 40) { $v2 = $v2.Substring(0, 40) + '..' }
    $lines += ('r' + $rc[0] + 'c' + $rc[1] + ' v2=[' + $v2 + '] text_len=' + $tx.Length)
  }
  $wb.Close($false)
} finally { $excel.Quit(); [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null }
[System.IO.File]::WriteAllText('C:\Users\Reshetov\AppData\Local\Temp\cae_1401_b.txt', ($lines -join [Environment]::NewLine), [System.Text.Encoding]::UTF8)
Write-Output 'B DONE'