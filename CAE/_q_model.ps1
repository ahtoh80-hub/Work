$m = Get-Content (Join-Path $env:TEMP 'cae_osbl_model.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Output '--- systems matching 23001 or condens:'
$m.systems | Where-Object { ($_.tag -match '23001') -or ($_.nameEN -match 'condens') } | ForEach-Object { Write-Output ($_.sheet + ' ' + $_.tag + ' | ' + $_.nameEN) }
Write-Output '--- items with AZI tags:'
$m.items | Where-Object { ([string]$_.tagEN) -match 'AZI' } | ForEach-Object { Write-Output ($_.sheet + ' sys=' + $_.sysTag + ' tag=' + $_.tagEN + ' eff=' + @($_.effects).Count) }
Write-Output '--- items with XZV-04:'
$m.items | Where-Object { ([string]$_.tagEN) -match 'XZV.04' } | ForEach-Object { Write-Output ($_.sheet + ' sys=' + $_.sysTag + ' tag=' + $_.tagEN) }
Write-Output ('--- all system tags:')
Write-Output (($m.systems | ForEach-Object { $_.tag }) -join ', ')