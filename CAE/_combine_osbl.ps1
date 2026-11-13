$parts = @('C:\VSCode\CAE\_osbl_full_p1.ps1','C:\VSCode\CAE\_osbl_full_p2.ps1','C:\VSCode\CAE\_osbl_full_p3.ps1','C:\VSCode\CAE\_osbl_full_p4.ps1','C:\VSCode\CAE\_osbl_full_p5.ps1')
$all = New-Object 'System.Collections.Generic.List[string]'
foreach ($p in $parts) {
  $content = [System.IO.File]::ReadAllLines($p, [System.Text.Encoding]::UTF8)
  foreach ($l in $content) { $all.Add($l) }
}
$outFile = 'C:\VSCode\CAE\_osbl_full.ps1'
[System.IO.File]::WriteAllLines($outFile, $all.ToArray(), [System.Text.UTF8Encoding]::new($true))
Write-Output ('combined: ' + $all.Count + ' lines -> ' + $outFile)