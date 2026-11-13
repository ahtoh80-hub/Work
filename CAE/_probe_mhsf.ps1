$m = Get-Content (Join-Path $env:TEMP 'cae_match.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$lines = @()
foreach ($tag in @('1110-MHS-00101C','1110-MHS-01101C')) {
  foreach ($pr in $m.PSObject.Properties) {
    if ($pr.Name -like ($tag.ToUpperInvariant() + '||*')) {
      if ($null -eq $pr.Value) { $lines += ($pr.Name + ' : NE NAJDENO (Net dannykh)') }
      else { $lines += ($pr.Name + ' -> loop=' + $pr.Value.loop + ' | sposob=' + $pr.Value.sposob) }
    }
  }
}
$v = $m.PSObject.Properties['1130-YDO-35901||1130-Z-9010'].Value
$lines += ('YDO: loop=' + $v.loop + ' sposob=' + $v.sposob)
$v2 = $m.PSObject.Properties['1110-K-301A||1130-Z-3003'].Value
$lines += ('K-301A||Z-3003: loop=' + $v2.loop + ' sposob=' + $v2.sposob)
$v3 = $m.PSObject.Properties['1110-LZIAHH-10101||1110-Z-1001'].Value
if ($null -eq $v3) { $lines += 'LZIAHH||Z-1001: NE NAJDENO (po parnomu podstanitsya v build)' } else { $lines += ('LZIAHH||Z-1001: loop=' + $v3.loop + ' sposob=' + $v3.sposob) }
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_mhs_fix.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'MHS FIX CHECK DONE'