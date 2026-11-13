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
function Read-XlsxColumnar([string]$path) {
  $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
  try {
    $ss = @()
    $ssEntry = $zip.GetEntry('xl/sharedStrings.xml')
    if ($null -ne $ssEntry) { $sr = New-Object System.IO.StreamReader($ssEntry.Open(), [System.Text.Encoding]::UTF8); $xml = [xml]$sr.ReadToEnd(); $sr.Close(); $ss = @($xml.sst.si | ForEach-Object { $_.InnerText }) }
    $wsEntry = $zip.GetEntry('xl/worksheets/sheet1.xml')
    $sr2 = New-Object System.IO.StreamReader($wsEntry.Open(), [System.Text.Encoding]::UTF8)
    $wx = [xml]$sr2.ReadToEnd(); $sr2.Close()
    $rowsOut = New-Object 'System.Collections.Generic.List[object]'
    foreach ($row in $wx.worksheet.sheetData.row) {
      $rn = [int]$row.r; if ($rn -le 1) { continue }
      $cells = @{}; $ec = 1
      foreach ($c in $row.c) { $ref = [string]$c.r; $colIdx = 0
        if ($ref -ne '') { $letters = ($ref -replace '\d',''); $ci = 0; foreach ($ch in $letters.ToCharArray()) { $ci = $ci * 26 + ([int]$ch - 64) }; $colIdx = $ci } else { $colIdx = $ec }
        $ec = $colIdx + 1; $t = [string]$c.t; $val = ''
        if ($t -eq 's') { $val = $ss[[int]$c.v] } elseif ($t -eq 'inlineStr') { $val = [string]$c.is.InnerText } else { $val = [string]$c.v }
        $cells[$colIdx] = $val
      }
      $g = { param($ix) if ($cells.ContainsKey($ix)) { ([string]$cells[$ix]).Trim() } else { '' } }
      $rowsOut.Add([pscustomobject]@{ loop=(& $g 2); kks=(& $g 3); typno=(& $g 4); marka=(& $g 6); tipo=(& $g 7); name=(& $g 8); desc=(& $g 9); ctrl=(& $g 19); shab=(& $g 22) }) | Out-Null
    }
    return $rowsOut
  } finally { $zip.Dispose() }
}
$tbl = New-Object 'System.Collections.Generic.List[object]'
foreach ($fd in @(
  @{ file='C:\VSCode\CAE\OSBL_SCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'; short='OSBL_SCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx' },
  @{ file='C:\VSCode\CAE\OSBL_DCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'; short='OSBL_DCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx' },
  @{ file='C:\VSCode\CAE\OSBL_GDS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'; short='OSBL_GDS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx' }
)) {
  $rowsOut = Read-XlsxColumnar $fd.file; $n = $rowsOut.Count
  $loopU = New-Object string[] $n; $loopNd = New-Object string[] $n; $namesU = New-Object string[] $n; $descsU = New-Object string[] $n; $wordSets = New-Object object[] $n
  $exAll = @{}; $exBaseAll = @{}
  for ($i = 0; $i -lt $n; $i++) {
    $r = $rowsOut[$i]; $loopU[$i] = $r.loop.ToUpperInvariant(); $loopNd[$i] = $loopU[$i] -replace '-',''
    $namesU[$i] = $r.name.ToUpperInvariant(); $descsU[$i] = $r.desc.ToUpperInvariant()
    $k = $loopU[$i]
    if ($k -ne '') { if (-not $exAll.ContainsKey($k)) { $exAll[$k] = New-Object 'System.Collections.Generic.List[int]' }; $exAll[$k].Add($i); $kb = $k -replace '-\d{1,2}$',''; if (-not $exBaseAll.ContainsKey($kb)) { $exBaseAll[$kb] = New-Object 'System.Collections.Generic.List[int]' }; $exBaseAll[$kb].Add($i) }
  }
  for ($i = 0; $i -lt $n; $i++) {
    $r = $rowsOut[$i]; $txt = (([string]$r.name) + ' ' + ([string]$r.desc)).ToLowerInvariant() -replace '\d{4}-[a-z0-9]+(?:-[a-z0-9]+)*',' ' -replace '[^a-z0-9\-]+',' '
    $ws2 = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($w in ($txt -split '\s+')) { $w = $w.Trim('-'); if ($w.Length -ge 2 -and $w -notin @('the','and','of','in','on','at','for','with','from','by','or','an')) { [void]$ws2.Add($w) } }
    $wordSets[$i] = $ws2
  }
  $tbl.Add([pscustomobject]@{ short=$fd.short; n=$n; rows=$rowsOut; loopU=$loopU; loopNd=$loopNd; namesU=$namesU; descsU=$descsU; ws=$wordSets; exAll=$exAll; exBaseAll=$exBaseAll }) | Out-Null
  Write-Output ($fd.short + ' rows=' + $n)
}
function Read-XlsxColumnarModbus([string]$path) {
  $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
  try {
    $wsEntry = $zip.GetEntry('xl/worksheets/sheet1.xml')
    $sr2 = New-Object System.IO.StreamReader($wsEntry.Open(), [System.Text.Encoding]::UTF8)
    $wx = [xml]$sr2.ReadToEnd(); $sr2.Close()
    $rowsOut = New-Object 'System.Collections.Generic.List[object]'
    foreach ($row in $wx.worksheet.sheetData.row) {
      $rn = [int]$row.r; if ($rn -le 1) { continue }
      $cells = @{}; $ec = 1
      foreach ($c in $row.c) {
        $ref = [string]$c.r; $colIdx = 0
        if ($ref -ne '') { $letters = ($ref -replace '\d',''); $ci = 0; foreach ($ch in $letters.ToCharArray()) { $ci = $ci * 26 + ([int]$ch - 64) }; $colIdx = $ci } else { $colIdx = $ec }
        $ec = $colIdx + 1; $t = [string]$c.t; $val = ''
        if ($t -eq 'inlineStr') { $val = [string]$c.is.InnerText } else { $val = [string]$c.v }
        $cells[$colIdx] = $val
      }
      $g = { param($ix) if ($cells.ContainsKey($ix)) { ([string]$cells[$ix]).Trim() } else { '' } }
      $rowsOut.Add([pscustomobject]@{ loop=(& $g 6); kks=(& $g 11); typno=(& $g 7); marka=''; tipo=(& $g 14); name=(& $g 25); desc=(& $g 24); ctrl=(& $g 15); shab='' }) | Out-Null
    }
    return $rowsOut
  } finally { $zip.Dispose() }
}
$mbRows = Read-XlsxColumnarModbus 'C:\VSCode\CAE\OSBL_MODBUS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'
$mbN = $mbRows.Count
$mbLoopU = New-Object string[] $mbN; $mbLoopNd = New-Object string[] $mbN; $mbNamesU = New-Object string[] $mbN; $mbDescsU = New-Object string[] $mbN; $mbWordSets = New-Object object[] $mbN; $mbExAll = @{}
for ($i = 0; $i -lt $mbN; $i++) {
  $r = $mbRows[$i]; $mbLoopU[$i] = $r.loop.ToUpperInvariant(); $mbLoopNd[$i] = $mbLoopU[$i] -replace '-',''
  $mbNamesU[$i] = $r.name.ToUpperInvariant(); $mbDescsU[$i] = $r.desc.ToUpperInvariant()
  $k = $mbLoopU[$i]; if ($k -ne '' -and -not $mbExAll.ContainsKey($k)) { $mbExAll[$k] = New-Object 'System.Collections.Generic.List[int]' }
  if ($k -ne '') { $mbExAll[$k].Add($i) }
}
for ($i = 0; $i -lt $mbN; $i++) {
  $r = $mbRows[$i]; $txt = (([string]$r.name) + ' ' + ([string]$r.desc)).ToLowerInvariant() -replace '\d{4}-[a-z0-9]+(?:-[a-z0-9]+)*',' ' -replace '[^a-z0-9\-]+',' '
  $ws2 = New-Object 'System.Collections.Generic.HashSet[string]'
  foreach ($w in ($txt -split '\s+')) { $w = $w.Trim('-'); if ($w.Length -ge 2 -and $w -notin @('the','and','of','in','on','at','for','with','from','by','or','an')) { [void]$ws2.Add($w) } }
  $mbWordSets[$i] = $ws2
}
$tbl.Add([pscustomobject]@{ short='OSBL_MODBUS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'; n=$mbN; rows=$mbRows; loopU=$mbLoopU; loopNd=$mbLoopNd; namesU=$mbNamesU; descsU=$mbDescsU; ws=$mbWordSets; exAll=$mbExAll; exBaseAll=@{} }) | Out-Null
Write-Output ('OSBL_MODBUS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx rows=' + $mbN)
function Get-FuncCode([string]$kksU) { $s = ([string]$kksU) -replace '^\d{4}-',''; $d = $s.IndexOf('-'); if ($d -gt 0) { return $s.Substring(0,$d) }; return $s }
function Get-LoopNumber([string]$kksU) { $s = ([string]$kksU) -replace '^\d{4}-',''; if ($s.IndexOf('-') -gt 0) { $s = $s.Substring($s.IndexOf('-')+1) }; if ($s.IndexOf('-') -ge 0) { $s = $s.Substring(0,$s.IndexOf('-')) }; $d = ($s -replace '\D','').TrimStart('0'); if ($d -eq '') { $d = '0' }; return $d }
# ---- matching engine (ported from EBSM _technol_match.ps1 — rules of the previous title) ----
function Base-Of([string]$tagU) { return ($tagU -replace '-\d{1,2}$', '') }
function Nd([string]$s) { return (([string]$s).ToUpperInvariant() -replace '-', '') }
function CommonPrefixLen([string]$a, [string]$b) {
  $la = $a.Length; $lb = $b.Length
  $lim = [Math]::Min($la, $lb); $i = 0
  while ($i -lt $lim -and $a[$i] -eq $b[$i]) { $i++ }
  return $i
}
function Select-BestIdx($t, $idxList, [string]$tagU) {
  $refCore = Nd ($tagU -replace '^\d{4}-', '')
  $bestIdx = [int]$idxList[0]
  $bestS1 = -1; $bestS2 = -999999
  foreach ($i in $idxList) {
    $kksNd = Nd $t.rows[$i].kks
    $s1 = CommonPrefixLen $kksNd $refCore
    $s2 = -[Math]::Abs($kksNd.Length - $refCore.Length)
    if ($s1 -gt $bestS1 -or ($s1 -eq $bestS1 -and $s2 -gt $bestS2)) { $bestS1 = $s1; $bestS2 = $s2; $bestIdx = $i }
  }
  return $bestIdx
}
function Add-Cand($list, $seen, [string]$s, [string]$lbl) {
  $s = ([string]$s).Trim()
  if ($s -eq '' -or $seen.ContainsKey($s)) { return }
  $seen[$s] = $true
  $list.Add(@{ s = $s; lbl = $lbl })
}
function Find-MatchOne([string]$tagU, [string]$trLabel, $tblArr) {
  $suffix = $trLabel
  $tagCode = Get-FuncCode $tagU
  $isGasTag = $tagU -match '^\d{4}-G'
  # 1) exact Loop
  foreach ($t in $tblArr) {
    if ($t.exAll.ContainsKey($tagU)) { $i = Select-BestIdx $t $t.exAll[$tagU] $tagU; return @{ i = $i; t = $t; sposob = ('Loop (точное совпадение)' + $suffix) } }
  }
  # 2) Loop without suffix (-N)
  $baseTag = Base-Of $tagU
  foreach ($t in $tblArr) {
    if ($t.exBaseAll.Count -gt 0 -and $t.exBaseAll.ContainsKey($baseTag)) {
      $i = Select-BestIdx $t $t.exBaseAll[$baseTag] $tagU
      $lu2 = $t.loopU[$i]
      $numOk = ($tagCode -in @('K','P','E')) -or ((Get-LoopNumber $lu2) -eq (Get-LoopNumber $tagU))
      if (-not $numOk) { continue }
      $sp = if ($baseTag -eq $tagU) { 'Loop (точное совпадение)' } else { 'Loop (совпадение без суффикса)' }
      return @{ i = $i; t = $t; sposob = ($sp + $suffix) }
    }
  }
  # 3) Loop contains tag
  foreach ($t in $tblArr) {
    if (-not $isGasTag -and $t.short -like '*GDS*') { continue }
    for ($ii = 0; $ii -lt $t.n; $ii++) {
      if ($t.loopU[$ii].Contains($tagU)) {
        $numOk = ($tagCode -in @('K','P','E')) -or ((Get-LoopNumber $t.loopU[$ii]) -eq (Get-LoopNumber $tagU))
        if (-not $numOk) { continue }
        $lu = $t.loopU[$ii]
        $i2 = Select-BestIdx $t $t.exAll[$lu] $tagU
        return @{ i = $i2; t = $t; sposob = ('Loop (вхождение)' + $suffix) }
      }
    }
  }
  # 4) Наименование contains tag
  foreach ($t in $tblArr) {
    if (-not $isGasTag -and $t.short -like '*GDS*') { continue }
    for ($ii = 0; $ii -lt $t.n; $ii++) {
      if ($t.namesU[$ii].Contains($tagU)) {
        $lu = $t.loopU[$ii]; $lc = Get-FuncCode $lu
        $letterOk = ($lc.Substring(0, 1) -eq $tagCode.Substring(0, 1)) -or (($tagCode -in @('K','P','E')) -and $lc.StartsWith('M'))
        if (-not $letterOk) { continue }
        $numOk = ($tagCode -in @('K','P','E')) -or ((Get-LoopNumber $lu) -eq (Get-LoopNumber $tagU))
        if (-not $numOk) { continue }
        $i2 = Select-BestIdx $t $t.exAll[$lu] $tagU
        return @{ i = $i2; t = $t; sposob = ('Наименование (вхождение)' + $suffix) }
      }
    }
  }
  # 5) Описание contains tag
  foreach ($t in $tblArr) {
    if (-not $isGasTag -and $t.short -like '*GDS*') { continue }
    for ($ii = 0; $ii -lt $t.n; $ii++) {
      if ($t.descsU[$ii].Contains($tagU)) {
        $lu = $t.loopU[$ii]
        $numOk = ($tagCode -in @('K','P','E')) -or ((Get-LoopNumber $lu) -eq (Get-LoopNumber $tagU))
        if (-not $numOk) { continue }
        $i2 = Select-BestIdx $t $t.exAll[$lu] $tagU
        return @{ i = $i2; t = $t; sposob = ('Описание (вхождение)' + $suffix) }
      }
    }
  }
  # 6) without system prefix and dashes (inter-controller signals)
  $core = $tagU -replace '^\d{4}-', ''
  if ($core -ne $tagU -and $core.Length -ge 4) {
    $coreNd = Nd $core
    foreach ($t in $tblArr) {
      if (-not $isGasTag -and $t.short -like '*GDS*') { continue }
      for ($ii = 0; $ii -lt $t.n; $ii++) {
        if ($t.loopNd[$ii].Contains($coreNd)) {
          $lu = $t.loopU[$ii]
          $i2 = Select-BestIdx $t $t.exAll[$lu] $tagU
          return @{ i = $i2; t = $t; sposob = ('Loop (вхождение без префикса и дефисов)' + $suffix) }
        }
      }
    }
  }
  return $null
}
function Find-Match([string]$tagRaw, $tblArr) {
  $clean = ([string]$tagRaw).Trim().ToUpperInvariant()
  if ($clean -eq '' -or $clean -eq ([char]0x2014)) { return $null }
  $list = New-Object 'System.Collections.Generic.List[object]'
  $seen = @{}
  Add-Cand $list $seen $clean ''
  if ($clean.Contains('/')) { foreach ($part in ($clean -split '/')) { Add-Cand $list $seen $part '' } }
  if ($clean.Contains(' ')) { foreach ($part in ($clean -split ' ')) { Add-Cand $list $seen $part '' } }
  # gas detector: G-code ending with T -> T replaced by IA (GET->GEIA, GBT->GBIA, GMCT->GMCIA, GGUT->GGUIA)
  foreach ($c in @($list)) { if ($c.s -match '^(\d{4}-G[A-Z]+)T(-.*)$') { Add-Cand $list $seen ($Matches[1] + 'IA' + $Matches[2]) ' [преобразование T->IA]' } }
  # pump motor: P -> PM (1120-P-003A = 1120-PM-003A, 2650-P-1001B = 2650-PM-1001B)
  foreach ($c in @($list)) { if ($c.s -match '^(\d{4}-)P(-\d.*$)$') { Add-Cand $list $seen ($Matches[1] + 'PM' + $Matches[2]) ' [преобразование P->PM]' } }
  # compressor/heater motor: K -> KM, E -> EM (1110-K-101A = 1110-KM-101A, 1140-E-414 = 1140-EM-414)
  foreach ($c in @($list)) { if ($c.s -match '^(\d{4}-)(K|E)(-\d.*$)$') { Add-Cand $list $seen ($Matches[1] + $Matches[2] + 'M' + $Matches[3]) (' [преобразование ' + $Matches[2] + '->' + $Matches[2] + 'M]') } }
  # reverse: KM -> K, PM -> P, EM -> E
  foreach ($c in @($list)) { if ($c.s -match '^(\d{4}-)(KM|PM|EM)(-.*)$') { Add-Cand $list $seen ($Matches[1] + $Matches[2].Substring(0, 1) + $Matches[3]) (' [преобразование ' + $Matches[2] + '->' + $Matches[2].Substring(0, 1) + ']') } }
  foreach ($cand in $list) { $res = Find-MatchOne $cand.s $cand.lbl $tblArr; if ($null -ne $res) { return $res } }
  return $null
}
function Resolve-ByName($m, [string]$tagU, $tblArr) {
  $row = $m.t.rows[$m.i]
  $text = (([string]$row.name) + ' ' + ([string]$row.desc)).ToUpperInvariant()
  $ms = [regex]::Matches($text, '\d{4}-[A-Z]+(?:-[A-Z0-9]+)*')
  if ($ms.Count -eq 0) { return $m }
  $rowLoopU = ([string]$row.loop).Trim().ToUpperInvariant()
  $hits = @(); $seenR = @{}
  foreach ($mm in $ms) {
    $cand = $mm.Value.Trim()
    if ($cand -eq '' -or $seenR.ContainsKey($cand)) { continue }
    $seenR[$cand] = $true
    if ($cand -eq $rowLoopU) { continue }
    if ($cand -eq $tagU) { continue }
    foreach ($t in $tblArr) {
      if ($t.exAll.ContainsKey($cand)) {
        $candCode = Get-FuncCode $cand
        $numOk = ($candCode -in @('K','P','E')) -or ((Get-LoopNumber $cand) -eq (Get-LoopNumber $tagU))
        if (-not $numOk) { continue }
        $i2 = Select-BestIdx $t $t.exAll[$cand] $tagU
        $hits += @{ i = $i2; t = $t; cand = $cand }
        break
      }
    }
  }
  if ($hits.Count -eq 0) { return $m }
  $best = $null; $bestLen = -1; $bestSim = -1
  foreach ($h in $hits) {
    $len = $h.cand.Length
    $sim = CommonPrefixLen (Nd ($h.cand -replace '^\d{4}-', '')) (Nd ($tagU -replace '^\d{4}-', ''))
    if ($len -gt $bestLen -or ($len -eq $bestLen -and $sim -gt $bestSim)) { $best = $h; $bestLen = $len; $bestSim = $sim }
  }
  return @{ i = $best.i; t = $best.t; sposob = ($m.sposob + ' [Loop из Наименования: ' + $best.cand + ']') }
}
$matchMap = @{}
foreach ($i in $items) {
  $sysU = ([string]$i.sysTag).Trim().ToUpperInvariant()
  $isDCSOnly = ($sysU -match '^\d{4}-I-')
  $tblActive = if ($isDCSOnly) { @($tbl | Where-Object { $_.short -like '*DCS*' }) } else { $tbl.ToArray() }
  $causeTag = ([string]$i.tagEN).Trim().ToUpperInvariant()
  if ($causeTag -ne '' -and $causeTag -ne ([char]0x2014)) {
    $key = $causeTag + '||' + $sysU
    $m = $null
    foreach ($t in $tblActive) { if ($t.exAll.ContainsKey($causeTag)) { $m = @{ i=[int]($t.exAll[$causeTag][0]); t=$t; sposob='Loop (точное совпадение)' }; break } }
    if ($null -eq $m) {
      $tc = Get-FuncCode $causeTag
      foreach ($t in $tblActive) {
        if (-not $isDCSOnly -and $t.short -like '*GDS*' -and $tc -notmatch '^G') { continue }
        for ($ii = 0; $ii -lt $t.n; $ii++) {
          if ($t.namesU[$ii].Contains($causeTag)) {
            $lu = $t.loopU[$ii]; $lc = Get-FuncCode $lu
            if (($lc.Substring(0,1) -eq $tc.Substring(0,1)) -or (($tc -in @('K','P','E')) -and $lc.StartsWith('M'))) { $m = @{ i=$ii; t=$t; sposob='Наименование (вхождение)' }; break }
          }
        }
        if ($null -ne $m) { break }
      }
    }
    if ($null -eq $m -and $i.descEN) {
      $dw = New-Object 'System.Collections.Generic.HashSet[string]'
      $dt = ([string]$i.descEN).ToLowerInvariant() -replace '\d{4}-[a-z0-9]+(?:-[a-z0-9]+)*',' ' -replace '[^a-z0-9\-]+',' '
      foreach ($w in ($dt -split '\s+')) { $w = $w.Trim('-'); if ($w.Length -ge 2 -and $w -notin @('the','and','of','in','on','at','for','with','from','by','or','an')) { [void]$dw.Add($w) } }
      if ($dw.Count -ge 3) {
        $bR = 0.0; $bT = $null; $bI = -1
        foreach ($t in $tblActive) { for ($ii = 0; $ii -lt $t.n; $ii++) { if (-not $t.loopU[$ii].StartsWith($causeTag.Substring(0,4))) { continue }; $cd = $t.ws[$ii]; if ($null -eq $cd -or $cd.Count -eq 0) { continue }; $itr = 0; foreach ($w in $dw) { if ($cd.Contains($w)) { $itr++ } }; $mx = [Math]::Max($dw.Count, $cd.Count); if ($mx -gt 0) { $rr = $itr / $mx; if ($itr -ge 3 -and $rr -gt $bR) { $bR = $rr; $bT = $t; $bI = $ii } } } }
        if ($null -ne $bT -and $bR -ge 0.6) { $pct = [int][Math]::Round($bR * 100); $m = @{ i=$bI; t=$bT; sposob=('Сходство описания (' + $pct + '%)') } }
      }
    }
    if ($null -ne $m) { $row = $m.t.rows[$m.i]; $matchMap[$key] = [ordered]@{ loop=$row.loop; typno=$row.typno; marka=$row.marka; name=$row.name; tipo=$row.tipo; shab=$row.shab; ctrl=$row.ctrl; file=$m.t.short; sposob=$m.sposob } } else { $matchMap[$key] = $null }
  }
  foreach ($e in $i.effects) {
    $effTag = ([string]$e.tag).Trim().ToUpperInvariant()
    if ($effTag -eq '' -or $effTag -eq ([char]0x2014)) { continue }
    $key = $effTag + '||' + $sysU
    if ($matchMap.ContainsKey($key)) { continue }
    $m = $null
    foreach ($t in $tblActive) { if ($t.exAll.ContainsKey($effTag)) { $m = @{ i=[int]($t.exAll[$effTag][0]); t=$t; sposob='Loop (точное совпадение)' }; break } }
    if ($null -ne $m) { $row = $m.t.rows[$m.i]; $matchMap[$key] = [ordered]@{ loop=$row.loop; typno=$row.typno; marka=$row.marka; name=$row.name; tipo=$row.tipo; shab=$row.shab; ctrl=$row.ctrl; file=$m.t.short; sposob=$m.sposob } } else { $matchMap[$key] = $null }
  }
}
$fc = 0; foreach ($v in $matchMap.Values) { if ($null -ne $v) { $fc++ } }
Write-Output ("matchMap: " + $matchMap.Count + " found: " + $fc)
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
$mx=0;$mi=0;$mp=0;$crossCount=0; foreach ($i in $items) { $crossCount += @($i.effects).Count; foreach ($e in $i.effects) { switch ([string]$e.mark) { 'X'{$mx++} 'I'{$mi++} 'P'{$mp++} } } }
[void]$r5.Add(@('Источник','CAE_OSBL.docx (OSBL)')); [void]$r5.Add(@('Систем блокировок',[string]$systems.Count)); [void]$r5.Add(@('Блокировок — причин',[string]$items.Count)); [void]$r5.Add(@('Пересечений всего',[string]$crossCount)); [void]$r5.Add(@('   X',[string]$mx)); [void]$r5.Add(@('   I',[string]$mi)); [void]$r5.Add(@('   P',[string]$mp)); [void]$r5.Add(@('Теги найдено',[string]$fc + ' из ' + $matchMap.Count))
$outPath = 'C:\VSCode\CAE\CAE_OSBL_Перечень блокировок.xlsx'
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
function Write-Sheet($wb, [string]$name, [string[]]$hdrs, [object[]]$dataRows) {
  $ws = $wb.Worksheets.Add([Type]::Missing, $wb.Worksheets.Item($wb.Worksheets.Count))
  $ws.Name = $name
  $n = $dataRows.Count + 1; $c = $hdrs.Count
  $arr = [Array]::CreateInstance([object], [int[]]@($n, $c), [int[]]@(1, 1))
  for ($j = 0; $j -lt $c; $j++) { $arr.SetValue([string]$hdrs[$j], 1, ($j+1)) }
  for ($i = 0; $i -lt $dataRows.Count; $i++) { for ($j = 0; $j -lt $c; $j++) { $arr.SetValue($dataRows[$i][$j], ($i+2), ($j+1)) } }
  $rng = $ws.Range($ws.Cells(1, 1), $ws.Cells($n, $c)); $rng.Value2 = $arr
  $rng.HorizontalAlignment = -4131; $rng.VerticalAlignment = -4160
  $hdr = $ws.Range($ws.Cells(1, 1), $ws.Cells(1, $c)); $hdr.Font.Bold = $true; $hdr.Interior.Color = 15123099; $hdr.HorizontalAlignment = -4108
  $rng.Borders.LineStyle = 1; $rng.Borders.Weight = 2
  $ws.Rows.Item(1).RowHeight = 30
  foreach ($wc in @(4,5,6,8,9,10,11,12)) { $ws.Columns.Item($wc).WrapText = $true }
  $ws.Columns.AutoFit() | Out-Null
  $ws.Activate(); $excel.ActiveWindow.SplitRow = 1; $excel.ActiveWindow.FreezePanes = $true
  $ws.Range($ws.Cells(1, 1), $ws.Cells(1, $c)).AutoFilter() | Out-Null
}
try {
  $wb = $excel.Workbooks.Add()
  while ($wb.Worksheets.Count -gt 1) { $wb.Worksheets.Item($wb.Worksheets.Count).Delete() }
  $r1Arr = $r1; $r2Arr = $r2; $r3Arr = $r3; $r4Arr = $r4; $r5Arr = $r5
  $h1Arr = $h1; $h2Arr = $h2; $h3Arr = $h3; $h4Arr = $h4; $h5Arr = $h5
  Write-Sheet $wb 'Перечень блокировок' $h1Arr $r1Arr
  Write-Sheet $wb 'Причины и следствия' $h2Arr $r2Arr
  Write-Sheet $wb 'Реестр систем' $h3Arr $r3Arr
  Write-Sheet $wb 'Теги причин и следствий' $h4Arr $r4Arr
  Write-Sheet $wb 'Сводка' $h5Arr $r5Arr
  foreach ($ws2 in @($wb.Worksheets)) { if ($ws2.Name -eq 'Лист1' -or $ws2.Name -eq 'Sheet1') { $ws2.Delete() } }
  if (Test-Path $outPath) { Remove-Item -LiteralPath $outPath -Force }
  $wb.SaveAs($outPath, 51); $wb.Close($false)
  Write-Output ("SAVED -> " + $outPath)
} finally { $excel.Quit(); [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null }
Write-Output 'OSBL PIPELINE DONE'
