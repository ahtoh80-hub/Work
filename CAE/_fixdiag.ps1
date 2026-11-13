$t = [System.IO.File]::ReadAllText('C:\VSCode\CAE\_pdf1401_to_excel.ps1')
$old1 = "  if ($null -eq $header) { return @{ kind = 'EMPTY'; rows = $null; title = '' } }"
$new1 = "  if ($null -eq $header) { if ($pi -le 3) { Write-Output ('NO-HEADER p' + $pi + ' actYi=' + $actYi + ' xcl=' + $xSorted.Count) }; return @{ kind = 'EMPTY'; rows = $null; title = '' } }"
$t = $t.Replace($old1, $new1)
$old2 = "  return @{ kind = 'DATA'; rows = $rows; title = $title }"
$new2 = "  if ($pi -le 3) { Write-Output ('OK p' + $pi + ' eff=' + $effInfo.Count + ' causes=' + $causes2.Count + ' actYi=' + $actYi + ' hdrYi=' + $header.yi + ' voteX=' + $voteX) }; return @{ kind = 'DATA'; rows = $rows; title = $title }"
$t = $t.Replace($old2, $new2)
[System.IO.File]::WriteAllText('C:\VSCode\CAE\_pdf1401_to_excel.ps1', $t, [System.Text.UTF8Encoding]::new($true))
Write-Output 'DIAG2 ADDED'