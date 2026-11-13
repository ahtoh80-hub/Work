$f = 'C:\VSCode\CAE\_pdf1401_v2.ps1'
$t = [System.IO.File]::ReadAllText($f)
$t = $t.Replace('$tagMinX = [Math]::Max(1, $revX - 6)', '$tagMinX = [Math]::Max(1, $revX - 5)')
[System.IO.File]::WriteAllText($f, $t, [System.Text.UTF8Encoding]::new($true))
Write-Output 'TAG-FIX DONE'