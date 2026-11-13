# Build the output workbook data from the CAE model.
$ErrorActionPreference = 'Stop'
$modelPath = Join-Path $env:TEMP 'cae_model.json'
$outPath = 'C:\VSCode\CAE\TX.CAE-0001_3_ER-20260717_EBSM_Перечень блокировок.xlsx'

$m = Get-Content $modelPath -Raw -Encoding UTF8 | ConvertFrom-Json
$items = @($m.items)
$systems = @($m.systems)

# load controller per interlock from Контроллер_EBSM_блокировок.xlsx (key = Блокировка (B))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$ctrlMap = @{}
$ctrlPath = 'C:\VSCode\CAE\Контроллер_EBSM_блокировок.xlsx'
if (Test-Path $ctrlPath) {
  $zip = [System.IO.Compression.ZipFile]::OpenRead($ctrlPath)
  try {
    $we = $zip.GetEntry('xl/worksheets/sheet1.xml')
    $srC = New-Object System.IO.StreamReader($we.Open(), [System.Text.Encoding]::UTF8)
    $rawC = $srC.ReadToEnd()
    $srC.Close()
    $cRows = [regex]::Matches($rawC, '<row[^>]*r="(\d+)"[^>]*>(.*?)</row>')
    foreach ($cr in $cRows) {
      $cC = [regex]::Match($cr.Groups[2].Value, '<c r="C\d+"[^>]*>.*?</c>')
      $cF = [regex]::Match($cr.Groups[2].Value, '<c r="F\d+"[^>]*>.*?</c>')
      $cv = [regex]::Match($cC.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
      $fv = [regex]::Match($cF.Value, '<t[^>]*>(.*?)</t>').Groups[1].Value
      if ($cv -ne '' -and $cv -ne 'Блокировка (B)' -and $fv -ne '') { $ctrlMap[$cv.Trim().ToUpperInvariant()] = $fv.Trim() }
    }
  } finally { $zip.Dispose() }
}
function Get-CtrlOf([string]$sysTag) {
  $st = ([string]$sysTag).Trim()
  if ($st -eq '' -or $st -eq '(ITCC)') { return 'Нет данных' }
  $k = $st.ToUpperInvariant()
  if ($ctrlMap.ContainsKey($k)) { return $ctrlMap[$k] }
  $k2 = $k -replace '\s*[（(]([Nn]ote|Примечание)\s*\d+[）)]\s*$', ''
  if ($k2 -ne $k -and $ctrlMap.ContainsKey($k2)) { return $ctrlMap[$k2] }
  return 'Нет данных'
}

function Effect-LineEN($e) {
  $tag = ([string]$e.tag).Trim()
  $act = ([string]$e.actionEN).Trim()
  $des = ([string]$e.descEN).Trim()
  $left = if ($tag -ne '') { $tag } else { $des }
  $mid = if ($tag -ne '' -and $des -ne '' -and $des -ne $left) { $des } else { '' }
  $parts = @($left)
  if ($mid -ne '') { $parts += $mid }
  if ($act -ne '') { $parts += $act }
  return (($parts -join ' — ') + ' [' + $e.mark + ']')
}
function Effect-LineRU($e) {
  $tag = ([string]$e.tag).Trim()
  $act = ([string]$e.actionRU).Trim()
  $des = ([string]$e.descRU).Trim()
  $left = if ($tag -ne '') { $tag } else { $des }
  $mid = if ($tag -ne '' -and $des -ne '' -and $des -ne $left) { $des } else { '' }
  $parts = @($left)
  if ($mid -ne '') { $parts += $mid }
  if ($act -ne '') { $parts += $act }
  return (($parts -join ' — ') + ' [' + $e.mark + ']')
}
# --- sheet 1: Перечень блокировок (one row per cause) ---
$h1 = @('№ п/п','Лист','Строка источника','Система блокировки — тег','Система — наименование EN','Система — наименование RU','Контроллер блокировки','№ причины','Причина (описание) EN','Причина (описание) RU','Входные сигналы EN','Входные сигналы RU','Тег входного сигнала EN','Тег входного сигнала RU','P&ID','Следствия EN','Следствия RU')
$rows1 = New-Object 'System.Collections.Generic.List[object]'
$crossCount = 0
foreach ($i in $items) {
  $effE = (($i.effects | ForEach-Object { Effect-LineEN $_ }) -join "`n")
  $effR = (($i.effects | ForEach-Object { Effect-LineRU $_ }) -join "`n")
  $crossCount += @($i.effects).Count
  $sysTagDisp = if (([string]$i.sysTag).Trim() -ne '') { $i.sysTag } else { '(ITCC)' }
  $ctrlVal = Get-CtrlOf $i.sysTag
  $rows1.Add(@(
    [string]$i.seq, [string]$i.sheet, [string]$i.sheetRow, $sysTagDisp,
    [string]$i.sysNameEN, [string]$i.sysNameRU, $ctrlVal, [string]$i.no,
    [string]$i.descEN, [string]$i.descRU,
    (([string]$i.inputEN) -replace "`n", " `n"), (([string]$i.inputRU) -replace "`n", " `n"),
    (([string]$i.tagEN) -replace "`n", " `n"), (([string]$i.tagRU) -replace "`n", " `n"),
    [string]$i.pid, $effE, $effR
  )) | Out-Null
}
# --- sheet 2: Пересечения причин и следствий (long) ---
$h2 = @('№ п/п','Лист','Система — тег','Система — наименование EN','Система — наименование RU','№ причины','Причина EN','Причина RU','Тег следствия','Следствие — описание EN','Следствие — описание RU','Действие EN','Действие RU','Метка')
$rows2 = New-Object 'System.Collections.Generic.List[object]'
$c2 = 0
foreach ($i in $items) {
  foreach ($e in $i.effects) {
    $c2++
    $sysTagDisp = if (([string]$i.sysTag).Trim() -ne '') { $i.sysTag } else { '(ITCC)' }
    $rows2.Add(@(
      [string]$c2, [string]$i.sheet, $sysTagDisp,
      [string]$i.sysNameEN, [string]$i.sysNameRU, [string]$i.no,
      [string]$i.descEN, [string]$i.descRU,
      ([string]$e.tag).Trim(), ([string]$e.descEN).Trim(), ([string]$e.descRU).Trim(),
      ([string]$e.actionEN).Trim(), ([string]$e.actionRU).Trim(), [string]$e.mark
    )) | Out-Null
  }
}

# --- sheet 3: Реестр систем ---
$h3 = @('Лист','Тег системы','Наименование EN','Наименование RU','Связанная блокировка — тег','Связанная блокировка — наименование EN','Колонки следствий (в листе-источнике)')
$rows3 = New-Object 'System.Collections.Generic.List[object]'
foreach ($s in $systems) {
  $rows3.Add(@(
    [string]$s.sheet, ([string]$s.tag).Trim(), ([string]$s.nameEN).Trim(), ([string]$s.nameRU).Trim(),
    ([string]$s.rel).Trim(), ([string]$s.relName).Trim(),
    ($s.start.ToString() + '-' + $s.end.ToString())
  )) | Out-Null
}

# --- sheet 4: Сводка ---
$h4 = @('Показатель','Значение')
$markX = 0; $markI = 0; $markP = 0
foreach ($i in $items) { foreach ($e in $i.effects) { switch ($e.mark) { 'X' { $markX++ } 'I' { $markI++ } 'P' { $markP++ } } } }
$sheetsList = @($items | ForEach-Object { $_.sheet } | Select-Object -Unique)
$rows4 = New-Object 'System.Collections.Generic.List[object]'
[void]$rows4.Add(@('Исходный файл','CAE\TX.CAE-0001_3_ER-20260717_EBSM.xlsx (листы SHEET 2..19, -EN)'))
[void]$rows4.Add(@('Проанализировано листов', [string]$sheetsList.Count))
[void]$rows4.Add(@('Систем блокировок (Z / I / ITCC)', [string]$systems.Count))
[void]$rows4.Add(@('Блокировок — причин (строк перечня)', [string]$items.Count))
[void]$rows4.Add(@('Пересечений «причина — следствие» всего', [string]$crossCount))
[void]$rows4.Add(@('   отметка X (TRIP, CLOSE, OPEN, ACTIVATE, STOP)', [string]$markX))
[void]$rows4.Add(@('   отметка I (внутренние действия DCS)', [string]$markI))
[void]$rows4.Add(@('   отметка P (Permissive — разрешающее условие, листы 17-19)', [string]$markP))
[void]$rows4.Add(@('Дублирующие листы -RU', 'Не учитывались как отдельные блокировки; использованы только для русских наименований и описаний'))
[void]$rows4.Add(@('Примечание 1','SHEET 2: причина №3 в исходном документе отсутствует (нумерация 1, 2, 4)'))
[void]$rows4.Add(@('Примечание 2','SHEET 17: причины в строках 43-46 (разрешающие условия запуска K-101B) не имеют отметок в исходном документе; система 1110-I-1605 присвоена по порядку следования'))
[void]$rows4.Add(@('Примечание 3','SHEET 18/19 (ITCC): единственная система управления задана заголовком листа; метка P означает разрешающее условие запуска'))
[void]$rows4.Add(@('Примечание 4','SHEET 14/15 (Emergency Empty): каждой системе соответствует один отсечной клапан; причина — кнопка SIS'))
[void]$rows4.Add(@('Лист «Теги причин и следствий»', 'Каждый тег причины и каждое следствие — отдельной строкой: ' + ($rows5.Count - $crossCount) + ' тегов причин + ' + $crossCount + ' следствий'))

# --- sheet 5: Теги причин и следствий (each row = one tag of a cause OR one effect tag) ---
# --- + lookup in ТЕХНОЛ_ОБЪЕКТЫ (primary key: Loop) ---
$h5 = @('Тип','Метка','№ п/п','Лист','Строка источника','Система — тег','Система — наименование EN','Система — наименование RU','Контроллер блокировки','№ причины','Причина — описание EN','Причина — описание RU','Тип тега','Тег','Парный тег','Следствие — описание EN','Следствие — описание RU','Действие EN','Действие RU','P&ID',
'Loop','TYPNO','Марка','Наименование','Тип объекта','Шаблон','Контроллер','Файл ТЕХНОЛ_ОБЪЕКТЫ','Способ поиска',
'Контур: Loop','Контур: TYPNO','Контур: Марка','Контур: Контроллер','Контур: Файл ТЕХНОЛ_ОБЪЕКТЫ')
$rows5 = New-Object 'System.Collections.Generic.List[object]'
$c5 = 0
function Split-Tags([string]$s) {
  @(@([string]$s -split "`r?`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
}
# load match results (key = cleaned uppercase tag)
$matchPath = Join-Path $env:TEMP 'cae_match.json'
$matchMap = @{}
if (Test-Path $matchPath) {
  $mj = Get-Content $matchPath -Raw -Encoding UTF8 | ConvertFrom-Json
  foreach ($pr in $mj.PSObject.Properties) { $matchMap[$pr.Name] = $pr.Value }
}
function Get-LookupCols([string]$tag, [string]$pairTag, [string]$sysTag) {
  # priority: result for the main tag; if not found and a pair tag exists - result for the pair
  # key = tag + '||' + system (the same tag may belong to different interlocks with different search scopes)
  $t = ([string]$tag).Trim()
  $p = ([string]$pairTag).Trim()
  $s = ([string]$sysTag).Trim().ToUpperInvariant()
  $mMain = $null
  if ($t -ne '' -and $t -ne '—') {
    $k = $t.ToUpperInvariant() + '||' + $s
    if ($matchMap.ContainsKey($k)) { $mMain = $matchMap[$k] }
  }
  if ($null -ne $mMain) {
    return @([string]$mMain.loop, [string]$mMain.typno, [string]$mMain.marka, [string]$mMain.name, [string]$mMain.tipo, [string]$mMain.shab, [string]$mMain.ctrl, [string]$mMain.file, [string]$mMain.sposob)
  }
  if ($p -ne '' -and $p -ne '—') {
    $kp = $p.ToUpperInvariant() + '||' + $s
    if ($matchMap.ContainsKey($kp)) {
      $mp = $matchMap[$kp]
      return @([string]$mp.loop, [string]$mp.typno, [string]$mp.marka, [string]$mp.name, [string]$mp.tipo, [string]$mp.shab, [string]$mp.ctrl, [string]$mp.file, ('По парному тегу ' + $p + ': ' + [string]$mp.sposob))
    }
  }
  if ($t -eq '' -or $t -eq '—') { return @('', '', '', '', '', '', '', '', '') }
  return @('Нет данных','Нет данных','Нет данных','Нет данных','Нет данных','Нет данных','Нет данных','Нет данных','не найдено')
}
function Get-ContourCols([string]$tag, [string]$sysTag) {
  # contour (tech object itself) of the row's main tag
  $t = ([string]$tag).Trim()
  if ($t -eq '' -or $t -eq '—') { return @('', '', '', '', '') }
  $k = $t.ToUpperInvariant() + '||' + ([string]$sysTag).Trim().ToUpperInvariant()
  $m = $null
  if ($matchMap.ContainsKey($k)) { $m = $matchMap[$k] }
  if ($null -ne $m -and ([string]$m.cLoop).Trim() -ne '') {
    return @([string]$m.cLoop, [string]$m.cTypno, [string]$m.cMarka, [string]$m.cCtrl, [string]$m.cFile)
  }
  return @('Нет данных','Нет данных','Нет данных','Нет данных','Нет данных')
}
foreach ($i in $items) {
  $sysTagDisp = if (([string]$i.sysTag).Trim() -ne '') { $i.sysTag } else { '(ITCC)' }
  $base = @([string]$i.sheet, [string]$i.sheetRow, $sysTagDisp, [string]$i.sysNameEN, [string]$i.sysNameRU, (Get-CtrlOf $i.sysTag), [string]$i.no, [string]$i.descEN, [string]$i.descRU)
  $inTags = @(Split-Tags $i.inputEN)
  $inTagsRU = @(Split-Tags $i.inputRU)
  $pairTagsEN = @(Split-Tags $i.tagEN)
  $pairTagsRU = @(Split-Tags $i.tagRU)
  if ($inTags.Count -gt 0) {
    for ($k = 0; $k -lt $inTags.Count; $k++) {
      $c5++
      $pairE = if ($k -lt $pairTagsEN.Count) { $pairTagsEN[$k] } else { '' }
      $pairR = if ($k -lt $pairTagsRU.Count) { $pairTagsRU[$k] } else { '' }
      $pairDisp = if ($pairE -ne '' -or $pairR -eq '') { $pairE } else { $pairR }
      $rows5.Add(@('Причина', '', [string]$c5) + $base + @('ВХОД (INPUT)', $inTags[$k], $pairDisp, '', '', '', '', [string]$i.pid) + (Get-LookupCols $inTags[$k] $pairDisp $i.sysTag) + (Get-ContourCols $inTags[$k] $i.sysTag)) | Out-Null
    }
  } elseif ($pairTagsEN.Count -gt 0) {
    for ($k = 0; $k -lt $pairTagsEN.Count; $k++) {
      $c5++
      $rows5.Add(@('Причина', '', [string]$c5) + $base + @('№ тех. поз. (вход)', $pairTagsEN[$k], '', '', '', '', '', [string]$i.pid) + (Get-LookupCols $pairTagsEN[$k] '' $i.sysTag) + (Get-ContourCols $pairTagsEN[$k] $i.sysTag)) | Out-Null
    }
  } else {
    $c5++
    $rows5.Add(@('Причина', '', [string]$c5) + $base + @('—', '', '', '', '', '', '', [string]$i.pid) + (Get-LookupCols '' '' $i.sysTag) + (Get-ContourCols '' $i.sysTag)) | Out-Null
  }
  foreach ($e in $i.effects) {
    $c5++
    $desE = ([string]$e.descEN).Trim()
    $desR = ([string]$e.descRU).Trim()
    $rows5.Add(@(
      'Следствие', [string]$e.mark, [string]$c5) + $base + @('СЛЕДСТВИЕ', ([string]$e.tag).Trim(), '',
      $desE, $desR, ([string]$e.actionEN).Trim(), ([string]$e.actionRU).Trim(), [string]$i.pid
    ) + (Get-LookupCols ([string]$e.tag).Trim() '' $i.sysTag) + (Get-ContourCols ([string]$e.tag).Trim() $i.sysTag)) | Out-Null
  }
}

# --- сводка по сопоставлению с ТЕХНОЛ_ОБЪЕКТЫ ---
$mTotal = $matchMap.Count; $mFound = 0
$mByFile = @{}; $mBySposob = @{}
foreach ($v in $matchMap.Values) {
  if ($null -ne $v) {
    $mFound++
    $fk = [string]$v.file; if (-not $mByFile.ContainsKey($fk)) { $mByFile[$fk] = 0 }
    $mByFile[$fk] = [int]$mByFile[$fk] + 1
    $sk = [string]$v.sposob; if (-not $mBySposob.ContainsKey($sk)) { $mBySposob[$sk] = 0 }
    $mBySposob[$sk] = [int]$mBySposob[$sk] + 1
  }
}
[void]$rows4.Add(@('Сопоставление с ТЕХНОЛ_ОБЪЕКТЫ (уникальные теги)', 'Найдено: ' + $mFound + ' из ' + $mTotal + '; не найдено: ' + ($mTotal - $mFound) + ' — преимущественно входы от вендорских пакетов (TZIAHH/PZIALL/VZIA/MXA/XS) и газодетекторы (GGUT/GBT/GMCT/GHT/GET), отсутствующие в файлах объектов'))
foreach ($kv in ($mByFile.GetEnumerator() | Sort-Object Value -Descending)) { [void]$rows4.Add(@('   найдено в файле', $kv.Key + ': ' + $kv.Value)) }
foreach ($kv in ($mBySposob.GetEnumerator() | Sort-Object Value -Descending)) { [void]$rows4.Add(@('   способ поиска', $kv.Key + ': ' + $kv.Value)) }
[void]$rows4.Add(@('Сопоставление: правила', '1) точное совпадение Loop; 2) совпадение без суффикса (-N); 3) вхождение в Loop; 4) вхождение в Наименование; 5) вхождение в Описание; 6) вхождение без префикса системы и дефисов (межконтроллерные сигналы); 7) для ненайденных тегов — сходство описания (EN): пересечение слов описания причины/следствия с Наименованием+Описанием объекта ≥ 60% и ≥ 3 общих слова, в пределах одного префикса системы (например, 1130-YDO-35901 -> 1130-YZA-35901A «SIS to ITCC active K-301A ITCC interlock stop»). Для датчиков загазованности дополнительно проверяется вариант функционального кода с заменой T на IA (GET->GEIA, GBT->GBIA, GMCT->GMCIA, GGUT->GGUIA и т.д. — I: отображение в АСУ ТП, A: alarm/наличие уставок). Для двигателей вспомогательных насосов — вариант P -> PM (1120-P-003A = 1120-PM-003A). Внутри одного Loop выбирается запись с максимальным совпадением KKS с искомым тегом. Для блокировок -I- (РСУ/DCS) теги ищутся ТОЛЬКО в EBSM_DCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx (с ESD/SCS и GDS не связываются). Если основной тег не найден, а парный (№ тех. поз.) найден — подставляется результат по парному с пометкой в «Способ поиска». Если не найдено ничего — «Нет данных». Колонки «Контур: ...» — сам технологический объект тега (не его сигналы): точное совпадение Loop тега с учётом P->PM и T->IA, либо Loop, извлечённый из Наименования (например, 1115-P-101A -> контур 1115-PM-101A; 1110-K-101A -> 1110-KM-101A-1); при отсутствии — «Нет данных»'))
[void]$rows4.Add(@('Сопоставление: цифровая часть Loop', 'Цифровая часть Loop обязана совпадать с цифровой частью тега (без учёта ведущих нулей) во всех стратегиях поиска и в сходстве описаний. Исключение — двигатели и компрессоры/испарители (теги с кодом K, E и P): они ищутся по комплексному правилу, в том числе по Наименованию, где номер сигнала может отличаться от номера контура (например, 1110-K-101A -> сигнал останова 1110-MXI-00101A, 1140-E-414 -> 1140-EM-414). Благодаря правилу и исправлению суффиксного поиска (Base: отрезается только короткий канал -1/-2, а не номер контура) отсекаются неверные связи, например 1110-TZIAHH-11814 -/-> 1110-TZIA-11714, 1115-HZS-10004 -/-> 1115-HZS-10007, 1130-TZIA-35024 -/-> 1130-TZIA-30110'))
[void]$rows4.Add(@('Сопоставление: Наименование — буквы', 'Стратегия 4 (вхождение тега в Наименование): приоритет файлов для блокировок Z — EBSM_SCS, затем EBSM_DCS. Найденный Loop обязан совпадать с тегом по первой букве функционального кода (например, 1115-PZIALL-10301 -> 1115-PZIA-10301). Исключение для двигателей/компрессоров/насосов/испарителей (теги K, E и P): их сигналы имеют другие буквы и разрешены, если Loop начинается на «M» (например, для следствия 1110-K-101A сигнал останова 1110-MXI-00101A, для 1140-E-414 сигнал останова 1140-MXI-00414A, для 1115-P-101A — 1115-MXI-00101A). Объекты GDS для не-G тегов по-прежнему исключены'))
[void]$rows4.Add(@('Сопоставление: GDS-исключение', 'Объекты GDS (газодетекторы) сопоставляются с тегами только если функциональный код тега начинается на «G» (GET/GBT/GMCT/... с преобразованием T->IA). По Наименованию/Описанию/сходству газодетекторы с тегами оборудования не связываются (например, 1130-K-301A больше не связывается с 1130-GMCIA-3091 «1130-K-301A Acoustic enclosure»); следствия «To ITCC active K-301A/B ITCC interlock stop» блокировки 1130-Z-3003 корректно связываются через сходство описаний с 1130-YZA-35901A/36001A'))
[void]$rows4.Add(@('Сопоставление: сходство описаний — контекст', 'Сходство описаний (правило 7) применяется по контексту тега: для тегов-следствий сравнивается только описание следствия, для тегов-причин/входов — только описание причины. Это исключает ложные связки (например, разрешающий сигнал 1110-MHS-00101C блокировки 1110-I-1604 больше не связывается с 1110-PIA-11504 «Oil supply pressure» по тексту причины — сам сигнал MHS в файлах объектов отсутствует, поэтому «Нет данных»)'))
[void]$rows4.Add(@('Сопоставление: приоритет SCS', 'Для блокировок Z файл EBSM_SCS имеет наивысший приоритет: если тег найден в EBSM_SCS (например, сигнал останова 1110-MXI-00101A для 1110-K-101A), результат сохраняется как есть и не заменяется на Loop из другого файла (1110-KM-101A-1 в EBSM_DCS). Разрешение Loop по Наименованию применяется только к записям, найденным в EBSM_DCS/GDS'))

[void]$rows4.Add(@('Контроллер блокировок', 'Из файла Контроллер_EBSM_блокировок.xlsx (лист Общий_перечень_EBSM_блокировок): для каждой блокировки указан контроллер, где располагается её логика (колонка «Контроллер блокировки» на листе «Перечень блокировок»). Для ITCC-систем (SHEET 18/19) и блокировок, отсутствующих в файле (1140-I-4001, 1140-Z-9136), — «Нет данных»'))

Write-Output ("rows1=" + $rows1.Count + " rows2=" + $rows2.Count + " rows3=" + $rows3.Count + " rows5=" + $rows5.Count + " crossings=" + $crossCount)
$prep = @{ h1 = $h1; h2 = $h2; h3 = $h3; h4 = $h4; h5 = $h5; r1 = $rows1; r2 = $rows2; r3 = $rows3; r4 = $rows4; r5 = $rows5; out = $outPath }
$prepPath = Join-Path $env:TEMP 'cae_prep.json'
[System.IO.File]::WriteAllText($prepPath, ($prep | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
Write-Output "PREP -> $prepPath"