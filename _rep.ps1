function Apply([string]$path,[string]$o,[string]$n){
  $b=[IO.File]::ReadAllBytes($path)
  $bom=($b.Length -ge 3 -and $b[0] -eq 239 -and $b[1] -eq 187 -and $b[2] -eq 191)
  $off=$(if($bom){3}else{0})
  $t=[Text.Encoding]::UTF8.GetString($b,$off,$b.Length-$off)
  $ol=($o -replace "`r`n","`n") -split "`n"
  $rx=(($ol | ForEach-Object { [regex]::Escape($_) }) -join '\r?\n')
  $re=New-Object Text.RegularExpressions.Regex($rx)
  $ms=$re.Matches($t)
  if($ms.Count -eq 0 -and $t.IndexOf(($n -replace "`r`n","`n").Split("`n")[0],[StringComparison]::Ordinal) -ge 0){ Write-Output ('SKIP-ALREADY ' + $ol[0].Substring(0,[Math]::Min(50,$ol[0].Length))); return }
  if($ms.Count -ne 1){ Write-Output ('FAIL-COUNT' + $ms.Count + ' ' + $ol[0].Substring(0,[Math]::Min(60,$ol[0].Length))); return }
  $n2=(($n -replace "`r`n","`n") -replace "`n","`r`n").Replace('$','$$')
  $t=$re.Replace($t,$n2,1)
  $enc=New-Object System.Text.UTF8Encoding($bom)
  [IO.File]::WriteAllText($path,$t,$enc)
  Write-Output ('OK ' + $ol[0].Substring(0,[Math]::Min(55,$ol[0].Length)))
}
