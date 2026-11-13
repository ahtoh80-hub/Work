$ErrorActionPreference = 'Stop'
$lines = @()
try {
  $app = New-Object -ComObject AcroExch.App
  $av = New-Object -ComObject AcroExch.AVDoc
  $ok = $av.Open('C:\VSCode\CAE\CAE_OSBL.pdf', '')
  $pd = $av.GetPDDoc()
  $n = $pd.GetNumPages()
  $lines += ('pages: ' + $n)
  # check JS availability and per-page content stats for first pages
  try {
    $js = $pd.GetJSObject()
    $lines += ('JS: ' + ($null -ne $js))
    if ($null -ne $js) {
      $max = [Math]::Min($n, 8)
      for ($p = 0; $p -lt $max; $p++) {
        $wc = 0; $rot = 0
        try { $wc = $js.GetPageNumWords($p) } catch { $lines += ('  p' + $p + ' GetPageNumWords err: ' + $_.Exception.Message) }
        try { $rot = $js.getPageRotation($p) } catch { }
        $lines += ('  p' + $p + ': words=' + $wc + ' rot=' + $rot)
        if ($p -eq 6) {
          $sample = @()
          $cnt = [Math]::Min($wc, 12)
          for ($w = 0; $w -lt $cnt; $w++) {
            $t = ''
            try { $t = $js.getPageNthWord($p, $w, $true) } catch { }
            $q = $null
            try { $q = $js.getPageNthWordQuads($p, $w, $true) } catch { }
            $ql = ''
            if ($null -ne $q) { try { $ql = ($q -join ';').Substring(0, [Math]::Min(80, ($q -join ';').Length)) } catch { $ql = 'q?' } }
            $sample += ('[' + $w + ']' + $t + '@' + $ql)
          }
          $lines += ('  p7 words: ' + ($sample -join ' '))
        }
      }
    }
  } catch {
    $lines += ('JS error: ' + $_.Exception.Message)
  }
  $pd.Close()
  $av.Close(1)
  $app.CloseAllDocs()
  $app.Exit()
} catch {
  $lines += ('ERROR: ' + $_.Exception.Message)
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_pdf_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'PDF PROBE DONE'