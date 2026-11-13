Add-Type -AssemblyName System.IO.Compression.FileSystem
$lines = @()
$zip = [System.IO.Compression.ZipFile]::OpenRead('C:\VSCode\CAE\OSBL_MODBUS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx')
$e = $zip.GetEntry('xl/worksheets/sheet1.xml')
$sr = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
$raw = $sr.ReadToEnd()
$sr.Close()
$zip.Dispose()
$ms = [regex]::Matches($raw, '<row[^>]*r="(\d+)"[^>]*>(.*?)</row>')
$lines += ('rows: ' + $ms.Count)
$hdrCells = [regex]::Matches($ms[0].Value, '<c r="([A-Z]+\d+)"[^>]*>.*?</c>')
$hdrs = @()
foreach ($cm in $hdrCells) {
  $tv = [regex]::Match($cm.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
  if ($tv -ne '') { $hdrs += ($cm.Groups[1].Value + '=' + $tv) }
}
$lines += ('header: ' + ($hdrs -join ' | '))
foreach ($mm in ($ms | Select-Object -First 3)) {
  if ($mm.Groups[1].Value -eq '1') { continue }
  $cells = [regex]::Matches($mm.Value, '<c r="([A-Z]+\d+)"[^>]*>.*?</c>')
  $parts = @()
  foreach ($cm in $cells) {
    $tv = [regex]::Match($cm.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
    if ($tv -ne '') { $parts += ($cm.Groups[1].Value + '=' + $tv) }
  }
  $lines += ('  ROW ' + $mm.Groups[1].Value + ': ' + ($parts -join ' | '))
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_osbl_modbus_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'MODBUS PROBE DONE'