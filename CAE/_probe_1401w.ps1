$ErrorActionPreference = 'Stop'
$lines = @()
$src = 'C:\VSCode\CAE\1401 Cause & Effect Code A_1.pdf'
$docx = 'C:\VSCode\CAE\1401_CAE_reflow.docx'
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
  $doc = $word.Documents.Open($src, $false, $true)
  $pages = $doc.ComputeStatistics(2)
  $lines += ('word pages: ' + $pages)
  $lines += ('word tables: ' + $doc.Tables.Count)
  $doc.SaveAs2($docx, 16)
  $lines += ('saved: ' + $docx)
  # inspect first tables
  $maxT = [Math]::Min($doc.Tables.Count, 6)
  for ($ti = 1; $ti -le $maxT; $ti++) {
    $tbl = $doc.Tables.Item($ti)
    $rc = $tbl.Rows.Count; $cc = $tbl.Columns.Count
    $pg = 0
    try { $pg = $tbl.Range.Information(3) } catch { }
    $head = ''
    try {
      for ($c = 1; $c -le [Math]::Min($cc, 6); $c++) {
        $t = $tbl.Cell(1, $c).Range.Text -replace "`r`a", '' -replace "`r", ' '
        $head += ('[' + $c + ']' + $t.Trim().Substring(0, [Math]::Min(20, $t.Trim().Length)) + ' ')
      }
    } catch { $head = 'cell-err' }
    $lines += ('  T' + $ti + ' (' + $rc + 'x' + $cc + ') page~' + $pg + ' head: ' + $head)
  }
  $doc.Close($false)
} finally {
  $word.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_1401_word.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'WORD PROBE DONE'