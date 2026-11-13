$f = 'C:\VSCode\CAE\_pdf1401_v2.ps1'
$t = [System.IO.File]::ReadAllText($f)
$old = '    $res = Build-PageRows $ex $pi'
$new = '    $res = Build-PageRows $ex $pi' + [char]13 + [char]10 + '    if ($pi -le 3) { Write-Host ("MAIN p" + $pi + " kind=" + $res.kind + " rows=" + $res.rows.Count + " c0=" + $res.rows[0].Count + " c2=" + $res.rows[2].Count) }'
$t = $t.Replace($old, $new)
[System.IO.File]::WriteAllText($f, $t, [System.Text.UTF8Encoding]::new($true))
Write-Output 'MAIN-DIAG ADDED'