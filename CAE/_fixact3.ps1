$f = 'C:\VSCode\CAE\_pdf1401_v2.ps1'
$c = Get-Content $f -Encoding UTF8
$out2 = New-Object System.Collections.Generic.List[string]
$i = 0
$inBP = $false
while ($i -lt $c.Count) {
  $s = [string]$c[$i]
  if ($s.Contains('function Build-PageRows')) { $inBP = $true }
  if ($inBP -and $s.Contains('$actYi = 0; $maxActs = 0')) {
    # skip old block: until line containing 'actYi = [int]$cl.yi }'
    while ($i -lt $c.Count) { $s2 = [string]$c[$i]; if ($s2.Contains('actYi = [int]$cl.yi }')) { $i++; break }; $i++ }
    [void]$out2.Add('  $actYi = 0; $maxActs = 0')
    [void]$out2.Add("  `$actPref = @('CLOSE','STOP','OPEN','START','EXPORT')")
    [void]$out2.Add('  $actPref += @(([string][char]0x0417 + [char]0x0410 + [char]0x041A), ([string][char]0x041E + [char]0x0421 + [char]0x0422 + [char]0x0410 + [char]0x041D), ([string][char]0x041E + [char]0x0422 + [char]0x041A + [char]0x0420), ([string][char]0x041F + [char]0x0423 + [char]0x0421 + [char]0x041A), ([string][char]0x0417 + [char]0x0410 + [char]0x041F))')
    [void]$out2.Add('  foreach ($cl in $ySorted) {')
    [void]$out2.Add('    $k = 0')
    [void]$out2.Add('    foreach ($w in $cl.items) {')
    [void]$out2.Add('      if ([int]$w.xi -gt 40) { continue }')
    [void]$out2.Add('      $u = $w.t.ToUpperInvariant()')
    [void]$out2.Add('      foreach ($ap in $actPref) { if ($u.StartsWith($ap)) { $k++; break } }')
    [void]$out2.Add('    }')
    [void]$out2.Add('    if ($k -gt $maxActs) { $maxActs = $k; $actYi = [int]$cl.yi }')
    [void]$out2.Add('  }')
    $i++
    continue
  }
  if ($inBP -and $s.Contains("if (`$actWords -contains `$u) { `$act = `$w.t; break }")) {
    [void]$out2.Add('    $act = ''')
    [void]$out2.Add('    foreach ($w in $parts) { $yy = [int]$w.yi; if (($yy -ge $actYi) -and ($yy -le ($actYi + 2))) { $act += $w.t } }')
    $i++
    continue
  }
  [void]$out2.Add($s)
  $i++
}
[System.IO.File]::WriteAllLines($f, $out2, [System.Text.UTF8Encoding]::new($true))
Write-Output ('ACT3-FIXED, lines: ' + $out2.Count)