# Extract all sheets (EN + RU) of the CAE workbook into JSON files for parsing.
$ErrorActionPreference = 'Stop'
$src = 'C:\VSCode\CAE\TX.CAE-0001_3_ER-20260717_EBSM.xlsx'
$outDir = Join-Path $env:TEMP 'cae_json'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null

$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
try {
  $wb = $excel.Workbooks.Open($src, 0, $true)
  foreach ($ws in $wb.Worksheets) {
    $u = $ws.UsedRange
    $rows = $u.Rows.Count
    $cols = $u.Columns.Count
    $vals = $u.Value2
    $grid = New-Object 'System.Collections.Generic.List[object]'
    if ($rows -eq 1 -and $cols -eq 1) {
      $grid.Add(@([string]$vals)) | Out-Null
    } else {
      for ($r = 1; $r -le $rows; $r++) {
        $line = New-Object string[] $cols
        for ($c = 1; $c -le $cols; $c++) {
          $v = $vals[$r, $c]
          if ($null -ne $v) { $line[$c-1] = [string]$v }
        }
        $grid.Add($line) | Out-Null
      }
    }
    # merged ranges of row 1 (system group headers) - per-cell MergeArea
    $merges = New-Object 'System.Collections.Generic.List[object]'
    $seen = @{}
    for ($c = 1; $c -le $cols; $c++) {
      $cell = $ws.Cells(1, $c)
      if ($cell.MergeCells) {
        $a = $cell.MergeArea
        $key = "$($a.Row),$($a.Column),$($a.Rows.Count),$($a.Columns.Count)"
        if (-not $seen.ContainsKey($key)) {
          $seen[$key] = $true
          $merges.Add(@{ r = $a.Row; c = $a.Column; nr = $a.Rows.Count; nc = $a.Columns.Count }) | Out-Null
        }
      }
    }
    $obj = [ordered]@{
      name = $ws.Name
      rows = $rows
      cols = $cols
      row1Merges = $merges
      grid = $grid
    }
    $fn = Join-Path $outDir ($ws.Name + '.json')
    $json = ConvertTo-Json -InputObject $obj -Depth 5
    [System.IO.File]::WriteAllText($fn, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output ("OK " + $ws.Name + " rows=$rows cols=$cols merges=" + $merges.Count)
  }
  $wb.Close($false)
} finally {
  $excel.Quit()
  [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
Write-Output "DONE -> $outDir"