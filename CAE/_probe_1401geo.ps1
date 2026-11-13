$ErrorActionPreference = 'Stop'
. 'C:\VSCode\CAE\_pos_strategy.ps1'
$src = 'C:\VSCode\CAE\1401 Cause & Effect Code A_1.pdf'
$reader = New-Object iTextSharp.text.pdf.PdfReader($src)
$st = New-Object PosTextStrategy
$parser = New-Object iTextSharp.text.pdf.parser.PdfReaderContentParser($reader)
[void]$parser.ProcessContent(1, $st)
$lines = @()
for ($i = 0; $i -lt $st.Txt.Count; $i++) {
  $x1 = $st.Pos[$i][0]; $y1 = $st.Pos[$i][1]; $x2 = $st.Pos[$i][2]; $y2 = $st.Pos[$i][3]
  if (($x1 -ge 590 -and $x1 -le 616 -and $y1 -ge 530 -and $y1 -le 800) -or ($y1 -ge 480 -and $y1 -le 700 -and [Math]::Abs($x1 - 594) -le 30)) {
    $lines += ('(' + [math]::Round($x1, 1) + ',' + [math]::Round($y1, 1) + ')-(' + [math]::Round($x2, 1) + ',' + [math]::Round($y2, 1) + ') [' + $st.Txt[$i] + ']')
  }
}
$reader.Close()
[System.IO.File]::WriteAllText('C:\Users\Reshetov\AppData\Local\Temp\cae_1401_geo.txt', ($lines -join [Environment]::NewLine), [System.Text.Encoding]::UTF8)
Write-Output ('GEO DONE, rows: ' + $lines.Count)