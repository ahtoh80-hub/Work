# OSBL: Build prep data from cae_osbl_model.json, then match against OSBL_ТЕХНОЛ_ОБЪЕКТЫ
$ErrorActionPreference = 'Stop'
$modelPath = Join-Path $env:TEMP 'cae_osbl_model.json'
$outPath = 'C:\VSCode\CAE\CAE_OSBL_Перечень блокировок.xlsx'
$m = Get-Content $modelPath -Raw -Encoding UTF8 | ConvertFrom-Json
$items = @($m.items)
$systems = @($m.systems)
Write-Output ("model: items=" + $items.Count + " systems=" + $systems.Count)

# load controller mapping (check if OSBL version exists, otherwise skip)
$ctrlMap = @{}
$ctrlPath = 'C:\VSCode\CAE\Контроллер_EBSM_блокировок.xlsx'
if (Test-Path $ctrlPath) {
  # reuse EBSM controller mapping as fallback
}
function Get-CtrlOf([string]$sysTag) { return 'Нет данных' }

# build h1/h5 headers and rows (same structure as EBSM but simpler for OSBL - no pair tags, no contour)
# load controller mapping from Контроллер_OSBL_блокировок.xlsx
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$ctrlMap = @{}
try {
  $wb = $excel.Workbooks.Open('C:\VSCode\CAE\Контроллер_OSBL_блокировок.xlsx', 0, $true)
  $ws = $wb.Worksheets.Item(1)
  $last = $ws.UsedRange.Rows.Count
  for ($r = 2; $r -le $last; $r++) {
    $k = ([string]$ws.Cells.Item($r, 4).Text).Trim().ToUpperInvariant()
    $v = ([string]$ws.Cells.Item($r, 9).Text).Trim()
    if ($k -ne '' -and $v -ne '') { $ctrlMap[$k] = $v }
  }
  $wb.Close($false)
  Write-Output ("ctrlMap entries: " + $ctrlMap.Count)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
function Get-CtrlOf([string]$sysTag) {
  $st = ([string]$sysTag).Trim().ToUpperInvariant()
  if ($st -eq '') { return 'Нет данных' }
  if ($ctrlMap.ContainsKey($st)) { return $ctrlMap[$st] }
  return 'Нет данных'
}

$h1 = @('№ п/п','Лист','Система — тег','Система — наименование EN','Контроллер блокировки','№ причины','Причина (описание) EN','Входные сигналы EN','Тег входного сигнала EN','P&ID','Следствия EN')
$rows1 = New-Object 'System.Collections.Generic.List[object]'
$crossCount = 0
foreach ($i in $items) {
  $ctrlVal = Get-CtrlOf $i.sysTag
  $effE = (($i.effects | ForEach-Object { (([string]$_.tag).Trim() + ' — ' + ([string]$_.actionEN).Trim() + ' [' + $_.mark + ']') }) -join "`n")
  $crossCount += @($i.effects).Count
  $rows1.Add(@(
    [string]$i.seq, [string]$i.sheet, [string]$i.sysTag,
    [string]$i.sysNameEN, $ctrlVal, [string]$i.no,
    [string]$i.descEN, [string]$i.inputEN, [string]$i.tagEN, [string]$i.pid,
    $effE
  )) | Out-Null
}
Write-Output ("rows1=" + $rows1.Count + " crossings=" + $crossCount)
$prep = @{ h1 = $h1; r1 = $rows1; out = $outPath }
$prepPath = Join-Path $env:TEMP 'cae_osbl_prep.json'
[System.IO.File]::WriteAllText($prepPath, ($prep | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
Write-Output "PREP -> $prepPath"