$f = 'C:\VSCode\CAE\_pdf1401_v2.ps1'
$c = Get-Content $f -Encoding UTF8
$out2 = New-Object System.Collections.Generic.List[string]
$inBlock = $false
foreach ($l in $c) {
  $s = [string]$l
  if ($s.Contains('foreach ($w in $cl.items) { if ([int]$w.xi -ge 40) { $s45 += $w.t } }')) { [void]$out2.Add($s); [void]$out2.Add('    $u45 = $s45.ToUpperInvariant()'); $inBlock = $true; continue }
  if ($inBlock -and $s.Contains('$s45 -match')) { [void]$out2.Add($s.Replace('$s45 -match', '$u45 -match')); $inBlock = $false; continue }
  [void]$out2.Add($s)
}
[System.IO.File]::WriteAllLines($f, $out2, [System.Text.UTF8Encoding]::new($true))
Write-Output ('U45-FIX DONE, u45 count: ' + ([regex]::Matches(($out2 -join [Environment]::NewLine), '\$u45 -match')).Count)