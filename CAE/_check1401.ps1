# 1401 Cause & Effect Code A_1.pdf -> Excel (1 page = 1 sheet), v2 rebuild
$ErrorActionPreference = 'Stop'
$src = 'C:\VSCode\CAE\1401 Cause & Effect Code A_1.pdf'
$outPath = 'C:\VSCode\CAE\1401_CAE_PDF_pages.xlsx'
$dll = 'C:\Users\Reshetov\AppData\Local\Temp\itextsharp\lib\net461\itextsharp.dll'
$bc = 'C:\Users\Reshetov\AppData\Local\Temp\bc2\lib\net461\BouncyCastle.Cryptography.dll'
[void][System.Reflection.Assembly]::LoadFrom($bc)
[void][System.Reflection.Assembly]::LoadFrom($dll)
$csLines = New-Object System.Collections.Generic.List[string]
$csLines.Add('using System;')
$csLines.Add('using System.Collections.Generic;')
$csLines.Add('using iTextSharp.text.pdf;')
$csLines.Add('using iTextSharp.text.pdf.parser;')
$csLines.Add('public class PosTextStrategy : ITextExtractionStrategy {')
$csLines.Add('  public List<float[]> Pos = new List<float[]>();')
$csLines.Add('  public List<string> Txt = new List<string>();')
$csLines.Add('  public void BeginTextBlock() {}')
$csLines.Add('  public void EndTextBlock() {}')
$csLines.Add('  public void RenderImage(ImageRenderInfo renderInfo) {}')
$csLines.Add('  public void RenderText(TextRenderInfo renderInfo) {')
$csLines.Add('    LineSegment seg = renderInfo.GetBaseline();')
$csLines.Add('    Vector p1 = seg.GetStartPoint();')
$csLines.Add('    Vector p2 = seg.GetEndPoint();')
$csLines.Add('    Pos.Add(new float[] { p1[Vector.I1], p1[Vector.I2], p2[Vector.I1], p2[Vector.I2] });')
$csLines.Add('    Txt.Add(renderInfo.GetText());')
$csLines.Add('  }')
$csLines.Add('  public string GetResultantText() { return string.Empty; }')
$csLines.Add('}')
$cs = ($csLines -join [Environment]::NewLine)
Add-Type -TypeDefinition $cs -ReferencedAssemblies @($bc, $dll)
$tagRx = '^\d{4}-[A-Z]|^[A-Z]{2,7}-\d{2,6}[A-Z0-9/]*\s*$'
$actWords = @('CLOSE','STOP','OPEN','START','RUN','SHUTDOWN','EXPORT')
$actWords += @(([string][char]0x0417 + [char]0x0410 + [char]0x041A + [char]0x0420 + [char]0x042B + [char]0x0422 + [char]0x042C), ([string][char]0x041E + [char]0x0422 + [char]0x041A + [char]0x0420 + [char]0x042B + [char]0x0422 + [char]0x042C), ([string][char]0x041E + [char]0x0421 + [char]0x0422 + [char]0x0410 + [char]0x041D + [char]0x041E + [char]0x0412 + [char]0x0418 + [char]0x0422 + [char]0x042C), ([string][char]0x041F + [char]0x0423 + [char]0x0421 + [char]0x041A), ([string][char]0x0417 + [char]0x0410 + [char]0x041F + [char]0x0423 + [char]0x0421 + [char]0x041A))
$revWord = [string][char]0x0420 + [char]0x0415 + [char]0x0412
function Extract-Page($reader, $parser, $pi) {
  $st = New-Object PosTextStrategy
  [void]$parser.ProcessContent($pi, $st)
  $words = New-Object 'System.Collections.Generic.List[object]'
  for ($i = 0; $i -lt $st.Txt.Count; $i++) {
    $tw = $st.Txt[$i].Trim()
    if ($tw -ne '') { [void]$words.Add([pscustomobject]@{ t = $tw; xa = $st.Pos[$i][0]; xb = $st.Pos[$i][2]; y1 = $st.Pos[$i][1]; y2 = $st.Pos[$i][3] }) }
  }
  if ($words.Count -eq 0) { return @{ kind = 'EMPTY'; words = $words; xSorted = $null; ySorted = $null } }
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
  return @{ kind = 'DATA'; words = $words; xSorted = $xSorted; ySorted = $ySorted }
}$script:diag = New-Object 'System.Collections.Generic.List[string]'
function Build-PageRows($ex, $pi) {
  $xSorted = $ex.xSorted; $ySorted = $ex.ySorted
  $rows = New-Object 'System.Collections.Generic.List[object]'
  $actYi = 0; $maxActs = 0
  $yArr = @($ySorted)
  for ($ci = 0; $ci -lt $yArr.Count; $ci++) {
    $s26 = ''
    for ($oj = 0; $oj -le 2; $oj++) { $j = $ci + $oj; if ($j -ge $yArr.Count) { break }; foreach ($w in $yArr[$j].items) { if ([int]$w.xi -le 40) { $s26 += $w.t } } }
    $u26 = $s26.ToUpperInvariant()
    $k = 0
    foreach ($aw in $actWords) { $k += ([regex]::Matches($u26, [regex]::Escape($aw))).Count }
    if ($k -gt $maxActs) { $maxActs = $k; $actYi = [int]$yArr[$ci].yi }
  }
  $header = $null
  foreach ($cl in $ySorted) {
    if (($actYi -gt 0) -and ([int]$cl.yi -ge $actYi)) { continue }
    $s45 = ''
    foreach ($w in $cl.items) { if ([int]$w.xi -ge 40) { $s45 += $w.t } }
    $u45 = $s45.ToUpperInvariant()
    $ok = ($u45 -match 'REV') -or ($u45 -match ('^|[^A-Z]' + $revWord)) -or (($u45 -match 'G-NO') -and ($u45 -match 'TA')) -or (($u45 -match [string][char]0x2116) -and ($u45 -match 'TX'))
    if ($ok) { if ($null -eq $header) { $header = $cl } else { if ([int]$cl.yi -gt [int]$header.yi) { $header = $cl } } }
  }
  if ($null -eq $header) { $script:diag.Add('NO-HEADER p' + $pi + ' actYi=' + $actYi); return @{ kind = 'EMPTY'; rows = $null; title = '' } }
  $revX = 0; $tagMinX = 9999; $tagMaxX = 0
  foreach ($w in $header.items) {
    $uxi = [int]$w.xi
    if ($w.t.ToUpperInvariant() -match ('^(REV|' + $revWord + ')')) { if ($uxi -gt $revX) { $revX = $uxi } }
  }
  foreach ($w in $header.items) { $uxi = [int]$w.xi; if ($uxi -gt 40 -and $uxi -lt $revX) { if ($uxi -lt $tagMinX) { $tagMinX = $uxi }; if ($uxi -gt $tagMaxX) { $tagMaxX = $uxi } } }
  if ($revX -eq 0) { $revX = $tagMaxX + 2 }
  if ($tagMaxX -eq 0) { $tagMinX = [Math]::Max(1, $revX - 6); $tagMaxX = $revX - 1 }
  $voteX = 0
  foreach ($cl in $xSorted) { foreach ($w in $cl.items) { if ($w.t -match '^2oo') { $voteX = [int]$cl.xi; break } }; if ($voteX -gt 0) { break } }
  $effList = New-Object 'System.Collections.Generic.List[object]'
  foreach ($cl in $xSorted) {
    if ($voteX -gt 0 -and [int]$cl.xi -ge $voteX) { continue }
    $hasX = $false; $hasAct = $false
    foreach ($w in $cl.items) { $u = $w.t.ToUpperInvariant(); if ($u -eq 'X' -or $u -eq 'I' -or $u -eq 'P') { $hasX = $true }; if ($actWords -contains $u) { $hasAct = $true } }
    if ($hasX -or $hasAct) { [void]$effList.Add($cl) }
  }
  $effSorted = $effList | Sort-Object -Property @{ e = { [int]$_.xi }; Descending = $true }
  $effInfo = New-Object 'System.Collections.Generic.List[object]'
  foreach ($cl in $effSorted) {
    $parts = $cl.items | Sort-Object -Property @{ e = { [double]$_.y1 }; Descending = $false }
    $s = ''
    foreach ($w in $parts) { $s += $w.t }
    $act = ''
    foreach ($w in $parts) { $u = $w.t.ToUpperInvariant(); if ($actWords -contains $u) { $act = $w.t; break } }
    $tag = ''
    $ms = [regex]::Matches($s, '\d{4}-[A-Z][A-Z0-9]*(?:-[A-Z0-9/]+)+')
    if ($ms.Count -gt 0) { $tag = $ms[$ms.Count - 1].Value }
    $desc = $s
    if ($act -ne '') { $ix = $s.IndexOf($act); if ($ix -ge 0) { $desc = $s.Substring($ix + $act.Length) } }
    if ($tag -ne '' -and $desc.EndsWith($tag)) { $desc = $desc.Substring(0, $desc.Length - $tag.Length) }
    $desc = $desc.Trim('-').Trim()
    [void]$effInfo.Add([pscustomobject]@{ tag = $tag; desc = $desc; act = $act })
  }
  $causes = New-Object 'System.Collections.Generic.List[object]'
  foreach ($cl in $ySorted) {
    if ([int]$cl.yi -gt [int]$header.yi) { continue }
    $hasVote = $false; $hasTag = $false; $hasX = $false
    foreach ($w in $cl.items) {
      if ($w.t -match '^2oo') { $hasVote = $true }
      $uxi = [int]$w.xi
      if ($w.t -match $tagRx) { $hasTag = $true }
      if ($w.t.ToUpperInvariant() -eq 'X') { $hasX = $true }
    }
    $sAll = ''
    foreach ($w in $cl.items) { $sAll += $w.t }
    if ($sAll.ToUpperInvariant() -match 'ANDEFFECT') { continue }
    if (-not ($hasVote -or $hasTag -or $hasX)) { continue }
    if ($cl.items.Count -eq 1 -and ([string]$cl.items[0].t) -match '^\d+\s+of\s+\d+$') { continue }
    $tag = ''; $desc = ''; $notes = ''
    $tp = @(); $dp = @()
    foreach ($w in $cl.items) {
      $uxi = [int]$w.xi
      if ($tagMaxX -gt 0 -and $uxi -ge $tagMinX -and $uxi -le $tagMaxX) { $tp += $w }
      if ($voteX -gt 0 -and $uxi -eq $voteX) { $notes = $w.t }
      if ($voteX -gt 0 -and $uxi -gt $voteX -and $uxi -lt $tagMinX) { $dp += $w }
    }
    $tp2 = $tp | Sort-Object -Property @{ e = { [double]$_.xa }; Ascending = $true }
    foreach ($w in $tp2) { $tag += $w.t }
    $dp2 = $dp | Sort-Object -Property @{ e = { [double]$_.xa }; Ascending = $true }
    foreach ($w in $dp2) { $desc += $w.t }
    [void]$causes.Add([pscustomobject]@{ yi = [int]$cl.yi; tag = $tag; desc = $desc; notes = $notes })
  }
  $causes2 = $causes | Sort-Object -Property @{ e = { [int]$_.yi }; Descending = $true }
  $title = ''
  $tCl = $ySorted | Sort-Object -Property @{ e = { [int]$_.yi }; Descending = $true }
  $tParts = New-Object 'System.Collections.Generic.List[string]'
  foreach ($cl in $tCl) {
    if ([int]$cl.yi -lt ([int]$header.yi + 2)) { continue }
    $tp3 = $cl.items | Where-Object { [int]$_.xi -ge ($voteX + 1) } | Sort-Object -Property @{ e = { [double]$_.xa }; Ascending = $true }
    $s2 = ''
    foreach ($w in $tp3) { $s2 += $w.t }
    $s2 = $s2.Trim()
    if ($s2 -ne '') { [void]$tParts.Add($s2) }
  }
  $title = ($tParts -join ' ').Trim()
  [void]$rows.Add(@($title))
  [void]$rows.Add(@(''))
  $r3 = @('TAG-No.'); $r4 = @('Description'); $r5 = @('Action')
  foreach ($e in $effInfo) { $r3 += $e.tag; $r4 += $e.desc; $r5 += $e.act }
  [void]$rows.Add($r3); [void]$rows.Add($r4); [void]$rows.Add($r5)
  [void]$rows.Add(@('Cause'))
  [void]$rows.Add(@('Rev.', 'TAG-No.', 'Description', 'Notes'))
  $effXi = @()
  foreach ($cl in $effSorted) { $effXi += [int]$cl.xi }
  $markMap = @{}
  foreach ($w in $ex.words) { $u = $w.t.ToUpperInvariant(); if ($u -in @('X','I','P','O','C','R','S')) { $markMap[[string]$w.xi + ':' + [string]$w.yi] = $u } }
  foreach ($c in $causes2) {
    $marks = @()
    foreach ($exi in $effXi) { $mk = ''; $kk = [string]$exi + ':' + [string]$c.yi; if ($markMap.ContainsKey($kk)) { $mk = $markMap[$kk] }; $marks += $mk }
    $line = @('', $c.tag, $c.desc, $c.notes) + $marks
    [void]$rows.Add($line)
  }
  $script:diag.Add('OK p' + $pi + ' eff=' + $effInfo.Count + ' causes=' + $causes2.Count + ' actYi=' + $actYi + ' hdrYi=' + $header.yi + ' voteX=' + $voteX)
  return @{ kind = 'DATA'; rows = $rows; title = $title }
}$reader = New-Object iTextSharp.text.pdf.PdfReader($src)
$parser = New-Object iTextSharp.text.pdf.parser.PdfReaderContentParser($reader)
$nPages = $reader.NumberOfPages
$prepared = New-Object 'System.Collections.Generic.List[object]'
$meta = New-Object 'System.Collections.Generic.List[object]'
for ($pi = 1; $pi -le $nPages; $pi++) {
  try {
    $ex = Extract-Page $reader $parser $pi
    if ($ex.kind -eq 'EMPTY') { [void]$prepared.Add([pscustomobject]@{ page = $pi; kind = 'EMPTY'; rows = $null; title = '' }); [void]$meta.Add([pscustomobject]@{ page = $pi; causes = 0; effects = 0; title = '(empty page)' }); continue }
    $res = Build-PageRows $ex $pi
    if ($pi -le 3) { Write-Host ("MAIN p" + $pi + " kind=" + $res.kind + " rows=" + $res.rows.Count + " c0=" + $res.rows[0].Count + " c2=" + $res.rows[2].Count) }
    if ($res.kind -ne 'DATA') { [void]$prepared.Add([pscustomobject]@{ page = $pi; kind = 'EMPTY'; rows = $null; title = '' }); [void]$meta.Add([pscustomobject]@{ page = $pi; causes = 0; effects = 0; title = '(no header found)' }); continue }
    $nc = 0; $ne = 0
    if ($null -ne $res.rows -and $res.rows.Count -ge 7) { $nc = $res.rows.Count - 7; $ne = $res.rows[2].Count - 1 }
    [void]$prepared.Add([pscustomobject]@{ page = $pi; kind = 'DATA'; rows = $res.rows; title = $res.title })
    [void]$meta.Add([pscustomobject]@{ page = $pi; causes = $nc; effects = $ne; title = $res.title })
  } catch {
    [void]$prepared.Add([pscustomobject]@{ page = $pi; kind = 'ERROR'; rows = $null; title = '' })
    [void]$meta.Add([pscustomobject]@{ page = $pi; causes = 0; effects = 0; title = ('ERROR: ' + $_.Exception.Message) })
  }
}
$reader.Close()
[System.IO.File]::WriteAllText('C:\Users\Reshetov\AppData\Local\Temp\diag1401.txt', ($script:diag -join [Environment]::NewLine), [System.Text.Encoding]::UTF8)
Write-Output ('pages processed: ' + $prepared.Count)
$out = New-Object -ComObject Excel.Application
$out.Visible = $false; $out.DisplayAlerts = $false
try {
  $wbN = $out.Workbooks.Add()
  while ($wbN.Worksheets.Count -gt 1) { $wbN.Worksheets.Item($wbN.Worksheets.Count).Delete() }
  $toc = $wbN.Worksheets.Item(1)
  $toc.Name = 'Contents'
  foreach ($p in $prepared) {
    $ws = $wbN.Worksheets.Add([Type]::Missing, $wbN.Worksheets.Item($wbN.Worksheets.Count))
    $ws.Name = ('P' + $p.page.ToString('000'))
    if ($p.kind -ne 'DATA') {
      $ws.Cells.Item(1, 1) = ('Page ' + $p.page + ' (' + $p.kind + '): ' + $p.title)
      $ws.Cells.Item(1, 1).Font.Italic = $true
      continue
    }
    $rows = $p.rows
    Write-Host ("WB p" + $p.page + " kind=" + $p.kind + " n=" + $rows.Count + " c0=" + $rows[0].Count + " c2=" + $rows[2].Count)
    $n = $rows.Count
    $maxc = 1
    foreach ($line in $rows) { if ($line.Count -gt $maxc) { $maxc = $line.Count } }
    $arr = [Array]::CreateInstance([object], [int[]]@($n, $maxc), [int[]]@(1, 1))
    for ($i = 0; $i -lt $n; $i++) {
      $line = $rows[$i]
      for ($j = 0; $j -lt $line.Count; $j++) { $arr.SetValue([string]$line[$j], ($i + 1), ($j + 1)) }
    }
    $rng = $ws.Range($ws.Cells.Item(1, 1), $ws.Cells.Item($n, $maxc))
    $rng.Value2 = $arr
    $rng.Borders.LineStyle = 1; $rng.Borders.Weight = 2
    $rng.HorizontalAlignment = -4131; $rng.VerticalAlignment = -4160
    $hdr = $ws.Range($ws.Cells.Item(1, 1), $ws.Cells.Item(7, $maxc))
    $hdr.Font.Bold = $true
    $hdr.Interior.Color = 15123099
    $ws.Rows.Item(1).RowHeight = 30
    $ws.Cells.Item(2, 1) = 'Page normalized from rotated (vertical-text) PDF layout'
    $ws.Cells.Item(2, 1).Font.Italic = $true
    $ws.Cells.Item(2, 1).Font.Color = 8388736
    $ws.Columns.AutoFit() | Out-Null
    for ($c = 1; $c -le $maxc; $c++) {
      $w = [double]$ws.Columns.Item($c).ColumnWidth
      if ($w -gt 55) { $ws.Columns.Item($c).ColumnWidth = 55; $ws.Columns.Item($c).WrapText = $true }
    }
    $ws.Activate()
    $out.ActiveWindow.SplitRow = 7
    $out.ActiveWindow.FreezePanes = $true
  }  $tocHdrs = @('Page', 'Sheet', 'Causes', 'Effects', 'Title')
  for ($c = 1; $c -le $tocHdrs.Count; $c++) { $toc.Cells.Item(1, $c) = $tocHdrs[$c - 1] }
  $hT = $toc.Range($toc.Cells.Item(1, 1), $toc.Cells.Item(1, $tocHdrs.Count))
  $hT.Font.Bold = $true; $hT.Interior.Color = 15123099
  $ri = 2
  foreach ($mrow in $meta) {
    $tabName = ('P' + $mrow.page.ToString('000'))
    $toc.Cells.Item($ri, 1) = [string]$mrow.page
    $toc.Hyperlinks.Add($toc.Cells.Item($ri, 2), '', ("'" + $tabName + "'!A1"), '', $tabName) | Out-Null
    $toc.Cells.Item($ri, 3) = [string]$mrow.causes
    $toc.Cells.Item($ri, 4) = [string]$mrow.effects
    $toc.Cells.Item($ri, 5) = [string]$mrow.title
    $ri++
  }
  $toc.Columns.AutoFit() | Out-Null
  $toc.Activate()
  $out.ActiveWindow.SplitRow = 1
  $out.ActiveWindow.FreezePanes = $true
  if (Test-Path $outPath) { Remove-Item -LiteralPath $outPath -Force }
  $wbN.SaveAs($outPath, 51)
  $wbN.Close($false)
  Write-Output ('SAVED -> ' + $outPath)
} finally {
  $out.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($out) | Out-Null
}
