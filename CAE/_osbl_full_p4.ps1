# Build sheets
$ruOf = { param($st) if ($ruMap.ContainsKey($st)) { $ruMap[$st] } else { 'Нет данных' } }
$ctrlOf = { param($st) if ($ctrlMap.ContainsKey($st)) { $ctrlMap[$st] } else { 'Нет данных' } }
$h1 = @('№ п/п','Лист','Система — тег','Система — наименование EN','Система — наименование RU','Контроллер блокировки','№ причины','Причина (описание) EN','Входные сигналы EN','Тег входного сигнала EN','P&ID','Следствия EN')
$r1 = New-Object 'System.Collections.Generic.List[object]'
foreach ($i in $items) {
  $st = ([string]$i.sysTag).Trim().ToUpperInvariant()
  $rn = & $ruOf $st; $cv = & $ctrlOf $st
  $effE = (($i.effects | ForEach-Object { (([string]$_.tag).Trim() + ' — ' + ([string]$_.actionEN).Trim() + ' [' + $_.mark + ']') }) -join "`n")
  $r1.Add(@([string]$i.seq, [string]$i.sheet, [string]$i.sysTag, [string]$i.sysNameEN, $rn, $cv, [string]$i.no, [string]$i.descEN, [string]$i.inputEN, [string]$i.tagEN, [string]$i.pid, $effE)) | Out-Null
}
$h2 = @('№ п/п','Лист','Система — тег','Система — наименование EN','Система — наименование RU','Контроллер блокировки','№ причины','Причина EN','Тег следствия','Следствие — описание EN','Действие EN','Метка')
$r2 = New-Object 'System.Collections.Generic.List[object]'; $c2 = 0
foreach ($i in $items) {
  $st = ([string]$i.sysTag).Trim().ToUpperInvariant(); $rn = & $ruOf $st; $cv = & $ctrlOf $st
  foreach ($e in $i.effects) { $c2++; $r2.Add(@([string]$c2, [string]$i.sheet, [string]$i.sysTag, [string]$i.sysNameEN, $rn, $cv, [string]$i.no, [string]$i.descEN, ([string]$e.tag).Trim(), ([string]$e.descEN).Trim(), ([string]$e.actionEN).Trim(), [string]$e.mark)) | Out-Null }
}
$h3 = @('Лист','Тег системы','Наименование EN','Наименование RU','Контроллер')
$r3 = New-Object 'System.Collections.Generic.List[object]'
foreach ($s in $systems) { $st = ([string]$s.tag).Trim().ToUpperInvariant(); $rn = & $ruOf $st; $cv = & $ctrlOf $st; $r3.Add(@([string]$s.sheet, ([string]$s.tag).Trim(), ([string]$s.nameEN).Trim(), $rn, $cv)) | Out-Null }
$h4 = @('Тип','Метка','№ п/п','Лист','Система — тег','Система — наименование EN','Система — наименование RU','Контроллер блокировки','№ причины','Причина EN','Тип тега','Тег','Действие EN','Метка','Loop','TYPNO','Марка','Наименование','Тип объекта','Шаблон','Контроллер','Файл ТЕХНОЛ_ОБЪЕКТЫ','Способ поиска')
$r4 = New-Object 'System.Collections.Generic.List[object]'; $c4 = 0
foreach ($i in $items) {
  $st = ([string]$i.sysTag).Trim().ToUpperInvariant(); $rn = & $ruOf $st; $cv = & $ctrlOf $st
  $sysTagDisp = if ($st -ne '') { $i.sysTag } else { '(ITCC)' }
  $c4++
  $mk = (([string]$i.tagEN).Trim().ToUpperInvariant() + '||' + $st)
  $mm = if ($matchMap.ContainsKey($mk)) { $matchMap[$mk] } else { $null }
  $v = @('','','','','','','','','')
  if ($null -ne $mm) { $v = @($mm.loop,$mm.typno,$mm.marka,$mm.name,$mm.tipo,$mm.shab,$mm.ctrl,$mm.file,$mm.sposob) } elseif (([string]$i.tagEN).Trim() -ne '') { $v = @('Нет данных')*9 }
  $r4.Add(@('Причина','',[string]$c4,[string]$i.sheet,$sysTagDisp,[string]$i.sysNameEN,$rn,$cv,[string]$i.no,[string]$i.descEN,'ВХОД (INPUT)',([string]$i.tagEN).Trim(),'','','','','','','','','','')) | Out-Null
  foreach ($e in $i.effects) {
    $c4++; $effTag = ([string]$e.tag).Trim().ToUpperInvariant()
    $ek = $effTag + '||' + $st; $em = if ($matchMap.ContainsKey($ek)) { $matchMap[$ek] } else { $null }
    $ev = @('','','','','','','','','')
    if ($null -ne $em) { $ev = @($em.loop,$em.typno,$em.marka,$em.name,$em.tipo,$em.shab,$em.ctrl,$em.file,$em.sposob) } elseif ($effTag -ne '') { $ev = @('Нет данных')*9 }
    $r4.Add(@('Следствие',[string]$e.mark,[string]$c4,[string]$i.sheet,$sysTagDisp,[string]$i.sysNameEN,$rn,$cv,[string]$i.no,[string]$i.descEN,'СЛЕДСТВИЕ',$effTag,([string]$e.actionEN).Trim(),[string]$e.mark) + $ev) | Out-Null
  }
}
$h5 = @('Показатель','Значение'); $r5 = New-Object 'System.Collections.Generic.List[object]'
$mx=0;$mi=0;$mp=0; foreach ($i in $items) { foreach ($e in $i.effects) { switch ($e.mark) { 'X'{$mx++} 'I'{$mi++} 'P'{$mp++} } } }
[void]$r5.Add(@('Источник','CAE_OSBL.docx (OSBL)')); [void]$r5.Add(@('Систем блокировок',[string]$systems.Count)); [void]$r5.Add(@('Блокировок — причин',[string]$items.Count)); [void]$r5.Add(@('Пересечений всего',[string]$crossCount)); [void]$r5.Add(@('   X',[string]$mx)); [void]$r5.Add(@('   I',[string]$mi)); [void]$r5.Add(@('   P',[string]$mp)); [void]$r5.Add(@('Теги найдено',[string]$fc + ' из ' + $matchMap.Count))