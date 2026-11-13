$f = 'C:\VSCode\CAE\_pdf1401_v2.ps1'
$t = [System.IO.File]::ReadAllText($f)
$old1 = '    if ([int]$cl.yi -ge $actYi) { continue }'
$new1 = '    if (($actYi -gt 0) -and ([int]$cl.yi -ge $actYi)) { continue }'
$t = $t.Replace($old1, $new1)
[System.IO.File]::WriteAllText($f, $t, [System.Text.UTF8Encoding]::new($true))
Write-Output ('actGuard=' + $t.Contains('($actYi -gt 0)'))