$ErrorActionPreference = 'Stop'
$dll = 'C:\Users\Reshetov\AppData\Local\Temp\itextsharp\lib\net461\itextsharp.dll'
$bc = 'C:\Users\Reshetov\AppData\Local\Temp\bc2\lib\net461\BouncyCastle.Cryptography.dll'
[void][System.Reflection.Assembly]::LoadFrom($bc)
[void][System.Reflection.Assembly]::LoadFrom($dll)
$cs = @'
using System;
using System.Collections.Generic;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
public class PosTextStrategy : ITextExtractionStrategy {
  public List<float[]> Pos = new List<float[]>();
  public List<string> Txt = new List<string>();
  public void BeginTextBlock() {}
  public void EndTextBlock() {}
  public void RenderImage(ImageRenderInfo renderInfo) {}
  public void RenderText(TextRenderInfo renderInfo) {
    LineSegment seg = renderInfo.GetBaseline();
    Vector p1 = seg.GetStartPoint();
    Vector p2 = seg.GetEndPoint();
    Pos.Add(new float[] { p1[Vector.I1], p1[Vector.I2], p2[Vector.I1], p2[Vector.I2] });
    Txt.Add(renderInfo.GetText());
  }
  public string GetResultantText() { return string.Join(" ", Txt.ToArray()); }
}
'@
if (-not ('PosTextStrategy' -as [type])) { Add-Type -TypeDefinition $cs -ReferencedAssemblies @($bc, $dll) }
$src = 'C:\VSCode\CAE\1401 Cause & Effect Code A_1.pdf'
$reader = New-Object iTextSharp.text.pdf.PdfReader($src)
$lines = @()
$lines += ('pages: ' + $reader.NumberOfPages)
$pi = 1
$rot = $reader.GetPageRotation($pi)
$sz = $reader.GetPageSize($pi)
$W = $sz.Width
$lines += ('page 1 rot=' + $rot + ' size=' + [math]::Round($W) + 'x' + [math]::Round($sz.Height))
$st = New-Object PosTextStrategy
$parser = New-Object iTextSharp.text.pdf.parser.PdfReaderContentParser($reader)
[void]$parser.ProcessContent($pi, $st)
$items = New-Object 'System.Collections.Generic.List[object]'
for ($i = 0; $i -lt $st.Txt.Count; $i++) {
  $t = $st.Txt[$i].Trim()
  if ($t -eq '') { continue }
  $x1 = $st.Pos[$i][0]; $y1 = $st.Pos[$i][1]; $x2 = $st.Pos[$i][2]; $y2 = $st.Pos[$i][3]
  $xc = ($x1 + $x2) / 2.0
  $items.Add([pscustomobject]@{ t = $t; x1 = $x1; x2 = $x2; xc = $xc; y1 = $y1; y2 = $y2 })
}
# logical view (CW): row = x cluster (bigger x = higher), text along y
$lines += ('total chunks: ' + $items.Count)
$clusters = New-Object 'System.Collections.Generic.List[object]'
foreach ($it in $items) {
  $placed = $false
  foreach ($cl in $clusters) {
    if ([Math]::Abs($cl.xc - $it.xc) -le 6) { [void]$cl.items.Add($it); $cl.xc = ($cl.xc * ($cl.items.Count - 1) + $it.xc) / $cl.items.Count; $placed = $true; break }
  }
  if (-not $placed) { $nc = New-Object psobject -Property @{ xc = $it.xc; items = (New-Object 'System.Collections.Generic.List[object]') }; [void]$nc.items.Add($it); [void]$clusters.Add($nc) }
}
$lines += ('x-clusters: ' + $clusters.Count)
$sortedCl = $clusters | Sort-Object -Property @{ e = { [double]$_.xc }; Descending = $true }
$rowNo = 0
foreach ($cl in $sortedCl) {
  $rowNo++
  if ($rowNo -gt 30) { break }
  $sorted = $cl.items | Sort-Object -Property @{ e = { [double]$_.y1 }; Descending = $false }
  $txt = ''
  foreach ($c in $sorted) { $txt += $c.t + ' | ' }
  if ($txt.Length -gt 200) { $txt = $txt.Substring(0, 200) + '..' }
  $lines += ('row' + $rowNo + ' (x~' + [math]::Round($cl.xc) + '): ' + $txt)
}
# После кластеризации X (уже есть) — строю карту Y-кластеров
$yclusters = New-Object 'System.Collections.Generic.List[object]'
foreach ($it in $items) {
  $yc = ($it.y1 + $it.y2) / 2.0
  $placed = $false
  foreach ($cl in $yclusters) {
    if ([Math]::Abs($cl.yc - $yc) -le 9) { [void]$cl.items.Add($it); $cl.yc = ($cl.yc * ($cl.items.Count - 1) + $yc) / $cl.items.Count; $placed = $true; break }
  }
  if (-not $placed) { $nc = New-Object psobject -Property @{ yc = $yc; items = (New-Object 'System.Collections.Generic.List[object]') }; [void]$nc.items.Add($it); [void]$yclusters.Add($nc) }
}
$sortedY = $yclusters | Sort-Object -Property @{ e = { [double]$_.yc }; Descending = $false }
$lines += ('y-clusters: ' + $yclusters.Count)
# map: assign each item to x-cluster index and y-cluster index
$xIdx = @{}
$xSorted = $clusters | Sort-Object -Property @{ e = { [double]$_.xc }; Descending = $true }
$xi = 0
foreach ($cl in $xSorted) { $xi++; $xIdx[[int]$cl.xc] = $xi }
$yi = 0
$yIdxMap = @{}
foreach ($cl in $sortedY) { $yi++; $yIdxMap[[int]$cl.yc] = $yi }
foreach ($cl in $xSorted) {
  foreach ($it in $cl.items) {
    $yc = ($it.y1 + $it.y2) / 2.0
    $bestY = 0; $bestD = 1e9
    foreach ($cl2 in $sortedY) { $d = [Math]::Abs($cl2.yc - $yc); if ($d -lt $bestD) { $bestD = $d; $bestY = [int]$cl2.yc } }
    $it | Add-Member -NotePropertyName xi -NotePropertyValue $xIdx[[int]$cl.xc] -Force
    $it | Add-Member -NotePropertyName yi -NotePropertyValue $yIdxMap[$bestY] -Force
  }
}
$lines += ('=== grid map (yi x xi): text ===')
foreach ($cl in $sortedY) {
  $yi2 = $yIdxMap[[int]$cl.yc]
  $g = @{}
  foreach ($it in $cl.items) { $key = [int]$it.xi; if (-not $g.ContainsKey($key)) { $g[$key] = '' }; $g[$key] = ($g[$key] + $it.t).Trim() }
  $out = ''
  for ($xi2 = 1; $xi2 -le $xi; $xi2++) {
    if ($g.ContainsKey($xi2)) { $v = $g[$xi2]; if ($v.Length -gt 14) { $v = $v.Substring(0, 14) + '..' }; $out += ('[' + $xi2 + ']' + $v + ' ') }
  }
  if ($out.Length -gt 400) { $out = $out.Substring(0, 400) + '..' }
  $lines += ('Y' + $yi2 + ' (y~' + [math]::Round($cl.yc) + '): ' + $out)
}$reader.Close()
[System.IO.File]::WriteAllText((Join-Path C:\Users\Reshetov\AppData\Local\Temp 'cae_1401_itext2.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'ITEXT2 DONE'
