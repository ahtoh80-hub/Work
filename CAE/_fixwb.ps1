$f = 'C:\VSCode\CAE\_pdf1401_v2.ps1'
$t = [System.IO.File]::ReadAllText($f)
$old = '    $rows = $p.rows'
$new = '    $rows = $p.rows' + [char]13 + [char]10 + '    Write-Host ("WB p" + $p.page + " kind=" + $p.kind + " n=" + $rows.Count + " c0=" + $rows[0].Count + " c2=" + $rows[2].Count)'
$t = $t.Replace($old, $new)
[System.IO.File]::WriteAllText($f, $t, [System.Text.UTF8Encoding]::new($true))
Write-Output 'WB-DIAG ADDED'