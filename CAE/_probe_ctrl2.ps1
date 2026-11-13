Add-Type -AssemblyName System.IO.Compression.FileSystem
$lines = @()
$path = 'C:\VSCode\CAE\Контроллер_EBSM_блокировок.xlsx'
$zip = [System.IO.Compression.ZipFile]::OpenRead($path)
$e = $zip.GetEntry('xl/worksheets/sheet1.xml')
$sr = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
$raw = $sr.ReadToEnd()
$sr.Close()
$zip.Dispose()
$ms = [regex]::Matches($raw, '<row[^>]*r="(\d+)"[^>]*>(.*?)</row>')
$lines += ('rows: ' + $ms.Count)
$map = @{}
foreach ($mm in $ms) {
  $c = [regex]::Match($mm.Value, '<c r="C\d+"[^>]*>.*?</c>')
  $f = [regex]::Match($mm.Value, '<c r="F\d+"[^>]*>.*?</c>')
  $cv = [regex]::Match($c.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
  $fv = [regex]::Match($f.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
  if ($cv -ne '' -and $cv -ne 'Блокировка (B)') { $map[$cv.ToUpperInvariant()] = $fv }
}
$lines += ('unique B: ' + $map.Count)
$check = @('1110-Z-1001','1115-Z-1002','1110-I-1601','1110-I-1605','1120-I-2001','1130-I-3071','1110-Z-1601','1120-Z-2031','1000-Z-9000','1115-Z-9102','1130-Z-3001','1140-Z-4002','1110-Z-9001','1130-Z-9011','1110-Z-9101')
foreach ($t in $check) {
  $k = $t.ToUpperInvariant()
  $v = if ($map.ContainsKey($k)) { $map[$k] } else { '<ОТСУТСТВУЕТ>' }
  $lines += ('  ' + $t + ' -> ' + $v)
}
$pref = @{}
foreach ($k in $map.Keys) {
  $p = ($k -replace '^\d{4}-', '') -replace '[\dA-Z].*$', ''
  if (-not $pref.ContainsKey($p)) { $pref[$p] = 0 }
  $pref[$p] = [int]$pref[$p] + 1
}
foreach ($kv in ($pref.GetEnumerator() | Sort-Object Value -Descending)) { $lines += ('  prefix ' + $kv.Key + ' = ' + $kv.Value) }
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_ctrl_map.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'CTRL MAP WRITTEN'