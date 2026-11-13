$p5 = 'C:\VSCode\CAE\_osbl_full_p5.ps1'
$content = [System.IO.File]::ReadAllText($p5, [System.Text.Encoding]::UTF8)
$content = $content.Replace('.ToArray()', '')
[System.IO.File]::WriteAllText($p5, $content, [System.Text.UTF8Encoding]::new($true))
Write-Output 'p5 fixed'