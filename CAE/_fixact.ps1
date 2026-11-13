$f = 'C:\\VSCode\\CAE\\_pdf1401_to_excel.ps1'
$c = Get-Content $f -Encoding UTF8
# 1) remove prepended duplicate block: lines from first '$actYi = 0' until first '$header = $null' inclusive
$i1 = -1; $i2 = -1
for ($i = 0; $i -lt $c.Count; $i++) { $s = [string]$c[$i]; if ($i1 -lt 0 -and $s.Contains('$actYi = 0')) { $i1 = $i }; if ($i1 -ge 0 -and $s.Contains('$header = $null')) { $i2 = $i; break } }
if ($i1 -ge 0 -and $i2 -ge $i1) { $c2 = @(); for ($i = 0; $i -lt $c.Count; $i++) { if ($i -ge $i1 -and $i -le $i2) { continue }; $c2 += $c[$i] }; $c = $c2 }
# 2) inside Build-PageRows: replace s26-based block with chunk-based count
$out2 = New-Object System.Collections.Generic.List[string]
$skip = $false
for ($i = 0; $i -lt $c.Count; $i++) {
  $s = [string]$c[$i]
  if ($s.Contains('s26 = ') -or $s.Contains('s45 = ')) { $skip = $true; [void]$out2.Add('    $k = 0'); continue }
  if ($skip -and $s.Contains('foreach ($aw in $actWords)')) { [void]$out2.Add('    foreach ($w in $cl.items) { $u = $w.t.ToUpperInvariant(); if ($actWords -contains $u) { $k++ } }'); continue }
  if ($skip -and $s.Contains('$k -gt $maxActs')) { [void]$out2.Add('    if ($k -gt $maxActs) { $maxActs = $k; $actYi = [int]$cl.yi }'); $skip = $false; continue }
  if ($skip -and $s.Contains('$actYi -ge [int]$actYi')) { $skip = $false; continue }
  [void]$out2.Add($l)
}
[System.IO.File]::WriteAllLines($f, $out2, [System.Text.UTF8Encoding]::new($true))
Write-Output ('FIXED, lines: ' + $out2.Count)