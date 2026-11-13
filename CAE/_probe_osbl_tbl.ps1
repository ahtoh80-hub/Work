Add-Type -AssemblyName System.IO.Compression.FileSystem
$lines = @()
foreach ($f in @('C:\VSCode\CAE\OSBL_SCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx','C:\VSCode\CAE\OSBL_DCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx','C:\VSCode\CAE\OSBL_GDS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx')) {
  $zip = [System.IO.Compression.ZipFile]::OpenRead($f)
  $e = $zip.GetEntry('xl/worksheets/sheet1.xml')
  $sr = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
  $raw = $sr.ReadToEnd()
  $sr.Close()
  $zip.Dispose()
  $ms = [regex]::Matches($raw, '<row[^>]*r="(\d+)"[^>]*>(.*?)</row>')
  $hdrRow = [regex]::Match($ms[0].Value, '<c r="([A-Z]+)1"[^>]*>.*?</c>')
  $hdrs = @()
  foreach ($cm in [regex]::Matches($ms[0].Value, '<c r="([A-Z]+\d+)"[^>]*>.*?</c>')) {
    $tv = [regex]::Match($cm.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
    if ($tv -ne '') { $hdrs += ($cm.Groups[1].Value + '=' + $tv) }
  }
  $lines += ('=== ' + (Split-Path $f -Leaf) + ' dataRows=' + ($ms.Count - 1))
  $lines += ('  header: ' + ($hdrs -join ' | '))
  # sample data row
  if ($ms.Count -gt 1) {
    $cells = [regex]::Matches($ms[1].Value, '<c r="([A-Z]+\d+)"[^>]*>.*?</c>')
    $parts = @()
    foreach ($cm in $cells) {
      $tv = [regex]::Match($cm.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
      if ($tv -ne '') { $parts += ($cm.Groups[1].Value + '=' + $tv) }
    }
    $lines += ('  sample: ' + ($parts -join ' | '))
  }
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_osbl_tbl_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'OSBL TBL PROBE DONE'