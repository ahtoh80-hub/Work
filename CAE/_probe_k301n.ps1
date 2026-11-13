Add-Type -AssemblyName System.IO.Compression.FileSystem
$lines = @()
foreach ($f in @('C:\VSCode\CAE\EBSM_SCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx','C:\VSCode\CAE\EBSM_DCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx')) {
  $zip = [System.IO.Compression.ZipFile]::OpenRead($f)
  $e = $zip.GetEntry('xl/worksheets/sheet1.xml')
  $sr = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
  $raw = $sr.ReadToEnd()
  $sr.Close()
  $zip.Dispose()
  $lines += ('=== ' + (Split-Path $f -Leaf))
  foreach ($pat in @('1130-K-301A','1130-K-301B','MXI-00301A','MXI-00302A','KM-301A','KM-301B')) {
    $ms = [regex]::Matches($raw, ('<row[^>]*r="(\d+)"[^>]*>((?!</row>).)*' + $pat + '((?!</row>).)*</row>'))
    $lines += ('  ' + $pat + ' rows: ' + $ms.Count)
    foreach ($mm in ($ms | Select-Object -First 3)) {
      $cells = [regex]::Matches($mm.Value, '<c r="([A-Z]+\d+)"[^>]*>.*?</c>')
      $parts = @()
      foreach ($cm in $cells) {
        $tv = [regex]::Match($cm.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
        if ($tv -ne '') { $parts += ($cm.Groups[1].Value + '=' + $tv) }
      }
      $lines += ('    ROW ' + $mm.Groups[1].Value + ': ' + ($parts -join ' | '))
    }
  }
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_k301_name_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'K301 NAME PROBE WRITTEN'