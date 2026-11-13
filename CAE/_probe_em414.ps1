Add-Type -AssemblyName System.IO.Compression.FileSystem
$lines = @()
$zip = [System.IO.Compression.ZipFile]::OpenRead('C:\VSCode\CAE\EBSM_SCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx')
$e = $zip.GetEntry('xl/worksheets/sheet1.xml')
$sr = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
$raw = $sr.ReadToEnd()
$sr.Close()
$zip.Dispose()
foreach ($pat in @('MXI-00414A','MXI-00414','EM-414','E-414','1140-EM-414','1140-E-414')) {
  $ms = [regex]::Matches($raw, ('<row[^>]*r="(\d+)"[^>]*>((?!</row>).)*' + $pat + '((?!</row>).)*</row>'))
  $lines += ('SCS pattern ' + $pat + ' rows: ' + $ms.Count)
  foreach ($mm in ($ms | Select-Object -First 2)) {
    $cells = [regex]::Matches($mm.Value, '<c r="([A-Z]+\d+)"[^>]*>.*?</c>')
    $parts = @()
    foreach ($cm in $cells) {
      $tv = [regex]::Match($cm.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
      if ($tv -ne '') { $parts += ($cm.Groups[1].Value + '=' + $tv) }
    }
    $lines += ('  ROW ' + $mm.Groups[1].Value + ': ' + ($parts -join ' | '))
  }
}
# also check match json
$m = Get-Content (Join-Path $env:TEMP 'cae_match.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($pr in $m.PSObject.Properties) {
  if ($pr.Name -like '1140-EM-414||*') {
    if ($null -eq $pr.Value) { $lines += ('MATCH: ' + $pr.Name + ' -> NE NAJDENO') }
    else { $lines += ('MATCH: ' + $pr.Name + ' -> loop=' + $pr.Value.loop + ' | file=' + $pr.Value.file + ' | sposob=' + $pr.Value.sposob + ' | kontur=' + $pr.Value.cLoop) }
  }
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_em414_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'EM414 PROBE DONE'