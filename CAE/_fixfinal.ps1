$f = 'C:\VSCode\CAE\_pdf1401_v2.ps1'
$t = [System.IO.File]::ReadAllText($f)
$old1 = '    if ([int]$cl.yi -ge [int]$actYi) { continue }'
$new1 = '    if (($actYi -gt 0) -and ([int]$cl.yi -ge [int]$actYi)) { continue }'
$t = $t.Replace($old1, $new1)
$old2 = '  if ($revX -eq 0) { $revX = $tagMaxX + 2 }'
$new2 = '  if ($revX -eq 0) { $revX = $tagMaxX + 2 }' + [char]13 + [char]10 + '  if ($tagMaxX -eq 0) { $tagMinX = [Math]::Max(1, $revX - 6); $tagMaxX = $revX - 1 }'
$t = $t.Replace($old2, $new2)
[System.IO.File]::WriteAllText($f, $t, [System.Text.UTF8Encoding]::new($true))
Write-Output ('FINAL2: actGuard=' + $t.Contains('($actYi -gt 0)') + ' tagZone=' + $t.Contains('$tagMinX = [Math]::Max(1, $revX - 6)'))