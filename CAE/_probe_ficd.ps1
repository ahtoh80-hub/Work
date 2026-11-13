Add-Type -AssemblyName System.IO.Compression.FileSystem
$lines = @()
function Get-SheetXml([string]$path) {
  $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
  $e = $zip.GetEntry('xl/worksheets/sheet1.xml')
  $sr = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
  $raw = $sr.ReadToEnd()
  $sr.Close()
  $zip.Dispose()
  return $raw
}
$raw = Get-SheetXml 'C:\VSCode\CAE\EBSM_DCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'
foreach ($pat in @('FIC-10301','FI-10301','1110-FIC','_1110_FIC')) {
  $ms = [regex]::Matches($raw, ('<row[^>]*r="(\d+)"[^>]*>((?!</row>).)*' + $pat + '((?!</row>).)*</row>'))
  $lines += ('DCS pattern ' + $pat + ' rows: ' + $ms.Count)
  foreach ($mm in ($ms | Select-Object -First 3)) {
    $cells = [regex]::Matches($mm.Value, '<c r="([A-Z]+\d+)"[^>]*>.*?</c>')
    $parts = @()
    foreach ($cm in $cells) {
      $tv = [regex]::Match($cm.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
      if ($tv -ne '') { $parts += ($cm.Groups[1].Value + '=' + $tv) }
    }
    $lines += ('  ROW ' + $mm.Groups[1].Value + ': ' + ($parts -join ' | '))
  }
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_fic_dcs_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'FIC DCS PROBE WRITTEN'