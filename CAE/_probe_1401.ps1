$ErrorActionPreference = 'Stop'
$lines = @()
$src = 'C:\VSCode\CAE\1401 Cause & Effect Code A_1.pdf'
try {
  $app = New-Object -ComObject AcroExch.App
  $av = New-Object -ComObject AcroExch.AVDoc
  [void]$av.Open($src, '')
  $pd = $av.GetPDDoc()
  $n = $pd.GetNumPages()
  $lines += ('pages: ' + $n)
  # try JS saveAs to xlsx (per-page sheets, same engine as CAE_OSBL_exel)
  $js = $pd.GetJSObject()
  $outXlsx = 'C:\VSCode\CAE\1401_CAE_acrobat.xlsx'
  $outXls = 'C:\VSCode\CAE\1401_CAE_acrobat.xls'
  if (Test-Path $outXlsx) { Remove-Item -LiteralPath $outXlsx -Force }
  if (Test-Path $outXls) { Remove-Item -LiteralPath $outXls -Force }
  $try1 = $false
  try {
    $js.saveAs($outXlsx, 'com.adobe.acrobat.xlsx')
    $try1 = $true
    $lines += ('saveAs xlsx: OK')
  } catch { $lines += ('saveAs xlsx err: ' + $_.Exception.Message) }
  if (-not $try1 -or -not (Test-Path $outXlsx)) {
    try {
      $js.saveAs($outXls, 'com.adobe.acrobat.xls')
      $lines += ('saveAs xls: OK')
    } catch { $lines += ('saveAs xls err: ' + $_.Exception.Message) }
  }
  Start-Sleep -Seconds 2
  foreach ($f in @($outXlsx, $outXls)) {
    if (Test-Path $f) { $lines += ('exists: ' + $f + ' (' + [math]::Round((Get-Item $f).Length / 1KB) + ' KB)') }
  }
  $pd.Close()
  $av.Close(1)
  $app.Exit()
} catch {
  $lines += ('ERROR: ' + $_.Exception.Message)
}
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'cae_1401_probe.txt'), ($lines -join "`r`n"), [System.Text.Encoding]::UTF8)
Write-Output '1401 PROBE DONE'