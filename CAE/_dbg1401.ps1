$ErrorActionPreference = 'Stop'
. 'C:\VSCode\CAE\_pos_strategy.ps1'
$src = 'C:\VSCode\CAE\1401 Cause & Effect Code A_1.pdf'
$tagRx = '^\d{4}-[A-Z]|^[A-Z]{2,7}-\d{2,6}[A-Z0-9/]*\s*$'
$reader = New-Object iTextSharp.text.pdf.PdfReader($src)
$parser = New-Object iTextSharp.text.pdf.parser.PdfReaderContentParser($reader)
$lines = @()
foreach ($pg in @(2)) {
  $st = New-Object PosTextStrategy
  [void]$parser.ProcessContent($pg, $st)
  $words = New-Object 'System.Collections.Generic.List[object]'
  for ($i = 0; $i -lt $st.Txt.Count; $i++) {
    $tw = $st.Txt[$i].Trim()
    if ($tw -ne '') { [void]$words.Add([pscustomobject]@{ t = $tw; xa = $st.Pos[$i][0]; xb = $st.Pos[$i][2]; y1 = $st.Pos[$i][1]; y2 = $st.Pos[$i][3] }) }
  }
  $xcl = New-Object 'System.Collections.Generic.List[object]'
  foreach ($w in $words) {
    $cx = ($w.xa + $w.xb) / 2.0
    $pl = $false
    foreach ($cl in $xcl) { if ([Math]::Abs($cl.c - $cx) -le 6.0) { [void]$cl.items.Add($w); $cl.c = ($cl.c * ($cl.items.Count - 1) + $cx) / $cl.items.Count; $pl = $true; break } }
    if (-not $pl) { $nc = New-Object psobject -Property @{ c = $cx; items = (New-Object 'System.Collections.Generic.List[object]') }; [void]$nc.items.Add($w); [void]$xcl.Add($nc) }
  }
  $ycl = New-Object 'System.Collections.Generic.List[object]'
  foreach ($w in $words) {
    $cy = ($w.y1 + $w.y2) / 2.0
    $pl = $false
    foreach ($cl in $ycl) { if ([Math]::Abs($cl.c - $cy) -le 9.0) { [void]$cl.items.Add($w); $cl.c = ($cl.c * ($cl.items.Count - 1) + $cy) / $cl.items.Count; $pl = $true; break } }
    if (-not $pl) { $nc = New-Object psobject -Property @{ c = $cy; items = (New-Object 'System.Collections.Generic.List[object]') }; [void]$nc.items.Add($w); [void]$ycl.Add($nc) }
  }
  $xSorted = $xcl | Sort-Object -Property @{ e = { [double]$_.c }; Descending = $true }
  $xi = 0
  foreach ($cl in $xSorted) { $xi++; $cl | Add-Member -NotePropertyName xi -NotePropertyValue $xi -Force; foreach ($w in $cl.items) { $w | Add-Member -NotePropertyName xi -NotePropertyValue $xi -Force } }
  $ySorted = $ycl | Sort-Object -Property @{ e = { [double]$_.c }; Descending = $false }
  $yi = 0
  foreach ($cl in $ySorted) { $yi++; $cl | Add-Member -NotePropertyName yi -NotePropertyValue $yi -Force; foreach ($w in $cl.items) { $w | Add-Member -NotePropertyName yi -NotePropertyValue $yi -Force } }
  $actWords = @('CLOSE','STOP','OPEN','START','RUN','SHUTDOWN','EXPORT')
  $actWords += @(([string][char]0x0417 + [char]0x0410 + [char]0x041A + [char]0x0420 + [char]0x042B + [char]0x0422 + [char]0x042C), ([string][char]0x041E + [char]0x0422 + [char]0x041A + [char]0x0420 + [char]0x042B + [char]0x0422 + [char]0x042C), ([string][char]0x041E + [char]0x0421 + [char]0x0422 + [char]0x0410 + [char]0x041D + [char]0x041E + [char]0x0412 + [char]0x0418 + [char]0x0422 + [char]0x042C), ([string][char]0x041F + [char]0x0423 + [char]0x0421 + [char]0x041A), ([string][char]0x0417 + [char]0x0410 + [char]0x041F + [char]0x0423 + [char]0x0421 + [char]0x041A))
  $lines += ('=== page ' + $pg + ' xcl=' + $xcl.Count + ' ycl=' + $ycl.Count)
  $actYi = 0
  foreach ($cl in $ySorted) { $k = 0; foreach ($w in $cl.items) { $u = $w.t.ToUpperInvariant(); if ($actWords -contains $u) { $k++ } }; if ($k -ge 3) { $lines += ('  ACTION-line yi=' + $cl.yi + ' y=' + [math]::Round($cl.c) + ' acts=' + $k); $actYi = [int]$cl.yi; break } }
  foreach ($cl in $ySorted) {
    $k45 = 0; foreach ($w in $cl.items) { if ([int]$w.xi -ge 45) { $k45++ } }
    $has2oo = $false; foreach ($w in $cl.items) { if ($w.t -match '^2oo') { $has2oo = $true } }
    $hasTag = $false; foreach ($w in $cl.items) { if (([int]$w.xi -ge 45) -and ($w.t -match $tagRx)) { $hasTag = $true } }
    $hasX = $false; foreach ($w in $cl.items) { if ($w.t.ToUpperInvariant() -eq 'X') { $hasX = $true } }
    
    $s26 = ''; foreach ($w in $cl.items) { if ([int]$w.xi -le 27) { $s26 += $w.t } }; $s28 = ''; foreach ($w in $cl.items) { if ([int]$w.xi -ge 28 -and [int]$w.xi -le 33) { $s28 += $w.t } }; $s34 = ''; foreach ($w in $cl.items) { if ([int]$w.xi -ge 34 -and [int]$w.xi -le 47) { $s34 += $w.t } }; $s48 = ''; foreach ($w in $cl.items) { if ([int]$w.xi -ge 48) { $s48 += $w.t } }; if ($s26 -ne '' -or $s28 -ne '' -or $s34 -ne '' -or $s48 -ne '') { $lines += ('  Y' + $cl.yi + ' y=' + [math]::Round($cl.c) + ' A=[' + $s26 + '] B=[' + $s28 + '] C=[' + $s34 + '] D=[' + $s48 + ']') }
  }
}
$reader.Close()
[System.IO.File]::WriteAllText('C:\Users\Reshetov\AppData\Local\Temp\cae_1401_dbg.txt', ($lines -join [Environment]::NewLine), [System.Text.Encoding]::UTF8)
Write-Output ('DBG DONE, rows: ' + $lines.Count)