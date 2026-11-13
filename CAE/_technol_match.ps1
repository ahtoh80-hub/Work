# Match tags from the CAE elements tab against ТЕХНОЛ_ОБЪЕКТЫ files (primary key: Loop).
# - within one Loop the row with best KKS match to the searched tag is selected (for Марка)
# - gas detector tags: functional code trailing 'T' is also tried as 'IA' (GET->GEIA, GBT->GBIA, GMCT->GMCIA)
# Reads xlsx directly via ZipArchive/XML (no Excel COM).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$prepPath = Join-Path $env:TEMP 'cae_prep.json'
$outPath = Join-Path $env:TEMP 'cae_match.json'
$p = Get-Content $prepPath -Raw -Encoding UTF8 | ConvertFrom-Json
$r5 = @($p.r5)
# column indices resolved dynamically from headers (layout may change)
$h5 = @($p.h5)
$ixTag = -1; $ixPair = -1; $ixSys = -1; $ixDescCause = -1; $ixDescEffect = -1; $ixType = -1
for ($i = 0; $i -lt $h5.Count; $i++) {
  if ($h5[$i] -eq 'Тег') { $ixTag = $i }
  if ($h5[$i] -eq 'Парный тег') { $ixPair = $i }
  if ($h5[$i] -eq 'Система — тег') { $ixSys = $i }
  if ($h5[$i] -eq 'Причина — описание EN') { $ixDescCause = $i }
  if ($h5[$i] -eq 'Следствие — описание EN') { $ixDescEffect = $i }
  if ($h5[$i] -eq 'Тип тега') { $ixType = $i }
}
if ($ixTag -lt 0 -or $ixPair -lt 0 -or $ixSys -lt 0 -or $ixDescCause -lt 0 -or $ixDescEffect -lt 0 -or $ixType -lt 0) { throw 'required columns not found in prep h5' }

function Clean-Key([string]$s) {
  $t = ([string]$s).Trim().ToUpperInvariant()
  $t = $t -replace '\s+', ' '
  return $t
}
function Base-Of([string]$tagU) {
  $b = $tagU -replace '-\d{1,2}$', ''
  return $b
}
function Nd([string]$s) {
  return (([string]$s).ToUpperInvariant() -replace '-', '')
}
# functional code of a KKS tag: letters between the system prefix and the loop number (K, KM, MXI, PZIALL...)
function Get-FuncCode([string]$kksU) {
  $s = ([string]$kksU) -replace '^\d{4}-', ''
  $dash = $s.IndexOf('-')
  if ($dash -gt 0) { return $s.Substring(0, $dash) }
  return $s
}
# numeric part of a KKS tag: digits of the loop number (leading zeros ignored)
function Get-LoopNumber([string]$kksU) {
  $s = ([string]$kksU) -replace '^\d{4}-', ''
  if ($s.IndexOf('-') -gt 0) { $s = $s.Substring($s.IndexOf('-') + 1) }
  if ($s.IndexOf('-') -ge 0) { $s = $s.Substring(0, $s.IndexOf('-')) }
  $d = $s -replace '\D', ''
  $d = $d.TrimStart('0')
  if ($d -eq '') { $d = '0' }
  return $d
}
# word set of a text for description similarity (KKS tokens and stopwords removed)
function Get-Words([string]$text) {
  $t = ([string]$text).ToLowerInvariant()
  $t = $t -replace '\d{4}-[a-z0-9]+(?:-[a-z0-9]+)*', ' '
  $t = $t -replace '[^a-z0-9\-]+', ' '
  $ws = New-Object 'System.Collections.Generic.HashSet[string]'
  foreach ($w in ($t -split '\s+')) {
    $w = $w.Trim('-')
    if ($w.Length -lt 2) { continue }
    if ($w -in @('the','and','of','in','on','at','for','with','from','by','or','an','is','are')) { continue }
    [void]$ws.Add($w)
  }
  return $ws
}
# longest common prefix length of two strings
function CommonPrefixLen([string]$a, [string]$b) {
  $la = $a.Length; $lb = $b.Length
  $lim = [Math]::Min($la, $lb)
  $i = 0
  while ($i -lt $lim -and $a[$i] -eq $b[$i]) { $i++ }
  return $i
}

# ---- collect unique tag/system pairs (+ EN descriptions for similarity matching) ----
# effect tags are matched by the effect description, cause/input tags - by the cause description
$pairKeys = @{}
$pairDescCause = @{}
$pairDescEffect = @{}
foreach ($row in $r5) {
  $sys = ([string]$row[$ixSys]).Trim().ToUpperInvariant()
  $type = ([string]$row[$ixType]).Trim().ToUpperInvariant()
  $dc = ([string]$row[$ixDescCause]).Trim()
  $de = ([string]$row[$ixDescEffect]).Trim()
  $isEffect = ($type -eq 'СЛЕДСТВИЕ')
  foreach ($ix in @($ixTag, $ixPair)) {
    $k = ([string]$row[$ix]).Trim().ToUpperInvariant()
    if ($k -eq '' -or $k -eq '—') { continue }
    $pairKeys[$k + '||' + $sys] = $true
    $mapRef = if ($isEffect) { $pairDescEffect } else { $pairDescCause }
    $desc = if ($isEffect) { $de } else { $dc }
    if ($desc -ne '') {
      if (-not $mapRef.ContainsKey($k + '||' + $sys)) { $mapRef[$k + '||' + $sys] = New-Object 'System.Collections.Generic.List[string]' }
      $mapRef[$k + '||' + $sys].Add($desc)
    }
  }
}
$pairList = @($pairKeys.Keys)
Write-Output ("unique tag/system pairs: " + $pairList.Count)

# ---- read ТЕХНОЛ_ОБЪЕКТЫ xlsx via zip/xml ----
function Read-XlsxColumnar([string]$path) {
  $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
  try {
    $ss = @()
    $ssEntry = $zip.GetEntry('xl/sharedStrings.xml')
    if ($null -ne $ssEntry) {
      $sr = New-Object System.IO.StreamReader($ssEntry.Open(), [System.Text.Encoding]::UTF8)
      $xml = [xml]$sr.ReadToEnd()
      $sr.Close()
      $ss = @($xml.sst.si | ForEach-Object { $_.InnerText })
    }
    $wsEntry = $zip.GetEntry('xl/worksheets/sheet1.xml')
    $sr2 = New-Object System.IO.StreamReader($wsEntry.Open(), [System.Text.Encoding]::UTF8)
    $wx = [xml]$sr2.ReadToEnd()
    $sr2.Close()
    $rowsOut = New-Object 'System.Collections.Generic.List[object]'
    foreach ($row in $wx.worksheet.sheetData.row) {
      $rn = [int]$row.r
      if ($rn -le 1) { continue }
      $cells = @{}
      $expectedCol = 1
      foreach ($c in $row.c) {
        $ref = [string]$c.r
        if ($ref -ne '') {
          $letters = ($ref -replace '\d', '')
          $colIdx = 0
          foreach ($ch in $letters.ToCharArray()) { $colIdx = $colIdx * 26 + ([int]$ch - 64) }
        } else {
          $colIdx = $expectedCol
        }
        $expectedCol = $colIdx + 1
        $t = [string]$c.t
        $val = ''
        if ($t -eq 's') { $val = $ss[[int]$c.v] }
        elseif ($t -eq 'inlineStr') { $val = [string]$c.is.InnerText }
        else { $val = [string]$c.v }
        $cells[$colIdx] = $val
      }
      $g = { param($ix) if ($cells.ContainsKey($ix)) { ([string]$cells[$ix]).Trim() } else { '' } }
      $rowsOut.Add([pscustomobject]@{
        loop = (& $g 2); kks = (& $g 3); typno = (& $g 4); marka = (& $g 6); tipo = (& $g 7)
        name = (& $g 8); desc = (& $g 9); ctrl = (& $g 19); shab = (& $g 22)
      }) | Out-Null
    }
    return $rowsOut
  } finally {
    $zip.Dispose()
  }
}

$fileDefs = @(
  @{ file = 'C:\VSCode\CAE\EBSM_SCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'; short = 'EBSM_SCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx' },
  @{ file = 'C:\VSCode\CAE\EBSM_DCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'; short = 'EBSM_DCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx' },
  @{ file = 'C:\VSCode\CAE\EBSM_GDS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'; short = 'EBSM_GDS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx' }
)
$tbl = New-Object 'System.Collections.Generic.List[object]'
foreach ($fd in $fileDefs) {
  $rowsOut = Read-XlsxColumnar $fd.file
  $n = $rowsOut.Count
  $loopU = New-Object string[] $n
  $loopNd = New-Object string[] $n
  $namesU = New-Object string[] $n
  $descsU = New-Object string[] $n
  $wordSets = New-Object object[] $n
  $exAll = @{}
  $exBaseAll = @{}
  for ($i = 0; $i -lt $n; $i++) {
    $r = $rowsOut[$i]
    $loopU[$i] = $r.loop.ToUpperInvariant()
    $loopNd[$i] = $loopU[$i] -replace '-', ''
    $namesU[$i] = $r.name.ToUpperInvariant()
    $descsU[$i] = $r.desc.ToUpperInvariant()
    $wordSets[$i] = Get-Words ($r.name + ' ' + $r.desc)
    $k = $loopU[$i]
    if ($k -ne '') {
      if (-not $exAll.ContainsKey($k)) { $exAll[$k] = New-Object 'System.Collections.Generic.List[int]' }
      $exAll[$k].Add($i)
      $kb = Base-Of $k
      if (-not $exBaseAll.ContainsKey($kb)) { $exBaseAll[$kb] = New-Object 'System.Collections.Generic.List[int]' }
      $exBaseAll[$kb].Add($i)
    }
  }
  $tbl.Add([pscustomobject]@{
    short = $fd.short; n = $n; rows = $rowsOut
    loopU = $loopU; namesU = $namesU; descsU = $descsU
    loopNd = $loopNd
    ws = $wordSets
    exAll = $exAll; exBaseAll = $exBaseAll
  }) | Out-Null
  Write-Output ($fd.short + ' rows=' + $n)
}
# ---- selection of the best row within one Loop: max KKS match to the searched tag ----
function Select-BestIdx($t, $idxList, [string]$tagU) {
  # reference: tag without system prefix and dashes (KKS-like core)
  $refCore = Nd ($tagU -replace '^\d{4}-', '')
  $bestIdx = [int]$idxList[0]
  $bestS1 = -1; $bestS2 = -999999
  foreach ($i in $idxList) {
    $kksNd = Nd $t.rows[$i].kks
    $s1 = CommonPrefixLen $kksNd $refCore
    $s2 = -[Math]::Abs($kksNd.Length - $refCore.Length)
    if ($s1 -gt $bestS1 -or ($s1 -eq $bestS1 -and $s2 -gt $bestS2)) {
      $bestS1 = $s1; $bestS2 = $s2; $bestIdx = $i
    }
  }
  return $bestIdx
}

# ---- match functions ----
function Find-MatchOne([string]$tagU, [string]$trLabel) {
  $suffix = $trLabel
  # 1) exact Loop
  foreach ($t in $tblActive) {
    if ($t.exAll.ContainsKey($tagU)) {
      $i = Select-BestIdx $t $t.exAll[$tagU] $tagU
      return @{ i = $i; t = $t; sposob = ('Loop (точное совпадение)' + $suffix) }
    }
  }
  # 2) Loop без суффикса
  $baseTag = Base-Of $tagU
  $tagCode = Get-FuncCode $tagU
  foreach ($t in $tblActive) {
    if ($t.exBaseAll.ContainsKey($baseTag)) {
      $i = Select-BestIdx $t $t.exBaseAll[$baseTag] $tagU
      $lu2 = $t.loopU[$i]
      # numeric part must match (exemption: motors/compressors K/P)
      $numOk = ($tagCode -eq 'K' -or $tagCode -eq 'P') -or ((Get-LoopNumber $lu2) -eq (Get-LoopNumber $tagU))
      if (-not $numOk) { continue }
      $sposob = if ($baseTag -eq $tagU) { 'Loop (точное совпадение)' } else { 'Loop (совпадение без суффикса)' }
      return @{ i = $i; t = $t; sposob = ($sposob + $suffix) }
    }
  }
  # 3) Loop contains tag
  $isGasTag = $tagU -match '^\d{4}-G'
  foreach ($t in $tblActive) {
    if (-not $isGasTag -and $t.short -like '*GDS*') { continue }
    for ($i = 0; $i -lt $t.n; $i++) {
      if ($t.loopU[$i].Contains($tagU)) {
        # numeric part must match (implied by containment; defense)
        $numOk = ($tagCode -eq 'K' -or $tagCode -eq 'P') -or ((Get-LoopNumber $t.loopU[$i]) -eq (Get-LoopNumber $tagU))
        if (-not $numOk) { continue }
        $lu = $t.loopU[$i]
        $i2 = Select-BestIdx $t $t.exAll[$lu] $tagU
        return @{ i = $i2; t = $t; sposob = ('Loop (вхождение)' + $suffix) }
      }
    }
  }
  # 4) Наименование contains tag
  # первая буква функционального кода Loop обязана совпадать с кодом тега;
  # исключение - двигатели/компрессоры/насосы (теги K/P): их сигналы MXI/M... имеют другие буквы
  # (например, для следствия 1110-K-101A сигнал останова 1110-MXI-00101A)
  $tagCode = Get-FuncCode $tagU
  foreach ($t in $tblActive) {
    if (-not $isGasTag -and $t.short -like '*GDS*') { continue }
    for ($i = 0; $i -lt $t.n; $i++) {
      if ($t.namesU[$i].Contains($tagU)) {
        $lu = $t.loopU[$i]
        $loopCode = Get-FuncCode $lu
        $letterOk = ($loopCode.Substring(0, 1) -eq $tagCode.Substring(0, 1)) -or (($tagCode -eq 'K' -or $tagCode -eq 'P') -and $loopCode.StartsWith('M'))
        if (-not $letterOk) { continue }
        # numeric part must match (exemption: motors/compressors K/P - complex rule incl. Наименование)
        $numOk = ($tagCode -eq 'K' -or $tagCode -eq 'P') -or ((Get-LoopNumber $lu) -eq (Get-LoopNumber $tagU))
        if (-not $numOk) { continue }
        $i2 = Select-BestIdx $t $t.exAll[$lu] $tagU
        return @{ i = $i2; t = $t; sposob = ('Наименование (вхождение)' + $suffix) }
      }
    }
  }
  # 5) Описание contains tag
  foreach ($t in $tblActive) {
    if (-not $isGasTag -and $t.short -like '*GDS*') { continue }
    for ($i = 0; $i -lt $t.n; $i++) {
      if ($t.descsU[$i].Contains($tagU)) {
        $lu = $t.loopU[$i]
        # numeric part must match (exemption: motors/compressors K/P)
        $numOk = ($tagCode -eq 'K' -or $tagCode -eq 'P') -or ((Get-LoopNumber $lu) -eq (Get-LoopNumber $tagU))
        if (-not $numOk) { continue }
        $i2 = Select-BestIdx $t $t.exAll[$lu] $tagU
        return @{ i = $i2; t = $t; sposob = ('Описание (вхождение)' + $suffix) }
      }
    }
  }
  # 6) совпадение без префикса системы и без дефисов (межконтроллерные сигналы: 1130-Z-3001 -> 1131-Z-3001DO)
  $core = $tagU -replace '^\d{4}-', ''
  if ($core -ne $tagU -and $core.Length -ge 4) {
    $coreNd = Nd $core
    foreach ($t in $tblActive) {
      if (-not $isGasTag -and $t.short -like '*GDS*') { continue }
      for ($i = 0; $i -lt $t.n; $i++) {
        if ($t.loopNd[$i].Contains($coreNd)) {
          $lu = $t.loopU[$i]
          $i2 = Select-BestIdx $t $t.exAll[$lu] $tagU
          return @{ i = $i2; t = $t; sposob = ('Loop (вхождение без префикса и дефисов)' + $suffix) }
        }
      }
    }
  }
  return $null
}

function Find-Match([string]$tagRaw) {
  $clean = ([string]$tagRaw).Trim().ToUpperInvariant()
  if ($clean -eq '' -or $clean -eq '—') { return $null }
  # candidates: whole cleaned string, parts split by '/', gas-detector variant T->IA, pump-motor variant P->PM
  $cands = @(@{ s = $clean; lbl = '' })
  if ($clean.Contains('/')) {
    foreach ($part in ($clean -split '/')) {
      $pp = $part.Trim()
      if ($pp -ne '' -and $pp -ne $clean) { $cands += @{ s = $pp; lbl = '' } }
    }
  }
  # gas detector: functional code of a G-tag ending with T -> replace T with IA (GET->GEIA, GBT->GBIA, GMCT->GMCIA)
  foreach ($c in @($cands)) {
    if ($c.s -match '^(\d{4}-G[A-Z]+)T(-.*)$') {
      $variant = ($Matches[1] + 'IA' + $Matches[2])
      $exists = $false
      foreach ($cc in $cands) { if ($cc.s -eq $variant) { $exists = $true; break } }
      if (-not $exists) { $cands += @{ s = $variant; lbl = ' [преобразование T->IA]' } }
    }
  }
  # pump motor: functional code P -> PM (1120-P-003A = 1120-PM-003A)
  foreach ($c in @($cands)) {
    if ($c.s -match '^(\d{4}-)P(-\d.*$)$') {
      $variant = ($Matches[1] + 'PM' + $Matches[2])
      $exists = $false
      foreach ($cc in $cands) { if ($cc.s -eq $variant) { $exists = $true; break } }
      if (-not $exists) { $cands += @{ s = $variant; lbl = ' [преобразование P->PM]' } }
    }
  }
  # compressor/heater/evaporator motor: K -> KM and E -> EM (1110-K-101A = 1110-KM-101A, 1140-E-414 = 1140-EM-414)
  foreach ($c in @($cands)) {
    if ($c.s -match '^(\d{4}-)(K|E)(-\d.*$)$') {
      $variant = ($Matches[1] + $Matches[2] + 'M' + $Matches[3])
      $exists = $false
      foreach ($cc in $cands) { if ($cc.s -eq $variant) { $exists = $true; break } }
      if (-not $exists) { $cands += @{ s = $variant; lbl = ' [преобразование ' + $Matches[2] + '->' + $Matches[2] + 'M]' } }
    }
  }
  # reverse: KM -> K, PM -> P, EM -> E (1110-KM-101A = 1110-K-101A, 1140-EM-414 = 1140-E-414)
  foreach ($c in @($cands)) {
    if ($c.s -match '^(\d{4}-)(KM|PM|EM)(-.*)$') {
      $variant = ($Matches[1] + $Matches[2].Substring(0, 1) + $Matches[3])
      $exists = $false
      foreach ($cc in $cands) { if ($cc.s -eq $variant) { $exists = $true; break } }
      if (-not $exists) { $cands += @{ s = $variant; lbl = ' [преобразование ' + $Matches[2] + '->' + $Matches[2].Substring(0, 1) + ']' } }
    }
  }
  foreach ($cand in $cands) {
    $res = Find-MatchOne $cand.s $cand.lbl
    if ($null -ne $res) { return $res }
  }
  return $null
}

# ---- resolution of the object Loop from Наименование/Описание of the found row ----
# e.g. row Loop=1110-MXI-00101A, Наименование='1110-K-101A main driver motor(1110-KM-101A-1):Stop'
#      -> object loop 1110-KM-101A-1 (exists in DCS) becomes the resulting Loop
function Resolve-ByName($m, [string]$tagU) {
  $row = $m.t.rows[$m.i]
  $text = (([string]$row.name) + ' ' + ([string]$row.desc)).ToUpperInvariant()
  $ms = [regex]::Matches($text, '\d{4}-[A-Z]+(?:-[A-Z0-9]+)*')
  if ($ms.Count -eq 0) { return $m }
  $rowLoopU = ([string]$row.loop).Trim().ToUpperInvariant()
  $hits = @()
  $seen = @{}
  foreach ($mm in $ms) {
    $cand = $mm.Value.Trim()
    if ($cand -eq '' -or $seen.ContainsKey($cand)) { continue }
    $seen[$cand] = $true
    if ($cand -eq $rowLoopU) { continue }          # the row's own loop - keep as is
    if ($cand -eq $tagU) { continue }              # the searched tag itself
    foreach ($t in $tblActive) {
      if ($t.exAll.ContainsKey($cand)) {
        # numeric part must match unless the searched tag is a motor/compressor (K/P - complex rule)
        $candCode = Get-FuncCode $cand
        $numOk = ($candCode -eq 'K' -or $candCode -eq 'P') -or ((Get-LoopNumber $cand) -eq (Get-LoopNumber $tagU))
        if (-not $numOk) { continue }
        $i = Select-BestIdx $t $t.exAll[$cand] $tagU
        $hits += @{ i = $i; t = $t; cand = $cand }
        break
      }
    }
  }
  if ($hits.Count -eq 0) { return $m }
  # prefer the longest candidate, then the most similar KKS to the searched tag
  $best = $null; $bestLen = -1; $bestSim = -1
  foreach ($h in $hits) {
    $len = $h.cand.Length
    $sim = CommonPrefixLen (Nd ($h.cand -replace '^\d{4}-', '')) (Nd ($tagU -replace '^\d{4}-', ''))
    if ($len -gt $bestLen -or ($len -eq $bestLen -and $sim -gt $bestSim)) {
      $best = $h; $bestLen = $len; $bestSim = $sim
    }
  }
  return @{ i = $best.i; t = $best.t; sposob = ($m.sposob + ' [Loop из Наименования: ' + $best.cand + ']') }
}

# ---- contour search: the technological object itself (not its signals) ----
# only exact/base Loop match of the tag / its transformed variants (T->IA, P<->PM, K<->KM), then base match
function Find-Contour([string]$tagRaw, [string]$extraCand) {
  $clean = ([string]$tagRaw).Trim().ToUpperInvariant()
  if ($clean -eq '' -or $clean -eq '—') { return $null }
  $cands = @($clean)
  if ($clean -match '^(\d{4}-G[A-Z]+)T(-.*)$') { $cands += ($Matches[1] + 'IA' + $Matches[2]) }
  if ($clean -match '^(\d{4}-)P(-\d.*$)$') { $cands += ($Matches[1] + 'PM' + $Matches[2]) }
  if ($clean -match '^(\d{4}-)(K|E)(-\d.*$)$') { $cands += ($Matches[1] + $Matches[2] + 'M' + $Matches[3]) }
  if ($clean -match '^(\d{4}-)(KM|PM|EM)(-.*)$') { $cands += ($Matches[1] + $Matches[2].Substring(0, 1) + $Matches[3]) }
  if ($clean -match '^(\d{4}-)(KM|PM)(-.*)$') { $cands += ($Matches[1] + $Matches[2].Substring(0, 1) + $Matches[3]) }
  $ex = ([string]$extraCand).Trim().ToUpperInvariant()
  if ($ex -ne '') { $cands += $ex }
  $cands = @($cands | Select-Object -Unique)
  foreach ($cand in $cands) {
    foreach ($t in $tblActive) {
      if ($t.exAll.ContainsKey($cand)) {
        $i = Select-BestIdx $t $t.exAll[$cand] $cand
        return @{ i = $i; t = $t }
      }
    }
  }
  foreach ($cand in $cands) {
    $baseTag = Base-Of $cand
    foreach ($t in $tblActive) {
      if ($t.exBaseAll.ContainsKey($baseTag)) {
        $i = Select-BestIdx $t $t.exBaseAll[$baseTag] $cand
        return @{ i = $i; t = $t }
      }
    }
  }
  return $null
}

# ---- match all unique tags ----
$matchMap = @{}
$statSposob = @{}
$statFile = @{}
$found = 0
$simMatched = 0
$idx = 0
foreach ($key in $pairList) {
  $idx++
  $sep = $key.IndexOf('||')
  $tagU = $key.Substring(0, $sep)
  $sysU = $key.Substring($sep + 2)
  # scope restriction: tags of -I- interlocks (РСУ) are searched in EBSM_DCS only (no ESD/SCS, no GDS)
  $tblActive = if ($sysU -match '^\d{4}-I-') { @($tbl | Where-Object { $_.short -like '*DCS*' }) } else { $tbl.ToArray() }
  $m = Find-Match $tagU
  # SCS has top priority for Z-blockings: a match found in EBSM_SCS is kept as is;
  # Loop resolution from Наименование applies only to matches found in EBSM_DCS/GDS
  if ($null -ne $m -and ($m.t.short -notlike '*SCS*')) { $m = Resolve-ByName $m $tagU }
  $extra = if ($null -ne $m) { ([string]$m.t.rows[$m.i].loop).Trim().ToUpperInvariant() } else { '' }
  $mc = Find-Contour $tagU $extra
  # for Z-blockings: if match is from DCS but SCS has a Наименование match -> swap
  # Loop = SCS signal, Контур = DCS object
  if ($null -ne $m -and ($m.t.short -like '*DCS*') -and ($sysU -notmatch '^\d{4}-I-')) {
    $dcsIdx = $m.i; $dcsT = $m.t; $dcsSposob = $m.sposob
    $mDCS = $m
    # try to find in SCS via Наименование (original tag and M-variants for K/E/P)
    $searchTags = @($tagU)
    if ($tagU -match '^(\d{4}-)(K|E|P)(-\d.*$)$') {
      $searchTags += ($Matches[1] + $Matches[2] + 'M' + $Matches[3])
    }
    $mSCS = $null
    foreach ($st in $searchTags) {
      if ($null -ne $mSCS) { break }
      foreach ($t2 in $tbl) {
        if (-not ($t2.short -like '*SCS*')) { continue }
        for ($i2 = 0; $i2 -lt $t2.n; $i2++) {
          if ($t2.namesU[$i2].Contains($st)) {
            $lu2 = $t2.loopU[$i2]
            $iBest = Select-BestIdx $t2 $t2.exAll[$lu2] $tagU
            $mSCS = @{ i = $iBest; t = $t2; sposob = ('Наименование (вхождение) [приоритет SCS]') }
            break
          }
        }
        if ($null -ne $mSCS) { break }
      }
    }
    if ($null -ne $mSCS) {
      # SCS Наименование match found -> use it for Loop, keep DCS for Контур
      $m = $mSCS
      $mc = @{ i = $mDCS.i; t = $mDCS.t }
    }
  }
  if ($null -eq $m) {
    $matchMap[$key] = $null
    $statSposob['НЕ НАЙДЕНО'] = [int]$statSposob['НЕ НАЙДЕНО'] + 1
  } else {
    $found++
    $r = $m.i
    $t = $m.t
    $row = $t.rows[$r]
    $cLoop = ''; $cTypno = ''; $cMarka = ''; $cCtrl = ''; $cFile = ''
    if ($null -ne $mc) {
      $crow = $mc.t.rows[$mc.i]
      $cLoop = $crow.loop; $cTypno = $crow.typno; $cMarka = $crow.marka; $cCtrl = $crow.ctrl; $cFile = $mc.t.short
    }
    $matchMap[$key] = [ordered]@{
      loop = $row.loop; typno = $row.typno; marka = $row.marka
      name = $row.name; tipo = $row.tipo; shab = $row.shab
      ctrl = $row.ctrl; file = $t.short; sposob = $m.sposob
      cLoop = $cLoop; cTypno = $crow.typno; cMarka = $cMarka; cCtrl = $cCtrl; cFile = $cFile
    }
    $statSposob[$m.sposob] = [int]$statSposob[$m.sposob] + 1
    $statFile[$t.short] = [int]$statFile[$t.short] + 1
  }
  if ($idx % 100 -eq 0) { Write-Output ("  matched " + $idx + "/" + $pairList.Count) }
}
# ---- second pass: unresolved pairs -> description similarity within the same system prefix ----
foreach ($key in $pairList) {
  if ($matchMap.ContainsKey($key) -and $null -ne $matchMap[$key]) { continue }
  $sep = $key.IndexOf('||')
  $tagU = $key.Substring(0, $sep)
  $sysU = $key.Substring($sep + 2)
  $descList = $null
  if ($pairDescEffect.ContainsKey($key)) { $descList = @($pairDescEffect[$key] | Where-Object { ($_ -ne $null) -and ($_ -ne '') }) }
  if (($null -eq $descList -or $descList.Count -eq 0) -and $pairDescCause.ContainsKey($key)) { $descList = @($pairDescCause[$key] | Where-Object { ($_ -ne $null) -and ($_ -ne '') }) }
  if ($null -eq $descList -or $descList.Count -eq 0) { continue }
  $tblActive = if ($sysU -match '^\d{4}-I-') { @($tbl | Where-Object { $_.short -like '*DCS*' }) } else { $tbl.ToArray() }
  if ($tagU.Length -lt 4) { continue }
  $pref = $tagU.Substring(0, 4)
  $isGasTag2 = $tagU -match '^\d{4}-G'
  $tagCode2 = Get-FuncCode $tagU
  $descSets = @()
  foreach ($d in $descList) {
    $w = Get-Words $d
    $descSets += @{ words = $w; cnt = $w.Count }
  }
  $bestRatio = 0.0; $bestCommon = 0; $bestT = $null; $bestI = -1
  foreach ($t in $tblActive) {
    if (-not $isGasTag2 -and $t.short -like '*GDS*') { continue }
    for ($i = 0; $i -lt $t.n; $i++) {
      if (-not $t.loopU[$i].StartsWith($pref)) { continue }
      # numeric part must match unless the searched tag is a motor/compressor (K/P - complex rule)
      $numOk = ($tagCode2 -eq 'K' -or $tagCode2 -eq 'P') -or ((Get-LoopNumber $t.loopU[$i]) -eq (Get-LoopNumber $tagU))
      if (-not $numOk) { continue }
      $cand = $t.ws[$i]
      if ($null -eq $cand -or $cand.Count -eq 0) { continue }
      foreach ($ds in $descSets) {
        if ($ds.cnt -eq 0) { continue }
        $inter = 0
        foreach ($w in $ds.words) { if ($cand.Contains($w)) { $inter++ } }
        $maxc = [Math]::Max($ds.cnt, $cand.Count)
        if ($maxc -eq 0) { continue }
        $ratio = $inter / $maxc
        if ($inter -ge 3 -and $ratio -gt $bestRatio) {
          $bestRatio = $ratio; $bestCommon = $inter; $bestT = $t; $bestI = $i
        }
      }
    }
  }
  if ($null -ne $bestT -and $bestRatio -ge 0.6) {
    $found++
    $simMatched++
    $row = $bestT.rows[$bestI]
    $pct = [int][Math]::Round($bestRatio * 100)
    $matchMap[$key] = [ordered]@{
      loop = $row.loop; typno = $row.typno; marka = $row.marka
      name = $row.name; tipo = $row.tipo; shab = $row.shab
      ctrl = $row.ctrl; file = $bestT.short; sposob = ('Сходство описания (' + $pct + '%)')
      cLoop = $row.loop; cTypno = $row.typno; cMarka = $row.marka; cCtrl = $row.ctrl; cFile = $bestT.short
    }
    $statSposob['Сходство описания (EN)'] = [int]$statSposob['Сходство описания (EN)'] + 1
    $statFile[$bestT.short] = [int]$statFile[$bestT.short] + 1
  }
}
Write-Output ("similarity matched: " + $simMatched)
Write-Output ("found: " + $found + " of " + $pairList.Count)
$contourFound = 0
foreach ($v in $matchMap.Values) { if ($null -ne $v -and ([string]$v.cLoop).Trim() -ne '') { $contourFound++ } }
Write-Output ("contours found: " + $contourFound)
$statSposob.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { Write-Output ('  sposob: ' + $_.Key + ' = ' + $_.Value) }
$statFile.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { Write-Output ('  file: ' + $_.Key + ' = ' + $_.Value) }
[System.IO.File]::WriteAllText($outPath, ($matchMap | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("MATCH -> " + $outPath)