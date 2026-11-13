$m = Get-Content (Join-Path $env:TEMP 'cae_match.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$lines = @()
foreach ($tag in @('1115-P-101A','1115-P-101B','1120-P-203A','1110-P-131A','1130-P-331A','1110-P-132A','1612-P-606A','1110-K-101A','1110-XZV-60101','1110-FIC-10301')) {
  $v = $m.PSObject.Properties[$tag.ToUpperInvariant()].Value
  if ($null -eq $v) { $lines += ($tag + ' : NE NAJDENO') }
  else { $lines += ($tag + ' : loop=' + $v.loop + ' | marka=' + $v.marka + ' | file=' + $v.file + ' | sposob=' + $v.sposob + ' | kontur=' + $v.cLoop) }
}
$nf = 0; $fd = 0
foreach ($v in $m.PSObject.Properties) { if ($null -eq $v.Value) { $nf++ } else { $fd++ } }
$lines += ('STATS unique: found=' + $fd + ', not found=' + $nf)
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_isys_check.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output 'ISYS CHECK DONE'