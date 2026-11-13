$ErrorActionPreference = 'Stop'
. 'C:\VSCode\CAE\_pos_strategy.ps1'
$src = 'C:\VSCode\CAE\1401 Cause & Effect Code A_1.pdf'
$reader = New-Object iTextSharp.text.pdf.PdfReader($src)
$st = New-Object PosTextStrategy
$parser = New-Object iTextSharp.text.pdf.parser.PdfReaderContentParser($reader)
[void]$parser.ProcessContent(2, $st)
$lines = @()
for ($i = 0; $i -lt $st.Txt.Count; $i++) {
  $t2 = $st.Txt[$i]
  if ($t2 -match '[\u0400-\u04FF]' -and $t2.Trim().Length -ge 2) {
    $x1 = $st.Pos[$i][0]; $y1 = $st.Pos[$i][1]; $x2 = $st.Pos[$i][2]; $y2 = $st.Pos[$i][3]
    $dir = 'UP'
    if ($y2 -lt $y1) { $dir = 'DOWN' }
    if ($x2 -gt $x1) { $dir = 'RIGHT' }
    if ($x2 -lt $x1) { $dir = 'LEFT' }
    $lines += ($dir + ' (' + [math]::Round($x1, 0) + ',' + [math]::Round($y1, 0) + ')-(' + [math]::Round($x2, 0) + ',' + [math]::Round($y2, 0) + ') [' + $t2 + ']')
  }
}
$reader.Close()
[System.IO.File]::WriteAllText('C:\Users\Reshetov\AppData\Local\Temp\cae_1401_kir.txt', ($lines -join [Environment]::NewLine), [System.Text.Encoding]::UTF8)
Write-Output ('KIR DONE, rows: ' + $lines.Count)