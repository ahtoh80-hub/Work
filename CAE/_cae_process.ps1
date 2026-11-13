# Parse CAE sheets (EN + RU) into a unified model JSON with validation report.
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:TEMP 'cae_json'
$modelPath = Join-Path $env:TEMP 'cae_model.json'
$reportPath = Join-Path $env:TEMP 'cae_report.txt'

function T($s) { if ($null -eq $s) { '' } else { ([string]$s).Trim() } }

function Get-CellByGrid($grid, $rows, $cols, $r, $c) {
  if ($r -lt 1 -or $r -gt $rows) { return '' }
  if ($c -lt 1 -or $c -gt $cols) { return '' }
  if ($c -gt $grid[$r-1].Count) { return '' }
  $v = $grid[$r-1][$c-1]
  if ($null -eq $v) { '' } else { [string]$v }
}

function Parse-Sheet($sh, $capCols, $relSemantics) {
  $grid = $sh.grid
  $rows = $sh.rows
  $cols = [Math]::Min($sh.cols, $capCols)
  $res = @{ sh = $sh; rows = $rows; cols = $cols }

  $headerRow = 0
  for ($r = 1; $r -le $rows; $r++) {
    $a = T (Get-CellByGrid $grid $rows $cols $r 1)
    $b = T (Get-CellByGrid $grid $rows $cols $r 2)
    if (($a -eq 'NO.' -or $a -eq '№') -and ($b -like 'DESCRIPTION*' -or $b -like 'ОПИСАНИЕ*')) { $headerRow = $r; break }
  }
  if ($headerRow -eq 0) { throw "header row not found ($($sh.name))" }
  $res.headerRow = $headerRow

  $tagRow = 0; $tagLabelCol = 0
  for ($r = 1; $r -lt $headerRow; $r++) {
    for ($c = 4; $c -le 10; $c++) {
      $v = T (Get-CellByGrid $grid $rows $cols $r $c)
      if ($v -match '^(TAG NO\.|№ ТЕХНОЛОГ\. ПОЗ\.|ТАГОВЫЙ НОМЕР)$') { $tagRow = $r; $tagLabelCol = $c; break }
    }
    if ($tagRow -gt 0) { break }
  }
  if ($tagRow -eq 0) { throw "tag row not found ($($sh.name))" }
  $res.tagRow = $tagRow
  $effectStart = $tagLabelCol + 1
  $res.effectStart = $effectStart

  # verify pid label row
  $pidRow = $tagRow + 1
  $pidLabelCol = 0
  for ($c = 2; $c -le 12; $c++) {
    if (T (Get-CellByGrid $grid $rows $cols $pidRow $c) -match '^№?\s*P&ID') { $pidLabelCol = $c; break }
  }
  $res.pidRow = $pidRow
  $res.pidCol = $effectStart - 1
  if ($pidLabelCol -ne 0 -and $pidLabelCol -ne $res.pidCol) {
    $res.pidLabelMismatch = "$pidLabelCol <> $($res.pidCol)"
  }

  $inputCol = 0; $causeTagCol = 0
  for ($c = 1; $c -le 10; $c++) {
    $v = T (Get-CellByGrid $grid $rows $cols $headerRow $c)
    if ($v -match '^(INPUT|ВХОД|входные данные)$') { $inputCol = $c }
    if ($v -match '^(TAG NO\.|№ ТЕХНОЛОГ\. ПОЗ\.|ТАГОВЫЙ НОМЕР)$') { $causeTagCol = $c }
  }
  $res.inputCol = $inputCol
  $res.causeTagCol = $causeTagCol

  $maxEffectCol = $effectStart - 1
  for ($c = $effectStart; $c -le $cols; $c++) {
    if (T (Get-CellByGrid $grid $rows $cols $tagRow $c) -ne '') { $maxEffectCol = $c }
  }
  $res.maxEffectCol = $maxEffectCol

  # groups from row 1 non-empty cells within effect area
  $groups = New-Object 'System.Collections.Generic.List[object]'
  $cur = $null
  for ($c = $effectStart; $c -le $maxEffectCol; $c++) {
    $v = T (Get-CellByGrid $grid $rows $cols 1 $c)
    if ($v -ne '') {
      if ($null -ne $cur) { $cur.end = $c - 1; $groups.Add($cur) | Out-Null }
      $cur = @{ start = $c; end = $maxEffectCol; tag = $v }
    }
  }
  if ($null -ne $cur) { $groups.Add($cur) | Out-Null }
  if ($groups.Count -gt 0 -and $groups[0].start -gt $effectStart) {
    $g0 = @{ start = $effectStart; end = ($groups[0].start - 1); tag = '' }
    $groups.Insert(0, $g0)
  }
  $res.groups = $groups

  # register rows: 3..tagRow, col1 non-empty (cols 3/4 may hold a second entry pair or cross-reference)
  $splitCell = { param($s) @(@([string]$s -split "`r?`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }) }
  $register = New-Object 'System.Collections.Generic.List[object]'
  for ($r = 3; $r -le $tagRow; $r++) {
    $t1 = T (Get-CellByGrid $grid $rows $cols $r 1)
    if ($t1 -eq '') { continue }
    $t2 = T (Get-CellByGrid $grid $rows $cols $r 2)
    $t3 = T (Get-CellByGrid $grid $rows $cols $r 3)
    $t4 = T (Get-CellByGrid $grid $rows $cols $r 4)
    $tags1 = @(& $splitCell $t1)
    $names2 = @(& $splitCell $t2)
    $tags3 = @(& $splitCell $t3)
    $names4 = @(& $splitCell $t4)
    for ($i = 0; $i -lt $tags1.Count; $i++) {
      $register.Add(@{
        tag = $tags1[$i]
        name = if ($i -lt $names2.Count) { $names2[$i] } else { '' }
        rel = if ($relSemantics -and $i -lt $tags3.Count) { $tags3[$i] } else { '' }
        relName = if ($relSemantics -and $i -lt $names4.Count) { $names4[$i] } else { '' }
        row = $r
      }) | Out-Null
    }
    # second register pair (same rows list another system in cols 3/4)
    for ($i = 0; $i -lt $tags3.Count; $i++) {
      $register.Add(@{
        tag = $tags3[$i]
        name = if ($i -lt $names4.Count) { $names4[$i] } else { '' }
        rel = ''
        relName = ''
        row = $r
      }) | Out-Null
    }
  }
  $res.register = $register

  # cause rows -> blocks
  $blocks = New-Object 'System.Collections.Generic.List[object]'
  $curRows = New-Object 'System.Collections.Generic.List[int]'
  for ($r = $headerRow + 1; $r -le $rows; $r++) {
    $a = T (Get-CellByGrid $grid $rows $cols $r 1)
    if ($a -match '^(NOTES|ПРИМЕЧАНИЯ)') { break }
    if ($a -match '^\d+$') { $curRows.Add($r) | Out-Null }
    elseif ($curRows.Count -gt 0) { $blocks.Add(@{ rows = $curRows }) | Out-Null; $curRows = New-Object 'System.Collections.Generic.List[int]' }
  }
  if ($curRows.Count -gt 0) { $blocks.Add(@{ rows = $curRows }) | Out-Null }
  $res.blocks = $blocks
  return $res
}
# ============ main ============
$files = Get-ChildItem $dir -Filter '*.json' | Sort-Object Name
$sheets = @{}
foreach ($f in $files) {
  $obj = Get-Content $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
  $sheets[$obj.name] = $obj
}

$warnings = New-Object 'System.Collections.Generic.List[string]'
function Warn([string]$sheet, [string]$msg) { $warnings.Add("[$sheet] $msg") | Out-Null }

$pairs = @()
foreach ($name in $sheets.Keys) {
  if ($name -match '^(SHEET \d+)-EN$') {
    $base = $Matches[1]
    $ru = $base + '-RU'
    $pairs += @{ base = $base; en = $sheets[$name]; ru = $(if ($sheets.ContainsKey($ru)) { $sheets[$ru] } else { $null }) }
  }
}
$pairs = $pairs | Sort-Object { [int]($_.base -replace '\D', '') }

function Get-Reg([hashtable]$map, [string]$tag) {
  if ($map.ContainsKey($tag)) { return $map[$tag] }
  $t2 = $tag -replace '\s*[（(][Nn]ote\s*\d+[）)]\s*$', ''
  $t2 = $t2 -replace '\s*[（(]Примечание\s*\d+[）)]\s*$', ''
  if ($map.ContainsKey($t2)) { return $map[$t2] }
  return $null
}

$model = @{ items = New-Object 'System.Collections.Generic.List[object]'; systems = New-Object 'System.Collections.Generic.List[object]' }
$report = New-Object 'System.Text.StringBuilder'
$seq = 0
foreach ($pair in $pairs) {
  $base = $pair.base
  $relSem = ($base -eq 'SHEET 16')
  $en = Parse-Sheet $pair.en $pair.en.cols $relSem
  $ru = if ($pair.ru) { Parse-Sheet $pair.ru $pair.en.cols ($base -eq 'SHEET 16') } else { $null }

  [void]$report.AppendLine("=== $base (EN rows=$($en.rows) cols=$($en.cols)) effectStart=$($en.effectStart) maxEffectCol=$($en.maxEffectCol) tagRow=$($en.tagRow) headerRow=$($en.headerRow) inputCol=$($en.inputCol) causeTagCol=$($en.causeTagCol)" + $(if ($en.pidLabelMismatch) { " PID-LABEL-MISMATCH: $($en.pidLabelMismatch)" } else { '' }))
  [void]$report.AppendLine("  groups: " + (($en.groups | ForEach-Object { "$($_.start)-$($_.end)'$($_.tag)'" }) -join ' '))
  [void]$report.AppendLine("  register($($en.register.Count)): " + (($en.register | ForEach-Object { "$($_.tag)=$($_.name)" + $(if ($_.rel) { " -> $($_.rel)=$($_.relName)" }) }) -join ' ;; '))
  [void]$report.AppendLine("  blocks: $($en.blocks.Count)  causes/block: " + (($en.blocks | ForEach-Object { $_.rows.Count }) -join ','))

  $markCount = @{ X = 0; I = 0; P = 0 }
  $unknown = @{}
  $grpIssues = New-Object 'System.Collections.Generic.List[string]'
  foreach ($b in $en.blocks) {
    $cnt = @{}
    foreach ($r in $b.rows) {
      for ($c = $en.effectStart; $c -le $en.maxEffectCol; $c++) {
        $v = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols $r $c)
        if ($v -eq '') { continue }
        $m = $v.ToUpper()
        if ($markCount.ContainsKey($m)) { $markCount[$m]++ } else { $unknown[$m] = [int]$unknown[$m] + 1 }
        $g = $null
        foreach ($gg in $en.groups) { if ($c -ge $gg.start -and $c -le $gg.end) { $g = $gg; break } }
        $key = if ($g) { $g.tag } else { '<NO-GROUP>' }
        $cnt[$key] = [int]$cnt[$key] + 1
      }
    }
    $r0 = $b.rows[0]; $r1 = $b.rows[$b.rows.Count - 1]
    if ($cnt.Keys.Count -gt 1) {
      $grpIssues.Add("rows $r0-$r1 : " + (($cnt.GetEnumerator() | ForEach-Object { "'$($_.Key)'=$($_.Value)" }) -join ', ')) | Out-Null
    } elseif ($cnt.Keys.Count -eq 0) {
      $grpIssues.Add("rows $r0-$r1 : NO MARKS") | Out-Null
    }
  }
  [void]$report.AppendLine("  marks: X=$($markCount['X']) I=$($markCount['I']) P=$($markCount['P'])" + $(if ($unknown.Count) { "  UNKNOWN: " + (($unknown.GetEnumerator() | ForEach-Object { "'$($_.Key)'=$($_.Value)" }) -join ', ') } else { '' }))
  foreach ($gi in $grpIssues) { [void]$report.AppendLine("  MULTI-GROUP-BLOCK $gi"); Warn $base "block $gi" }

  if ($en.pidLabelMismatch) { Warn $base "PID label col mismatch: $($en.pidLabelMismatch)" }
  if ($ru) {
    if ($ru.effectStart -ne $en.effectStart -or $ru.maxEffectCol -ne $en.maxEffectCol) { Warn $base "RU layout differs: effectStart RU=$($ru.effectStart) EN=$($en.effectStart); maxCol RU=$($ru.maxEffectCol) EN=$($en.maxEffectCol)" }
    if ($ru.blocks.Count -ne $en.blocks.Count) { Warn $base "block count differs RU=$($ru.blocks.Count) EN=$($en.blocks.Count)" }
  }
# RU maps
  $ruRegMap = @{}; $ruTagMap = @{}
  if ($ru) {
    foreach ($e in $ru.register) { if (-not $ruRegMap.ContainsKey($e.tag)) { $ruRegMap[$e.tag] = $e } }
    for ($c = $ru.effectStart; $c -le $ru.maxEffectCol; $c++) {
      $t = T (Get-CellByGrid $ru.sh.grid $ru.rows $ru.cols $ru.tagRow $c)
      if ($t -ne '' -and -not $ruTagMap.ContainsKey($t)) {
        $ruTagMap[$t] = @{ col = $c; action = (Get-CellByGrid $ru.sh.grid $ru.rows $ru.cols 2 $c); desc = (Get-CellByGrid $ru.sh.grid $ru.rows $ru.cols 3 $c) }
      }
    }
  }
  $enRegMap = @{}
  foreach ($e in $en.register) { if (-not $enRegMap.ContainsKey($e.tag)) { $enRegMap[$e.tag] = $e } }

  # pseudo-system for sheets without a group header row (SHEET 18/19)
  $pseudoEN = ''; $pseudoRU = ''
  if ($en.groups.Count -eq 0) {
    for ($c = 2; $c -le $en.maxEffectCol; $c++) {
      $v = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols 1 $c)
      if ($v -ne '') { $pseudoEN = $v; break }
    }
    if ($ru) {
      for ($c = 2; $c -le $ru.maxEffectCol; $c++) {
        $v = T (Get-CellByGrid $ru.sh.grid $ru.rows $ru.cols 1 $c)
        if ($v -ne '') { $pseudoRU = $v; break }
      }
    }
    $model.systems.Add([ordered]@{
      sheet = $base; tag = '(ITCC)'; nameEN = $pseudoEN; nameRU = $pseudoRU; rel = ''; relName = ''; start = $en.effectStart; end = $en.maxEffectCol
    }) | Out-Null
  }

  # emit systems
  foreach ($g in $en.groups) {
    if ($g.tag -eq '') { continue }
    $reg = Get-Reg $enRegMap $g.tag
    $ruReg = if ($ru) { Get-Reg $ruRegMap $g.tag } else { $null }
    $model.systems.Add([ordered]@{
      sheet = $base
      tag = $g.tag
      nameEN = if ($reg) { $reg.name } else { $g.tag }
      nameRU = if ($ruReg) { $ruReg.name } else { '' }
      rel = if ($reg) { $reg.rel } else { '' }
      relName = if ($reg) { $reg.relName } else { '' }
      start = $g.start; end = $g.end
    }) | Out-Null
  }

  $ruCauseRows = @{}
  if ($ru) { foreach ($b in $ru.blocks) { foreach ($r in $b.rows) { $ruCauseRows[$r] = $true } } }
  $prevSysTag = ''
  $lastGroupIdx = -1

  foreach ($b in $en.blocks) {
    $cnt = @{}
    foreach ($r in $b.rows) {
      for ($c = $en.effectStart; $c -le $en.maxEffectCol; $c++) {
        if ((T (Get-CellByGrid $en.sh.grid $en.rows $en.cols $r $c)) -ne '') {
          $g = $null
          foreach ($gg in $en.groups) { if ($c -ge $gg.start -and $c -le $gg.end) { $g = $gg; break } }
          $key = if ($g) { $g.tag } else { '' }
          $cnt[$key] = [int]$cnt[$key] + 1
        }
      }
    }
    $sysTag = ''; $best = -1
    foreach ($k in $cnt.Keys) { if ($cnt[$k] -gt $best) { $best = $cnt[$k]; $sysTag = $k } }
    if ($sysTag -ne '') {
      for ($gi = 0; $gi -lt $en.groups.Count; $gi++) { if ($en.groups[$gi].tag -eq $sysTag) { $lastGroupIdx = $gi; break } }
    }
    if ($sysTag -eq '') {
      # no marks in block: take the next group after the last assigned one
      $nextIdx = $lastGroupIdx + 1
      if ($nextIdx -lt $en.groups.Count -and $en.groups[$nextIdx].tag -ne '') {
        $sysTag = $en.groups[$nextIdx].tag
        $lastGroupIdx = $nextIdx
        Warn $base ("block rows " + $b.rows[0] + "-" + $b.rows[$b.rows.Count-1] + " has no marks; assigned next system '$sysTag' by order")
      } elseif ($prevSysTag -ne '') {
        $sysTag = $prevSysTag
      }
    }
    $regSys = if ($sysTag) { Get-Reg $enRegMap $sysTag } else { $null }
    foreach ($r in $b.rows) {
      $seq++
      $effects = New-Object 'System.Collections.Generic.List[object]'
      for ($c = $en.effectStart; $c -le $en.maxEffectCol; $c++) {
        $v = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols $r $c)
        if ($v -eq '') { continue }
        $tagE = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols $en.tagRow $c)
        $actR = ''; $desR = ''
        if ($ru) {
          $ruSameColTag = T (Get-CellByGrid $ru.sh.grid $ru.rows $ru.cols $ru.tagRow $c)
          if ($ruSameColTag -eq $tagE) {
            $actR = T (Get-CellByGrid $ru.sh.grid $ru.rows $ru.cols 2 $c)
            $desR = T (Get-CellByGrid $ru.sh.grid $ru.rows $ru.cols 3 $c)
          } elseif ($tagE -ne '' -and $ruTagMap.ContainsKey($tagE)) {
            $actR = T $ruTagMap[$tagE].action
            $desR = T $ruTagMap[$tagE].desc
          } elseif ($tagE -ne '') {
            Warn $base "RU effect tag not found: '$tagE' (row $r)"
          }
        }
        $effects.Add([ordered]@{
          tag = $tagE
          actionEN = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols 2 $c)
          actionRU = T $actR
          descEN = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols 3 $c)
          descRU = T $desR
          mark = $v.ToUpper()
        }) | Out-Null
      }
      $ruRowOk = ($ru -ne $null) -and $ruCauseRows.ContainsKey($r)
      if ($ru -and -not $ruRowOk) { Warn $base "RU cause row missing at row $r" }
      $ruRegSys = if ($ru -and $sysTag) { Get-Reg $ruRegMap $sysTag } else { $null }
      $model.items.Add([ordered]@{
        seq = $seq; sheet = $base; sheetRow = $r; sysTag = $sysTag
        sysNameEN = if ($regSys) { $regSys.name } elseif ($sysTag) { $sysTag } else { $pseudoEN }
        sysNameRU = if ($ruRegSys) { $ruRegSys.name } else { $pseudoRU }
        rel = if ($regSys) { $regSys.rel } else { '' }
        relName = if ($regSys) { $regSys.relName } else { '' }
        no = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols $r 1)
        descEN = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols $r 2)
        descRU = if ($ruRowOk) { T (Get-CellByGrid $ru.sh.grid $ru.rows $ru.cols $r 2) } else { '' }
        inputEN = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols $r $en.inputCol)
        inputRU = if ($ruRowOk) { T (Get-CellByGrid $ru.sh.grid $ru.rows $ru.cols $r $ru.inputCol) } else { '' }
        tagEN = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols $r $en.causeTagCol)
        tagRU = if ($ruRowOk) { T (Get-CellByGrid $ru.sh.grid $ru.rows $ru.cols $r $ru.causeTagCol) } else { '' }
        pid = T (Get-CellByGrid $en.sh.grid $en.rows $en.cols $r ($en.effectStart - 1))
        effects = $effects
      }) | Out-Null
    }
    if ($sysTag) { $prevSysTag = $sysTag }
  }
}

$report.Append($warnings.Count.ToString() + " warnings`n") | Out-Null
foreach ($w in $warnings) { [void]$report.AppendLine("WARN $w") }

$modelJson = $model | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($modelPath, $modelJson, (New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($reportPath, $report.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("ITEMS: " + $model.items.Count + "  SYSTEMS: " + $model.systems.Count)
Write-Output ("WARNINGS: " + $warnings.Count)
Write-Output "MODEL -> $modelPath"
Write-Output "REPORT -> $reportPath"