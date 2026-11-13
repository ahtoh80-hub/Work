# OSBL Full Pipeline - Part 1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$modelPath = Join-Path $env:TEMP 'cae_osbl_model.json'
$m = Get-Content $modelPath -Raw -Encoding UTF8 | ConvertFrom-Json
$items = @($m.items); $systems = @($m.systems)
Write-Output ("model: items=" + $items.Count + " systems=" + $systems.Count)
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
$ctrlMap = @{}; $ruMap = @{}
try {
  $wb = $excel.Workbooks.Open('C:\VSCode\CAE\Контроллер_OSBL_блокировок.xlsx', 0, $true)
  $ws = $wb.Worksheets.Item(1); $last = $ws.UsedRange.Rows.Count
  for ($r = 2; $r -le $last; $r++) {
    $k = ([string]$ws.Cells.Item($r, 4).Text).Trim().ToUpperInvariant()
    $fcs = ([string]$ws.Cells.Item($r, 9).Text).Trim()
    $ru = ([string]$ws.Cells.Item($r, 5).Text).Trim()
    if ($k -ne '') { if ($fcs -ne '') { $ctrlMap[$k] = $fcs }; if ($ru -ne '') { $ruMap[$k] = $ru } }
  }
  $wb.Close($false)
  Write-Output ("ctrlMap: " + $ctrlMap.Count + " ruMap: " + $ruMap.Count)
} finally { $excel.Quit(); [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null }