$ErrorActionPreference = 'Stop'
$dll = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) '..\Local\Temp\itextsharp\lib\net461\itextsharp.dll'
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
Add-Type -TypeDefinition $cs -ReferencedAssemblies @($dll)
$src = 'C:\VSCode\CAE\1401 Cause & Effect Code A_1.pdf'
$reader = New-Object iTextSharp.text.pdf.PdfReader($src)
$lines = @()
$lines += ('pages: ' + $reader.NumberOfPages)
for ($pi = 1; $pi -le [Math]::Min(3, $reader.NumberOfPages); $pi++) {
  $rot = $reader.GetPageRotation($pi)
  $sz = $reader.GetPageSize($pi)
  $lines += ('--- page ' + $pi + ' rot=' + $rot + ' size=' + [math]::Round($sz.Width) + 'x' + [math]::Round($sz.Height))
  $st = New-Object PosTextStrategy
  $parser = New-Object iTextSharp.text.pdf.parser.PdfReaderContentParser($reader)
  [void]$parser.ProcessContent($pi, $st)
  $lines += ('  chunks: ' + $st.Txt.Count)
  # sort chunks: y desc, x asc; show first 40
  $items = New-Object 'System.Collections.Generic.List[object]'
  for ($i = 0; $i -lt $st.Txt.Count; $i++) {
    $items.Add([pscustomobject]@{ t = $st.Txt[$i]; x1 = $st.Pos[$i][0]; y1 = $st.Pos[$i][1]; x2 = $st.Pos[$i][2]; y2 = $st.Pos[$i][3] })
  }
  $sorted = $items | Sort-Object -Property @{ e = { [double]$_.y1 }; Descending = $true }, @{ e = { [double]$_.x1 }; Descending = $false }
  $cnt = [Math]::Min(40, $sorted.Count)
  for ($i = 0; $i -lt $cnt; $i++) {
    $c = $sorted[$i]
    $lines += ('    [' + $i + '] (' + [math]::Round($c.x1) + ',' + [math]::Round($c.y1) + ')-(' + [math]::Round($c.x2) + ',' + [math]::Round($c.y2) + ') ' + $c.t)
  }
}
$reader.Close()
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_1401_itext.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'ITEXT PROBE DONE'